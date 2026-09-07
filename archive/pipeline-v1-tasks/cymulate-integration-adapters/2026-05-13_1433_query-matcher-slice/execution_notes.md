# Execution Notes

## Created

- Created matcher slice contract and task state (full tier).
- Added `QueryMatcher` implementing SDK `IMatcher` as an admission-only stage.
- Added `QueryMatcherTests` covering admission, ResultType propagation including flag combinations, JobId/SectionId/QueryId propagation, payload pass-through, QueryId mismatch failure, cancellation, and null guards.
- Updated Query README to mark matcher as implemented and to move structured matcher filtering into the not-implemented list.

## Hygiene Fix Folded In

- `QueryDistributorTests.cs` was missing required `using` directives at HEAD (committed in `9ee236c "query distributer"`) and the project did not build. The matcher slice cannot validate its success criterion ("Query shared tests pass") without first restoring the distributor tests' imports.
- With operator approval, the missing usings were added in this slice. No behavior, fixture, or assertion change.
- This indicates the aggregate `state.json` claim of "86 tests passed" predates the breakage and was stale until this slice repaired it.

## Validation

- Contract has non-empty constraints and success criteria.
- Aggregate handoff and distributor handoff identify concrete `IMatcher` as the next slice.
- SDK `IMatcher` signature confirmed: `ValueTask<IReadOnlyList<Finding>> MatchAsync(DistributedResponse, QueryV2, Guid jobId, CancellationToken)`.
- SDK exposes no structured expression dialect, payload-side targeting schema, or payload-side result-type field — operator approved admission-only scope.
- Shared project build passed with 0 warnings and 0 errors.
- Query shared test project passed 93 tests (86 prior plus 7 new matcher tests).
- Test command emitted NU1900 warnings because external NuGet/CodeArtifact vulnerability indexes were unavailable.
- Verifier found all success criteria satisfied, all constraints respected, full test coverage, no drift, accurate README update.
- Code-reviewer reported no blockers or majors. Two minors: a `distributed.Response` null guard (declined — consistent with repo-wide convention and SDK boundary trust per RULES), and a test name overstating struct-vs-reference semantics (accepted; renamed `ReusesRawResponsePayloadInstance` to `PreservesRawResponsePayloadContent`).

## Commands

- `dotnet build src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Cymulate.Integration.Adapters.Shared.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false`
- `dotnet build src/Cymulate.Integration.Adapters/UnitTests/Query/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false`
- `dotnet test src/Cymulate.Integration.Adapters/UnitTests/Query/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false -v minimal`

## Residual Risks

- `QueryMatcher` does not filter by expression, IP/hostname, or payload-side result type because SDK does not yet expose structured rules for any of them. Every distributed response becomes a finding; this is correct per the contract but will need extension when SDK gains an expression dialect, payload-side targeting schema, or payload-side result type.
- The matcher does not branch on `IAdapterSideMatchingCapability`. The SDK doc says pipeline composition replaces the matcher with a passthrough when an adapter declares it; today the default matcher is already a passthrough, so the swap is unobservable. When structured filtering is added, pipeline composition must install a capability-aware passthrough.
- `distributed.Response` is dereferenced without a null guard, matching the sibling `QueryDistributor` convention. Trust boundary is the SDK contract for `DistributedResponse`.
- `QueryV2.ResultType` is `[Flags]`; the matcher copies the combined flag value to `Finding.ResultType` verbatim. Splitting composite result types into per-flag findings is an open product question and would belong to a follow-up slice if SDK decides on it.
