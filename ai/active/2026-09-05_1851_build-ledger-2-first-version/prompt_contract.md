# Prompt Contract

## Goal

Create the first usable version of AILedger 2.0 in this repository, using the v2.1 dossier and the installed AILedger pipeline itself. Preserve the current textual skills verbatim as the cognitive specification and implement the initial mechanical governance layer around them.

## Context

The repository currently contains only two design dossiers and has no commits. The authoritative AILedger 1.x checkout is `/Users/user/Dev/AILedger` at commit `5bdb627`; its Codex installation has been updated and verified clean. The v2.1 dossier recommends beginning with a persistent task kernel, structured task truth, Markdown projections, two lead adapters, and role-aware context assembly without porting automated execution or swarms yet.

## Constraints

- Follow every constraint in `constraints.md`.
- Treat the textual skills as source artifacts, not prose to rewrite during the initial copy.
- Any codified mechanism must have an explicit relationship to the textual rule it enforces or supports.
- Preserve operator authority and prevent agents from silently changing their own roles or task scope.
- Use deterministic, testable domain and persistence behavior.
- Keep provider-specific process details outside the core domain.

## Success Criteria

- A buildable .NET solution and automated test suite exist in the repository.
- All six authoritative AILedger 1.x skill directories are copied verbatim into a clearly named cognitive-layer location, with a manifest identifying their source commit and integrity hashes.
- The system can create or reopen a durable governed task and report its current status and event history.
- The authoritative state model represents actors, role assignments, claims, evidence, decisions, challenges, work items, lifecycle stage, and agent runs with provenance.
- State changes append durable causal events and update a recoverable materialized current state through a single-writer boundary.
- Human-readable Markdown projections are produced for the task, assumptions, and decisions without becoming the authoritative representation.
- Rejecting or superseding a claim mechanically marks directly dependent decisions and work items stale or blocked, with tests proving the behavior.
- Role assignments are explicit, auditable, and operator-controlled; an agent cannot assign itself additional authority.
- Context assembly produces deterministic role-appropriate manifests containing governing rules, skills, task goal, constraints, relevant evidence, current stage, capabilities, and stop conditions.
- Codex and Claude adapters can launch or resume real locally available agent runtimes using verified invocation mechanisms, while supporting a deterministic dry-run or fake process boundary for tests.
- A CLI exposes at least task open, status, history, actor attach, context build, and the initial claim/evidence/decision/challenge operations.
- The first version enforces one active orchestration run per work item.
- Repository documentation explains architecture, local setup, CLI usage, provider prerequisites, persistence layout, and current limitations.
- The complete test suite passes, and at least one end-to-end test demonstrates two differently assigned leads interacting through shared task state rather than a shared chat transcript.
- The existing AILedger pipeline performs independent verification and isolated code review before closeout.

## Execution Rules

- Run internal repository recon before choosing the execution path.
- Resolve current Codex and Claude launch behavior from local command help and authoritative vendor documentation when local evidence is insufficient.
- Decompose implementation across disjoint file ownership where the orchestrator identifies separable surfaces.
- Freeze shared contracts before parallel implementation.
- Record any necessary deviation from the dossier in `decisions.md` and `execution_notes.md`.
- Never claim a provider integration works without executing an appropriate non-destructive verification or recording it as untested.
- Do not alter the source skill copies when introducing codified policies; add mappings or generated projections separately.

## Stop Conditions

- Stop if no supported, non-interactive local launch mechanism exists for either required provider and a real adapter cannot be implemented honestly.
- Stop if authoritative source skill files cannot be read or copied byte-for-byte.
- Stop if concurrent writers cannot be prevented by the selected persistence boundary.
- Stop if a required design choice would silently weaken operator authority or the existing independent verification model.
- Stop if the verifier or code-reviewer pass is missing or leaves unresolved findings.

## Handoff

Deliver the working repository plus the archived AILedger task record. Report the execution path, research performed, tests run, provider-launch verification, review outcomes, accepted risks, and any assumptions that remained untested or were rejected.
