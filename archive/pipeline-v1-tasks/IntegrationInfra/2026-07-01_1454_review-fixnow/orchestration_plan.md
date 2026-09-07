# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: Three small, tightly-scoped, verified fixes (S1/C4/C1) across three concerns, each with one focused
  test. They share the same context (the review findings) and the risk is scope discipline, not decomposition.
  Worker split would be pure coordination overhead. Tight single-context iteration + build/test is correct.

## Research Decisions
- None needed. All edges are in-repo; S1 and C4 were already re-verified against source in SYNTHESIS.md. OPEN
  assumptions A1–A4 are read-the-file confirmations (and STOP conditions), not external-system research.

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Not applicable — direct path.

## Verification Obligations
- Cross-check all 5 Success Criteria in prompt_contract.md.
- S1: exception Message + BodySnippet + Url redacted at raise site; classifier still raw; LogError not double-scrubbed; test masks a real secret.
- C4: kind-switch correct for Utc/Local/Unspecified + MinValue; XML doc states Unspecified=assume-UTC; TZ-independent test.
- C1: all identified callback sites guarded (bus + resume); terminal publishes use CancellationToken.None; test proves a throwing OnUnhandledException does not suppress the failure publish; the two existing invariant tests stay green.
- Scope discipline: diff limited to the 5 named source files + touched tests (exception type only if a ctor change is unavoidable). No drift into C2/C3/M7/cert-bypass/hydrator/resume-M3-M12.
- Full slnx build clean; all 7 test projects pass.
