# Verifier — Pass 1
Date: 2026-05-19
Verdict: PASS

## Success Criteria

1. **`generateCacheByIoc` and `queryByIoc` no longer referenced in `CybereasonApi.cs`** — PASS
   Evidence: `grep -nE "generateCacheByIoc|queryByIoc|createEventsCacheByIocAsync|iIocGroup|iKeyword|QueryByIocGroupModel" CybereasonApi.cs` returns zero matches across the file. The git diff also shows the previous `generateCacheByIoc(eQueryResultTypes.Event, …, queryByIoc, …)` block (HEAD lines 156-162) removed cleanly from `generateCacheForStandardCustomQueriesInternal`.

2. **`generateCacheByTimeRange` invoked once with bitmask `Alert | Event` and callback `queryByTimeRange`** — PASS
   Evidence: `CybereasonApi.cs:148-154`:
   ```
   generateCacheByTimeRange(supportedTimeRangeQueriesResultsType: eQueryResultTypes.Alert | eQueryResultTypes.Event,
                            iStandardCustomQueries,
                            iCacheWriter,
                            iConnectionResult,
                            iApiInfo,
                            queryByTimeRange,
                            iCancellationToken);
   ```
   Only one such invocation in the file (verified by grep — single match at line 148).

3. **`queryByTimeRange` signature matches `QueryByTimeRangeCallback<CybereasonApiInfoModel>` exactly** — PASS
   Delegate (`Events/QueryByTimeRangeCallback.cs:6-11`):
   `(TApiInfo iApiInfo, CheckConnectionResultModel iConnectionResult, IFileStreamWriter<CacheDetailModel> iCacheWriter, ISet<QueryByTimeRangeGroupModel> iTimeRangeFilters, CancellationToken iCancellationToken)`
   Implementation (`CybereasonApi.cs:276-280`):
   `private void queryByTimeRange(CybereasonApiInfoModel iApiInfo, CheckConnectionResultModel iCheckConnectionResult, IFileStreamWriter<CacheDetailModel> iCacheWriter, ISet<QueryByTimeRangeGroupModel> iTimeRangeGroups, CancellationToken iCancellationToken)`
   Same arity, same types, same order. Method group conversion is valid (parameter-name differences don't affect signature). Not a lambda, no captures.

4. **Callback dispatches by `HasFlag(Alert)` and `HasFlag(Event)` — both branches present** — PASS
   `CybereasonApi.cs:295-304` calls `createAlertsCacheByTimeRangeAsync` under `HasFlag(eQueryResultTypes.Alert)`; `CybereasonApi.cs:306-315` calls `createEventsCacheByTimeRangeAsync` under `HasFlag(eQueryResultTypes.Event)`. Guard at lines 289-293 throws `NotSupportedException` if neither flag is set, matching contract intent.

5. **`createEventsCacheByTimeRangeAsync(QueryByTimeRangeGroupModel)` exists; `createEventsCacheByIocAsync` gone** — PASS
   Defined at `CybereasonApi.cs:1092-1111`, accepts `QueryByTimeRangeGroupModel iTimeRangeGroup`. Calls `writeEventsBySearchTypeToCacheAsync` twice (Machine + File). `createEventsCacheByIocAsync` symbol absent (grep returns nothing).

6. **`writeEventsBySearchTypeToCacheAsync` accepts `QueryByTimeRangeGroupModel`; no `iIocGroup.Query` references** — PASS
   Signature at `CybereasonApi.cs:1133-1138` takes `QueryByTimeRangeGroupModel iTimeRangeGroup`. Grep for `iIocGroup` returns zero matches in the whole file. Method body (lines 1140-1612) uses `iTimeRangeGroup.Hostname` and `iTimeRangeGroup.DateTimeRange` exclusively; query body is built from time range + hostname via `buildEventQuery(iSearchType, iTimeRangeGroup.DateTimeRange, iTimeRangeGroup.Hostname)` at line 1155-1157.

7. **`buildEventQuery(eEventSearchTypes, DateTimeRangeModel, string)` — no `iKeyword`** — PASS
   `CybereasonApi.cs:1619-1629`: `private static JObject buildEventQuery(eEventSearchTypes iSearchType, DateTimeRangeModel iTimeRange, string iHostname)`. No `iKeyword` parameter. Switch dispatches to file/machine helpers passing only `iHostname, iTimeRange`.

8. **`buildEventMachineSearchQuery(string, DateTimeRangeModel)` — no `iKeyword`; Process node only has `creationTime Between`** — PASS
   `CybereasonApi.cs:1631-1708`: signature `(string iHostname, DateTimeRangeModel iTimeRange)`. Process node (lines 1659-1676) has exactly one filter, `facetName: "creationTime"` with `filterType: "Between"` and `[from.ToPosixTimestamp(), to.ToPosixTimestamp()]`. No `elementDisplayName ContainsIgnoreCase` filter on Process. Machine node (lines 1638-1658) retains hostname `elementDisplayName ContainsIgnoreCase` filter as required.

9. **`buildEventFileSearchQuery(string, DateTimeRangeModel)` — no `iKeyword`; File node has `Between` on `createdTime`; Machine node keeps hostname filter** — PASS
   `CybereasonApi.cs:1710-1789`: signature `(string iHostname, DateTimeRangeModel iTimeRange)`. File node (lines 1717-1739) filter: `facetName: "createdTime"`, `filterType: "Between"`, values `[from.ToPosixTimestamp(), to.ToPosixTimestamp()]`. `createdTime` matches the research recommendation (research file confidence: MEDIUM, but explicitly disambiguates that File facet is `createdTime`, NOT `creationTime`). Machine node (lines 1740-1755) keeps `elementDisplayName ContainsIgnoreCase` hostname filter.

10. **`queryByIoc` deleted** — PASS
    Grep confirms no occurrences anywhere in `CybereasonApi.cs`. Diff confirms the previous callback body (HEAD lines 322-359) removed.

11. **`runAdvancedCustomQueryInternalAsync` and `tryFetchEventsByAdvancedQueryAsync` unchanged** — PASS
    Method-by-method diff:
    - `runAdvancedCustomQueryInternalAsync`: HEAD lines 165-200 vs working tree lines 157-192 — `diff` returned empty (byte-for-byte identical).
    - `tryFetchEventsByAdvancedQueryAsync`: HEAD lines 635-1119 vs working tree lines 606-1090 — `diff` returned empty (byte-for-byte identical).
    Line-number shift is purely due to upstream deletions (`queryByIoc` body etc.), not any edit to these methods.

12. **`dotnet build` Actions project: 0 errors** — PASS
    Command: `dotnet build Source/Application/Cymulate.Agent.Application.Actions/Cymulate.Agent.Application.Actions.csproj /p:Platform=x64 /p:Configuration=DebugMac /p:DefineConstants="DEBUG%3BMac"`.
    Result: `485 Warning(s)`, `0 Error(s)`, build succeeded. Warnings are pre-existing project-wide (CS0649, CA1416, etc.), unrelated to this refactor.

13. **Test parity** — PASS
    Command: `dotnet test Tests/Application/Cymulate.Agent.Application.Integrations.Tests/Cymulate.Agent.Application.Integrations.Tests.csproj -c DebugMac /p:DefineConstants="DEBUG%3BMac" --filter "FullyQualifiedName~CybereasonApi"`.
    Result: `Failed: 4, Passed: 5, Skipped: 1, Total: 10`. The 4 failing tests are exactly the pre-existing branch failures listed in the verifier brief: `StartQueryAsync_StandardQueryOnly_AlertsOnly_PublishedAsExpected`, `StartQueryAsync_StandardAndAdvancedQueries_FindingsPublishedAsExpected`, `StartQueryAsync_StandardQueryOnly_ComplexWithOrOperator_EvaluatedAsExpected`, `StartQueryAsync_StandardQueryOnly_ComplexWithAndOperator_EvaluatedAsExpected`. No new failures; parity preserved.

## Constraint Adherence
- `git diff --stat HEAD -- 'Source/Application/Cymulate.Agent.Application.Actions/Actions/QueryIntegration/Logic/Clients/EDR/Cybereason/'` shows only `CybereasonApi.cs` modified (+64 / -97). PASS.
- No NuGet/csproj changes: `git diff HEAD -- '*.csproj'` shows no Cybereason-related project edits. PASS.
- No edits to base classes (`QueryMakerBase`, `AdvancedQueryMakerBase`), factories (`IntegrationApiClientFactory`, `ApiInfoModelFactory`), `eIntegrationProducts`, `CybereasonApiInfoModel`, `CybereasonCheckConnectionResultModel`, `CybereasonAlertCacheDataModel`, `CybereasonMalopInfoModel`, `parseRawEventsResponse`, `parseRawAlertsResponse`, `parseRawIncidentsResponse`, auth flow, or the advanced-query path — verified via diff (sole modified file is `CybereasonApi.cs`, and within that file the advanced path bodies are byte-identical to HEAD). PASS.
- Endpoints unchanged: `/rest/visualsearch/query/simple` still used at line 1153; no other endpoint strings touched in the diff. PASS.
- `queryByTimeRange` is a method group (not lambda, no captures). PASS.
- No empty-string dummy keyword threaded anywhere — grep `iKeyword` returns nothing. PASS.
- Naming style: new methods/params follow legacy camelCase + `i` prefix conventions (`createEventsCacheByTimeRangeAsync`, `iTimeRangeGroup`, `iHostname`, `iTimeRange`). PASS.
- CLAUDE.md: explicit types (no `var` in modified regions), braces on all `if`s (e.g., 289, 295, 306), `IsNullOrWhiteSpace` used at line 1177, no new `#region` blocks introduced, new params land before `CancellationToken`. PASS.

## Test parity
- 5 Passed / 4 Failed / 1 Skipped — same as HEAD baseline (per verifier brief).
- Failing tests are pre-existing branch issues, out of scope.
- Test file untouched by this refactor — confirmed by `git status` (no `CybereasonApiTests.cs` modification).

## Build
- 0 errors, 485 warnings (all pre-existing project-wide). Build succeeded in ~3.5 s.

## Notes
- Other working-tree modifications (`Kaspersky/KasperskyEdrApi.cs`, `Humio/HumioApi.cs`, `HumioApiTests.cs`, `QueryJobInfoModel.cs`) are unrelated to this Cybereason task and belong to sibling work on the same branch (`CA-71900-Humio-Kaspersky-Cyberreason-to-timerange`). They do not affect Cybereason verification.
- File-node time facet chosen: `createdTime` (MEDIUM confidence per `research/cybereason-file-time-filter.md`). Research explicitly disambiguates that on the File element this facet is named `createdTime`, NOT `creationTime` (which is the Process facet). If Cybereason rejects this filter in production, fallback plan is documented in research §"Fallback plan if the facet is rejected".
- The 4 pre-existing test failures (`AlertsOnly`, `StandardAndAdvancedQueries`, `ComplexWithOrOperator`, `ComplexWithAndOperator`) all fail with `Assert.True() Failure Expected:True Actual:False` deep in `QueryMakerTestsBase.StartQueryAsync_V3_PublishedAsExpected`. These are HEAD-baseline failures and explicitly out of scope for this contract per the verifier brief.
