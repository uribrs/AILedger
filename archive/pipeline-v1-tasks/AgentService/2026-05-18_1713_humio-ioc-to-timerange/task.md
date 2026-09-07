# Humio Integration: IOC → TimeRange Refactor

## Summary

Convert the Humio (LogScale) SIEM/Falcon query integration from per-IOC querying to time-range querying. The IOC/keyword filter must be removed from the query layer; the integration must fetch all data inside the time window and let downstream code do IOC matching against the cache.

## Scope

In scope:
- `Source/Application/Cymulate.Agent.Application.Actions/Actions/QueryIntegration/Logic/Clients/SIEM/Falcon/Humio/HumioApi.cs`
- Any helper file inside the same `Humio/` directory if a new keyword-less query builder needs to live there
- `Tests/Application/Cymulate.Agent.Application.Integrations.Tests/SIEM/Humio/HumioApiTests.cs`

Out of scope:
- `QueryMakerBase`, `AdvancedQueryMakerBase`
- `IntegrationApiClientFactory`, `ApiInfoModelFactory`
- `eIntegrationProducts` enum
- `HumioApiInfoModel`
- Auth flow, response parsing, HTTP error handling

## Target shape

- `generateCacheForStandardCustomQueriesInternal` calls
  `generateCacheByTimeRange(eQueryResultTypes.Event | eQueryResultTypes.Alert, …, queryByTimeRange, …)`
- Private callback `queryByTimeRange(HumioApiInfoModel, CheckConnectionResultModel, IFileStreamWriter<CacheDetailModel>, ISet<QueryByTimeRangeGroupModel>, CancellationToken)`
- `queryByIoc` is removed
- A keyword-less Humio query string is used in `tryCreateQueryJobsAsync` (or via a new helper) so the cached payload reflects all events/alerts in the window for the configured repositories

## References

- Existing IOC call site: `HumioApi.cs:117`
- Existing IOC callback: `HumioApi.cs:335`
- Conversion pattern: `.claude/skills/refactor-query-integration/SKILL.md`
