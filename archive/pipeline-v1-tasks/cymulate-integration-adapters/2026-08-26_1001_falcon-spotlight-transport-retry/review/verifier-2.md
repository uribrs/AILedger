# Verifier 2 — Falcon Spotlight in-flow transport retry (post-repair cycle)

Verified against what LANDED: `git diff 77ec50cb` plus the untracked
`Collectors/FalconCollector/Exceptions/FalconTransportFailureException.cs`.
Tests re-run by the verifier, not taken from `execution_notes.md`:

- Filtered class run (`FalconSpotlightConcurrencyTests | FalconResilienceStrategyTests |
  FalconFlowExceptionClassifierTests | FalconCollectorConfigurationBuilderTests |
  FalconDeferralPositionObservationTests`): **88 passed, 0 failed, 19s, 0 build warnings.**
  (Cycle 1 was 80; +8 is exactly the new R4 theory's 6 cases plus the two new facts.)

## 1. Verdict

**Pass.**

All six of verifier-1's findings are closed, and each closure is real rather than cosmetic — I read the
arithmetic, re-derived the bound myself, and attacked the R5 test rather than trusting its name.

The two that carried actual risk:

- **R4's cap is a real bound, not a restated clamp.** `MaxTransportRetryDelay = 30s`
  (`FalconSpotlightBatchPump.cs:93`) is applied inside `Math.Min` *before* jitter
  (`:487-491`), so the worst legal configuration (10 retries × 30s base) sums to 300s of sleep plus
  up to 10s of jitter — against ~10.2 days uncapped. I recomputed every theory case; all six are correct
  against `min(base × 3^(attempt-1), 30) + jitter`, and two of them (`(4,2,30)`, `(10,30,30)`) fail
  outright if the cap is deleted.
- **The R5 test is genuine, and I can prove it exercises the flow guard specifically.** Its vendor is
  constructed with an **empty** drop map (`new DroppingSpotlightVendor(new Dictionary<string, int>(), …)`),
  so the pump's wrap at `FalconSpotlightBatchPump.cs:405` cannot fire. The only other site that constructs
  `FalconTransportFailureException` in `src/` is the Phase 2 guard at `FalconFindingsFlow.cs:386`. Its
  assertion greps `CapturingLogger.Containing`, which matches the *formatted message* only
  (`FalconConcurrencyHarness.cs:114-115`) — and the sole log line in the whole path that renders an
  exception's type name into message text is `recover.decision … ExceptionType={ExceptionType}`
  (`FalconResilienceStrategyFactory.cs:153`), reached only once Falcon's own mapped policy claims the
  exception. Remove the guard and a raw `IOException` escapes to chain position 4, Falcon's
  `LogRecoverDecision` never runs, and no line contains the string. The test fails. It is not shaped to pass.

Everything the brief asked me to check independently holds: no `.csproj`, `Directory.Build.props` or
`Directory.Packages.props` in the diff and no version bump; `FalconCorrelatedRecord.cs` absent from
`git diff --stat`; both throw sites use the one-arg ctor and no third construction site exists in `src/`
(the two-arg overload is gone, so this is now structural rather than a convention); and the new
`internal static ComputeTransportRetryDelay` widens nothing — `FalconSpotlightBatchPump` is already
`internal sealed` (`:79`), the instance overload stayed `private` (`:477`), and no other member's
visibility moved in the diff.

Three residual observations are recorded in §5. None changes what ships; all three are wording, not code.

## 2. Success Criteria coverage

| # | Criterion (prompt_contract.md) | Status | Evidence |
|---|---|---|---|---|
| 1 | Mid-stream transport failure retried in-process; no page minted; no live cursor across the backoff | met | Unchanged from cycle 1 and re-read: retry loop `FalconSpotlightBatchPump.cs:350-378`; the `await foreach` in `ScrollBatchAsync` (`:420-425`) has unwound before `DelayTransportRetryAsync` (`:449-465`) sleeps. Only findings-path `AdvancePage` is inside `OnBatchPublished` (`FalconFindingsFlow.cs:329`), downstream of the pump. `R3_DegreeOne_TransportDropRetriesAndPublishesEachBatchOnce` — PASSED |
| 2 | Exhaustion → unbudgeted flat 5-minute `RequestDeferredRecovery`, `Reason = "falcon-transport-failure"`, `UseRecoveryBudget: false` | met | `FalconResilienceStrategyFactory.cs:97-108`; `Create_WhenSpotlightTransportRetriesExhausted_ReturnsUnbudgetedFlatFiveMinuteDeferral` — PASSED |
| 3 | Skipped aid batch impossible by construction; a test shows exhaustion throws, not a partial/empty batch | met | `FetchAsync`'s only exits are a complete `FetchedAidBatch` (`:367`) or `throw ExhaustedTransportRetries(...)` (`:373`). `TransportDropsBeyondTheBudget_SurfaceAsFalconTransportFailure` — PASSED |
| 4a | Test: the retry predicate | met | `IsRetryableTransportFailure` (`:435-437`) exercised by every drop test; negative side by `Create_WhenRawTransportFailureIsUnwrapped_IsClaimedAheadOfTheMappedPolicy` — PASSED |
| 4b | Test: the delay ladder | **met** (was *partial* in cycle 1) | `R4_TransportRetryLadder_GrowsThenStopsAtTheCeiling`, 6 theory cases, asserts the arithmetic directly against the now-`internal static` overload. I recomputed each: `(1,2,2) (2,2,6) (3,2,18)` = the growth arm; `(4,2,30)` = 54s clamped; `(10,2,30)` = 39,366s clamped; `(10,30,30)` = the legal worst case clamped. Ranges are `[n, n+1]`, correct for additive sub-second jitter — PASSED |
| 4c | Test: exhaustion→deferral end to end through `FalconResilienceStrategyFactory.Create()` | met | Decides through the whole chain, so it also proves the four policies ahead of it declined — PASSED |
| 4d | Test: retried fetch neither advances the page nor double-counts `_fetchedBatches` | met | `R2_RetriedFetch_CountsBatchOnceNotPerAttempt` — PASSED. Single `Interlocked.Increment` at `:361`, inside the success arm |
| 5 | Concurrency tradeoff in a code comment and in the docs | met (reformulated, as recorded in cycle 1) | `DelayTransportRetryAsync` remarks (`:440-448`); `01-collection-strategy.md:149-157`; `FalconCollectorConfiguration.cs:253-262`. Stated as head-of-line blocking rather than "6-k", deliberately |
| 6 | Docs updated in the same diff: `01-collection-strategy.md`, `03-current-concerns.md`, `SKILL.md` | met, and extended | Cycle 1's three targets stand. This cycle added the "Uncovered (open)" paragraph to `03-current-concerns.md:52-58` and two edits to `02-decision-making.md` (a file the contract did not list) |
| 7 | Build clean; filtered Falcon test classes green | met | Verifier's own run: 0 warnings, 0 errors, 88/88 |
| 8 | Execution rules: resolve A5/A6/A7 against source or logs; suggest a bump, do not apply | met | §3. No `.csproj`/`Directory.Build.props` in `git diff --stat`; minor bump suggested in `execution_notes.md`, not applied |

**Stop Conditions:** none tripped, unchanged from cycle 1. The repairs introduced no new test failures
(80 → 88, all passing) and touched nothing outside the adapters repo.

## 3. Assumption Disposition

> Format note (orchestrator, post-pass): an `actor` column was appended to this table after the close-out
> validator rejected the archive — `VALIDATED`/`REJECTED` without a named actor reads as "NEVER-TESTED wearing
> a verdict", correctly. The values are the ones verifier-1 recorded and this cycle carried forward; no
> status, citation or judgement was altered.

Verifier-1's dispositions were terminal. **A1–A8 are all carried forward unchanged** — this cycle produced
no new evidence for or against any of them, because every repair was to the mitigation, the tests or the
prose, not to the vendor behaviour or the throw-site question. The one place a carried disposition gained a
consequence is A6, noted below.

| id | status | carried / new | citation | actor |
|---|---|---|---|---|
| A1 — `TenableIoChunkRetry` is the right shape to copy | VALIDATED | carried forward from verifier-1 | Shape at `FalconSpotlightBatchPump.cs:350-378`; holds under injected drops at degrees 1, 2, 4 — all PASSED again in this cycle's run | verifier |
| A2 — expired `after` surfaces as 404 in a 200 body; transport retry does not cover it | NEVER-TESTED | carried forward | Nothing this cycle exercised the 404-in-200 path. `01-collection-strategy.md:149` still scopes its bullet to "response stream dies mid-body" and claims nothing about cursor expiry | — |
| A3 — Spotlight rate limit is a per-customer token bucket | NEVER-TESTED | carried forward | Still nothing measured. The 30s per-attempt cap added this cycle is a head-of-line-blocking bound, not a rate-limit one, so it is not evidence for A3 either | — |
| A4 — peak memory ≈ degree × one materialised batch; a backing-off batch extends residency | NEVER-TESTED | carried forward | Still unmeasured; the widened exposure at degree 1 stands (`:61-76`, `FalconCollectorConfiguration.cs:217-225`). The 90–125 MB figure from 2026-08-18 remains the only datum. See Finding 2 for a doc line that still reads as if it did not widen | — |
| A5 — `Retry-After` / 429 observable on the Spotlight path | REJECTED | carried forward | `FalconHttpFailureClassifier.cs:47-54` constructs without `retryAfter`; `grep -rn -i retryafter Collectors/FalconCollector/` still returns only comments. Omitting both rungs remains correct — re-affirmed in `ComputeTransportRetryDelay`'s remarks (`:471-476`) | verifier |
| A6 — the EOF originates on the Spotlight body read inside `FetchAsync` | NEVER-TESTED | carried forward, **de-risking now documented** | Still no stack, still unresolved. What changed is bookkeeping, not evidence: the residual — Phase 1 spool and the assets flow — is now written down at `03-current-concerns.md:52-58` instead of living only in a review file, so the next person to see this signature learns where the fix does and does not reach | — |
| A7 — a Phase 2 deferral reaches `OnTerminalSnapshotWithoutPublishedPage` and does not `AdvancePage` | REJECTED as to mechanism; conclusion separately confirmed | carried forward | `OnTerminalSnapshotWithoutPublishedPage` is `Flows/Assets/FalconAssetsCheckpointWriter.cs:114`, called only from the assets runner. Conclusion holds by the `OnBatchPublished`/`AdvancePage` route (`FalconFindingsFlow.cs:329`) | recon |
| A8 — `FetchAsync` is idempotent across attempts | VALIDATED | carried forward | `ScrollBatchAsync` (`:412-428`) allocates fresh `stats`/`records` per attempt and re-seeds from the frozen batch; `FalconCorrelatedRecord.cs` still absent from `git diff --stat` | verifier |

### Decision drift

Cycle 1's drift table stands in full. Three entries move to a stronger state this cycle:

| decision | outcome after the repairs |
|---|---|
| MID-EXEC: the wrapper must carry NO inner exception | **now unforgeable, not merely documented.** The two-arg `(message, innerException)` ctor is deleted; `FalconTransportFailureException` exposes exactly one ctor, and its remarks say so explicitly ("There is deliberately NO inner-exception constructor: the invariant is worth more unforgeable than documented"). Both throw sites necessarily comply; the two tripwire tests remain as belt-and-braces |
| MID-EXEC: R5 upgraded from accept to partial mitigation | **now covered by a test**, `R5_StagedPageReadDrop_DefersAsFalconTransportFailure_RatherThanTerminating`, on a new `ReadFault` seam (`InMemoryFalconStagingStore.cs:67,138-142`). Falsification attempt in §1 |
| MID-EXEC: degree 1's rollback is weaker and says so | **the contradicting claim is gone.** `FalconFindingsFlow.cs:264-267` now reads "…but it is no longer the pre-concurrency code path, because it now materialises a batch like every other degree…". Repo-wide sweep result in §4/R4 and Finding 2 |
| NEW this cycle: the ladder carries a per-attempt ceiling | Not in `decisions.md`. `MaxTransportRetryDelay = 30s` is a real behaviour change to the shipped path (any configuration with `maxRetries ≥ 4` at the default base now behaves differently from what cycle 1 would have shipped). It is well documented at its definition (`:82-93`); worth a line in `decisions.md` if that file is meant to be the complete record |

## 4. Attention Item Disposition

| id | final disposition | evidence |
|---|---|---|
| R1 — new exception falls through to the budgeted tail | handled | `Create_WhenSpotlightTransportRetriesExhausted_ReturnsUnbudgetedFlatFiveMinuteDeferral` re-run this cycle — PASSED. Branch at `FalconResilienceStrategyFactory.cs:97` sits above the budgeted tail at `:125-132` |
| R2 — retry increments `_fetchedBatches` per attempt | handled | `R2_RetriedFetch_CountsBatchOnceNotPerAttempt` — PASSED. One `Interlocked.Increment`, at `FalconSpotlightBatchPump.cs:361`, in the success arm after the awaited `ScrollBatchAsync` returned |
| R3 — degree-1 materialisation double-emits or changes published-object count | handled | `R3_DegreeOne_TransportDropRetriesAndPublishesEachBatchOnce` — PASSED. `CountCompletedScrollAsync` still returns zero `grep` hits in `src/` |
| R4 — in-flow ladder long enough to stall the pipeline | **handled** (was `unresolved`) | Decided fresh by reading and running, not from the brief. (a) The cap exists and is applied where it must be — inside `Math.Min` at `FalconSpotlightBatchPump.cs:487-489`, before jitter, on the shared `internal static` overload both the instance path and the tests call, so there is no second uncapped route. (b) The bound is real: worst legal configuration is 300s of sleep + ≤10s jitter, versus ~10.2 days uncapped. I recomputed all six theory cases independently. (c) Both new tests bite — deleting the cap fails `(4,2,30)`, `(10,2,30)`, `(10,30,30)` and `R4_WorstLegalLadder_NeverOutlastsTheDeferralItAvoids` by nine orders of magnitude. (d) The two places that misrepresented the parse-time clamp as the whole mitigation are corrected and now say the opposite explicitly: `FalconCollectorConfiguration.cs:259-262` ("the clamp bounds the ladder's INPUT, not its stall") and `FalconCollectorConfigurationBuilderTests.cs:347-354` ("HALF of R4's mitigation and was briefly mistaken for all of it… Both halves are needed; neither is sufficient"). Residual wording nit in Finding 1 — it does not reopen the item |
| R5 — Phase 2 stream reads outside `FetchAsync` remain uncovered | handled (partial mitigation, now tested) | Guard at `FalconFindingsFlow.cs:365-388`; test falsification attempted and failed in §1. The remaining uncovered surface (Phase 1, assets flow) is now an explicit accepted-risk entry in `03-current-concerns.md:52-58` rather than an unrecorded gap |
| R6 — the wrapper is claimed by chain position 4 anyway, making the change a no-op | handled | Both tripwires re-run — PASSED. Strengthened this cycle: with the two-arg ctor deleted there is no longer a way to construct a chained instance at all |

## 5. Findings

**Verifier-1's findings 1–6 are all closed.** Each was checked at its own citation, not by trusting the
repair list:

1. Closed — `FalconFindingsFlow.cs:264-267` rewritten. Repo-wide re-sweep over `src/` and `ai/` for
   `pre-concurrency | degree 1 | degree-1 | sequential branch | materialis | materializ` surfaces no false
   claim. See the one soft residual below.
2. Closed — the cap landed, bounds what it claims to bound, and both misleading passages are corrected.
3. Closed — the R5 test exists, exercises the flow guard specifically, and fails if the guard is removed.
4. Closed — `03-current-concerns.md:52-58` records the residual accurately: Phase 1 (Discover scroll +
   staged-page spool) and the assets flow escape raw to chain position 4, assets being the more exposed as
   the longer scroll. That matches what I read in the code.
5. Closed — `02-decision-making.md:129-135` now carves the Phase 2 circuit-open out of
   `falcon-server-error` and names the alerting consequence.
6. Closed — (a) the two-arg ctor is deleted and the remarks updated to say the absence is deliberate;
   (b) `02-decision-making.md:146` now reads "The three scheduled classes", with the transport class
   enumerated at `:155-159`.

### Non-blocking observations

None of these changes what ships. Recording them so they are decisions rather than oversights.

**1 — "never outlasts the deferral" is stated slightly tighter than the arithmetic supports.** Three
places assert the worst legal ladder stalls for *less than* the 5-minute deferral: the pump's
`MaxTransportRetryDelay` remarks (`:88-91`, "at most `maxRetries × 30s` = 5 minutes"),
`02-decision-making.md:158`, and `R4_WorstLegalLadder`'s own rationale. Two ways it is not strictly true:
jitter is additive *after* the clamp, so 10 attempts is 300s + up to 10s; and the sum counts only the
sleeps, not the failing scroll attempts between them, which on a large aid batch are not free. The test
itself is honest about the first — it asserts `≤ 300 + 10` and says "The allowance is one second of jitter
per attempt" — so only the prose overstates. Nobody reaches this configuration by accident (it needs
`maxRetries = 10` *and* `base = 30`), and at the defaults the ladder is ~26s. Cheapest fix if the operator
wants it: say "about five minutes" in the two prose sites.

**2 — one degree-1 sentence in the memory section did not get the new caveat.**
`03-current-concerns.md:369`: *"Keep the sequential degree-1 rollback available when diagnosing memory
pressure."* This is **not false** — degree 1 is still sequential and still cuts live record sets from
`degree` to one — which is why the sweep passes. But it is the paragraph a reader lands on during exactly
the memory incident where "degree 1 no longer restores the pre-concurrency profile" is the thing they need
to know, and it is ~350 lines away from the new transport section that says so. A half-sentence
cross-reference would close it. Related to A4, which remains unmeasured.

**3 — the R5 test's remarks claim an ordering guarantee its assertions do not provide.** It says it "pins
the guard BELOW the `OperationCanceledException` catch". The assertion behind that
(`capture.Errors` contains nothing matching "cancel") would not fail if the two catch clauses were
swapped: the guard's filter is `HttpTransportFailureClassifier.IsRetryableTransportFailure`, which does not
match an `OperationCanceledException`, so ordering is behaviourally irrelevant here. The test is fully
genuine for its actual purpose — proving the staged-page drop defers as the Falcon type — and I would not
change the test; the remark is one claim too many.

**Checked and clear:** no `.csproj`, `Directory.Build.props` or `Directory.Packages.props` in
`git diff --stat`; no version bump (minor suggested in `execution_notes.md`, correctly not applied);
`FalconCorrelatedRecord.cs` untouched; exactly two `new FalconTransportFailureException` sites in `src/`
(`FalconSpotlightBatchPump.cs:405`, `FalconFindingsFlow.cs:386`) and four in tests, all one-arg because no
other ctor exists; `FalconSpotlightBatchPump` was already `internal sealed`, so the new `internal static`
overload widens nothing and the instance overload stayed `private`; all 8 new test cases assert real
properties and three of them fail outright if the code under test is reverted.
