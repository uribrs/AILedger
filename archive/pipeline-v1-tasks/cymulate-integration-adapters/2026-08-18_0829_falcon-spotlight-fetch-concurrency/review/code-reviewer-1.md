# Code review — Falcon Spotlight fetch concurrency

Reviewer: code-reviewer-1
Date: 2026-08-18

## Scope reviewed

Working tree of `/Users/user/Dev/cymulate-integration-adapters-falcon-concurrency`
(branch `falcon-concurrency-and-server-side-retries-with-backoff`, base `1f7a2ba6`) —
10 modified + 3 untracked files, all uncommitted.

Subsystems touched: Falcon two-phase correlated findings flow (Phase 2 traversal), collector
configuration + builder, flow-run preparation, shared collector test infrastructure.

Docs consulted before judging idiom: repo `CLAUDE.md`,
`FalconDocs/CollectorDocs/04-architecture.md`, `05-testing.md`, `06-resume-live-state-authority.md`.

### Verification performed

- Falcon collector and its test project rebuilt (`--no-incremental`): **0 warnings, 0 errors**.
- New concurrency tests run: **13 passed**.
- Full Falcon test project run: **206 passed, 1 failed** (see B1).
- The failing test re-run against a detached `git worktree` at `HEAD`: **passes** (365 ms) —
  so B1 is a regression introduced by this change set, not pre-existing.
- Other test projects consuming the shared `FakeHttpClientFactory` (Qualys, IsbLoadTest,
  ServiceNowCmdb, InsightVmCloud) run. All green **except** ServiceNowCmdb's
  `ProcessAsync_Assets_WithFilter_ReturnsFlowNotSupported_AndPublishesFailure`, which was
  confirmed to fail identically at `HEAD` — **pre-existing, unrelated to this change set,
  not filed against this PR**.
- `System.Threading.Channels` completion-exception semantics probed directly on net8.0 (8.0.28)
  and net10.0 (see I1) rather than asserted from recall.

---

## Blockers

- **B1** — `UnitTests/.../FalconTwoPhaseFindingsTests.cs:1679` (test);
  root cause `Processing/Configuration/FalconCollectorConfiguration.cs:214`
  - **Problem:** `ResumeAfterACooperativeStop_PublishesEveryHostExactlyOnce` fails on this branch
    and passes at `HEAD` (verified in a clean worktree). Neither
    `FalconTwoPhaseFindingsTests.RunMetadata` (`:267`) nor its `InitializedCollector` (`:381`)
    sets `maxConcurrentSpotlightBatches`, so the entire pre-existing two-phase suite now runs at
    the new default degree of 4. That test's fake cancels on the 4th Spotlight request
    (`spotlightCalls > 3`, `:1706`); at degree 4 all four scrolls are dispatched before the
    consumer publishes anything, so leg 1 publishes **0** objects instead of 3 and the assertion
    at `:1732` fails. The product behaviour is what the design intends (a stop discards up to N
    fetched batches) — but the branch as it stands ships a red suite in the collector's own
    high-signal regression file, and `05-testing.md` makes that the merge contract ("Green is the
    contract… the whole suite passes — no test weakened to go green").
  - **Suggestions:**
    1. Pin the legacy suite to the sequential degree — add
       `["maxConcurrentSpotlightBatches"] = "1"` to both `FalconTwoPhaseFindingsTests.RunMetadata`
       and its `InitializedCollector` config dictionary, matching what `FalconConcurrencyHarness`
       already does explicitly. **Preferred:** those tests were written against sequential
       semantics (call-counted cancellation, single-threaded fakes),
       `FalconSpotlightConcurrencyTests` is the concurrency coverage, and it is a two-line change
       that makes the assumption visible rather than inherited.
    2. Keep the default and re-time the cancellation on an observable the fan-out cannot outrun —
       cancel from `OnCheckpoint` on the third checkpoint (as
       `FalconSpotlightConcurrencyTests.CancellationWithFetchesInFlight…` does at `:519`) instead
       of from the HTTP fake's request counter, then relax the expectation to "leg 1 published a
       prefix, leg 1 ∪ leg 2 = h1..h6". More faithful to production, but it rewrites a test that
       is currently a precise witness for mid-scroll interruption.

---

## Important

- **I1** — `Flows/Findings/Correlated/FalconSpotlightBatchPump.cs:163` and `:186`
  - **Problem:** Correct fault classification silently depends on an undocumented asymmetry in
    `System.Threading.Channels`: `WaitToReadAsync` rethrows the completion exception
    **unwrapped**, while `ReadAsync`/`ReadAllAsync` wrap it in `ChannelClosedException`
    (verified on net8.0/8.0.28 and net10.0). Because `DispatchAsync` reports source faults via
    `writer.TryComplete(ex)` (`:249`), every exception from `FalconFrozenKeyList.EnumerateAsync`
    — the missing-staged-page `InvalidOperationException`, `ObjectStoreAccessDeniedException`,
    `ObjectStoreTransientException` — currently reaches
    `FalconFlowExceptionClassifier.TryClassify` as its own type *only* because of the
    `WaitToReadAsync` + `TryRead` shape. Collapsing that double loop into the more idiomatic
    `await foreach (var t in channel.Reader.ReadAllAsync(ct))` — the obvious future
    "simplification" — turns all of them into `ChannelClosedException`, which hits `_ => null` and
    falls to `UnknownFlowRetryPolicy`'s blind 3× 30/60/120 s retry. That is exactly the live
    incident the classifier's own doc-comment (`FalconFlowExceptionClassifier.cs:28-35`) was
    written to stop. Separately, the comment above `await dispatcher` at `:186` misstates the
    mechanism: that line is reached only when the channel completed *successfully*, so it never
    surfaces a dispatcher fault.
  - **Suggestions:**
    1. Replace the `:184-186` comment with the real rule ("`WaitToReadAsync` rethrows the
       completion exception as-is; `ReadAsync`/`ReadAllAsync` would wrap it in
       `ChannelClosedException` and break `FalconFlowExceptionClassifier` — do not switch") and
       keep `await dispatcher` only as a completion join, noting it cannot fault here.
    2. Pin it with a test: seed `InMemoryFalconStagingStore` so a staged page named by the
       manifest is absent past the resume position, run at degree 4, and assert the published
       `ErrorRequest.ErrorCode` is not the unclassified path — i.e. that the
       `InvalidOperationException` type survives the channel. Cheap, and it fails loudly if anyone
       rewrites the read loop.

- **I2** — `FalconSpotlightBatchPump.cs:170-180`;
  doc claim at `Processing/Configuration/FalconCollectorConfiguration.cs:196-203`
  - **Problem:** Peak live record sets is **degree + 1**, not degree, so the configuration doc's
    "no more than `degree` record sets are ever live" and its 140–200 MB / 280–400 MB /
    560–800 MB arithmetic are understated by one batch (~35–50 MB). `fetched` is a hoisted local
    of the async iterator and `pumped` is the flow's loop variable; neither is cleared, so the
    just-published batch's `List<ReadOnlyMemory<byte>>` is still rooted when `slots.Release()` at
    `:180` admits the next producer. In a collector whose entire recent history is object-size and
    heap incidents, an ops-facing memory number should not be optimistic.
  - **Suggestions:**
    1. Drop the records before releasing the slot — make `FetchedAidBatch.Records` a
       `List<ReadOnlyMemory<byte>>` and clear it immediately after `yield return` (the consumer
       has provably published by then, and `ReplayAsync` has been fully drained), then
       `slots.Release()`. **Preferred:** it makes the documented bound true rather than restating
       it.
    2. If you would rather not add teardown coupling, correct the doc to
       `(degree + 1) × published-object-size` and update the three quoted ranges.

- **I3** — `Processing/Configuration/FalconCollectorConfigurationBuilder.cs:188-192`
  - **Problem:** The degree is only readable from the per-integration `configuration` dictionary,
    yet it ships **on by default (4)** for every Falcon tenant. The documented rollback
    ("set `maxConcurrentSpotlightBatches` to 1 … with no redeploy",
    `FalconCollectorConfiguration.cs:180-184`) therefore means editing every Falcon integration
    individually — which is not a usable response to a fleet-wide memory or rate-limit incident.
  - **Suggestions:**
    1. Give the degree the same resolution chain the flow already uses for
       `IngestionOptions`/`MemoryPressureOptions` (`FalconFindingsFlow.cs:582-583`):
       per-integration config → `IConfiguration` → environment variable → default. One env var
       then drops the whole fleet to 1 without a redeploy or a per-tenant edit. **Preferred** —
       the precedent and the plumbing already exist in this flow.
    2. Alternatively, ship the default at `1` and enable per-tenant, turning the rollout into an
       opt-in rather than the rollback into an opt-out.

- **I4** — `Processing/Configuration/FalconCollectorConfigurationBuilder.cs:17-25` and `:334-336`
  - **Problem:** The `CymulateUserAgent` / `ConfigureClient` addition is unrelated to both halves
    of this branch (concurrency, server-side retries), was not in the change description, changes
    the wire shape of **every** outbound Falcon request including the OAuth2 token call, and has
    no test. It rides along in a diff a reviewer is reading for concurrency. The header value
    itself parses correctly (`ParseAdd` accepts `Cymulate_Agent_v2 Cymulate-Agent/1.0` as two
    product tokens) and the `ConfigureClient` hook matches the pattern used by 15+ sibling
    collectors, so the code is fine — the problem is that it is invisible in this review surface.
  - **Suggestions:**
    1. Split it into its own commit/PR with a one-line test asserting `SessionSpec.ConfigureClient`
       sets both product tokens — the sibling collectors' builders show the shape. **Preferred.**
    2. If it must ship here, call it out in the PR description and add the test, so the header
       change is not discovered later by a CrowdStrike-side allowlist.

- **I5** — `UnitTests/.../FalconTwoPhaseFindingsTests.cs:529`, `:1706`
  - **Problem:** `FakeHttpClientFactory` and `InMemoryFalconStagingStore` were hardened for
    concurrency, but the per-test fakes inside `FalconTwoPhaseFindingsTests` were not, and they
    are the ones the HTTP layer now calls from up to 4 tasks: `spotlightFilters.Add(decoded)`
    (`:529`) is a bare `List<string>.Add`, and `spotlightCalls++` (`:1706`) is a non-atomic
    increment used as a control signal. Today `:529` happens to be safe (that test has a single
    aid batch) and `:1706` is B1 — but the pattern is now wrong by default in a 2,600-line file,
    and the next test added there inherits it.
  - **Suggestions:**
    1. If you take B1 option 1 (pin the file to degree 1), state that in the file's harness comment
       so the invariant is explicit — "these fakes are single-threaded; concurrency coverage lives
       in `FalconSpotlightConcurrencyTests`". **Preferred**, and it is then a documentation fix
       only.
    2. Otherwise convert the two sites: `ConcurrentQueue<string>` for `spotlightFilters`,
       `Interlocked.Increment(ref spotlightCalls)` with the result captured into a local for the
       `> 3` comparison.

- **I6** — `FalconSpotlightBatchPump.cs:239-240`
  - **Problem:** The pump's own doc guarantees that draining "observes every task, so a producer
    that faulted after the consumer walked away cannot resurface as an unobserved exception"
    (`:190-194`). There is one window where that is false: `FetchAsync` is started at `:239` and
    the task is not referenced anywhere until `writer.WriteAsync` succeeds. `WriteAsync` fails
    fast when the token is already cancelled — even with capacity available — so a cancellation
    landing between those two lines drops the task un-awaited. Pure cancellation is harmless (no
    exception holder), but a genuine fault racing the cancel becomes an unobserved task exception
    with no drain to catch it.
  - **Suggestion:** Use the capacity the semaphore already guarantees and take cancellation out of
    the write:
    ```csharp
    Task<FetchedAidBatch> fetch = FetchAsync(batch, windowStartUtc, cancellationToken);
    if (!writer.TryWrite(fetch))
    {
        // A slot was taken above and capacity == degree, so this cannot block; None keeps the task observable.
        await writer.WriteAsync(fetch, CancellationToken.None).ConfigureAwait(false);
    }
    ```

---

## Nits

- **N1** — `FalconSpotlightBatchPump.cs:3`
  - **Problem:** `using Cymulate.Integration.Adapters.Collectors.FalconCollector.Dtos;` is unused —
    every type the file names (`StagedAidBatch`, `DiscoverHost`, `BatchEmitStats`,
    `HostFindingsAccumulator`) resolves from `TwoPhase` or the file's own `Correlated` namespace.
  - **Suggestion:** Delete the line.

- **N2** — `Flows/Findings/FalconFindingsFlow.cs:479`; remark at `FalconSpotlightBatchPump.cs:87-91`
  - **Problem:** `DiscardedFetchedBatches = fetchedBatches - publishedBatches` counts only scrolls
    that ran to **completion**, so the batch cancelled mid-scroll is excluded. The log can read
    `DiscardedFetchedBatches=0` while a partial scroll was thrown away, and the pump's remark
    claims the resume redoes "exactly `FetchedBatches - publishedBatches` batches' worth of vendor
    calls".
  - **Suggestion:** Reword both to "completed-but-unpublished batches (excludes any batch
    interrupted mid-scroll)", or rename the field `DiscardedCompletedBatches`. Do **not** start
    counting started-but-incomplete scrolls into `FetchedBatches` — that would blur the counter the
    doc rightly protects.

- **N3** — `FalconSpotlightBatchPump.cs:143-148`; claim at `FalconCollectorConfiguration.cs:174-176`
  - **Problem:** The semaphore covers the publish as well as the fetch, so while the consumer is
    uploading a 35–50 MB object only `degree - 1` scrolls can be in flight. "A real 4x step off
    serial" overstates the steady-state speed-up at the shipped default.
  - **Suggestion:** Say "up to `degree` batches are resident, of which one is being published, so
    steady-state fetch parallelism is `degree - 1`" — the design trade (memory bound and fetch
    bound share one counter) is the right one, it just should not be advertised as 4×.

- **N4** — `Dtos/FindingsDtos/FindingsFlowRunConfig.cs:51`
  - **Problem:** The new property's remark correctly diagnoses `BatchScopedStorage`'s divergent
    default (`false` here, `true` in `FalconCollectorConfiguration`) as a live two-authority trap,
    then leaves it in place one line above.
  - **Suggestion:** Make `BatchScopedStorage` `required` too — `FalconFlowRunPreparer` already sets
    it, so the only sites the compiler will break are tests, which is precisely the point the new
    remark makes.

- **N5** — `UnitTests/.../FalconSpotlightConcurrencyTests.cs:734-751`
  - **Problem:** `ReadFindingsFlowSource` resolves the flow's source through `[CallerFilePath]` and
    throws when it is missing. That is the right call for a local run, but it hard-fails any
    pipeline that executes tests from a published artifact or a different container stage than the
    one that compiled them.
  - **Suggestion:** Link the flow file into the test project as an `EmbeddedResource`
    (`<EmbeddedResource Include="..\..\..\Collectors\FalconCollector\Flows\Findings\FalconFindingsFlow.cs" LogicalName="FalconFindingsFlow.cs" />`)
    and read it from the assembly, so the guard travels with the test binary instead of with the
    checkout.

- **N6** — `UnitTests/.../InMemoryFalconStagingStore.cs:25-31`
  - **Problem:** The new remark says "Every operation is guarded by one lock, and the recording
    lists with it", but only the *mutations* are guarded — `Reads`, `Writes`, `Deletes`,
    `DeletedPrefixes` and `WrittenContent` are still exposed as raw mutable collections, so a test
    that reads them while the flow is running can still throw. `Keys` was correctly given a
    snapshot; these were not.
  - **Suggestion:** Give them the same treatment as `Keys` — back each with a private list and
    expose `IReadOnlyList<string>` that snapshots under `_gate` — or narrow the remark to
    "mutations are guarded; the recording lists are safe to read only after the run completes".

---

## Open questions

Unproven — raised as questions rather than findings, because I could not establish the answer from
the diff or from this repo.

- Is `AdapterHttpClient` / the underlying `IHttpSession` from
  `Cymulate.IntegrationInfra.Conversation` safe for concurrent `GetStreamAsync` calls from one
  instance, and does `CredentialProviderAuthenticator`'s 401-driven force refresh coalesce when 4
  in-flight requests hit a 401 at the same moment, or does it stampede the token endpoint?
  `FalconSpotlightBatchScroller` itself is genuinely stateless per call (all scroll state is local
  — verified), so the pump's "N of them share one instance safely" claim holds for the collector's
  own code; the question is only about the substrate below it, which I cannot read from this repo.
- The session-level circuit breaker is `FailureThreshold = 3` / `BreakDuration = 300s` and the rate
  limiter is a shared token bucket (`FalconCollectorConfiguration.cs:46-62`), both tuned when this
  collector was a serial caller. At degree 4 a single bad vendor moment can produce 4 simultaneous
  failures rather than 4 sequential ones. Do you know whether that threshold counts across
  concurrent requests, and was a 300 s session-wide break at degree 4 considered — or is the intent
  to observe it in prod-eu-west first?
- `MaxConcurrentSpotlightBatches` clamps to `[1, 16]` while, per the test's own remark at
  `FalconCollectorConfigurationBuilderTests.cs:243-249`, the frozen contract for this work says
  `[1, 32]`. Which number is authoritative?
