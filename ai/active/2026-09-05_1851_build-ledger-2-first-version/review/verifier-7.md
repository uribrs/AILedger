# Verifier Pass 7

## Verdict

**PASS.** Verifier-6 F10 is repaired on the production path. CLI work creation now resolves and persists canonical absolute directory scopes, Core rejects relative scopes from every caller, and a later provider launcher cannot reinterpret the stored grant from another current directory. The solution builds cleanly, all 79 tests pass, all prompt-contract criteria and R1-R5 pass, and no material verifier finding remains.

Because `state.json.baseRef` remains `null` and the repository has no `HEAD`, verification covered the complete current repository and active task artifacts rather than a commit diff. This pass modified no production code.

## Verifier-6 Finding Disposition

| finding | final disposition | evidence |
|---|---|---|
| F10 — relative governed scopes can be rebound | resolved | `work add` resolves every supplied scope through `ResolveExistingDirectory` before constructing `AddWorkItemCommand` (`src/AILedger.Cli/CliApplication.cs:176-183`), so the event/state store receives canonical absolute directories. Core independently rejects any relative scope (`src/AILedger.Core/Application/CommandHandler.cs:319-352`). Provider launch rejects legacy relative records and canonicalizes the persisted absolute paths without using launcher CWD (`src/AILedger.Cli/CliApplication.cs:376-399`). `WorkCreationFreezesRelativeScopeAsCanonicalAbsolutePath` and `CoreRejectsRelativeResourceScope` pass. |

### Independent two-directory reproduction

The exact verifier-6 attack sequence was rerun against current production CLI code in `/private/tmp/ailedger-pass7-scope.wLyA1w`:

1. From `operator-cwd`, the operator created `W1` with relative input `scope-dir`.
2. State persisted `/private/tmp/ailedger-pass7-scope.wLyA1w/operator-cwd/scope-dir`, not the relative text.
3. From `launcher-cwd`, the owning implementation lead requested `/private/tmp/ailedger-pass7-scope.wLyA1w/launcher-cwd/scope-dir`.
4. Launch exited 1 with `Provider directory ... is outside work item 'W1' scope`; state remained version 4 and `R1` was never created.

This proves the grant is fixed when the operator creates the work item and cannot be rebound by a later launcher CWD. Existing sibling-prefix, symlink-escape, non-owner, and non-operator-unscoped protections remain green.

## Independent Verification

- `dotnet build AILedger.sln --no-restore -m:1 --disable-build-servers` — passed; 0 warnings, 0 errors.
- `dotnet test tests/AILedger.Tests/AILedger.Tests.csproj --no-build --no-restore -m:1 --disable-build-servers --logger 'console;verbosity=normal'` — passed; **79 passed, 0 failed**.
- `git diff --check` — passed.
- F10 production reproduction — passed as described above; no unauthorized run/event was persisted.
- Cognitive source verification — exact nine-entry inventory, byte equality to Git object contents at `5bdb62717f8da0b33075b704b519a9e80a4e8490`, and every manifest SHA-256 passed.
- Focused verifier context — current CLI output contains governing rules, task-orchestrator skill/rubric, task goal/stage, assigned work scope, capabilities, authority constraint, and stop condition without unrelated skills.
- Documentation — README reports 79 tests and current provider versions; operator documentation accurately explains creation-time canonicalization and no launch-time rebinding.
- Current provider versions remain Codex `0.150.0-alpha.8` and Claude Code `2.1.261`.

## Provider Evidence Applicability

- F10 changed the local pre-launch scope guard and work-item persistence only. Provider command arguments, process lifecycle, JSONL parsing, terminal success predicate, session acquisition/resume, and permission flags are unchanged.
- Authenticated CR1/CR2 and CL6/CL7 therefore remain valid live evidence for both adapters' external launch/resume contracts. CL6/CL7 also used owned work items with canonical-compatible absolute scope shapes.
- CR1/CR2 were unscoped non-operator CLI runs and would not pass today's guard unchanged; they are adapter/protocol evidence rather than one continuous current-guard Codex CLI run. The current guard is independently exercised through production CLI tests and the F10 reproduction. This compositional evidence satisfies the adapter criterion without repeating an authenticated external run whose protocol path did not change.

## Success Criteria Cross-check

| # | result | evidence |
|---|---|---|
| 1 | PASS | .NET 8 solution builds cleanly; 79/79 tests pass. |
| 2 | PASS | Six canonical skill directories, their eight files, and canonical rules match pinned source bytes and manifest hashes. |
| 3 | PASS | Durable task open/reopen/status/history, authoritative replay, recovery, and concurrency paths pass. |
| 4 | PASS | State/events represent all required governance entities and provenance; validation remains fail closed. |
| 5 | PASS | Single-writer mutation, atomic event-log commit, complete lock deadline, replay, and projection recovery pass. |
| 6 | PASS | Task, assumption, and decision Markdown files remain disposable, repairable projections. |
| 7 | PASS | Reject/supersede invalidation and temporal creation/start/completion safety pass. |
| 8 | PASS | Roles and scope are explicit, audited, operator-controlled, self-expansion resistant, and now durably canonical. |
| 9 | PASS | Deterministic role/work context and verifier/reviewer isolation pass source, tests, hashes, and focused CLI checks. |
| 10 | PASS | Fakeable adapters, governed canonical directory grants, process cleanup, exact sessions, current capability/version evidence, and authenticated provider runs pass. |
| 11 | PASS | Required open/status/history/attach/context and initial truth CLI operations remain present and tested, with work/run/stage/provider extensions. |
| 12 | PASS | R3 proves one pending/active orchestration run per work item under concurrent service instances. |
| 13 | PASS | README and docs cover architecture, setup, CLI, providers, canonical scope behavior, persistence/recovery, and limitations with the current 79-test count. |
| 14 | PASS | All 79 tests pass, including the two-lead durable-shared-state end-to-end scenario. |
| 15 | PASS FOR VERIFIER GATE | Seven independent verifier passes and three isolated code reviews exist; this post-repair verifier is clean. A final isolated post-repair code-review gate remains before closeout. |

## Constraint and Execution Review

- The bounded v2.1 slice, .NET conventions, local inspectable persistence, provider-neutral Core, immutable cognitive snapshot, explicit exclusions, source-checkout read-only rule, preserved dossiers, and no-commit/no-push constraints remain honored.
- Operator authority is restored for operational provider scope: relative input is frozen at operator-time, direct Core callers cannot persist ambiguous scope, and providers receive only existing canonical paths contained by stored grants.
- Decomposition remains justified by the original disjoint ownership sets. The repair stayed inside the established CLI/Core/test/docs surfaces.
- No OPEN assumption, unresolved attention item, unsupported provider claim, or undocumented decision drift remains.

## Assumption Disposition

| id | status | name | citation | actor |
|----|--------|------|----------|-------|
| A1 | VALIDATED | v2.1-first-slice-boundary | Original request; `task.md`; `prompt_contract.md`; `constraints.md`; dossier §16 | verifier |
| A2 | VALIDATED | local-provider-contracts | `research/provider-cli-launch-contracts.md`; current local versions/help; authenticated Codex CR1/CR2 and Claude CL6/CL7 adapter/protocol evidence | verifier |
| A3 | VALIDATED | single-ledger-writer-provider-boundary | `IGovernedTaskService`; Core validation; atomic storage; durable provider completion; R3; F10 production reproduction | verifier |
| A4 | VALIDATED | verbatim-cognition-plus-code-policy | Independent nine-entry source-object/manifest byte/hash verification; Core policy; architecture mapping | verifier |
| A5 | VALIDATED | local-file-persistence-sufficient | Full storage suite; actual 30-second deadline evidence; replay/recovery/projection/concurrency checks | verifier |
| A6 | NEVER-TESTED | initial-commit-after-implementation | `git rev-parse --verify HEAD` still fails; no-commit constraint remains active | verifier |
| A7 | VALIDATED | researcher-with-missing-reference-files | Provider research dossier, provider tests, current local probes, and authenticated evidence | verifier |

A6 risk remains unchanged: the finished repository is unversioned until the operator authorizes its first commit. No assumption is OPEN, and all statuses remain terminal.

## Attention Item Disposition

| id | final disposition | name | evidence |
|---|---|---|---|
| R1 | handled | stale-materialized-state | Replay authority, atomic history commit, stale/missing projection healing, and postcommit failure tests pass. |
| R2 | handled | actor-self-escalation | R2 and privileged Core authority tests pass; canonical creation-time scope plus the two-directory reproduction now prevent operational scope rebinding. |
| R3 | handled | duplicate-active-run | Concurrent services permit exactly one active run for a work item; the complete task-lock deadline remains verified. |
| R4 | handled | reviewer-context-leakage | Reviewer deny-list and focused verifier allow-list context checks pass. |
| R5 | handled | false-provider-success | Protocol/session/malformed/overflow/process-cleanup/durable-status tests pass; current authenticated adapter evidence remains applicable as qualified above. |

## Decision Drift

| decision | disposition | evidence/reason |
|---|---|---|
| Build the bounded first slice | landed as decided | Implementation and deferred list match the signed contract. |
| Preserve AILedger 1.x skills as immutable inputs | landed as decided | Nine cognitive artifacts remain source-byte/hash identical. |
| Keep roles/capabilities provider-neutral | landed as decided | Core authority contains no provider-specific role logic. |
| Use one authoritative writer | landed with deliberate physical refinement | Atomic full-history replacement preserves logical append-only events and one mutation boundary. |
| Include real adapters with a fakeable process boundary | landed as decided | Tests and authenticated provider evidence cover both adapters. |
| Keep one governed run per work item | landed as decided | R3 passes. |
| Proceed on then-unverified provider stability | resolved by evidence | Current versions/help and authenticated exact-session evidence validate external contracts. |
| Proceed on then-unverified file persistence | resolved with refinement | Replay, atomic replacement, bounded locking, and recovery tests validate the store. |
| Use direct child processes, argument arrays, JSONL, and exact sessions | landed as decided | Process/adapter tests and live logs confirm the mechanism. |
| Use fail-closed workspace permission mappings | fully landed after F10 repair | Work creation freezes canonical absolute scope; Core rejects relative grants; launch contains all directories and rejects rebinding, siblings, and symlinks. |
| Exclude Claude ambient MCP and slash-command skills | landed as decided | Adapter arguments and CL6/CL7 evidence remain valid. |
| Preserve invalidation across time | strengthened after review and landed | Creation/start guards and completion preservation pass. |
| Reap failed provider processes before returning | strengthened after review and landed | Source audit and callback-quiescence test pass. |
| Bound complete task-lock acquisition | strengthened after review and landed | Shared deadline and actual 30-second failure path pass. |

## Required Next Gate

No verifier repair remains. Run the isolated post-repair code-reviewer with minimal implementation context. This pass did not archive, commit, push, or mark closeout complete.
