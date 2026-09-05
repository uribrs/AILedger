# Independent Code Review

## Calibration

- Change type: infrastructure/orchestration with public contracts and a local CLI.
- Risk: high. The reviewed paths own persistence, cross-process concurrency, authorization, process lifecycle, and provider session identity.
- Accepted tradeoffs considered reasonable: one mutation boundary, replayed filesystem projections, full-history atomic replacement for v0.1, synchronous direct CLI providers, and no distributed/database/background-dispatcher machinery.

## Findings

### Major — Undefined numeric enum values can be accepted and permanently persisted

**Problem:** `CliApplication.ParseEnum<T>` uses `Enum.TryParse` without `Enum.IsDefined` (`src/AILedger.Cli/CliApplication.cs:363-366`). `Enum.TryParse` accepts numeric strings for unnamed values. Several command handlers likewise assume enum-typed public-contract inputs are defined: role assignment persists `command.Role` directly (`src/AILedger.Core/Application/CommandHandler.cs:83-101`), claim resolution rejects only selected named states (`:480-495`), challenge disposition rejects only `Open` (`:291-306`), and run completion rejects only `Active`/`Pending` (`:377-399`). A direct CLI reproduction with `actor attach --role 999 --capability add-claim` returned exit code 0 and persisted `"role": 999` into state/history.

**Impact:** Invalid lifecycle and authorization state enters the authoritative event log. Subsequent code may serialize numeric statuses, take incorrect branches, or throw unrelated exceptions (for example, an undefined stage indexes `StageTransitionPolicy.AllowedTransitions` directly). Because history is authoritative, the corruption survives projection rebuilds.

**Recommended fix:** Make CLI enum parsing require `Enum.IsDefined(parsed)`, and independently validate every enum received at the core mutation boundary before producing events. Also configure persisted enum deserialization to reject integer enum values so edited/corrupt logs fail closed with a clear `InvalidDataException`. Add tests for numeric CLI values, direct API commands containing cast undefined values, and replay of numeric/undefined enum data.

**Scope:** Local validation patches; no architectural refactor required.

### Major — The public mutation boundary can grant non-operators scope-changing authority

**Problem:** The restriction that non-operator roles cannot receive `ManageRoles` or `ManageScope` exists only in the CLI helper (`src/AILedger.Cli/RoleDefaults.cs:23-31`). `CommandHandler.AssignRole` accepts and persists any capability set (`src/AILedger.Core/Application/CommandHandler.cs:83-101`). `AuthorizationPolicy` requires `ManageWork` plus `ManageScope` for `AddWorkItemCommand`, but does not require the actor's role to be `Operator` (`src/AILedger.Core/Domain/AuthorizationPolicy.cs:30-44`). Therefore a caller using the public `IGovernedTaskService`/command contracts can assign both capabilities to a worker or lead, after which that actor can create resource scopes. The CLI check is not an authoritative security boundary.

**Impact:** Library callers can bypass the intended role boundary and mutate governed resource scope while all persisted events appear authorized. This is especially risky because the contracts and service are public and the event log becomes the durable source of truth.

**Recommended fix:** Enforce privileged-capability assignment rules in `CommandHandler` or `AuthorizationPolicy`, and require operator role for scope-changing commands in addition to checking capability flags. Keep `RoleDefaults.EnsureSafe` only as early CLI feedback. Add direct core/service tests proving non-operator privileged-capability assignment and scope mutation are rejected.

**Scope:** Local authorization-policy patch; no architectural refactor required.

### Major — Provider probes concurrently mutate non-thread-safe collections

**Problem:** `SystemProcessRunner` drains stdout and stderr concurrently (`src/AILedger.Providers/Process/SystemProcessRunner.cs:34-39`). `ProbeVersionAsync` routes both callbacks into the same `List<string>` (`src/AILedger.Providers/Adapters/AgentAdapterBase.cs:18-31`), while `ProbeCapabilitiesAsync` routes both into the same `StringBuilder` (`:165-182`). Neither type supports concurrent writers. The current `ScriptedProcessRunner` invokes stdout and stderr sequentially, so provider tests cannot expose the race.

**Impact:** Real CLIs that emit warnings and help/version output on different streams can corrupt collection state, lose text, throw, or produce a nondeterministic detected version. Capability probing may sporadically reject a compatible provider before launch—the sort of bug that only appears when the logs are most “helpful.”

**Recommended fix:** Capture stdout and stderr into separate buffers and combine them after both drains finish, or serialize access through a lock/channel. For the version probe, define deterministic precedence (normally first non-empty stdout line, then stderr fallback). Add a real concurrent `IProcessRunner` test that invokes both callbacks at the same time repeatedly.

**Scope:** Local adapter patch plus targeted tests; no broader refactor required.

### Major — Failed or protocol-invalid provider runs still produce a successful CLI exit

**Problem:** `AgentAdapterBase.RunAsync` represents nonzero child exits, protocol failures, and internal timeouts as `AgentRunResult` values (`src/AILedger.Providers/Adapters/AgentAdapterBase.cs:104-139`). `CliApplication.LaunchProviderAsync` persists that terminal status and prints the result but does not signal failure (`src/AILedger.Cli/CliApplication.cs:289-313`); `RunAsync` then unconditionally returns 0 (`:87-90`). Only thrown exceptions or caller cancellation produce nonzero exits.

**Impact:** Shell scripts and parent orchestrators interpret a failed agent, malformed provider stream, or timed-out run as success and may advance the workflow despite the persisted failure. Inspecting JSON manually is not an adequate process-level failure contract for a CLI orchestration boundary.

**Recommended fix:** After durably completing the run and emitting diagnostic JSON, return a nonzero exit code for every non-`Completed` result. A small typed exception mapped to a dedicated exit code, or making dispatch return an exit code, is sufficient. Add CLI tests for `Failed`, `ProtocolError`, and adapter timeout results while asserting the run is still closed durably.

**Scope:** Small CLI control-flow change; no architectural refactor required.

### Minor — File-lock acquisition retries every `IOException` forever

**Problem:** `TaskMutationLock.AcquireFileLockAsync` treats every `IOException` as lock contention and retries every 25 ms until external cancellation (`src/AILedger.Storage/TaskMutationLock.cs:27-49`). Storage commands do not impose their own acquisition deadline. Persistent filesystem errors can therefore masquerade as a busy writer indefinitely. In addition, `Lease.DisposeAsync` releases the in-process semaphore only after `FileStream.DisposeAsync` succeeds (`:52-58`), so an exceptional dispose can strand the semaphore.

**Impact:** A damaged/unavailable filesystem or non-contention I/O failure can hang all access to a task instead of surfacing a diagnostic; a rare disposal failure can deadlock later same-process operations.

**Recommended fix:** Retry only errors positively identified as sharing/lock contention, or use a bounded retry window before surfacing the original error. Put `processLock.Release()` in a `finally` around file-lock disposal. Add fault-injection tests for persistent non-contention I/O failure and exceptional lease disposal.

**Scope:** Local concurrency utility patch; no architectural refactor required.

## Verification Performed

`dotnet test tests/AILedger.Tests/AILedger.Tests.csproj --no-restore --verbosity minimal` passed: 63 tests, 0 failures. The green suite does not cover the failure modes above.
