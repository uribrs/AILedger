# Execution Notes

## Created

- Created section tracker slice contract and task state (full tier).
- Added `QuerySectionTracker` implementing SDK `ISectionTracker` with store-driven completion detection.
- Added `QuerySectionTrackerOptions` exposing the polling interval (default 1 second).
- Added `QuerySectionTrackerTests` covering happy path, empty-query polling emission, multi-section unit-failure attribution, multi-unit-per-query waiting, wait-for-matching-failure invariant, finding arrival order, idempotent emission, cancellation in all three SDK methods, second-consumer rejection, and unknown-unit-id failure.
- Updated Query README to list section tracker under implemented and to move Spillover plus `IExecutionPlanStore` concrete implementation into the not-implemented list.

## Hygiene Fix Folded In

- The operator/linter trimmed per-file `using` directives from `QueryDistributorTests.cs` and `QueryMatcherTests.cs` between this slice and the previous one, leaving the test project unable to compile.
- Resolution: added a single project-level `GlobalUsings.cs` covering the namespaces those trimmed files (and this slice's new tests) reference: Dataflow, SDK Enums, SDK Query Models (Common, Plan, Runtime, Wire), SDK Query Persistence, SDK Query Pipeline, FluentAssertions, and Moq.
- The fix is additive — no per-file usings were restored; the operator's trim is preserved.

## Contract Reconciliation

- The contract said "Use a bounded `Channel<SectionResultV2>` for the output stream." The implementation yields directly via `IAsyncEnumerable<SectionResultV2>` and uses a bounded `Channel<byte>` purely as the wake signal. Functionally equivalent: single consumer, single emission per section, back-pressure honored through the consumer's `MoveNextAsync` cadence. Decisions.md was updated to reflect the actual mechanism.

## Validation

- Contract has non-empty constraints and success criteria.
- Aggregate handoff and matcher handoff identify concrete `ISectionTracker` as the next slice.
- SDK `ISectionTracker` signature confirmed: `RecordFindingAsync`, `RecordUnitFailureAsync`, `CompletedSections`.
- SDK doc on `IExecutionPlanStore` confirms store-driven completion detection is the contract intent.
- Shared project build passed with 0 warnings and 0 errors.
- Query shared test project passed 105 tests (93 prior + 12 new tracker tests).
- Test command emitted NU1900 warnings because external NuGet/CodeArtifact vulnerability indexes were unavailable.
- Verifier: PASS. One informational nit (channel-wording reconciliation) addressed above.
- Code-reviewer: 0 blockers, 2 majors, 2 minors, 2 nits, 2 observations.
  - M1 (signal-coalesce wake latency bounded by PollInterval): accepted as documentation only — XML remark added.
  - M2 (CTS leak in tests): fixed — `NewTimeoutToken()` replaced with `NewTimeoutCts()` returning a disposable `CancellationTokenSource`; every consumer now uses `using`.
  - Mi1 (linear scan in `LookupRepresentedQueryIds`): fixed — added `_queryIdsByUnitId` reverse index built in `BuildIndex`; lookup is now O(1).
  - Mi2 (single-consumer invariant for `_emittedSections`): fixed — added inline comment on the field.
  - N1 (`result!` null-forgiving): fixed — added `[NotNullWhen(true)]` to `TryBuildSectionResult` out parameter.
  - N2 (`GetRawText()` re-encoding): declined — cold path, allocation cost not material.
  - O1, O2 (test timing/cancellation-order observations): no action; documented behavior.

## Commands

- `dotnet build src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Cymulate.Integration.Adapters.Shared.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false`
- `dotnet build src/Cymulate.Integration.Adapters/UnitTests/Query/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false`
- `dotnet test src/Cymulate.Integration.Adapters/UnitTests/Query/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false -v minimal`

## Residual Risks

- The tracker is forward-looking. No concrete `IExecutionPlanStore` implementation exists in this repo, and no production code currently calls `RecordFindingAsync`, `RecordUnitFailureAsync`, or transitions store state. Until the dispatcher writes to a real store AND records failures through this tracker, no section will emit in production. Behavior is correct in isolation; integration is future work.
- Signal-coalesce wake-up has bounded latency equal to the configured `PollInterval`. Correctness is preserved (poll always fires); only emission latency can be delayed by up to one interval if a signal lands during the drain window. Documented in the class XML.
- `GetUnitStatesAsync` returns the entire job's unit-state map on every tick; large plans (thousands of units) may need paging. Out of scope for this slice.
- `QueryJobCompletedV2` is the publisher's responsibility per the existing `QuerySectionPublisher`; the tracker emits only `SectionResultV2`.
- The composite `[Flags] QueryV2.ResultType` open question still applies — the tracker copies the unit's `ResultType` to `QueryResultV2.ResultType` verbatim, mirroring the matcher's pass-through choice.
