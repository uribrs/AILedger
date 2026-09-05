# Independent code review

## Calibration

- Change type: infrastructure/orchestration kernel and public contracts
- Risk: high (persistence, concurrency, authorization, provider permissions, and child-process lifecycle)
- Reviewed: `Directory.Build.props`, `NuGet.Config`, `src/**`, and `tests/**`
- Verification: `dotnet test tests/AILedger.Tests/AILedger.Tests.csproj --no-restore` passed (73/73)

## Findings

### Major — Provider filesystem grants are not constrained by the governed work-item scope

**Problem:** `AddWorkItemCommand` records operator-governed `ResourceScope`, but `CliApplication.LaunchProviderAsync` constructs `WorkingDirectory` and `AdditionalDirectories` directly from the launching actor's `--working-directory` and `--add-dir` arguments (`src/AILedger.Cli/CliApplication.cs:284-293`). The selected work item is never consulted for path authorization. Those caller-selected paths are then turned into actual writable sandbox roots by the Codex adapter (`src/AILedger.Providers/Adapters/CodexAgentAdapter.cs:18-19,70-74`) and into Claude `--add-dir` grants (`src/AILedger.Providers/Adapters/ClaudeAgentAdapter.cs:66-73`). Default planning/implementation roles have `ManageRuns` but not `ManageScope`, so an actor that cannot define scope can nevertheless launch a provider with write access to an arbitrary absolute directory.

**Impact:** The authorization model protects scope mutation in the ledger but does not protect the operational permission boundary that matters. A non-operator run manager can bypass operator-defined scope and grant a child agent access outside its work item. This remains a defect even with no hard host-filesystem enforcement: the application must at least ensure that the paths it supplies to the provider sandbox are derived from, or contained by, the governed scope.

**Recommended fix:** Add one canonical path-scope policy at the launch boundary. Load the selected work item, normalize its resource-scope entries against a trusted workspace root, require the working directory and every additional directory to be contained by those entries, and reject launches without a work item unless an explicit operator-only policy allows them. Prefer deriving provider grants from the stored scope instead of accepting free-form grants from the launching actor. Add tests proving a `ManageRuns` actor cannot launch outside scope, including sibling-prefix and symlink/reparse cases appropriate to the stated soft-enforcement model.

**Change shape:** Focused cross-component refactor; a local adapter-only patch is insufficient because authorization needs ledger state at request construction.

### Major — Dependency invalidation can be bypassed or erased by later commands

**Problem:** Dependency safety is enforced only when a claim transitions to `Rejected`/`Superseded`. `ProposeDecision` and `AddWorkItem` check only that referenced claims exist (`src/AILedger.Core/Application/CommandHandler.cs:220-221,322-323`), and `StartRun` checks only the work-item status (`src/AILedger.Core/Application/CommandHandler.cs:355-369`). Consequently, a decision or work item created after its dependency was already rejected remains usable, and the work item can be run. Separately, when an active work item is correctly changed to `Blocked` by `WorkItemInvalidated`, a later `RunCompleted` unconditionally replaces its status with `Completed` or `Paused` (`src/AILedger.Core/Domain/TaskReducer.cs:124-130`), erasing the invalidation.

**Impact:** Work based on known-invalid claims can enter execution and later appear successfully completed. The replayed projection therefore no longer reliably represents the causal safety state that the invalidation events establish.

**Recommended fix:** Centralize a small dependency-status guard and apply it when proposing/resolving decisions, adding/starting work, and any other point that makes a dependent artifact actionable. In `CompleteRun`, preserve `Blocked`/`Stale` (or model run outcome separately from work-item validity) instead of overwriting invalidation state. Add two regression sequences: reject-then-create/start, and start-then-reject-then-complete.

**Change shape:** Targeted domain-policy refactor; the reducer correction itself is local, but creation/use guards should share one helper to avoid another temporal hole.

### Major — Failed process runs return before child shutdown and stream tasks are observed

**Problem:** `SystemProcessRunner.RunAsync` starts exit, stdout, stderr, and stdin tasks, but on the first failure it only cancels the linked token and calls `Process.Kill`; it then immediately rethrows (`src/AILedger.Providers/Process/SystemProcessRunner.cs:32-47`). It neither waits for process exit nor awaits/observes the remaining drain/write tasks. `AgentAdapterBase` can catch that exception and return an `AgentRunResult` immediately (`src/AILedger.Providers/Adapters/AgentAdapterBase.cs:107-118`) while a stderr callback may still be appending to the same mutable `errors` list included in the result.

**Impact:** Timeout, cancellation, output-limit, and callback-failure paths can outlive the reported terminal result. That permits races while the result is serialized, unobserved task exceptions, disposal concurrent with pending stream reads, and a child/process tree that has been signaled but not confirmed terminated. These are precisely the paths where bounded lifecycle behavior is most important.

**Recommended fix:** On failure, cancel, kill the tree, then await a bounded shutdown/reap path and observe all started tasks before returning or rethrowing; preserve the original exception while collecting cleanup failures. Do not expose mutable collections until drains have stopped (snapshot them before constructing the result). Add real-process tests for timeout/cancellation and receiver failure that assert the process has exited and no callbacks occur after `RunAsync` completes.

**Change shape:** Localized refactor within `SystemProcessRunner`; adapter snapshots are a small defensive patch.

### Minor — The 30-second acquisition bound does not cover the in-process lock

**Problem:** `TaskMutationLock.AcquireAsync` waits on the per-path `SemaphoreSlim` with only the caller token, then applies the 30-second limit only while opening the file lock (`src/AILedger.Storage/TaskMutationLock.cs:11-18,28-57`). A same-process holder delayed in replay or projection repair can therefore make another operation wait indefinitely when callers use `CancellationToken.None`.

**Impact:** The advertised bounded lock acquisition behavior is not end-to-end within a long-lived host, and one stuck operation can indefinitely queue all same-task callers.

**Recommended fix:** Use the same absolute acquisition deadline for both the semaphore and file-lock phases, throwing the same timeout error when the remaining budget expires. Add a contention test that holds the process lease past a short injectable test timeout.

**Change shape:** Local patch, ideally with the timeout/deadline injected for fast deterministic tests.

