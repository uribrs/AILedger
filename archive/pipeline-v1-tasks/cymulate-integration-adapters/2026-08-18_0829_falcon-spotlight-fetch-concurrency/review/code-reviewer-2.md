# Code review 2 — Falcon Spotlight fetch concurrency (test-file re-review)

Reviewer: code-reviewer-1 (second pass)
Date: 2026-08-18

Supersedes nothing. `review/code-reviewer-1.md` stands as the record of the earlier state; this file
covers the tree as it is now and states explicitly which of those findings are carried forward.

## Scope reviewed

Re-review limited to the **test files**, per the brief. Production files were last modified 11:54–12:05
and the test files at 13:09–13:10 (mtimes checked), which confirms the production code is unchanged
since the first pass — I did not re-derive findings about it.

Changed since pass 1:

- `UnitTests/.../FalconTwoPhaseFindingsTests.cs` — `ResumeAfterACooperativeStop_PublishesEveryHostExactlyOnce`
  is now a `[Theory]` over degrees 1/2/4; fixture grown 6 → 20 staged hosts; the cancellation trigger
  counts published objects instead of Spotlight HTTP requests; every expectation derived from the
  observed publish count rather than hardcoded; `InitializedCollector` gained an optional
  `maxConcurrentSpotlightBatches` parameter.
- `UnitTests/.../FalconCollectorConfigurationBuilderTests.cs` — the ceiling test became a `[Theory]`
  over `16`/`17`/`999`, and the XML remark recording a `[1,32]`-vs-`[1,16]` contract discrepancy was
  removed.

### Verification performed

- Falcon test project rebuilt (`--no-incremental`): **0 warnings, 0 errors**.
- Full Falcon test project: **213 passed, 0 failed** (was 206 passed / 1 failed). B1 from pass 1 is
  gone, and the suite gained 6 cases from the two Theory expansions.
- `FalconResumeReach.RecordLegStart` read to confirm it calls `SetState` only and never fires
  `OnCheckpoint` — this is what makes the new trigger's counter mean "published objects".
- `Processing/Triggers/FalconCollectorConfigurationMapper.cs:63-72` read to confirm metadata merges
  into config via `TryAdd`, so `SetConfiguration`'s degree wins and the two channels cannot disagree.
- Task docs `decisions.md:50` and `constraints.md:19` read to settle the clamp-ceiling question
  (see "Open questions retired").

---

## Blockers

**None.** B1 from pass 1 is genuinely resolved — see the analysis below, not merely "the suite is green".

### Was B1 resolved, or resolved-looking?

Resolved, and for a structural reason rather than a timing one.

The trigger now lives inside the Spotlight `SendAsync` fake
(`FalconTwoPhaseFindingsTests.cs:1741`) and fires `cooperativeStop.Cancel()` when
`Volatile.Read(ref publishedSoFar) >= 3`. Because that call site *is* a Spotlight request, and a
Spotlight request is only ever issued from inside `FalconSpotlightBatchScroller.EmitBatchRecordsAsync`,
the token trips while some batch's scroll is mid-flight and holding seeded-but-unflushed per-host
accumulators. That holds **at every degree by construction**, not by luck — the mid-scroll condition
is a property of where the trigger lives, and no scheduling interleaving can move it to a batch
boundary. The original test's condition is preserved, not lost.

What differs by degree, and is worth understanding rather than worrying about:

- At **degree 1** the pump takes its sequential branch, so the interrupted scroll is necessarily
  batch `published + 1` — the consumer's very next object. This is exactly the pre-concurrency shape.
- At **degree > 1** the interrupted scroll belongs to some batch `K >= published + 1`, and batches
  between `published + 1` and `K` may have been fetched to completion and never published. The test's
  derived assertions cover both shapes identically, which is why it passes at all three degrees.

The fixture growth 6 → 20 is load-bearing and correctly reasoned in the code comment
(`:1734-1741`): the semaphore admits at most `degree` resident batches, so at degree 4 only batches
~4–7 can have been dispatched when the third publish lands, leaving 13 batches in front of the stop.
With the old 6-host fixture at degree 2 every scroll could already have been issued before arming and
the stop would never land — which the comment records as observed. The margin at 20 is generous.

The literal expectations that were dropped (`exactly 3 published`, `position 4`,
`lastCompletedStagedPage == 2`) were artifacts of sequential fetch timing, not contract. What replaced
them still pins every invariant that matters: contiguity and order of the first leg's prefix
(`:1782`), position equals `published + 1` (`:1791`), staged-page index equals `published - 1`
(`:1804`), the second leg starts at the first unpublished host and walks forward (`:1835`), the union
across legs covers all 20 hosts exactly once (`:1841`), and object naming continues with no gap or
overwrite (`:1850`). That is a legitimate derivation, not a weakening — with the single exception in
I1 below.

---

## Important

- **I1 (new)** — `UnitTests/.../FalconTwoPhaseFindingsTests.cs:1763`
  - **Problem:** The rewrite deleted `interrupted.Success.Should().BeFalse(...)` and left
    `AdapterResult interrupted = await firstLeg.ProcessAsync(...)` assigned but never read (verified:
    the only other occurrences of the identifier are a different test at `:2025`/`:2028` and the word
    "interrupted" inside a `because` string at `:1837`). The stated reason — "Assert the PUBLISH, not
    the status" — misapplies the rule from `06-resume-live-state-authority.md`, which says *assert the
    publish, **not just** the status*, and whose worked example is a run that returns `Success` while
    ingesting nothing. Dropping the status assertion re-opens exactly that hole in the direction the
    doc warns about: a flow that swallowed the cancellation and returned `Success = true` after
    publishing, say, 5 of 20 objects would now pass every remaining assertion. The upper bound of
    `BeInRange(3, 19)` catches only the total-completion case, not partial-success-while-cancelled.
    The same file keeps the assertion in its sibling test at `:2028`, so this is also internally
    inconsistent.
  - **Suggestion:** Restore it next to the publish assertions rather than instead of them —
    `interrupted.Success.Should().BeFalse("the leg was cancelled part-way through the frozen key list, so it did not complete")`
    immediately after `:1765`. That satisfies the doc's rule (both are asserted), removes the dead
    local, and costs one line.

### Carried forward from pass 1 — still standing, unchanged

The production code is untouched, so these all stand exactly as written in `code-reviewer-1.md` and I
am carrying them without restating the detail:

- **I1 (pass 1)** — `FalconSpotlightBatchPump.cs:163`/`:186`: fault classification depends on
  `WaitToReadAsync` rethrowing the completion exception unwrapped; `ReadAllAsync` would wrap it and
  break `FalconFlowExceptionClassifier`. Comment + test still wanted.
- **I2 (pass 1)** — **RETRACTED. See "Retractions" below. The config doc is correct as written;
  do not change it.**
- **I3 (pass 1)** — no fleet-wide rollback path for the degree; per-integration config only.
- **I4 (pass 1)** — the `CymulateUserAgent` / `ConfigureClient` change is an unrelated, untested rider
  on this branch.
- **I6 (pass 1)** — orphan `FetchAsync` task when `WriteAsync` fails fast on an already-cancelled
  token; the pump's own "observes every task" guarantee has this one hole.
- **N1–N6 (pass 1)** — unchanged, except N6 which concerns `InMemoryFalconStagingStore` and is
  untouched by this rewrite.

### Carried forward from pass 1 — narrowed by the rewrite

- **I5 (pass 1)** — unguarded per-test fake state. **Substantially addressed.** The concrete hazard
  (`spotlightCalls++`) is gone. One instance survives: `spotlightFilters.Add(decoded)` at
  `FalconTwoPhaseFindingsTests.cs:542`, a bare `List<string>.Add` inside an HTTP fake. It is
  **latent, not active** — that test seeds 2 hosts at `aidBatchSize 50`, so it produces a single aid
  batch and a single scroll regardless of degree. Every other `.Add` I found in the file's fakes is
  on `OnCheckpoint`, which fires only on the serial consumer and is therefore safe. The general
  exposure remains, though: `InitializedCollector`'s new doc comment states that omitting the
  parameter means "the collector's own default applies", so every other test in this 2,600-line file
  now runs at degree 4 with single-threaded fakes. That is a defensible choice — but it is now an
  explicit one, which is an improvement over inheriting it silently.
  - **Suggestion (unchanged):** convert `:542` to `ConcurrentQueue<string>` when convenient, or state
    in the file header that fakes there must be thread-safe because the default degree is 4.

---

## Nits

- **N1 (new)** — `UnitTests/.../FalconTwoPhaseFindingsTests.cs:1756`
  - **Problem:** `publishedSoFar` is incremented from `OnCheckpoint`, which also fires once from
    `FalconFindingsFlow.RecordCooperativeYield` (`:493`) for the terminal state snapshot. So the
    variable counts *checkpoint commits*, not publishes, and ends the run one above the true publish
    count. It is harmless today — the extra increment happens strictly after the trigger has already
    armed and fired, and every assertion derives `published` from `firstCapture.StreamBatches` rather
    than from this counter — but the comment at `:1729` says "PUBLISHES, not requests", which is true
    only during the arming window.
  - **Suggestion:** Extend the comment to "counts checkpoint commits, which equal publishes until the
    terminal cooperative-yield snapshot — do not read this after the run", so nobody later reuses it
    as an assertion input.

- **N2 (new)** — `UnitTests/.../FalconTwoPhaseFindingsTests.cs:1673-1683` (the `[Theory]` remark)
  - **Problem:** The Theory genuinely exercises the fan-out — the fixture is large enough at degree 4,
    and the second-leg assertions do cover fetched-but-unpublished batches when they occur. But every
    assertion is degree-invariant by construction, so a degree-2 or degree-4 run that happened to
    degenerate to the degree-1 shape (interrupted batch == `published + 1`, nothing fetched ahead)
    would pass silently and look identical. The rows therefore buy regression breadth rather than a
    new observation, and nothing in the test says so.
  - **Suggestion:** Add one sentence to the remark cross-referencing
    `FalconSpotlightConcurrencyTests.CancellationWithFetchesInFlight_YieldsAtThePublishedPosition_AndResumeRedoesEveryUnpublishedBatch`,
    which does pin the in-flight-discard property via `fetchedNotPublished.Should().NotBeEmpty()`.
    I would **not** add a "requests > published + 1" assertion here — it is timing-dependent and would
    reintroduce exactly the class of flake the rewrite just removed.

---

## Open questions retired

- **The `[1,16]` vs `[1,32]` clamp discrepancy (pass 1, open question 3) is resolved, and `16` is
  correct.** The task's own `decisions.md:50` states "Recommended default **4**, clamp ceiling **16**"
  with its justification; the `32` came from `constraints.md:19`, which cites Tenable's 32/4/4 as
  "reference points, not defaults to copy". The earlier test's remark labelling this a "discrepancy on
  record" was itself the mistake, so the rewrite's removal of that remark is a **correction, not a
  loss of record**. The new boundary Theory (`16`/`17`/`999`) is also a real improvement — it pins
  both sides of the clamp, where the previous single `999` case would have passed against a ceiling
  of 15.

## Open questions still open

Both recorded as unproven, unchanged, and (per the brief) already being carried to the operator — not
re-raised for action here:

- Circuit-breaker `FailureThreshold = 3` / `BreakDuration = 300s` tuned for a serial caller, now
  driven at degree 4.
- 401 token-refresh coalescing vs stampede across concurrent requests in
  `CredentialProviderAuthenticator`.

Neither is answerable from this repository, as stated in pass 1.

---

## Retractions

- **I2 (pass 1 and carried into pass 2) is WITHDRAWN.** I claimed peak live record sets was
  `degree + 1` rather than `degree`, on the reasoning that the pump iterator's `_current` field (and
  the flow's `pumped` local) would keep the just-published batch's `List<ReadOnlyMemory<byte>>`
  reachable past `slots.Release()` at `FalconSpotlightBatchPump.cs:180`, while the dispatcher had
  already been admitted to start a replacement. **That is wrong. Measured peak is `degree`.**

  Evidence — a faithful mirror of `PumpConcurrentlyAsync`/`DispatchAsync`/`FetchAsync`/`ReplayAsync`
  (same statement order, same yield-then-release ordering, same consumer-loop shape), Release build,
  8 MB per batch, degree 4, full GC + `WeakReference` sweep every 3 ms, with batch 1 made a 2.5 s
  straggler specifically to hold the hypothesised window open:

  ```
  t=77   RELEASE permit of batch 0
  t=77   START fetch batch 4
  t=81   live=4 ids=[1,2,3,4]        <- batch 0 already unreachable
  t=2534 RELEASE permit of batch 1   <- consumer stalled on batch 1 for 2.45s
  ```

  Across the whole 2.45 s stall the live set stayed `[1,2,3,4]`. Batch 0 and batch 4 never coexist.
  Same result in Debug, and in both fast-producer and slow-producer regimes.

  A separate probe established that a fully drained async iterator *does* still retain its parameter,
  so the retention question turns solely on whether anything roots the yielded `PumpedAidBatch` past
  the release — and nothing does. The mechanism is inferred (the async-iterator rewriter appears to
  clear `_current` on resumption); the observation is measured. Residual unknown the mirror cannot
  cover: whether `NdjsonBatchEmitter` retains the `IAsyncEnumerable` beyond
  `PublishFindingsUtf8PageAsync` — unlikely, since a fresh emitter is created per iteration and is
  itself unrooted after the call.

  **Consequence:** `FalconCollectorConfiguration.cs:196-203` is correct as written
  (`degree x published-object-size`), the A6 residual-risk numbers computed off it stand, and no
  correction should be applied to the shipped comment.

  **N3 is unaffected and slightly strengthened by this.** The permit covers the publish as well as
  the fetch, and the record set becomes unreachable exactly at the release boundary — the two
  lifetimes coincide. That is a tighter design than pass 1 credited it with; the only observation
  that survives is that steady-state *fetch* parallelism is `degree - 1` while a publish is in
  flight, which is a throughput note, not a memory one.

