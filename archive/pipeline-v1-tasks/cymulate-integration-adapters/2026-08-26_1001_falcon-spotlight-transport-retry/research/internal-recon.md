# Internal Recon

All paths are relative to the repo root `/Users/user/Dev/cymulate-integration-adapters`.
Adapter code lives under `src/Cymulate.Integration.Adapters/`; abbreviated below as `SRC/`.
Substrate source is the sibling repo `/Users/user/Dev/IntegrationInfra` (abbrev. `INFRA/`) — its
`Directory.Build.props:39` reads `1.2.0-preview.0`, which is exactly the version pinned at
`SRC/Directory.Packages.props:123`, so that source is authoritative for this task.
Transport source is the sibling repo `/Users/user/Dev/Cymulate.Http.Package` (abbrev. `HTTPPKG/`).

## Durable sources read

- `CLAUDE.md` — settles the four-layer retry split (in-process Polly → vendor-delay externalisation →
  `RetryableTransportFailurePolicy` → collector classifier + `MappedFailurePolicy`), and states outright that
  **Falcon stays on `SessionRetryDefaults.CreateTransient()` and does not pass `transientTransportBackoff`**
  because it owns its own long-haul recovery. Also fixes the ownership boundary: substrate owns sequencing and
  policy ordering, the collector owns vendor classification and pagination.
- `ai/skills/collector-execution-and-recovery/SKILL.md` — settles that a deferred failure inherits the
  executor-owned progress-anchored recovery budget, that policies must never gate on a raw attempt count, and
  that Falcon explicitly does not pass `transientTransportBackoff`. Do not re-derive the budget rules.
- `ai/skills/collector-flow-patterns/SKILL.md` — settles three rules this task lands on directly:
  (a) **"Do not classify a failure by matching its message. Raise a type and match the type."**
  (b) **supplying an `AdapterResilienceStrategy` makes the strategy's classifier the ONLY one consulted** —
  `CollectorResumeDefinition.ClassifyFlowException` is then dead for the runner path.
  (c) `HttpTransportFailureClassifier.IsRetryableTransportFailure` **ignores HTTP status codes entirely**.
- `SRC/Collectors/FalconCollector/FalconDocs/CollectorDocs/01-collection-strategy.md` — settles the Phase 1 /
  Phase 2 topology, that Phase 2 is bounded per aid-batch, that "Phase 2 batch retry" today means *redo the
  unpublished batch on resume*, that there are **no planned yields** in findings, and (line ~"Transport retries
  stay in DefensiveToolkit's Polly loop … neither is duplicated in the collector") that duplicating transport
  retry in the collector is currently an explicit non-goal. This task changes that statement; the doc will need
  updating.
- `SRC/Collectors/FalconCollector/Flows/Findings/TwoPhase/README.md` — settles the frozen-key-list invariants
  (write-once, positional keys, deterministic output naming) and that the design record is
  `docs/two-phase-correlation-and-the-object-store-capability.md`, not this folder.
- `SRC/Collectors/FalconCollector/FalconDocs/CollectorDocs/04-architecture.md` — read; adds nothing the above
  does not already settle for this delta.

**The delta these do not settle:** none of them cover a failure that happens *while reading a response body*.
Every retry statement in the durable layer is about the request→headers phase, which is the only phase
DefensiveToolkit's Polly pipeline actually wraps (see Q1/Q2).

## Answers to Q1-Q9

### Q1 — What Falcon can observe about vendor backpressure on the Spotlight path

Two distinct exception shapes reach the flow, and neither carries a usable `RetryAfter` in practice.

- **HTTP-status failures** are raised by `AdapterHttpClient.ThrowFailure` at
  `INFRA/src/IntegrationInfra/Conversation/AdapterHttpClient.cs:152`. It first consults the collector's
  classifier at `AdapterHttpClient.cs:166`; a non-null result is thrown *instead of* the generic exception.
- Falcon's classifier is `SRC/Collectors/FalconCollector/Flows/SharedFlows/FalconHttpFailureClassifier.cs:17`.
  It returns non-null for **any** response whose body parses as CrowdStrike-style error JSON
  (`FalconHttpFailureClassifier.cs:37` → `TryFormatCrowdStrikeStyleError` at `:85`). The
  `AdapterHttpRequestFailedException` it constructs at `FalconHttpFailureClassifier.cs:47-53` passes **six
  arguments and omits `retryAfter`**, which defaults to `null`
  (`INFRA/src/IntegrationInfra/Kernel/Exceptions/AdapterHttpRequestFailedException.cs:28`).
  → On the classified path `StatusCode` is populated, `RetryAfter` is **always null**.
- Only when the body does *not* parse as CrowdStrike JSON (empty body, proxy HTML, body > 4096 chars —
  `FalconHttpFailureClassifier.cs:91`) does control fall through to the generic throw at
  `AdapterHttpClient.cs:191`, which does populate `retryAfter: ParseRetryAfter(response)`
  (`AdapterHttpClient.cs:201`). `ParseRetryAfter` reads only the standard `Retry-After` header
  (`AdapterHttpClient.cs:203`).
- **Mid-body transport failures carry neither.** They are raw `IOException` / `HttpRequestException` /
  `SocketException` surfaced from the stream read — not `AdapterHttpRequestFailedException` at all — so they
  have no `StatusCode` and no `RetryAfter` property to read. See Q2.

**Verdict on the TenableIo delay ladder (`SRC/Collectors/TenableIoCollector/Flows/Findings/Correlated/TenableIoChunkRetry.cs:15-30`):**
- rung 1 `ex?.RetryAfter` — **dead** for the target failure (mid-stream EOF is not an
  `AdapterHttpRequestFailedException`) and dead for classified Falcon HTTP failures (never populated).
  Reachable only on the narrow unparseable-body fallback.
- rung 2 `ex?.StatusCode == 429` — **live but unreachable from the EOF path**; reachable for a Falcon 429
  whose body is CrowdStrike-shaped, because `AdapterHttpRequestFailedException : HttpRequestException` and
  `base(..., statusCode:)` is set at `AdapterHttpRequestFailedException.cs:29`.
- rung 3 exponential + jitter — the only rung that fires for the failure this task targets.

Note also `SRC/Collectors/FalconCollector/Processing/Configuration/FalconCollectorConfiguration.cs:43-44`:
Falcon uses `SessionRetryDefaults.CreateTransient(minExternalizedServerSuggestedDelay: DefaultInProcessServerDelayThreshold)`,
i.e. sub-60s vendor delays are already slept in-process by the session — but again, only around the
request→headers phase.

### Q2 — Where the `"Received an unexpected EOF or 0 bytes from the transport stream."` can originate

First, why this is a body-read failure and not something Polly can absorb:
`HTTPPKG/Session/Logic/Lifecycle/HttpSession.cs:153-156` states in code and comment that the session's tracked
operation ends **when headers arrive**, and `HTTPPKG/Session/Logic/Transport/TrueStreamingTransportService.cs:182`
(`SendWithTrueStreamingAsync`, the retried call) returns at that point;
`TrueStreamingTransportService.cs:218` hands the caller the still-open stream. **Body consumption is outside the
retry pipeline entirely.** That is the mechanical reason the claim under test is plausible.

The marker string itself is matched by `INFRA/src/IntegrationInfra/Kernel/Transport/HttpTransportFailureClassifier.cs:11-23`
(`"0 bytes from the transport stream"`, `"unexpected eof"`, `"transport stream"`), so
`IsRetryableTransportFailure` at `:50` already returns true for it — no new matching needed.

**Every body/stream read on the `CollectFindings` path:**

| # | Call site | Phase | Inside `FetchAsync`? |
|---|---|---|---|
| 1 | `SRC/.../Correlated/FalconSpotlightBatchScroller.cs:117-118` — `TopLevelJsonArrayStreamReader` over `streamed.ContentStream` | Phase 2 Spotlight scroll | **YES** (reached via `FalconSpotlightBatchPump.cs:289-291`) |
| 2 | `SRC/.../Correlated/FalconDiscoverHostScroller.cs:66,71` — Discover scroll body | Phase 1a spool | NO |
| 3 | `SRC/.../Policies/FalconDevicePolicyClient.cs:53,62` — `POST /devices/entities/devices/v2` body | Phase 1b enrichment | NO |
| 4 | `SRC/.../Policies/FalconPreventionPolicyClient.cs:59,67` — `GET /policy/entities/prevention/v1` body | Phase 1b enrichment | NO |
| 5 | `SRC/.../TwoPhase/FalconStagedHostPage.cs:70,73` — `StreamReader` + `ReadLineAsync` over the staged-page object stream, reached from `FalconStagingArea.cs:289-291` (`OpenReadAsync`) via `FalconFrozenKeyList.cs:123` | Phase 2 **source enumerable** | **NO** — it runs inside `DispatchAsync`'s `await foreach` at `FalconSpotlightBatchPump.cs:235`, one level above `FetchAsync` |
| 6 | `SRC/Processing/Validation/FalconAccessProber.cs:102,110` and `:164` — access probes | init / validation | NO |
| 7 | `SRC/Processing/Validation/FalconConfigurationValidationService.cs:45,65` | pre-init validation | NO |
| 8 | Error-snippet reads: `StreamedResponseBodyReader.ReadFirstCharsAsync` at `INFRA/.../AdapterHttpClient.cs:111` and `:137` | any failing request | inherited from whichever caller |
| 9 | Emission/publish — `NdjsonBatchEmitter…PublishFindingsUtf8PageAsync` at `SRC/.../FalconFindingsFlow.cs:309` | Phase 2 consumer | **NO** — runs on the consumer thread, not in `FetchAsync` |

**Answer to the claim:** the Spotlight body read (#1) is the only *response-body* read inside `FetchAsync`, so a
mid-stream Spotlight EOF does originate there. But it is **not** the only place on the findings path that can
raise a transport-marker exception during Phase 2: #5 (staged-page object-store read) and #9 (S3 upload) both
run in Phase 2 and lie outside `FetchAsync`. A retry scoped to `FetchAsync` covers #1 only. Whether #5/#9 need
covering is a design question, not a recon one — but note that #5's failure surfaces as a *dispatcher* fault
(`FalconSpotlightBatchPump.cs:264-269`, `writer.TryComplete(ex)`), which is a different code path.

### Q3 — What happens when an exception escapes Phase 2's consumer loop

**`OnTerminalSnapshotWithoutPublishedPage` is NOT reached, and is not even on this flow.** It is an
**assets-flow** method: defined at `SRC/.../Flows/Assets/FalconAssetsCheckpointWriter.cs:114`, called only from
`SRC/.../Flows/Assets/FalconAssetsScrollRunner.cs:339` and `:365`. The `~:442` the task prompt points at is a
`<remarks>` cross-reference inside a doc comment (`SRC/.../Flows/Findings/FalconFindingsFlow.cs:442`), not a
call site.

The findings-flow analogue is `RecordCooperativeYield` at `FalconFindingsFlow.cs:461`, and the **only** catch
around the Phase 2 consumer loop is `catch (OperationCanceledException)` at `FalconFindingsFlow.cs:351-358`.

Chain for a non-cancellation exception (which a new retry-exhausted exception would be):

1. thrown inside `FetchAsync` (`FalconSpotlightBatchPump.cs:281`) → faults the `Task<FetchedAidBatch>`
2. observed by the consumer at `FalconSpotlightBatchPump.cs:170` (`await fetch`) at that batch's queue position
3. propagates out of `PumpConcurrentlyAsync`; the `finally` at `:188-195` cancels producers and drains
4. escapes the `await foreach` at `FalconFindingsFlow.cs:269`; **does not** match the `OperationCanceledException`
   catch at `:351`
5. leaves `RunFlowAsync`, up through `AdapterBusEntrypointRunner`, into
   `AdapterResilienceStrategy` wired at `SRC/Collectors/FalconCollector/FalconCollector.cs:321`
   (and `:801`/`:822` on the resume legs)
6. `MappedFailurePolicy.classify` at
   `SRC/Processing/Resilience/FalconResilienceStrategyFactory.cs:23-29` calls
   `FalconFlowExceptionClassifier.TryClassify` (`Processing/FalconFlowExceptionClassifier.cs:39`)
7. a retryable classification routes to `CreateRetryableDecision` at
   `FalconResilienceStrategyFactory.cs:57`; unmatched arms fall to the tail at `:105-113`
   (`RequestDeferredRecovery`, **budgeted**, `falcon-mapped-retryable`, 5/15/30-minute plan from
   `CreateClassifiedRecoveryBackoff` → `CreateCursorRecoveryBackoff` at `:147`/`:150`).
   The unbudgeted flat-5-minute plan the task wants already exists as
   `CreateServerErrorRecoveryBackoff` at `:175`, issued with `UseRecoveryBudget: false` at `:88`.

**Confirmed: a deferral does not call `AdvancePage` and does not mint a page.**
`AdvancePage` on this flow is only reached through `checkpointWriter.OnBatchPublished`
(`FalconFindingsFlow.cs:329`), which runs only *after* a successful publish at `:309`. The escape path at step 4
bypasses it entirely. `RecordCooperativeYield` (the only other checkpoint-firing path) explicitly stamps and
fires `OnCheckpoint` **without** `AdvancePage` (`FalconFindingsFlow.cs:495-499`, and the reasoning at `:443-444`)
— and it is not called on this path anyway.

Consequence worth stating: on the deferral path, **nothing at all is written** — no state snapshot, no reached
position. The resume relies purely on the last `OnBatchPublished` checkpoint, which is correct (the in-flight
batch published nothing) but means the leg leaves no `_resume.*` reach statement.

### Q4 — Is `FetchAsync` safe to call twice for the same `StagedAidBatch`?

**Yes**, with three named conditions on how the retry loop is written.

Everything `FetchAsync` touches, audited:

- `stats` (`FalconSpotlightBatchPump.cs:286`) and `records` (`:287`) are **locals allocated per call**. A retry
  loop must re-allocate both per attempt, or counts double and records duplicate. *(condition 1)*
- `Interlocked.Increment(ref _fetchedBatches)` at `:296` is **after** the loop, so a failed attempt does not
  increment. A retry loop must keep it after the last successful attempt. *(condition 2)*
- `SeedAccumulators(batch)` at `:362-374` allocates a **fresh** `Dictionary` and a **fresh**
  `HostFindingsAccumulator` per call. `HostFindingsAccumulator`
  (`SRC/.../Correlated/HostFindingsAccumulator.cs:11-28`) holds `Findings` (fresh `List`), `ChunkIndex` and
  `TotalFindings` (both default 0). Nothing survives a call.
- **The one shared object is `DiscoverHost.Host`** (`FalconDiscoverHostScroller.cs:26`), a `JsonObject` handed
  to the accumulator by reference at `FalconSpotlightBatchPump.cs:370`. `StagedAidBatch.Hosts`
  (`FalconFrozenKeyList.cs:27-30`) is materialised once at `FalconFrozenKeyList.cs:123`/`:136`.
  **It is never mutated and never re-parented**: `FalconCorrelatedRecord.Build` writes
  `["host"] = host.DeepClone()` at `SRC/.../Correlated/FalconCorrelatedRecord.cs:69`. Without that
  `DeepClone`, a second `Build` on the same `JsonObject` would throw
  (`System.Text.Json` nodes have a single parent) — so the safety here is real but load-bearing on line 69.
- `_scroller` is stateless per call. `FalconSpotlightBatchScroller.EmitBatchRecordsAsync`
  (`FalconSpotlightBatchScroller.cs:61`) keeps `aidList`, `baseFilter`, `seenFindingIds`, `updatedFloor`,
  `scrollFilter` and `after` all as method locals (`:67-80`). Fields are `_http`, `_logger`,
  `_spotlightBaseUrl`, `_chunkFindingsCap` — all readonly. The class doc asserts this at `:36-37`.
- `FalconCorrelatedRecord.ShapeFinding` (`FalconCorrelatedRecord.cs:32`) re-parses from raw text, so shaped
  findings are detached from the disposed source document.

*(condition 3)* A retry restarts the scroll from `windowStartUtc` with an empty `seenFindingIds`, so a partially
consumed batch is re-fetched **from the beginning**, not resumed. That is correct (nothing was published) but it
means the retry cost is the whole batch, and any partial records accumulated in `records` must be discarded.

**Nothing carries state across two attempts** provided conditions 1–3 hold.

### Q5 — SemaphoreSlim slot lifecycle vs. a delay inside `FetchAsync`

- Slot **taken** at `FalconSpotlightBatchPump.cs:237` (`await slots.WaitAsync`), **before** `FetchAsync` is
  invoked at `:239`.
- Slot **released** at `:180`, on the consumer, **after** the consumer has published that batch and come back
  for the next. The comment at `:177-179` states this is deliberate: releasing at fetch completion would let
  producers run a full degree ahead of a slow publisher.

A sleep inside `FetchAsync` therefore **holds its slot for the whole delay**. Effects:

1. Dispatch of *new* batches stalls once all `degree` slots are occupied. With `degree=6`, five other fetches
   can still be in flight; if several hit the same vendor fault (which is the realistic case for a server-side
   problem) the dispatcher stops entirely.
2. **The consumer stalls regardless of the degree.** The consumer awaits tasks in dequeue order at `:170`, so if
   the *head-of-line* batch is sleeping, publish and checkpoint stop for the full delay even though later
   batches have already completed. Head-of-line blocking is the dominant effect, not slot exhaustion.
3. Materialised records of every completed-but-unpublished batch stay live in memory for the duration
   (`FalconSpotlightBatchPump.cs:277-279` and the memory model at
   `FalconCollectorConfiguration.cs:216-224`: degree 6 ≈ 420–600 MB on the heaviest observed tenant). A long
   in-`FetchAsync` sleep extends that residency window.
4. `producerCts` (`:149`) links to the flow's token, so a sleep must honour the token or a co-operative stop is
   delayed by the full backoff.
5. **`degree == 1` does not go through `FetchAsync` at all** — `RunAsync` at `:107-109` branches to
   `PumpSequentiallyAsync` (`:116`), which calls `_scroller.EmitBatchRecordsAsync` directly at `:128` inside a
   lazy enumerable driven by the publisher. See Landmines.

### Q6 — Config conventions and where new retry knobs go

Falcon's idiom is **two-stage and differs from TenableIo's**:

- **Stage 1 — extract + clamp**, in `FalconCollectorConfigurationBuilder`: `TryGetInt(configuration, "<key>", out var v)`
  then `Math.Clamp(...)` into a nullable local, collected into the `ExtractedFields` record
  (`SRC/Processing/Configuration/FalconCollectorConfigurationBuilder.cs:364-366`). Examples:
  `aidBatchSize` clamp `1..5000` at `:172-174`; `maxPagesPerCursorScroll` clamp `1..100000` at `:178-180`;
  `maxConcurrentSpotlightBatches` clamp `1..16` at `:188-191`.
- **Stage 2 — fold**, in `ApplyOptionalOverrides` (`:214`): `if (fields.X.HasValue) cfg = cfg with { X = fields.X.Value };`
  at `:229-242`. Simpler knobs skip `ExtractedFields` and clamp inline —
  `chunkFindingsCap` at `:258-261`, `batchScopedStorage` at `:266-269`,
  `enablePreventionPolicyEnrichment` at `:273-276`.
- **Defaults live on the record**, typed, with a doc comment carrying the justification:
  `AidBatchSize = 8` (`FalconCollectorConfiguration.cs:128`), `ChunkFindingsCap = 2000` (`:147`),
  `MaxPagesPerCursorScroll = 500` (`:157`), `BatchScopedStorage = false` (`:166`),
  `MaxConcurrentSpotlightBatches = DefaultMaxConcurrentSpotlightBatches` (`:235`) reading a
  `private const int … = 6` at `:203` — the "one place the shipped degree is written down" pattern (`:181-185`).
- **Absent / unparseable / out-of-range values keep the typed default** — stated at `:212` and `:257`.

TenableIo by contrast declares defaults *and* bounds at the call site:
`MaxChunkRetryAttempts = 5` / `ChunkRetryBaseDelaySeconds = 10` on the record
(`SRC/Collectors/TenableIoCollector/Processing/Configuration/TenableIoCollectorConfiguration.cs:54,60`) with
`GetInt(dict, "chunkRetryBaseDelaySeconds", defaultValue: 10, min: 1, max: 60)` in the builder
(`.../TenableIoCollectorConfigurationBuilder.cs:78-79`). **Do not import `GetInt`'s signature into Falcon** —
Falcon has no such helper; its helper is `TryGetInt` at `FalconCollectorConfigurationBuilder.cs:464`.

New retry knobs go: property + doc comment + `private const` default on
`FalconCollectorConfiguration.cs` (near `:235`), clamp in `FalconCollectorConfigurationBuilder.cs` (near
`:188-191`), fold in `ApplyOptionalOverrides` (near `:239-242`), and `ExtractedFields` extension at
`:364-366` if the two-stage form is used. Config-key documentation strings live in
`SRC/Processing/Configuration/FalconIdentification.cs:37`.

### Q7 — `Exceptions/` conventions in FalconCollector

Only two types exist, and they disagree on two points — pick the sealed one.

- `SRC/Collectors/FalconCollector/Exceptions/FalconResumeNotPossibleException.cs:6` — `public sealed class …
  : Exception`, two ctors (`string message` at `:8`, `string message, Exception innerException` at `:13`),
  a single `<summary>` doc comment naming the *condition* (`:3-5`). **This is the convention to mirror.**
- `SRC/Collectors/FalconCollector/Exceptions/FalconCursorExpiredException.cs:9` — `public class` (not sealed),
  four ctors (`:24`, `:33`, `:43`, `:54`), extra data properties `CursorToken` (`:14`) and `CursorAge` (`:19`),
  full XML docs on every member. Richer, older, and unsealed.

Both are `public` (not `internal`) and sit directly in the flat `Exceptions/` folder, namespace
`Cymulate.Integration.Adapters.Collectors.FalconCollector.Exceptions`. Both name the vendor condition, not the
mechanism. Both are matched **by type** in `FalconFlowExceptionClassifier.cs:46` and `:74` and in
`FalconResilienceStrategyFactory.cs:39`/`:63`.

### Q8 — Test conventions and the injection seam

**The seam is `FakeHttpClientFactory`** — `SRC/UnitTests/Collectors/Collectors.Tests.Infrastructure/FakeHttpClientFactory.cs:27`,
constructed from a `Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>` (`:32`) and injected
via `mockServiceProvider.Setup(sp => sp.GetService(typeof(IHttpClientFactory)))` at
`SRC/UnitTests/.../FalconConcurrencyHarness.cs:282`. A mid-body EOF is injectable by returning an
`HttpResponseMessage` whose `Content` wraps a stream that throws on read — the harness's `Ok(string)` helper
(`FalconConcurrencyHarness.cs:373`) would need a throwing-stream sibling.

There is **no fake at the scroller or pump level**. `FalconSpotlightBatchScroller` and
`FalconSpotlightBatchPump` are both concrete `internal sealed class` with no interface, and every existing test
drives them end-to-end through the collector.

Harness surface a new retry test would use:
- `FalconConcurrencyHarness.CreateFactory(Func<string,string,CancellationToken,Task<HttpResponseMessage?>>)`
  at `FalconConcurrencyHarness.cs:383` — async route handler with the two access probes pre-answered at
  `:390-400`, so a gate/counter is not fooled by them.
- `FalconConcurrencyHarness.SpotlightScrollMarker` at `:376` — distinguishes a real Spotlight scroll from the
  `limit=1` probe.
- `FalconConcurrencyHarness.CreateContext(IHttpClientFactory, …)` at `:271`, which also registers
  `IAdapterObjectStore` at `:288` (the `InMemoryFalconStagingStore`,
  `SRC/UnitTests/.../InMemoryFalconStagingStore.cs`).
- `FalconConcurrencyHarness.InitializedCollector(context, degree, aidBatchSize)` at `:579`.
- `FalconConcurrencyHarness.SeedOneHostPerPage(...)` at `:468` — one staged page = one aid batch = one object.
- `FalconConcurrencyHarness.AssertPublished(result, capture, expectedObjects)` at `:612` — and its remarks at
  `:604-611` warn explicitly **not** to assert on `result.Success`, because a resumed leg's partial-success
  gate can report success having published nothing.
- `FalconConcurrencyHarness.ConcurrencyCapture` at `:197` — records `PublishEnter`/`PublishExit`/`Checkpoint`
  on one interleaved timeline (`:180-192`), and `capture.Errors` (used at `:621`).
- Degree knob key: `FalconConcurrencyHarness.ConcurrencyKey = "maxConcurrentSpotlightBatches"` at `:65`.
- Older synchronous route helper for the non-concurrent tests:
  `SRC/UnitTests/.../FalconCorrelatedFindingsTests.cs:232` (`CreateFactory(Func<string,string,HttpResponseMessage?>)`),
  plus a bare `new FakeHttpClientFactory(...)` at `:1528`.

Unit-level shapes for the classifier/strategy halves of the change:
- `SRC/UnitTests/.../FalconFlowExceptionClassifierTests.cs:27` — call `TryClassify` directly, assert
  `ErrorCode` + `IsRetryable`.
- `SRC/UnitTests/.../FalconResilienceStrategyTests.cs:16-55` — build the strategy via
  `FalconResilienceStrategyFactory.Create()`, `await strategy.DecideAsync(new AdapterFailureContext { … })`,
  assert the decision subtype and the `BackoffPlan.GetDelay(n)` ladder. This is the exact shape for asserting
  "unbudgeted flat 5-minute" (`decision.Should().BeOfType<AdapterFailureDecision.RequestDeferredRecovery>()`,
  then `UseRecoveryBudget` and `GetDelay(0..n) == 5min`).

**Test classes that touch the pump/Spotlight path** (all under
`SRC/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.FalconCollector.Test/`):
`FalconSpotlightConcurrencyTests.cs` (944 lines), `FalconCorrelatedFindingsTests.cs` (1704),
`FalconTwoPhaseFindingsTests.cs` (2860), `FalconConcurrencyHarness.cs` (629, shared harness),
`FalconCollectorTests.cs`, `FalconTacticalLoggingTests.cs`, `FalconAccessProberTests.cs`,
`FalconUrlsTests.cs`, `FalconCollectorConfigurationBuilderTests.cs`.

### Q9 — Existing retry / backoff / `Task.Delay` in FalconCollector

**None.** A grep for `Task.Delay|Thread.Sleep|for (int attempt|while (attempt|maxAttempts|MaxAttempts|retryCount|Polly`
across `SRC/Collectors/FalconCollector/` returns only:

- `SRC/Processing/Configuration/FalconCollectorConfigurationBuilder.cs:405,414` — parsing the
  `retryMaxAttempts` config key into `SessionRetryDefaults.CreateTransient(...)` at `:413-416`. Session
  configuration, not a loop.
- `SRC/Processing/Configuration/FalconIdentification.cs:37` — the config-key description string.
- `SRC/Recovery/FalconResumeRunner.cs:14` — a `using Polly;` import for the substrate resume scaffold.
- `SRC/Flows/Policies/FalconPolicyEnricher.cs:158` and `:269` — comments stating that retry is *already
  exhausted* by the session's Polly pipeline, so the enricher does not retry.

**There is no in-collector retry convention to mirror. TenableIo is the only exemplar in this repo.**
That also means this change introduces the first in-flow sleep in FalconCollector, and contradicts the standing
statement in `FalconDocs/CollectorDocs/01-collection-strategy.md` ("Transport retries stay in DefensiveToolkit's
Polly loop … neither is duplicated in the collector") — that doc line needs updating with the change.

## Files in scope

Verified against the prompt's expected list. Corrections marked **[+]** (add) / **[!]** (revise).

- `SRC/Collectors/FalconCollector/Flows/Findings/Correlated/FalconSpotlightBatchPump.cs` — the retry loop's home
  (`FetchAsync`, `:281`) — touched by: retry implementation.
- **[!]** `SRC/Collectors/FalconCollector/Flows/Findings/Correlated/FalconSpotlightBatchPump.cs:116-132`
  (`PumpSequentiallyAsync`) — the degree-1 path, which bypasses `FetchAsync` entirely — touched by: retry
  implementation, *if* degree 1 is in scope.
- `SRC/Collectors/FalconCollector/Exceptions/` — new exception type — touched by: exception + classification.
- `SRC/Collectors/FalconCollector/Processing/FalconFlowExceptionClassifier.cs:41` — new switch arm — touched by:
  classification.
- `SRC/Collectors/FalconCollector/Processing/Resilience/FalconResilienceStrategyFactory.cs:57-113` — new branch
  in `CreateRetryableDecision`; `CreateServerErrorRecoveryBackoff` at `:175` is the existing unbudgeted flat
  5-minute plan — touched by: routing.
- `SRC/Collectors/FalconCollector/Processing/Configuration/FalconCollectorConfiguration.cs` — new knob(s) —
  touched by: config.
- `SRC/Collectors/FalconCollector/Processing/Configuration/FalconCollectorConfigurationBuilder.cs:172-191,
  229-242, 364-366` — parse/clamp/fold — touched by: config.
- **[!]** `SRC/Collectors/FalconCollector/Flows/Findings/FalconFindingsFlow.cs` — **read-only for this change.**
  The only catch is `OperationCanceledException` at `:351`; nothing here needs editing for the exception to
  reach the strategy. Edit it only if the deferral should also leave a reach statement (it currently will not).
- **[+]** `SRC/Collectors/FalconCollector/FalconDocs/CollectorDocs/01-collection-strategy.md` — the
  "Transport retries stay in DefensiveToolkit's Polly loop … neither is duplicated in the collector" statement
  and the "Phase 2 batch retry" paragraph both become wrong — touched by: docs.
- **[+]** `SRC/Collectors/FalconCollector/Processing/Configuration/FalconIdentification.cs:37` — config-key
  descriptions, if a new key is exposed — touched by: config.
- **[+]** `ai/skills/collector-flow-patterns/SKILL.md` — per the operator's standing rule (`ai/skills` is the
  repo's collector how-to home and must be updated when behaviour/architecture changes).
- `SRC/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.FalconCollector.Test/` — new tests +
  possible harness helper — touched by: tests.

## Patterns to mirror

- **In-flow chunk retry helper** → `SRC/Collectors/TenableIoCollector/Flows/Findings/Correlated/TenableIoChunkRetry.cs:13`
  — a `internal static class` of pure helpers (`ComputeDelay`, `IsRetryableStreamFailure`, `AddJitter`), no
  state, called from the phase's own loop. Its callers show the loop shape:
  `SRC/Collectors/TenableIoCollector/Flows/Findings/Correlated/TenableIoVulnPhase.cs:254-261` and
  `.../TenableIoAssetSpoolPhase.cs:291-298` — `catch (…) when (!isLastAttempt && IsRetryableStreamFailure(ex))`.
- **Transport-failure predicate** → `INFRA/src/IntegrationInfra/Kernel/Transport/HttpTransportFailureClassifier.cs:50`
  (`IsRetryableTransportFailure`) + `:30` (`IsCircuitBreakerException`). Already matches the EOF string at
  `:11-23`. **Use these; do not write a new message match** (`collector-flow-patterns/SKILL.md`).
- **Exception type** → `SRC/Collectors/FalconCollector/Exceptions/FalconResumeNotPossibleException.cs:6`
  (public sealed, two ctors, one condition-naming summary).
- **Classifier arm** → `SRC/Processing/FalconFlowExceptionClassifier.cs:74-79` — type pattern → 
  `FlowExceptionHandling(Message, ErrorCode, ErrorSeverity.Error, IsRetryable)`. Note the ordering rule stated
  at `:81-82`: subtypes before base types.
- **Unbudgeted flat deferral** → `SRC/Processing/Resilience/FalconResilienceStrategyFactory.cs:77-89`
  (branch) + `:175-182` (plan). The remarks at `:164-174` explain exactly why `MaxRetries = int.MaxValue` with
  `UseRecoveryBudget: false` is correct and what the executor does and does not read.
- **Decision logging** → `FalconResilienceStrategyFactory.LogRecoverDecision` at `:117` — every branch logs
  `recover.decision` with classification/attempt/delay/reason/budget/fallback. A new branch must call it.
- **Config knob** → `FalconCollectorConfiguration.cs:203` (`private const` default) + `:235` (property with
  justification doc) + `FalconCollectorConfigurationBuilder.cs:188-191` (clamp) + `:239-242` (fold).
- **Strategy unit test** → `SRC/UnitTests/.../FalconResilienceStrategyTests.cs:31-55`.
- **End-to-end fault-injection test** → `SRC/UnitTests/.../FalconConcurrencyHarness.cs:383` route handler +
  `:612` `AssertPublished`.

## Shared surface to freeze

- **`FetchAsync(StagedAidBatch, DateTime, CancellationToken) → Task<FetchedAidBatch>`**
  (`FalconSpotlightBatchPump.cs:281`) — produced by the pump's dispatcher (`:239`), consumed by the channel
  reader (`:170`). Its **fault semantics are the contract**: a faulted task surfaces at the batch's queue
  position, and the `finally` at `:188-195` guarantees drain. Any retry must keep the method returning a
  `Task<FetchedAidBatch>` that either completes with a full batch or faults.
- **`FalconSpotlightBatchScroller.EmitBatchRecordsAsync(IReadOnlyDictionary<string, HostFindingsAccumulator>, DateTime, BatchEmitStats, CancellationToken)`**
  (`FalconSpotlightBatchScroller.cs:61`) — produced by the scroller, consumed by **both** pump paths
  (`FalconSpotlightBatchPump.cs:128` sequential, `:290` concurrent). Stateless-per-call is the property both
  paths rely on and the retry depends on.
- **`FalconFlowExceptionClassifier.TryClassify(Exception) → FlowExceptionHandling?`**
  (`FalconFlowExceptionClassifier.cs:39`) — produced here, consumed by
  `FalconResilienceStrategyFactory.cs:25`, `FalconCollector.cs:319`, `FalconResumeRunner.cs:108` and `:153`.
  One classifier, four consumers: a new arm changes behaviour on all of them at once.
- **`AdapterFailureDecision.RequestDeferredRecovery(handling, plan, Reason, UseRecoveryBudget)`** — produced at
  `FalconResilienceStrategyFactory.cs:84` and `:109`, consumed by the substrate's
  `AdapterFailureDecisionExecutor`. `UseRecoveryBudget: false` is the switch that makes the deferral unbounded.
- **`BatchEmitStats`** (`FalconSpotlightBatchScroller.cs:15`) — mutable counter bag, written by the scroller,
  read by the consumer at `FalconFindingsFlow.cs:297,321-322,341-344`. A retry must not hand the consumer a
  `BatchEmitStats` that accumulated a failed attempt.
- **Test contract: `FalconConcurrencyHarness.ConcurrencyKey`** (`:65`) and `SpotlightScrollMarker` (`:376`) —
  produced by the harness, consumed by every concurrency test. New tests should reuse, not restate.

## Disjoint sets available

- **Set A — pump retry**: `SRC/Collectors/FalconCollector/Flows/Findings/Correlated/FalconSpotlightBatchPump.cs`
  — independent of: Set B, Set C.
- **Set B — exception + classification + routing**:
  `SRC/Collectors/FalconCollector/Exceptions/<new>.cs`,
  `SRC/Collectors/FalconCollector/Processing/FalconFlowExceptionClassifier.cs`,
  `SRC/Collectors/FalconCollector/Processing/Resilience/FalconResilienceStrategyFactory.cs`
  — independent of: Set A, Set C. **Set A depends on Set B's type name only** — freeze the exception's
  name and ctor signature up front and the two proceed in parallel.
- **Set C — config**:
  `SRC/Collectors/FalconCollector/Processing/Configuration/FalconCollectorConfiguration.cs`,
  `.../FalconCollectorConfigurationBuilder.cs`, `.../FalconIdentification.cs`
  — independent of: Set A, Set B. **Set A depends on Set C's property names only** — same treatment.
- **Set D — docs**: `SRC/Collectors/FalconCollector/FalconDocs/CollectorDocs/01-collection-strategy.md`,
  `ai/skills/collector-flow-patterns/SKILL.md` — independent of all; must run last to describe what shipped.
- **Tests are NOT disjoint.** The strategy/classifier unit tests
  (`FalconResilienceStrategyTests.cs`, `FalconFlowExceptionClassifierTests.cs`) partition cleanly with Set B.
  But an end-to-end retry test must add a throwing-stream helper to the *shared*
  `FalconConcurrencyHarness.cs`, which `FalconSpotlightConcurrencyTests.cs` also owns — one worker must own
  the harness file. `FalconCollectorConfigurationBuilderTests.cs` partitions with Set C.

## Landmines

1. **`degree == 1` never calls `FetchAsync`.** `RunAsync` branches at `FalconSpotlightBatchPump.cs:107-109`;
   degree 1 goes to `PumpSequentiallyAsync` (`:116`), which hands the publisher a **lazy** enumerable
   (`:128`) driven from inside the consumer's publish. A retry placed only in `FetchAsync` leaves the
   documented rollback path (`FalconCollectorConfiguration.cs:212-215`: "set `maxConcurrentSpotlightBatches`
   to 1 … the rollback") with **no** transport retry. Worse, retrofitting a retry into the lazy path is not
   symmetric: by the time the sequential enumerable throws, the publisher has already consumed records, so a
   restart-from-scratch would double-emit. Decide explicitly whether degree 1 is in scope.
2. **Head-of-line blocking, not slot starvation, is the cost.** A sleeping head-of-line batch stalls publish
   and checkpoint for the entire delay while up to `degree` materialised record sets (≈420–600 MB at degree 6
   on the heaviest observed tenant — `FalconCollectorConfiguration.cs:216-224`) stay resident. Total in-process
   retry budget should be sized against that, not against wall-clock politeness.
3. **`FalconCorrelatedRecord.Build`'s `host.DeepClone()` at `FalconCorrelatedRecord.cs:69` is what makes a
   second `FetchAsync` attempt legal.** The `DiscoverHost.Host` `JsonObject` is shared across attempts by
   reference (`FalconSpotlightBatchPump.cs:370`); `System.Text.Json` nodes have a single parent, so removing
   that `DeepClone` would turn a retry into an `InvalidOperationException`. Do not touch line 69.
4. **`Retry-After` is structurally unavailable to Falcon.** `FalconHttpFailureClassifier.cs:47-53` omits the
   `retryAfter` argument, so every classified Falcon HTTP failure has `RetryAfter == null`. Copying
   `TenableIoChunkRetry.ComputeDelay`'s first rung verbatim ships a branch that can never fire. Either fix the
   classifier (a real, small, separate improvement) or drop the rung — do not ship it dead.
5. **A mid-body EOF is not an `AdapterHttpRequestFailedException`** at all — it is a raw
   `IOException`/`HttpRequestException` from the stream read, with no `StatusCode`. A `catch
   (AdapterHttpRequestFailedException ex)` arm will not see it. Catch on
   `HttpTransportFailureClassifier.IsRetryableTransportFailure` (`INFRA/.../HttpTransportFailureClassifier.cs:50`).
6. **The unbudgeted flat 5-minute plan already exists** at `FalconResilienceStrategyFactory.cs:175`
   (`CreateServerErrorRecoveryBackoff`), used by the `IsServerErrorRecovery` branch at `:77-89`. Adding a
   *second* identical plan is duplication; adding the new exception to `IsServerErrorRecovery` at `:198` is
   probably wrong (it is a status/circuit predicate). A third named branch reusing the same plan factory is the
   shape that fits.
7. **`FalconCursorExpiredException` is caught and swallowed inside the scroll**
   (`FalconSpotlightBatchScroller.cs:99-110`) when an `after` token exists. A retry wrapping `FetchAsync` will
   never see it, and must not accidentally re-anchor logic that already lives one level down.
8. **A budgeted deferral would exhaust the run.** The tail branch at `FalconResilienceStrategyFactory.cs:105-113`
   defaults to `UseRecoveryBudget: true` with the 3-attempt 5/15/30 plan (`:150`). The remarks at `:164-174`
   record that this exact shape previously killed runs on the fourth consecutive no-progress deferral. If the
   new exception falls through to the tail instead of getting its own branch, the change silently reintroduces
   that failure.
9. **`writer.TryComplete(ex)` at `FalconSpotlightBatchPump.cs:268` is a different fault path.** A staged-page
   read failure (`FalconStagedHostPage.cs:70,73` → `FalconStagingArea.cs:289-291`, reached from
   `FalconFrozenKeyList.cs:123`) runs inside `DispatchAsync`'s source enumeration, is caught at `:264`, and
   surfaces to the flow only after already-queued batches drain (`:186`). It is a Phase 2 stream read that a
   `FetchAsync`-scoped retry does not cover.
10. **The classifier is shared by four call sites** — `FalconCollector.cs:319`,
    `FalconResilienceStrategyFactory.cs:25`, `FalconResumeRunner.cs:108`, `FalconResumeRunner.cs:153`. Also note
    `ai/skills/collector-flow-patterns/SKILL.md`: supplying an `AdapterResilienceStrategy` makes the strategy's
    classifier the ONLY one consulted; `CollectorResumeDefinition.ClassifyFlowException` is not a fallback.
11. **Do not assert `result.Success` in the new test.** `FalconConcurrencyHarness.cs:604-611` documents that a
    resumed leg can report success having published nothing. Assert the published-object count and
    `capture.Errors`.
12. **`FetchedBatches` semantics.** `Interlocked.Increment(ref _fetchedBatches)` at
    `FalconSpotlightBatchPump.cs:296` means "a scroll ran to completion" at both degrees (`:343` for the
    sequential path). Incrementing it per *attempt* would corrupt the co-operative-stop diagnostic at
    `FalconFindingsFlow.cs:356` and its "5 fetched, 2 published" log line at `:474-485`. The doc comment at
    `:83-93` states this counter must never become a position.
13. **Versioning.** Per the operator's standing rule, `CollectorVersion` lives in
    `SRC/Collectors/Directory.Build.props` only — never in the csproj — and the bump is the operator's call.
14. **Do not run the full FalconCollector.Test suite** (operator rule: risky simulations hang). Filter to
    `FalconResilienceStrategyTests`, `FalconFlowExceptionClassifierTests`, `FalconSpotlightConcurrencyTests`,
    and `FalconCollectorConfigurationBuilderTests`.
