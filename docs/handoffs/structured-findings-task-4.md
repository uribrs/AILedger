# Handoff: task 4 only — provider adoption of structured findings

## Assignment and workspace

Continue in `/Users/user/.codex/worktrees/structured-findings-contract/AILedger`, branch `codex/structured-findings-contract`. Inspect `git status` first and preserve subsequent changes. Task 1 is `a62053c`; task 2 is `00270fb`; locate the task-3 endpoint commit with `git log` (it contains this handoff). Do not install the global tool, copy into the original checkout, merge or publish without a new instruction.

Task 4 is the provider wiring and recording-guidance task in [backlog 63](../../Backlog/structured-agent-interface.md). Do not implement tasks 5–8, YAML, broader batch commands, role/stage changes, or workflow retirement. The user's kernel-workflow exception is local to this redesign: no `ai-kernel`, governed development task, or ledger writes to govern development. Authorization/validation inside the implementation and isolated fixtures remains authoritative. Do not change global instructions/skills or dispatch other agents unless the user changes that instruction. Follow the .NET skill and small-method/SRP conventions.

## Read in order

1. [Task-3 validation](../structured-findings-v1/task-3-validation.md), then [endpoint and trust contract](../structured-findings-v1/task-3-endpoint.md).
2. [Frozen v1 design](../structured-findings-v1.md), schemas/examples in `docs/structured-findings-v1/`, [task-2 limitations](../structured-findings-v1/task-2-validation.md), and [measurement baseline](../structured-findings-v1/measurement-baseline.md). Verify frozen hashes before production edits.
3. `src/AILedger.Cli/Findings/FindingsMcpHost.cs`, `FindingsStdioCommand.cs`, `FindingsMcpServer.cs`, `FindingsTransportAttempt.cs`; inspect the parser/response writer only if needed. The real recorder seam is unchanged: `IFindingsRecorder.RecordAsync(binding, request, token)`.
4. `tests/AILedger.Tests/Findings/FindingsMcp*.cs` and task-2 tests. `FindingsMcpFixture` shows an actual CLI subprocess handshake/call and disposable host setup.
5. Existing provider composition/permissions: `Cli/Providers/ProviderLauncher.cs`, `ProviderGrantResolver.cs`, `ProviderRunRecorder.cs`; provider adapter configuration and existing Roslyn MCP registration in `AILedger.Providers`. Read the actual grant/session paths before choosing a split; do not couple findings availability to Roslyn.

## Available integration surface

Start the built CLI with argument-list entries `findings`, `serve`, and an absolute **trusted** host-configuration path. Keep stdout exclusively MCP and drain stderr. `task_workspace_root` is the already resolved task workspace directory (typically `<ledger-home>/tasks`), not cwd. The endpoint registers only `record_findings` and exposes the frozen schemas in discovery. It supports MCP `2025-11-25` and `2025-06-18`; unsupported versions negotiate the latest supported version. Verify both real provider clients' negotiated behavior rather than assuming SDK compatibility.

The configuration fields and limits are documented in the endpoint contract. Grants default false and are reread per call. All other configuration fields are pinned for that connection, including nullable provider/session observations. Kernel role capabilities are never conferred by this host grant. `clientInfo`, `_meta`, JSON-RPC IDs and `request_id` cannot establish identity.

**The composing provider host must own/protect configuration and process launch.** Do not place writable attribution/grant configuration in agent-controlled workspace scope or let the agent choose/rewrite the endpoint command/path. Determine actual provider configuration and permission mechanisms in the repository; do not assume a file is trusted merely because it is JSON. Configure only the intended tool permission, without a global approval bypass or expanded filesystem grant. Document any provider limitation that prevents establishing this boundary.

For provider-dispatched work, bind the subject actor and actual run ID, with correlation equal to the run ID; do not substitute the dispatching operator. Supply provider/session identity only from actual host observations; unavailable stays null. Because identity is pinned, do not overwrite those fields on a live connection after discovering a session ID. Either retain honest missing observations for that connection or deliberately design a trusted observation update while preserving the immutable ledger attribution; test any change. A new session recovering a lost response must retain the original task/actor/run/correlation/causation and request body/key. A new run with changed attribution conflicts; cross-actor recovery is unsupported.

Call the operation directly through MCP, never shell-translate prose or loop over ordinary commands. There is no adapter receipt cache. Receipt/event identities, original commit time/version, kernel authorization, task-2 canonical atomicity, run ownership, and retry rules are settled contracts.

## Task-4 acceptance

- Configure the endpoint and actual tool permission in both Claude and Codex launches, independently of Roslyn navigation enablement.
- Update relevant recording guidance/manifests only where the structured endpoint is supplied. Preserve operator CLI support and other operations; do not edit global skills/instructions for this effort.
- Verify subject/run/provider/session attribution, existing launch outcome/usage/first-write/manifest observations, and ordinary evidence prose through the real tool path. Never derive provider cost or turns from batch size, event count, or retries.
- Prove unauthorized calls remain refused and host configuration/launch cannot be rebound by tool payloads or agent writes allowed by the configured provider setup.
- Exercise a real disposable episode with each provider and its actual configured grants. Include a stable-key retry and demonstrate that existing provider usage remains single-counted. Report environmental blockers honestly; do not mark a provider accepted from mocked configuration alone.
- Preserve the task-3 local suite, task-2 86 findings cases, and existing provider/storage/measurement tests. Run `dotnet test`, the repository-aware full runner, frozen hash checker and read-only report probe; never recapture expected outputs to hide a difference.

Task 3 emits per-connection transport JSONL with application attempt/receipt joins and best-effort stderr fallback; it does not integrate those files into reports (task 5). Transport/application durations nest; response delivery and commit outcome are separate. Missing/truncated observations stay missing. Return `CollectionStatus` only through diagnostics, never the frozen v1 tool response.

Keep the old-storage singleton restriction and existing timing-sensitive test visible in validation. Build outside the checkout. The repository runner needs its existing application-data fixture permissions; fix neither tests nor expectations to hide host restrictions. Finish with a scoped commit, task-4 status based on actual acceptance, validation/limitations, and a focused task-5 handoff.
