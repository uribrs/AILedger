# Assumptions

## A1 — Humio queryjobs endpoint accepts a keyword-less query string
- Status: OPEN
- Statement: The Humio/LogScale `POST {BaseUrl}/api/v1/repositories/{repo}/queryjobs` endpoint accepts a `queryString` that has no keyword/IOC filter, returning all events/alerts inside `start`–`end` (e.g., `"*"`, an empty pipeline like `head(limit=N)`, or a trivial true predicate). We need the concrete query-string value that is safe for both Cloud and self-hosted Humio/LogScale and that is accepted by the test mocks.
- Why it matters: A wrong query string either (a) returns nothing in production, (b) errors out on the server, or (c) blows the result limit. This is the only piece of external behavior the refactor depends on.
- Resolution path: route to `technical-researcher` to consult Humio/LogScale official docs (HumioQuery API / LogScale REST API) for the canonical "all events in window" form, and confirm that omitting the keyword filter is supported on the existing `queryjobs` endpoint.

## A2 — Existing `tryCreateQueryJobsAsync` is the right insertion point
- Status: OPEN
- Statement: The new keyword-less query is best implemented by branching inside the existing `tryCreateQueryJobsAsync` (or adding a sibling `tryCreateTimeRangeQueryJobsAsync`) rather than introducing a new HTTP path. The `advanced custom query` path (`runAdvancedCustomQueryInternalAsync`) and `checkConnectionInternalAsync` must keep using the keyword form unchanged.
- Why it matters: Affects whether a new helper method is created or the existing one is overloaded; informs the test-mock surface.
- Resolution path: validated during implementation by inspecting all call sites of `tryCreateQueryJobsAsync` and `tryFetchQueryResultsAsync`. Light reading only — no external research.

## A3 — Result-type union semantics
- Status: VALIDATED
- Statement: `generateCacheByTimeRange` can be called once with `eQueryResultTypes.Event | eQueryResultTypes.Alert` and the callback dispatches per-flag, mirroring what `queryByIoc` already does today.
- Evidence: existing `queryByIoc` body already inspects `iocGroup.QueryResultType.HasFlag(...)` for both `Alert` and `Event` and dispatches to the same `tryFetchQueryResultsAsync` with different repositories.

## A4 — Time-range groups carry the same date window semantics as IOC groups
- Status: VALIDATED
- Statement: `QueryByTimeRangeGroupModel.DateTimeRange` (inherited from `QueryGroupBase`) provides the same `From`/`To` window the current code reads from `iocGroup.DateTimeRange`.
- Evidence: confirmed in the project-local skill (`refactor-query-integration/SKILL.md`, "Group Model Differences").

## A5 — Test names need no `ByIoc → ByTimeRange` rename
- Status: VALIDATED
- Statement: The existing `HumioApiTests` overrides (e.g., `StartQueryAsync_StandardQueryOnly_AlertsOnly_PublishedAsExpected`) come from `AdvancedQueryMakerTestsBase` and do not encode `ByIoc` in their names; only mock bodies and asserted request payloads need updating, not method names.
- Evidence: `grep "public override async Task"` against the test file shows no `ByIoc`/`ByTimeRange` in any method name.
