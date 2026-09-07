Role:
You are a .NET engineer applying three small, independent correctness/cleanup fixes.

Goal:
(B-M2) stop the plain `cursor` strategy from looping on a non-advancing cursor; (ProbeAsync) make the connection
probe target the first request-bearing step; (doc) prune pagination knobs the engine doesn't read.

Context:
- `CursorPaginationStrategy.Next`: `hasMore = !empty(token) && recordsThisPage>0`; `RunContext.Cursor` is the
  current cursor.
- `ProbeAsync` uses `Steps[0]`; for_each/poll_and_drain-first steps carry the request on `.Step`.
- yaml-contract.md may document knobs (StopWhen/SortKey/timeWindow) with no consumers; `hydrate` IS consumed.

Constraints:
- See constraints.md. Salient: cursor guard stops ONLY a non-advancing cursor; ProbeAsync unchanged for
  fetch-first; doc removals grep-justified (hydrate stays); no vendor identity; net8.0/CollectorBase.slnx; 86
  prior tests green.

Success Criteria:
1. `dotnet build CollectorBase.slnx` clean.
2. `dotnet test CollectorBase.slnx` — 86 prior pass + new: (B-M2) a fixed non-advancing cursor with full pages
   terminates (bounded, no hang); a normal advancing cursor still collects all pages; (ProbeAsync) a for_each-first
   profile's probe hits the inner request path (assert the probed URL contains it).
3. No regression (full suite green).
4. note.md / execution_notes: cursor guard, probe resolution, the doc-drift grep findings (knobs pruned + why).

Execution Rules:
- Pin A1–A4; confirm ctx.Cursor semantics; grep doc knobs before pruning. Apply the three edits. Respect constraints.

Output Format:
- CursorPaginationStrategy.cs + CollectorExecutorRunner.cs (ProbeAsync) + yaml-contract.md + tests; note + execution_notes.

Stop Conditions:
- The cursor guard would break a normal advancing cursor — stop and reconsider the equality basis.
- Goal achieved and full suite green.
