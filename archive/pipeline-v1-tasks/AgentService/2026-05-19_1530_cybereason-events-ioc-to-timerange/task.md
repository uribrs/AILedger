# Cybereason EDR Integration: Events IOC → TimeRange Refactor

## Summary

Cybereason's Alert path was already converted to `generateCacheByTimeRange` in earlier work. This task converts the remaining **Event** path from `generateCacheByIoc` to `generateCacheByTimeRange`, so cached events come from a hostname + time-range query rather than a per-IOC keyword query.

## Scope

In scope:
- `Source/Application/Cymulate.Agent.Application.Actions/Actions/QueryIntegration/Logic/Clients/EDR/Cybereason/CybereasonApi.cs`
- `Tests/Application/Cymulate.Agent.Application.Integrations.Tests/EDR/CybereasonApiTests.cs` (only if needed — see Tests section)

Out of scope:
- `QueryMakerBase`, `AdvancedQueryMakerBase`, factories, `eIntegrationProducts`, `CybereasonApiInfoModel`, `CybereasonCheckConnectionResultModel`, `CybereasonAlertCacheDataModel`, `CybereasonMalopInfoModel`.
- Auth (`checkConnectionInternalAsync`, `setRequestAuthInfo`), error handling, response parsing (`parseRawEventsResponse`, `parseRawAlertsResponse`, `parseRawIncidentsResponse`).
- The Alert path — already TimeRange.
- The Advanced-query path (`runAdvancedCustomQueryInternalAsync` → `tryFetchEventsByAdvancedQueryAsync`) — uses a different code path that takes a caller-supplied JSON; no overlap with the IOC helpers.
- Humio and Kaspersky (separate refactors, already done on this branch).

## Target shape

- `generateCacheForStandardCustomQueriesInternal` makes a single `generateCacheByTimeRange(eQueryResultTypes.Alert | eQueryResultTypes.Event, …, queryByTimeRange, …)` call (combined).
- `queryByTimeRange` dispatches by `QueryResultType.HasFlag(Alert)` and `HasFlag(Event)` to run the existing Alert pipeline and the new Event pipeline respectively.
- `createEventsCacheByIocAsync` → `createEventsCacheByTimeRangeAsync(QueryByTimeRangeGroupModel)`.
- `writeEventsBySearchTypeToCacheAsync` → takes `QueryByTimeRangeGroupModel` instead of `QueryByIocGroupModel`.
- `buildEventQuery` / `buildEventMachineSearchQuery` / `buildEventFileSearchQuery` drop the `iKeyword` parameter:
  - Machine query: remove the `elementDisplayName ContainsIgnoreCase keyword` filter from the Process node. Keep hostname filter on Machine node; keep `creationTime Between` filter on Process node.
  - File query: remove the `elementDisplayName ContainsIgnoreCase keyword` filter from the File node. **Add** a time-range filter on File node (facet to be confirmed by research — see assumption A1).
- `queryByIoc` deleted; `createEventsCacheByIocAsync` is renamed/deleted.

## Tests

`CybereasonApiTests.cs` mock setup keys on URL + a `requestedType` substring (`"requestedType":"Machine"` / `"requestedType":"File"`) — not on the keyword. Tests assert that the searchTerm string appears in the response evidences (where it was injected into the mocked response, not the request body). Therefore the existing tests should pass unchanged after the refactor. Verification step runs `dotnet test --filter FullyQualifiedName~CybereasonApi`; any failures get triaged.

## References

- Existing call sites: `CybereasonApi.cs:148` (Alert TimeRange — keep), `CybereasonApi.cs:156` (Event IOC — convert).
- Existing IOC callback: `CybereasonApi.cs:322`.
- Existing TimeRange callback (Alert-only today): `CybereasonApi.cs:284`.
- Event query builders: `CybereasonApi.cs:1642`, `1655`, `1744`.
- Advanced query (untouched): `CybereasonApi.cs:165` → `CybereasonApi.cs:635`.
- Conversion pattern: `.claude/skills/refactor-query-integration/SKILL.md`.
- Vendor research: `research/cybereason-file-time-filter.md`.
- Same-branch precedents: Humio (`ai/active/2026-05-18_1713_humio-ioc-to-timerange/`), Kaspersky (`ai/active/2026-05-19_1430_kaspersky-edr-ioc-to-timerange/`).
