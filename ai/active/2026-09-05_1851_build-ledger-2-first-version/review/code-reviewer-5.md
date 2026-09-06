# Independent Code Review

## Calibration

- Change type: infrastructure/orchestration with a public core API, filesystem persistence, and external process execution.
- Risk: high. The implementation controls authorization, authoritative state, provider sandbox grants, cancellation, and recovery.
- Verification performed: `dotnet test AILedger.sln --no-restore` passed all 98 tests; `dotnet build AILedger.sln -c Release --no-restore` succeeded with zero warnings.

## Findings

### Blocker — Provider scopes inside the Ledger root are accepted

**Problem:** `EnsureLedgerIsOutsideProviderDirectories` only rejects a provider directory that is equal to or contains the Ledger root (`IsContainedPath(directory, canonicalLedgerRoot)`). It does not reject the inverse case, where a provider directory is itself inside the Ledger root. Both work creation and provider launch call this same incomplete check (`src/AILedger.Cli/CliApplication.cs:181-190`, `363-479`). A work scope equal to `<ledger-root>/<task-id>` is therefore accepted, and provider-grant resolution accepts the task directory as the working directory.

**Impact:** The launched provider receives write access to the directory containing the authoritative `events.jsonl`, `state.json`, projections, and writer lock. A governed worker can directly rewrite its task history, manufacture authority, corrupt another task if granted that sibling directory, or interfere with locking. This defeats the trust boundary that the provider sandbox and out-of-scope checks are intended to enforce. I confirmed the CLI accepts a task directory as a work scope and proceeds through provider-grant resolution.

**Recommended fix:** Require the Ledger root and every provider-writable directory to be disjoint. Reject when either `IsContainedPath(providerDirectory, ledgerRoot)` or `IsContainedPath(ledgerRoot, providerDirectory)` is true, after canonical/symlink resolution. Apply the same rule to scoped and unscoped launches. Add tests for a task directory, a sibling task directory, descendants of the Ledger root, and symlinks resolving to each case.

**Change shape:** Local patch plus focused security tests; no architectural refactor is required.

### Major — Cancellation discards a provider session learned after launch

**Problem:** `AgentAdapterBase.RunAsync` keeps the observed/preassigned session ID only in its local `sessionId` variable (`src/AILedger.Providers/Adapters/AgentAdapterBase.cs:50-61`). It converts only its internal timeout into an `AgentRunResult`; caller cancellation escapes as an exception (`110-121`). `CliApplication` then closes the run using the original command-line `sessionId`, which is null for a new run (`src/AILedger.Cli/CliApplication.cs:320-329`). This affects Codex after `thread.started` has supplied an ID and Claude after `BuildArguments` has generated one.

**Impact:** Ctrl-C or SIGTERM after session establishment records a cancelled run without its exact provider session identity. The provider session may still exist, but the Ledger can no longer resume it safely, producing an orphaned remote/local session precisely on the recovery path where resume is most valuable.

**Recommended fix:** Preserve partial run metadata across cancellation. For example, have the adapter throw a typed interruption exception containing the observed session ID and partial result, or return a cancelled partial result and let the CLI persist it before re-propagating caller cancellation. Test cancellation after a Codex `thread.started` event and after Claude session preassignment, asserting that the terminal Ledger event retains the exact session ID.

**Change shape:** Small cross-layer refactor of the adapter/CLI interruption contract; a local CLI-only patch cannot recover an ID that the adapter currently discards.

### Major — Replay accepts semantically invalid authority-changing events

**Problem:** replay validates the event envelope and sequence, then passes payloads directly to `TaskReducer` (`src/AILedger.Storage/FileGovernedTaskService.cs:133-149`). The reducer validates only schema/task/opening shape before applying payloads (`src/AILedger.Core/Domain/TaskReducer.cs:7-31`). It does not validate event-level authorization, payload provenance, role/capability constraints, referenced entities, or legal status transitions. For example, `RoleAssigned` is applied without proving that the event actor was an operator or that `Assignment.AssignedBy.ActorId` matches it.

**Impact:** A syntactically valid but corrupted event line can silently reconstruct an impossible state and authorize later commands. I confirmed that changing only the assigned actor in the opening `actor.role-assigned` event passed replay, made that actor an operator, and allowed it to attach another operator. Other malformed payloads can instead produce unhandled dictionary lookup failures during replay. This undermines fail-closed recovery and makes derived state depend on unchecked payload assumptions.

**Recommended fix:** Validate every persisted event transition against the prior state during replay, including actor authorization, provenance equality, legal enum/status transitions, reference existence, initial-opening invariants, and role/capability restrictions. Keep this validation in a shared domain transition boundary so replay cannot diverge from command handling. If protection from a writer who can rewrite the complete history is part of the threat model, semantic checks are insufficient; add a keyed signature or externally anchored hash chain.

**Change shape:** Required domain-level refactor. Scattered replay-only checks would duplicate rules and drift from command handling.

### Major — A successful provider result can be lost while the run remains active

**Problem:** after the provider returns, the CLI attempts the terminal Ledger write once, with a fixed 10-second token, before exposing the result (`src/AILedger.Cli/CliApplication.cs:340-342`, `350-360`). This completion is outside the launch exception-recovery block. Lock contention, transient IO failure, or the 10-second token expiring leaves the already-finished external operation persisted as `Active`; its result and newly learned session ID are never written to stdout. The mutation lock itself permits a 30-second wait, so the completion deadline can expire during otherwise supported contention (`src/AILedger.Storage/TaskMutationLock.cs:7-24`).

**Impact:** The work item cannot start another run and the task cannot archive until an operator manually repairs it. The operator may not know the provider's terminal status or session ID because output occurs only after completion persistence succeeds. This is an unsafe distributed-process recovery gap.

**Recommended fix:** Introduce a recoverable terminal-persistence path: use a completion deadline compatible with lock behavior, retry known pre-commit failures, and retain/report the provider result whenever closure fails. Make reconciliation idempotent (same run/status/session is success) or add an explicit reconciliation command that consumes the retained result. Test injected lock contention and a first-attempt completion failure after a successful provider return.

**Change shape:** Small lifecycle/persistence refactor; merely increasing the timeout reduces frequency but does not make recovery reliable.

### Major — Accepting a replacement can overwrite an invalidated predecessor

**Problem:** `ProposeDecision` verifies that `Supersedes` references a current decision, but `ResolveDecision` does not revalidate that predecessor when the replacement is later accepted (`src/AILedger.Core/Application/CommandHandler.cs:224-235`, `249-273`). It always emits `DecisionResolved(predecessor, Superseded)`. If the predecessor became `Invalidated` after proposal, the reducer changes it back to `Superseded`; multiple pending replacements can likewise be accepted after the first has already superseded the common predecessor.

**Impact:** Current state erases the stronger invalidation status and can represent conflicting accepted replacements. Downstream context and projections then present misleading governance state even though the history contains the earlier invalidation.

**Recommended fix:** Re-read and validate the predecessor at acceptance. Reject acceptance when it is no longer current, or model invalidation and supersession as separate facts if both must be retained. Add tests for invalidation between proposal and acceptance and for competing replacements of one predecessor.

**Change shape:** Local domain patch plus tests.

### Minor — State reads fail when disposable projections cannot be repaired

**Problem:** `GetStateAsync` always rewrites all projections and lets projection exceptions escape (`src/AILedger.Storage/FileGovernedTaskService.cs:89-97`), while mutation-time projection failures are explicitly suppressed because projections are disposable (`294-306`). A missing, unwritable, or malformed projection destination can therefore make a valid authoritative state unavailable through the read API.

**Impact:** `status` and any caller using `GetStateAsync` can fail even though replay succeeded and commands may continue committing. This couples authoritative reads to non-authoritative presentation files and complicates recovery.

**Recommended fix:** Return replayed state independently of projection repair. Treat repair as best-effort with an observable warning/result, or expose an explicit repair operation whose failure does not change read availability.

**Change shape:** Local patch; no refactor required.
