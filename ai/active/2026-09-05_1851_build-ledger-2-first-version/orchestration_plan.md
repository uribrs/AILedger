# Orchestration Plan

## Problem Classification

- Contract sufficiency: sufficient.
- Classes:
  - `agent-governance` — introduces durable authority, role, lifecycle, and context rules around agent cognition.
  - `event-sourced-state` — authoritative changes are causal events with replayed materialized state and readable projections.
  - `provider-process-integration` — launches and resumes two external CLI runtimes through a normalized process boundary.
- Additional classified prior art: none — no matching live ledger rows were found for the selected classes.
- Recon correction: confirmed the greenfield scope and refined it into seven disjoint file sets with provider launch behavior isolated from the core.
- New or changed artifacts:
  - `LedgerEvent` envelope and closed event payloads → event serializer and replay engine → append, deserialize, and deterministically apply task changes.
  - `GovernedTaskState` aggregate → command handler and state projector → authorize commands, enforce lifecycle invariants, and materialize current state.
  - role assignment and capability policy → command authorization guard → reject self-assignment, scope change, and ungranted actions.
  - task workspace lock and single-writer service → every CLI mutation → serialize append, replay, and projection replacement.
  - `ContextManifest` and role policy → provider launch preparation → include allowed context in stable order and exclude reviewer-forbidden material.
  - `AgentLaunchRequest` and `AgentRunResult` → Codex and Claude process adapters → map neutral intent to version-checked argument arrays and normalized JSONL results.
  - Markdown projections → task workspace writer → regenerate human-readable task, assumptions, and decisions views after committed events.

### Attention Items

| id | name | failure mode | causal path and impact | planned handling | source |
|---|---|---|---|---|---|
| R1 | stale-materialized-state | Event append succeeds but state projection fails | process interruption between two durable artifacts leaves state stale; reopening may report false task truth | `test:tests/AILedger.Tests/Storage/RecoveryTests.cs::R1_ReopenReplaysEventsWhenMaterializedStateIsStale` | `research/internal-recon.md` |
| R2 | actor-self-escalation | Actor escalates its own authority | unguarded attach or role mutation grants capabilities not approved by the operator | `test:tests/AILedger.Tests/Core/AuthorizationTests.cs::R2_NonOperatorCannotAssignRoleOrCapabilities` | `research/internal-recon.md` |
| R3 | duplicate-active-run | Two active orchestration runs start for one work item | concurrent read-then-append permits duplicate execution trees | `test:tests/AILedger.Tests/Storage/ConcurrencyTests.cs::R3_ConcurrentStartsAllowOnlyOneActiveRun` | `research/internal-recon.md` |
| R4 | reviewer-context-leakage | Reviewer receives forbidden context indirectly | generic context blob leaks request, contract, plan, or verifier output and defeats independent review | `test:tests/AILedger.Tests/Core/ContextIsolationTests.cs::R4_CodeReviewerManifestExcludesForbiddenArtifacts` | `research/internal-recon.md` |
| R5 | false-provider-success | Parseable provider output is mistaken for success | non-zero exit, failed terminal event, missing or mismatched session identity produces a false successful run | `test:tests/AILedger.Tests/Providers/ProviderProtocolTests.cs::R5_SuccessRequiresExitZeroSuccessfulTerminalAndMatchingSession` | `research/provider-cli-launch-contracts.md` |

### Research Questions

| topic slug | triggered by | question | decision it can change | authority | result |
|---|---|---|---|---|---|
| provider-cli-launch-contracts | assumption:A2 | Which non-interactive launch, resume, session, structured-output, context, and permission surfaces should each adapter use? | `AgentLaunchRequest`, `AgentRunResult`, process arguments, parser, and capability probes | official OpenAI and Anthropic CLI documentation plus installed CLI help/probes | complete — `research/provider-cli-launch-contracts.md` |

## Complexity Decision

- Path: decompose.
- Axis scores: Complexity high | Separability high | Coupling low | Dependency order medium | Execution risk high | Worker clarity high.
- Rationale: recon identified multiple disjoint source, test, cognitive, and documentation sets; both the disjoint-set and separable-test hard triggers require decomposition.

## Research Decisions

- External topic: `provider-cli-launch-contracts` — triggered by: `assumption:A2` — status: complete.
- Internal recon: complete → `research/internal-recon.md`.

## File Ownership

- Disjoint sets found: 7.
- W0 owns: `AILedger.sln`, `Directory.Build.props`, `.gitignore`, project files, `src/AILedger.Core/Contracts/**`, and serialization metadata required to freeze shared surfaces.
- W1 owns: `cognitive/**`.
- W2 owns: `src/AILedger.Core/Domain/**`, `src/AILedger.Core/Application/**`.
- W3 owns: `src/AILedger.Storage/**` except its project file.
- W4 owns: `src/AILedger.Providers/**` except its project file.
- W5 owns: `src/AILedger.Cli/**` except its project file.
- W6 owns: `tests/**` except its project file.
- W7 owns: `README.md`, `docs/**`.
- Shared surface frozen in phase 0: identifiers, actor/role/capability types, commands/events, state/apply contract, single-writer boundary, context manifest/policy, launch request/result/process runner, persistence layout, JSON versioning, and project references.

## Worker Plan

- W0 — scope: scaffold solution and freeze all shared contracts — output: compiling contract-only solution — phase: 0 — continuity: main thread.
- W1 — scope: copy the six skills byte-for-byte from commit `5bdb627` and produce source/hash manifest — owns: `cognitive/**` — inputs: manifest schema — output: verified cognitive snapshot — phase: 1 — continuity: fresh.
- W2 — scope: implement task aggregate, validation, command handling, causal invalidation, lifecycle, authorization, and context assembly — owns: core Domain/Application paths — inputs: W0 contracts and recon — output: core behavior — phase: 1 — continuity: fresh.
- W3 — scope: implement file workspace, event append/replay, atomic projection recovery, exclusive mutation boundary, and Markdown projections — owns: storage path — inputs: W0 contracts and recon — output: durable storage — phase: 1 — continuity: fresh.
- W4 — scope: implement fakeable process runner, capability probes, Codex/Claude argument builders and defensive JSONL normalization — owns: providers path — inputs: W0 contracts and provider research — output: provider runtime — phase: 1 — continuity: fresh.
- W5 — scope: compose services and expose the contracted CLI commands — owns: CLI path — inputs: W2-W4 public surfaces — output: runnable CLI — phase: 2 — continuity: fresh.
- W6 — scope: independently implement production-path unit, recovery, concurrency, provider, CLI, and two-lead end-to-end tests, including R1-R5 — owns: tests path — inputs: frozen contracts and landed production surfaces — output: passing behavioral suite — phase: 2 — continuity: fresh.
- W7 — scope: document architecture, setup, commands, providers, persistence, rule-to-mechanism mapping, deferred scope, and limitations — owns: README/docs — inputs: landed behavior — output: usable operator documentation — phase: 3 — continuity: fresh.

Workers must return `BLOCKED: needs <artifact>` instead of inventing or changing a shared contract.

## Synthesis Approach

The main thread freezes and builds the shared solution first, then fans out W1-W4. After integrating and building those outputs, it launches W5 and W6 against the stable public surface, runs the complete suite and provider smoke checks, then delegates W7 against observed behavior. Any shared-contract change returns to the main thread and causes affected workers to be re-briefed.

## Verification Obligations

- Cross-check every Success Criterion in `prompt_contract.md`.
- Verify copied cognitive files against `git show 5bdb627:<path>` hashes, not the dirty source working tree.
- Run build and complete automated tests.
- Exercise CLI open, attach, context, truth mutation, status, and history against a temporary workspace.
- Run non-destructive authenticated new-and-resume smoke tests for both providers, or record the exact blocked/untested condition without claiming operational support.
- Resolve R1-R5 through their named production-path tests.
- Confirm reviewer context exclusion, operator-only role authority, event replay recovery, and one-active-run atomicity.
- Run a full-context verifier, repair material gaps, then run a separate isolated code-reviewer.
