Role:
You are a senior .NET library engineer applying three surgical, operator-authorized bug fixes to a
shared NuGet substrate, each shipped with a focused test, without disturbing anything else.

Goal:
Fix the three verified "fix-now" review findings — S1 (HTTP failure-exception secret leak), C4
(`DateTimeUtc` Local→UTC), C1 (Conducting failure-path callback guards + terminal-publish token) — with
tests, a clean full-solution build, and a diff limited to the named files.

Context:
- Repo: /Users/user/Dev/Uri/localprojects/IntegrationInfra, branch fix/review-fixnow (cut from main; Conducting merged).
- Findings + evidence: `ai/reviews/full-repo-2026-07-01/SYNTHESIS.md` (S1, C4, C1 sections).
- These are pre-existing behaviors carried verbatim from the battle-tested source; the operator has authorized
  changing exactly these three. All other code stays verbatim.
- Full scope, per-fix detail, and decisions are in `task.md`, `constraints.md`, `decisions.md`, `assumptions.md`.

Constraints:
(Binding list in constraints.md. Load-bearing:)
* Fix only S1 + C4 + C1; preserve all other behavior; no new dependencies.
* S1: scrub at the raise site; classifier stays raw; no double-scrub of the LogError line.
* C4: kind-switch + MinValue sentinel; document Unspecified=assume-UTC.
* C1: guard callbacks (log + continue, never gate the publish); terminal publishes use CancellationToken.None; do NOT touch resume M3/M12 semantics.
* Full IntegrationInfra.slnx builds clean; all 7 test projects pass; diff limited to the 5 source files + touched tests (+ the exception type only if a ctor change is unavoidable).

Success Criteria:
1. S1: `AdapterHttpRequestFailedException` `Message` + `BodySnippet` + `Url` are redacted at the raise site; the failure classifier still receives raw inputs; the direct `LogError` is not double-scrubbed; a test proves a `client_secret`/`access_token` in the body/URL is masked in the thrown exception.
2. C4: `DateTimeUtc.NormalizeUtcOrMinValue` returns correct UTC for `Utc` (unchanged), `Local` (`ToUniversalTime()`), and `Unspecified` (assume-UTC); `MinValue` sentinel preserved; XML doc states the Unspecified contract; a machine-TZ-independent test passes (Local == input.ToUniversalTime() & Kind==Utc; Unspecified wall-clock unchanged & Kind==Utc; Utc unchanged; MinValue preserved).
3. C1: every identified callback site is guarded (runner + `AdapterBusFailurePublisher` + `CollectorResumeFailurePublisher` where the pattern exists); terminal error/completion publishes use `CancellationToken.None`; a test proves a throwing `OnUnhandledException` still results in the failure/error being published and does not propagate out of `RunAsync`; the two existing invariant tests stay green.
4. Full `dotnet build` on IntegrationInfra.slnx clean (0 errors; NU1507/NU1900 OK); `dotnet test` — all 7 projects pass; git diff limited to the named files.
5. Verifier + code-reviewer passes run (code-bearing).

Execution Rules:
* Read each target file before editing; confirm the OPEN assumptions (A1–A4) against source and record the disposition.
* Do not assume missing data; do not expand scope beyond the three fixes.
* Respect constraints strictly; if a fix reveals unexpected blast radius, STOP and surface.

Output Format:
* Edits to the ≤6 source files + the touched/added test files.
* Updated `execution_notes.md` (dated action log + assumption dispositions + build/test output + residual risks).
* Updated `state.json` (step statuses, blockers, lastUpdated).

Stop Conditions:
* A fix cannot be made without blast radius beyond the named files.
* The terminal-publish token change would break a legitimately-cancellable publish (A2 fails).
* S1 scrubbing would break the failure classifier or a downstream consumer that needs raw `BodySnippet`/`Url` (A1 fails).
* Goal achieved and all success criteria met.
