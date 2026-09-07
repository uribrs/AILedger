# Decisions

- Use a single `generateCacheByTimeRange` call with combined `eQueryResultTypes.Event | eQueryResultTypes.Alert` — preserves current IOC-call shape.
- Callback name: `queryByTimeRange` (camelCase, matching legacy file style; no PascalCase rename of surrounding private methods).
- Do not pass a dummy keyword into the existing keyword path; instead introduce a keyword-less branch/helper for building the time-range-only Humio query body. Exact query-string value is deferred to research (A1).
- `queryByIoc` is deleted, not deprecated. Once unreferenced, dead code is removed.
- `tryFetchQueryResultsAsync` is updated to accept `QueryByTimeRangeGroupModel` instead of `QueryByIocGroupModel`, and any `iIocGroup.Query` reads are replaced with either the time-range pipeline string built by the new helper or with logging that does not require a keyword.
- `runAdvancedCustomQueryInternalAsync` and `checkConnectionInternalAsync` keep the keyword form of `tryCreateQueryJobsAsync` — they have a real keyword and are out of refactor scope.
- Test updates: mock the new request body shape (no keyword in `queryString`, `start`/`end` still present) and keep all response/parse assertions intact. No test-method renames.
- Reviewer surface: keep this PR within Humio files only — no cross-cutting infrastructure changes.
