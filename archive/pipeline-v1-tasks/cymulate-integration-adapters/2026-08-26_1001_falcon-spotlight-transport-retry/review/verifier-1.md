# Verifier 1 — Falcon Spotlight in-flow transport retry

Verified against what landed: `git diff 77ec50cb` plus the untracked
`Collectors/FalconCollector/Exceptions/FalconTransportFailureException.cs`.
Tests re-run independently by the verifier (not taken from execution_notes):

- Filtered class run (`FalconSpotlightConcurrencyTests | FalconResilienceStrategyTests |
  FalconFlowExceptionClassifierTests | FalconCollectorConfigurationBuilderTests |
  FalconDeferralPositionObservationTests`): **80 passed, 0 failed, 18s, 0 build warnings.**
- Targeted re-run of the 24 new test cases by name: **all passed** (names quoted in §2).

## 1. Verdict

**Pass with findings.** The change does what the operator asked and does it correctly at the points that
matter most. The single highest-risk property — that `FalconTransportFailureException` never carries a
transport-marker-bearing inner exception, or the whole change is a no-op — holds at **both** throw sites
(`FalconSpotlightBatchPump.cs:390` and `FalconFindingsFlow.cs:384`, both the one-arg ctor; grep over `src/`
finds no third construction outside tests), is structurally safe against the Infra predicate
(`HttpTransportFailureClassifier.cs:70-74` gates the marker test on `is HttpRequestException or IOException`,
and the Falcon type derives from plain `Exception`), and is guarded by two tripwire tests that both pass.
`maxRetries == 0` still wraps, `_fetchedBatches` counts once per batch, degree 1 genuinely routes through
`FetchAsync`, `CountCompletedScrollAsync` is deleted with zero remaining references, no `.csproj` or
`Directory.Build.props` was touched, and `FalconCorrelatedRecord.cs:69` is byte-identical. Three things keep
this from a clean pass: a **stale degree-1 claim survived in `FalconFindingsFlow.cs:264`** (the brief asked
explicitly that none survive anywhere); the **R4 clamp does not actually bound the stall it was raised to
bound** (at the clamp's own ceilings the in-flow ladder is ~10 days); and the **R5 guard — the newest code in
the diff — has no test**, resting on the same invisible property everything else got a tripwire for.

## 2. Success Criteria coverage

| # | Criterion (prompt_contract.md) | Status | Evidence |
|---|---|---|---|
| 1 | Mid-stream transport failure on a Spotlight aid-batch fetch retried in-process, no page minted, no live cursor across the backoff | met | Retry loop `FalconSpotlightBatchPump.cs:336-366`; the scroll's `await foreach` in `ScrollBatchAsync` (`:397-413`) has already unwound (and disposed its enumerator) before `DelayTransportRetryAsync` (`:440-452`) sleeps, so no `after` cursor is live across the delay. No page mint: the only `AdvancePage` on the findings path is inside `checkpointWriter.OnBatchPublished` (`FalconFindingsFlow.cs:329`), downstream of the pump. Test `R3_DegreeOne_TransportDropRetriesAndPublishesEachBatchOnce` asserts object names 1..N with no gap and no repeat — PASSED |
| 2 | Exhaustion → unbudgeted flat 5-minute `RequestDeferredRecovery`, `Reason = "falcon-transport-failure"`, `UseRecoveryBudget: false`, not `PublishFailure` | met | `FalconResilienceStrategyFactory.cs:97-108`, reusing `CreateUnbudgetedFlatRecoveryBackoff()`. Test `Create_WhenSpotlightTransportRetriesExhausted_ReturnsUnbudgetedFlatFiveMinuteDeferral` asserts the type, reason, `UseRecoveryBudget == false`, and flatness at attempt 0 **and** attempt 3 — PASSED |
| 3 | Skipped aid batch impossible by construction; a test shows exhaustion throws rather than returning a partial/empty batch | met | `FetchAsync` has exactly two exits: a completed `FetchedAidBatch` or `throw ExhaustedTransportRetries(...)` (`:357-359`) — no partial return path exists. Test `TransportDropsBeyondTheBudget_SurfaceAsFalconTransportFailure` asserts h2 publishes **nothing** (not an empty object), h3 is never attempted, and the strategy saw the Falcon type — PASSED |
| 4a | Test: retry predicate | met | `IsRetryableTransportFailure` (`pump:421-423`) exercised by every drop test; the negative side by `Create_WhenRawTransportFailureIsUnwrapped_IsClaimedAheadOfTheMappedPolicy` — PASSED |
| 4b | Test: the delay ladder | **partial** | The ladder's *bounds* are tested at parse time (`R4_Build_WithSpotlightTransportRetryBaseDelayOutOfRange_ClampsToTheBounds`, 6 cases — PASSED). The ladder's *arithmetic* (`base × 3^(attempt-1)` at `pump:465`) has no direct test; the retry tests all run at 1 or 2 attempts and only assert that a delay happened. See Finding 2 — the untested arithmetic is where the defect is |
| 4c | Test: exhaustion→deferral end to end through `FalconResilienceStrategyFactory.Create()` | met | `Create_WhenSpotlightTransportRetriesExhausted_...` decides through the **whole chain**, not the mapped policy in isolation, so it also proves the four policies ahead of it declined — PASSED |
| 4d | Test: a retried fetch neither advances the page nor double-counts `_fetchedBatches` | met | `R2_RetriedFetch_CountsBatchOnceNotPerAttempt` (5 scrolls → 3 fetched-batch log lines) — PASSED. Code check, not just the name: exactly one `Interlocked.Increment(ref _fetchedBatches)` exists, at `pump:347`, inside the success arm of the loop, after the awaited `ScrollBatchAsync` returned. Page non-advance covered by criterion 1's target-path assertion |
| 5 | Concurrency tradeoff stated in a code comment and in the docs | met (reformulated) | Code: `DelayTransportRetryAsync` remarks (`pump:428-437`). Docs: `01-collection-strategy.md:149-157`, `FalconCollectorConfiguration.cs:251-258`. The shipped wording is **not** the contract's "degree 6 with k retrying yields 6-k" — it states head-of-line blocking (a sleeping batch stalls publish/checkpoint for everything behind it *at any degree*, with up to `degree` record sets resident). That is recon's more accurate framing and is a deliberate improvement, recorded here so nobody "corrects" it back |
| 6 | Docs updated in the same diff: `01-collection-strategy.md` (amend the "neither is duplicated in the collector" line), `03-current-concerns.md`, `SKILL.md` | met | `01-collection-strategy.md:163-172` replaces the line with the request/body split and names the incident; `03-current-concerns.md:21-55` adds the full section incl. both traps; `ai/skills/collector-execution-and-recovery/SKILL.md:27-46` rewrites the false "Falcon … stays on the legacy `PublishFailure(IsRetryable=true)` shape" line and adds four durable rules. `collector-flow-patterns` was optional ("if it fits") and was not touched — its two existing transport lines are still true |
| 7 | Build clean; filtered Falcon test classes green | met | Verifier's own run: 0 warnings, 0 errors, 80/80 passed |
| 8 | Execution rules: resolve A5/A6/A7 against source or logs; suggest a version bump, do not apply | met | See §3. `execution_notes.md` records the suggestion (minor) and no `.csproj`/`Directory.Build.props` is in `git diff --stat` |

**Stop Conditions:** none tripped. A6 did not resolve against the design (it did not resolve at all — see §3);
A7's *conclusion* held (its mechanism did not, which is not a Stop Condition); nothing outside the adapters
repo was touched; the change resolved test failures and introduced none.

## 3. Assumption Disposition

| id | status | citation | actor |
|---|---|---|---|
| A1 — `TenableIoChunkRetry` is the right shape to copy | VALIDATED | The per-unit in-flow retry landed as that shape (`pump:336-366`, attempt body extracted to `ScrollBatchAsync`) and holds under injected mid-body drops at degree 1, degree 2 and degree 4 with reverse-speed answering: `R3_DegreeOne_...`, `R2_RetriedFetch_...`, `TransportDropUnderAFanOut_IsRetried_AndEveryBatchPublishesOnceInOrder` — all PASSED, verifier's own run | verifier |
| A2 — expired `after` surfaces as 404 inside a 200 body; transport retry does not cover it and must not be documented as if it does | NEVER-TESTED | No test, no log, no research file exercises the 404-in-200 path in this task. The *documentation* half of the obligation was met — `01-collection-strategy.md:149` scopes the new bullet to "response stream dies mid-body" and claims nothing about cursor expiry — but that is doc hygiene, not evidence for the vendor behaviour | — |
| A3 — Spotlight rate limit is a per-customer token bucket; retries spend a shared budget | NEVER-TESTED | Nothing measured. The default ladder (≈2s/6s/18s, 4 scrolls max) is *consistent with* a conservative reading of A3 but is not evidence for it. Do not cite the shipped defaults as confirmation of the bucket model | — |
| A4 — peak memory ≈ degree × one materialised batch, unmeasured; a backing-off batch extends residency | NEVER-TESTED | Still unmeasured, and this change **widened** the exposure rather than narrowing it: degree 1 now materialises a batch too (`pump:157`, `FalconCollectorConfiguration.cs:222-225`), so the documented memory-incident rollback no longer restores the pre-concurrency profile. The 90–125 MB observation from 2026-08-18 remains the only datum | — |
| A5 — `Retry-After` / a 429 status is observable on the Falcon Spotlight path | **REJECTED** | Contradicted by source: `FalconHttpFailureClassifier.cs:47-54` constructs `AdapterHttpRequestFailedException` with **no** `retryAfter` argument, and `grep -rn -i retryafter Collectors/FalconCollector/` returns only the new pump comment. A mid-body drop is a bare `IOException` with no status and no headers (the production message, and what `MidBodyFailureStream` reproduces). **Do not re-assume** that a `Retry-After` or 429 rung could ever fire at the `FetchAsync` seam — omitting them from the ladder was correct. Scope note: the *session* layer separately honours `Retry-After` headers via `DefaultInProcessServerDelayThreshold`; that is a different layer and is untouched | verifier |
| A6 — the EOF originates on the Spotlight body read inside `FetchAsync` | NEVER-TESTED | No new log evidence was obtained; the throw site was never captured with a stack, and nothing in the diff or the test run speaks to where the production EOF arose. **Do not re-assume** the throw site is known. Materially de-risked but not resolved: the R5 guard (`FalconFindingsFlow.cs:365-386`) means any retryable transport fault raised *anywhere inside the Phase 2 consumer loop* now defers instead of terminating, so the outage is fixed under either branch of A6 **provided the fault is inside Phase 2**. Phase 1 spool and the assets flow remain uncovered — Finding 4 | — |
| A7 — a deferral from Phase 2 reaches `OnTerminalSnapshotWithoutPublishedPage` (`FalconFindingsFlow.cs:442`) and does not `AdvancePage` | **REJECTED as to mechanism; conclusion separately confirmed** | Mechanism is wrong: `OnTerminalSnapshotWithoutPublishedPage` is declared at `Flows/Assets/FalconAssetsCheckpointWriter.cs:114` and is called only from `Flows/Assets/FalconAssetsScrollRunner.cs:339` and `:365` — the **assets** flow. `FalconFindingsFlow.cs:471` mentions it in a doc-comment cross-reference only; nothing on the findings path invokes it. The conclusion holds by a different route: `grep AdvancePage` over `FalconCollector/` shows the findings path's only advance is inside `checkpointWriter.OnBatchPublished` (`FalconFindingsFlow.cs:329`), which runs after a successful publish at `:309`, and both throw paths escape before it. **Do not re-assume** findings shares the assets writer's terminal-snapshot path | recon (mechanism), verifier (conclusion) |
| A8 — `FetchAsync` is idempotent across attempts | VALIDATED | Source: `ScrollBatchAsync` (`pump:396-414`) allocates a fresh `BatchEmitStats` and a fresh `List<>` per attempt and re-seeds accumulators from the frozen `StagedAidBatch`; `FalconCorrelatedRecord.cs:69` (`host.DeepClone()`) is untouched — `git diff --stat` over that file is empty. Behaviour: `R3_DegreeOne_TransportDropRetriesAndPublishesEachBatchOnce` asserts each retried aid's object carries its one finding exactly **once**, which a retained partial attempt would break — PASSED | W1 (source), verifier (test) |

### Decision drift

| decision (decisions.md) | outcome |
|---|---|
| Fix Falcon-side, not Infra-side | **as decided.** Every changed path is under `Collectors/FalconCollector/` or its test project; `HttpTransportFailureClassifier` is consumed, unmodified (read from the Infra repo to confirm the predicate, not edited) |
| Mimic `TenableIoChunkRetry`, not its exhaustion policy | **as decided.** In-flow retry copied; no skip path exists in `FetchAsync` (only exits are a full batch or a throw), asserted by `TransportDropsBeyondTheBudget_...` |
| Retry lives inside `FalconSpotlightBatchPump.FetchAsync` | **as decided** (`pump:336`) |
| Predicate = `IsRetryableTransportFailure \|\| IsCircuitBreakerException` | **as decided** (`pump:421-423`), verbatim. Carries an unremarked side effect — Finding 5 |
| Exhaustion throws a new Falcon exception, classified retryable, routed to the same unbudgeted flat 5-minute plan under `falcon-transport-failure` | **as decided.** `FalconFlowExceptionClassifier.cs:74-80`, `FalconResilienceStrategyFactory.cs:97-108`; plan factory renamed `CreateServerErrorRecoveryBackoff` → `CreateUnbudgetedFlatRecoveryBackoff` since it now serves three conditions |
| Accepted tradeoff: a backing-off batch keeps its semaphore slot; state it in comment + docs | **changed during execution, for the better.** Stated as head-of-line blocking rather than "6-k effective concurrency" — recon showed the queue stall, not slot starvation, is the real cost. Both the comment and the docs landed |
| Proceeding on unverified A6 | **as decided, and hedged.** Still unverified; the R5 upgrade means the fix does not depend on it within Phase 2 |
| Proceeding on unverified A7 | **refined during execution.** Recon refuted the mechanism and confirmed the conclusion before any code was written; the landed code depends only on the confirmed conclusion |
| MID-EXEC: the wrapper must carry NO inner exception | **as decided, and it holds.** Both throw sites use the one-arg ctor; the two-arg ctor survives on the type but is unused in `src/` (Finding 6) |
| MID-EXEC: the wrap happens even at `maxRetries = 0` | **as decided.** Arithmetic read, not inferred from the test name: at `_transportMaxRetries == 0`, attempt 1 catches, `1 > 0` is true (`pump:357`), so the first drop wraps with zero delays. Asserted by `ZeroRetries_StillWrapsTheFirstDrop_RatherThanLettingItEscapeRaw` — PASSED |
| MID-EXEC: R5 upgraded from accept to partial mitigation | **as decided** (`FalconFindingsFlow.cs:365-386`), re-throw only, no retry — but untested (Finding 3) |
| MID-EXEC: renamed to `FalconTransportFailureException` | **as decided.** No `FalconSpotlightTransportFailureException` remains anywhere in `src/` |
| MID-EXEC: degree 1's rollback is weaker and says so | **as decided** in the pump (`:61-76`), the config (`:222-225`) and `01-collection-strategy.md` — but **one contradicting claim survived**, Finding 1 |

## 4. Attention Item Disposition

| id | final disposition | evidence |
|---|---|---|
| R1 — new exception falls through to the budgeted tail | handled | Named test `Create_WhenSpotlightTransportRetriesExhausted_ReturnsUnbudgetedFlatFiveMinuteDeferral` run by the verifier — PASSED, asserting flat 5m at attempt 0 *and* 3 and `UseRecoveryBudget == false`. Guard read at its citation: `FalconResilienceStrategyFactory.cs:97` sits above the tail at `:125-132`. (The budgeted tail still exists and is still reached by the *other* mapped retryables — unchanged, and correct) |
| R2 — retry increments `_fetchedBatches` per attempt | handled | Named test `R2_RetriedFetch_CountsBatchOnceNotPerAttempt` — PASSED (5 scrolls, 3 batch-fetched log lines). Verified in code, not by name: the single `Interlocked.Increment` is at `pump:347`, inside the success arm |
| R3 — degree-1 materialisation double-emits or changes published-object count | handled | Named test `R3_DegreeOne_TransportDropRetriesAndPublishesEachBatchOnce` — PASSED, asserting target paths 1..3 with no gap/repeat, aid order `h1,h2,h3`, and one finding per aid. `PumpSequentiallyAsync` calls `FetchAsync` at `pump:157`; `CountCompletedScrollAsync` returns **zero** `grep` hits in `src/` — deleted, not orphaned |
| R4 — in-flow ladder long enough to stall the pipeline | **unresolved** | The named artifact exists and passes (`R4_Build_With…ClampsToTheBounds`, 11 cases — PASSED), so attempts and base delay *are* clamped at parse time. But the check that matters is the arithmetic at the clamp's own ceilings, and it fails the item: `pump:465` is `base × 3^(attempt-1)` with **no per-delay cap**, so the legal maximum (10 retries, base 30s) yields a final attempt of 30 × 3⁹ = 590,490s ≈ **6.8 days** and a total in-flow stall of ≈10.2 days. Even `maxRetries = 10` at the *default* base of 2 stalls the publisher ≈13.7 hours. The clamp bounds the inputs; it does not bound the failure mode the item names. See Finding 2 |
| R5 — Phase 2 stream reads outside `FetchAsync` remain uncovered | handled (partial, as re-decided) | Guard read at its citation: `FalconFindingsFlow.cs:365-386`. It is positioned after the `OperationCanceledException` catch, predicated on `HttpTransportFailureClassifier.IsRetryableTransportFailure`, logs the original with its stack, and re-throws the Falcon type with **no** inner exception. Correct as written and as re-scoped (re-throw, not retry). No test — Finding 3 |
| R6 — the wrapper is claimed by chain position 4 anyway, making the change a no-op | handled | Both named tests run by the verifier: `Create_WhenTransportFailureCarriesTransportMarkerInMessage_StillReachesTheMappedPolicy` (marker text in the message, still reaches the mapped arm) and the negative `Create_WhenRawTransportFailureIsUnwrapped_IsClaimedAheadOfTheMappedPolicy` (raw `IOException` → `PublishFailure`/`ADAPTER_EXCEPTION`) — both PASSED. Structural confirmation from Infra source: `HttpTransportFailureClassifier.cs:70-74` tests the marker only on a chain member that `is HttpRequestException or IOException`, and `EnumerateExceptionChain` (`:79-91`) walks 10 deep. `grep` over `src/` confirms both throw sites use the one-arg ctor |

## 5. Findings

Ranked by what would change what ships.

**1 — A false degree-1 claim survived, in the flow itself. (correctness of documentation; the brief asked
specifically that none survive)**
`FalconFindingsFlow.cs:264`: *"Degree 1 takes the pump's sequential branch, which is the pre-concurrency code
path itself rather than a re-creation of it."* That is exactly the sentence this change made false, and it
sits **twelve lines above the pump construction that made it false**. The pump's own class doc, the config
remarks and `01-collection-strategy.md` were all rewritten correctly; this one was missed. A reader reaching
for the degree-1 rollback during a memory incident is most likely to read it here. One-line fix.
(Repo-wide sweep otherwise clean: every other "degree 1"/"pre-concurrency"/"sequential path" hit in `src/` and
`ai/` is either already corrected or still true — the two `FalconSpotlightConcurrencyTests` comments about
degree 1 having no dispatcher and no prefetch remain accurate.)

**2 — The R4 clamp does not bound the stall it was raised to bound.**
`ComputeTransportRetryDelay` (`pump:465`) is `base × 3^(attempt-1)` with no ceiling on the individual delay,
while the config clamps allow `maxRetries = 10` and `base = 30`. At those legal values a single batch sleeps
~6.8 days on its last attempt and ~10.2 days in total, holding its semaphore slot and blocking publish and
checkpoint for the whole run behind it. `maxRetries = 10` at the default base — a plausible thing for an
operator to type during an incident, since 10 is the documented ceiling — stalls ~13.7 hours. The
configuration remarks (`FalconCollectorConfiguration.cs:254-258`) and the test comment
(`FalconCollectorConfigurationBuilderTests.cs:349-352`) both present the clamp as the protection against
precisely this, so the code and its own documentation disagree. Defaults are fine (2s/6s/18s ≈ 26s); this is
a misconfiguration cliff, not a live bug. Cheapest fix: cap the per-attempt delay
(`Math.Min(computed, TimeSpan.FromMinutes(2))` or similar) and say so where the clamp is documented. There is
also no test on the ladder arithmetic itself (criterion 4b), which is why this got through.

**3 — The R5 guard is the newest code in the diff and the only new path with no test.**
`FalconFindingsFlow.cs:365-386` is correct as read, but its correctness rests on the same invisible
no-inner-exception property that every other site got a tripwire for, and its message interpolates
`ex.Message` — so it *will* carry marker text, and is safe only because of the type gate. Nothing exercises
it: no test drops a staged-page read or a multipart publish. A single test that fails
`FalconStagedHostPage`'s object-store read with the marker `IOException` and asserts the strategy sees
`FalconTransportFailureException` would close it, and would also pin the catch's position relative to the
`OperationCanceledException` catch above it.

**4 — Residual uncovered surface, stated so it is a decision rather than a surprise.**
The guard is scoped to the Phase 2 consumer loop's `try`. A mid-body transport failure in **Phase 1** (the
Discover host scroll and staged-page spool, all of which run before the `try` at `FalconFindingsFlow.cs:270`)
or anywhere in the **assets** flow still escapes raw to chain position 4 and publishes a terminal failure —
the same outage, different phase. The assets flow is the longer scroll of the two. Out of the contract's
scope and correctly so; worth recording in `03-current-concerns.md`, which currently says "partly covered
elsewhere" about Phase 2 only.

**5 — Observability relabel for circuit-open inside the scroll.**
The pump's predicate includes `IsCircuitBreakerException` (`pump:421-423`), so an open circuit raised inside a
Spotlight scroll is now retried ~26s at the default ladder — pointlessly, since a Polly break duration
typically outlives it — and then wrapped, arriving at the strategy as `falcon-transport-failure` rather than
`falcon-server-error`. Same plan and same delay, so no behavioural regression, but `02-decision-making.md:129`
("`500`…and an opened circuit breaker become `deferred-recovery:falcon-server-error`") is now true only for
circuit-opens raised *outside* the pump, and any alert keyed on that reason loses the Phase-2 ones.

**6 — Two loose ends worth one line each.**
(a) `FalconTransportFailureException` keeps its two-arg `(message, innerException)` ctor. It is unused in
`src/`, and the type's own remarks forbid the thing it enables. The remarks plus
`Create_WhenTransportFailureCarriesTransportMarkerInMessage_...` are real protection, but deleting the ctor
would make the invariant unforgeable rather than merely documented — the strongest available form of the
mid-execution decision.
(b) `02-decision-making.md:139-152` enumerates the scheduled recovery classes and how each is budgeted. It now
omits the third one. The contract did not list this file, so this is a gap rather than a miss, but it is the
Falcon doc a reader consults for exactly this question.

**Not findings, checked and clear:** no `.csproj`, `Directory.Build.props` or `Directory.Packages.props` in
`git diff --stat`; no version bump anywhere (minor suggested in `execution_notes.md`, correctly not applied);
`FalconCorrelatedRecord.cs` untouched; `FalconSpotlightBatchPump`'s ctor has one caller and it passes both new
knobs; `FindingsFlowRunConfig`'s two new `required` members are filled at all three construction sites.
`FalconTwoPhaseFindingsTests.cs` was edited (required-property fill only) and that class was **not executed**,
correctly, under the no-full-suite rule — it compiles, and the edit is behaviour-neutral by inspection.
