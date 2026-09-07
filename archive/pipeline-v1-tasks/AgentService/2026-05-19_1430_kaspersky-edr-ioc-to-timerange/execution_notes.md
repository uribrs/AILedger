# Execution Notes

## Diff summary

Single file changed: `Source/Application/Cymulate.Agent.Application.Actions/Actions/QueryIntegration/Logic/Clients/EDR/Kaspersky/KasperskyEdrApi.cs`.

- `generateCacheForStandardCustomQueriesInternal` — now calls `generateCacheByTimeRange(eQueryResultTypes.Alert, …, queryByTimeRange, …)` (was `generateCacheByIoc(… queryByIoc, …)`).
- New private method `queryByTimeRange(KasperskyEdrApiInfoModel, CheckConnectionResultModel, IFileStreamWriter<CacheDetailModel>, ISet<QueryByTimeRangeGroupModel>, CancellationToken)` matches the `QueryByTimeRangeCallback<KasperskyEdrApiInfoModel>` delegate exactly.
- `fetchAlertsAsync`, `writeAlertsToCacheAsync`, `createEventProcessingTaskAsync`, `createAlertsRequestContent` — first parameter retyped from `QueryByIocGroupModel` to `QueryByTimeRangeGroupModel`; local parameter name `iIocGroup` → `iTimeRangeGroup`; `iocGroup` → `timeRangeGroup`.
- `createAlertsRequestContent` — `EVP_FTX_QUERY` removed from the `pFilter` JSON. Time-range and hostname keys are preserved. The "// to search by all the iocs at once" comment is removed along with the field.
- `queryByIoc` — deleted.
- Log lines no longer reference `.Query`; they reference `Hostname` and `DateTimeRange`.
- New private helper `describeTimeRangeQuery(QueryByTimeRangeGroupModel)` produces a `host='X' range=…..…` string used to populate `KeywordRequestDetailsModel.Query` (added in response to code-reviewer I1 — preserves a debug breadcrumb on cache-error rows now that the IOC value is gone).

## Build verification

```
dotnet build Source/Application/Cymulate.Agent.Application.Actions/Cymulate.Agent.Application.Actions.csproj -c DebugMac /p:Platform=x64 /p:DefineConstants="DEBUG%3Bx64%3BMac"
```
Result: **0 Errors**, 394 Warnings (all pre-existing — same set the rest of the branch already carries; none introduced by this change).

## Tests

No Kaspersky tests exist under `Tests/`. `find Tests -iname "*kaspersky*"` returns zero matches. No test changes were made; test creation is out of scope per task contract.

## Reviewer feedback resolution

- **I1 (Important)** — Applied. `requestDetails.Query` now carries `host='…' range=From..To` instead of empty string. Net change: +6 lines (new `describeTimeRangeQuery` helper) inside the same file.
- **S1 (Suggestion)** — Not applied. The unconditional `KLEVP_EVENT_HOST_NETBIOSNAME = Hostname` line is pre-existing behavior (the IOC path did the same). Empty-hostname semantics on the Kaspersky API side are a separate concern. Tracked in research/kaspersky-edr-event-processing.md "Open follow-ups".
- **S2, S3, S4, S5, N1** — Not applied. All describe pre-existing patterns (duplicate error logging across layers, missing QueryIds in log lines, sync-over-async style, legacy `#region` blocks). Contract explicitly forbids drive-by cleanup outside the refactor scope.

## Risks / Notes for reviewer

- Result-set size grows: removing `EVP_FTX_QUERY` means each `EventProcessing.GetRecordRange` call returns every event in the time window for the host, not just events matching the IOC. The existing `nStart=0, nEnd=50000` cap is unchanged; if real Kaspersky deployments exceed 50k events/window/host, cursor pagination would be needed. This is the same risk profile as the Humio precedent on this branch.
- The Kaspersky API filter `KLEVP_EVENT_HOST_NETBIOSNAME` is still passed verbatim from `Hostname`. If `QueryByTimeRangeGroupModel.Hostname` is ever empty/whitespace in production, the call may return zero rows (KSC `pFilter` keys are conjunctive). The IOC path had the same property — this PR does not regress it but also does not fix it.

## Same-branch sibling work

This branch (`CA-71900-Humio-Kaspersky-Cyberreason-to-timerange`) already carries uncommitted Humio changes (HumioApi.cs, HumioApiTests.cs, QueryJobInfoModel.cs) from yesterday's task `ai/active/2026-05-18_1713_humio-ioc-to-timerange/`. Cybereason refactor is still pending per the branch name; not in this task's scope.
