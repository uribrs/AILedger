# Interaction guidance refresh — 2026-09-29

The six kernel-served role skills now prefer the supplied structured recording and inspection tools
at their point of use. Verifier/reviewer submissions retain complete content and required tables;
host IDs replace manual artifact IDs on the supported path. Shared rules, both repository agent
instruction files, manuals, tool inventory and CLI help agree on routing and authorized fallbacks.

Task 13 is now the **default assurance flow for supported implementation work**, as explicitly
directed by the operator. Trusted host setup remains part of normal assurance preparation. This is
an operating-policy change; the runtime still requires explicit authority/store configuration and
scoped grants. It does not automatically infer identities, checks or candidate closure. Missing
configuration and unsupported scope remain visible gaps. Existing governed stages, independent
verification, blind-review isolation, artifact requirements and completion gates remain intact.
Task 12 remains opt-in and offline/read-only. Client trials remain pending.

## Validation

- Full `dotnet test`: **2,285 main + 99 memory tests passed**, zero failures/skips. Builds and runtime
  fixtures were outside the checkout. The initial sandboxed attempt could not open VSTest's IPC
  socket; the authorized local run passed with sequential test collections.
- All 12 cognitive manifest hashes match. All six legacy-format skill frontmatters parsed. Fresh
  real CLI context builds for all seven roles served rules 1.3.1 and the updated role instructions.
- Installed Codex `ai-kernel` 1.0.5 passed the skill-creator validator; its absolute document links
  resolve to the main checkout. Repository role skills retain their existing top-level `version`
  convention instead of changing schema to satisfy the installed-skill validator.
- All 11 frozen fixture hashes match. The previously built baseline verifier matched the four
  historical report fixtures and four provider-cost samples. Measurement implementation and expected
  baselines were unchanged; CLI help now lists the existing artifact/disposition report flags.
- `git diff --check` passed. No real/billable provider episode, development agent, governed development
  task or live ledger mutation was used. Disposable context fixtures were used for runtime validation.

## Installed skills and rollback

Codex entrypoint: `/Users/user/.codex/skills/ai-kernel/SKILL.md`, version **1.0.5**.
Exact prior copy:
`/Users/user/.local/share/ailedger/rollback/ai-kernel/codex-1.0.4-before-default-assurance.md`.
Claude's last inspected copy was `/Users/user/.claude/skills/ai-kernel/SKILL.md`, version **1.0.3**;
it was not edited by this rollout. Its “never the default” task-13 instruction conflicts with the
new policy. Use the [Claude update prompt](claude-skill-update.md) to update and back it up.

Revert the commit introducing this refresh to reverse its repository policy and guidance together.
Restore the installed Codex skill from the exact backup, and restore Claude's own backup if it has
been updated. Installed files are outside Git and do not revert automatically. Restore the matching
prior CLI package from the installation rollback receipt when reverting runtime help. Rebuild
governed context before further dispatch so roles receive the restored snapshot. A broader redesign
rollback must also restore the entrypoints and CLI matching that earlier revision; the 1.0.4 backup
reverses this default-policy update only.
