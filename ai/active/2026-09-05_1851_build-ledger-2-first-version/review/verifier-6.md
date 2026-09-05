# Verifier Pass 6

## Verdict

**FAIL.** Temporal dependency safety, process cleanup, result snapshots, the complete lock deadline, documentation count, cognitive/context integrity, and all 77 tests pass. One material scope-authority defect remains: a relative work-item scope is not durably tied to the directory in which the operator defined it. A later non-operator launch resolves that same stored text against the launcher's different current directory and can grant a provider access there.

Because `state.json.baseRef` remains `null` and the repository has no `HEAD`, this pass inspected the complete repository and active task record rather than a commit diff. The verifier changed no production code.

## Material Finding

### F10 — Relative governed scopes can be rebound by the provider launcher

**Evidence:** `AddWorkItem` persists `ResourceScope` as trimmed strings (`src/AILedger.Core/Application/CommandHandler.cs:319-348`). At provider launch, every relative stored scope is combined with the *current launch process* directory (`src/AILedger.Cli/CliApplication.cs:376-382`), not a durable root captured when the operator created the work item. The operator guide explicitly describes that invocation-relative behavior (`docs/operator-guide.md:172`).

An independent production-CLI reproduction used two different current directories, each containing `scope-dir`:

1. From `operator-cwd`, the operator created `W1`, owned by `lead`, with stored scope `scope-dir`.
2. From `launcher-cwd`, `lead` launched `R1` with working directory `launcher-cwd/scope-dir`.
3. The scope guard accepted the unrelated physical directory, persisted `run.started`, invoked `/usr/bin/true`, and then durably closed the run as failed only because that executable produced no version text.

Workspace: `/private/tmp/ailedger-pass6-scope.CDoY2m`. The launch exited 1 with `codex version probe failed with exit code 0`; state advanced from version 4 to version 6 and contains `R1`, proving the launch passed scope authorization. Had the executable been an authenticated provider, it would have received the rebound directory.

This violates the contract constraint that agents cannot silently change task scope and the architecture's claim that provider grants remain within the selected operator-defined scope. The new sibling-prefix and symlink tests pass because they use absolute scope paths; they do not exercise stable interpretation across invocations.

**Required repair:** Canonicalize relative scope against a stable, trusted base when the operator creates it and persist the resulting absolute/canonical path, or persist the base required to resolve it later. Provider launch should reject ambiguous legacy relative scopes rather than bind them to its own current directory. Also enforce work ownership and operator-only unscoped runs atomically in the Core `StartRun` boundary; the current pre-read at `CliApplication.cs:280-282` leaves those role-dependent checks outside the single-writer command transaction. Add a two-working-directory regression test through the production CLI/service path.

## Code-review-3 Repair Disposition

| repair target | result | evidence |
|---|---|---|
| Provider directory grants | FAIL | Absolute sibling-prefix and symlink escapes are rejected, owner and unscoped checks behave correctly, and directories must exist; F10 shows that stored relative scope can still be rebound across CLI invocations. |
| Temporal dependency safety | PASS | `EnsureDependenciesAreCurrent` guards decision proposal/resolution, work creation, and run start for rejected/superseded claims. Reject-then-create and supersede/invalidation tests pass; `TaskReducer.CompleteRun` preserves `Blocked`/`Stale` after terminal run completion. |
| Failed-process cleanup | PASS | Failure cancels the linked token, kills the tree, waits up to five seconds for exit, and observes all active exit/stdout/stderr/stdin tasks before preserving the original exception. `ReceiverFailureReapsProcessBeforeReturning` proves callback quiescence after return; the real runaway-output test remains green. |
| Adapter result snapshots | PASS | Both normal and exceptional `AgentRunResult` paths copy provider events with `ToArray`; process cleanup completes before failure-result construction, and stderr is materialized to an immutable string. |
| End-to-end lock deadline | PASS | One absolute deadline covers the 30-second in-process semaphore wait and remaining cross-process file-lock phase. A real CLI/file-lock probe returned exit 1 in exactly 30 seconds with the documented timeout error, and the task was immediately readable afterward. |
| Documentation and test count | PASS WITH F10 CAVEAT | README correctly reports 77 tests and current provider versions. Architecture/operator docs cover temporal invalidation, provider scope checks, output bounds, persistence, and lock behavior; their invocation-relative scope statement accurately describes the implementation but exposes the unsafe semantics in F10. |

## Owner and Unscoped Authority Checks

- A second implementation lead attempting to launch the first lead's work inside its otherwise valid directory was rejected before run creation: `Actor 'other-lead' does not own work item 'W1'.`
- A non-operator `ManageRuns` lead attempting an unscoped launch was rejected before run creation: `Only an operator may launch a provider without governed work scope.`
- An operator unscoped launch advanced to adapter probing and durably failed there, confirming the explicit operator exception.
- These checks used `/private/tmp/ailedger-pass6-scope.CDoY2m`; only `R1` (the F10 reproduction) and the operator probe were persisted.

## Live Provider Evidence Applicability

- Codex remains `0.150.0-alpha.8`; Claude Code remains `2.1.261`; all required current help surfaces are present.
- CR1/CR2 and CL6/CL7 remain valid authenticated evidence for the unchanged provider argument, process, JSONL, terminal-event, and exact-session mechanisms. The repair did not change normal adapter/process semantics.
- CL6/CL7 used owned work items with absolute `/private/tmp/ailedger2-provider-work-20260905` scopes, so their command shape is compatible with the new guard. CR1/CR2 were unscoped runs by the non-operator `codex-lead`; those exact CLI commands would now be rejected before the adapter. They remain adapter/protocol evidence, but are not evidence that the current full Codex CLI path passes the new scope guard.
- No repeat authenticated provider execution is needed to validate unchanged external protocol behavior. After F10 is repaired, a current work-scoped Codex launch/resume smoke is warranted before claiming one continuous current-code CLI path for Codex.

## Independent Verification

- `dotnet build AILedger.sln --no-restore -m:1 --disable-build-servers` — passed; 0 warnings, 0 errors.
- `dotnet test tests/AILedger.Tests/AILedger.Tests.csproj --no-build --no-restore -m:1 --disable-build-servers --logger 'console;verbosity=normal'` — passed; **77 passed, 0 failed**.
- `git diff --check` — passed.
- Cognitive snapshot — exact nine-entry inventory, source Git-object byte equality at `5bdb62717f8da0b33075b704b519a9e80a4e8490`, and every manifest SHA-256 passed.
- Focused verifier context — current CLI context from `/private/tmp/ailedger-verifier-pass5.sknu6f` contains governing rules, task-orchestrator skill/rubric, task goal/stage, work scope, capabilities, authority constraint, and stop condition without unrelated skills.
- Provider versions/help — current installed versions and all adapter-required launch/resume/isolation flags passed local probes.
- Lock deadline — an externally held task lock made production `status` fail in exactly 30 seconds; the subsequent status read succeeded.
- Scope authority — absolute sibling and symlink tests pass; independent non-owner/unscoped checks pass; cross-directory relative-scope reproduction fails as F10.

## Success Criteria Cross-check

| # | result | evidence |
|---|---|---|
| 1 | PASS | .NET 8 solution builds cleanly; 77/77 tests pass. |
| 2 | PASS | All six canonical skill directories, their eight files, and canonical rules match pinned source bytes and hashes. |
| 3 | PASS | Durable open/reopen/status/history, authoritative replay, recovery, and concurrency behavior pass. |
| 4 | PASS | State/events represent every required governance entity and provenance; replay validation remains fail closed. |
| 5 | PASS | Single-writer mutation, atomic event-log commit, recoverable projections, envelope checks, and complete lock deadline pass. |
| 6 | PASS | Task, assumption, and decision Markdown files remain derived, repairable projections. |
| 7 | PASS | Causal invalidation now remains safe across later creation, start, and completion sequences. |
| 8 | PASS | Role assignment remains explicit, audited, operator-controlled, and self-expansion resistant. |
| 9 | PASS | Deterministic role/work context and verifier/reviewer isolation pass source, unit, and focused CLI checks. |
| 10 | FAIL | Adapter/process/session mechanics and live evidence pass, but the integrated launch boundary can reinterpret a relative operator-defined work scope and grant a provider a different directory (F10). |
| 11 | PASS | Required task, truth, context, work, run, stage, and provider CLI surfaces remain present and tested. |
| 12 | PASS | R3 still proves one pending/active run per work item under concurrency. |
| 13 | PASS WITH F10 CAVEAT | Documentation covers all required topics and accurately states invocation-relative scope behavior, but the architecture's operator-scope guarantee is not actually durable. |
| 14 | PASS | All 77 tests pass, including two differently assigned leads coordinating through durable state. |
| 15 | FAIL PENDING REPAIR | Six independent verifier passes and three isolated code reviews exist, but this material verifier finding prevents closeout. |

## Constraint and Execution Review

- The v2.1 slice, .NET conventions, local inspectable store, provider-neutral Core, immutable cognitive snapshot, explicit exclusions, read-only source checkout, preserved dossiers, and no-commit/no-push constraints remain honored.
- Decomposition remains justified and file ownership remains coherent.
- F10 violates the explicit operator-authority/task-scope constraint. It must be repaired before closeout.
- Atomic event persistence, causal invalidation, process cleanup, and lock repair introduce no contradictory drift.

## Assumption Disposition

| id | status | name | citation | actor |
|----|--------|------|----------|-------|
| A1 | VALIDATED | v2.1-first-slice-boundary | Original request; `task.md`; `prompt_contract.md`; `constraints.md`; dossier §16 | verifier |
| A2 | VALIDATED | local-provider-contracts | `research/provider-cli-launch-contracts.md`; current versions/help; authenticated Codex CR1/CR2 and Claude CL6/CL7 adapter/protocol evidence; applicability qualification above | verifier |
| A3 | VALIDATED | single-ledger-writer-provider-boundary | `IGovernedTaskService`; atomic storage; durable provider completion; R3; note that F10 concerns pre-launch scope interpretation, not mutation routing | verifier |
| A4 | VALIDATED | verbatim-cognition-plus-code-policy | Independent nine-entry source-object/manifest byte/hash verification; Core policy; rule mapping | verifier |
| A5 | VALIDATED | local-file-persistence-sufficient | Full storage suite; actual 30-second deadline; successful post-timeout replay/read; recovery and concurrency evidence | verifier |
| A6 | NEVER-TESTED | initial-commit-after-implementation | `git rev-parse --verify HEAD` still fails; no-commit constraint remains active | verifier |
| A7 | VALIDATED | researcher-with-missing-reference-files | Research dossier, provider tests, current help probes, and authenticated protocol evidence | verifier |

A6 risk is unchanged: the repository remains unversioned until the operator authorizes its initial commit. No assumption is OPEN; all terminal statuses are preserved.

## Attention Item Disposition

| id | final disposition | name | evidence |
|---|---|---|---|
| R1 | handled | stale-materialized-state | Replay authority, atomic history commit, stale/missing projection healing, and I/O/non-I/O failure tests pass. |
| R2 | handled | actor-self-escalation | The named R2 role-escalation artifact passes, as do direct privileged-capability and scope-mutation Core tests. F10 is a distinct operational relative-path interpretation defect. |
| R3 | handled | duplicate-active-run | Concurrent services still allow exactly one active run per work item; the complete lock deadline passes. |
| R4 | handled | reviewer-context-leakage | Reviewer deny-list and focused verifier allow-list context checks pass. |
| R5 | handled | false-provider-success | Protocol, session, malformed/overflow, callback-failure cleanup, durable terminal status, and exit-code tests pass; current live adapter evidence remains applicable as qualified above. |

## Decision Drift

| decision | disposition | evidence/reason |
|---|---|---|
| Build the bounded first slice | landed as decided | Current implementation and deferred list match the signed boundary. |
| Preserve AILedger 1.x skills as immutable inputs | landed as decided | Nine cognitive entries remain source-byte/hash identical. |
| Keep roles/capabilities provider-neutral | landed as decided | Core authority remains provider-neutral. |
| Use one authoritative writer | landed with deliberate physical refinement | Atomic full-history replacement preserves logical append-only events and single-writer semantics. |
| Include real provider adapters with a fakeable process boundary | landed as decided | Tests and authenticated protocol evidence cover both adapters. |
| Keep one governed run per work item | landed as decided | R3 passes. |
| Proceed on then-unverified provider stability | resolved by evidence | Current versions/help and authenticated session evidence validate external contracts, with the CLI-guard qualification above. |
| Proceed on then-unverified file persistence | resolved with refinement | Replay, atomic replacement, complete lock deadline, and recovery tests validate the local store. |
| Use direct child processes, argument arrays, JSONL, and exact sessions | landed as decided | Provider tests, current probes, and live session logs confirm the mechanism. |
| Use fail-closed workspace permission mappings | incomplete — material drift | Absolute/symlink containment works, but relative governed scope is rebound at launch and is not durably operator-defined (F10). |
| Exclude Claude ambient MCP and slash-command skills | landed as decided | Adapter arguments and CL6/CL7 evidence remain unchanged and valid. |
| Preserve invalidation across time | strengthened after review and landed | Creation/start guards and completion preservation tests pass. |
| Reap failed provider processes before returning | strengthened after review and landed | Source audit and real callback-quiescence tests pass. |
| Bound the complete task-lock acquisition | strengthened after review and landed | Shared 30-second deadline and real end-to-end timeout pass. |

## Required Repair and Reverification

Repair F10 without weakening owner/unscoped, sibling-prefix, or symlink protections; add the cross-working-directory production-path regression. Re-run the verifier as a new pass. Because the repair affects launch authorization and possibly stored scope contracts, keep closeout and archival pending.
