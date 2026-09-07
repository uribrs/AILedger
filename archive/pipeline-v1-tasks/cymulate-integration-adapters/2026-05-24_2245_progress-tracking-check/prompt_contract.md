Role:
You are summarising delivery progress on the failure-handling redesign to
the operator.

Goal:
Honest status: where we are vs the plan, what quality signals say, and
whether the remaining phases (P4, P5) look on-track.

Context:
- Plan rev 4 §6 defines seven phases: P0a, P0b, P1, P2, P3, P4, P5.
- This session completed P2 + P2 patches + P3 (and P3 mid-execution
  redesign).
- Working tree carries uncommitted P0a + P0b + P1 + P2 + P3.
- HANDOFF.md at `ai/active/2026-05-24_1427_failure-handling-redesign/`
  is the authoritative current-state document.

Constraints:
- No code edits. No phase-artifact edits.
- Quote LoC, test counts, and decisions verbatim from artifacts where
  numeric.
- Surface accepted risks and open follow-ups, don't bury them.
- "On track" / "behind" / "ahead" requires a yardstick — plan has no
  dates; use phase-completion ratio + quality signals as the proxy.

Success Criteria:
1. Per-phase: status, headline numbers, key decision (one line each).
2. Quality signals: build clean? tests green? C10 status?
3. Tracking judgment with explicit reasoning.
4. Top 3 risks for P4 + P5.
5. One recommended next move.

Execution Rules:
- Read HANDOFF.md and the root state.json before composing. Don't
  re-derive from phase files unless HANDOFF.md is silent on something.
- Be terse — operator wants signal, not exhaustive recap.

Output Format:
- Five short sections matching success criteria.
- Bullet form preferred.

Stop Conditions:
- Numeric claims in HANDOFF.md disagree with state.json — stop and
  surface the conflict.
