# Kaspersky EDR Integration: IOC → TimeRange Refactor

## Summary

Convert the Kaspersky EDR query integration from per-IOC querying to time-range querying. The IOC/keyword filter must be removed from the Kaspersky Security Center API request body; the integration must fetch all alerts inside the time window for the given hostname and let downstream code do IOC matching against the cache.

## Scope

In scope:
- `Source/Application/Cymulate.Agent.Application.Actions/Actions/QueryIntegration/Logic/Clients/EDR/Kaspersky/KasperskyEdrApi.cs`

Out of scope:
- `QueryMakerBase`, `AdvancedQueryMakerBase`, `ApiClientBase`
- `IntegrationApiClientFactory`, `ApiInfoModelFactory`
- `eIntegrationProducts` enum
- `KasperskyEdrApiInfoModel`, `KasperskyEdrCheckConnectionResultModel`
- Auth flow (`checkConnectionInternalAsync`, `setRequestAuthInfo`)
- Response parsing (`parseRawAlertsResponse`, `normalizeSeverity`)
- HTTP error handling (`tryHandleApiCallError`)
- Cybereason / Humio (separate refactors)

## Target shape

- `generateCacheForStandardCustomQueriesInternal` calls
  `generateCacheByTimeRange(eQueryResultTypes.Alert, …, queryByTimeRange, …)`
- Private callback `queryByTimeRange(KasperskyEdrApiInfoModel, CheckConnectionResultModel, IFileStreamWriter<CacheDetailModel>, ISet<QueryByTimeRangeGroupModel>, CancellationToken)`
- `queryByIoc` is removed
- `fetchAlertsAsync`, `writeAlertsToCacheAsync`, `createEventProcessingTaskAsync`, `createAlertsRequestContent` are updated to accept `QueryByTimeRangeGroupModel` (rename the parameter) and the request body no longer carries `EVP_FTX_QUERY`

## Tests

No Kaspersky integration tests exist under `Tests/`. None are added in this refactor; test creation is out of scope. The Humio precedent updated mocks because tests already existed; that does not apply here.

## References

- Existing IOC call site: `KasperskyEdrApi.cs:132`
- Existing IOC callback: `KasperskyEdrApi.cs:141`
- Existing request body builder: `KasperskyEdrApi.cs:347` (`createAlertsRequestContent`)
- KSC OpenAPI: https://support.kaspersky.com/help/KSC/13/KSCAPI/a00125.html (linked at top of `KasperskyEdrApi.cs`)
- Conversion pattern: `.claude/skills/refactor-query-integration/SKILL.md`
- Vendor-API skill: `.claude/skills/vendor-integration-expert/SKILL.md`
- Same-branch precedent: `ai/active/2026-05-18_1713_humio-ioc-to-timerange/`
