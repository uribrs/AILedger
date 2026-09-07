# Decisions

- Use a single `generateCacheByTimeRange(eQueryResultTypes.Alert, …)` call — matches the existing single-result-type scope; no combined `Event | Alert` because `Event` is not supported.
- Callback name: `queryByTimeRange` (camelCase, matches legacy file style; mirrors Humio precedent).
- Remove `EVP_FTX_QUERY` from `pFilter` outright rather than passing an empty string — the field is the IOC keyword filter, not a required field. Removal yields "no keyword constraint".
- Rename `iIocGroup` parameters to `iTimeRangeGroup` (and the local variable correspondingly) in `fetchAlertsAsync`, `writeAlertsToCacheAsync`, `createEventProcessingTaskAsync`, `createAlertsRequestContent`. Drop the `IocGroup` suffix from method-internal vocabulary.
- `KeywordRequestDetailsModel.Query` is set to `""` (empty string) in `createEventProcessingTaskAsync` — preserves the field for diagnostics without inventing data. Mirrors Humio decision.
- Replace `iIocGroup.Query` interpolations in log lines with `iTimeRangeGroup.DateTimeRange` / `Hostname` — keeps the log informative without referring to a value that no longer exists.
- `queryByIoc` is deleted, not deprecated.
- `requestDetails.Query` field name and existence in `KeywordRequestDetailsModel` are not changed — that is a cross-cutting model and out of scope.
- Pagination (50k cap) is left untouched in this PR — pre-existing concern, not part of this refactor.
- Reviewer surface: keep this PR within `KasperskyEdrApi.cs` only.
