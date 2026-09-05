# Independent Code Review

## Calibration

- Change type: infrastructure/orchestration with shared persistence, authorization, and provider-process libraries.
- Risk level: high. The implementation controls durable state, actor capabilities, filesystem scope, child-process lifetime, and provider protocol results.
- Verification run: `dotnet test AILedger.sln --no-restore --verbosity minimal` passed all 79 tests.

## Findings

### Major — The governance boundary is writable and identities are self-asserted

**Problem:** Every CLI mutation accepts `--actor` as an arbitrary string (`CliApplication.cs:474`), and authorization only looks that string up in task state (`AuthorizationPolicy.cs:7-30`). There is no binding between an actor ID and an authenticated caller. In addition, the default ledger root is `.ailedger/tasks` below the current directory (`CliApplication.cs:87`), while provider work commonly grants that same directory tree. Nothing prevents a work scope from containing the authoritative ledger. A launched provider with write access can therefore invoke the CLI while claiming the operator ID or directly replace `events.jsonl` with a syntactically valid forged history.

**Impact:** The role/capability model is not an enforceable security boundary against a provider or any other process running as the same OS user. Such a process can grant itself capabilities, alter scope, forge decisions, or mark runs complete while replay and sequence validation still succeed.

**Recommended fix:** Keep authoritative ledger storage outside every provider-granted directory and mediate mutations through a trusted broker that binds the caller to an actor identity. If this is intentionally a cooperative, single-principal audit tool, explicitly document that limitation and avoid presenting actor authorization as protection from local/provider processes. Tamper evidence (for example a keyed hash/signature chain anchored outside the workspace) is also needed if direct filesystem modification is in the threat model.

**Change scope:** Architectural refactor if authorization is intended to be a security boundary; documentation/configuration hardening only if the tool is explicitly cooperative.

### Major — Run ownership is enforced only by one CLI path and can be bypassed

**Problem:** `ResolveProviderGrants` rejects a non-operator launching work owned by another actor (`CliApplication.cs:367-375`), but the core `StartRun` and `CompleteRun` handlers do not enforce owner or run-actor identity (`CommandHandler.cs:356-416`). The public `run start` and `run complete` CLI commands call the core directly (`CliApplication.cs:185-191`) and bypass `ResolveProviderGrants` entirely.

**Impact:** Any actor with `ManageRuns` can create an active run against somebody else's work item, blocking the owner from starting a real provider run, and can later complete another actor's run, which may mark that work item completed or paused. The protection depends on which facade the caller happens to use rather than on the aggregate invariant.

**Recommended fix:** Enforce ownership in `CommandHandler`: only the work owner or an operator should start a run for owned work, and only the run actor or an operator should complete it. Keep the CLI checks as early diagnostics, but treat the core rule as authoritative. Add direct core and `run start`/`run complete` tests for non-owner actors.

**Change scope:** Local patch.

### Major — Provider processes receive ambient secrets and retained output is only partially redacted

**Problem:** `CreateStartInfo` leaves the inherited `ProcessStartInfo.Environment` intact and merely overlays requested variables (`SystemProcessRunner.cs:94-115`). Thus even the CLI's nominally empty request environment (`CliApplication.cs:288-296`) passes the provider every environment variable held by the parent process. Separately, the adapter redacts requested secret values only from stderr; stdout is parsed and retained verbatim as `ProviderEvent.RawJson` and `FinalOutput` (`AgentAdapterBase.cs:72-105,120-142`). Inherited values are not even in the redaction set.

**Impact:** An autonomous provider can read unrelated CI, cloud, package, or service credentials from the host environment. If a requested or inherited secret is echoed in valid provider JSON, `provider launch` serializes it back to stdout as part of `AgentRunResult`, where terminal capture or logs can persist it. The `WorkspaceGoverned` profile constrains filesystem arguments but does not provide process-environment isolation.

**Recommended fix:** Start from an explicit environment allowlist and inject only variables required by the selected provider; classify any credential variables as secrets. Parse stdout from the original line, but retain/return a redacted copy of raw JSON and final text. Add a real-process environment test and protocol tests that echo a known secret on stdout and stderr.

**Change scope:** Cross-cutting local patch in the process runner and adapter result-retention path.

### Major — A provider error can be masked by a later successful terminal event

**Problem:** Completion selects only `events.LastOrDefault(item => item.IsTerminal)` and evaluates that one event (`AgentAdapterBase.cs:120-125,198-231`). A valid stream such as `thread.started`, `error`, `turn.completed` exits zero and is classified as `Completed`, even though the parser itself marks `error` as terminal and erroneous (`ProviderProtocol.cs:12-16`). The same issue applies to multiple Claude `result` records where an earlier result is an error.

**Impact:** Failed or protocol-corrupt provider execution can be recorded as successful, and the reducer then marks its work item completed. This is a durable false-success state, not just a diagnostic defect.

**Recommended fix:** Fail if any parsed event is marked `IsError`, and validate the protocol's terminal cardinality/order instead of silently choosing the last terminal. Add tests for error-then-success, success-then-success, and valid events after a terminal record.

**Change scope:** Local patch.

### Major — Event persistence has superlinear cost and holds the global task lock throughout

**Problem:** Every mutation replays the complete log (`FileGovernedTaskService.cs:43-48,116-134`) and then creates a replacement file by copying the complete old log before appending (`FileGovernedTaskService.cs:175-219`). Cumulative I/O across `n` mutations is therefore O(n²). Replay also applies events by cloning the affected full dictionary on every entity update (`TaskReducer.cs:159-169`), so a task with many same-kind entities incurs another O(n²) allocation path per full replay. All of this runs while holding the per-task cross-process writer lease, whose acquisition timeout is fixed at 30 seconds.

**Impact:** Long-lived tasks suffer rapidly increasing latency and write amplification; concurrent commands eventually fail lock acquisition even when the system is healthy. Dependency invalidation magnifies this because one command can emit many events, each cloning another full dictionary.

**Recommended fix:** Use a genuine append protocol with recovery framing/checksums, and load a validated snapshot plus only the event tail. Use immutable persistent maps or a mutable replay builder so replay updates do not copy the full aggregate collection per event. If the utility deliberately supports only small ledgers, enforce and document a maximum event/entity count instead.

**Change scope:** Persistence/replay refactor; an explicit bounded-ledger limit is the smaller safe alternative.

### Major — Failed termination can skip cleanup and leave a provider running

**Problem:** The exception path calls `TryKill` before `ReapAndObserveAsync` (`SystemProcessRunner.cs:45-50`), but `TryKill` catches only `InvalidOperationException` (`SystemProcessRunner.cs:201-212`). Expected process-control failures such as `Win32Exception` can escape, replace the original timeout/cancellation/protocol exception, and skip reaping entirely. Even when kill returns, `ReapAndObserveAsync` swallows its five-second wait timeout without verifying that the child actually exited (`SystemProcessRunner.cs:55-80`).

**Impact:** A timed-out or rejected agent may continue modifying files after the caller believes the run was stopped, while the recorded error describes cleanup rather than the initiating failure. That undermines both timeout enforcement and workspace safety.

**Recommended fix:** Put kill/reap in a cleanup path that always runs and preserves the original exception. Capture expected kill failures separately, verify `HasExited` after the bounded reap, and surface an explicit cleanup failure if the process tree remains alive. Add an injectable process abstraction or platform-focused tests for kill failure and reap timeout.

**Change scope:** Local process-runner refactor.

### Minor — Unknown CLI options are silently accepted

**Problem:** `CommandLine` records every `--name` but no dispatch path validates which options were consumed (`CommandLine.cs:11-50`). A typo such as `--timout-seconds` silently falls back to the 30-minute default; more seriously, an operator typo in `--work` turns a scoped provider launch into the explicitly unscoped operator path (`CliApplication.cs:348-360`).

**Impact:** Safety-relevant intent can be silently changed rather than rejected, making operator mistakes difficult to notice.

**Recommended fix:** Define the allowed and required option set per command and reject unconsumed/unknown options before executing any mutation or provider launch.

**Change scope:** Local parser/dispatch patch.

## Overall assessment

The implementation is readable, uses cancellation and async disposal thoughtfully, and has useful recovery/concurrency tests. It should not be treated as a hardened governance or long-lived orchestration boundary until the authority, process-isolation, protocol-result, and persistence findings above are resolved or explicitly constrained.
