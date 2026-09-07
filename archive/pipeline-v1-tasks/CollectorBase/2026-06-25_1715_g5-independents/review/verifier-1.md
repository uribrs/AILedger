# Verifier-1 — Group 5: independents (B-M2 cursor guard / ProbeAsync / doc drift)

Independent adversarial verification. Suite run by verifier, not trusted from notes.

## Criterion 1 — build clean + suite ≥88, 0 failed, both new tests present, no hang — **PASS**
- `dotnet build CollectorBase.slnx` → 0 Errors (12 NU1507 warnings, pre-existing package-source-mapping noise, unrelated).
- `dotnet test CollectorBase.slnx` → **Passed! Failed: 0, Passed: 88, Skipped: 0, Total: 88, Duration: 10 s**.
- The non-advancing-cursor test COMPLETED (10s total run) — a broken guard would have hung the suite indefinitely. It did not.
- Both required tests present and green:
  - `Cursor_NonAdvancingToken_Terminates_NoInfiniteLoop` (CollectorExecutorTests.cs:1406)
  - `Probe_ForEachFirstProfile_HitsInnerRequestPath` (CollectorExecutorTests.cs:1421)

## Criterion 2 — cursor guard compares returned token to ctx.Cursor; normal cursor unaffected — **PASS**
- `Strategies/Pagination/CursorPaginationStrategy.cs:24`:
  `var advanced = !string.IsNullOrEmpty(token) && !string.Equals(token, ctx.Cursor, StringComparison.Ordinal);`
  `var hasMore = advanced && recordsThisPage > 0;`
  Comment (line 23) states `ctx.Cursor is the cursor used for this request` — i.e. the cursor that produced THIS page. Correct semantics.
- Fixture proof (genuine, not tautological): `/stuck/query` (CollectorExecutorTests.cs:283-288) returns a FULL 2-record page + a fixed `after=STUCK` token every call. Test asserts exactly 4 records = 2 pages: page1 advances null→STUCK (token≠ctx.Cursor null → continue), page2 sees STUCK==ctx.Cursor → stop. Without the guard this loops forever.
- Normal advancing cursor unaffected: token differs each page ⇒ `advanced` true ⇒ hasMore preserved. Existing cursor tests green.
- Parity confirmed: `cursor_watermark` (CursorWatermarkStrategy.cs:40) uses the IDENTICAL `string.Equals(token, ctx.Cursor, StringComparison.Ordinal)` non-advance guard. The new cursor guard matches the established pattern.
- No regression in the pagination family: targeted run (Cursor|Probe|NextUrl|Watermark|ValidateConfiguration|Connection) → 13/13 pass, incl. `NextUrl_NonAdvancingLink_StopsInsteadOfLooping`, `NextUrl_FollowsAbsoluteODataLink_AcrossPages`.

## Criterion 3 — ProbeAsync resolves inner request for for_each-first, unchanged for fetch-first — **PASS**
- `CollectorExecutorRunner.cs:78`:
  `var probeReq = !string.IsNullOrWhiteSpace(step.Request?.Path) ? step.Request : (step.Step?.Request ?? step.Request);`
  - Fetch-first: `step.Request.Path` non-empty ⇒ uses `step.Request` (prior behavior, unchanged).
  - for_each/poll-first: `Steps[0].Request.Path` empty ⇒ falls back to `step.Step.Request` (the inner request-bearing step). Final `?? step.Request` is a safe no-op fallback.
- Fixture proof: `ForEachFirstProbeProfile` (CollectorExecutorTests.cs:2970) has `Steps[0]` = for_each with inner `request.path: /noauth/query`. Test runs `ValidateConfigurationAsync`, asserts `IsValid` AND `httpFactory.RequestedUrls` contains `/noauth/query` — i.e. the probe hit the inner path, not `{base_url}` with no path. The `/noauth/query` mock (line 241) requires NO auth header, so a 2xx genuinely exercises the real request path.

## Criterion 4 — doc edits accurate + grep-justified — **PASS**
Grep over `Strategies/` + `CollectorExecutor/` (`.cs`, excluding tests):
- `sort_key`/`SortKey`: ONLY declaration `Profile.cs:151` (`public string? SortKey`). Zero read sites. (Present as data in 2 Falcon YAML profiles in Runner/DefaultProfiles.cs, but nothing consumes the value.) Doc (yaml-contract.md:80) now: "reserved — parsed but not currently consumed by any strategy"; the false `R` and the non-existent `resumeDriftAccepted` removed. **Accurate.**
- `stop_when`/`StopWhen`: ONLY declaration `Profile.cs:155` (default "empty_page"). Zero read sites. Doc (line 83): "reserved — not currently consumed … no-op today." **Accurate.**
- `watermark_field`/`WatermarkField`: required-validation gated on `cursor_watermark` (`Profile.cs:299-300`), read at runtime `CollectorExecutorRunner.cs:328,330`. Doc (line 82): "required for `strategy: cursor_watermark` (validated at load)." **Corrected and accurate** (previously misattributed).
- `hydrate`/`Hydrate`: live — HydrateSpec consumed across Profile.cs, StepHelpers (BuildHydrateRequest), Runner (291-316). Retained in doc (lines 111, 179). **Correct to keep.**
- Bonus: doc also notes `link_header` removed (line 77) — consistent with code (no consumer); not in scope but accurate.

## Task note — **PASS**
`execution_notes.md` documents: the guard (token-must-advance-past-ctx.Cursor, parity with cursor_watermark), the probe resolution (inner request-bearing step), and the doc-drift grep findings (sort_key/stop_when dead, watermark_field requirement fix, hydrate kept). All three claims independently re-verified above and hold.

## VERDICT: **PASS**
All 4 success criteria met. Build clean, 88/88 (0 failed), suite did not hang, both new tests genuine and green, cursor guard semantically correct and pattern-consistent, ProbeAsync fetch-first path unchanged, doc edits grep-justified. No regressions in the cursor/cursor_watermark/next_url pagination family.
