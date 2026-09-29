# Handoff assurance v1 — task 13

Task 13 is the default assurance boundary for supported implementation work, using **actual externally produced,
bounded UTF-8 candidates**. It does not execute implementation. Existing governed implementation/provider
launches and their authorization, stages, completion and assurance rules remain intact. Task 12
still executes only offline read-only episodes. Neither path gains waived or invented histories.
Tasks 1–6 remain accepted. Tasks 7–13 client trials remain pending; implementation, installation
and Uri's acceptance are separate checkpoints. No billable provider was used in development.

## Default operating policy

As of 2026-09-29, task 13 is the standard candidate assurance flow; a separate opt-in is no longer
required. This supersedes earlier opt-in guidance. Client-trial status remains pending: adopting
the default does not claim trials passed.

Task 13 is the default assurance flow for supported implementation work; no separate opt-in is
needed. Before assurance dispatch, the authorized coordinator/operator arranges the trusted host
policy and store, complete declared candidate/requirement/source inputs, configured check IDs and
independent principals. Use a fresh `provider launch` with `--assurance-authority FILE` and
`--assurance-store DIR`, or the authorized external `assurance serve` entry point. Tools remain
explicitly granted by the host; a policy default neither creates authority nor supplies missing tools.

The current slice supports bounded UTF-8 input closures (32 paths and 64 KiB per area), not arbitrary
repository builds. Missing configuration or unsupported scope is an assurance gap to resolve before
claiming task-13 acceptance. Do not silently revert to a legacy flow, omit material inputs, invent
receipts or weaken isolation. Preserve governed verifier/reviewer outputs and completion gates as
well as explicit task-13 acceptance. Requirements-aware task-13 review must use an authorized
context that permits requirements; it cannot repurpose a blind code reviewer by widening its brief.
Route any incompatibility to an authorized host/profile decision and retain the unresolved gap.

Existing CLI launches without assurance flags remain a compatibility mechanism and still expose
seven tools. They do not automatically satisfy the default assurance policy. Host configuration
is explicit because identities, resource grants, candidate inputs and check authority cannot be
safely inferred from an untrusted request. No runtime grant or stage gate is weakened by this
guidance change. Task 12 remains opt-in and offline/read-only.

## Supported integration

`IAssuranceService.InvokeAsync` accepts five closed typed operations through the existing local
MCP transport. `AssuranceService` composes task 8's content identities, task 11's strict JSON
contracts, task 12's `IEpisodeStore`/`FileEpisodeStore`, cross-process lease and receipt recovery,
and the existing `IProcessRunner`. It introduces no blob database, task stage machine or ledger
replay rule. Assurance records use a domain-separated case hash in the existing episode journal.
They are not governed events or task-12 executions and do not enter historical measurements.

An implementation produced by an existing authorized executor, a person, or an already available
external workflow can be supplied directly. The host reads the declared files, captures their
exact bytes and constructs the immutable candidate and requirement identities. The implementer
name comes from operator policy and is explicitly an attribution attestation; there is **no claim
that this service observed implementation execution**. No pretend working run is required.

Two trusted entry points are supported:

* Existing `provider launch`, with explicit `--assurance-authority /absolute/policy.json` and
  `--assurance-store /absolute/store`. The parent owns the service and authenticated relay.
  Actor/run/provider come from the real launch, never tool payloads. Every operation rechecks the
  existing task-10 inspector's live run/BuildContext admission and the current compatible role:
  CodeReviewer→review, Verifier→verification, Operator→acceptance, and
  Operator/PlanningLead/ImplementationLead→synthesis. Assurance requires a fresh provider launch;
  provider resume is refused for this assurance path. Existing launches without the options still
  expose exactly their original seven tools. This does not waive existing dispatch prerequisites.
* `ailedger assurance serve --authority FILE --store DIR --principal ID --session ID` is a local
  operator-configured MCP entry point for independent external clients or a human's local tool
  session. Startup arguments are trusted operator configuration, not agent arguments. The operator
  must authenticate the external recipient and keep the policy/store outside its write authority.
  This is not a remote authentication service or an authorization inferred from a role name.

Codex and Claude adapters advertise and grant **only the configured principal's operation subset**;
no wildcard, arbitrary shell tool or global configuration change is added. The parent rejects
policy/store paths inside the provider's granted write roots and rejects symlinked authority roots.
Filesystem permissions still matter outside those known roots. Local OS-owner tampering is outside
this trust boundary, just as it is for the existing operator CLI.

The standalone entry point cannot mutate governed state. Assurance acceptance does not complete a
kernel work item, merge, publish, deploy or grant release authority. Existing kernel acceptance and
completion rules remain independently authoritative when that workflow is used.

## Host policy and identities

`AssurancePolicy` is a bounded operator-owned JSON document (64 KiB maximum). It names a case,
absolute candidate root, attested implementer, areas, principals and configured host checks.
Each area declares candidate paths, requirement paths, source paths, explicit depended-on areas,
and criteria with descriptions and host check IDs. Every relied-on area must also be in the
principal's granted scope. All inputs must be canonical relative non-symlink UTF-8 files.

The policy declares what the bounded candidate contains. It does not discover undeclared code,
resources or environmental dependencies. The client must inspect this selection before relying on
assurance. This first slice has at most eight areas, 32 total paths per area, 64 KiB exact bytes per
area, 32 criteria/principals/check definitions, 1–8 checks per batch and eight check batches per case.
A check timeout is 1–300 seconds; a batch's total configured timeouts cannot exceed 300 seconds.
Use a narrowly scoped change whose complete relevant inputs fit. Binaries, symlinks and larger
repository build closures are unsupported; split the task deliberately rather than omit material
inputs to satisfy a bound. External build caches/system runtime dependencies are not hermetic.

Paths locate bytes; they are never identities. SHA-256 over ordered path/content-digest/byte-count
inventories establishes the candidate. Requirements additionally bind the typed criteria. A binding
hash includes the candidate, requirements, source digests, implementer and transitive area bindings.
The receipt also binds the relevant area/check policy, including executable hashes/argument vectors.
A changing requirement criterion invalidates its consumers; an unrelated area's valid acceptance
remains usable. A new session is required after immutable host policy changes; in-place enabled=false
revokes a principal immediately at the next operation boundary. Expiry is always checked.

Every stored submission includes a host-generated AU identity, exact canonical content SHA-256 and
byte count, unique `ailedger-assurance:<case-hash>:<receipt-id>` reference, actual principal,
**host session** (the governed run ID or externally authenticated session label), configured provider,
requested model (not observed model), full admission-policy hash and scope-policy hash. External
clients have provider `external-client`; no provider/model identity or usage is invented. Existing
provider-run receipts remain the place for actual observed provider session/model/usage. Distinct
host principals/sessions are required; same provider is disclosed rather than prohibited universally.

## Typed interactions

Strict request schemas are under [handoff-assurance-v1/](handoff-assurance-v1/). Requests cap at
128 KiB and reject duplicate/unknown fields, including actor, grants, shell commands and host receipts.
A committed payload caps at 256 KiB. The existing transport retains its frame/concurrency bounds.

| Operation | Meaning and host work |
|---|---|
| `inspect_assurance` | Captures current identities and returns input metadata, missing inspection/tests, unresolved findings, pending check attempts, acceptance applicability and historical receipt IDs with precise reassessment reasons. Page history in groups of 16 with `offset`/`expected_version`; read one complete visible receipt using `receipt_id`. No acceptance is inferred. |
| `read_assurance` | Returns the selected captured input bytes and a durable host read receipt on `expected_binding`. Receipt means bytes were returned by the service, not that the person/model understood them or acknowledged delivery. |
| `run_assurance_checks` | Verification principal selects configured check IDs. Host materializes exact area/dependency inputs into a disposable directory, executes the pinned program/argument vector and captures real stdout/stderr, exit, OS/runtime, cwd, times and truncation coverage. No command string comes from the agent. |
| `record_assurance` | Records one coherent review, verification or targeted synthesis checkpoint; every criterion is pass/fail/unknown/not_checked. Actual inspected paths need own current-session read receipts. Verification passes additionally need own applicable successful host check receipts. Uninspected paths are computed explicitly. |
| `accept_assurance` | Acceptance principal explicitly names current complete independent review and verification reports plus rationale and dispositions. Rechecks identities, scope/grants, coverage, tests, uncertainty and disagreement. Allocates a decision receipt; never closes a process/work item. |

Review and synthesis receive inspect/read/record; verification additionally receives host checks;
acceptance receives inspect/accept. An implementer cannot submit independent assurance or accept
its own candidate. Acceptor, reviewer and verifier must have different actual principals; review
and verification must have different host sessions. These facts do not prove cognitive independence.
First-pass inspectors see only their own assurance reports through this endpoint. Authorized
synthesis/acceptance can retrieve cross-area reports within their explicit scope. Existing governed
manifests and filesystem visibility remain as before, so this is **not a blanket blind-review claim**.
A requirements-aware review has requirements; do not mislabel it as blind technical review.

Process outcome, authored status, recorded evidence and acceptance are separate. An observed zero
exit means only the configured program succeeded. A report marked complete may still have unknown
checks, uninspected files or uncertainty and cannot support acceptance. Checks with missing
executables/environment, cancellation, unknown endings, changed captured inputs or incomplete output
coverage do not support a pass. Successful configured tests can still be inadequate; semantic
relevance, genuine inspection and declared dependency completeness require independent judgment.

## Contradictions, freshness and targeted synthesis

Findings have local keys and durable `<report-id>:<key>` identities, kinds defect/contradiction/ambiguity,
paths and explicit contradictory finding references. Superseding a checkpoint never erases an
unresolved finding on the same candidate. Acceptance requires an attributable disposition for every
current-candidate finding. The supported disposition is `not_applicable`, with explicit rationale
and applicable independent report evidence. Actual defects require repair and new assurance; there
is no missing-test waiver, implicit risk approval or override that turns fail/unknown into pass.
Another current incomplete/failed/uncertain inspector cannot be ignored by selecting convenient
reports. Preserve the disagreement, perform targeted reinspection/synthesis and let the authorized
acceptor decide only when the evidence prerequisites are satisfied.

Synthesis is optional and explicitly scoped. It can read the affected area reports, preserve a
cross-area contradiction or ambiguity, and submit its own bounded judgment. It cannot accept work.
No universal synthesis agent, universal stage order or new ceremonial task is required.
An explicitly declared depended-on area must have current independent acceptance before its consumer
can be accepted. A new contradiction or revoked endorsement in that dependency also marks the
consumer for reassessment even when no candidate bytes changed; unrelated areas remain usable.

Freshness is computed from host-observed bytes and current authority every time. A candidate,
requirement, source or explicit transitive dependency change marks the affected reports/acceptances
`reassessment_required`, with exact changed paths/dependencies. Historical payloads and original
receipts remain retrievable. Unrelated candidate areas retain their assurance. Observations are
not filesystem reservations; external mutation immediately after a check is detected on the next
operation. Disappearance/unreadability is unavailable, never an empty successful snapshot.

## Interruption, durability and recovery

Content and receipt share one immutable, hash-chained task-12 journal record under the case lease.
The same `(case, principal, operation, request_id)` with the same request/session/policy returns
its original receipt. Changed content or binding conflicts. Retry appends a durable retry observation
before acknowledging the preserved receipt; it allocates no new report. Each attempt has a separate
identity. After an unknown transport/storage outcome, inspect and retry the **original** body/key
on its original host session. A replay is a historical recording fact, never a new freshness verdict.

Submit partial reports early. A continuation names the author's latest receipt in `supersedes`.
New sessions can inspect prior own reports and continue, but must acquire new current-session input
and test receipts; interrupted/unknown coverage is never inherited as pass. A changed candidate needs
fresh observations. Pending file fragments stay visible and are never promoted into canonical output.
Committed corruption fails closed. Existing episode journal capacity remains 2,048 records/32 MiB.

Host checks durably reserve their operation before launch and record each actual observation even
if later authorization/freshness fails. Inspection exposes start data, retained observations and
missing endings. An unobserved check prevents new checks and never automatically reruns on retry.
The operator must stop the remaining process and attest with:

```sh
ailedger assurance reconcile --authority /absolute/policy.json --store /absolute/store   --principal operator --session operator-session --request-id interrupted-key   --check-principal verifier --confirm-provider-stopped
```

Reconciliation stays unknown; it permits an explicitly requested new check key within the case's
remaining bound. The old key remains interrupted. It is not proof of a successful process ending.
The check runner uses existing process termination/timeout handling; it is an operator-authorized
local test process, not task 12's offline sandbox or a hostile-code sandbox. Configure only the
exact test executable/arguments and environment authority appropriate for the selected candidate.

## Measurement and compatibility

Assurance transport rows use `assurance-transport-*`; application attempts occupy a domain-separated
journal in the existing store. Both retain attempt/request/receipt/host-session correlation; missing
provider usage stays null. They are not mixed into historical findings/retrospective populations.
No historical event, baseline, grant default, claim dependency or kernel assurance rule changes.
Existing task-8 artifact recording and task-10 inspection remain independently usable.

Task-11 handoffs can carry the exact exported assurance receipt JSON as an explicitly pinned source,
including its source location and SHA-256, after authorized retrieval. That is an input snapshot,
not authority or a substitute for live assurance freshness. The task-11 EpisodeResult remains an
authored execution report, while these typed reports add the candidate/criteria/evidence semantics
it intentionally lacked. Task-12 offline scripts cannot gain assurance or implementation authority
by emitting new operation names; use the explicitly configured task-13 host instead.

See [client trial](handoff-assurance-v1/trial.md), [validation](handoff-assurance-v1/validation.md)
and [installation and retained examples](handoff-assurance-v1/installation.md).

For governed launches with task-13 assurance, every principal-visible input (including dependency inputs) must also remain inside the provider’s existing resource-directory grants and outside the authoritative ledger. Canonical path checks run with live admission on every call; an assurance policy cannot expand those grants.
