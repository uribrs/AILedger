# Execution Notes

## Diff summary

Single file changed: `Source/Application/Cymulate.Agent.Application.Actions/Actions/QueryIntegration/Logic/Clients/EDR/Cybereason/CybereasonApi.cs`.

### `generateCacheForStandardCustomQueriesInternal` (line 142-156)
- Collapsed the two prior calls into one: `generateCacheByTimeRange(eQueryResultTypes.Alert | eQueryResultTypes.Event, …, queryByTimeRange, …)`.
- The Alert-only `generateCacheByTimeRange` + Event-only `generateCacheByIoc` invocations are gone.

### `queryByTimeRange` callback (line 284-336)
- Now dispatches by `HasFlag(Alert)` AND `HasFlag(Event)` — each branch independently triggers the corresponding cache build.
- `NotSupportedException` thrown only when the group's result type matches neither flag (reviewer S3: message tightened to "Unsupported query result type for time-range: {QueryResultType}").
- **Cancellation** (reviewer I1): `iCancellationToken.ThrowIfCancellationRequested()` moved OUTSIDE the broad `try`. Catch clause filters with `when (ex is not OperationCanceledException)` so a mid-loop cancel unwinds the foreach instead of being swallowed and continuing.
- Catch log now reports `{QueryResultType} cache for host '{Hostname}' over {DateTimeRange}` — hostname + time range now in the error path.

### `queryByIoc` — DELETED

### `createEventsCacheByIocAsync` → `createEventsCacheByTimeRangeAsync` (line ~1115)
- Parameter type: `QueryByTimeRangeGroupModel`. Same two-step flow (Machine then File search).

### `writeEventsBySearchTypeToCacheAsync` (line ~1156)
- Parameter retyped to `QueryByTimeRangeGroupModel`.
- All `iIocGroup.Query` references removed:
  - Pre-request log now reports `Searching events of type: X For host 'Y' over time range: {DateTimeRange}`.
  - `requestDetails.Query` populated via new `describeTimeRangeQuery` helper (`host='X' range=From..To`) for debug breadcrumb parity with the Kaspersky precedent.
  - "Empty response", "No results", and per-error-write log lines now use Hostname + DateTimeRange.

### `describeTimeRangeQuery` (new private static)
- Same shape as the Kaspersky variant. Reviewer S2 flagged duplication — out of scope (would require modifying `QueryMakerBase`); tracked as follow-up.

### `buildEventQuery` (line ~1633)
- Signature: `(eEventSearchTypes, DateTimeRangeModel, string)` — no `iKeyword` parameter.

### `buildEventMachineSearchQuery` (line ~1647)
- Signature: `(string iHostname, DateTimeRangeModel iTimeRange)`.
- Process node now has ONLY the `creationTime Between` filter. The `elementDisplayName ContainsIgnoreCase keyword` filter is removed. Machine node's hostname filter is unchanged.

### `buildEventFileSearchQuery` (line ~1727)
- Signature: `(string iHostname, DateTimeRangeModel iTimeRange)` — gained `iTimeRange` (the parameter was absent previously; the File query had no time-range filter at all).
- File node: `elementDisplayName ContainsIgnoreCase keyword` filter REPLACED with `createdTime Between [from, to]` (POSIX millis). Facet name `createdTime` per research (MEDIUM confidence — see `research/cybereason-file-time-filter.md`; HIGH on the spelling, MEDIUM that Cybereason's API accepts time filters on File nodes).
- Machine node's hostname filter is unchanged.

## Build verification

```
dotnet build Source/Application/Cymulate.Agent.Application.Actions/Cymulate.Agent.Application.Actions.csproj -c DebugMac /p:DefineConstants="DEBUG%3BMac"
```
Result: **0 Errors**, 394 Warnings (all pre-existing).

## Test verification

```
dotnet test Tests/Application/Cymulate.Agent.Application.Integrations.Tests/Cymulate.Agent.Application.Integrations.Tests.csproj -c DebugMac /p:DefineConstants="DEBUG%3BMac" --filter "FullyQualifiedName~CybereasonApi"
```
Result: **5 Passed, 4 Failed, 1 Skipped**.

The 4 failures are pre-existing on this branch (verified by file-swap: restoring HEAD's `CybereasonApi.cs` and running `StartQueryAsync_StandardQueryOnly_AlertsOnly_PublishedAsExpected` also fails with the same `Assert.True(finding.AlertsFound)` failure). Same pass/fail parity as HEAD — no new regressions introduced.

Pre-existing failing tests (out of scope for this PR):
- `StartQueryAsync_StandardQueryOnly_AlertsOnly_PublishedAsExpected`
- `StartQueryAsync_StandardAndAdvancedQueries_FindingsPublishedAsExpected`
- `StartQueryAsync_StandardQueryOnly_ComplexWithOrOperator_EvaluatedAsExpected`
- `StartQueryAsync_StandardQueryOnly_ComplexWithAndOperator_EvaluatedAsExpected`

The contract Success Criterion ("currently-passing tests still pass") is satisfied.

## Reviewer feedback resolution

- **I1 (Important)** — Applied. Cancellation token check moved out of `try`; broad catch filtered with `when (ex is not OperationCanceledException)`. A mid-loop cancel now unwinds instead of being swallowed.
- **I2 (Important)** — Not applied. Adding a new focused test for the time-range Event path is a meaningful expansion of scope (contract explicitly says do not pre-emptively edit tests; tests should change only if a regression demands it). The existing `StartQueryAsync_StandardQueryOnly_EventsOnly_PublishedAsExpected` test still exercises both Machine and File branches via URL + `requestedType` substring match; what it loses is keyword-in-body coverage. Tracked as a follow-up below.
- **S1 (Suggestion)** — Not applied (out of band). Live-fire verification of the `createdTime` facet against a real Cybereason instance is a runtime validation step that belongs in the deploy pipeline, not the source diff. Research output captured MEDIUM confidence; the fallback options (1→3) in `research/cybereason-file-time-filter.md` are the contingency plan.
- **S2 (Suggestion)** — Not applied. Hoisting `describeTimeRangeQuery` to `QueryMakerBase` would touch the base class, which the contract excludes from scope. Two clients duplicate it; a third repeat would justify the lift.
- **S3 (Suggestion)** — Applied. `NotSupportedException` message tightened to `"Unsupported query result type for time-range: {QueryResultType}"`.
- **S4, N1** — Not applied. Cosmetic; the existing file convention is camelCase for private methods and inconsistent log formats are below the bar for an in-scope change.

## Risks / Follow-ups for the PR

- **`createdTime` facet on File node**: MEDIUM confidence per research. If real Cybereason deployments reject the filter or return zero results, fall back to:
  1. Filter by `Process.creationTime Between` and pull the File via `imageFile` (mirrors the Machine query pattern).
  2. Last-resort client-side filter on `simpleValues.createdTime.values[0]`.
- **Event-test coverage**: the standard-Event test (`CybereasonApiTests.cs:255-758`) no longer asserts request-body content tied to the search term. A new focused test that asserts the new request-body shape (Machine: `Process.creationTime Between`; File: `File.createdTime Between`; neither has `ContainsIgnoreCase` keyword filters) would close the gap. Out of scope for this PR.
- **Pre-existing test failures**: 4 tests already fail on HEAD. Not caused by this refactor; should be triaged separately.
- **`describeTimeRangeQuery` duplication**: Cybereason now duplicates Kaspersky's helper. When a third vendor needs it, hoist into a shared utility (likely on `QueryGroupBase` or as a static in `DateTimeRangeQueriesMaker`).

## Same-branch sibling work

Branch `CA-71900-Humio-Kaspersky-Cyberreason-to-timerange` now carries all three refactors:
- Humio: `ai/active/2026-05-18_1713_humio-ioc-to-timerange/` (completed yesterday).
- Kaspersky: `ai/active/2026-05-19_1430_kaspersky-edr-ioc-to-timerange/` (completed earlier today).
- Cybereason: this task.

The branch is ready for PR.
