# Verifier Pass 1

## Verdict

**FAIL.** The repository builds, all 44 tests pass, the six-skill cognitive snapshot is byte-for-byte correct, durable task behavior works, R1-R5 are handled, and both installed providers have authenticated launch/resume evidence. The delivered context layer does not yet preserve the promised methodology for every role: `Verifier` receives no verifier instructions, and the only artifact typed as governing rules is manifest metadata rather than the authoritative operator rules. Two provider-protocol hardening gaps also diverge from the completed research contract.

Because `state.json.baseRef` is `null` and the repository remains unborn, this pass scoped the comparison to the artifacts named in `execution_notes.md`, the complete current source/test/documentation tree, and the original dossier files.

## Findings

### F1 — P1 — Verifier contexts contain no verifier methodology

`ContextAssembler` maps `RoleKind.Verifier` to a skill ID named `verifier` (`src/AILedger.Core/Application/ContextAssembler.cs:42`), but the canonical six-skill snapshot has no such directory. An end-to-end CLI context build for an attached verifier returned an empty skill list, while planning and implementation leads received their mapped skills. The v2.1 dossier defines the verifier as a mandatory full-context gate, and the copied `task-orchestrator` skill contains that protocol. As landed, Ledger cannot kick off a verifier in accordance with the same methodology, so the role-appropriate-context criterion and the user's central pipeline requirement are not satisfied. Add a real mapping to the existing verifier protocol and a production-path context test that proves the verifier receives it.

### F2 — P1 — The artifact labeled `Rules` is not the governing rules

`CognitiveArtifactLoader` loads `cognitive/manifest.json` and labels its hash inventory as `ContextArtifactKind.Rules` (`src/AILedger.Cli/CognitiveArtifactLoader.cs:27-31`). The authoritative `/Users/user/Dev/AILedger/RULES.md` is neither copied nor loaded, so provider contexts receive integrity metadata plus one hard-coded authority constraint, not the governing RULES required by dossier §10 and the context success criterion. The verifier smoke showed `rules/cognitive-manifest` as the sole Rules artifact. Preserve the actual governing rules as a separately hash-verified cognitive artifact (or provide an equivalently explicit governed rule source); do not misclassify the manifest itself as behavioral rules.

### F3 — P2 — An intermediate provider session mismatch can be masked

For every parsed event, `AgentAdapterBase.RunAsync` overwrites the observed session with the newest non-null value (`src/AILedger.Providers/Adapters/AgentAdapterBase.cs:72-75`) and validates only that final value (`:93-95`, `:123-136`). A Claude stream containing an event for the wrong session followed by a terminal result for the expected session is therefore accepted. This conflicts with `research/provider-cli-launch-contracts.md` requirement to verify every emitted Claude event/session identity and weakens exact-session isolation. Track the first/expected identity, fail permanently on any conflicting event, and add the mixed-session protocol test.

### F4 — P2 — Codex capability probing does not probe the contract it executes

Capability probing always runs only top-level `--help` (`src/AILedger.Providers/Adapters/AgentAdapterBase.cs:139-161`), and Codex requires only the tokens `exec` and `--version` (`src/AILedger.Providers/Adapters/CodexAgentAdapter.cs:10`). It never checks the `exec`/`exec resume` surfaces for `--json`, `--strict-config`, `--sandbox`, `--cd`, exact-session resume, `--add-dir`, or `--output-schema`, even though those flags are used by the adapter and the research explicitly requires relevant help-surface probes. The installed CLI smoke proves version `0.150.0-alpha.8` works today, but the documentation claim that missing future flags fail before execution is not true. Probe the actual subcommands and test a missing required Codex flag.

### F5 — P3 — Attention-item plan rows omit the required name field

The `orchestration_plan.md` Attention Items table is `id | failure mode | ...` rather than the required `id | name | failure mode | ...` schema. R1-R5 still resolve unambiguously and their test artifacts pass, so this did not hide a product failure, but it breaks the durable naming contract used by the pipeline. Repair the table before closeout.

## Success Criteria Cross-check

| # | result | evidence |
|---|---|---|
| 1 | PASS | `dotnet build AILedger.sln --no-restore`: 0 warnings/errors; `dotnet test AILedger.sln --no-build --no-restore -m:1`: 44/44 passed. |
| 2 | PASS | `cognitive/manifest.json`; independent destination/manifest/Git-object SHA-256 and exact inventory checks passed for all eight files from the six source directories at full commit `5bdb62717f8da0b33075b704b519a9e80a4e8490`. |
| 3 | PASS | Verifier CLI smoke opened/reopened a task, returned status version 10, and streamed 10 history events; recovery tests passed. |
| 4 | PASS | `GovernedTaskState` and event envelopes cover roles/actors, claims, evidence, decisions, challenges, work, stage, and runs; provenance is retained directly or through the causal event envelope. |
| 5 | PASS | `FileGovernedTaskService`, `TaskMutationLock`, R1, R3, and concurrent-different-work tests prove append/replay and serialized mutation. |
| 6 | PASS | `MarkdownTaskProjectionWriter`; projection and stale-state recovery tests pass. |
| 7 | PASS | Both rejection and supersession tests prove decision invalidation and active/non-active work blocking/staleness. |
| 8 | PASS | R2 and self-assignment/capability tests pass through production command handling. |
| 9 | FAIL | Ordering, evidence scoping, capabilities, stage, and stop conditions are present, but F1 and F2 leave mandatory role/rule context absent. |
| 10 | PARTIAL | Fake-process tests and persisted authenticated Codex/Claude new/resume smokes pass; F3 and F4 leave research-specified protocol/capability guarantees incomplete. |
| 11 | PASS | CLI exposes the minimum surface plus work/run/stage/provider commands; verifier smoke exercised open, attach, claims, evidence, decision, challenge, context, status, and history. |
| 12 | PASS | R3 passed and state contained exactly one active run after concurrent starts. |
| 13 | PASS WITH CORRECTION NEEDED | README and both docs cover required subjects, but the verifier-skill row and governing-rules description must change with F1/F2 repairs. |
| 14 | PASS | 44 tests pass; `TwoLeadsCoordinateThroughDurableTaskStateRatherThanTranscript` uses independent service instances and no transcript. |
| 15 | PENDING BY ORDER | This independent verifier ran. The isolated code-reviewer is correctly scheduled after verifier repair and must run before closeout. |

## Constraint and Execution Review

- No evidence of changes to the authoritative `/Users/user/Dev/AILedger` skill source; its `HEAD` equals `origin/main` at `5bdb627...`, `git diff HEAD -- skills` is empty, and the installed Codex skills match the source directories.
- Both dossier files remain present. No commit or push occurred.
- The .NET implementation keeps provider-specific behavior outside Core and does not add swarms, automatic dispatch, LangGraph, generic policy language, hard host enforcement, archive movement, or TTL deletion.
- The `decompose` path remained correct: recon identified seven disjoint sets, the shared contracts were frozen, and implementation/tests/docs remained separable. No concrete cross-owner write violation was found from the durable outputs; main-thread provider hardening was a synthesis/repair action within the planned surface.
- External research aligned with the implemented direct-process, JSONL, explicit-session, bounded-permission approach, except F3/F4. The live Claude finding justifiably added strict MCP and slash-skill isolation after the initial research recommendation.

## Assumption Disposition

| id | status | name | citation | actor |
|----|--------|------|----------|-------|
| A1 | VALIDATED | v2.1-first-slice-boundary | Original request; `task.md`; `prompt_contract.md`; `constraints.md`; dossier §16 | verifier |
| A2 | VALIDATED | local-provider-contracts | `research/provider-cli-launch-contracts.md`; local CLI versions/help | researcher |
| A3 | VALIDATED | single-ledger-writer-provider-boundary | `IGovernedTaskService`; `CliApplication.LaunchProviderAsync`; R3; persisted provider smoke events | verifier |
| A4 | VALIDATED | verbatim-cognition-plus-code-policy | independent Git-object/hash inventory check; `cognitive/manifest.json`; `docs/architecture.md` mapping | verifier |
| A5 | VALIDATED | local-file-persistence-sufficient | R1; R3; storage suite; verifier CLI smoke | verifier |
| A6 | NEVER-TESTED | initial-commit-after-implementation | `git rev-parse --verify HEAD` still fails; no-commit constraint | verifier |
| A7 | VALIDATED | researcher-with-missing-reference-files | `research/provider-cli-launch-contracts.md`; provider tests; authenticated smoke evidence | verifier |

A6 risk: the first version has not yet been captured by a repository commit. This is intentional under the operator's no-commit constraint, but provenance remains filesystem-only until the operator authorizes the first commit.

## Attention Item Disposition

| id | final disposition | name | evidence |
|---|---|---|---|
| R1 | handled | stale-materialized-state | `tests/AILedger.Tests/Storage/RecoveryTests.cs::R1_ReopenReplaysEventsWhenMaterializedStateIsStale` exists, drives `FileGovernedTaskService`, and passed. |
| R2 | handled | actor-self-escalation | `tests/AILedger.Tests/Core/AuthorizationTests.cs::R2_NonOperatorCannotAssignRoleOrCapabilities` exists, drives `CommandHandler`, and passed; the companion operator self-assignment test also passed. |
| R3 | handled | duplicate-active-run | `tests/AILedger.Tests/Storage/ConcurrencyTests.cs::R3_ConcurrentStartsAllowOnlyOneActiveRun` exists, uses concurrent independent production services, and passed. |
| R4 | handled | reviewer-context-leakage | `tests/AILedger.Tests/Core/ContextIsolationTests.cs::R4_CodeReviewerManifestExcludesForbiddenArtifacts` exists, drives `ContextAssembler`, and passed. |
| R5 | handled | false-provider-success | `tests/AILedger.Tests/Providers/ProviderProtocolTests.cs::R5_SuccessRequiresExitZeroSuccessfulTerminalAndMatchingSession` exists, drives `CodexAgentAdapter`, and passed. F3 is a broader mixed-event identity gap outside the named test's covered cases. |

## Decision Drift

| decision | disposition | evidence/reason |
|---|---|---|
| Build the bounded first slice | landed as decided | Project shape and deferred list match `constraints.md` and docs. |
| Keep AILedger 1.x skills as immutable seed inputs | landed as decided | Exact Git-object and manifest hash checks passed. |
| Separate roles/capabilities from providers | landed as decided | Core contracts contain no Codex/Claude branches; provider names remain adapter/run data. |
| Use one authoritative writer and file-backed event truth | landed as decided | Storage service, locks, replay, R1/R3. |
| Include real adapters with fakeable process boundary | landed as decided | `IProcessRunner`, provider tests, and live smoke evidence. |
| Keep one governed run per work item | landed as decided | R3. |
| Proceed on initially unverified local runtime behavior | changed with evidence, not abandoned | A2 research plus authenticated new/resume smokes validated the premise before closeout. |
| Direct child processes, JSONL, exact IDs, no shell/latest-session shortcuts | landed with F3 gap | Argument tests and source confirm the design; per-event session consistency remains incomplete. |
| Fail-closed workspace-bounded provider permissions | landed as decided | Adapter arguments/tests and live Claude hardening evidence. |
| Exclude ambient Claude MCP/slash skills | changed during execution and landed | Initial live smoke exposed ambient discovery; `--strict-mcp-config` and `--disable-slash-commands` were added, then CL4/CL5 passed. |
| Use repository-scoped NuGet configuration | introduced during execution, justified | Default restore encountered an expired corporate feed; `NuGet.Config` scopes restore to nuget.org and the solution builds. No product-scope change. |
| Follow the technical-researcher exact referenced templates | changed during execution | Three referenced files were absent; the researcher followed the main skill contract and documented the limitation. A7 shows the output remained implementation-grade, but the source package defect should be retained as a pipeline lesson. |

## Required Repair Before Verifier 2

1. Repair F1/F2 and add production-path context coverage for actual cognitive artifacts.
2. Repair F3/F4 with protocol/capability tests.
3. Correct affected documentation and the attention-item table schema.
4. Re-run build, the full suite, and a focused CLI context check; then create `review/verifier-2.md` without overwriting this report.

