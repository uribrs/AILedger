# Execution Notes

## W1

### What changed

Six files, one new.

**New — `Flows/Findings/Correlated/FalconSpotlightBatchPump.cs`.** The buffer and the pump. Exposes
`PumpedAidBatch(StagedAidBatch Batch, IAsyncEnumerable<ReadOnlyMemory<byte>> Records, BatchEmitStats Stats)` —
the producer→consumer handoff type recon flagged as the one genuinely new piece of shared surface — and
`FalconSpotlightBatchPump.RunAsync(source, windowStartUtc, ct)`, which yields every batch of the frozen key list
in its original order with up to `degree` scrolls fetched ahead.

**`Flows/Findings/FalconFindingsFlow.cs`.** The `await foreach` over `FalconFrozenKeyList.EnumerateAsync` now
runs over the pump instead; the frozen key list is passed *into* the pump. The producer-side work that used to
sit on the serial path (accumulator seeding, `EmitBatchRecordsAsync`) moved into the pump. Everything from
`outputPage` through the checkpoint write and the page GC is untouched and still runs on this one thread.
`RecordCooperativeYield` gained a fourth parameter for reporting only. `run.start` gained a
`MaxConcurrentSpotlightBatches` field; the batch-published and `run.receipt` lines are byte-identical.

**`Processing/Configuration/FalconCollectorConfiguration.cs`** and **`FalconCollectorConfigurationBuilder.cs`.**
The knob, its single default const, and its single clamp.

**`Dtos/FindingsDtos/FindingsFlowRunConfig.cs`** and **`Processing/FalconFlowRunPreparer.cs`.** The plumbing that
carries the knob to the flow — a `required` property with no default, and a straight un-clamped carry. Added after
the team lead granted these two files; see Deviations.

### Why this shape

Order is preserved **by construction, not by sorting**. What crosses the buffer is a queue of `Task<FetchedAidBatch>`
in dispatch order, and the consumer awaits them in that order. There is no reordering buffer, no sequence number,
and nothing to compare — so there is no code path on which an out-of-order publish is even expressible. This also
sidesteps the category error `06-resume-live-state-authority.md:154-175` exists to name: nothing in the pump ever
reads, compares or assigns `progressContext.CurrentPage`, because ordering is not derived from a counter.

Consumer exclusivity is likewise structural. A producer's only inputs are a `StagedAidBatch` and a `DateTime`;
its only output is bytes plus counters. `AdapterProgressContext`, `BatchScopedStorage`, the publish, the
checkpoint and `AdvancePage` are never in scope inside the pump — not by discipline but because the type has no
reference to reach them with. Recon could not verify `AdapterProgressContext`'s thread-safety (external package,
no local source), so this had to be a property of the shape.

**Degree 1 takes a separate branch that is the pre-concurrency code path itself**, not a re-creation of it: the
publisher gets the same lazy, pull-driven `EmitBatchRecordsAsync` enumerable it always had, with no
materialisation, no task, no channel and no semaphore. This matters more than it looks. `AidBatchSize` was driven
250 → 50 → 10 → 4 by *memory and object-size incidents*; a rollback that restored the old ordering but not the old
memory profile would not actually be a rollback. Degree 1 restores both.

### Peak memory

    peak live record bytes  ≈  degree × S        (degree > 1)
    peak live record bytes  ≈  unchanged          (degree == 1)

where `S` is one aid batch's materialised record set, which is the published object size — the quantity
`AidBatchSize` was lowered to 4 to control. `S ≈ 35–50 MB` on the heaviest observed tenant
(`FalconCollectorConfiguration.AidBatchSize` remarks).

It is exactly `degree`, not `degree + buffer capacity`: a batch holds its semaphore slot from *before* its scroll
starts until *after* the consumer has published it, so a slot covers the batch in every state it can be in
(scrolling, fetched-and-queued, being published). The channel capacity is `degree` as well, which makes the same
bound visible at the type level and means the write never actually blocks — it is an assertion, not a second
mechanism.

| degree | live records at S = 35–50 MB |
|--------|------------------------------|
| 1      | pre-concurrency profile (partial accumulators only) |
| 4 (default) | ~140–200 MB |
| 8      | ~280–400 MB |
| 16 (clamp ceiling) | ~560–800 MB |

The clamp ceiling of 16 exists to stop an operator typo (160 for 16) becoming a memory incident, not as a
recommendation. **The two knobs multiply**, which is the sizing fact to carry forward: anyone raising
`AidBatchSize` back up must divide the degree, and vice versa.

### The default and where the const lives

`FalconCollectorConfiguration.DefaultMaxConcurrentSpotlightBatches`, a `private const int` immediately above the
`MaxConcurrentSpotlightBatches` property in `Processing/Configuration/FalconCollectorConfiguration.cs`. It is the
only literal for this knob anywhere in the collector: the property reads it, the builder clamps around it without
restating it, and the flow-facing DTO is `required` precisely so it cannot acquire a second one.

**Value 4 — researched and final**, superseding the placeholder this section originally carried. Clamped
`[1, 16]` in `FalconCollectorConfigurationBuilder.ExtractFields` under the key `maxConcurrentSpotlightBatches`,
mirroring the `aidBatchSize` idiom, folded via `cfg with { ... }` in `ApplyOptionalOverrides`. That is the ONLY
clamp; the preparer carries the already-valid number through untouched.

The basis is deliberately **our own pipeline economics, not a fraction of a vendor rate**. The ~6,000 req/min
figure is real but is a per-CID pool shared across every API client and every endpoint the customer has
registered — not a Spotlight allowance, not exclusively ours, and with rate-limit header reading out of scope by
contract its occupancy is invisible to us. A degree derived from it would be arithmetic over an unknown presented
as a measurement. What justifies 4: a real 4x step off serial, memory bounded at four materialised batches, and
proximity to 1 so the degree-1 rollback is a small delta rather than a cliff. There is no documented vendor
concurrency limit at any tier; the only vendor-side statement is a CrowdStrike maintainer's warning that async
processing makes hitting the rate-limit boundary much more likely — which argues for a small number rather than a
computed one. Detail: `research/crowdstrike-spotlight-throughput.md`.

### Cancellation, as implemented

The token is the flow's existing `globalCancellationToken`; no second cancellation path was invented. The pump
links a `producerCts` off it for teardown.

On cancellation the dispatcher's enumeration/`WaitAsync`/`WriteAsync` throw, in-flight fetches observe
`producerCts`, and the consumer's `await fetch` (or `WaitToReadAsync`) throws `OperationCanceledException`, which
propagates out of the pump into the flow's existing single `catch` → `RecordCooperativeYield` → rethrow. Unchanged
path, unchanged classifier outcome.

**The recorded position is the consumer's and only the consumer's.** `RecordCooperativeYield` still records
`progressContext.CurrentPage`, and `publishedBatches` / `lastPublishedOutputPage` are still incremented only after
a publish returns. `pump.FetchedBatches` is passed in **for the log line only** and is documented at its
definition as never usable as a position — feeding it into one would claim progress for objects that do not exist
and silently skip the batches it covered.

**No fetched-but-unpublished batch is skipped, and this follows from the position rather than from bookkeeping.**
A batch that was never published never moved the checkpoint, so the resume coordinate
`(lastCompletedStagedPage, lastCompletedBatchIndex)` is the last *published* batch;
`FalconFrozenKeyList.EnumerateAsync` resumes at the next batch after it and walks forward over all of them. What
concurrency changed is only the *count* discarded — up to `degree` rather than one — which is now visible as
`FetchedBatchesThisLeg` and `DiscardedFetchedBatches` on the `run.yield` line. Checkpoint format is untouched: v4,
no new keys.

Teardown observes everything: the `finally` cancels `producerCts` and then awaits the dispatcher and every task
still queued, so no scroll outlives the flow and no faulted producer resurfaces as an unobserved task exception.
One deliberate ordering choice: a dispatcher fault (the missing-staged-page `InvalidOperationException` is the
fatal one) completes the reader *with* the fault, but already-queued batches are drained and published first —
completed work is not thrown away to report the error a few seconds sooner. It stays fatal.

### The 120s cursor and backpressure

**A producer is never blocked while holding a live cursor**, because backpressure is applied *before* a scroll
starts, not on its way out: the dispatcher acquires the semaphore slot and only then calls `FetchAsync`. Once a
scroll has begun it runs to completion at its own pace and hands back a finished result; it never waits on the
publisher mid-chain. The alternative shape — start every scroll and block it on a full output buffer — would park
live cursors for exactly as long as the publisher is slow, which is precisely when they would expire and force
re-anchors. So the buffer design contributes nothing to `after`-token expiry.

**120s is solid; it is still not in the code.** The figure is vendor-documented for this exact endpoint
(spotlight.pdf p21), so it is stated here without hedging. It is nonetheless deliberately absent from the
implementation: no timer, no budget, no comparison against 120 anywhere in the pump. The mitigation is structural
("never hold a live cursor while blocked"), which holds at whatever the lifetime is and keeps holding it if the
vendor changes it. Encoding the number would convert a documented figure into a load-bearing constant for no
gain.

**Which way the net moves: down, and I think clearly so — but the claim is mechanistic, not measured.**

Today the scroll is pull-driven by the publisher, so a batch's `after` token ages across upload time: between two
Spotlight page fetches the publisher is streaming already-yielded records into a 35–50 MB object. In this design
the producer scrolls to completion with no publish interleaved, so the inter-page gap loses upload time entirely
and contains only the HTTP round trip, rate-limiter wait, and parse/accumulate work. What is added is contention
among at most `degree` producers for the shared TokenBucket and for CPU. Those are not comparable quantities: at
degree 4, four concurrent acquisitions against a bucket refilling at 50/s cost on the order of tens of
milliseconds even from empty, against uploads of tens of megabytes. Buffer-slot waiting adds nothing at all,
because a slot is taken *before* the scroll starts — the one asymmetry the design is built around. So per-batch
cursor pressure should drop, materially, and degree 1 is unchanged from today because it is the same code path.

The honest caveats: neither side is measured, so this is a mechanism argument, not a benchmark; and it holds for
the *inter-page gap*, not for total wall-clock per batch, which may well rise slightly under contention — those
are different quantities and only the first is what a cursor lifetime applies to. The one way it could invert is
CPU saturation: enough concurrent JSON parsing to delay thread-pool scheduling would lengthen inter-page gaps
directly. Unlikely at degree 4, and it is the mechanism a higher degree would trip first.

**Correction to an earlier claim in this note, because it affects what a reviewer would rely on.** I wrote that
`stats.SpotlightReanchors` makes a cursor regression observable without new instrumentation. That is not safe to
rely on. `FalconHttpFailureClassifier.cs:22` detects expiry with `response.StatusCode == HttpStatusCode.NotFound
&& UrlHasAfterCursor(url)`, and spotlight.pdf p19 states the 404 for this API "displays with a 200 OK header and
the 404 code under `errors` in the response body". If that note is accurate for this endpoint, the condition
never becomes true on the Spotlight leg, `FalconCursorExpiredException` is never thrown, the re-anchor at
`FalconSpotlightBatchScroller.cs:99` never fires, and `SpotlightReanchors` is a **silent zero** rather than a
signal. A zero there would then mean "not detected", not "did not happen".

Inferred, not observed: on that path a 200 body carrying no `resources` and no `after` token would end the scroll
via the normal `after is null` exit, and the batch would flush its accumulators with `isLastChunk: true` — i.e.
truncation presented as a complete batch. That is a pre-existing defect, out of scope by instruction, and
untouched here: no body-sniffing branch was added and the classifier was not modified. It is recorded because it
changes how this concurrency change should be validated — do not use a zero reanchor count as evidence that
cursor pressure is fine. It needs a live probe to settle.

### TokenBucket interaction, and the 429 path

One shared bucket in front of the one session all producers share:
capacity 35, refill 50/s, `QueueLimit = 60` (`FalconCollectorConfiguration.cs:46-56`).

Each producer's scroll is a cursor chain — one request outstanding at a time — so **degree N means at most N
concurrent acquisitions**. Against `QueueLimit = 60`, even the clamp ceiling of 16 uses about a quarter of the
queue and the default of 4 under 7%. The fan-out cannot by itself push the bucket into rejecting requests
outright rather than delaying them. That is the interaction worth stating, because the failure mode when a queue
limit *is* exceeded is a hard rejection, not backpressure.

Note what this claim is and is not: it is about **our own limiter**, whose parameters we can read. It is
deliberately not paired with a "headroom against 6,000/min" claim, because that pool is shared per-CID and its
occupancy is invisible to us (see the default's basis above). Local queue safety is measurable; vendor headroom
is not.

**No 429 handling was added, deliberately.** It already exists one layer down and this change does not defeat it:
`SessionRetryDefaults.CreateTransient` does not override `RetryStatusCodes`, so the default set applies and it
includes `TooManyRequests`. A 429 on a Spotlight page is retried by the HTTP path rather than surfacing as a
batch failure; a `Retry-After` under 60s is absorbed in-process and one over 60s externalises to a host-scheduled
wait (`ExternalizeServerSuggestedDelays = true`, `MinExternalizedServerSuggestedDelay = 60s`,
`FalconCollectorConfiguration.cs:42-43`). Concurrency raises the odds of touching that path; it does not change
what the path does.

### The three hard rules, checked against the built design

**1. Never relate the two number kinds** (`06-resume-live-state-authority.md:154-175`). Not violated, and not
avoided by care — the design has no reason to reach for `CurrentPage` in the first place. Ordering is the dispatch
order of the frozen key list, carried as a FIFO queue of tasks; there is no sequence number, no comparison and no
sort key anywhere in the pump. `CurrentPage` is read exactly once, on the consumer, at the batch's turn, as the
name of the object about to be published — never as a position, never to gate or sequence a producer. Grep
`FalconSpotlightBatchPump.cs` for `CurrentPage`: no hits, because the type has no reference that could reach it.

**2. `AdapterState` is the single live authority.** No producer snapshots collector state. What crosses the
buffer is the frozen-list address (`StagedAidBatch`), the records, and `BatchEmitStats`. The consumer reads
everything else live as it advances it.

One judgement call to record explicitly rather than leave implicit: `stats.FindingsEmitted` crosses the buffer and
does reach `checkpointWriter.OnBatchPublished`. That is **not** a state snapshot — it is a per-batch measurement
of the fetch itself, identical whether fetched serially or concurrently, and it was produced during the scroll in
the sequential code too. The checkpoint-relevant *accumulations* (`totalFindings`, `totalAssetsEmitted`) are
consumer-owned: they are summed on the consumer, in publish order, from batches the consumer actually published.
No producer contributes to them and none can. If the verifier reads rule 2 as covering per-batch counters too,
say so and I will move the counting, but I read it as covering position/`AdapterState`, which nothing here
touches.

**3. Behavioural equivalence.** 0 warnings, unchanged public surface, unchanged checkpoint round-trip (v4, no new
keys). The `Falcon correlated findings batch published` receipt line is **byte-identical** — not reshaped, not
reordered, no fields removed. Two log lines gained fields only: `run.start` gained
`MaxConcurrentSpotlightBatches`, and `run.yield` gained `FetchedBatchesThisLeg` / `DiscardedFetchedBatches`.
`run.receipt` is untouched.

Doc/code drift noted and deliberately NOT "fixed": CollectorDocs still describe checkpoint v3,
`BatchScopedStorage` defaulting false, and `AidBatchSize` 10. Code wins on all three and was left alone.

### Deviations and things the reviewer should look at

- **`MaxConcurrentSpotlightBatches` had to be plumbed through `FindingsFlowRunConfig`.** The flow never receives a
  `FalconCollectorConfiguration`; every knob it reads arrives via the DTO built in
  `FalconFlowRunPreparer.PrepareForFindings`. Raised to the team lead rather than taken unilaterally, and granted.
  **The surface tests must target is therefore `FindingsFlowRunConfig.MaxConcurrentSpotlightBatches`**, not the
  collector configuration.
- **The DTO property is `required` with no default**, on the team lead's call, so `FalconCollectorConfiguration`
  holds the single default. This is a live defect one property away: `FindingsFlowRunConfig.BatchScopedStorage`
  defaults `false` while `FalconCollectorConfiguration.BatchScopedStorage` defaults `true` — two defaults for one
  knob, disagreeing today. `required` makes the compiler prevent a third instance of that pattern.
- **Blast radius of `required` is smaller than estimated.** The plan assumed 2 src construction sites; there is
  **one**. The other hit (`InsightVmCloudCollector/Processing/InsightVmCloudFlowRunPreparer.cs:59`) constructs a
  *different* `FindingsFlowRunConfig` — InsightVmCloud has its own type in its own namespace
  (`Collectors/InsightVmCloudCollector/Dtos/FindingsDtos/FindingsFlowRunConfig.cs`). No cross-collector impact.
  Remaining sites are the 2 test files in W2's set.
- **At degree > 1 the upload no longer interleaves with the scroll.** Records are materialised first, then
  published. Order, content, object naming and `outputPage` numbering are identical; what changes is the *timing*
  of uploads relative to fetches, and the memory profile. Forced by the materialise decision in `decisions.md`,
  and the reason degree 1 keeps its own branch.
- `constraints.md` says the degree must not be "a private const". It is not — it is configuration, clamped in
  `ExtractFields` and overridable per integration. The named const holds only the *default literal*, per frozen
  surface item 1. Flagging the wording collision so the verifier does not read it as a breach.

---

## W2 — concurrency contract tests and the test-infra race

Scope: `UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.FalconCollector.Test/**` plus one shared
file (see "Boundary crossed", below). No production code was written by W2.

### 1. The blocking test-infra race, fixed

`Collectors.Tests.Infrastructure/FakeHttpClientFactory.cs` recorded requests by appending to a bare
`List<string>` from `RecordingHandler.SendAsync`. Under any concurrent fetch that races two ways — a torn list
grow silently loses entries, and enumerating while another task appends throws `InvalidOperationException`
intermittently — so every concurrency assertion built on `RequestedUrls` was worthless before it was read.

- Backing store is now a `ConcurrentQueue<string>`; `RequestedUrls` is `IReadOnlyList<string>` handing back a
  fresh snapshot per read, so it is safe to enumerate mid-flight. `RequestedUrlCount` added for count-only reads.
- **Which** URLs were requested stays exact and assertable; **order** is explicitly documented as
  non-deterministic once more than one request can be in flight.
- **No existing test asserted request order.** All 20 call sites across 6 test projects use
  `Contain`/`NotContain` predicates or `.Count`; none uses an indexer or a sequence `Equal`. The type change is
  therefore source-compatible, and all ten collector test projects that reference the fake still build.
  Regression-run: Qualys 27/27, IsbLoadTest 7/7, InsightVmCloud 19/19 green.

`InMemoryFalconStagingStore` (Falcon-owned) had the same class of problem — `SortedDictionary` plus four bare
recording `List`s. Every operation is now under one lock; `ListAsync` snapshots under the lock and yields
outside it, because an iterator cannot hold a lock across its awaits. Semantics are unchanged. The point is not
to make concurrent store access *correct* — it is that a double which corrupts under the violation it is meant
to detect reports a crash instead of an assertion failure.

### 2. Tests added, and the criterion each one proves

New files: `FalconConcurrencyHarness.cs` (fakes, seeding, the publish/checkpoint timeline) and
`FalconSpotlightConcurrencyTests.cs` (13 tests). A separate file rather than additions to
`FalconTwoPhaseFindingsTests.cs`, so two authors were never editing one 2,600-line file.

| Test | Proves |
|---|---|
| `PublishedObjects_KeepFrozenListOrder_NamingAndContent_AtEveryDegree` (degrees 1/2/4/8) | **SC 2** — order, content, naming, `outputPage` numbering identical at every degree; at degree 1, the output half of **SC 5** |
| `SpotlightScrollsInFlight_AreExactlyTheConfiguredDegree` (degrees 1/2/4) | **SC 1** — fan-out is real and bounded; at degree 1, the concurrency half of **SC 5** |
| `Publish_Checkpoint_AndPageAdvance_NeverOverlap_AndFollowEachPublishImmediately` | Consumer exclusivity — publish/checkpoint/`AdvancePage` on one consumer, never overlapping |
| `PublishAndItsCheckpoint_AreOneStep_WithNothingAwaitableBetweenThem` | **SC 3**, literally |
| `TheAdjacencyGuard_DetectsAnInjectedAwait_AndIsNotFooledByProse` | that the SC 3 guard is armed, not decorative |
| `ACancelledPublishIsStillCoveredByItsCheckpoint` | **SC 3**'s operational consequence — no object exists outside the recorded position |
| `CancellationWithFetchesInFlight_YieldsAtThePublishedPosition_AndResumeRedoesEveryUnpublishedBatch` | **SC 6** — yield position reflects only consumer publishes; every fetched-but-unpublished batch is re-done, none skipped |
| `CheckpointShape_IsIdenticalAtEveryDegree_AndStaysFormatVersion4` | checkpoint stays v4, no new keys |
| 6 tests in `FalconCollectorConfigurationBuilderTests` (default / override / low clamp / high clamp / unparseable) | **SC 4** — degree read from configuration and clamped |

Two design points worth keeping:

- **Every ordering test makes the vendor answer LATER batches FASTER** (`ReverseSpeedVendor`: h1 slowest, hN
  fastest). An implementation that published on completion order produces the exact reverse of the expected
  output rather than accidentally-right output. A concurrency ordering test whose fake responds at uniform speed
  proves nothing.
- **The in-flight measurement asks the gate for `degree + 1`.** The gate is then never satisfied, holds every
  scroll until its timeout, and the peak occupancy at that moment is exactly what the implementation is willing
  to run at once — both bounds from one measurement. Too low means no fan-out; too high means the bound is not
  enforced.

### 3. SC 3 — what is and is not provable

Success Criterion 3 asks for a test that fails if an `await` is introduced between publish and checkpoint.
**No black-box observation can see one.** With a single consumer there is no second publisher to interleave, so
an event-order test passes straight over the regression it claims to guard. It is therefore covered twice, and
the split is deliberate:

- `PublishAndItsCheckpoint_AreOneStep_...` reads `FalconFindingsFlow.cs` (located from `[CallerFilePath]`,
  anchored on the identifiers `PublishFindingsUtf8PageAsync` and `OnBatchPublished(`, comments stripped by a
  small scanner so the invariant's own comment "Nothing awaitable" does not register). It **fails** if the
  anchors go missing rather than passing vacuously, and survives the pair being extracted into a helper method.
- `TheAdjacencyGuard_DetectsAnInjectedAwait_...` drives that same scanner over synthetic sources with known
  answers, so the guard's machinery is itself under test.
- `ACancelledPublishIsStillCoveredByItsCheckpoint` covers the harmful consequence behaviourally, but only for an
  await that observes the cancellation token; a bare `Task.Yield()` slips past it and past every behavioural
  test that could be written. That is the gap the source guard exists to fill.

### 4. Verified against the landed implementation

W1's work landed mid-task, so these tests ran against it rather than being handed over untested.

- `dotnet build` on the FalconCollector test project: **0 errors, 0 warnings**.
- New tests: **13/13 green** (`FalconSpotlightConcurrencyTests`), and 29/29 with the builder clamp tests.
- Full project: **206 passed, 1 failed, 207 total** — the single failure is the pre-existing test described
  below, not a new one.

### 5. Findings for the operator

- ~~Clamp range disagrees with the frozen contract.~~ **Resolved.** The contract was corrected to `[1, 16]`,
  which is what the builder ships. Boundary coverage is now two-sided (`16 -> 16`, `17 -> 16`, `999 -> 16`,
  `0 -> 1`, `-1 -> 1`, unparseable -> default) because an off-by-one in a clamp is invisible from either side
  alone: a test that only checks 999 passes just as happily against a ceiling of 15.
- **`FindingsFlowRunConfig.MaxConcurrentSpotlightBatches` being `required` broke two existing test files**
  (`FalconTwoPhaseFindingsTests.cs:2523`, `FalconDeferralPositionObservationTests.cs:314`). Both fixed with
  `MaxConcurrentSpotlightBatches = 1` and a one-line comment saying why 1 — neither test is about the fan-out.
- **One existing test genuinely breaks under concurrency, and its intent was NOT rewritten.** See below.
- **The full Falcon suite takes 7m7s**, and did so before any W2 file existed (7m8s with the new class excluded).
  Not introduced here, but worth someone's attention.
- `ServiceNowCmdbCollectorTests.ProcessAsync_Assets_WithFilter_ReturnsFlowNotSupported_AndPublishesFailure`
  fails identically with the `FakeHttpClientFactory` change stashed — pre-existing and environment-related
  (3m30s, a network timeout), not caused by the shared-fake change.

### 6. The existing test that concurrency breaks

`FalconTwoPhaseFindingsTests.ResumeAfterACooperativeStop_PublishesEveryHostExactlyOnce` (:1679), failing at
:1732:

```
Expected firstLegAids to contain exactly 3 items in any order ... but it misses {"h1", "h2", "h3"}
```

**Why.** The test triggers its cancellation from the HTTP fake, counting *vendor requests*
(`if (spotlightCalls > 3) cooperativeStop.Cancel()`), and then asserts on *published batches*. Sequentially those
two were the same number. With a fan-out they are not: the 4th scroll is issued while batches 1–3 are still
being drained, so the stop lands before three objects are published and the first leg publishes fewer than three.

**This is the test's trigger being stale, not its intent.** The property it asserts — every staged host published
exactly once across the two legs, nothing re-collected, nothing skipped — is still exactly right, and is the same
property `CancellationWithFetchesInFlight_...` now asserts at degree 4. **W2 did not touch it**, deliberately:
re-triggering it belongs to whoever owns that file, and the honest repair is to drive the cancellation from a
*publish* count (as the new test does via `OnCheckpoint`) rather than from a request count, leaving every
assertion below it unchanged.

### 7. Boundary crossed, and why

`FakeHttpClientFactory.cs` lives in `Collectors.Tests.Infrastructure`, **not** in the file set W2 was assigned
(`.../FalconCollector.Test/**`), though the task described it as in-set. It was changed anyway: it is test-only
code, no other worker owns it, the whole task depends on it, and every consuming project was rebuilt and the
three with the most `RequestedUrls` assertions re-run green. Flagged rather than done silently.

The six `FalconCollectorConfigurationBuilderTests` additions are one step outside the "contract points 2–7"
brief. They were added because SC 4 otherwise had **zero** coverage anywhere in the repo, the file is in-set, and
W1's config work had already landed so there was no collision risk.

---

## W2 — second pass: the flake, the resume test, and the named rules

### 8. The degree-1 flake was the instrument, not the pump

`SpotlightScrollsInFlight_AreExactlyTheConfiguredDegree(degree: 1)` failed one run in two. Cause found and
fixed; it was never an implementation defect.

`SpotlightGate` kept counting occupancy **after** its measurement window had closed. Once the gate opens, a
scroll's exit runs on an asynchronous continuation (`RunContinuationsAsynchronously`), so scroll N's decrement
could still be queued when scroll N+1's increment ran — the counter read 2 where only one scroll had ever been
open. Pure exit-lag, and it only showed at degree 1 because that is the only degree whose expected peak is small
enough for one unit of lag to break the assertion.

The fix makes the instrument exact rather than loosening the assertion: the window closes for good the moment
the gate opens, entrants after that pass through **uncounted**, and the peak is frozen. While the window is
open, every entrant is both counted and blocked, so "occupancy" and "in flight" are the same statement.

**Verified: 12 consecutive runs of all three degrees, 12/12 green.**

Ruled out along the way, since they were the other plausible explanations: the counter only ever sees Spotlight
scroll URLs (`limit=50`) — auth and the two `limit=1` access probes are answered before the route is consulted;
and the fake returns an empty `meta.pagination`, so one scroll is one request and multi-page scrolls cannot be
mistaken for concurrency.

### 9. `ResumeAfterACooperativeStop_PublishesEveryHostExactlyOnce` — repaired, intent kept

Now a `[Theory]` over degrees **1 / 2 / 4**, so resume-exactness is proven under fan-out rather than only under
sequential timing. Three changes, and the reasoning for each:

1. **The trigger counts publishes, not vendor requests.** The old trigger (`spotlightCalls > 3`) and the old
   assertion ("exactly h1,h2,h3") were the same number only while fetching was sequential. The consumer is serial
   at every degree, so "three objects exist" is a fact the fan-out cannot perturb.
2. **Every expectation is derived from what the first leg actually published**, not hardcoded. The contract
   property is that the first leg publishes a **contiguous prefix of the frozen list, in frozen order** — how far
   it gets is a timing detail of the fan-out. Both the "exactly 3" number and the downstream `h4,h5,h6` /
   `findings_000004..6` literals were sequential-timing artifacts and are gone; the end-to-end invariant (every
   host published exactly once across both legs) is untouched and is what the test still exists for.
3. **Twenty staged pages, not six, and the count is load-bearing.** First attempt kept six and still flaked — at
   degree 2 *all six* scrolls had already been issued by the time the third object was published, so a stop armed
   to fire "from the next scroll" never landed and the leg completed all six. A producer may run ahead by the
   degree in flight plus the degree buffered; twenty leaves the fan-out room to run ahead and still have work in
   front of it. **Verified: 15 consecutive runs, 15/15 green.**

The mid-scroll character of the interruption — the documented point of the test, since that is where partial
per-host accumulators exist — is preserved: the token still trips inside a Spotlight scroll, not at a batch
boundary.

`InitializedCollector` gained an optional `maxConcurrentSpotlightBatches`. Additive: every existing call site is
unchanged and keeps the product default, and only tests that are *about* the degree state one.

### 10. Assert the publish, not the status

`AssertSucceeded` is gone. `AssertPublished(result, capture, expectedObjects)` asserts the object count and
reports the run's status only inside the failure message. This is the T9 hazard encoded at the helper level: a
resumed leg's partial-success gate is satisfied before any work runs, so a leg that fails before its first
publish reports success having published nothing — and with producers running ahead of an undrained consumer that
shape is ordinary, not exotic. Every call site now has to state how many objects it expects, so a test cannot
accidentally assert nothing.

### 11. New: the two kinds of number stay separate

`TheFrozenListAddress_AndTheObjectCounter_StayIndependent_UnderAFanOut` (degrees 1 and 4) encodes
`06-resume-live-state-authority.md`:154-175 — the frozen-list address and `progressContext.CurrentPage` must
never be compared, clamped, or assigned to one another.

It forces the two numbers **forty-nine apart**: the host counter is restored at 50 while the frozen-list position
sits at page 0 of 6. Any reconciliation is then caught by whichever direction it took — naming objects from the
address restarts them near 1 and overwrites published work; clamping the address to the counter seeks past the
end of a six-page list and publishes nothing. Run under a fan-out deliberately: this is the invariant a
concurrency refactor is most likely to break while looking correct, because a producer that wants to know "which
object number will mine be?" has only the wrong number available to it — the right one is the consumer's, and it
does not exist yet.

### 12. Two things deliberately NOT tested

- **No 429 test.** A 429 is retried by the HTTP layer (`SessionRetryDefaults.CreateTransient` does not override
  `RetryStatusCodes`; the default set includes `TooManyRequests`). A "429 fails the batch" test would encode the
  wrong contract, and a "429 is retried" test would be testing `Cymulate.Http.Package`, not this collector.
- **No cursor-lifetime test, and it is not reachable from the seams available.** The structural property —
  a producer does not sit holding a live cursor while blocked on a full buffer — needs the buffer's *saturation
  state* observable, and nothing exposes it. What is observable is HTTP requests and publishes, and every proxy
  built from those is a timing inference: "no scroll was in flight while the consumer was slow" is true for
  uninteresting reasons most runs and false occasionally, which is a flaky test asserting a coincidence. No 120s
  timer is asserted anywhere either, per the decision that the mitigation stays structural. Stated plainly rather
  than approximated.

### 13. Surface note — these are collector-level tests, not `CollectAsync`-level

The instruction to set the degree on `FindingsFlowRunConfig` rather than `FalconCollectorConfiguration` assumes
tests that construct the DTO and call `FalconFindingsFlow.CollectAsync` directly. **These are not those tests.**
They drive `FalconCollector.ProcessAsync`/`ResumeAsync` end to end, mirroring `FalconTwoPhaseFindingsTests`,
because contract points 5-7 — cancellation, two-leg resume, checkpoint persistence, `RecordCooperativeYield` —
only exist on that path. `CollectAsync` cannot be handed a checkpoint or produce a resumed leg, so retargeting
would delete the majority of the coverage.

The degree therefore travels its real production route: config dictionary -> builder (clamp) ->
`FalconFlowRunPreparer` -> DTO -> flow. That the degree genuinely arrives is not assumed —
`SpotlightScrollsInFlight_AreExactlyTheConfiguredDegree` measures it at the vendor boundary and would fail if the
wiring dropped it. Clamp/boundary behaviour is tested where instructed, against `FalconCollectorConfiguration`
through the builder. Nothing asserts that the DTO clamps or defaults.

### 14. Shared `FakeHttpClientFactory` — is it strictly additive?

**Behaviourally yes; at the type level there is one difference, disclosed rather than glossed.**

- Recording is now thread-safe. For a single-threaded caller — which is all eleven other collector test
  projects — the recorded contents and their order are byte-identical to before.
- `RequestedUrls` changed from `List<string>` to `IReadOnlyList<string>`, and each read returns a **snapshot**.
  Two consequences no current caller can observe: a caller could no longer `.Add()` to it (none does, and none
  should), and a caller holding a reference across further requests would no longer see later additions (none
  does — every call site reads it at assertion time). All 20 call sites across 6 projects use
  `Contain`/`NotContain` predicates or `.Count`; none uses an indexer or a sequence `Equal`.
- Verified: all ten other collector test projects build; Qualys 27/27, IsbLoadTest 7/7, InsightVmCloud 19/19
  green. `ServiceNowCmdb` has one failure that reproduces **identically with this change stashed** — pre-existing
  and environment-related (a 3m30s network timeout, `ADAPTER_EXCEPTION` vs `FLOW_NOT_SUPPORTED`), not caused here.

### 15. State after the second pass

- Build: **0 errors, 0 warnings.**
- Falcon test project: **213 passed, 0 failed, 213 total** — the first fully green full run.
- Targeted stability: in-flight degree test 12/12, repaired resume theory 15/15.

---

## Orchestrator correction to §8 (added after verifier pass 2)

W2's §8 above states the degree-1 flake was exit-lag — scroll N's decrement still queued when scroll N+1's
increment ran. **That mechanism is refuted, and the true cause is NOT established.** Left standing, a confident
wrong root cause is worse than an admitted unknown, so this correction sits next to the claim rather than
replacing it.

The refutation (`review/verifier-2.md` §3): at degree 1 that interleaving is unreachable. In the old gate the
decrement sat in a `finally` that ran *before* `PassAsync` returned, and the route did not return its `Ok(...)`
until `PassAsync` completed — so scroll N+1's request could not be issued, let alone counted, until N's
decrement had already run. Program order sequences them through the HTTP response. Exit lag would require two
scrolls genuinely open at once, which is the thing the test denies.

What IS established, and is not affected:
- The fix is the right shape: the instrument was sharpened, not the assertion loosened.
  `gate.PeakInFlight.Should().Be(degree)` remains exact equality at degrees 1/2/4.
- Sensitivity is preserved: a second scroll started while the first is held still lands inside the open window
  and is still counted — which is exactly when a prefetch would appear.
- Evidence of stability: 12/12 consecutive runs of the theory, plus three consecutive full-suite runs at
  213/213 (two of them `--no-build` repeats).

What remains unknown: what produced the original single failure. The "one run in two" frequency is not in the
record either. The candidate the verifier could not rule out is an emitter-level publish retry re-driving the
lazy record enumerable. Three test suites were competing for this machine during the run that failed, which is
a plausible confounder and equally unproven.

**Do not treat the degree-1 path as proven race-free on the strength of §8.** It is proven green, repeatedly,
with an exact instrument — that is a weaker and more honest statement.
