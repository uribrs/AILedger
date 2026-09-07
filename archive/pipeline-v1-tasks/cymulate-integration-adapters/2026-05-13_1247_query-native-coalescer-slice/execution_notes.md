# Execution Notes

## 2026-05-13

Created the native coalescer slice contract from the aggregate Query state, completed optimizer handoff, and SDK descriptor.

Planned implementation:

* Add `NativeQueryCoalescer` under Shared Query planning.
* Extend deterministic unit-id helper for coalesced native identities.
* Add focused tests to the Query shared test project.
* Update Query README/planning docs and aggregate Query state.

Implemented:

* Added `NativeQueryCoalescer` implementing SDK `INativeCoalescer`.
* Added `QueryPlanResultLimits` to share least-restrictive result-limit merge policy.
* Extended `QueryPlanUnitIds` with coalesced native unit-id generation.
* Updated `QueryPlanOptimizer` to use `QueryPlanResultLimits`.
* Added `NativeQueryCoalescerTests`.
* Updated Query domain docs and aggregate Query creation state.

Validation:

* `dotnet build src/Cymulate.Integration.Adapters/Shared/Cymulate.Integration.Adapters.Shared/Cymulate.Integration.Adapters.Shared.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false`
  * Passed with 0 warnings and 0 errors.
* `dotnet test src/Cymulate.Integration.Adapters/UnitTests/Query/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test/Cymulate.Integration.Adapters.QueryIntegration.Shared.Test.csproj --no-restore --disable-build-servers -p:UseSharedCompilation=false -v minimal`
  * Passed: 62 tests.
  * Emitted NU1900 warnings because external NuGet/CodeArtifact vulnerability indexes were unavailable.

Verifier repairs:

* Added cancellation checks during output materialization in `NativeQueryCoalescer`.
* Mirrored the same cancellation check in `QueryPlanOptimizer`.
* Added direct cancellation-before-output-materialization tests for native coalescing and time-window optimization.

Final review:

* Final verifier reported no material request-coverage issues after repairs.
* Task-orchestrator code-reviewer pass reported no material implementation-quality issues.
* Final explicit `code-reviewer` pass reported no material implementation-quality issues.

Residual risks:

* Native coalescing remains exact-match on `NormalizedQuery`; semantically equivalent text variants do not merge.
* Least-restrictive result-limit merging can increase upstream fetch volume for mixed capped/unlimited groups.
* Composition-level `INativeCoalescingOptOut` wiring remains future work.
