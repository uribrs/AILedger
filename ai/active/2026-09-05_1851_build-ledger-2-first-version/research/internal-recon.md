# Internal Recon

## Durable sources read
- `/Users/user/Dev/AILedger/RULES.md` — operator authority, evidence-ranked assumptions, pipeline entry/ordering, mandatory verifier plus isolated code-reviewer, and simplicity constraints are already settled here.
- `/Users/user/Dev/AILedger/skills/{workflow-coordinator,prompt-contract-designer,task-orchestrator,contract-driven-execution,technical-researcher,code-reviewer}/` at `5bdb62717f8da0b33075b704b519a9e80a4e8490` — the six canonical cognitive-layer directories; `git diff 5bdb627 -- skills` is empty, including the two reference files.
- `AILedger_2_Design_Dossier_v2.1.docx` §§4–16 — task-as-entrypoint, structured truth plus append-only events, causal invalidation, legal lifecycle transitions, role-aware context, provider-neutral runtime boundary, minimal API, file-backed single-writer persistence, and the bounded first slice.
- `AILedger_2_Design_Dossier_v2.1.docx` §§17–18 — archive/TTL is a later lifecycle concern; continuity, provenance, lifecycle, and governance belong to Ledger while cognition remains with models and authority with the operator.
- `ai/active/2026-09-05_1851_build-ledger-2-first-version/{state.json,prompt_contract.md,assumptions.md,constraints.md,decisions.md}` — finalized task boundary; real Codex/Claude adapters are in this slice, but automated execution, swarms, generic policy language, hard filesystem enforcement, and archive implementation are not.
- No repository `AGENTS.md` exists. The session-provided repository instructions require .NET, small methods, SRP, close feature ownership, and no forced abstraction.

## Files in scope
- `AILedger.sln`, `Directory.Build.props`, `.gitignore` — build/tooling root — touched by: phase-0 scaffold only.
- `cognitive/skills/**`, `cognitive/manifest.json` — byte-for-byte copies of the six canonical skill directories plus source commit/per-file SHA-256 inventory — touched by: cognitive-seed work only.
- `src/AILedger.Core/**` — provider-neutral task aggregate, actors/roles/capabilities, claims/evidence/decisions/challenges/work items/runs, lifecycle, commands, causal events, invalidation, and context contracts — touched by: phase-0 shared contracts and core-domain work.
- `src/AILedger.Storage/**` — task workspace, exclusive single-writer boundary, append/replay/materialization/recovery, atomic state replacement, and Markdown projections — touched by: persistence work.
- `src/AILedger.Providers/**` — process boundary plus Codex/Claude launch-or-resume adapters and deterministic fake/dry-run implementation — touched by: provider-adapter work.
- `src/AILedger.Cli/**` — composition root and commands for open/status/history/attach/context plus initial truth operations — touched by: CLI/integration work.
- `tests/AILedger.Core.Tests/**`, `tests/AILedger.Storage.Tests/**`, `tests/AILedger.Providers.Tests/**`, `tests/AILedger.Cli.Tests/**`, `tests/AILedger.EndToEnd.Tests/**` — contract-driven unit, recovery, adapter, CLI, and two-lead shared-state coverage — touched by: independent test work after phase 0.
- `README.md`, `docs/**` — architecture, setup, persistence layout, CLI/provider prerequisites, rule-to-mechanism mapping, and limitations — touched by: documentation work.
- `AILedger_2_Design_Dossier*.docx`, `/Users/user/Dev/AILedger/**` — read-only inputs; never touched.

## Patterns to mirror
- Task state is machine authority, Markdown is projection → `/Users/user/Dev/AILedger/skills/prompt-contract-designer/SKILL.md:99` and dossier §§5,12 — commands append causal events, replay/materialize state, then regenerate readable views.
- Assumption status needs actor plus evidence; untested is not validated → `/Users/user/Dev/AILedger/RULES.md` “Assumption Evidence Rule” and `/Users/user/Dev/AILedger/skills/prompt-contract-designer/SKILL.md:183` — encode provenance/status transition guards, not model confidence.
- Shared surface freezes before disjoint work → `/Users/user/Dev/AILedger/skills/task-orchestrator/SKILL.md:206` — define event/command/state/context/provider contracts before consumers implement.
- Reviewer isolation is input exclusion, not a prompt reminder → `/Users/user/Dev/AILedger/skills/code-reviewer/SKILL.md:9` and dossier §10 — role context policies must omit request/contract/plan/verifier artifacts for code-reviewer contexts.
- Provider details stop at an adapter/process boundary → dossier §§3,9 — core types express roles/capabilities and launch requests/results, never Codex/Claude domain branches.
- Host supports `net8.0` and newer (`dotnet --info`: SDK 8.0.411/8.0.422, 9.0.301, 10.0.301); no existing project conventions are available to mirror.

## Shared surface to freeze
- `TaskId`, actor/role/capability identities, lifecycle/status enums, entity IDs, provenance, and causal-reference types — produced by phase 0; consumed by every domain, storage, context, adapter, CLI, and test component.
- Closed command/event vocabulary and `LedgerEvent` envelope (`event id`, `task id`, `actor`, `timestamp`, `causation/correlation`, typed payload) — produced by phase 0; consumed by command handling, JSON serialization, replay, history, projections, and tests.
- `GovernedTaskState` plus deterministic `Apply(event)`/command-result contract — produced by phase 0; consumed by the single writer, replay/recovery, projections, context builder, and CLI queries.
- Single-writer application boundary (`Execute` and read snapshots/history) — produced by phase 0; consumed by CLI and future agent-facing transports; provider processes must not write task files directly.
- `ContextManifest`/role policy contract — produced by phase 0; consumed by context assembly, both provider adapters, dry-run output, and isolation tests.
- Provider-neutral `AgentLaunchRequest`, `AgentRunResult`, and process-runner interface — produced by phase 0; consumed by Codex/Claude adapters and deterministic fakes.
- Persistence filenames/layout and JSON serialization options/version field — produced by phase 0; consumed by storage, CLI, E2E tests, docs, and future compatibility handling.

## Disjoint sets available
- Cognitive seed: `cognitive/**` — independent of implementation after destination and manifest schema are frozen.
- Core domain: `src/AILedger.Core/**` — independent implementation boundary; supplies frozen contracts to all consumers.
- Storage/projections: `src/AILedger.Storage/**` — independent of provider adapters and CLI once core contracts/layout are frozen.
- Provider/context runtime: `src/AILedger.Providers/**` — independent of storage internals; uses the application boundary and frozen launch/context contracts.
- CLI/composition: `src/AILedger.Cli/**` — independent after public core/storage/provider surfaces are frozen.
- Tests: `tests/**` — separable from `src/**` and should construct behavior through production boundaries, especially invalidation, recovery, role authority, one-active-run, and two-lead E2E scenarios.
- Documentation: `README.md`, `docs/**` — independent once CLI syntax and persistence/provider behavior settle.

## Landmines
- `main` is unborn and `state.json.baseRef` is `null`; all product artifacts will be untracked, so verification must use the files named in `execution_notes.md` rather than a base-ref diff unless a commit is explicitly authorized.
- `/Users/user/Dev/AILedger` has unrelated dirty `README.md` and `lessons.md`; copy only the six clean skill directories from commit `5bdb627`, then verify every destination file against commit-content SHA-256. Do not copy ambient working-tree extras.
- `events.jsonl` plus `state.json` is a two-artifact consistency problem: interruption after append but before materialization must recover by replay; exclusive ownership must cover every mutation entrypoint, not merely each individual file write.
- Append-only history is not append-only if CLI repair/reopen paths rewrite or truncate it. State recovery must never treat materialized state as the source from which history is reconstructed.
- Agent attach is an authority escalation surface. Provider processes may request actions only within operator-assigned role/capabilities; they cannot assign themselves roles, alter scope, or call an unguarded attach/authorize path.
- Causal invalidation is only reliable if decision/work dependencies reference claim IDs structurally. Text mentions cannot drive rejection/supersession propagation.
- One active orchestration run must be enforced atomically at the work-item command boundary; a read-then-append check permits duplicate active runs under concurrency.
- Context determinism requires stable ordering and explicit exclusion rules. Including reviewer-forbidden artifacts indirectly through a generic “task context” blob defeats isolation.
- Local provider binaries/help and non-interactive launch/resume behavior are external/runtime facts, not settled by repository recon; implementation must consume the dedicated research result and retain a fake/dry-run process boundary.
- The dossier’s archive/TTL acceptance language conflicts with the signed first-slice non-goal; follow the contract and document archive lifecycle as deferred rather than quietly expanding scope.
