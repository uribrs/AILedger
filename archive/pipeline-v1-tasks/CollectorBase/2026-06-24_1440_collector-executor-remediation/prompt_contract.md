Role:
You are a senior .NET engineer remediating three audited findings in the CollectorExecutor interpreter,
without regressing its just-completed SDK-conformance.

Goal:
Fix exactly three findings — (1) two robustness bugs (non-string next-token crash; zero/negative
page_size|batch_size infinite loop), (2) de-duplicate the Preflight/RunAsync validation prefix, (3) delete
the dead FetchSignal/Signal vocabulary and fix its misleading comments — with build clean and tests green.

Context:
- Repo: /Users/user/Dev/Uri/localprojects/CollectorBase (net8.0; CollectorBase.slnx).
- Findings + file:line in ai/active/2026-06-24_1410_collector-executor-conformance-audit/review/.
- Touch points: Strategies/Pagination/{CursorPaginationStrategy,CursorWatermarkStrategy,NextUrlPaginationStrategy}.cs;
  Strategies/PageSize/StaticPageSizeStrategy.cs + CollectorExecutor/Execution/CollectorExecutorRunner.cs (Chunk,
  page-size/batch resolution, Preflight/RunAsync); CollectorExecutor/Seams/Seams.cs (FetchSignal/RunContext.Signal).

Constraints:
- See constraints.md (authoritative). Exactly items 1-3; no scope creep; behavior-preserving extraction for
  item 2; confirm-then-delete for item 3; do not regress conformance.

Success Criteria:
1. dotnet build CollectorBase.slnx clean.
2. dotnet test CollectorBase.slnx all pass (>= current 43; add tests for 1a and 1b if feasible).
3. A numeric/bool next-token no longer throws; page_size/batch_size <= 0 cannot infinite-loop.
4. Validation logic is a single shared routine called by both Preflight and RunAsync.
5. No FetchSignal/FetchDecision/RunContext.Signal remains; comments corrected.
6. Conformance intact (interfaces, bus routing, resume, per-emit-target page counter, RUN-envelope ingress).

Execution Rules:
- Verify "nothing reads Signal" by search before deleting (item 3).
- Do not assume; keep edits minimal and within scope. Respect constraints strictly.

Output Format:
- Code edits to the files above; execution_notes.md updated; state.json steps in sync.

Stop Conditions:
- All success criteria met.
- A fix would require touching a deferred finding or regressing conformance (stop and surface).
