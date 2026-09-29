# Structured inspection and readiness v1 — task 10

Task 10 delivers three read tools through the application service and trusted Codex/Claude
endpoints: `inspect_task`, `retrieve_context`, and `check_readiness`. It adds no authority,
workflow advancement, generic command execution, or automatic approval. Tasks 11–14 are outside
this change. Tasks 1–6 remain accepted; tasks 7–10 require separate client observations.
The historical task-9 simulation was mechanical, not a client trial or evidence of improved judgment,
cost, or delivery time.

## Supported scope

Responses identify schema version 1. `ITaskInspector` on `FileGovernedTaskService` is the typed application API. `InspectionBinding`
is constructed by the trusted host: task, actor, run, correlation/cause, narrow endpoint grants,
and freshly loaded cognitive artifacts. None is accepted from tool arguments or retrieved prose.
Existing `BuildContext` authority and `ContextAssembler` selection/role policy govern every read.
Bound reads require the existing active run to belong to the actor. Historical assignments retain
exactly their recorded grants; no assignment is rewritten. Revocation is checked per call.

`inspect_task({"schema_version":1,"selection":"relevant","limit":20})` returns current stage,
role/capabilities, own candidate identity where bound, recorded-brief freshness, ledger version,
last event ID, observation time, and a page of record references. Each reference includes kind,
stable ID, bounded summary, truncation marker, serialized length, SHA-256, and a complete
`retrieve_context` query. The index reports its selected count and next offset. Further pages
require `expected_version`; changed ledger state produces `stale_snapshot`.

The relevant selection uses the run's work/assurance membership. `selection=task` is the explicit
path to unrelated current records, through the same role policy. A frozen reviewer cannot escape
its isolation with task selection, a guessed reference, or another actor's receipt. Excluded records
are not counted or described: revealing protected identifiers/counts would itself weaken isolation.
Historical/superseded material and challenge records omitted by the existing selector remain explicitly named CLI gaps.

`retrieve_context` accepts the returned versioned, digest-bound reference. Claims, evidence,
decisions and work items return their existing typed models using `LedgerJson` conventions;
other selected context uses `ContextArtifact`, preserving related IDs and producer links. Own
recorded brief and own run/assurance metadata are also retrievable. Run secrets and other actors'
run narratives are never returned. Cognitive rules/skills are selected through existing policy,
loaded from a pinned host cognitive root with the existing manifest hash verifier. Missing cognitive
material is explicit in brief freshness; inspection never records or refreshes a brief.

The four receipt kinds return the original typed v1 receipt only for the same actor **and run**
(or same runless binding) with current recording capability and host grant. These are observations
of complete event-log groups, not a durability re-flush. After an uncertain recording outcome,
retry the original recording body/key/binding; retrieval cannot repair or certify a failed flush.
Receipt judgments describe the original transaction, not necessarily the current claim state.
Completed-run recovery remains the existing recording retry path, since run-bound inspection
uses the assembler's active-run read boundary.

## Bounds and freshness

- Requests use strict JSON, reject duplicates/unknown attribution/waiver fields and cap at 256 KiB.
- Index pages contain 1–32 references (default 20), summaries at most 240 UTF-16 code units,
  and at most 64 KiB of reference metadata. A selected legacy ID over 256 characters produces
  an explicit unsupported-reference diagnostic with the authorized CLI path.
- Retrieval returns at most 16,384 UTF-16 code units of record JSON (default 8,192).
  A fitting record returns `data`. A larger record returns `json_chunk`, `next_offset`, total
  characters and digest. Concatenate chunks at one version/digest, verify UTF-8 SHA-256, then parse.
  Offsets never split a surrogate pair. Partial content is visibly partial, never complete evidence.
- The required `expected_sha256` also detects changed cognitive content without a ledger event.
  A digest/version mismatch requires reinspection; never combine old and new chunks.
- Replay retains the existing 10,000-event / 64-MiB defaults. Exceeding configured read capacity or
  unreadable/corrupt history is unavailable/unknown, never an empty successful context.

Versions are observations, not reservations. All mutation services continue current-state,
authorization, lifecycle, dependency and assurance validation at execution under their write lock.
No readiness result is a grant or is accepted as input that bypasses validation.

## Readiness action set

`check_readiness` takes `schema_version:1`, `action`, `expected_version` and a single typed
`proposal`. Recording proposals are the exact existing v1 tool arguments. The embedded MCP schema
spells out all seven shapes. It rejects arbitrary command strings and authority/waiver fields.

| Action | Evaluation and execution path |
|---|---|
| `record_findings` | Existing normalization, binding validation, idempotency identity and progressive claim/evidence candidate builder; execute `record_findings`. |
| `record_alternatives` | Existing references, recorded grants, exact retry identity and candidate builder; execute `record_alternatives`. |
| `submit_artifact` | Existing content digest, producer/run, lifecycle, supersession and assurance binding validator; execute `submit_artifact`. |
| `record_claim_dispositions` | Existing explicit expected status, evidence direction, rationale and progressive dependency consequences; execute `record_claim_dispositions`. |
| `prepare_work` | `{id,title,owner?,claims,scope,not_split_justification?}`; existing `AddWorkItemCommand` handler including stage, current host-observed skills, grants, dependencies and scope conflicts. Execute CLI `work add`, which captures its optional repository base ref and revalidates. No waiver or filesystem candidate assertion is implied. |
| `complete_work` | `{work_id}`; existing work completion handler checks active coordination, dependencies and independent assurance. Execute CLI `work complete`. No verification waiver. |
| `transition_stage` | `{stage,reason?,serial_justification?}`; existing transition handler, prerequisites, accepted links and lifecycle. Execute CLI `stage transition`. No prerequisite waiver or automatic transition. |

`ready` means the specified ledger admission and inspected cognitive prerequisites passed at this
snapshot; it does not predict future storage I/O or provider availability. `blocked` means a known
input/authority/state/prerequisite refusal, retaining the kernel's exact reason and failing item or
work reference. Diagnostics include action, prerequisite and supported recovery. The first decisive
refusal is returned, as in the authoritative handler; this does not promise an exhaustive blocker list.
`unsupported` names an action outside this closed set. `unknown` means the snapshot or relevant
physical candidate could not be inspected. Missing facts never produce a successful check.

Physical assurance candidates are **not inspected** by these endpoints. For assurance-bound artifact
submission or work completion, a ledger pass becomes `unknown/candidate_uninspected`, with recorded
identity and the existing assurance-host recovery path. Stale membership/provenance, missing outputs,
independence and dependencies still fail through their actual rules. Work preparation checks the
existing admission rules; its optional git base-ref capture remains an execution-host responsibility.
Provider executable availability, credentials, budget and future run results are not inspected;
`dispatch_run` and `complete_run` are unsupported rather than optimistic passes. Decision acceptance,
claim supersession, all waivers and other mutations are also unsupported.

## Non-mutation and implementation boundary

Inspection replays the existing complete-group parser and reducer under the task synchronization
lease. It does **not** call `GetStateAsync` (which repairs derived views), the CLI context builder
(which records `context.built`), receipt replay (which re-flushes), or a mutation service. It never
repairs torn tails, projections or history, appends canonical events, changes assignments, accepts a
decision, completes work, or satisfies a prerequisite. Only the existing `.writer.lock` may be
opened/created for synchronization. Dirty projections and incomplete tails remain byte-for-byte intact.

Readiness calls the **same** recording request validators, binding checks, fingerprints, candidate
builders, `AuthorizationPolicy` and `CommandHandler` used for execution. Candidate states, provisional
IDs and events are discarded and never returned. The append serializer is extracted unchanged for
both execution and prospective byte sizing, including operation receipts and dependency events.
Byte capacity conservatively includes an uncommitted tail; only an existing authorized write may
repair it. No replay rule or event model changed.

## Measurement and provider guidance

Provider instructions explain inspect → follow omissions/retrieve → reason → check a supported action
when prerequisites matter → execute the existing operation. The existing four recording contracts,
retries and default reports are preserved. The tools are individually granted in Codex/Claude;
there is no wildcard tool or shell grant. The host also handles the deterministic transport
bookkeeping: a delivered response's bounded telemetry tail cannot falsely exhaust the eight
outstanding-request slots during rapid sequential retrieval. Actual concurrent requests remain bounded.

MCP read/readiness observations go only to `inspection-transport-*.jsonl`, with operation, trusted
actor/task/run correlation, transport attempt, application-entry/timing, status and delivery state.
They are **not** mutation attempts, durable requests, transactions or events. Application-only reads
currently have no telemetry; `measurement=not_collected` says so. Provider usage remains unknown in
inspection rows and joins the existing completion once per run. Telemetry failure cannot change
readiness, and default/frozen retrospective reports do not consume this new population. Missing
observations remain missing. A lower refusal count would show changed observation boundaries, not
proof of stronger safeguards, lower cost, or better judgment.

## Interaction coverage and next ordinary task

Implementation coverage below is tested with disposable fixtures/scripted local clients. Every
**task-10 ordinary-client observation is pending**; none of these rows claims a client acceptance.

| Routine interaction | Owner/path now | Remaining scope | Client observation |
|---|---|---|---|
| Inspect relevant state, claims, evidence, decisions | Agent `inspect_task` + `retrieve_context` | Historical material outside selector remains CLI | Pending |
| Follow omitted pages/large records/unrelated current context | Agent versioned pagination/chunks/task selection | Role-excluded material stays protected | Pending |
| Read own recording receipts | Agent retrieval | Cross-run/history inspection CLI; uncertain flush uses exact recording retry | Pending |
| Discover supported predictable blockers | Agent `check_readiness` | First decisive blocker, seven named actions only | Pending |
| Record findings/evidence and allocate IDs | Agent `record_findings`; deterministic host IDs/atomic append/receipt | No implicit resolution | Tasks 1–6 accepted; task-10 interaction pending |
| Record alternatives | Agent `record_alternatives`; deterministic host IDs/receipt | Existing grants retained | Tasks 7–10 trials pending |
| Record explicit claim judgments | Agent `record_claim_dispositions` | Judgment/authority remain explicit | Tasks 9–10 trials pending |
| Submit verifier/reviewer output | Agent `submit_artifact`; deterministic host identity/content/links | Other artifact kinds/export CLI | Tasks 8–10 trials pending |
| Choose scope/owner/dependencies | Explicit operator judgment | CLI work preparation remains; readiness inspects proposal | Pending |
| Prepare work record/base ref | CLI `work add`; deterministic base-ref capture | Future narrow preparation tool requires separate scope | Pending |
| Build/refresh a governed brief | CLI context build / existing launch host | Inspection never substitutes for this event | Pending |
| Select dispatch subject/model/assurance members | Explicit authorized coordinator judgment | CLI provider launch; readiness unsupported | Pending |
| Dispatch/authenticated relay/configure provider | Existing deterministic launch host after CLI admission | No new dispatch endpoint or automatic follow-up | Pending |
| Capture run usage/results and close launched run | Existing deterministic launch host | Hand-run completion CLI; readiness unsupported | Pending |
| Complete work | Explicit authorized CLI command after readiness | No implicit completion or assurance waiver | Pending |
| Inspect physical candidate/environment | Existing assurance/coordinator process | Endpoint returns unknown; no filesystem inspection tool added | Pending |
| Propose/accept decisions | Explicit authorized CLI | Future narrow decision operation, not readiness approval | Pending |
| Stage administration | Explicit authorized CLI after transition readiness | Stage choice/waivers retained, no advancement automation | Pending |
| Raise/dispose challenges | Explicit authorized CLI | Existing selector does not project challenge records; readiness preserves resulting kernel blockers | Pending |
| Assign/revoke grants, escalation decisions | Explicit operator CLI | No automatic reassignment or implicit approval | Pending |
| Recover failed durability/storage | Existing recording retry/write recovery | Inspection observes only; never heals | Pending |

On Uri's next ordinary task, inspect actual tool calls, retrieved pages/chunks, stale/unknown results,
useful blockers, actual mutation receipts, and every residual CLI interaction (especially preparation,
brief refresh, dispatch, run/work completion and stage changes). Account for judgment retained or
missing, operator intervention and once-per-run usage separately. Installation is a technical
checkpoint; observations and acceptance stay pending until that task occurs. No billable trial is
part of this delivery.
