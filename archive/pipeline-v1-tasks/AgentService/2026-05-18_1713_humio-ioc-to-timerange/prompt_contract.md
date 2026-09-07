Role:
You are a .NET 8 / C# engineer working on the Cymulate Agent Service codebase, specifically the QueryIntegration layer for the Humio (LogScale) SIEM/Falcon vendor.

Goal:
Refactor the Humio integration so it caches data using `generateCacheByTimeRange` instead of `generateCacheByIoc`, removing IOC/keyword filtering from the query layer and letting downstream matching run against the cached payload.

Context:
- File: `Source/Application/Cymulate.Agent.Application.Actions/Actions/QueryIntegration/Logic/Clients/SIEM/Falcon/Humio/HumioApi.cs`
  - Line 117: current `generateCacheByIoc(eQueryResultTypes.Event | eQueryResultTypes.Alert, …, queryByIoc, …)`
  - Line 335: current `queryByIoc(HumioApiInfoModel, CheckConnectionResultModel, IFileStreamWriter<CacheDetailModel>, IReadOnlyCollection<QueryByIocGroupModel>, CancellationToken)`
- File: `Tests/Application/Cymulate.Agent.Application.Integrations.Tests/SIEM/Humio/HumioApiTests.cs`
- Pattern reference: `.claude/skills/refactor-query-integration/SKILL.md`
- Target callback delegate:
  `QueryByTimeRangeCallback<HumioApiInfoModel>(HumioApiInfoModel, CheckConnectionResultModel, IFileStreamWriter<CacheDetailModel>, ISet<QueryByTimeRangeGroupModel>, CancellationToken)`
- The keyword path is still used by `runAdvancedCustomQueryInternalAsync` and `checkConnectionInternalAsync` — leave those untouched.

Constraints:
- Only modify files inside `Source/.../Logic/Clients/SIEM/Falcon/Humio/` and `Tests/.../SIEM/Humio/`.
- Do not modify: `QueryMakerBase`, `AdvancedQueryMakerBase`, `IntegrationApiClientFactory`, `ApiInfoModelFactory`, `eIntegrationProducts`, `HumioApiInfoModel`, auth, response parsing, HTTP error handling.
- Do not change the API endpoint URLs; only the request body shape (`queryString` becomes keyword-less) and the callback signature may change.
- New `queryByTimeRange` callback must match the delegate signature exactly — no lambda wrappers, no extra captured state.
- Do not pass an empty string or dummy keyword to existing keyword builders; introduce a separate keyword-less code path (helper method or explicit branch).
- Remove `queryByIoc` once unreferenced; do not leave dead code.
- Match the existing file's naming style: legacy camelCase private method names and `r`/`i`/`o` prefixes already in use.
- Follow `CLAUDE.md` conventions: explicit types over `var`, braces on all `if` statements, `IsNullOrWhiteSpace` over `IsNullOrEmpty`, no new `#region` blocks, new parameters appended (before `CancellationToken`).
- Do not add NuGet packages.
- Solution must build cleanly.
- `dotnet test --filter FullyQualifiedName~HumioApi` must pass.

Success Criteria:
- `generateCacheByIoc` is no longer referenced in `HumioApi.cs`.
- `generateCacheByTimeRange` is invoked with the result-type set `eQueryResultTypes.Event | eQueryResultTypes.Alert` (same union currently passed to IOC).
- A private `queryByTimeRange` callback exists with the exact `QueryByTimeRangeCallback<HumioApiInfoModel>` signature.
- A keyword-less query path exists for time-range caching (no dummy keyword passed to the existing builder).
- `queryByIoc` is deleted.
- `runAdvancedCustomQueryInternalAsync` and `checkConnectionInternalAsync` continue to use the keyword form unchanged.
- `HumioApiTests` mocks are updated to expect the new keyword-less request body shape; all existing test methods still pass with their current names; assertion of parsed responses is unchanged.
- `dotnet build AgentService.sln` succeeds.
- `dotnet test --filter FullyQualifiedName~HumioApi` succeeds.

Execution Rules:
- Do not assume missing data — if A1 (Humio keyword-less query string) is not resolved, do not implement the new helper; surface the blocker.
- Respect constraints strictly.
- Do not expand scope: no parallel refactor of other vendors, no “drive-by” cleanup outside the Humio folder.
- Keep methods short and focused; extract a helper rather than inflating an existing method.

Output Format:
- Modified C# source files under the Humio folder.
- Modified test file `HumioApiTests.cs`.
- A short summary listing: files touched, the chosen Humio time-range query string, helper methods added, tests adjusted, and the verification command run.

Stop Conditions:
- When all Success Criteria are met.
- When the keyword-less Humio query string (A1) cannot be sourced from official documentation — stop and report blocker rather than guess.
- When a required constraint would have to be violated to proceed.
- When more than one attempt to make tests pass causes additional regressions — revert and report.
