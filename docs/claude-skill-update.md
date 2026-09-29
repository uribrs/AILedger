# Update Claude's installed ai-kernel skill

Give Claude this prompt. This updates its entrypoint; the role skills are served from the repository's
verified cognitive snapshot and must not be copied into separate installed role skills.

```text
Update your installed ai-kernel skill to match:
/Users/user/.codex/skills/ai-kernel/SKILL.md
(version 1.0.5), and the current interaction policy in:
/Users/user/Dev/Uri/localprojects/AILedger/cognitive/RULES.md
/Users/user/Dev/Uri/localprojects/AILedger/docs/handoff-assurance-v1.md

Find and read your actual installed copy first. The last observed location was
/Users/user/.claude/skills/ai-kernel/SKILL.md (1.0.3); verify it rather than assuming.
Back up that exact copy outside the active skills directory for rollback. Apply equivalent guidance,
preserving Claude-specific paths, metadata conventions and advertised MCP tool names. Bump its
version to 1.0.5, or report a newer existing version before choosing the next version.

Prefer the seven supplied structured AILedger tools for supported recording and inspection.
Batch related operations, retain host-assigned durable IDs and receipt mappings, and preserve the
original body/request_id/trusted binding for uncertain retries. Do not duplicate writes through CLI.
Use readiness checks for uncertain prerequisites, not before every routine call. Reads do not
replace context build. Retain authorized CLI paths for unsupported operations and unavailable
endpoints; a refused call is not an unavailable endpoint. submit_artifact covers VerifierOutput and
CodeReviewOutput only; claim supersession and other unsupported operations remain on the CLI.

Task 13 is now the DEFAULT assurance flow for supported implementation work. Remove the old
"opt-in", "never the default" and "do not add task-13 guidance" instructions for task 13.
Do not ask for a separate feature opt-in. Normal assurance preparation must arrange the trusted
policy/store, complete candidate/requirement/source inputs, configured checks and independent
principals. Host configuration and actual grants remain explicit: fresh provider launch with both
assurance options, or an authorized assurance serve host. Do not fabricate configuration or authority.

Use inspect_assurance, read_assurance, run_assurance_checks, record_assurance and accept_assurance
only as actually granted. Preserve exact immutable bindings, current-session read/check receipts,
partial checkpoints, criterion pass/fail/unknown/not_checked, contradictions, supersession,
freshness reassessment and explicit independent acceptance. Unknown outcomes never become passes.
Hard-interrupted checks require authorized operator reconciliation after the process is stopped.

Missing configuration or work beyond the current bounded UTF-8 input limits is an unresolved
assurance gap, not permission to silently skip assurance or omit material inputs. Keep existing
governed completion requirements, verifier/reviewer artifacts, authorization and blind-review
isolation. Requirements-aware task-13 review needs a compatible authorized context; report a
conflict instead of widening a blind reviewer's brief. Acceptance does not close governed work or
authorize merge/release. Targeted synthesis is needed only for actual cross-area judgment.

Keep task 12 opt-in and offline/read-only. Do not claim task 13 executes implementation, that client
trials have passed, or that default adoption establishes improved judgment, time or cost.
Take the updated role instructions from a fresh kernel context; do not maintain duplicate copies.

This authorizes updating your installed skill and its rollback backup only. Do not create a governed
development task, launch agents, spend on provider trials, alter AILedger implementation or change
unrelated configuration. Validate the skill and report its exact path, version, backup path and
changes. Flag conflicting instructions elsewhere; do not silently rewrite them. If this rollout is
reverted, restore this skill update with it.
```
