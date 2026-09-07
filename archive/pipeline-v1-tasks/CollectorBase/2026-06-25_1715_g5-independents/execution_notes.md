# Execution Notes — Group 5: independents

## Outcome
`dotnet build` clean; `dotnet test CollectorBase.slnx` → **88/88** (86 prior + 2 new). Three independent fixes.

## B-M2 — cursor non-advancing-cursor guard
`Strategies/Pagination/CursorPaginationStrategy.cs`: `hasMore` now requires the returned token to ADVANCE past
`ctx.Cursor` (the cursor that produced this page): `advanced = !empty(token) && !Equals(token, ctx.Cursor, Ordinal)`.
A vendor echoing a fixed token with full pages now terminates (was infinite). Test
`Cursor_NonAdvancingToken_Terminates_NoInfiniteLoop` (/stuck/query repeats `after=STUCK`) → 4 records (2 pages)
then stops. Normal advancing cursors unaffected (their token differs each page; existing tests green).

## ProbeAsync — request-bearing step resolution
`CollectorExecutorRunner.ProbeAsync`: `probeReq = !empty(step.Request.Path) ? step.Request : (step.Step?.Request ?? step.Request)`.
A for_each/poll_and_drain-first profile now probes the inner step's real path instead of `{base_url}`. Test
`Probe_ForEachFirstProfile_HitsInnerRequestPath` → ValidateConfigurationAsync probes `/noauth/query` (the inner
fetch) and reports valid. Fetch-first profiles unchanged (step.Request.Path present → used as before).

## Doc drift (docs-only, grep-justified)
Grep findings: `StopWhen`/`stop_when` and `SortKey`/`sort_key` have ZERO consumers outside the Profile.cs model
declarations (Profile.cs:151,155) — dead knobs. `timeWindow`/`time_window` does not exist anywhere. `hydrate` IS
consumed (HydrateSpec) — kept. yaml-contract.md corrected:
- `sort_key` → marked "reserved — parsed but not currently consumed"; removed the false R + non-existent
  `resumeDriftAccepted`.
- `stop_when` → marked "reserved — not currently consumed" (termination is fixed per strategy).
- `watermark_field` → fixed the requirement: it's required for `strategy: cursor_watermark` (validated at load,
  per G4), NOT `cursor`. Also snake_cased these three rows + cursor_expiry_seconds.
(Out of scope: the doc's broader camelCase-vs-snake_case inconsistency in other rows/examples — flagged, not fixed.)

## No-regression
Full suite green (88/88), incl. cursor / cursor_watermark / next_url pagination, the validate probe, flatten, auth.

## Post-review repair (code-reviewer-1)
- MINOR FIXED: the cursor strategy now returns `NextCursor: null` on the non-advance stop (was persisting the
  dead repeated token into the final checkpoint) — parity with cursor_watermark. NITs accepted: no xUnit Timeout
  on the stuck test (the guard makes it terminate; a regression surfaces as an obvious hang); the doc's
  worked-sketch still shows camelCase `sortKey` (broader camelCase-vs-snake_case doc cleanup is out of scope).

## Commands
- `dotnet build CollectorBase.slnx` → clean. `dotnet test CollectorBase.slnx` → 88/88.
