# Orchestration Plan

## Complexity Decision
- Path: **direct**
- Rationale: A single ~10-line edit inside one method plus one focused unit test. Coupling between the change and the test is tight; decomposing into bounded workers would create coordination overhead without isolation benefit. Per the rubric: low Complexity, low Separability (production change + its regression test are one unit of thought), low Coupling to other modules, no Dependency-order concerns, low-medium Execution risk (mocking IHttp pagination requires care but is bounded), high Worker clarity is irrelevant on the direct path.

## Research Decisions
- None needed. All open assumptions are about internal codebase state, not external vendor behavior:
  - **A1** (call sites of `FetchEndpointsAsync`) — VALIDATED via repo grep.
  - **A2** (`endpoint_id` is stable + always present) — VALIDATED via the captured production data and existing `MapAsset` / `buildFindingId` usage.
  - **A3** (live-agent check-in is the drift driver) — VALIDATED via the duplicate-cluster signature (+7 / +6 / +6).
  - **A4** (test project exists) — VALIDATED. Host project: `Tests/Application/Cymulate.Agent.Application.Integrations.Tests/`. Existing class `EDR/XdrApiTests.cs` extends `IntegrationProductsTestsBase` and already demonstrates `A.Fake<IHttp>()` + `A.CallTo(...).Returns(...).Once().Then.Returns(...)` for sequenced fake HTTP responses — the exact pattern needed.
  - **A5** (Info-level logging acceptable) — defer to verifier/code-reviewer; default OPEN.
  - **A6** (branch) — OPEN, but constraint already neutralizes it (no commit; uncommitted change for operator to stage).

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Not applicable — direct path. The executor produces both artifacts (production edit + test) in one coherent pass; no integration step.

## Verification Obligations
- Cross-check against every Success Criterion in `prompt_contract.md` (8 items).
- Confirm the request JSON, sort field, sort direction, page size, and outer `do/while` are byte-identical to pre-change (Success Criterion 2). Quote both versions if needed.
- Confirm `endpointsFound++` runs BEFORE the dedup skip (constraint and Decision D4).
- Confirm the new test exercises the actual drift scenario (page 2 starts with last N of page 1), not a simpler "no dedup" sanity check.
- Confirm no commits were made (`git status` should show two unstaged modifications: one production file + one test file; the task directory under `ai/active/` is also expected).
- Confirm `state.json` is internally consistent with the markdown files.

## Execution Sequence
1. **S1** — Read `PaloAltoCortexApiBase.cs:83-190` and surrounding helpers (`tryLogWithProductName`, `eLogLevels`).
2. **S2** — Apply the in-method `HashSet<string>` dedup per Decision D2/D5/D6.
3. **S3** — Read `Tests/Application/Cymulate.Agent.Application.Integrations.Tests/EDR/XdrApiTests.cs` and `IntegrationProductsTestsBase` to understand fixture wiring; choose either to add the test to `XdrApiTests.cs` directly or to create a sibling `XdrApiPaginationTests.cs` in the same folder if doing so produces a cleaner test class.
4. **S4** — Write the regression test:
   - Stage two fake `IHttp` responses: page 1 returns endpoint IDs `[e1..e100]` (100 items, full page) sorted by descending `last_seen`; page 2 returns endpoint IDs `[e95..e100, e101..e194]` (first 6 items are duplicates of page 1's tail, then 94 fresh items, total 100).
   - Optional page 3 returns `< 100` items to terminate the walk cleanly.
   - Pass `onEndpointAsync` callback that appends each emitted endpoint's `endpoint_id` to a `List<string>`.
   - Assert: collected IDs are all distinct; count equals the union size (`e1..e194`); the duplicate IDs (`e95..e100`) appear exactly once each.
5. **S5** — Build the touched test project: `dotnet build Tests/Application/Cymulate.Agent.Application.Integrations.Tests/Cymulate.Agent.Application.Integrations.Tests.csproj`. Run only the new test class via filter to keep the loop fast; then run the whole test class to confirm no regressions.
6. **S6** — Update `state.json` + write `execution_notes.md`.
7. **S7** — Orchestrator runs verifier subagent with full context (Agent tool, full prompt). Writes `review/verifier-1.md`. Repairs if needed.
8. **S8** — Orchestrator runs code-reviewer subagent with **minimal context only** (Agent tool, sender-side enforcement: only the diff, risk classification, stack indicator, taskPath). Writes `review/code-reviewer-1.md`. Repairs material findings.
9. **S9** — Coordinator confirms `workflow.verifierRun = true` and `workflow.codeReviewerRun = true` in `state.json`. Reports.

## Stop Gate — Operator Sign-Off Required Before S2
RULES.md Phase 1 requires explicit sign-off before execution begins. The contract phase is complete (`task.md`, `prompt_contract.md`, `constraints.md`, `assumptions.md`, `decisions.md`, `state.json` all written; A4 promoted to VALIDATED in this plan, to be reflected in `assumptions.md` on next state update). S1 is read-only and may be performed inline. **S2 onward — actual code mutation — waits for the operator's "proceed".**
