# Code Review

Risk calibration: **High** — infrastructure/orchestration code with append-only persistence, cross-process concurrency, authorization, and child-process lifecycle concerns.

## Findings

### Major — A torn final append can permanently brick the authoritative event log

**Problem:** `FileGovernedTaskService.AppendEventsAsync` opens the authoritative log directly in append mode and writes the whole command payload to it with a cancellable `WriteAsync` (`src/AILedger.Storage/FileGovernedTaskService.cs:177-195`). A cancellation, process crash, disk-full condition, or short/torn filesystem write can therefore leave a partial JSON line at EOF. Replay treats every malformed line as fatal (`:142-163`) and has no narrowly scoped torn-tail recovery mechanism.

**Impact:** One interrupted mutation can make all subsequent reads and writes for the task fail. This is especially dangerous because a multi-event command is serialized into one buffer but is not committed atomically as a file operation; the event log is the sole authority, so neither `state.json` nor projections can recover it.

**Recommended fix:** Introduce an explicit append commit protocol. At minimum, write and durably flush a framed batch to a temporary/journal file, then append under the exclusive lease using a recoverable batch boundary/checksum; on replay, distinguish and safely truncate only an incomplete final batch while continuing to reject corruption in committed history. Add fault-injection tests for cancellation/short writes at every byte boundary of single- and multi-event commands.

**Scope:** Requires a focused persistence refactor; a local exception-handling patch is insufficient.

### Major — Successful event commits are reported as failures, and stale projections may never heal

**Problem:** `ExecuteAsync` commits events first, then writes `state.json` and three projections (`src/AILedger.Storage/FileGovernedTaskService.cs:50-53`). Any later write failure makes the command throw even though the authoritative mutation is already committed. A retry has no correlation/idempotency check and can either add another event or fail as a duplicate. Separately, `GetStateAsync` repairs projections only when the byte-for-byte materialized state is stale (`:73-77`); if `state.json` succeeded but one projection failed or was later corrupted/deleted, reads consider the cache current and never regenerate that projection. `MarkdownTaskProjectionWriter.WriteAsync` runs its three writes concurrently (`src/AILedger.Storage/MarkdownTaskProjectionWriter.cs:24-36`), so partial projection success is an ordinary failure mode.

**Impact:** Callers cannot determine whether a failed command committed, retries are unsafe, and human-facing files can remain missing or stale while `GetStateAsync` reports healthy state. That undermines both recovery and the single authoritative mutation boundary.

**Recommended fix:** Define the event append as the commit point and make derived-state/projection repair independently retryable without converting a committed command into an ambiguous failure; alternatively add durable correlation-based deduplication and return the original outcome on retry. Validate/repair each derived file independently rather than gating all projection repair on `state.json` equality. Add a projection writer that fails after selected writes to cover every partial-success ordering.

**Scope:** Requires a small persistence-flow refactor plus idempotency/recovery tests.

### Major — Cancellation strands runs in `Active` state

**Problem:** `provider launch` persists `RunStarted` before building context or launching the process (`src/AILedger.Cli/CliApplication.cs:274-289`). Its compensation path catches failures but calls `CompleteFailedRunAsync` with the same cancellation token (`:291-312`). When cancellation caused the failure, that token is already canceled, so acquiring the task lock/replaying state immediately throws and the completion event is never written. The executable entry point also supplies `CancellationToken.None` (`src/AILedger.Cli/Program.cs:3`), so the actual CLI has no graceful Ctrl+C token path at all.

**Impact:** An interrupted CLI invocation can leave both the run and its work item active indefinitely. Subsequent starts for that work item are rejected as already active, forcing manual ledger intervention.

**Recommended fix:** Wire `Console.CancelKeyPress`/process termination into a CLI cancellation source. In the launch workflow, perform bounded cleanup with a fresh, non-canceled token after the child has been stopped, preserve the original exception if cleanup also fails, and test cancellation before context assembly, during process execution, and during completion persistence.

**Scope:** Local lifecycle patch plus tests; no architectural rewrite needed.

### Major — Claim status can be justified by unrelated evidence

**Problem:** `ResolveClaim` verifies only that supplied evidence IDs exist (`src/AILedger.Core/Application/CommandHandler.cs:127-140`). It never verifies that evidence used to validate the claim lists that claim in `Evidence.Supports`, or that evidence used to reject it lists the claim in `Evidence.Refutes`, even though those relationships are explicit parts of the data model.

**Impact:** Any existing evidence record—even one about a different claim—can validate or reject a claim. The resulting projection looks properly evidenced while the ledger contains no supporting relationship, weakening the central governance invariant and potentially triggering unjustified decision/work invalidations.

**Recommended fix:** For `Validated`, require at least one referenced evidence item whose `Supports` contains the claim; for `Rejected`, require an appropriate `Refutes` relationship (and decide explicitly what `Superseded` requires). Reject directionally unrelated evidence IDs and add negative tests.

**Scope:** Local domain-validation patch.

### Major — Provider output is accumulated without any bound

**Problem:** `AgentAdapterBase.RunAsync` stores every stdout JSON line, its raw JSON, every stderr line, and a final output string for the entire run (`src/AILedger.Providers/Adapters/AgentAdapterBase.cs:46-95, 111-125`). `SystemProcessRunner.DrainAsync` also uses unbounded `ReadLineAsync` (`src/AILedger.Providers/Process/SystemProcessRunner.cs:73-81`), so a single giant line is enough to allocate heavily. Runs default to 30 minutes, and child processes are external/untrusted from the host process's perspective.

**Impact:** A verbose or malfunctioning provider can exhaust the CLI process memory, lose the run result, and leave lifecycle state inconsistent. Retaining raw events and then serializing the complete result further amplifies peak memory.

**Recommended fix:** Stream provider events to a bounded/durable sink, retain only a capped diagnostic tail plus required terminal/session metadata, and enforce maximum line and total-output sizes. On limit violation, kill the child and return a deterministic failed/protocol-error result. Add large-output and overlong-line tests against the real process runner.

**Scope:** Focused provider/process refactor.

### Minor — History enumeration holds the mutation lock for the consumer's entire iteration

**Problem:** `GetHistoryAsync` acquires the exclusive per-task mutation lease before yielding events and retains it until the consumer disposes the async enumerator (`src/AILedger.Storage/FileGovernedTaskService.cs:92-105`). Consumer work, slow output, abandoned enumerators, or a callback that tries to mutate the same task can block writers indefinitely; the last case can self-deadlock.

**Impact:** A read-only history operation can cause avoidable write starvation or deadlock. This is not merely theoretical for library consumers because yielding transfers control while the exclusive lease remains held.

**Recommended fix:** Read/validate a stable snapshot under the lease and release it before yielding, or implement a short-lived snapshot/copy boundary. Bound memory if histories can be large (for example, copy the file or capture a committed byte length and stream that snapshot). Add a test that pauses enumeration and confirms a mutation can still make progress according to the chosen consistency semantics.

**Scope:** Focused storage refactor.

### Minor — Replay does not validate event ordering or identity continuity

**Problem:** Replay verifies schema and task identity but not that deterministic event IDs are unique/sequential or that each event's causation link matches the preceding event (`src/AILedger.Core/Domain/TaskReducer.cs:34-55`; `src/AILedger.Storage/FileGovernedTaskService.cs:119-124`). A duplicated or reordered valid JSON line can therefore be silently accepted whenever its payload remains reducible.

**Impact:** Accidental edits, copy/paste duplication, or some classes of corruption can alter state and version while preserving syntactically valid history, reducing the audit value of the append-only ledger.

**Recommended fix:** During replay, validate expected event ID/version continuity and the intra-command causation rules that the writer emits. If batches are introduced for atomic append, validate batch sequence/checksums as part of the same change.

**Scope:** Local replay validation patch, ideally paired with the append-protocol refactor.

## Verification

`dotnet test tests/AILedger.Tests/AILedger.Tests.csproj --no-restore` passed: **47 passed, 0 failed, 0 skipped**.

The current suite covers happy-path replay, malformed complete lines, and cross-instance locking, but not torn writes, ambiguous post-commit failures, projection-only corruption, cancellation compensation, unrelated evidence, unbounded output, or event-sequence corruption.
