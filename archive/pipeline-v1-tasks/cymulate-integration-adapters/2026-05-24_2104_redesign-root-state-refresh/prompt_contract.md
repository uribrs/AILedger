# Prompt Contract — Redesign Root State Refresh

Role:
You are the maintainer of the failure-handling-redesign task's machine-readable
state.

Goal:
Bring `ai/active/2026-05-24_1427_failure-handling-redesign/state.json` into
sync with the three signed-off phases (P0a, P0b, P1) and record the
marker-leak-schedule inconsistency under `openQuestions`.

Context:
- Root `state.json` `lastUpdated` is `2026-05-24T15:35:00Z`.
- Phases closed after that timestamp:
  - P0a: `signed_off` (per `phase-0a/state.json`)
  - P0b: `signed_off_with_deferred_finding` (per `phase-0b/state.json`,
    `lastUpdated 2026-05-24T19:51` local)
  - P1:  `signed_off` (per `phase-1/state.json`, `lastUpdated 22:30:00Z`)
- The current root file already records each phase's `status`, `actualDiff`,
  `verifierPasses`, `codeReviewerPasses`, and deferred items. The refresh is
  light: bump `lastUpdated`, set top-level `status` and `currentPhase`,
  ensure `requiredFiles` and `phases[]` rows reflect reality, append the
  marker-leak-schedule inconsistency to `openQuestions`.
- Resolved open questions Q-merge-1, Q-merge-2, Q-merge-3 (Rev 4 defaults)
  may stay or be moved to `decisionsCaptured` — minimal change preferred.

Constraints:
- Edit ONLY `ai/active/2026-05-24_1427_failure-handling-redesign/state.json`.
- Do not modify `plan.md`, any `phase-*` artifact, or any production code.
- Do not silently resolve the marker-leak-schedule contradiction. Record it
  as an open question with both sources cited.
- Preserve existing fields not being updated (decisionsCaptured, reviews,
  concernsAddressed, etc.). This is a minimal edit, not a rewrite.
- JSON must parse.

Success Criteria:
1. Root `state.json.lastUpdated` ≥ `2026-05-24T18:04:00Z` after the edit.
2. Root `state.json.status` and `currentPhase` reflect "P0a+P0b+P1 complete;
   P2 next" (exact field values up to executor; must be self-consistent).
3. Root `state.json.phases[]` rows for P0a, P0b, P1 retain their existing
   `status`, `actualDiff`, `verifierPasses`, `codeReviewerPasses`, and
   deferred-item fields unchanged.
4. A new entry in `state.json.openQuestions` (or equivalent field) names
   the marker-leak-schedule contradiction with verbatim citations:
   - "`phase-0b/state.json` `phases[].deferredToPhase4`: '… 1-line fix; …
     load-bearing only when classified-retry is enabled'"
   - "`plan.md` rev 4 W4 reading: not scheduled in any phase — accepted
     until P5 deletes the marker entirely"
5. `state.json` parses as valid JSON after the edit.
6. No other file is modified.

Execution Rules:
- Read the current root `state.json` before editing.
- Use surgical `Edit` (string replacement), not a full rewrite.
- Validate JSON parse after the edit (e.g., `python3 -m json.tool`).

Output Format:
- Updated root `state.json` on disk.
- One-paragraph summary of fields changed.

Stop Conditions:
- Edit cannot be made surgically (file structure has drifted from the
  read snapshot) — escalate.
- JSON does not parse after the edit — revert and escalate.
