# Prompt Contract

## Role
You are a senior .NET / C# engineer working in the Cymulate Agent Service repo. You implement a narrow, surgical fix inside an existing base-class method and add a focused regression unit test. You do not refactor, generalize, or expand scope.

## Goal
Make `PaloAltoCortexApiBase.FetchEndpointsAsync` emit each `endpoint_id` at most once per invocation, even when the Cortex XDR `get_endpoint` server-side sort drifts between paginated requests. Cover the fix with a unit test that simulates page-boundary drift.

## Context
The Cortex XDR endpoint walk pages via `search_from`/`search_to` over a `last_seen DESC` sort. Live agents bumping `last_seen` between page fetches cause endpoints near a page boundary to be returned on both sides of the boundary. With no dedup, the collector emits each such endpoint twice. Observed in production: 42 duplicate endpoints out of 953 unique, forming +7 / +6 / +6 constant-offset clusters consistent with three page boundaries.

The bug entered the codebase in commit `9a6cffe80` (2024-08-15, OsherBenDavid, CA-48775) when the sort field flipped from immutable `first_seen` to mutable `last_seen`. The `last_seen` choice is intentional — it powers the `lastSeenDate < iBaseDate` early-break for incremental walks — and must be preserved.

Target file:
- `Source/Application/Cymulate.Agent.Application.Actions/Actions/QueryIntegration/Logic/Clients/BaseApiClients/PaloAltoNetworks/PaloAltoCortexApiBase.cs` (method body at `:83-190`).

## Constraints
(See `constraints.md` for the full list.)
- Edit only the named file plus exactly one test file in an existing test project.
- Do not change the request body, sort field, sort direction, page size, or early-break semantics.
- Dedup is per-invocation, keyed on `endpoint_id` (case-insensitive).
- `endpointsFound++` must run before the dedup skip — page-size end-of-list logic depends on it.
- Skip both the emit and the `lastSeenDate` early-break when a duplicate is detected.
- Log skipped duplicates at `Info` via `tryLogWithProductName` with `endpoint_id` and `searchFrom`.
- No commits, no branch switching, no `git` mutations.

## Success Criteria
1. `PaloAltoCortexApiBase.FetchEndpointsAsync` declares a `HashSet<string>` (case-insensitive) at the top of the method body and uses it to skip duplicate `endpoint_id`s before the emit and `lastSeenDate` checks, while still incrementing `endpointsFound`.
2. The request JSON, sort field, sort direction, page size, and outer `do/while` control flow are unchanged.
3. The fix compiles cleanly under at least one platform configuration (`dotnet build` succeeds with the configuration the executor selects); no new warnings introduced in the touched file.
4. A unit test exists in an already-present test project covering `PaloAltoCortexApiBase` that:
   - Stages a fake paginated response where page 2 starts with the last N (e.g., 3) endpoint IDs of page 1.
   - Calls `FetchEndpointsAsync` and captures emitted endpoints via the `onEndpointAsync` callback.
   - Asserts: every emitted `endpoint_id` is unique, and the total emit count equals the size of the union of unique IDs across both fake pages.
   - Asserts (optional, recommended): `endpointsFound`-style "we kept paging" behavior is unchanged — the test does not regress the end-of-list break.
5. All existing tests in the touched test project still pass (`dotnet test <project>` succeeds).
6. `assumptions.md` A4 transitions to VALIDATED (test project located) or the executor stops with a documented blocker.
7. `execution_notes.md` records: the exact lines changed, the exact test added, the build/test command run, and any residual risks.
8. `state.json` is updated: `currentPhase = "execution_complete"`, `verification.status` advanced appropriately, `workflow.skillsRun` includes a `contract-driven-execution` entry with `completedAt`, and `lastUpdated` is current.

## Execution Rules
- Do not assume missing data — read the file before editing, read the test project structure before placing the test.
- Respect constraints strictly.
- Do not refactor or rename anything outside the minimal change.
- If A4 resolves to REJECTED (no host test project), stop and surface the blocker rather than improvising a new project.
- Do not invoke the verifier or code-reviewer from this skill — those are the orchestrator's responsibility.

## Output Format
- Final `state.json` and `execution_notes.md` reflecting the completed work.
- The two file edits (production file + test file) staged on disk, uncommitted.

## Stop Conditions
- A4 cannot be resolved (no test project covering `PaloAltoCortexApiBase`).
- The build fails after the change and the failure is not from the introduced test.
- Existing tests in the touched test project fail and the failure is not in the new test.
- Any constraint cannot be honored without scope expansion.
