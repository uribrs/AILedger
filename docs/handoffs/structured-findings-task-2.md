# Handoff to Astra: task 2 only

## Assignment

Implement the atomic, retry-safe findings application operation specified in [Task 1's design](../structured-findings-v1.md). This is task 2 of [backlog 63](../../Backlog/structured-agent-interface.md), priority 1. Stop after its application/storage contract and meaningful verification are complete. Do not proceed to MCP, provider integration, or an episodic rewrite.

The user explicitly turned the kernel workflow off for this effort. Work directly in the repository. Do not invoke `ai-kernel`, open a governed task, dispatch a provider through `ailedger`, or make development progress depend on ledger writes. Existing command authorization/validation remains part of the implementation and is exercised in isolated tests. This exception is local to this effort; do not edit global skills or AGENTS instructions. Respect the repository's small-method/SRP conventions and the .NET skill. The user is assigning one scoped task per agent; do not spawn more agents unless asked.

## Where to work and what already exists

- Worktree: `/Users/user/.codex/worktrees/structured-findings-contract/AILedger`.
- Branch: `codex/structured-findings-contract`.
- Baseline production source: `b72437ad3a91a8dea0503d0010582ea676533566`. Task 1 adds design, schemas, frozen fixtures and a read-only report harness; it makes no `src/` changes.
- Use the latest task-1 commit on this branch as your implementation base. Inspect `git status` before starting and preserve unrelated edits. Do not copy changes back to the original checkout, merge, install the global tool, or publish automatically.
- The original checkout contained unrelated lesson changes and a backlog-file deletion. They were not brought into this worktree. Its uncommitted design documents and priority list were copied in so this worktree is self-contained.

## Read in this order

1. [Task 1 design](../structured-findings-v1.md): contract, authority, hash semantics, storage choice, errors, and acceptance matrix. These are settled choices for this slice; report a concrete contradictory implementation fact if one must change.
2. [Request schema](../structured-findings-v1/request.schema.json), [response schema](../structured-findings-v1/response.schema.json), examples, and [fingerprint vector](../structured-findings-v1/fingerprint.example.json). They describe the future contract, not a running endpoint.
3. [Measurement baseline](../structured-findings-v1/measurement-baseline.md) and [validation record](../structured-findings-v1/task-1-validation.md). Run fixture verification before changing production code.
4. Source locations named below. The full survival investigation is optional background; avoid loading its history into your brief unless a specific design question requires it.

## Hard requirements

- One typed `IFindingsRecorder` operation; keep `IGovernedTaskService.ExecuteAsync` compatible. No generic batch escape hatch.
- Acquire the existing cross-process task lock once. Reuse `CommandHandler` against sequential candidate state. Only persist after every operation is valid.
- Findings create open claims. Evidence direction and recalled-lesson checks remain authoritative. Recording evidence never silently resolves a claim.
- Bind task/actor/run/correlation/grants through trusted host input, never agent payload. Reuse current kernel capabilities; do not relax authority or add an active-run prerequisite.
- Allocate durable IDs under the lock and resolve local references. Preserve existing event IDs, ordering and version semantics.
- Commit the immutable receipt in `_ailedgerFindingsReceipt` on the first event of a fully marked append group. A separate sidecar receipt commit is not sufficient. The design covers one-event groups, incomplete tails, metadata corruption, and a complete write followed by a failed flush.
- A repeated key with the same normalized content/binding returns the original receipt; different content conflicts. Recheck authority for receipt access; do not replay current mutation-stage rules on an already committed request.
- Preserve `LedgerEvent.CorrelationId == run ID` for run-bound writes. Request/transaction/attempt IDs must not replace it. Preserve ordinary refusal journaling and add the scoped observational attempt capture described in the design.
- Missing measurements remain missing. No new synthetic domain events, duplicated run cost, or rebaselining old reports just to make tests pass.

## Source map

- `src/AILedger.Storage/FileGovernedTaskService.cs`: `ExecuteAsync`, `ValidateOutcome`, `ReadEventsAsync`, `AppendEventsAsync`, tail repair, replay and best-effort projection repair. This service owns the lock and append; do not loop over its public single-command method.
- `src/AILedger.Storage/TaskMutationLock.cs`, `RefusalJournal.cs`, `LedgerJson.cs`: concurrency, failure telemetry and compatibility.
- `src/AILedger.Core/Application/CommandHandler.cs`, `Domain/AuthorizationPolicy.cs`: command translation must reuse these rules.
- `src/AILedger.Core/Claims/Logic/ClaimRules.cs`, `Evidence/Logic/EvidenceRules.cs`: existing trim, direction, ID/reference and lesson behavior.
- `src/AILedger.Core/Contracts/Events.cs`, `PersistenceContracts.cs`: current event envelope and service API. Raw optional storage fields can be ignored by typed historical consumers; new storage receipt lookup must retain and validate them.
- `src/AILedger.Cli/Providers/ProviderRunRecorder.cs`: first-write/run attribution assumption to preserve; no provider production edits in this task.
- Existing `Storage/RecoveryTests.cs`, `AppendInPlaceTests.cs`, `ConcurrencyTests.cs`, and telemetry tests under `tests/AILedger.Tests/` provide behavioral patterns. `tests/Fixtures/structured-findings-v1` is immutable baseline data.

Suggested additions stay local to `Core/Findings`, `Storage/Findings`, and `tests/AILedger.Tests/Findings`, with a small service refactor only as justified. Do not introduce broad abstractions or a second event store. Add a narrow internal fault seam only if actual append/flush recovery cannot otherwise be verified.

## Verification and finish

Follow the design's acceptance matrix, especially late-command rejection, same-key concurrency, competing CLI writes, lost-response retry after restart, torn groups, projection failure, metadata corruption, authority revocation, and unchanged run correlation. Check the full batch's event/byte cap before append. A cancelled/failed append may have an unknown outcome; never describe it as a rollback without proof.

Run the fixture hash checker and read-only report probe from the measurement document. Build outside the checkout. Run `dotnet test`; if its external-output working directory reproduces the documented environment failure, use `sh scripts/test-governed.sh all` and report both results honestly. Tests and probes must use disposable ledgers; no live provider launch is required in task 2.

Finish with the changed application contract, test results, any measured limitation, and a focused handoff for task 3. Update only task 2's backlog status when its actual acceptance criteria pass. Do not claim MCP/provider support or overall workflow speedup from this implementation.

## Suggested initial message for the fresh task

> Implement task 2 only from `docs/handoffs/structured-findings-task-2.md` in `/Users/user/.codex/worktrees/structured-findings-contract/AILedger`, branch `codex/structured-findings-contract`. Use this worktree directly. The kernel workflow is off for this effort; preserve kernel validation inside the implementation and isolated tests. Keep the measurement tools and frozen baselines intact. Complete the atomic, retry-safe application/storage operation and prepare a handoff for task 3. Do not start MCP/provider integration or dispatch other agents.
