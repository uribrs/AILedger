Role:
You are a .NET 8 / C# engineer working on the Cymulate Agent Service codebase, specifically the QueryIntegration EDR layer for the Cybereason vendor.

Goal:
Refactor the Cybereason integration's Event-result path from `generateCacheByIoc` to `generateCacheByTimeRange`, so cached events come from a hostname + time-range VisualSearch query rather than a per-IOC keyword query. The Alert path was already converted in earlier work; this task touches only the Event path.

Context:
- File: `Source/Application/Cymulate.Agent.Application.Actions/Actions/QueryIntegration/Logic/Clients/EDR/Cybereason/CybereasonApi.cs`
  - Line 148-154: existing `generateCacheByTimeRange(eQueryResultTypes.Alert, …, queryByTimeRange, …)` — keep, but extend the bitmask and callback to handle Event as well.
  - Line 156-162: existing `generateCacheByIoc(eQueryResultTypes.Event, …, queryByIoc, …)` — delete; collapse into the time-range call.
  - Line 284-320: existing `queryByTimeRange` callback (Alert-only). Extend to handle Event branch alongside Alert branch.
  - Line 322-359: existing `queryByIoc` callback (Event-only). Delete after collapse.
  - Line 1121-1140: `createEventsCacheByIocAsync(IFileStreamWriter<CacheDetailModel>, …, QueryByIocGroupModel iIocGroup, CancellationToken)` — convert to `createEventsCacheByTimeRangeAsync(QueryByTimeRangeGroupModel iTimeRangeGroup)`.
  - Line 1162-1640: `writeEventsBySearchTypeToCacheAsync` — convert `QueryByIocGroupModel` parameter to `QueryByTimeRangeGroupModel`. All `iIocGroup.Query` references must be removed (the keyword no longer flows into the request).
  - Line 1642-1742: `buildEventQuery` and `buildEventMachineSearchQuery` — drop the `iKeyword` parameter and remove the `elementDisplayName ContainsIgnoreCase keyword` filter from the Process node. Keep hostname filter on Machine node and `creationTime Between` filter on Process node.
  - Line 1744-1822: `buildEventFileSearchQuery` — drop the `iKeyword` parameter and remove the `elementDisplayName ContainsIgnoreCase keyword` filter from the File node. ADD a time-range filter on the File node (facet name confirmed by research; default `createdTime` with `Between`).
- Test file: `Tests/Application/Cymulate.Agent.Application.Integrations.Tests/EDR/CybereasonApiTests.cs`. Mocks key on URL + `"requestedType":"Machine"` / `"requestedType":"File"` substrings (verified by grep at lines 497-500, 661-669, 1198-1201, 1367-1370, 2399-2402, 2810-2812, 3271-3274). Test assertions check parsed response evidences, not request body content. Tests should pass unchanged.
- The Advanced-query path (`runAdvancedCustomQueryInternalAsync` → `tryFetchEventsByAdvancedQueryAsync`, lines 165-186 and 635-1119) is OUT OF SCOPE; it parses a caller-supplied JSON query, does not invoke the IOC helpers, and must continue to work.
- Pattern reference: `.claude/skills/refactor-query-integration/SKILL.md`.
- Vendor research (resolving the File time-facet question): `research/cybereason-file-time-filter.md`.

Constraints:
- Only files modified: `CybereasonApi.cs` (always) and `CybereasonApiTests.cs` (only if a test regression demonstrates a real mismatch — do not pre-emptively edit tests).
- Do not modify: `QueryMakerBase`, `AdvancedQueryMakerBase`, `IntegrationApiClientFactory`, `ApiInfoModelFactory`, `eIntegrationProducts`, `CybereasonApiInfoModel`, `CybereasonCheckConnectionResultModel`, `CybereasonAlertCacheDataModel`, `CybereasonMalopInfoModel`.
- Do not change Cybereason API endpoints (`/login.html`, `/rest/detection/inbox`, `/rest/detection/details`, `/rest/visualsearch/query/simple`).
- Do not change `runAdvancedCustomQueryInternalAsync` or `tryFetchEventsByAdvancedQueryAsync`.
- Do not change response parsing (`parseRawEventsResponse`, `parseRawAlertsResponse`, `parseRawIncidentsResponse`) or auth flow.
- The combined `queryByTimeRange` must match the `QueryByTimeRangeCallback<CybereasonApiInfoModel>` delegate signature exactly — no lambda, no extra captures.
- Do not pass an empty string as a dummy keyword anywhere. Remove keyword fields from the JSON builders outright. Builder signatures change to drop the `iKeyword` parameter.
- Remove `queryByIoc` and `createEventsCacheByIocAsync` (or rename the latter to `createEventsCacheByTimeRangeAsync`) once unreferenced. No dead code.
- Match existing file's naming style: legacy camelCase private methods, `i`/`o` parameter prefixes, `c`-prefixed private constants. Keep `r` prefix style only where it pre-exists; do not introduce it.
- Follow CLAUDE.md conventions: explicit types over `var`, braces on all `if` statements, `IsNullOrWhiteSpace` over `IsNullOrEmpty`, no new `#region` blocks introduced in modified regions (existing regions may stay), new parameters appended before `CancellationToken`.
- Do not add NuGet packages.
- `dotnet build` of `Cymulate.Agent.Application.Actions.csproj` must succeed.
- `dotnet test --filter FullyQualifiedName~CybereasonApi` must pass; if it doesn't, diagnose the test mismatch before making any test edits.

Success Criteria:
- `generateCacheByIoc` and `queryByIoc` are no longer referenced in `CybereasonApi.cs`.
- `generateCacheByTimeRange` is invoked once, with the bitmask `eQueryResultTypes.Alert | eQueryResultTypes.Event`, and the callback is the (extended) `queryByTimeRange`.
- `queryByTimeRange` callback handles both Alert and Event branches by `HasFlag` dispatch. Signature still matches `QueryByTimeRangeCallback<CybereasonApiInfoModel>` exactly.
- A private method (e.g. `createEventsCacheByTimeRangeAsync`) accepts `QueryByTimeRangeGroupModel` and is the entry point for Event caching.
- `writeEventsBySearchTypeToCacheAsync` accepts `QueryByTimeRangeGroupModel`. All `iIocGroup.Query` references in the method are gone.
- `buildEventQuery(eEventSearchTypes iSearchType, DateTimeRangeModel iTimeRange, string iHostname)` — no `iKeyword` parameter.
- `buildEventMachineSearchQuery(string iHostname, DateTimeRangeModel iTimeRange)` — no `iKeyword`; Process node has only the `creationTime Between` filter (no `elementDisplayName ContainsIgnoreCase`).
- `buildEventFileSearchQuery(string iHostname, DateTimeRangeModel iTimeRange)` — no `iKeyword`; File node has a `Between` filter on the time facet identified by research; Machine node retains hostname filter.
- `queryByIoc` is deleted; `createEventsCacheByIocAsync` is deleted or renamed.
- `runAdvancedCustomQueryInternalAsync` and `tryFetchEventsByAdvancedQueryAsync` are unchanged.
- `dotnet build` Actions project: 0 errors.
- `dotnet test --filter FullyQualifiedName~CybereasonApi`: all currently-passing tests still pass.

Execution Rules:
- Do not assume missing data — if research returns LOW confidence on the File time facet, stop and report rather than guess.
- Respect constraints strictly.
- Do not expand scope: no Advanced-query rework, no logging cleanup beyond what the IOC→TimeRange terminology change requires, no test refactors unless a real failure demands one.
- Keep methods short. Extract a helper inside the same file rather than inflating an existing method.

Output Format:
- Modified `CybereasonApi.cs`.
- Possibly modified `CybereasonApiTests.cs` (only if tests fail and require alignment).
- A short summary listing: lines touched, the chosen File-node time facet, helper signature changes, test result, and the build verification command run.

Stop Conditions:
- When all Success Criteria are met.
- When research returns LOW confidence on the File time-facet — stop and surface the blocker; do not guess.
- When a constraint would have to be violated.
- When a test fails for a reason not addressable by a mock alignment in `CybereasonApiTests.cs`.
