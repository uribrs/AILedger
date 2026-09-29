# Task 4: provider adoption and host ownership

Task 4 configures `ailedger.record_findings` in both production provider launches. The existing
application recorder, strict MCP parser and frozen response writer remain unchanged. Findings
registration does not depend on `NavigationHostAssembly`, navigation settings, Roslyn discovery or
Roslyn startup succeeding. The operator CLI, role authorization and other operations remain intact.

## Trusted composition

The task-3 file host is still available for explicitly trusted standalone operator composition.
Provider launches **do not** put its binding JSON in CODEX_HOME, a workspace, a temp file, MCP
arguments, or the environment. Both providers have writable scratch locations; treating those
files as trusted would permit rebinding.

Instead, `ProviderLauncher` creates `ProviderFindingsSession` after the real run and manifest exist,
inside the launch failure/cleanup boundary. It owns a `FindingsMcpHost` and the actual
`IFindingsRecorder` in its own process. The immutable configuration comes from the resolved task
workspace, subject actor, actual run ID, run correlation, original optional causation and selected
provider. The only host grant is record_findings; runless is false. Every call still passes through
task 2's current capability/run checks and task 3's strict parser. A service supplied to the CLI
must implement `IFindingsRecorder` to launch a findings-enabled provider; unsupported compositions
fail explicitly and retain ordinary failed-run cleanup.

The host binds a loopback-only TCP socket before starting the provider. It generates a random
256-bit connection capability, checks it in constant time, bounds admission to eight connections
with a five-second authentication deadline, and serves the unchanged MCP connection over that
stream. The listener remains owned by the launching process throughout the provider's lifetime.
No agent-editable socket path or binding file controls its identity or destination.

`dotnet <absolute-cli-assembly> findings relay <port> <capability>` is a byte relay, not a host
configuration command. Its address cannot create a host, select an actor/task, set grants, or name
a ledger. It connects only to loopback, authenticates, then forwards stdio. On stdin EOF it
half-closes the connection so admitted calls can drain; host disposal cancels and joins connections.
Stderr is diagnostic only. Reconnects to that live host retain the original binding and recover
receipts through the recorder; there is no adapter receipt cache. The standalone
`findings serve <trusted-config>` entry remains separate and unchanged.

The provider registration is a host-created process argument, not a file the agent can rewrite:

- Claude receives inline `--mcp-config`, `--strict-mcp-config`, and the exact allow entry
  `mcp__ailedger__record_findings`. Navigation, when present, shares that inline configuration.
- Codex receives a CLI `--config` override for `mcp_servers.ailedger`, `enabled_tools` containing
  only `record_findings`, `required=true`, and the per-tool `approval_mode="approve"`. The server
  default remains `prompt`. Findings configuration is absent from the writable temporary
  CODEX_HOME. Existing navigation configuration/hooks are preserved.

These mechanisms use the actual installed clients' flags and documented configuration:
[Codex MCP configuration](https://learn.chatgpt.com/docs/config-file/config-reference) and
[Claude strict MCP configuration](https://code.claude.com/docs/en/mcp). There is no global approval
bypass, extra filesystem grant, or command translation for findings.

This is a bound application channel, **not** a new OS principal or protection against arbitrary
same-user process control, debugger attachment, or direct ledger editing through pre-existing CLI/
filesystem authority. The runtime and provider host remain trusted infrastructure. The connection
capability authorizes access to one bound endpoint; it does not authenticate a model's prose and
must not be forwarded to other principals. A forged local host.json, MCP payload or different relay
arguments cannot reconfigure the existing host. Direct privileged host/ledger tampering remains
outside tasks 1–4's boundary, as it was for the file host. Disabling or damaging a relay can lose a
response, not undo a canonical commit; reconcile through the original body/key/binding.

## Attribution, lifecycle and guidance

Both providers start before an actual session observation is available to this composing host.
The connection's `provider_session_id` therefore stays **null**, including Claude's preassigned UUID
and a requested resume ID: requested identity is not an observation. Later actual session IDs remain
in the unchanged provider result/run completion. Join them by task/run; do not rewrite connection
identity after startup or invent session values. The provider name is the host's actual adapter
selection. Causation stays missing when absent and is retained when explicitly supplied.

The launcher still hashes the exact original manifest bytes, counts the same manifest artifacts,
reads usage once from the provider stream, measures the first run-correlated ledger write and
records the same completion/timeout/truncation outcomes. A receipt replay creates observations,
not new canonical events or a second provider usage record. New-run resume cannot recover an old
request under changed attribution: the existing conflict rule still applies. Recovery after the
host exits requires trusted reconstruction of the **original** binding; this task adds no general
cross-run recovery command.

Only endpoint-equipped briefings replace claim/evidence shell examples with the v1 request,
local/existing references, receipt mapping and stable-key retry guidance. This applies to ordinary
and assurance briefings. It explicitly supersedes shell recording examples in supplied skill
content without editing cognitive files or global skills. Claim disposition, artifacts,
alternatives, escalations and operator CLI support remain as before. No frozen manifest or report
was regenerated.

See [validation](task-4-validation.md), the [live probe](../../tools/FindingsProviderProbe/README.md)
and the [task-5 handoff](../handoffs/structured-findings-task-5.md).
