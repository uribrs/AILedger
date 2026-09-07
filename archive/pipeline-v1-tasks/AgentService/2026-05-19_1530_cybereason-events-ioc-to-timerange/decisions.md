# Decisions

- Collapse the two `generateCache*` calls into a single `generateCacheByTimeRange(eQueryResultTypes.Alert | eQueryResultTypes.Event, …, queryByTimeRange, …)` — leverages the existing Alert TimeRange callback, no second call needed.
- Extend `queryByTimeRange` callback with a second `HasFlag(Event)` branch alongside the existing `HasFlag(Alert)` branch. Throw `NotSupportedException` only if neither flag is set.
- Rename `createEventsCacheByIocAsync` → `createEventsCacheByTimeRangeAsync`. Same private-method visibility, same call shape from the callback, just takes `QueryByTimeRangeGroupModel`.
- Convert `writeEventsBySearchTypeToCacheAsync` in place — change parameter type from `QueryByIocGroupModel` to `QueryByTimeRangeGroupModel`. Rename `iIocGroup` → `iTimeRangeGroup`. Drop `iIocGroup.Query` references; replace log strings with `Hostname` / `DateTimeRange` content. The `requestDetails.Query` field is populated with a descriptive string (`host='X' range=From..To`) for debug breadcrumb parity with Kaspersky precedent.
- Drop `iKeyword` from `buildEventQuery`, `buildEventMachineSearchQuery`, `buildEventFileSearchQuery` signatures.
- Machine query: remove the Process-node `elementDisplayName ContainsIgnoreCase keyword` filter. Keep Machine-node hostname filter and Process-node `creationTime Between` filter unchanged.
- File query: remove the File-node `elementDisplayName ContainsIgnoreCase keyword` filter; ADD a File-node `Between` filter on the time facet selected by research (`createdTime` pending A1 confirmation). Keep Machine-node hostname filter unchanged.
- Delete `queryByIoc` outright. No deprecation, no aliasing.
- Match Humio precedent on `requestDetails.Query`: do NOT leave it empty when a meaningful descriptor is available; populate with hostname + time-range string.
- Tests: do NOT pre-emptively modify. Run them after the refactor; only edit if mocks legitimately fail to match the new request body, and only the minimum to realign.
- Reviewer surface: keep changes confined to `CybereasonApi.cs` (+ tests only if forced).
