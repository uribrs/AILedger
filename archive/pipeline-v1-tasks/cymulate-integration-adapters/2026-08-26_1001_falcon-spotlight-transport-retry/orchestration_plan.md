# Orchestration Plan

## Problem Classification

- Contract sufficiency: sufficient
- Classes (maximum 3):
  - `falcon` — exact reuse of the existing ledger tag (5 prior rows).
  - `streaming-transport` — minted. Mid-body failures after headers return; no existing tag covers it
    (`checkpoint-resume` is adjacent but about position, not transport).
  - `resilience-routing` — minted. Which policy in the ordered chain claims an exception, and with what budget.
- Additional classified prior art: none. The classified delta over `streaming|backoff|deferral|in-process|
  mid-stream|semaphore|concurren` returns only the four 2026-08-18 rows the designer already seeded as A1-A4.
- Recon correction: classification confirmed. Recon also **refuted A7's mechanism while confirming its
  conclusion** — `OnTerminalSnapshotWithoutPublishedPage` is an assets-flow method
  (`Flows/Assets/FalconAssetsCheckpointWriter.cs:114`), not findings; the cited `FalconFindingsFlow.cs:442`
  is a doc-comment cross-reference. A deferral nonetheless mints no page, because `AdvancePage` is reachable
  only via `checkpointWriter.OnBatchPublished` (`FalconFindingsFlow.cs:329`) after a successful publish
  (`:309`), which the escape path bypasses. Not a Stop Condition.

### Attention Items

| id | failure mode | causal path and impact | planned handling | source |
|---|---|---|---|---|
| R1 | New exception falls through to the budgeted tail branch instead of getting its own | `CreateRetryableDecision` tail at `FalconResilienceStrategyFactory.cs:105-113` defaults to `UseRecoveryBudget: true` on the 3-attempt 5/15/30 plan (`:150`). The remarks at `:164-174` record that this exact shape previously killed runs on the 4th consecutive no-progress deferral. Silently reintroduces the bug being fixed. | `test:UnitTests/.../FalconResilienceStrategyTests.cs::Create_WhenSpotlightTransportRetriesExhausted_ReturnsUnbudgetedFlatFiveMinuteDeferral` | recon Landmine 8 |
| R2 | Retry increments `_fetchedBatches` per attempt rather than per batch | `Interlocked.Increment` at `FalconSpotlightBatchPump.cs:296` means "a scroll ran to completion". Per-attempt counting corrupts the co-operative-stop diagnostic at `FalconFindingsFlow.cs:356` and the "N fetched, M published" log at `:474-485`. The counter doc at `:83-93` forbids it becoming a position. | `test:UnitTests/.../FalconSpotlightConcurrencyTests.cs::R2_RetriedFetch_CountsBatchOnceNotPerAttempt` | recon Landmine 12 |
| R3 | Degree-1 materialisation double-emits or changes published-object count | Operator decided degree 1 routes through the materialising `FetchAsync`. Today `PumpSequentiallyAsync` (`FalconSpotlightBatchPump.cs:116-131`) hands the publisher a lazy scroll; getting the conversion wrong either double-emits records or changes object naming on the documented rollback path. | `test:UnitTests/.../FalconSpotlightConcurrencyTests.cs::R3_DegreeOne_TransportDropRetriesAndPublishesEachBatchOnce` | recon Landmine 1 + operator decision |
| R4 | In-flow ladder long enough to stall the pipeline | Head-of-line blocking, not slot starvation, is the cost: the consumer awaits in dequeue order (`FalconSpotlightBatchPump.cs:170`), so a sleeping head batch stops publish and checkpoint entirely while up to `degree` materialised record sets stay resident (≈420-600 MB at degree 6, `FalconCollectorConfiguration.cs:216-224`). | `test:UnitTests/.../FalconCollectorConfigurationBuilderTests.cs::R4_SpotlightTransportRetryKnobsAreClamped` — bounds attempts and base delay at parse time | recon Landmine 2 |
| R5 | Phase 2 stream reads outside `FetchAsync` remain uncovered | The staged-page object-store read (`FalconStagedHostPage.cs:70,73`) and the multipart publish (`FalconFindingsFlow.cs:309`) both run in Phase 2 and escape raw to chain position 4, which publishes terminally. Same outage, different trigger. | **Upgraded from accept to mitigated:** `guard:Flows/Findings/FalconFindingsFlow.cs` — a catch on the Phase 2 consumer loop re-throws any retryable transport failure as `FalconTransportFailureException`, so it defers. Re-throw only, no retry. | recon Landmine 9, Q2 #5/#9 |
| R6 | The wrapper is claimed by chain position 4 anyway, making the whole change a no-op | `IsRetryableTransportFailure` walks `InnerException` 10 levels (`HttpTransportFailureClassifier.cs:15-25`), so wrapping the EOF as an inner exception hands it straight back to `RetryableTransportFailurePolicy`, which publishes terminally because Falcon passes no `transientTransportBackoff`. The classifier arm never runs and the change is a no-op. | `test:UnitTests/.../FalconResilienceStrategyTests.cs::R1_TransportFailureWithMarkerInMessage_StillReachesMappedPolicy`, plus the negative case asserting a raw marker-bearing `IOException` still yields `PublishFailure` | orchestrator, mid-execution |

### Research Questions

No research needed — the failure, its mechanism, and the fix all live in code recon has already read, and both
prior-art beliefs that touch external behaviour are non-blocking: A2 (Spotlight cursor expiry returning 404 in a
200 body) concerns re-anchoring that the scroller swallows one level below the retry
(`FalconSpotlightBatchScroller.cs:99-110`), and A3 (the shared per-customer token bucket) argues only for a
conservative ladder, which is the direction already chosen for R4.

## Complexity Decision
- Path: decompose
- Axis scores: Complexity medium | Separability high | Coupling low | Dependency order low | Execution risk medium | Worker clarity high
- Rationale: recon named four disjoint file sets — a hard trigger. Coupling is read off that finding, not
  estimated: Set A needs only Set B's type name and Set C's property names, both freezable in a small phase 0.
  Execution risk is medium because R1/R2/R3 are silent-corruption failures, which is why each carries a test
  rather than an intention.

## Research Decisions
- External research: none needed. Rationale above.
- Internal recon: complete → research/internal-recon.md

## File Ownership
- Disjoint sets found: 4 (recon: Sets A-D)
- Phase 0 (main thread) owns: `Exceptions/FalconSpotlightTransportFailureException.cs` (new),
  `Processing/Configuration/FalconCollectorConfiguration.cs`,
  `Processing/Configuration/FalconCollectorConfigurationBuilder.cs`,
  `Processing/Configuration/FalconIdentification.cs`
- W1 owns: `Flows/Findings/Correlated/FalconSpotlightBatchPump.cs`,
  `UnitTests/.../FalconConcurrencyHarness.cs`, `UnitTests/.../FalconSpotlightConcurrencyTests.cs`
- W2 owns: `Processing/FalconFlowExceptionClassifier.cs`,
  `Processing/Resilience/FalconResilienceStrategyFactory.cs`,
  `UnitTests/.../FalconFlowExceptionClassifierTests.cs`, `UnitTests/.../FalconResilienceStrategyTests.cs`,
  `UnitTests/.../FalconCollectorConfigurationBuilderTests.cs`
- Main thread owns after synthesis: the three doc targets (Set D).
- Shared surface frozen in phase 0: the exception type name and ctor signature; the config property names,
  typed defaults and clamp bounds. Nothing else crosses a worker boundary.

## Worker Plan
- W0 — scope: freeze shared surface (exception type + config knobs, fully implemented, not stubbed).
  output: the four phase-0 files compiling. phase: 0. Executed in the main thread — it is ~60 lines.
- W1 — scope: in-flow transport retry in the pump, covering BOTH degrees; delete the now-dead
  `CountCompletedScrollAsync`. owns: pump + harness + concurrency tests. inputs: W0 output.
  delivers: R2, R3. phase: 1. continuity: fresh.
- W2 — scope: classifier arm + a third named branch in `CreateRetryableDecision` reusing the existing
  unbudgeted flat 5-minute plan factory. owns: classifier + factory + three test files. inputs: W0 output.
  delivers: R1, R4. phase: 1. continuity: fresh.

## Synthesis Approach
W1 and W2 touch no common file, so synthesis is a build plus the filtered test run across both sets. The main
thread then writes the docs from what actually shipped, and reconciles the `_fetchedBatches` and object-count
claims against W1's test output rather than its prose.

## Verification Obligations
- Cross-check against `prompt_contract.md` Success Criteria.
- Dispose all 8 assumptions; A7's mechanism is already known refuted and its conclusion confirmed.
- Dispose R1-R5 against artifacts, not intentions.
- Confirm no `.csproj` version bump landed.
- Confirm `FalconCorrelatedRecord.cs:69` (`host.DeepClone()`) is untouched — retry safety depends on it.
