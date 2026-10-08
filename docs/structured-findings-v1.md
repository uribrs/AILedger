# Task 1: structured findings contract and storage design

Status: implementation-ready design, 2026-09-28. Baseline source: `b72437ad3a91a8dea0503d0010582ea676533566`. This document settles task 1 of [backlog 63](../Backlog/structured-agent-interface.md). No production endpoint is implemented. Development of this effort is outside the kernel workflow by the user's explicit instruction; existing domain rules remain the backend under test.

## Delivery boundary

Task 2 delivers an application operation that records findings and evidence atomically and returns a durable, retry-safe receipt. It includes contracts, storage integration, failure-path telemetry, and behavioral tests. Tasks 3 and 4 supply the MCP adapter and provider/session configuration. Task 5 verifies measurement continuity across those boundaries. No generic command-batch API, claim resolution, role changes, new stages, YAML parser, or production-ledger migration belongs in task 2.

The accompanying [request schema](structured-findings-v1/request.schema.json), [response schema](structured-findings-v1/response.schema.json), and examples specify the wire shape. They describe a future interface, not an already registered tool. The typed application contract has the same semantics without depending on MCP or a JSON Schema runtime library.

## Request and binding

`record_findings` accepts `schema_version: 1`, `request_id`, `findings`, and `evidence`. Both arrays are required and at least one item across them is required. Claims-only and evidence-only submissions are valid. Findings contain a request-local `key`, `statement`, optional nullable `consequence_if_wrong`, and optional nullable `from_lesson`. The last maps to today's `AddClaimCommand.FromLesson` and retains its recalled-lesson prerequisite. All findings create open claims.

Evidence contains `key`, `source_type`, `citation`, `summary`, and required `supports`/`refutes` arrays. Each reference is exactly one of `{ "finding": "f1" }` or `{ "claim_id": "C20" }`. Existing IDs refer only to the bound task. Empty direction arrays are allowed, as in today's evidence API. Local keys are unique across both arrays. A duplicate resolved target in one direction or a target in both directions is rejected by existing evidence validation; a local and an existing spelling cannot evade that check.

Reject unknown or duplicate JSON properties, wrong casing, null required fields, invalid Unicode scalar sequences, and non-JSON values at the wire boundary. Task 2's typed boundary enforces equivalent shape/limit rules for in-process callers; it does not require writing the transport parser. Task 3 must reject duplicate properties before deserialization. JSON escaping and member order do not change the request's meaning. Text is never interpolated into a command or interpreted as an instruction.

Initial limits are explicit engineering defaults, not measured optimal batch sizes: 256 KiB UTF-8 wire body; 32 findings; 64 evidence items; 64 references in either direction per evidence item; 1,024 references across the request. Keys are 1–64 ASCII letters/digits/underscore/hyphen, starting with a letter. Request IDs are 1–128 ASCII letters/digits/dot/underscore/colon/hyphen. Prose limits are in Unicode scalar values: statement/consequence 8,192, source type 128, citation 16,384, summary 32,768, existing claim/lesson ID 256. Non-null prose/IDs must be nonblank. After parsing, the typed request's compact normalized JSON must also fit 256 KiB, so in-process callers cannot bypass the cap. Storage's total event/byte caps still apply, including receipt metadata.

No actor, task, run, grant, timestamp, durable ID, or claim status is accepted from the request body. A host-created binding supplies ledger location, task ID, actor ID, optional run ID, stable correlation ID, optional existing causation event ID, and permission to use this application operation. It is an internal trusted input, not a serialized agent argument. Task 2 validates that any bound run exists and belongs to the actor, and that correlation equals that run's ID when a run is present. A runless caller must be explicitly configured by the host and supply a stable correlation. Do not add a new active-run prerequisite to claim/evidence recording; the present command rules remain authoritative. Provider session identity is obtained from the run/host, never invented from a request ID.

The host grant limits which operation may be invoked; the kernel independently authorizes every generated command against current roles/capabilities. A tool binding cannot confer `AddClaim`, `AddEvidence`, or `ResolveClaim`. The application service is not a sandbox against another process already able to edit ledger files.

## Text fidelity and equivalence

Existing [ClaimRules](../src/AILedger.Core/Claims/Logic/ClaimRules.cs) and [EvidenceRules](../src/AILedger.Core/Evidence/Logic/EvidenceRules.cs) trim surrounding whitespace. Keep that behavior. Quotes, dollar signs, backticks, pipes, internal newlines, and internal whitespace survive unchanged; there is no shell layer. Do not promise byte-for-byte preservation of surrounding whitespace in canonical event prose.

Idempotency compares the decoded request before that domain trimming. Omitted optional fields and explicit null normalize identically; property order and equivalent JSON escapes normalize identically. Array order, local keys, text whitespace, line endings inside strings, Unicode composition, and reference spelling remain significant. A differently spelled but domain-equivalent request is a conflict under a used key; callers must retain the original request for retries.

Define fingerprint algorithm `findings-v1-c14n1`: build a fixed-order object with `operation`, `schema_version`, `binding`, `findings`, `evidence`; `operation` is `record_findings`. Binding fields, in order, are `task_id`, `actor_id`, `run_id`, `correlation_id`, `causation_id`, with absent optional values explicitly null. Finding fields, in order, are `key`, `statement`, `consequence_if_wrong`, `from_lesson`; evidence fields are `key`, `source_type`, `citation`, `summary`, `supports`, `refutes`. Emit each reference with its sole discriminating property. Use compact `Utf8JsonWriter` with `JavaScriptEncoder.UnsafeRelaxedJsonEscaping`, no BOM or final newline, no Unicode normalization, and SHA-256 lowercase hex. These bytes are for hashing, never HTML rendering. The request ID is the lookup key and is excluded from the fingerprint. Commit time, current ledger version, attempt identity, and transport session are also excluded. Add golden canonicalization vectors in task 2; do not depend on dictionary enumeration or DTO serializer defaults.

## IDs, receipt, and retry ownership

Scope the idempotency key by ledger instance/location, task, actor, operation, and request ID. Schema version is in the fingerprint, not a new key namespace. Binding run/correlation/causation is also fingerprinted, so reusing a key from another run conflicts rather than silently duplicating a submission. Identical request IDs used independently by different actors or tasks are unrelated. There is no TTL: retain receipts with the canonical task log. A relocated copy of that log retains its retry history.

Under the task mutation lock, assign transaction ID `Guid.NewGuid().ToString("N")` and durable IDs `CF_<guid-N>` / `EF_<guid-N>`. Check generated IDs against both the candidate claims and evidence collections and each other; regenerate on collision. No global allocator or caller ID negotiation is needed. Keep current event IDs and sequence/version rules unchanged. Order generated commands as all findings in request order, then all evidence in request order. Use the actual actor, unchanged run correlation, and trusted causation on each command; do not invent causal links between independent findings.

Success has `schema_version`, a per-invocation `attempt_id`, `status: "committed"`, `replayed`, and an immutable `receipt`. The receipt includes request and transaction identity, task/actor/run attribution, fingerprint and algorithm, commit timestamp, original resulting ledger version, ordered event IDs, and local-key-to-durable-ID/event mappings. Examples and the response schema fix field names. This operation emits exactly one existing claim/evidence event per item today; task 2 must assert this expectation rather than drop extra events if the handler changes.

A repeated success changes only `attempt_id` and `replayed`. The receipt's version is the original commit version, not the task's present version. A replay never allocates new IDs, emits domain events, or increments provider usage. It may emit a new observational attempt record.

Re-authenticate the host binding on every invocation. Before returning a stored receipt, check the current actor's required `AddClaim`/`AddEvidence` authority through `AuthorizationPolicy.Authorize` using the original operation shape; do not rerun mutation-stage or reference rules. Thus a later archived stage alone does not invalidate an already committed receipt, while revoked authority blocks access without undoing the commit. A caller with a revoked grant gets `authorization_denied`; a current kernel capability refusal is `kernel_refused`. Do not disclose another actor's receipt. Recovery through a new session requires the same trusted attribution binding; a fresh run with different attribution cannot simply reuse the key. Broader cross-actor receipt retrieval is outside this delivery.

## One canonical commit

Relevant existing seams are [FileGovernedTaskService](../src/AILedger.Storage/FileGovernedTaskService.cs), [CommandHandler](../src/AILedger.Core/Application/CommandHandler.cs), and [persistence contracts](../src/AILedger.Core/Contracts/PersistenceContracts.cs). Today `ExecuteAsync` takes the cross-process task lock, replays, validates a command, checks envelope/limits, appends, then best-effort repairs projections. `ReadEventsAsync` buffers marked groups; an unterminated line or incomplete final group is invisible. `AppendEventsAsync` removes that uncommitted tail, writes with `WriteThrough`, and flushes to disk. Version is the committed event count.

Use that transaction boundary for the whole findings request. Do not implement a loop over public `ExecuteAsync`: that would commit a prefix and release/reacquire the lock between items.

1. Validate typed request/binding and compute the fingerprint without acquiring mutation authority from payload data.
2. Acquire the same task lock used by CLI writes. Replay committed groups and read any findings receipt metadata through one storage-owned parser. A missing task is a `task_not_found` result, not an implicit task opening.
3. Look up the actor/operation/request key. If found, apply receipt-access authorization, compare fingerprints, and return the original receipt or an idempotency conflict. Do this before current event/byte limit checks and mutation validation, so a full ledger can still answer retries.
4. For a new request, allocate IDs and build commands. Call the existing handler on successively updated **candidate** state, using a single UTC operation timestamp. Validate every intermediate outcome/envelope and the aggregate version/count. No events are persisted until all commands pass. Current domain refusals win; evidence does not resolve claims.
5. Build the receipt from those actual outcomes. Append the full event group and receipt metadata in one write/flush boundary, with total task/event/byte limits enforced. Preserve the old append behavior for ordinary CLI commands.
6. Repair projections best-effort and return the committed receipt. Projection failure must not turn a committed request into failure. On retry, reconstruct the receipt from the committed log, regardless of missing/stale projections.

### Where the receipt lives

Choose additive storage-envelope metadata named `_ailedgerFindingsReceipt` on the **first event line** of the group. Its object is the immutable receipt specified by the response schema. Use existing `_ailedgerCommandEventIndex` and `_ailedgerCommandEventCount` markers on **every findings-group line, including a one-event group**. Receipt metadata has no separate event, no domain state transition, and no effect on event counts or causal ordering.

A receipt is visible only after the complete newline-terminated group is visible. The new storage parser must validate supported receipt version/algorithm, location at index zero, task/actor/run correlation, event IDs, maps, event types, final version, timestamp, and fingerprint format against that group's actual events. Duplicate receipt keys in committed history, inconsistent metadata, or unknown metadata versions fail closed for the findings operation. New reads must not treat malformed metadata as a cache miss and recommit. Unmarked legacy events and old marked command groups without this metadata remain valid. No new replay-time domain prerequisite is added.

Existing typed `LedgerEvent` readers ignore unknown outer JSON fields; the domain model and historical telemetry consumers can remain unchanged. Storage needs a raw envelope/group reader that retains metadata, shared by replay and receipt lookup, so it cannot observe a torn prefix differently on each path. A rebuildable in-memory receipt index is optional; a separately committed `receipts.jsonl` or `state.json` is not the source of truth. No receipt-only JSONL line may enter `events.jsonl`.

This gives process-crash recovery under the existing local-filesystem guarantees, not a new claim of power-loss or distributed durability. If append/flush throws after writing might have begun, report an unknown outcome and retry with the same request. A complete group found on retry is re-flushed under the lock before acknowledging it as durable, covering the case where a previous flush failed after all bytes became visible. If that flush fails, keep the outcome unknown. Do not claim rollback merely because cancellation was requested. A torn tail stays invisible and is removed by the existing recovery path before a new append. Interior corruption is an error, not a tail to discard.

## Errors

Errors have `status: "error"`, `attempt_id`, and `error` with `code`, `message`, `boundary`, `commit_state`, `retry`, nullable `item_path`, and nullable `rule_id`. `commit_state` is `not_committed`, `committed`, or `unknown`; inability to check a protected receipt is `unknown`, never a claim that it does not exist. An authorized conflict proves an earlier key use committed but the new content did not; use `committed` and explain that distinction. No failure receipt is persisted as a successful idempotency result.

Wire validation rejected before application entry returns `boundary: "request"`,
`commit_state: "not_committed"` and `retry: "after_correction"`, with a field path where known.
Unknown-field diagnostics name allowed schema fields without echoing field values. This describes
the rejected attempt only; it does not reconcile an earlier unknown attempt under the same key.
The same pre-admission distinction applies to alternatives, claim dispositions and artifact
submission. Binding failures and uncertain application outcomes retain their existing semantics.

| Code | Boundary and behavior |
|---|---|
| `invalid_request` | Request shape, duplicates, nonblank fields, keys, or limits; correct a rejected request. First reconcile any earlier unknown attempt with its original body/key before submitting revised content under a new key |
| `invalid_reference` | Unknown request-local key; existing claim/lesson validity remains a kernel check |
| `authorization_denied` | Host binding/grant rejected; no automatic retry, no receipt disclosure |
| `kernel_refused` | Existing authorization/domain rule rejects a generated command; preserve its original message and item path |
| `idempotency_conflict` | Authorized lookup found different content/attribution under the same key; never overwrite or auto-generate a replacement key |
| `task_not_found` | Bound task does not exist; no task creation |
| `capacity_exceeded` | Existing event/byte budget would be exceeded before append; includes metadata size |
| `storage_unavailable` | Known pre-append transient I/O/lock failure; retry the same request; if lookup never completed, original commit state is unknown |
| `outcome_unknown` | Write/flush/cancellation could have happened after append began; retry exactly the same request and trusted binding |
| `history_corrupt` | Interior event corruption or inconsistent committed receipt metadata; stop and diagnose, no automatic mutation |

`retry` is one of `same_request`, `after_correction`, `none`. It is a recovery instruction, not permission to relax a rule. Current `GovernanceException` carries only prose: keep `rule_id: null` and do not derive stable identities or authority categories by parsing its message. Kernel command-time refusal counts refer to the failed generated command, not to every prior candidate command that would have succeeded.

## Measurement capture and failure behavior

The complete [measurement inventory and baseline](structured-findings-v1/measurement-baseline.md) identifies owners and existing tests. Task 2 adds application-attempt capture; tasks 3/4 add transport and provider capture. Do not replace `LedgerEvent.CorrelationId` with a transaction/request ID. Use the receipt's event IDs to join a batch to its original run, and keep attempt IDs independent of the durable idempotency key.

Keep service refusals in `RefusalJournal` at the existing service boundary with the failing command name, actor, original committed task version, original message, and kernel build identity. Journal a failed batch once, not once per earlier candidate command. Before-append shape errors are application observations; provider permission/hook denials are separate boundaries. Do not widen the journal's domain authority or redesign rule identity in this task.

Add a narrowly scoped, best-effort application attempt journal beside task telemetry (proposed `findings-attempts.jsonl`) containing schema version, attempt ID, request ID/fingerprint when available, task/actor/run, kernel identity, UTC start/end, monotonic duration, lock-wait/validation/append durations when observed, outcome/code, commit-state, transaction/event IDs when known, replay flag, and collection status. It contains no duplicated evidence prose. Use a bounded write under the existing lease, avoiding a second acquisition of the same task lock. Pre-lock failures may report telemetry unavailable rather than create a second blocking path. The adapter later captures requests that never reach the service.

Telemetry is never the commit receipt and cannot make a canonical write succeed/fail. A crash can omit a terminal observation: readers must preserve that gap instead of counting absence as success. Capture accepted/rejected/replayed outcomes; task 5 joins attempts to canonical commits and reports collection completeness. The durable receipt is the evidence of a commit even when observational timing was lost. Transport timing and application timing are nested intervals, not quantities to sum into elapsed task time. Provider cost belongs to provider runs and must not be multiplied by event count or retries.

## Smallest justified implementation split

- `src/AILedger.Core/Findings/Contracts/`: request/result/binding contracts and the narrow `IFindingsRecorder.RecordAsync(binding, request, cancellationToken)` interface. Follow existing namespaces rather than inventing a dependency-injection framework. Leave `IGovernedTaskService.ExecuteAsync` source-compatible.
- `src/AILedger.Core/Findings/`: local shape validation, canonical fingerprint writer, and command translation/candidate assembly as small helpers. Existing handlers continue to own domain rules. No general transaction planner.
- `src/AILedger.Storage/Findings/`: receipt envelope parsing/validation, serialization, and bounded attempt telemetry helpers. Implement the narrow interface on `FileGovernedTaskService` so it owns its existing lock, replay, append and projection behavior. A partial class or small internal helper extraction is acceptable where it keeps methods focused; no duplicate independent storage engine.
- `tests/AILedger.Tests/Findings/` plus focused existing storage/telemetry tests: contract equivalence, authority, mixed writers, actual serialized logs, and failure injection. Reuse existing constructor seams; add only a narrow internal append/flush fault seam if needed. No provider production code or MCP package is needed for task 2.

## Task 2 acceptance matrix

| Case | Required observation |
|---|---|
| Findings/evidence with local and existing references | Whole batch committed, exact maps, open claims, unchanged direction/provenance semantics |
| Claims only; evidence only; optional recalled lesson | Supported with existing command authority and lesson rules |
| Duplicate keys/refs, opposite directions, nonexistent refs, missing capability | No canonical batch prefix; actionable failure; appropriate single refusal observation |
| Quotes, shell metacharacters, multiline and Unicode text | Internal text intact, existing surrounding trim behavior documented |
| Same key and equivalent JSON; changed text/order/binding | Same committed receipt for the first; conflict for changed content |
| Current task advances, archives, fills its cap, or actor authority is revoked after commit | Original receipt unaffected by state advancement; current authorization still enforced; no new events |
| Concurrent same-key requests and competing different requests/CLI writes | Single commit for identical key; serialized valid outcomes and no ID/sequence loss for others |
| Rejection of the last command | Earlier candidate claims/evidence absent from canonical state |
| Cancellation before append, mid-line tear, complete-prefix tear, complete write before failed flush, response loss | Known-before-write no commit; torn group invisible; complete uncertain group recovered and durably acknowledged; no duplicates |
| Projection/telemetry failure after successful flush | Committed receipt remains recoverable; report collection gaps rather than rewrite domain truth |
| Historical unmarked/marked groups and mixed old CLI/new findings groups | Replay unchanged; version equals committed domain-event count; metadata does not enter reports as extra activity |
| Receipt metadata tampered, moved, duplicated, or unsupported | Findings operation fails closed; never silently falls back to a new commit |
| Canonical log versus state/index cache | Deleting derived files cannot destroy receipt recovery |
| First-write and coordinator attribution | Run correlation preserved; retries neither add provider usage nor become coordinator-authored events |

Run the frozen report probe in verification mode and the appropriate existing storage/measurement tests. Run `dotnet test` before declaring task 2 complete. Full old-binary deployment compatibility is not implied by typed unknown-field tolerance: exercise supported old-reader behavior and document the oldest supported writer/reader. Never run crash probes against historical task directories.
