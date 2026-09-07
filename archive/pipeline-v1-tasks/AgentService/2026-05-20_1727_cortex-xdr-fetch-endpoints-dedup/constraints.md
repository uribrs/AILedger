# Constraints

- Fix must reside in `Source/Application/Cymulate.Agent.Application.Actions/Actions/QueryIntegration/Logic/Clients/BaseApiClients/PaloAltoNetworks/PaloAltoCortexApiBase.cs` inside `FetchEndpointsAsync`.
- Must not change the page request shape, the page size (`100`), the sort field (`last_seen`), the sort direction (`DESC`), or the `lastSeenDate < iBaseDate` early-break — those preserve the CA-48775 incremental-walk optimization.
- Must not touch `CortexXdrCollector` or `CybiBatchUploader`.
- Must not introduce any change to `CybiCleanupService` or `CybiActionManager`.
- Dedup scope is per-invocation: each call to `FetchEndpointsAsync` gets a fresh tracking set. Cross-invocation state is forbidden.
- Dedup key is `endpoint_id` (raw token value, case-insensitive). No fallback to `endpoint_name` or composite keys.
- Page accounting (`endpointsFound++` and `endpointsFound < pageSize` end-of-list break) must remain accurate against what the API returned — duplicates must still increment the counter; only the emit and `last_seen` early-break checks may be skipped.
- Naming, indentation, and access modifiers must follow `CLAUDE.md` § Coding Conventions, including PascalCase methods, explicit types, no `var`, braces on all `if`, and parameters added at the end before `CancellationToken` if the signature changes (it should not).
- Production code change must be accompanied by a regression unit test in the existing test project that covers `PaloAltoCortexApiBase` (locate it; if none exists, surface as a blocker rather than create a new test project ad hoc).
- The fix must compile under all three platform configurations (`DebugWindows`, `DebugLinux`, `DebugMac`) — no platform-specific conditional code.
- No new runtime dependencies, no new NuGet references.
- Log lines for skipped duplicates must use the existing `tryLogWithProductName` helper at `eLogLevels.Info` level. No raw `_logger.*` calls.
- Branch hygiene: do not commit. Leave the change uncommitted for the operator to stage, since the current branch is unrelated work.
