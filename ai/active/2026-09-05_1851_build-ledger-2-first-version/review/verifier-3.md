# Verifier Pass 3

## Verdict

**FAIL.** The code-review repair materially improves persistence, projections, history locking, evidence direction, signal wiring, and provider-output containment, and the complete independent run passes all 56 tests. However, two high-impact lifecycle/audit paths remain incorrect: cancellation after a provider has returned a result can still strand its persisted run as `Active`, and replay validates deterministic event IDs but not the causal/provenance envelope it claims to validate. A narrower post-commit ambiguity remains possible through the public projection-writer boundary, and the provider-output documentation describes character limits as byte units.

Because `state.json.baseRef` is `null` and the repository remains unborn, this pass inspected the complete current source, tests, docs, cognitive snapshot, dossier-driven task artifacts, prior verifier reports, code-review report, and the repair notes rather than a commit diff.

## Findings

### F6 — Major — Cancellation after a successful provider result can strand the run

`CliApplication.LaunchProviderAsync` persists `RunStarted` before entering the launch `try` (`src/AILedger.Cli/CliApplication.cs:274-277`). The compensation block covers context assembly and adapter execution only (`:277-310`). The terminal `CompleteRunCommand` is outside that block and uses the caller's original token (`:312-314`). If Ctrl+C/SIGTERM arrives after the adapter returns, or while the terminal mutation is acquiring the lease/copying its temporary history, that mutation throws cancellation and no fresh-token compensation runs. The already-committed run and work item therefore remain `Active`, blocking later starts for that work item.

`CancelledProviderLaunchClosesPersistedRunWithFreshToken` does not cover this path: its fake cancels and throws inside `RunAsync` (`tests/AILedger.Tests/Cli/CliApplicationTests.cs:183-194`), so it exercises only the existing catch. The repair requested cancellation during completion persistence as a distinct case.

Required repair: put terminal persistence inside a lifecycle boundary that guarantees one bounded fresh-token close after any post-start cancellation, including cancellation after a successful adapter return and cancellation during the first completion attempt. Add a production-path test whose adapter cancels the caller token and returns a successful result, then assert a terminal persisted run/work status rather than `Active`.

### F7 — Major — Authoritative replay still accepts corrupted causal/provenance envelopes

`ValidateEventSequence` checks only task identity and the deterministic `taskId:sequence` event ID (`src/AILedger.Storage/FileGovernedTaskService.cs:313-321`). `TaskReducer.ValidateEnvelope` checks schema, task identity, and opening-event placement (`src/AILedger.Core/Domain/TaskReducer.cs:34-54`). Neither validates `CausationId`; replay also accepts empty/default actor, time, and correlation metadata after syntactically valid JSON deserialization. A hand-edited or otherwise corrupted event can therefore point to an unknown, future, or foreign cause and still become authoritative state.

This leaves the causal half of the prior code-review finding unresolved and conflicts with the rule mapping's claim that actor, time, correlation, causation, and sequence are validated (`docs/architecture.md:35`). The new test duplicates an event ID and proves only ID ordering (`tests/AILedger.Tests/Storage/RecoveryTests.cs:132-146`).

Required repair: validate the complete persisted event envelope during replay. At minimum require non-empty actor/correlation, a meaningful timestamp, and any non-null causation ID to name a valid earlier event in this task. If intra-command chaining is an invariant, persist enough batch information to validate it or narrow the documented guarantee explicitly. Add negative replay tests for unknown/future/foreign causation and malformed provenance.

### F8 — Moderate — Post-commit projection ambiguity is removed only for selected exception types

The event-log rename is correctly treated as the commit point, but `TryRepairDerivedStateAsync` suppresses only `IOException` and `UnauthorizedAccessException` (`src/AILedger.Storage/FileGovernedTaskService.cs:263-275`). `ITaskProjectionWriter` is a public injected boundary and has no exception restriction. A projection writer that throws another ordinary exception after the rename still makes `ExecuteAsync` report failure even though the command committed. The test injects only `IOException` (`tests/AILedger.Tests/Storage/RecoveryTests.cs:114-130,157-172`), so it does not establish the broader documented statement that a derived-view write failure never makes a committed command ambiguous (`docs/operator-guide.md:190`).

Required repair: make the post-commit contract explicit and non-ambiguous for all supported projection failures—either isolate all non-fatal projection exceptions from command success, return an explicit committed-with-repair-needed outcome, or constrain the writer boundary and documentation to the exact supported exception contract. Add a non-I/O projection failure test.

### F9 — Minor — Provider-output limits are documented in bytes but enforced in characters

`ProviderOutputLimits` caps 1,048,576 characters per line and 8,388,608 characters per stream/retained result (`src/AILedger.Providers/Process/SystemProcessRunner.cs:183-187`), while `docs/architecture.md:43` and `docs/operator-guide.md:168` call those values 1 MiB and 8 MiB. Unicode input makes those materially different quantities. Document the limits as characters, or enforce byte limits if MiB is the intended resource bound.

## Code-Review Repair Disposition

| prior finding | disposition | evidence |
|---|---|---|
| Torn final append can brick the log | resolved for exposed torn-tail semantics | `AppendEventsAsync` builds and flushes a same-directory temporary complete history before the overwrite rename (`FileGovernedTaskService.cs:175-226`). Before rename, the old authority remains intact; after rename, the new complete history is authoritative. A failed/cancelled pre-rename write leaves only a disposable temp file. Absolute power-loss durability of directory metadata is not promised/tested and remains a local-filesystem limitation. |
| Successful commits reported as failures / projections do not heal | substantially resolved; F8 remains | Default file-write failures are suppressed after commit; `GetStateAsync` always rewrites all Markdown projections and repairs stale `state.json`; both new recovery tests pass. The public writer's non-I/O exception path remains ambiguous. |
| Cancellation strands active runs | partially resolved; F6 remains | `Program.cs` wires Ctrl+C and SIGTERM, and adapter/context exceptions close with a fresh token. Terminal persistence remains outside the compensation block. |
| Unrelated evidence resolves claims | resolved | `CommandHandler.ResolveClaim` requires every cited item to support validation or refute rejection; both negative theory cases pass. |
| Provider output is unbounded | resolved | Per-line, per-stream, and combined retained character caps are enforced; limit violations fail fast, kill the child, and normalize to `ProtocolError`; fake adapter and real `/usr/bin/yes` tests pass. |
| History consumer holds mutation lease | resolved | History is fully read/validated into a snapshot under the lease and yielded after release; the paused-consumer concurrency test passes. |
| Replay does not validate identity/order/causation | partially resolved; F7 remains | Deterministic event identity/order is validated and tested; causal/provenance validation is absent. |

## Independent Verification

- `dotnet build AILedger.sln --no-restore -m:1 --disable-build-servers` — passed; 0 warnings, 0 errors.
- `dotnet test tests/AILedger.Tests/AILedger.Tests.csproj --no-build --no-restore -m:1 --disable-build-servers --logger 'console;verbosity=normal'` — passed; **56 passed, 0 failed**.
- `git diff --check` — passed.
- Cognitive source verification — exact inventory and byte equality passed for all nine manifest entries: eight files in the six canonical skill directories plus canonical `RULES.md`, each compared to its Git object at `5bdb62717f8da0b33075b704b519a9e80a4e8490` and its manifest SHA-256.
- Focused verifier-role CLI path — against `/private/tmp/ailedger-verifier-pass3.b9pejy`, task open, verifier attachment, and real `context build` all exited 0; the manifest contains `rules/governing-rules`, `skill/task-orchestrator`, its rubric, task goal/stage, authority constraint, and stop condition.
- Provider/session/capability paths — the full suite reran mixed-session rejection, exact resume handling, Codex `exec --help` plus `exec resume --help` probe expectations, bounded adapter output, and the real runaway-process termination path.
- Atomic persistence audit — the authoritative history has a single old-or-new rename commit point, sequence validation fails closed, derived views heal on reads, and history enumeration releases the writer lease before consumer control.

## Authenticated Provider Smoke Assessment

The prior authenticated CR1/CR2 Codex and CL4/CL5 Claude new/resume smokes remain valid normal-path evidence. Their provider argument builders, exact-session rules, and JSONL protocol parsers are unchanged by code-review repair 1. The repaired process runner preserves normal line delivery and adds only fail-fast size bounds/child termination; the recorded smoke results are far below those limits. Current argument/protocol tests and capability probes all pass. **No repeat authenticated live smoke is required for this repair set.** This conclusion does not excuse F6, which is a Ledger-side terminal-persistence race after adapter return rather than a provider invocation/protocol change.

## Success Criteria Cross-check

| # | result | evidence |
|---|---|---|
| 1 | PASS | .NET 8 solution builds cleanly; 56/56 tests pass. |
| 2 | PASS | Six canonical skill directories contain eight byte-identical files; canonical `RULES.md` is also preserved; all nine manifest entries match the source commit and hashes. |
| 3 | PASS | CLI/storage tests and focused smoke prove durable task open/reopen, status, history, authoritative replay, and stale-view repair. |
| 4 | PASS | The state and event contracts represent all named governance entities and provenance fields. F7 concerns validation of persisted envelope integrity, not representation. |
| 5 | FAIL | The old-or-new event-log commit and single-writer boundary are sound, but authoritative replay does not validate causal/provenance integrity and F8 leaves a narrower committed-but-reported-failed path. |
| 6 | PASS | Task, assumptions, and decisions Markdown files are derived, atomic views and are regenerated on reads. |
| 7 | PASS | Rejection/supersession invalidates dependent decisions and blocks/stales work; tests pass. |
| 8 | PASS | Roles/capabilities are explicit, event-audited, operator-controlled, and protected against self-escalation. |
| 9 | PASS | Deterministic work-scoped manifests include governing rules, role skills, task facts, evidence, capabilities, constraints, and stop conditions; verifier and isolated reviewer mappings are tested. |
| 10 | PASS | Both adapters retain verified real launch/resume evidence, exact sessions, fail-closed protocols, deterministic fakes, subcommand probes, and bounded/fail-fast output. Prior live smokes remain applicable. |
| 11 | PASS | The required CLI surface and the broader work/run/stage/provider commands are present and exercised. |
| 12 | PASS | R3 proves the one-active-run commit invariant under concurrent service instances. F6 can strand that single run but does not allow a duplicate. |
| 13 | FAIL | Setup and operational coverage are comprehensive, but the Ctrl+C guarantee, causal-envelope validation claim, derived-failure guarantee, and MiB units are not accurate on the remaining paths in F6-F9. |
| 14 | PASS | All 56 tests pass, including the two-lead durable-state end-to-end test. |
| 15 | FAIL | The required reviews ran, but code-review repair 1 still has material verifier findings. Closeout must wait for repair, verifier rerun, and an isolated post-repair code-review pass. |

## Constraint and Execution Review

- The implementation remains within the signed bounded v2.1 slice: .NET, local/inspectable state, provider-neutral Core, immutable cognitive copies, direct Codex/Claude adapters, and no automatic swarms, generic policy language, hard host enforcement, archive mover, or TTL implementation.
- The source `/Users/user/Dev/AILedger` was used read-only at the pinned commit. The two dossier files remain present. This repository still has no `HEAD`, commit, or push, honoring the no-commit constraint.
- The `decompose` decision remains correct in hindsight: the implementation, cognitive copy, providers, CLI, tests, and docs were disjoint, and independently owned tests were a hard decomposition trigger.
- Central code-review repairs stayed within the established source/test/doc surfaces. The physical storage technique changed from direct append to a logically append-only whole-history replacement, and that justified recovery-driven change is documented.
- Research alignment remains sound for provider arguments, sandboxing, exact sessions, structured output, and capability probes. No forbidden `--last`, `--continue`, shell-string, dangerous-bypass, ambient-Claude-MCP, or ambient-slash-skill behavior was introduced.

## Assumption Disposition

| id | status | name | citation | actor |
|----|--------|------|----------|-------|
| A1 | VALIDATED | v2.1-first-slice-boundary | Original request; `task.md`; `prompt_contract.md`; `constraints.md`; dossier §16 | verifier |
| A2 | VALIDATED | local-provider-contracts | `research/provider-cli-launch-contracts.md`; local CLI versions/help; authenticated CR1/CR2/CL4/CL5 smoke evidence; current provider tests | researcher |
| A3 | VALIDATED | single-ledger-writer-provider-boundary | `IGovernedTaskService`; `FileGovernedTaskService`; R3; provider mutations use the service. F6 is a missed terminal compensation path, not an alternate writer. | verifier |
| A4 | VALIDATED | verbatim-cognition-plus-code-policy | Independent nine-entry source-object/manifest byte and SHA-256 verification; Core policy code; architecture mapping | verifier |
| A5 | VALIDATED | local-file-persistence-sufficient | Atomic temp-history rename; R1/R3; recovery, sequence, history-release, and concurrent-writer tests; focused CLI persistence smoke | verifier |
| A6 | NEVER-TESTED | initial-commit-after-implementation | `git rev-parse --verify HEAD` fails; `constraints.md` forbids committing without an explicit request | verifier |
| A7 | VALIDATED | researcher-with-missing-reference-files | `research/provider-cli-launch-contracts.md`; provider tests; authenticated smoke evidence | verifier |

A6 risk is unchanged: the first version remains unversioned until the operator explicitly authorizes its initial commit. All assumptions are terminal; none is OPEN.

## Attention Item Disposition

| id | final disposition | name | evidence |
|---|---|---|---|
| R1 | handled | stale-materialized-state | R1 replay recovery, missing-projection repair, and post-commit I/O failure/healing tests all drive the production store and pass. |
| R2 | handled | actor-self-escalation | R2 plus operator self-assignment and explicit-capability tests pass through production authorization. |
| R3 | handled | duplicate-active-run | R3 concurrently starts two independent services and permits exactly one active run. F6 strands one run but never admits a duplicate. |
| R4 | handled | reviewer-context-leakage | R4 passes; reviewer allow-list excludes request/contract/plan/verifier artifacts, while the distinct verifier path receives task-orchestrator methodology. |
| R5 | handled | false-provider-success | R5, malformed JSON, non-zero/failed/missing terminals, resume mismatch, mixed session conflict, and output overflow paths all fail closed in the passing suite. |

## Decision Drift

| decision | disposition | evidence/reason |
|---|---|---|
| Bounded v2.1 first slice; immutable cognitive seeds; provider-neutral roles/capabilities; one writer/run | landed as decided | Current source, manifest, docs, R1-R4. |
| Direct append-only file history | deliberately refined after review | Direct append could expose a torn tail. Storage now rewrites a complete same-directory temporary history and atomically renames it; logical append-only semantics and the O(n) v0.1 cost are documented. |
| Derived state is disposable and recoverable | substantially landed; F8 remains | Default I/O failure and missing-view paths heal, but the public writer exception contract is broader than the suppression filter. |
| Graceful provider cancellation closes persisted runs | partially landed; F6 remains | Signal sources and fresh-token adapter-failure cleanup exist; cancellation during/after terminal persistence was omitted. |
| Deterministic causal event history | partially landed; F7 remains | Event IDs/order are now checked, but causal/provenance metadata is not validated on authoritative replay. |
| Provider output should be bounded and fail fast | landed, with documentation-unit drift | Process and retained-output character caps work and tests pass; docs call character counts MiB. |
| Real provider invocation/protocol contract | landed unchanged by repair | Existing authenticated smokes remain representative; new bounds do not change normal-path arguments or JSONL semantics. |

## Required Next Gate

Repair F6 and F7 before another verifier pass. Resolve F8's post-commit exception contract and correct F9's documentation in the same bounded repair. Then rerun the complete suite and verifier; only after a passing verifier should the isolated code-reviewer rerun against the repaired implementation.
