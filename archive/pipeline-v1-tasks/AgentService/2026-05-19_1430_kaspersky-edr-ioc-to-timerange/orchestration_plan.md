# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: Scope is tightly bounded to one production file (`KasperskyEdrApi.cs`). The work is one coherent refactor (callback signature swap + body-shape change + parameter renames + delete `queryByIoc`). No test file exists, so there is no second worktree to split. Decomposition would add overhead with no benefit.

## Research Decisions
- Topic: `kaspersky-edr-event-processing` — triggered by assumption A1 — covers KSC `EventProcessingFactory.CreateEventProcessing2` filter semantics, specifically what `EVP_FTX_QUERY` does and what happens when it is omitted. Output: `research/kaspersky-edr-event-processing.md`. Resolved via the vendor-integration-expert skill against Kaspersky Security Center OpenAPI documentation.

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Not applicable — direct path. `contract-driven-execution` produces the final diff for `KasperskyEdrApi.cs` in one pass.

## Verification Obligations
- Cross-check against `prompt_contract.md` Success Criteria, line by line:
  - `generateCacheByIoc` no longer referenced in `KasperskyEdrApi.cs`.
  - `generateCacheByTimeRange(eQueryResultTypes.Alert, …, queryByTimeRange, …)` is the call.
  - Private `queryByTimeRange` callback matches `QueryByTimeRangeCallback<KasperskyEdrApiInfoModel>` signature exactly (no lambda wrapper, no extra captures).
  - `fetchAlertsAsync`, `writeAlertsToCacheAsync`, `createEventProcessingTaskAsync`, `createAlertsRequestContent` accept `QueryByTimeRangeGroupModel`.
  - `createAlertsRequestContent` body excludes `EVP_FTX_QUERY` and still includes `KLEVP_EVENT_HOST_NETBIOSNAME`, `KLEVP_EVENT_RISE_TIME_LEAST`, `KLEVP_EVENT_RISE_TIME_GREATEST`, `vecFieldsToReturn`, `lifetimeSec`.
  - `queryByIoc` deleted.
  - No `.Query` references remain on the local group variable (log lines updated to time-range terms).
- `dotnet build AgentService.sln` succeeds.
- Constraint adherence: changes scoped to `KasperskyEdrApi.cs` only — confirm `git diff --stat` shows exactly that file.
- Code-reviewer pass (isolated, minimal context) on the diff: signature correctness, idiomatic .NET, no swallowed cancellation, no concurrency regression, no dead code.

## Execution Reference
- Repo skill: `/Users/user/Dev/AgentService/.claude/skills/refactor-query-integration/SKILL.md` — canonical IOC→TimeRange callback delta, group model differences.
- Repo skill: `/Users/user/Dev/AgentService/.claude/skills/vendor-integration-expert/SKILL.md` — used in S1 to validate the EVP_FTX_QUERY removal approach.
- Branch precedent: `ai/active/2026-05-18_1713_humio-ioc-to-timerange/` — proven applied pattern (different vendor, same refactor archetype).
