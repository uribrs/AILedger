# Verifier pass 2 — Falcon Spotlight fetch concurrency

Verified: 2026-08-18, after W2's second pass (test files at 13:09–13:11). Actor: `verifier`.
`review/verifier-1.md` stands unedited as the record of the pre-fix tree.

## 0. Scope of this pass, and what actually changed

**The implementation did not change.** File mtimes: `FalconSpotlightBatchPump.cs` 11:54:00,
`FalconCollectorConfigurationBuilder.cs` 11:59:19, `FalconCollectorConfiguration.cs` 12:00:02,
`FalconFindingsFlow.cs` 12:05:18 — all before verifier-1. `FakeHttpClientFactory.cs` 12:59:50 (formatting-era
touch, content already reviewed). What moved is the test project and `execution_notes.md`. So every
implementation finding in verifier-1 §1, §4.1, §4.2 and §5 carries over unchanged unless a new test moved the
evidence, which I check below.

Changed since pass 1, beyond what you listed: `FalconSpotlightConcurrencyTests.cs` (13:11) and
`FalconConcurrencyHarness.cs` (13:10) were also reworked — the gate instrument, `AssertSucceeded` →
`AssertPublished`, and one new theory. Your brief mentioned only the resume test.

What I ran, verbatim:

    dotnet build UnitTests/.../FalconCollector.Test.csproj --no-incremental
      Build succeeded.  0 Warning(s)  0 Error(s)

    dotnet test ... --no-build --logger "console;verbosity=normal"     (run C)
      Total tests: 213   Passed: 213   Total time: 7.1530 Minutes

    dotnet test ... --no-build --logger "console;verbosity=normal"     (run D)
      Total tests: 213   Passed: 213   Total time: 7.1632 Minutes

Both runs started with **eight `dotnet test` processes already live on this machine** (your two repeats plus
other agents). That is a hostile environment for a timing-sensitive suite, and it went green twice — worth more
than two clean-machine runs would have been. One caution: I ran `--no-incremental` on the test project at
~13:2x while your `--no-build` repeats were in flight. Sources were identical so the DLL is content-equivalent,
but if your repeats report anything strange, that is the likeliest cause and not a product signal.

---

## 1. SC8 — re-judged: **MET**

Build clean, 0 warnings, and 213/213 twice on the current tree. The failure I recorded in verifier-1 §5 is
gone, and it is gone because its cause was removed rather than because the run got lucky: I re-derived the
mechanism independently in pass 1 (fetch-count trigger vs publish count), and the repair addresses exactly that.

Nothing else in SC1–SC7 or SC9 changes, because the implementation is byte-identical to what pass 1 verified.
Two criteria gained *better* evidence without changing verdict:

- **SC2** — `firstLegAids` and the second leg's AIDs/target paths moved from `BeEquivalentTo` (order-insensitive)
  to `Equal` (order-sensitive) in `ResumeAfterACooperativeStop_...`. Strictly stronger.
- **SC5** — degree 1 is now also exercised through the full cancel-and-resume path
  (`ResumeAfterACooperativeStop_...(1)`) and the new number-independence theory. The gap I named stands: nothing
  still asserts that degree 1 does not *materialise*, which is the memory half of the rollback claim.

New coverage that was not in the contract's criteria but is worth crediting:
`TheFrozenListAddress_AndTheObjectCounter_StayIndependent_UnderAFanOut(1, 4)` encodes the hard rule from
`06-resume-live-state-authority.md:154-175` — forcing the counter to 50 against a frozen-list position of 0, so
a reconciliation in either direction fails loudly. That rule was in `constraints.md` as a durable-layer
requirement and was previously argued structurally in `execution_notes.md`; it is now tested.

`AssertPublished` replacing `AssertSucceeded` in the harness is also a real improvement, not a rename: every
call site must now state how many objects it expects, and the run's status appears only inside the failure
message. That is the T9 hazard encoded at the helper level.

---

## 2. Has `ResumeAfterACooperativeStop_PublishesEveryHostExactlyOnce` been re-timed, or weakened?

**Re-timed. The thing it was written for survives.** I went looking for the weakening you were worried about and
it is not there — but three smaller things were given up, and one of them I would take back.

### It still interrupts mid-scroll

This was your specific question, and it is the load-bearing detail. The trigger reads
`Volatile.Read(ref publishedSoFar) >= 3` **inside the Spotlight route handler**
(`FalconTwoPhaseFindingsTests.cs:1733-1743`) — that is, from inside a scroll that has already been issued and
has not yet answered. So the token trips while a scroll is in flight, not between batches:

- At degree 1 the scroll in flight belongs to the batch the consumer is currently driving, so that batch's
  partial per-host accumulators are exactly what gets discarded — identical in character to the old test.
- At degree 2/4 the triggering scroll belongs to a producer running ahead, and what the consumer discards is
  whatever it had not yet published. A scroll is still open when the token trips, which is the documented point.

A publish-count trigger *evaluated at the checkpoint hook* would have been the weakening you feared — it would
fire at a batch boundary with no scroll open. It is not what was built. The counter is incremented at the
boundary; the cancel is issued mid-scroll. That distinction is the whole repair and it is the right one.

### What got stronger

Twenty staged pages instead of six (with the reason recorded: at degree 2 all six scrolls could be issued before
the third publish, so the stop never landed); three degrees instead of one; order-sensitive `Equal` assertions;
every expectation derived from what the leg actually published, so the test can no longer pass by agreeing with
a hardcoded number that only sequential timing produced. The end-to-end invariant — every host published exactly
once across both legs, contiguous prefix in frozen order, object numbering continuing with no gap or overwrite,
`_resume.reachedPosition` equal to published+1 — is intact and now holds at every degree.

### What got given up

1. **`interrupted.Success.Should().BeFalse()` was deleted** (old line 1752). The justification offered is "assert
   the publish, not the status", but that rule is about not treating `Success == true` as evidence of work; it
   does not argue for dropping a check that a *cancelled* leg does not report success, which is the T9 symptom
   itself. Not a hole in the end: the same property is still asserted at `FalconSpotlightConcurrencyTests.cs:473`
   and `:560` and `FalconTwoPhaseFindingsTests.cs:2028`. Recorded so it is a decision, not an erosion.
2. **`published.Should().BeInRange(3, stagedHosts - 1)` is far looser than the design permits.** Nineteen is not
   a bound, it is "the stop landed at all". Once the stop is armed the consumer publishes at most the batch it
   already had in hand, so the real bound is about `3 + degree + 1`; a regression where cancellation is not
   observed until the pump drains its whole buffer would publish 7 or 8 and this test would stay green. I would
   tighten the upper bound to `3 + degree + 1` — it costs one expression and restores the promptness property
   that the old exact `3` was accidentally providing.
3. **`publishedSoFar` counts checkpoints, not publishes.** `OnCheckpoint` is the only seam the two-phase harness
   exposes, so this is defensible, and empirically nothing else checkpoints on this path (15/15 for W2, 2/2 for
   me). But it is a conflation: if a non-publish checkpoint is ever added to Phase 1 or to the resume path, the
   stop arms before any object exists and this test starts failing in a way that reads as a product defect. One
   line of comment, or an `OnPublishEnter`-style hook in that harness, would close it.

---

## 3. The degree-1 in-flight test — the fix is good, the diagnosis is not established

**The assertion was not loosened.** `gate.PeakInFlight.Should().Be(degree)` is unchanged, exact, at all three
degrees. The change is to the instrument: the measurement window now closes the moment the gate opens, and later
entrants pass through uncounted (`FalconSpotlightConcurrencyTests.cs:75-111`). That is the right shape of fix —
sharpen the instrument, do not relax the claim — and it preserves the sensitivity that matters, because any
second scroll started *while the first is held* still lands inside the open window and is still counted. That is
exactly when a prefetch would appear.

**But I do not accept the stated cause, and neither should you.** `execution_notes.md` §8 says scroll N's
decrement could still be queued when scroll N+1's increment ran. At degree 1 that interleaving is not reachable:
in the old gate the decrement sat in a `finally` that ran *before* `PassAsync` returned, and the route did not
return its `Ok(...)` until `PassAsync` completed — so scroll N+1's request could not be issued, let alone
counted, until N's decrement had already run. Program order sequences them through the HTTP response. Exit lag
would need two scrolls genuinely open at once, which is the thing being denied.

Three further points on the record:

- **The original failure message was never captured.** Nobody knows whether the degree-1 failure was
  `PeakInFlight` = 2, a publish-count mismatch, or something else. The whole diagnosis rests on an assumed
  symptom.
- **"Failed one run in two" is not supported by the record.** One failure exists across every logged run of that
  theory: your run 1. My pass-1 runs A and B both passed it on the *unfixed* instrument.
- **The retry-re-drive hypothesis is neither confirmed nor excluded**, and the narrowed window makes it slightly
  less observable: a second scroll starting after the gate opens is now uncounted by design.

Recommendation: keep the fix, drop the certainty. Change §8 to say the instrument was made exact and the
original symptom was never captured. If it ever recurs, capture the assertion message before anything else —
at degree 1 a peak above 1 would mean two Spotlight requests genuinely overlapped on the sequential path, which
would be an implementation finding, not an instrument one.

---

## 4. Assumption dispositions — **nothing moved**

I did not edit `assumptions.md`; its statuses from pass 1 are terminal and remain correct.

- **A4a, A6, A7 stay NEVER-TESTED.** Nothing probed the cursor, measured memory, or touched publish spacing.
  `execution_notes.md` §12 declines the cursor-lifetime test explicitly and gives an honest reason (the buffer's
  saturation state is not observable from the available seams, so any proxy is a timing coincidence). Declining
  with a reason is the right call and it is still not a test. A green suite is not evidence about memory.
- **A2 and A9** were already VALIDATED; the new tests broaden their evidence (degrees 1/2/4 through the resume
  path, order-sensitive assertions) without changing status.
- **A1, A3, A3a, A4, A5, A8** untouched by this pass.

---

## 5. Did the new work close verifier-1 §4.1 or §4.2?

**§4.1 (dispatcher-fault exception fidelity) — half closed, incidentally, and nobody noticed.**
`Resume_WhenAPageVanishedMidRun_StopsThereAndReportsTheInconsistency` (`:2252`) runs at the default degree 4 and
asserts `result.Data["partialErrorMessage"]` **contains** "no batch of it was ever published". That assertion
would fail if the dispatcher's exception were wrapped by a channel-completion refactor, so the message-fidelity
half of the trap is now guarded — by a pre-existing test, not by design. The **classification** half is still
open: the vanished-page path throws `InvalidOperationException`, which is unclassified before and after, so
nothing pins that a typed `ObjectNotFoundException` / `ObjectStoreAccessDeniedException` from the staged-page
read still reaches the classifier as itself and still reports `FALCON_STORAGE_NOT_FOUND` / non-retryable rather
than falling into the blind 30/60/120 s unknown ladder.

**§4.2 (a producer fault at degree > 1) — not closed.** I checked the two candidates.
`ResumedLegThatPublishesNothing_...` (`:2103`) does return 503 from Spotlight, but it resumes with a single
batch left, so there is no fan-out and no ordering to get wrong. Nothing anywhere faults one scroll while others
are in flight and asserts that earlier batches were published, that the fault surfaces at its own position, and
that later fetched batches are discarded rather than published out of order.

---

## 6. Claims in `execution_notes.md` I did not verify

Stated so they are not read as verified: the targeted stability loops (12/12 for the in-flight theory, 15/15 for
the resume theory), and the assertion that `ServiceNowCmdb`'s single failure reproduces with the change stashed.
I built all eleven other collector test projects clean and confirmed the 20 `RequestedUrls` call sites are
compatible; I did not run those suites myself.

---

## 7. Bottom line

**All nine success criteria are now MET.** SC7 still rests on a target figure rather than an observed one, and
SC5's memory half is still argued rather than tested — both stated, neither blocking.

Revised ranking of what is left, replacing verifier-1 §6:

1. **Add the dispatcher-fault *classification* test** — the message half is now accidentally guarded, the error
   code and retryability are not. Still the cheapest guard against the most expensive latent regression here.
2. **Add a producer-fault-at-degree>1 test** (verifier-1 §4.2). Untouched.
3. **Tighten `BeInRange(3, 19)` to `3 + degree + 1`** and note the checkpoint-vs-publish conflation in the
   counter (§2).
4. **Correct `execution_notes.md` §8** — the degree-1 flake's mechanism is asserted, not demonstrated, and the
   "one run in two" frequency is not in the record (§3).
5. **Either measure peak RSS at degree 4 on a heavy tenant, or restate S as a target** (A6/SC7). Do not raise
   the degree above 4 on the strength of the formula.
6. **Update `state.json`** — still `currentPhase: execution`, all steps and both workers `pending`,
   `verifierRun: false`, after two verifier passes.
7. Decide explicitly about the `required` public-surface change on `FindingsFlowRunConfig` (verifier-1 §4.5).
