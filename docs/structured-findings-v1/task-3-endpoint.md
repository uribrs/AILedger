# Task 3: local findings MCP endpoint

The local entry point is `dotnet /absolute/build/AILedger.Cli.dll findings serve /absolute/host.json`. It uses stdio only and exposes one tool, `record_findings`, over task 2's `IFindingsRecorder.RecordAsync`. It neither launches providers nor configures their permissions. No global tool installation is required.

## Trusted composition

A **host-created** configuration looks like this; these are illustrative paths/identities, not defaults:

```json
{
  "task_workspace_root": "/absolute/repository/.ailedger/tasks",
  "task_id": "disposable-task",
  "actor_id": "researcher",
  "run_id": "R1",
  "correlation_id": "R1",
  "causation_id": null,
  "allow_record_findings": true,
  "allow_runless": false,
  "diagnostics_directory": "/absolute/host-observations/findings",
  "provider": "codex",
  "provider_session_id": null
}
```

`task_workspace_root` is the directory containing task directories, not the repository directory or `.ailedger` home. The composing host resolves it (normally `<ledger-home>/tasks`); there is no cwd-based discovery or agent-controlled override in this endpoint. Workspace/configuration/diagnostics paths must be absolute. Configuration is bounded to 16 KiB and rejects duplicate/unknown properties before typed deserialization. Grants default to false. An explicitly allowed runless host supplies a stable correlation and `allow_runless: true`.

`FindingsMcpHost.LoadAsync` creates the file service for that root. For embedding, `FindingsMcpHost` takes a trusted initial configuration, recorder, and asynchronous configuration reader through its constructor; the host owns matching the recorder to the root. `FindingsMcpServer` accepts the host, input/output streams, and diagnostic writer. Each instance represents one connection.

The file host reloads configuration for **each call**. Only the two grant flags may change on a running connection; destination, actor/task/run/correlation/causation and diagnostic provider/session identity are pinned. Missing, invalid, or changed attribution refuses access without looking up a receipt. Update grants by atomically replacing the complete configuration. Kernel capabilities are independently checked by the recorder under its existing task lease. Reconnecting with the original trusted binding can recover a receipt; changing the run/correlation/causation under an already committed key conflicts. Cross-actor receipt recovery is unsupported.

This file and the composing process are the trust boundary, not authentication based on MCP arguments. **Task 4 must provision them outside agent write authority and prevent agent-controlled endpoint launch/rebinding.** A process that can rewrite the host configuration or ledger already has filesystem authority outside this interface. Client `clientInfo`, `_meta`, tool arguments, JSON-RPC IDs and request IDs never supply host identity or grants. Provider/session fields are nullable observations supplied by the host; client claims are not measurements. No provider permission or protection mechanism is delivered by task 3.

## Protocol and parsing

The implementation follows the MCP [stdio transport](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports), [lifecycle](https://modelcontextprotocol.io/specification/2025-11-25/basic/lifecycle), and [tools contract](https://modelcontextprotocol.io/specification/2025-11-25/server/tools). It negotiates `2025-11-25` or `2025-06-18`, supports `initialize`, `notifications/initialized`, `ping`, `tools/list`, `tools/call`, and cancellation notifications. It advertises only tools. Unknown versions receive the supported latest version for client negotiation. No HTTP endpoint, JSON-RPC batch execution, resources, prompts, task extension, or arbitrary command API is provided.

Send one UTF-8 JSON-RPC message per newline. The `tools/call` params are `{"name":"record_findings","arguments":<v1 request>}` (optional MCP `_meta` is ignored for binding). A tool call must have an ID and follow initialization; notifications cannot mutate the ledger. JSON-RPC IDs are strings up to 128 characters or signed 64-bit integers; this is separate from the findings request ID. The reader bounds complete frames at 272 KiB, with 16 KiB of envelope allowance around the 256 KiB raw arguments limit. The parser also enforces the existing normalized-body limit. An oversized frame is drained through its newline; an unterminated EOF frame never executes. JSON depth is bounded to 32. Eight calls may be in flight per connection; further calls receive a protocol overload error and may retry their original findings request. Output frames are serialized, and the input pump remains available for cancellation.

`JsonDocument` retains duplicate members. The adapter inspects decoded member names and string values before constructing typed findings; it does not let ordinary deserialization discard duplicate properties. Unknown properties, wrong casing, missing/null required values, invalid UTF-8/escaped surrogates, invalid JSON/numbers, invalid keys/references and all v1 limits reject without application entry. Exact numeric representations of version 1 normalize to integer 1; numbers are not rounded into a supported version. Existing prose/domain trimming and fingerprint semantics remain owned by task 2.

A recognized call returns MCP `structuredContent` equal to the frozen v1 success/error envelope, with an equivalent serialized JSON text content block and `isError`. No `CollectionStatus`, alternative null branch or C# property names leak into that envelope. Discovery embeds the frozen request/response schema resources from their original files; the advertised output schema adds only the redundant root `type: object` required by MCP (both frozen branches already require objects). The frozen files are unchanged. JSON escaping/formatting between structured and text content can differ; their decoded JSON values match.

Malformed framing/JSON-RPC envelopes, initialization errors, unknown methods/tools and overload use JSON-RPC errors because they are protocol failures rather than a completed tool operation. Every such rejection is observed separately. Valid tool arguments rejected by the parser use the v1 `invalid_request`/`invalid_reference` error. A pre-admission denial or malformed body cannot establish whether an earlier key use committed, so its commit state stays `unknown`. Reconcile a prior unknown result with its original body/key/binding before correcting content. `outcome_unknown` requires exactly the same request and trusted binding; `idempotency_conflict` means the earlier use committed and the new content did not.

EOF drains admitted calls. Cancellation is forwarded; failure/cancellation after append does not roll back the commit. A response write/flush failure closes the connection and retains the observed application outcome in diagnostics. The server has no receipt cache; reconnect and call the recorder again to recover.

## Transport observations

Each connection writes a separate `findings-transport-<session-id>.jsonl` in the host-selected diagnostics directory. Separate files avoid cross-process append contention without taking the canonical task lock. Writes are serialized within a connection and best-effort with a 250 ms cancellation budget. On collection failure, a small structured stderr row identifies the transport attempt and `collection_status: unavailable`; response and canonical truth are unchanged. Host startup failure emits structured stderr with `startup_failed`, `stderr_only`, exception type, and unknown commit state, since no trustworthy file destination exists yet. No diagnostics use stdout.

Rows contain transport attempt/session IDs, message kind, a valid unambiguous request ID when available, trusted attribution, actual nullable provider/session fields, adapter build identity, start/end and monotonic elapsed time, application entry/elapsed interval and attempt ID, application collection status, outcome/error boundary/code/commit state, returned receipt fingerprint/transaction/event joins, replay status, and response delivery. They contain no finding/evidence prose, exception message, raw request, or duplicated provider usage. An application exception has no fabricated application attempt ID. A conflict or uncertain result without a returned receipt joins via the application attempt instead of disclosing/inventing receipt fields.

`message_kind` separates `tool_call`, `protocol`, `connection`, and frames whose method could not be classified. A normal tool interval starts after the complete frame is received and includes parsing, binding, application and output; it excludes waiting for incoming bytes and the terminal journal write. Connection I/O failures have no invented elapsed interval. `application_ms` is nested inside transport duration, not additive. `written` means a local write and flush succeeded, **not** that the client acknowledged receipt. `failed` can coexist with `commit_state: committed`. Missing rows and truncated/unwritable telemetry remain collection gaps; a crash can omit a terminal row. The canonical receipt is the commit evidence. Existing application journals, refusal journals, run correlation and cost collection remain unchanged.

## Scope and compatibility

Task 2 or newer general storage binaries are required for findings-enabled logs; task-1 storage rejects singleton findings groups despite typed readers tolerating the additive metadata. Process/crash retry recovery is not a power-loss or distributed-durability proof. This delivery has local subprocess and deterministic stream/storage-fault coverage; no live Claude/Codex adoption, complete MCP SDK interoperability matrix, measurement speedup, workflow retirement, or telemetry report integration is claimed. See [validation](task-3-validation.md) and the [task-4 handoff](../handoffs/structured-findings-task-4.md).
