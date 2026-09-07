# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: Even though the file is large (1878 LOC) and the refactor touches 7 methods + 3 query builders, the work is one tightly coupled refactor on the Event path that cannot be meaningfully split without coordination overhead. The Alert path is already done; the Advanced-query path is out of scope. One executor pass produces a coherent diff.

## Research Decisions
- Topic: `cybereason-file-time-filter` — triggered by assumption A1 — running in parallel (vendor-API research subagent). Output: `research/cybereason-file-time-filter.md`. Gate before applying the File-query refactor; the Machine-query and callback changes can proceed independently.

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Not applicable — direct path.

## Execution Order
1. Wait for research subagent to resolve A1 (Cybereason File-node time facet).
2. Apply non-File changes first (collapse `generateCache*` calls, extend `queryByTimeRange`, convert `createEventsCacheByIocAsync` → `createEventsCacheByTimeRangeAsync`, convert `writeEventsBySearchTypeToCacheAsync` to TimeRange model, drop `iKeyword` from `buildEventMachineSearchQuery`).
3. Apply File query change once A1 resolved.
4. Delete `queryByIoc`.
5. Build.
6. Run targeted tests.
7. Verifier + code-reviewer subagents.

## Verification Obligations
- Cross-check against `prompt_contract.md` Success Criteria.
- `dotnet build` of the Actions project → 0 errors.
- `dotnet test --filter FullyQualifiedName~CybereasonApi` → all currently-passing tests pass.
- Constraint adherence: diff scoped to `CybereasonApi.cs` (and possibly `CybereasonApiTests.cs` if forced). `runAdvancedCustomQueryInternalAsync` and `tryFetchEventsByAdvancedQueryAsync` lines remain identical to HEAD.
- Code-reviewer pass on the diff: callback signature parity with delegate, no swallowed cancellation, idiomatic .NET, no dead IOC plumbing.

## Execution Reference
- Repo skill: `/Users/user/Dev/AgentService/.claude/skills/refactor-query-integration/SKILL.md`.
- Repo skill: `/Users/user/Dev/AgentService/.claude/skills/vendor-integration-expert/SKILL.md` (used for A1).
- Branch precedents: `ai/active/2026-05-18_1713_humio-ioc-to-timerange/`, `ai/active/2026-05-19_1430_kaspersky-edr-ioc-to-timerange/`.
