# Task 5: measurement continuity across record_findings

Task 5 extends the existing retrospective with an **opt-in** findings section. The default
report and pure historical reducers retain their exact serialized shape and metric definitions.
No recording, response, authorization, provider configuration or pricing logic changed.

## Use the existing report entry point

Build the CLI outside the checkout, then:

```sh
dotnet /absolute/external-build/AILedger.Cli.dll retrospective build --root /absolute/ledger/tasks --task TASK --findings
```

`--root` is the existing workspace root containing task directories, as with other CLI commands.
Production provider telemetry is discovered at `<task>/telemetry/findings-transport-*.jsonl`;
application telemetry is `<task>/findings-attempts.jsonl`. A standalone task-3 host may have used
another diagnostics directory. Select it explicitly with `--findings-telemetry /absolute/directory`
(which also enables the extension). This is an operator-selected **read source**, never a host
binding or authority-bearing configuration. The reader does not search provider homes or private
transcripts. An undiscovered historical source is missing coverage, not zero attempts.

The new storage read does not mutate or repair anything. The existing retrospective CLI still
uses `GetStateAsync`, whose established behavior may repair derived projections and take a task
lease. Use disposable copies for historical CLI comparisons. The pure frozen-baseline probe
continues to read original frozen inputs without that service. The provider-observation probe
copies the old episodes, compares reports, and hashes the originals before and after.

## Report contract

`TaskRetrospectiveReport.Findings` is omitted when not requested, even for serializers that
normally include nulls. When requested, its data describes one canonical version and observational
rows. Existing top-level metrics, scoring inputs, and `notMeasured` remain unchanged. The report uses the
existing camelCase serialization; the endpoint's frozen snake_case response is unchanged.

| Field | Meaning |
|---|---|
| `canonicalVersion` | Version of the supplied retrospective snapshot |
| `committedTransactions` / `granularEvents` | Validated canonical findings receipts and their granular event population; independent of journals and retries |
| `observedTransportAttempts` | Distinct readable transport observations, including protocol/connection observations |
| `observedToolAttempts` | Readable transport rows whose retained `message_kind` is `tool_call` |
| `observedApplicationAttempts` | Distinct readable application rows, not inferred from transport entry or events |
| `transactions` | Storage-validated immutable receipts, including request, attribution, fingerprint, transaction, event IDs and maps |
| `attempts` | Source path/line, journal kind, original observation and separate canonical/application/run/session joins |
| `runs` | One row per canonical task run; requested model separated from the original completion observation |
| `coverageGaps` | Detectable missing/unreadable/partial/conflicting sources and joins, with path/line where available |
| `notMeasured` | Explicit limitations: census completeness, missing observations, unsupported boundaries and invalid interpretations |

Counts of journal observations are **lower bounds**, even when there are no detected gaps.
The collectors have no completeness watermark; a crash or failed write can leave no row at all.
No readable rows in a population gives a nullable count (omitted by the CLI's existing null
policy), not zero. A zero tool count with readable protocol rows means zero *observed* tool rows,
not proof that no tool was called. An empty canonical findings population can legitimately be zero
when the canonical snapshot was successfully read and validated. Unavailable canonical receipts
give null totals and an explicit gap; telemetry is never promoted into commit proof.

Transport, application, and phase timings are retained as separate nested intervals. They are
never summed into task wall time or run cost. `written` is a local response write and flush, not a
client acknowledgement; `failed` may accompany `commit_state: committed`. An `unknown` attempt
stays unknown in the original observation even if the report can independently join it to a
canonical commit recovered later.

## Joins and trust

The application attempt ID is the primary cross-journal join, checked against task, actor, run,
request and any jointly observed fingerprint/transaction/event IDs. Canonical joins require the
same task/actor/run/request/fingerprint and any observed transaction/event IDs. Transport
correlation/causation must agree with the receipt. A response without receipt fields can join
through its matching application attempt. Conflicting metadata produces gaps, not silent fallback
to a convenient identity. Duplicate attempt IDs across files are excluded from counts and joins
with every source named; the reader does not choose an arbitrary copy.

The canonical reader is the task-2 `ReadEventsAsync` path, including receipt validation and atomic
group/torn-tail rules. It does not parse raw receipt-shaped JSON independently. A later snapshot
whose event IDs/version differ from the retrospective snapshot produces a retryable report gap
rather than combining versions. Normal CLI replay still fails closed on corrupt canonical history;
this extension does not turn that into a healthy task.

Run joins check run ID, actor and any observed provider. Session enrichment comes only from the
retained `RunCompleted` observation and is exposed as `joinedProviderSessionId`. It never updates
the transport observation or substitutes a requested/start/resume identity. Task-4 transport
`provider_session_id` therefore remains null. Requested model is separate from completion model;
Codex's unobserved served model and turn count stay absent. A conflicting transport session is
reported and not enriched. A positive provider `truncatedLines` adds an explicit coverage gap.

Each completion is exposed once per run. Provider tokens remain in their original independent
uncached/cache-write/cache-read/output buckets; the existing retrospective cost reducer retains
its own measured population per field. No cost is copied onto attempts or receipts, multiplied by
event/retry counts, or reconstructed by counting model events. A real zero cache-write bucket
remains zero. Completion status, actual timeout flag, provider failure reason, manifest hash/count,
first-write duration and truncation count retain their original meanings.

## Missing data and collector failures

The journal reader takes a bounded file-length snapshot and streams complete newline-terminated
rows. Missing or inaccessible files/directories, empty journals, unterminated tails, malformed
JSON/UTF-8, duplicate properties, missing required members, unsupported schema versions, wrong
task identity and invalid timings are visible gaps. Readable prefixes remain visible. Reads over
64 MiB per file or 256 KiB per row are explicitly excluded with gaps; these are report read bounds,
not changes to request/canonical storage limits. Cancellation is propagated.

Transport-reported application collection failure, missing application identity after entry,
unmatched application attempts, canonical receipts without application observations, and application
rows without a transport match all remain explicit. A direct in-process call may legitimately lack
transport, so that gap states both possible explanations. Counts do not manufacture an application
row from a transport row. Missing journals cannot undo a validated canonical receipt.

The test suite blocks journal paths with directories/files and loses response delivery after
canonical append. Both original recording and stable-key retry still succeed. Reports expose
the available attempts and one canonical transaction, including when neither journal is readable.
No dependency from recording to reporting was introduced, and no `CollectionStatus` was added to
the frozen tool response.

## Existing measurement consumers

| Consumer | Continuity |
|---|---|
| TaskRetrospective / CoordinatorMeasurement | Original counts, durations, coordinator brackets, causal joins, build partitions and per-field coverage unchanged; opt-in section only |
| RunCostReader / ProviderRunRecorder | Unmodified; one terminal usage interpretation and one completion, original first-write and manifest behavior |
| RefusalJournal / refusal report | Original failed-command boundary, actor, reason, build and recurrence semantics retained; a batch refusal is not multiplied by candidate operations |
| TaskCloseoutEvidence / assurance | Unmodified all-revision and producer/candidate joins; frozen assurance reports still match |
| Workflow retrospective / self-scoring | Rubric, evidence bindings and judgment/observation distinction unchanged; existing tests retained |
| Memory event/refusal ingestion | Existing complete canonical event and refusal ingestion retained; new observational journals are intentionally not indexed as canonical knowledge |

There is no stable cross-journal refusal ID. The report exposes application/transport error code
and boundary beside the existing refusal report, but does not invent a one-to-one refusal join by
matching prose or timestamps. Provider shell/search-hook denials are separate observations, not
findings journal rows. Exact MCP negotiation versions, relay authentication failures before entry,
client acknowledgement, user usefulness judgments and complete telemetry census remain unsupported.
These omissions are stated, not counted as successful calls or zero friction.

See [validation](task-5-validation.md) and the [task-6 handoff](../handoffs/structured-findings-task-6.md).
