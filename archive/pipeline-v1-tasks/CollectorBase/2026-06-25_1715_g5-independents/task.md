# Task: Independents (cursor cycle guard, probe step resolution, doc drift)

Three mutually-independent fixes (batched for efficiency, not coupling).

## B-M2 — cursor non-advancing-cursor guard
`Strategies/Pagination/CursorPaginationStrategy.cs`: `hasMore = !empty(token) && recordsThisPage>0` loops
forever if a vendor echoes the SAME cursor with full pages. `RunContext.Cursor` is the current cursor.
Fix: `advanced = !empty(token) && !string.Equals(token, ctx.Cursor, Ordinal); hasMore = advanced && recordsThisPage>0`.

## ProbeAsync — request-bearing step resolution
`CollectorExecutorRunner.ProbeAsync` probes `Steps[0]`, whose Request.Path is empty for a for_each/poll_and_drain
-first profile (real request on `.Step`). Fix: `probeReq = !empty(step.Request.Path) ? step.Request : (step.Step?.Request ?? step.Request)`.

## Doc drift (docs-only)
yaml-contract.md documents pagination knobs no strategy reads. Grep StopWhen/SortKey/timeWindow consumers;
prune/annotate only the dead ones (hydrate is real — keep). Ground each removal in a zero-consumer grep.

## Out of scope
No other behavior change; the cursor guard must only stop a NON-advancing cursor.
