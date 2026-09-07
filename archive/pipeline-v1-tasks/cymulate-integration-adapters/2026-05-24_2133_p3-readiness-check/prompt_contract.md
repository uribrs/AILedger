Role:
You are reporting status to the operator before deciding whether to draft P3.

Goal:
Answer two questions honestly:
(a) How much context has this session consumed?
(b) Is the session in shape to take on Phase 3 — and if so, what is needed
    first?

Context:
- Phase 2 completed in this session with full pipeline (contract → execute
  → verifier → code-reviewer). State at
  `ai/active/2026-05-24_1427_failure-handling-redesign/phase-2/`.
- Plan rev 4 §6 Phase 3 (lines 623-662) lists three EXPLICIT
  prerequisites that must clear BEFORE the P3 contract is drafted.
- Two of the three prereqs are answerable only outside this repo (SDK
  source, platform team).
- Model is Opus 4.7 (1M context window).

Constraints:
- No code changes.
- No P3 artifacts drafted yet (the plan gates that on prereqs).
- Do not bulldoze ahead. The plan explicitly says "investigate before
  drafting this phase's contract".

Success Criteria:
1. Honest context-usage estimate with reasoning.
2. Per-prereq status: (a) can I answer locally? (b) what needs operator
   or platform-team input?
3. One concrete next step the operator can approve or redirect.

Execution Rules:
- Estimate context based on session activity (file reads, subagent
  outputs, artifacts written), not a guess.
- For Prereq 2, perform the grep — it's local and cheap.
- For Prereqs 1 and 3, surface what would be needed; do not invent
  answers.

Output Format:
- Three sections: Context Usage / P3 Prereq Status / Recommended Next.
- Bullet form. No prose paragraphs.

Stop Conditions:
- Grep for Prereq 2 produces ambiguous results that I cannot interpret
  without operator input.
