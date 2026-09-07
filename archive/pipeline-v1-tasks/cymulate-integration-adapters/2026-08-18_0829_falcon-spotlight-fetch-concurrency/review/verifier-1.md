# Verifier pass 1 — Falcon Spotlight fetch concurrency

Verified: 2026-08-18, against the working tree of
`/Users/user/Dev/cymulate-integration-adapters-falcon-concurrency` (branch
`falcon-concurrency-and-server-side-retries-with-backoff`), baseRef `1f7a2ba6`.
Actor: `verifier`.

## 0. What I actually ran, and one thing you need to know first

**The tree moved during this pass, and the state I was briefed on was already stale.**
`FalconCollectorConfigurationBuilderTests.cs` was written at **12:40:10** — after my first build, and
after the brief that said W2 did not complete. It contains five clamp/default tests for
`maxConcurrentSpotlightBatches` that the brief did not mention. Other agents were concurrently running
`dotnet test` on this same project and on four other collector projects while I worked (three testhosts
observed at once). Two consequences:

- W2's **code** partly landed after the brief was written; W2's **report** did not (`execution_notes.md`
  still has no `## W2` section; its mtime is 12:04, before those tests existed). Treat "W2 incomplete" as
  "W2's clamp tests exist, W2's write-up and its :1679 fix do not".
- Every timing-sensitive measurement on this machine today, mine and everyone else's, was taken under CPU
  contention from parallel test runs. That matters for the flake discussion in §5. I also killed all
  running `dotnet test` processes at 12:47 to get a clean measurement — that will have aborted whatever
  another agent had in flight at that moment.

Commands and verbatim results:

    dotnet build Collectors/FalconCollector/...FalconCollector.csproj
      Build succeeded.  0 Warning(s)  0 Error(s)

    dotnet build UnitTests/.../FalconCollector.Test.csproj --no-incremental
      Build succeeded.  0 Warning(s)  0 Error(s)

    dotnet test UnitTests/.../FalconCollector.Test.csproj --no-build      (run A)
      Test Run Failed.  Total tests: 207   Passed: 206   Failed: 1   Total time: 7.1487 Minutes
      Failed ...FalconTwoPhaseFindingsTests.ResumeAfterACooperativeStop_PublishesEveryHostExactlyOnce [56 ms]

    dotnet test UnitTests/.../FalconCollector.Test.csproj --no-build      (run B)
      Test Run Failed.  Total tests: 207   Passed: 206   Failed: 1   Total time: 7.1816 Minutes
      Failed ...FalconTwoPhaseFindingsTests.ResumeAfterACooperativeStop_PublishesEveryHostExactlyOnce [22 ms]

    11 other collector test projects that consume the changed FakeHttpClientFactory:
      IsbLoadTest / InsightVm / Qualys / ServiceNowCmdb / InsightVmCloud / SentinelOne / DefenderVm /
      TenableSc / Dummy / DefenderForCloud / TenableIo  =>  all "0 Warning(s) 0 Error(s)"
      YamlAdapter.Test => FAILS TO COMPILE (CS9000 raw-string-literal errors in YamlAdapterTests.cs:492).
      Pre-existing: that file is byte-identical to baseRef (`git diff 1f7a2ba6` does not touch
      UnitTests/YamlAdapter/). Not this task's doing.

**The failure reproduced 2 out of 2 runs on the same test.** Combined with the brief's run 2, that is 3 of
4 known runs failing `ResumeAfterACooperativeStop_PublishesEveryHostExactlyOnce`. This is not a flake you
can wait out; see §5.

No writes occurred in the read-only main checkout `/Users/user/Dev/cymulate-integration-adapters`: its
`git status` shows only the operator's TenableIo/YamlAdapter work, and no file under any `Falcon*` path
has an mtime later than Aug 17 11:23.

---

## 1. Success Criteria 1–9

### SC1 — several scrolls demonstrably in flight, bounded by the configured degree — **MET**

- Bound is structural: `FalconSpotlightBatchPump.cs:148` `new SemaphoreSlim(_degree, _degree)`, acquired
  **before** the scroll starts (`:237`) and released only after the consumer has published (`:180`).
- Proven behaviourally by `FalconSpotlightConcurrencyTests.SpotlightScrollsInFlight_AreExactlyTheConfiguredDegree`
  at degrees 2 and 4 — the gate asks for `degree + 1`, is never satisfied, and `PeakInFlight` must equal
  `degree` exactly. Both passed in runs A and B.
- I independently confirmed the orchestrator's finding (b): the release at `:180` sits after the
  `yield return` at `:172`, and an async iterator does not resume past a `yield return` until the consumer
  asks for the next element — so the slot genuinely spans the publish. Peak materialised batches ≤ degree.

### SC2 — published object order, content and naming identical to sequential — **MET**

- `PublishedObjects_KeepFrozenListOrder_NamingAndContent_AtEveryDegree(1, 2, 4, 8)` asserts target paths
  `findings_000001..8`, AID order `h1..h8`, and per-object content, against a vendor fake that answers
  **later batches faster** (`ReverseSpeedVendor`) so completion order is the exact reverse of frozen order.
  Expectations are literal, not captured from a degree-1 run. Passed in both runs.
- Structural backing: what crosses the buffer is a FIFO of `Task<FetchedAidBatch>` awaited in dispatch
  order (`:151-176`); the object name is still `progressContext.CurrentPage` read on the consumer at the
  batch's turn (`FalconFindingsFlow.cs:279`).
- Scope limit worth naming: every new ordering test runs at `aidBatchSize 1` (one host per batch).
  Multi-host-per-batch ordering under concurrency is covered only incidentally, by the pre-existing
  two-phase tests now running at the default degree 4.

### SC3 — publish→checkpoint adjacency, and a test fails if an await is introduced — **MET**

- `FalconFindingsFlow.cs:300-324`: publish at `:301`, `checkpointWriter.OnBatchPublished(` at `:320`,
  nothing awaitable between (only counter increments at `:314-317`).
- `PublishAndItsCheckpoint_AreOneStep_WithNothingAwaitableBetweenThem` scans the flow source between the
  two production anchors and fails on a bare `await`; it also fails loudly if the anchors stop resolving.
- `TheAdjacencyGuard_DetectsAnInjectedAwait_AndIsNotFooledByProse` tests the guard's own scanner against
  known-good and known-bad synthetic sources. A guard with a meta-test is rarer than it should be; credit.
- `ACancelledPublishIsStillCoveredByItsCheckpoint` covers the operational consequence at degree 4.

### SC4 — degree from configuration, clamped, default documented and justified — **MET**

- Property `FalconCollectorConfiguration.cs:216`, default const `:184` (=4) with the justification in its
  remarks `:165-183`; single clamp `Math.Clamp(mc, 1, 16)` in
  `FalconCollectorConfigurationBuilder.ExtractFields:189-192`, folded at `:239-242`; straight un-clamped
  carry in `FalconFlowRunPreparer.cs:74-78`; DTO property `FindingsFlowRunConfig.cs:67`.
- Tests (landed at 12:40, after the brief): `Build_WithoutMaxConcurrentSpotlightBatches_UsesTheDocumentedDefault`,
  `..._OverridesTheDefault`, `..._BelowOne_ClampsToOne`, `..._AboveTheCeiling_ClampsToTheCeiling`,
  `..._Unparseable_KeepsTheDefault` in `FalconCollectorConfigurationBuilderTests.cs:209-291`.
- Stale artifact, not a defect: that file's remark at `:255-260` says "the frozen contract states [1, 32];
  the builder ships [1, 16]". The frozen contract in `orchestration_plan.md` says **[1, 16]** (corrected
  from 32 by the A3 research). Code and plan agree; the test comment is the only thing out of date.

### SC5 — degree 1 proven by test to reproduce sequential behaviour — **MET, with one untested claim**

- `PublishedObjects_...(1)`, `SpotlightScrollsInFlight_...(1)` (peak = 1), and
  `CheckpointShape_IsIdenticalAtEveryDegree_AndStaysFormatVersion4` (degree 1 vs 8, key-set equality).
- Orchestrator finding (a) confirmed: `FalconSpotlightBatchPump.cs:107-109` branches on `_degree == 1` into
  `PumpSequentiallyAsync:116-133`, which hands the publisher the same lazy `EmitBatchRecordsAsync`
  enumerable — no task, no channel, no semaphore, no materialisation.
- **Gap:** W1's own argument is that the point of the separate branch is restoring the *memory* profile,
  not just the order ("a rollback that restored the old ordering but not the old memory profile would not
  actually be a rollback"). Nothing tests that. No assertion observes that at degree 1 fetch and publish
  interleave, i.e. that the sequence is still lazy. A future refactor that collapsed the two branches into
  one materialising path would pass every test in this suite.

### SC6 — cancellation with N in flight specified and tested — **MET**

- Specified before implementation: `orchestration_plan.md` phase-0 item 5; implemented as described —
  `FalconFindingsFlow.cs:346-352` catch → `RecordCooperativeYield(progressContext, publishedBatches,
  lastPublishedOutputPage, pump.FetchedBatches)` at `:351`, where the fetch count is reported only and the
  recorded position remains `progressContext.CurrentPage` (`:465`).
- `CancellationWithFetchesInFlight_YieldsAtThePublishedPosition_AndResumeRedoesEveryUnpublishedBatch`
  asserts `_resume.reachedPosition == 4` after three publishes, asserts the fetched-but-unpublished set is
  **non-empty** (so the test would fail if the fan-out silently stopped happening), and asserts leg 2
  re-fetches every member of that set and that the two legs publish each host exactly once. Passed both runs.
- I confirmed the exception path empirically rather than by reading: with `Channel.TryComplete(ex)`,
  `WaitToReadAsync` rethrows the **original** exception (an `OperationCanceledException` stays an OCE, an
  `InvalidOperationException` stays itself), whereas `ReadAsync` wraps in `ChannelClosedException`. The pump
  uses `WaitToReadAsync` + `TryRead`. Probe output on runtime 8.0.28:
  `WaitToReadAsync threw: System.InvalidOperationException: MISSING STAGED PAGE` /
  `ReadAsync threw: System.Threading.Channels.ChannelClosedException / inner System.InvalidOperationException`.
  See §4.1 — this is load-bearing and unguarded.

### SC7 — peak in-flight memory bounded by the degree and stated as a formula — **MET as written**

- Formula present in `execution_notes.md` ("Peak memory"): `peak ≈ degree × S`, with a per-degree table,
  and the bound is structurally true (`:148`, `:237`, `:180`).
- **The provenance of S is overstated.** The notes call 35–50 MB "on the heaviest observed tenant". The
  cited source (`FalconCollectorConfiguration.cs:95-98`) says the opposite: at `AidBatchSize` 10 the
  heaviest observed tenant produced **90–125 MB** objects, and "4 **targets** ~35–50 MB". So S is a design
  target, not an observation. The criterion asks for a stated formula against observed batch size and gets
  a stated formula against a targeted one. Not a blocker; it is the residual risk in A6.

### SC8 — build clean with 0 warnings, and the FalconCollector test project passes in full — **NOT MET**

- Build half: MET, verbatim above, including a `--no-incremental` rebuild and 11 downstream test projects.
- Test half: **NOT MET.** 206/207 in both my runs, same test failing both times:
  `FalconTwoPhaseFindingsTests.ResumeAfterACooperativeStop_PublishesEveryHostExactlyOnce`
  (`FalconTwoPhaseFindingsTests.cs:1732`), message: *"Expected firstLegAids to contain exactly 3 items ...
  but it misses {h1, h2, h3}"* — i.e. leg 1 published **fewer than three** objects before the stop.
- Also worth flagging for suite cost: `Resume_WhenAPageVanishedMidRun_StopsThereAndReportsTheInconsistency`
  takes **3 m 30 s** (both runs), roughly half the suite's wall clock. That is the unclassified-exception
  retry ladder (30/60/120 s) described in `FalconFlowExceptionClassifier`'s own remarks. Almost certainly
  pre-existing — the missing-page `InvalidOperationException` was unclassified before this change too, and
  the probe shows the pump does not change its type — but I did not measure it at baseRef.

### SC9 — every assumption A1–A9 disposed with an actor and a citation — **MET by this document** (§2), and
written back into `assumptions.md`.

---

## 2. Assumption disposition table

Terminal. `NEVER-TESTED` is the default; `VALIDATED`/`REJECTED` appear only where a specific diff hunk,
test, command output, or research file moved them.

| id | status | citation | actor |
|----|--------|----------|-------|
| A1 | VALIDATED | Its prediction recurred: the vendor throughput belief this task rested on was wrong as stated — `research/crowdstrike-spotlight-throughput.md` §A3 / `FalconDocs/OfficialDocs/crowdstrike-auth.pdf` p42–43. Honoured in the work: `AidBatchSize` untouched (`FalconCollectorConfiguration.cs:122`, still 4) and the degree defaulted conservatively rather than derived from the vendor number. | `researcher` (evidence), `W1` (honoured) |
| A2 | VALIDATED | `FalconSpotlightConcurrencyTests.PublishedObjects_KeepFrozenListOrder_NamingAndContent_AtEveryDegree(1,2,4,8)` with `ReverseSpeedVendor` — completion order is the reverse of frozen order, and both order and per-object content are asserted. Record *identity* within a batch is not newly at risk: one batch is one sequential scroll (`FalconSpotlightBatchPump.cs:265-280`). | `W2` |
| A3 | REJECTED as written | `crowdstrike-auth.pdf` §1.8 p42–43 via `research/crowdstrike-spotlight-throughput.md` §A3: token bucket (100 req/s sustained, 6,000 burst), per-CID, pooled across every API client and endpoint — not a Spotlight allowance and not exclusively ours. | `researcher` |
| A3a | VALIDATED | Re-verified by me in source, not taken from the note: `SessionRetryDefaults.CreateTransient` (`/Users/user/Dev/IntegrationInfra/src/IntegrationInfra/Conversation/SessionRetryDefaults.cs:36-55`) never sets `RetryStatusCodes`, and the default set (`/Users/user/Dev/Cymulate.Http.Package/DefensiveToolkit/Contracts/Options/RetryOptions.cs:14-21`) includes `HttpStatusCode.TooManyRequests`. Externalisation wiring at `FalconCollectorConfiguration.cs:42-43`. No test in this task exercises a 429. | `orchestrator` (claim), `verifier` (confirmed) |
| A4 | VALIDATED | `FalconDocs/OfficialDocs/spotlight.pdf` p21 (and p19/24/29/31/34), `discover.pdf` p4/p12, via `research/crowdstrike-spotlight-throughput.md` §A4 — "Tokens expire 120 seconds after a call is made", for the exact endpoint this collector scrolls. | `researcher` |
| A4a | NEVER-TESTED | No 130-second cursor probe was run; `execution_notes.md` ("The 120s cursor and backpressure") says so itself. `Flows/SharedFlows/FalconHttpFailureClassifier.cs` is not in the diff, so the 404-status condition at `:22` is unchanged. Residual risk: if the vendor note is accurate, `SpotlightReanchors` is a silent zero and cursor pressure is unmeasurable on this leg — do not use it as evidence either way. | — |
| A5 | VALIDATED | `FalconSpotlightBatchPump.FetchAsync:265-280` materialises the batch into `List<ReadOnlyMemory<byte>>`; the degree-1 branch (`:116-133`) deliberately does not. The predicted consequence — that only materialising delivers real concurrency — is observed: `SpotlightScrollsInFlight_AreExactlyTheConfiguredDegree(2)` and `(4)` show peak in-flight equal to the degree. Records are independent arrays (`FalconCorrelatedRecord.cs:73`, `Encoding.UTF8.GetBytes`), so materialising them is safe. | `W1` (impl), `W2` (test) |
| A6 | NEVER-TESTED | Nothing measured memory. `execution_notes.md`'s "Peak memory" section is a derivation, and a formula is an argument. Its `S = 35–50 MB` is a **target** (`FalconCollectorConfiguration.cs:95-98`: "4 targets ~35–50 MB"), while the same paragraph's actual observation at `AidBatchSize` 10 was 90–125 MB. **Residual risk:** the *count* bound (≤ degree live record sets) is structurally sound; the *bytes* are not. If a heavy tenant's batch is nearer the observed class than the target, degree 4 is ~360–500 MB of live records rather than 140–200 MB, and the clamp ceiling of 16 is 1.4–2 GB — under the 8Gi container limit but capable of crossing the 70%-of-4Gi HPA memory trigger. No RSS was observed at any degree. | — |
| A7 | NEVER-TESTED | Nothing in the diff, the tests, or the research addresses whether any consumer of the published objects depends on wall-clock spacing. Concurrency compresses that spacing by design. | — |
| A8 | REJECTED | `research/internal-recon.md`, Landmines: "A8 in assumptions.md is wrong as stated" — all three TenableIo `Parallel.ForEachAsync` sites (`TenableIoVulnPhase.cs:429`, `TenableIoAssetSpoolPhase.cs:323`, `TenableIoAssetSpine.cs:282`) are unordered fan-outs over order-independent sub-items; the chunk level is a plain sequential `foreach` (`TenableIoVulnPhase.cs:215-224`). There is no ordered producer/consumer pipeline to observe or measure, running locally or otherwise. | `recon` |
| A9 | VALIDATED | Degree 1 is reachable by configuration (`Build_WithMaxConcurrentSpotlightBatchesBelowOne_ClampsToOne`, ceiling/floor clamp at `FalconCollectorConfigurationBuilder.cs:191`) and behaviourally indistinguishable in output (`PublishedObjects_...(1)`), in vendor-side concurrency (`SpotlightScrollsInFlight_...(1)`, peak 1) and in checkpoint shape (`CheckpointShape_IsIdenticalAtEveryDegree...`, degree 1 vs 8). **Gap carried into §4.4:** the memory half of "exactly today's behaviour" — that degree 1 still does not materialise — is asserted in prose only. | `W1`/`W2` |

---

## 3. Decision drift

| decision (`decisions.md`) | outcome |
|---|---|
| Fan-out around the batch loop, not inside `FalconSpotlightBatchScroller` | **As decided.** `FalconSpotlightBatchScroller.cs` is not in the diff at all; the pump wraps `FalconFrozenKeyList.EnumerateAsync` (`FalconFindingsFlow.cs:259-266`). |
| Producers materialise, consumer publishes | **Landed with a documented refinement.** True at degree > 1 (`FetchAsync:265-280`); at degree 1 the pump deliberately does *not* materialise (`:116-133`). The refinement is stated in `execution_notes.md` and is an improvement, not drift, but it means "producers materialise" is now degree-dependent. |
| Buffer bounded and small | **As decided.** `Channel.CreateBounded(_degree)` (`:151-158`) plus the semaphore, which is the actual bound. |
| Publish serial; checkpoint stays v4 | **As decided.** Publish/checkpoint/AdvancePage untouched on the consumer; `CheckpointShape_IsIdenticalAtEveryDegree_AndStaysFormatVersion4` pins the key set and `findingsFormatVersion == "4"`. |
| Degree is configuration, clamped like the other scalars | **As decided.** The wording collision W1 flagged ("not a private const") is not a breach: the const holds only the default literal. |
| `AidBatchSize` stays 4 | **As decided.** Unchanged in the diff. |
| A3 resolved → size the degree from our own economics, default 4, ceiling 16 | **As decided,** and the reasoning is carried into the shipped code comments (`FalconCollectorConfiguration.cs:165-215`) rather than living only in the task folder. |
| A4 resolved → keep the structural mitigation, do not encode 120s | **As decided.** No timer, no comparison against 120 anywhere in the pump; backpressure is applied before the scroll starts (`:237`), so no producer blocks holding a live cursor. |
| Cancellation semantics stated before implementation | **As decided,** and the implementation matches the phase-0 statement. |
| Deferred: rate-limit header reading, `BatchScopedStorage`/emitter-flag alignment, **a test pinning the User-Agent `ConfigureClient` hook** | **Still deferred.** Flagging only the third: the User-Agent change (out of this contract's scope, in scope of the overall goal) ships with no test at all. |

No decision was abandoned. No parallelism was relocated.

---

## 4. Contradictions, missing edge cases, and things nobody asked for

Ranked by what I would fix first.

### 4.1 (highest value) The dispatcher-fault exception type is load-bearing and nothing guards it

`FalconFlowExceptionClassifier.TryClassify` switches on exception **type**, and its own remarks say an
unclassified exception falls to `UnknownFlowRetryPolicy` — "an unmapped permanent fault is retried" over
30/60/120 s. Under the new design every dispatcher-side fault (`FalconFrozenKeyList.EnumerateAsync`, which
reads staged pages and can raise `ObjectNotFoundException`, `ObjectStoreAccessDeniedException`,
`ObjectStoreTransientException`) reaches the flow through the channel's completion.

The current code is **correct**: `WaitToReadAsync` rethrows the original exception unwrapped (probe output
in §1/SC6). But `ReadAsync` — the obvious "simplification" of `WaitToReadAsync` + `TryRead` — wraps
everything in `ChannelClosedException`, which is unclassified, which silently converts a non-retryable
403/404 storage fault into three blind retries and a different error code reported to the platform. That
is precisely the live incident the classifier was written for.

No test covers it. Add one: fault the frozen-key-list enumeration at degree > 1 and assert the published
`ErrorRequest.ErrorCode` is `FALCON_STORAGE_NOT_FOUND` with `IsRetryable: false`. One test pins the whole
property.

### 4.2 A producer fault at degree > 1 is untested

The pump claims a faulted scroll "surfaces to the flow's own catch at the position it belongs to, not at
whichever position happened to fail first" (`:167-170`). Nothing exercises it. The natural test: batches
1–4 in flight, batch 2's scroll throws, assert batch 1 was published and checkpointed, batch 2's failure is
what surfaces, and batches 3–4 are discarded rather than published out of order.

### 4.3 `ResumeAfterACooperativeStop_PublishesEveryHostExactlyOnce` is broken, not flaky — see §5

### 4.4 Missing coverage, smaller

- Degree 1's **laziness** (SC5 gap) — no test would notice if the two branches were merged.
- Multi-host batches (`aidBatchSize > 1`) under an explicit degree > 1 — covered only incidentally.
- The `run.yield` line's new `DiscardedFetchedBatches` arithmetic (`fetchedBatches - publishedBatches`) is
  asserted nowhere; at degree 1 `FetchedBatches` only increments if the publisher fully drains the lazy
  sequence (`:313-325`), so the two paths' counters can mean subtly different things under an early exit.

### 4.5 Things in the work the contract did not ask for (all defensible, none a breach)

- **`FindingsFlowRunConfig` is a `public` record and gained a `required` member** (`:67`). That is a
  source-breaking change to a public surface, which sits awkwardly against the behavioural-equivalence
  discipline's "unchanged public surface". In-repo blast radius is genuinely small (one src construction
  site plus two test sites; `InsightVmCloudCollector` has its own same-named type), and the reasoning —
  forcing single authority for the default, after `BatchScopedStorage` acquired two disagreeing defaults —
  is good. Worth an explicit operator ack rather than silence.
- **`FakeHttpClientFactory.RequestedUrls` changed from `List<string>` to `IReadOnlyList<string>`** plus a
  new `RequestedUrlCount`. I checked all 20 call sites across the repo: every one uses
  `Contain`/`NotContain`/`Count`, so the narrowing is source-compatible, and 11 of the 12 other consuming
  test projects build clean (the 12th, YamlAdapter.Test, was already broken at baseRef). Orchestrator
  finding (c) confirmed, and extended from 6 projects to 12.
- **`InMemoryFalconStagingStore` gained a lock** on every operation. Necessary, correct, and it changes no
  semantics; note that the public `Reads`/`Writes`/`WrittenContent` collections are still bare `List`/
  `Dictionary` — safe only because tests read them after the run.
- **Two log lines gained fields** (`run.start`, `run.yield`); the batch-published receipt and `run.receipt`
  are byte-identical. This is what the contract asked for.
- **A minor unobserved-task window**: if `writer.WriteAsync` throws at `:240` (cancellation) the `fetch`
  task created at `:239` is never observed and its slot never released. Harmless in practice — the pump is
  tearing down — but it is the one place the "nothing is left unobserved" claim at `:186-195` has a hole.

### 4.6 Process, not code

`state.json` was never updated: `currentPhase: execution`, all five steps `pending`, both workers
`pending`, `verification.status: not_started`, `verifierRun: false` — while the implementation is complete
and this is the verifier pass. `execution_notes.md` has no `## W2` section although W2's tests exist.
Whoever closes this task has no accurate machine-readable record of what happened.

---

## 5. Is the failing suite a TEST defect or an IMPLEMENTATION defect?

**Test defect — but a test defect this change caused, and it is reproducible, not flaky.** I checked it
independently rather than accepting the orchestrator's conclusion, and I disagree with the framing more
than the verdict.

The mechanism, precisely:

1. `FalconTwoPhaseFindingsTests.InitializedCollector` does **not** set `maxConcurrentSpotlightBatches`
   (`FalconTwoPhaseFindingsTests.cs`, harness near `:2437`). So every pre-existing two-phase test now runs
   at the shipped default of **4** — the concurrent path — where it used to exercise the sequential one.
   That is a real, unannounced change in what the regression suite covers.
2. That test's vendor fake cancels on the **fourth HTTP call**: `spotlightCalls++; if (spotlightCalls > 3)
   cooperativeStop.Cancel();` (`:1700-1712`). Under sequential fetch, "four HTTP calls have happened" was a
   sound proxy for "three batches have been published". Under a degree-4 fan-out the dispatcher starts four
   scrolls essentially at once, so the cancel fires while the consumer may still be publishing its first or
   second object.
3. The observed failure is exactly that: *"Expected firstLegAids to contain exactly 3 items ... but it
   misses {h1, h2, h3}"* — leg 1 published fewer than three. It reproduced in 2/2 of my runs.
4. Secondary, and worth fixing at the same time: `spotlightCalls++` is now incremented from up to four
   concurrent tasks. It is a plain non-atomic increment on a captured local — a genuine data race in the
   test double, capable of losing counts and of making the trigger fire at the wrong moment in either
   direction.

Why this is not an implementation defect: the product guarantee the test exists to protect — every host
published exactly once across two legs, nothing skipped, nothing repeated — is directly asserted at degree
4 by `CancellationWithFetchesInFlight_YieldsAtThePublishedPosition_AndResumeRedoesEveryUnpublishedBatch`,
and it passed in both runs. The old test's *assertion* is still true of the product; its *arrangement* is
no longer able to set up the situation it describes.

The fix is test-side and small: pin that test at degree 1 (it is a sequential-semantics test), or make its
cancellation trigger publish-count-based rather than HTTP-call-based, and make the counter `Interlocked`.
This is the part of W2's brief that did not land.

**On the other reported failure** — `SpotlightScrollsInFlight_AreExactlyTheConfiguredDegree(degree: 1)` —
I could not reproduce it: it passed in both of my runs, and in every other degree. I will not call it
benign, because I cannot explain it. On the sequential path only one Spotlight request can be outstanding,
so an observed peak above 1 would mean something issued a second concurrent scroll — the candidate worth
ruling out is an emitter-level publish retry re-driving the lazy record enumerable. It is also the tightest
assertion in the file (exact equality against 1). Note that three test suites were running concurrently on
this machine today; the brief's run 1 was taken under that contention. **Recommendation:** run this theory
50× in isolation before trusting it; if it reproduces, it is not a timing artifact and the retry-re-drive
hypothesis needs closing out.

**I searched for a real race in the pump and did not find one.** Ordering cannot be expressed wrongly (FIFO
of tasks awaited in dequeue order, no sequence number, no `CurrentPage` anywhere in the pump — grep
confirms zero hits); consumer exclusivity is structural (the type holds no reference that could reach
`AdapterProgressContext`, `BatchScopedStorage`, the emitter or the checkpoint writer); the scroller is
stateless per call (only four readonly fields, every cursor is a local); records are freshly allocated
arrays, so materialising them cannot alias; `BatchEmitStats` crosses the buffer with the task completion as
its happens-before edge; and the semaphore/channel pair cannot deadlock because the slot a publishing batch
holds is always released by the same consumer that is blocking the dispatcher.

---

## 6. Bottom line

The implementation is good work and does what the contract asked. Nine criteria: **eight met** (one of them,
SC7, with an overstated number rather than a wrong one), **one not met** — SC8, because the test project
does not pass in full and has not passed in full in 3 of the 4 runs anyone has recorded.

Ranked remaining work:

1. **Fix `ResumeAfterACooperativeStop_PublishesEveryHostExactlyOnce`** (§5). Until then the suite is red and
   "199/200, non-deterministic" understates it: it is 206/207, deterministic, same test.
2. **Add the dispatcher-fault classification test** (§4.1). It is the cheapest guard against the most
   expensive latent regression in this design.
3. **Add a producer-fault-at-degree>1 test** (§4.2).
4. **Either measure peak RSS at degree 4 on a heavy tenant, or restate S as a target** (A6, SC7). Do not
   raise the degree above 4 on the strength of the current formula.
5. **Write the `## W2` section and update `state.json`** (§4.6).
6. Decide explicitly about the `required` public-surface change (§4.5).
