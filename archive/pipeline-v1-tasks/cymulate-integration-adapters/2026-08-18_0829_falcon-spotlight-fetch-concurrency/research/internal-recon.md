# Internal Recon

## Durable sources read

- `CLAUDE.md` (worktree root) — layering (Conducting/Conversation/Kernel+Emission/Recovery/FaultGovernance),
  the "collector must never construct a cloud client" rule, and that `Cymulate.IntegrationInfra` is the only
  Cymulate package a collector may reference. No concurrency guidance here — it is silent on parallel
  collection, which is why this task has no existing skill to defer to.
- `ai/skills/collector-flow-patterns/SKILL.md` — states the flow's shape as of the pre-two-phase model
  (single-object-per-batch publish boundary, no planned yields, cursor re-anchor in-process). **Partially
  superseded**: it still says "no AID pre-pass, no assets stage" and describes a single-phase live scroll.
  The actual code has moved to the two-phase model below; do not trust this skill's traversal description,
  only its publish-boundary and re-anchor invariants, which still hold.
- `ai/skills/collector-recovery/SKILL.md` — the progress-anchored recovery budget (`RecoveryBudgetEvaluator`)
  is a substrate-owned mechanism orthogonal to this task: Falcon findings defers no failures through the host
  (`FalconRecoveryContinuationBuilder`/`FalconResilienceStrategyFactory` stay on `CreateTransient()`, no
  `retryableBackoff`), so introducing in-process concurrency does not touch the budget at all. Settles: no
  budget-interaction design work is needed.
- `docs/two-phase-correlation-and-the-object-store-capability.md` — **the single source of truth**, and it
  supersedes the skill above. Confirms the two-phase model, the `AidBatchSize` 250→50→10→4 history and its
  reasoning (§6, "`AidBatchSize`: 250 → 50"), the CrowdStrike ~6,000 req/min figure as *community consensus,
  not vendor-confirmed* (§"AidBatchSize" — this directly informs A3 in `assumptions.md`, which should stay
  OPEN), and the frozen-key-list / staging primitives vocabulary. Settles Q1 fully (see below) — no worker
  needs to re-derive the two-phase design.
- `Flows/Findings/TwoPhase/README.md` — one-line invariant ("never place per-page enrichment inside a scroll
  filtered/sorted on a mutable field") and the write-once/GC-not-a-cursor rules. Confirms the staging area
  read guards (buffered-read ceiling, NDJSON line cap, bounded concurrency) come from `GuardedObjectStore`,
  not a hand-rolled mirror.
- `FalconFindingsCheckpointState` doc comments (`Recovery/FalconCheckpointState.cs:126-221`) — settle the
  checkpoint version question directly: `CurrentFormatVersion = 4` (`FalconCheckpointState.cs:153`), and the
  v2/v3/v4 history is spelled out in the enclosing `<list>` (`:144-151`). The contract's "checkpoint format
  stays v4" constraint is already true today; no bump is needed for this task, only preserving it.
- `FalconDocs/CollectorDocs/06-resume-live-state-authority.md` — settles the resume-position-authority
  question (Q1/Q6) outright: **`AdapterProgressContext.AdapterState` is the single live authority on
  collector state, on every leg** (`:8`). Nothing may snapshot or checkpoint from anything else (not a
  `WorkItem`, not a leg-start photograph). For this task: whatever consumer loop drains the bounded buffer,
  its checkpoint write must read only live state as the consumer itself has advanced it — never a producer's
  own copy, never anything captured before the consumer's turn. It also states, as a hard rule and not just an
  observation, one this task must not violate by construction: **`LastCompletedStagedPage`/
  `LastCompletedBatchIndex` (the frozen-list address) and `progressContext.CurrentPage` (the dense object
  counter) are two different kinds of number and must never be compared, clamped, or assigned to one another**
  (`:154-175`, "If you are about to write `Math.Max`, `<`, or `=` between an output ordinal and `CurrentPage`,
  stop"). A buffer/ordering design that tries to use `CurrentPage` to reorder or gate concurrent producers
  would repeat exactly the category error this document exists to name.
- `FalconDocs/CollectorDocs/03-current-concerns.md` — the honest defect list. Two items interact with this
  task and are carried into Landmines below: **T9** (a resumed leg can report success having published
  nothing) and the batch-scoped-storage retrograde hazard (**T2**). Both this doc and `06-...md` also describe
  an **older, superseded checkpoint shape** in several sections — see Landmines for the specific contradictions
  with the current v4 code, which is the authority.
- `FalconDocs/CollectorDocs/05-testing.md` — settles Q4 partially: confirms the stack (xUnit + Moq +
  FluentAssertions), the regression-guard test files (`FalconCorrelatedFindingsTests`,
  `FalconRecoveryContinuationTests`, `FalconResumeRunnerTests`), and the **behavioral-equivalence discipline**
  (`:76-90`) any structural change here must hold: 0-warning build, whole suite green with no test weakened,
  unchanged public/contract surface, preserved log lines, and unchanged checkpoint round-trip. It predates
  `FalconTwoPhaseFindingsTests.cs` (2630 lines, the actual two-phase test file) and does not mention it — keep
  using that file as the primary test exemplar per the source recon, not this doc's coverage table.
- `FalconDocs/CollectorDocs/01-collection-strategy.md`, `02-decision-making.md`, `04-architecture.md` — each
  opens with an `[!IMPORTANT]` banner self-flagging the findings-flow content as superseded by the two-phase
  design doc, so their findings-flow narrative (watermark-as-resume-position, live scroll, AidBatchSize 50) is
  **historical by the docs' own admission**, not a contradiction to report. Their assets-flow content is still
  current and irrelevant to this task (constraints.md: "touch the assets flow" is out of scope).

## Files in scope

- `Flows/Findings/FalconFindingsFlow.cs` — the control-flow method (`CollectAsync`) that must gain the fan-out.
  The `await foreach` loop is at `:249-253`; everything from `:265` (`outputPage` read) through `:331`
  (page GC) is today's single-threaded per-batch body — touched by: the core worker.
- `Flows/Findings/TwoPhase/FalconFrozenKeyList.cs` — Phase 2's traversal (`EnumerateAsync`, `:80-157`).
  Read-only for this task: it already yields `StagedAidBatch` in frozen order and resumes correctly from
  `(lastCompletedStagedPage, lastCompletedBatchIndex)`. Nothing here needs to change; a fan-out just consumes
  more of its output before publishing the oldest unpublished one.
- `Flows/Findings/TwoPhase/FalconStagedHostPage.cs` — defines the `StagedAidBatch` record (`:29-34`) and its
  codec. Read-only; cited because the contract prompt's Q1 answer lives here, not in `FalconFrozenKeyList.cs`.
- `Flows/Findings/Correlated/FalconSpotlightBatchScroller.cs` — `EmitBatchRecordsAsync` (`:61-192`) is the
  per-batch cursor chain. Confirmed **stateless per call**: the only instance fields are `_http`, `_logger`,
  `_spotlightBaseUrl`, `_chunkFindingsCap` (all readonly, `:40-43`), and every mutable position
  (`seenFindingIds`, `updatedFloor`, `after`) is a local (`:73,74,80`). Safe to invoke concurrently from
  multiple producer tasks on the same scroller instance without new locking — touched by: nobody; this file
  is explicitly out of scope (constraints.md: "do not parallelise inside the Spotlight scroll").
- `Flows/Findings/Correlated/HostFindingsAccumulator.cs` — per-host accumulator seeded per batch
  (`FalconFindingsFlow.cs:267-273`). Each batch gets its own `Dictionary<string, HostFindingsAccumulator>`,
  so N concurrent batches never share one — no synchronization needed here either.
- `Processing/Configuration/FalconCollectorConfiguration.cs` / `FalconCollectorConfigurationBuilder.cs` — the
  new concurrency-degree knob's home. Touched by: the core worker (small, same PR as the flow change — see
  Disjoint Sets).
- `Recovery/FalconCheckpointState.cs`, `FalconResumeReach.cs`, `FalconResumeRunner.cs` — read-only. Confirm
  the resume position is `(StagingGenerationId, LastCompletedStagedPage, LastCompletedBatchIndex)`
  (`FalconCheckpointState.cs:167-194`), unaffected by how many batches ran concurrently, and that
  `RecordCooperativeYield` (`FalconFindingsFlow.cs:425-457`) only ever records what the (single) consumer has
  actually published — see Landmines for what concurrency changes about it.
- `UnitTests/.../FalconTwoPhaseFindingsTests.cs` — the test file to extend. Touched by: the core worker
  (tests are not separable from the implementation here — see Disjoint Sets).

## Patterns to mirror

- **Config scalar clamping** → `FalconCollectorConfigurationBuilder.cs:172-175`
  (`aidBatchSize = Math.Clamp(b, 1, 5000)`) or `:241-244` (`chunkFindingsCap`). Pattern: `TryGetInt` off the
  raw dictionary, `Math.Clamp` inline, store as `int?` in `ExtractedFields`, then fold into the typed config
  with a `cfg with { ... }` in `ApplyOptionalOverrides` (`:217-220` shows the exact fold for `AidBatchSize`).
  A new `SpotlightFetchConcurrency` (or similar) knob should mirror this exactly, including an explicit
  documented default with its own reasoning comment, matching `FalconCollectorConfiguration.cs:84-122`'s
  style for `AidBatchSize`.
- **Stateless-per-call collaborator, constructed once, called from a loop** →
  `FalconSpotlightBatchScroller` construction at `FalconFindingsFlow.cs:157`, reused for every batch. This is
  exactly the shape that already tolerates concurrent callers with zero change to the scroller itself.
- **Publish-then-checkpoint-with-nothing-awaitable-between** → `FalconFindingsFlow.cs:288-317`: the comment
  block at `:305-307` ("PUBLISH AND RECORD ARE ONE STEP") states the invariant this task's consumer must
  preserve verbatim for whichever batch it is currently draining.
- **Bounded fan-out for order-independent sub-item work** →
  `TenableIoVulnPhase.cs:429-454` (`Parallel.ForEachAsync`, `SpineLookupConcurrency = 4`, `:95`) and
  `TenableIoAssetSpoolPhase.cs:323-334` (`SpineWriteConcurrency = 32`, `:67`). This is the *only* concurrency
  shape TenableIo actually has (see Landmines — it is not the shape this task needs, but it is the exemplar
  for "how this codebase writes a bounded `Parallel.ForEachAsync`" if any sub-step needs it).

## Shared surface to freeze

- **`StagedAidBatch` record** (`FalconStagedHostPage.cs:29-34`) — produced by `FalconFrozenKeyList.EnumerateAsync`,
  consumed by the loop body. Its shape (`PageIndex`, `BatchIndex`, `PageKey`, `IsLastBatchOfPage`, `Hosts`)
  is exactly what a producer task needs to carry through to the point where it hands a materialized result
  back — freeze this as the unit of work threaded through the buffer.
- **The per-batch materialized result shape** — does not exist yet; whoever implements this defines a new
  type (e.g., "batch + records + `BatchEmitStats` + the `StagedAidBatch` it came from") that becomes the
  producer→consumer handoff contract. This is the one genuinely new piece of shared surface in this task and
  should be pinned early since tests will assert against it.
- **`BatchEmitStats`** (`HostFindingsAccumulator.cs:15-22`) — already a plain mutable counter bag populated
  during `EmitBatchRecordsAsync`. One instance per batch already (`FalconFindingsFlow.cs:275`); no sharing
  risk, but the consumer's log line at `:319-323` reads its fields after the fact and must keep doing so per
  batch, not accumulate them into a shared instance.
- **`progressContext` (`AdapterProgressContext`)** — external type from `Cymulate.Integration.Client.Contracts`
  (not vendored in this repo or in the local `IntegrationInfra` checkout; only available as a compiled NuGet
  DLL). Its thread-safety is **not verified** by this recon — treat "only the single consumer ever touches
  `progressContext`" as a constraint to enforce by construction (never pass it into a producer task), not as
  something the type itself guarantees.

## Disjoint sets available

- **None — this cannot be split across independent workers.** The fan-out, the bounded buffer, the ordered
  consumer, and the cancellation/checkpoint interaction are one control-flow method
  (`FalconFindingsFlow.CollectAsync`, specifically `:244-338`) with a single set of invariants that only make
  sense read and written together (publish/checkpoint adjacency, frozen-list order, cooperative-yield
  semantics). The config knob (`FalconCollectorConfigurationBuilder.cs`) and its tests
  (`FalconCollectorConfigurationBuilderTests.cs`) are small enough that splitting them out would create a
  false parallelism — they are a 10-line mirror of an existing pattern, not a bounded unit of work. The test
  additions to `FalconTwoPhaseFindingsTests.cs` must be written by whoever writes the implementation, because
  the new producer/consumer handoff type (see "Shared surface to freeze") does not exist yet for a second
  worker to test against independently. Recommend direct execution by one worker, not decomposition.

## Landmines

- **Doc/code contradiction — checkpoint shape.** `03-current-concerns.md` and `06-resume-live-state-authority.md`
  describe (and in `06-...md`'s "Known gap: the retrograde-page guard" section, treat as a still-live hazard,
  T2) a **v3** checkpoint shape: one sparse `LastCompletedOutputPage`/`OutputPage` ordinal that also names the
  published object, compared against `progressContext.CurrentPage` inside `BatchScopedStorage.BeginPage`. The
  current code has moved past this: `FalconFindingsCheckpointState` is v4 — the resume position is the
  two-field `(LastCompletedStagedPage, LastCompletedBatchIndex)` coordinate, and the object name (`outputPage`
  in `FalconFindingsFlow.cs:265`) is *always* read as `progressContext.CurrentPage` itself, not a
  separately-tracked ordinal — so the two values `BeginPage` would compare are, by construction, always equal.
  T2's guard-that-was-removed can no longer trip the way the doc describes, as a structural side effect of the
  v3→v4 redesign the doc predates. **Code wins per the repo's own rule** (`FalconDocs/CollectorDocs/README.md`
  ":33-34", "if they disagree, the code wins — fix the doc"); flagging rather than silently trusting the doc's
  "recorded, not guarded" framing. Report this to the operator — the doc should be corrected, but that is a
  separate task from this one.
- **Doc/code contradiction — `BatchScopedStorage` default.** `03-current-concerns.md:226` states "batchScopedStorage
  defaults to **false** and production runs with it off" as the reason T2 is "safe today." The current code
  sets the opposite default: `FalconCollectorConfiguration.cs:160` — `BatchScopedStorage { get; init; } = true;`
  — and the builder's comment (`FalconCollectorConfigurationBuilder.cs:246-251`) confirms this was a deliberate
  flip ("Armed for Falcon after the capability had run in production on Qualys and InsightVmCloud"). Combined
  with the previous point, the doc's safety argument for T2 no longer holds *for the reason the doc gives*, but
  the v3→v4 checkpoint redesign appears to have mooted the underlying hazard anyway. Either way this is a stale
  doc claim, not a live risk this task introduces — but it means whoever fixes the doc should fix both numbers
  together, not just the default flag.
- **Doc/code contradiction — `AidBatchSize` default.** `03-current-concerns.md:158-165` ("`AidBatchSize`
  default lowered to 10 in 6.2.0") is itself already one step stale: the code
  (`FalconCollectorConfiguration.cs:89-122`) documents a further drop, "10 → 4 (2026-08-16)," and the live
  default is **4**. `constraints.md`/`decisions.md` already correctly pin 4 and forbid changing it — this is
  a doc lag to report, not a planning error.
- **T9 (`03-current-concerns.md`, "KNOWN SHIPPED DEFECT") is a pre-existing defect this task's cancellation
  design should be aware of, not fix.** A resumed leg's `FalconPartialSuccessResultBuilder.TryBuild` gates
  partial success on `progressContext.CurrentPage - 1 > 0`, which is already true on a resumed leg before any
  work happens; a **non-retryable** exception before the leg's first publish then reports success having
  published nothing. Concurrency makes the triggering shape *more* reachable, not less: with N producers in
  flight, a non-retryable failure surfacing from, say, producer 3 while the consumer has not yet drained
  producer 1's result is now an ordinary early-failure shape rather than a rare one. This task does not need to
  fix T9 (out of scope, tracked separately), but `RecordCooperativeYield`'s consumer-only-counters redesign
  (see the cooperative-yield landmine above) must not paper over it or make it harder to diagnose — the
  existing rule from `06-resume-live-state-authority.md` ("assert the publish, not the status") applies
  directly to any new concurrency test: assert the actual published/checkpointed count, never just
  `result.Success`.
- **A8 in `assumptions.md` is wrong as stated, not just unverified.** "TenableIo's parallel implementation is
  running locally and observable, and can supply a measured concurrency degree" — checked every
  `Parallel.ForEachAsync` in the TenableIo correlated flow (`TenableIoVulnPhase.cs:429`,
  `TenableIoAssetSpoolPhase.cs:323`, `TenableIoAssetSpine.cs:282`, i.e. the "32/4/4" `constraints.md:19`
  refers to): all three are **unordered bounded fan-outs for order-independent sub-item work within one
  already-sequential chunk** (per-asset spine writes, per-chunk claim reads, per-uuid host lookups). The
  chunk/batch level itself — the level analogous to Falcon's aid-batch — is strictly sequential: a plain
  `foreach` over `FindNewChunkIds` in `TenableIoVulnPhase.cs:215-224`, calling `TryProcessChunkAsync` one
  chunk at a time. **There is no ordered concurrent-producer/single-consumer pipeline anywhere in this
  codebase to port.** TenableIo's "32/4/4" are reference points for *what fan-out width this codebase already
  trusts* (useful for A6's memory sizing), not a shape to mirror for the actual buffer/consumer construction.
  This should be surfaced to the operator, not silently worked around — the design will need to be built
  closer to first principles (e.g. `System.Threading.Channels`, or a bounded sliding window of `Task`s
  awaited in submission order), grep-confirmed absent from this repo (`grep -rn "Channel<" src/.../Collectors`
  returns nothing in either checkout).
- **`FakeHttpClientFactory` is not thread-safe.**
  `UnitTests/Collectors/Collectors.Tests.Infrastructure/FakeHttpClientFactory.cs:12,24,36` — `RequestedUrls`
  is a plain `List<string>`, mutated with an unguarded `.Add(...)` from `RecordingHandler.SendAsync`. A
  concurrency test that drives N producer tasks through this same fake (as `FalconTwoPhaseFindingsTests.cs`
  already does for the sequential case) will race on this list — `InvalidOperationException` (mutated during
  enumeration) or silent list corruption, intermittently. Any new concurrency test must either replace this
  with a thread-safe collection or give each producer's simulated HTTP calls a collision-free per-batch
  response source. This is a shared-infra file (`Collectors.Tests.Infrastructure`), not Falcon-owned — fixing
  it, if needed, affects every collector's tests that reuse the factory concurrently.
- **The rate limiter and the object-store read semaphore are shared, singleton, already-concurrency-aware
  gates the new fan-out will contend on — by design, not by accident.** `FalconCollectorConfiguration.cs:46-56`
  wires one `TokenBucket` (capacity 35, refill 50/s, `QueueLimit = 60`) in front of the one session all N
  producers share; `IntegrationInfra/Ingestion/GuardedObjectStore.cs:47-48,71` sizes a `SemaphoreSlim` to
  `IngestionOptions.MaxConcurrentReads` (default 4, dropping to 1 under memory pressure — see
  `GuardedObjectStore.cs:339`). Neither needs new code to be concurrency-safe, but the token bucket's
  `QueueLimit = 60` is a real ceiling on how many producers can be simultaneously blocked waiting for a
  token before requests start getting rejected outright rather than delayed — worth stating as an input to
  whatever default concurrency degree gets chosen, not just the vendor rate ceiling in A3. The object-store
  semaphore is not on the hot path per-batch (a staged page is read once per *page*, not per *batch* —
  `FalconFrozenKeyList.cs:128` — and batches from one page share the already-materialized `List<DiscoverHost>`
  in memory, `:132`), so it only matters for cross-page concurrency, not within-page fan-out.
- **`RecordCooperativeYield`'s "at most one batch abandoned" framing (`decisions.md:35-38`) needs an explicit
  redesign, not just a bigger number.** Today (`FalconFindingsFlow.cs:334-338`) the position it records
  (`progressContext.CurrentPage`) is exactly what the single consumer has published so far, because there is
  exactly one in-flight batch to discard. With N producers in flight, the consumer's own counters
  (`publishedBatches`, `lastPublishedOutputPage`) must still reflect *only what the consumer actually drained
  and checkpointed* — not what producers merely finished fetching — or the recorded "reached" position will
  overstate progress relative to what `checkpointWriter.OnBatchPublished` actually wrote. The fix is
  structural (only the consumer's own counters feed `RecordCooperativeYield`), and it must be reachable even
  when cancellation fires while producers are mid-fetch and the consumer is blocked waiting on the buffer —
  confirm the cancellation token passed to `Task.WhenAny`/buffer-read/producer tasks is the same
  `globalCancellationToken` already threaded through `CollectAsync`, so no second cancellation path needs
  inventing.
- **`AdapterProgressContext`'s thread-safety is unverified and unverifiable from this repo.** It is defined in
  `Cymulate.Integration.Client.Contracts`, an external NuGet package with no source checkout available locally
  (only `IntegrationInfra`'s `AdapterProgressContextExtensions.cs` — extension methods, not the type itself —
  was found under `/Users/user/Dev/IntegrationInfra/src/IntegrationInfra/Conducting/Collectors/Progress/`).
  Treat "only the consumer thread ever calls into it" as an implementation discipline to enforce, not a
  guarantee the type provides — never hand a reference to it into a producer task's closure.
- **`AdapterHttpClient` and the underlying session are safe to share across concurrent producers, but this is
  inherited, not designed for this task.** `IntegrationInfra/Conversation/AdapterHttpClient.cs:17-51` has no
  mutable instance state beyond immutable constructor-supplied fields; standard `HttpClient` and Polly
  resilience pipelines are documented safe for concurrent requests. This is a Conversation-layer property the
  Falcon collector benefits from without owning — do not add any Falcon-side locking around `_http`.
