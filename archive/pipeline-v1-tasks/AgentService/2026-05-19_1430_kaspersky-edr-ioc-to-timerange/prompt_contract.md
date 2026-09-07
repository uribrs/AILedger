Role:
You are a .NET 8 / C# engineer working on the Cymulate Agent Service codebase, specifically the QueryIntegration EDR layer for the Kaspersky Security Center vendor.

Goal:
Refactor the Kaspersky EDR integration so it caches alerts using `generateCacheByTimeRange` instead of `generateCacheByIoc`, removing the `EVP_FTX_QUERY` (IOC/keyword) filter from the Kaspersky Security Center request body and letting downstream matching run against the cached payload.

Context:
- File: `Source/Application/Cymulate.Agent.Application.Actions/Actions/QueryIntegration/Logic/Clients/EDR/Kaspersky/KasperskyEdrApi.cs`
  - Line 132: current `generateCacheByIoc(eQueryResultTypes.Alert, …, queryByIoc, …)`
  - Line 141: current `queryByIoc(KasperskyEdrApiInfoModel, CheckConnectionResultModel, IFileStreamWriter<CacheDetailModel>, IReadOnlyCollection<QueryByIocGroupModel>, CancellationToken)`
  - Line 179: `fetchAlertsAsync(QueryByIocGroupModel, …)`
  - Line 224: `writeAlertsToCacheAsync(IntegrationApiCallResultModel<string>, QueryByIocGroupModel, …)`
  - Line 288: `createEventProcessingTaskAsync(QueryByIocGroupModel, …)` — sets `requestDetails.Query = iIocGroup.Query`
  - Line 347: `createAlertsRequestContent(QueryByIocGroupModel)` — emits the JSON body that includes:
    - `pFilter.KLEVP_EVENT_HOST_NETBIOSNAME = iIocGroup.Hostname`
    - `pFilter.KLEVP_EVENT_RISE_TIME_LEAST = iIocGroup.DateTimeRange.From`
    - `pFilter.KLEVP_EVENT_RISE_TIME_GREATEST = iIocGroup.DateTimeRange.To`
    - `pFilter.EVP_FTX_QUERY = iIocGroup.Query`   ← MUST BE REMOVED (this is the IOC keyword filter)
- The class supports a single result type: `eQueryResultTypes.Alert` (passed to the base ctor and asserted in the callback).
- Target callback delegate:
  `QueryByTimeRangeCallback<KasperskyEdrApiInfoModel>(KasperskyEdrApiInfoModel, CheckConnectionResultModel, IFileStreamWriter<CacheDetailModel>, ISet<QueryByTimeRangeGroupModel>, CancellationToken)`
- Pattern reference: `.claude/skills/refactor-query-integration/SKILL.md`
- Vendor reference: `.claude/skills/vendor-integration-expert/SKILL.md`
- Same-branch precedent (completed yesterday): `ai/active/2026-05-18_1713_humio-ioc-to-timerange/`
- No tests exist for this integration under `Tests/`. Test creation is explicitly out of scope.

Constraints:
- Only modify `KasperskyEdrApi.cs`.
- Do not modify: `QueryMakerBase`, `AdvancedQueryMakerBase`, `IntegrationApiClientFactory`, `ApiInfoModelFactory`, `eIntegrationProducts`, `KasperskyEdrApiInfoModel`, `KasperskyEdrCheckConnectionResultModel`, auth flow, response parsing, HTTP error handling.
- Do not change the Kaspersky API endpoints (`Session.StartSession`, `EventProcessingFactory.CreateEventProcessing2`, `EventProcessing.GetRecordRange`).
- The request body change is limited to removing the `EVP_FTX_QUERY` field from `pFilter`. Time range and hostname filters stay.
- New `queryByTimeRange` callback must match the delegate signature exactly — no lambda wrappers, no extra captured state.
- Do not pass an empty string as a dummy IOC keyword through the existing code path. Remove the field from the JSON body outright.
- `requestDetails.Query` (which currently mirrored the IOC value) should be left empty string — `KeywordRequestDetailsModel.Query` is a record of what was queried; there is no IOC anymore.
- Remove `queryByIoc` once unreferenced; do not leave dead code.
- Match the existing file's naming style: legacy camelCase private method names, `i`/`o` parameter prefixes, `c`-prefixed private constants (e.g., `cTimeFilterFormat`).
- Follow `CLAUDE.md` conventions: explicit types over `var`, braces on all `if` statements, `IsNullOrWhiteSpace` over `IsNullOrEmpty`, no new `#region` blocks introduced in modified code (existing regions may stay as they are in this file), new parameters appended (before `CancellationToken`).
- Do not add NuGet packages.
- Solution must build cleanly: `dotnet build AgentService.sln` succeeds.

Success Criteria:
- `generateCacheByIoc` is no longer referenced in `KasperskyEdrApi.cs`.
- `generateCacheByTimeRange` is invoked with `eQueryResultTypes.Alert` (matching the current result-type scope).
- A private `queryByTimeRange` callback exists with the exact `QueryByTimeRangeCallback<KasperskyEdrApiInfoModel>` signature.
- `fetchAlertsAsync`, `writeAlertsToCacheAsync`, `createEventProcessingTaskAsync`, `createAlertsRequestContent` all take `QueryByTimeRangeGroupModel` instead of `QueryByIocGroupModel`.
- The JSON body built by `createAlertsRequestContent` no longer contains `EVP_FTX_QUERY`; it still contains `KLEVP_EVENT_HOST_NETBIOSNAME`, `KLEVP_EVENT_RISE_TIME_LEAST`, `KLEVP_EVENT_RISE_TIME_GREATEST`, `vecFieldsToReturn`, `lifetimeSec`.
- `queryByIoc` is deleted.
- Log lines that previously interpolated `iocGroup.Query` are updated to use the time-range (e.g., `DateTimeRange` / `Hostname`) — no `Query` references remain.
- `dotnet build AgentService.sln` succeeds.

Execution Rules:
- Do not assume missing data.
- Respect constraints strictly.
- Do not expand scope: no parallel refactor of Cybereason or other vendors, no “drive-by” cleanup outside the Kaspersky file.
- Keep methods short and focused; if a helper grows, extract it inside the same file.

Output Format:
- Modified C# source file `KasperskyEdrApi.cs`.
- A short summary listing: lines touched (callback swap, body change, helper renames), the resulting body shape, and the build verification command run.

Stop Conditions:
- When all Success Criteria are met.
- When a required constraint would have to be violated to proceed.
- When build fails after the refactor and the root cause is not a mechanical signature mismatch fix.
