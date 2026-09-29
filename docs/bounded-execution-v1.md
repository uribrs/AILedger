# Bounded execution v1 — task 12

This explicitly selected, experimental path executes **offline read-only episodes**. Its first
usable trial is a deterministic inventory audit of a curated package, with an interruption probe.
It is not a model research trial, an implementation executor or assurance for code changes.
Tasks 1–6 remain accepted. Tasks 7–11 remain implemented/installed with client trials pending.
Task 12 implementation, installation and client acceptance are separate checkpoints.

## Selection and existing components

`ailedger episode run|resume|inspect|reconcile` is a separate entry point. It never calls
`ProviderLauncher`, creates a governed task/run, synthesizes stages or waives prerequisites.
The existing `provider launch` and seven structured tools remain unchanged and usable.

`EpisodeExecutor` composes `IProcessRunner` / `SystemProcessRunner` for bounded process execution,
`ITaskInspector` / `HandoffPreparer` for existing authorized versioned reads, task 8's content
hash/size conventions, and the existing cross-process mutation lease for its separate journal.
`FileEpisodeStore` stores inline result content and its receipt in one record; there is no second
blob database, context selector or retrieval service. Existing governed findings/artifact recording
requires real task/run state and retains those rules. The experimental `submit_result` operation
records the task-11 `EpisodeResult`; it does not impersonate `record_findings` or `submit_artifact`.

The task-11 JSON parser and validation are reused. `read-only-audit` is one additive EpisodeSpec v1
profile; the existing research-to-design and verification-preparation packages stay compatible.
No historical reader, event, replay rule, grant default, dependency or assurance policy changes.

## Trusted admission

The operator supplies an **absolute host authority file** separately from the authored request:

```sh
ailedger episode run --authority /absolute/authority.json --store /absolute/results \
  --scratch /absolute/disposable-scratch --body-stdin < /absolute/start.json
```

`EpisodeStart` has only `schema_version:1`, `request_id` and the exact task-11 `handoff` envelope.
Strict JSON rejects duplicate/unknown fields; callers cannot supply an actor or grants in it.
`EpisodeAuthority` is trusted local operator configuration, not a provider tool argument. The
operator must keep it outside provider-readable system paths. Selecting it is an explicit local
authorization; never adopt provider-supplied configuration as a grant. Its fields are defined in
`src/AILedger.Core/Episodes/EpisodeExecutionContracts.cs`; the reproducible probe produces complete
examples. It binds:

- Actual local principal, enabled/revocable grant, expiry and exact package digest.
- Ledger root and existing inspection actor/run binding; existing kernel read authorization,
  including role/run isolation and current capabilities, remains authoritative.
- Exact requested read grants and permission to submit this episode's result. Every external
  source key/version/hash needs a host pin; source prose cannot grant access.
- Absolute executable and SHA-256, maximum invocations, attempts and elapsed seconds.

Admission validates the envelope/spec/record hashes, byte bounds and original task-8 artifact
receipt metadata. It reindexes the exact ledger version through task 10 and compares the complete
visible record inventory, including omissions. Missing resources, revoked grants, stale content,
untrusted source versions, unsupported environment and exhausted budgets stop execution.
Actual source pins mean the supplied immutable snapshot, **not verification of a live external
file or repository ref**. No arbitrary source path/URL is opened. Curators must authorize a new
package when a relied-on external source changes; semantic omission remains a curator risk.

The grant snapshot and hash, original package, host build, executable identity and deadline are
retained in admission. Grants, versions and executable identity are checked again at each tool
boundary, each follow-up/resume and at observed process exit. Revocation while an idle process
emits nothing is observed at its next boundary or deadline; it cannot acquire additional data or
commit output meanwhile. Observation is not a reservation against concurrent source changes.

## Offline provider boundary

The only built-in execution profile is `offline-stdio-v1` on macOS with `sandbox-exec`.
There is **no unsandboxed fallback, Codex/Claude model adapter, network grant or authorized spend**.
The provider is a pinned, self-contained executable or script using system runtimes. It is copied
and rehashed before execution to prevent a source-path replacement between check and launch.
The child starts in `/`, with an isolated HOME and no task store/authority file access. The
sandbox allows system runtime reads, root-directory listing/metadata, its exact executable and
`/dev/null` writes; network and other file writes are denied. The recorded profile/arguments show
actual permissions. Platform flags/executable availability are inspected before dispatch; inability
to apply the child sandbox is a launch failure, not permission to retry without it.

The default environment inherits only the existing process runner's allowlist, overriding home,
temporary/config/cache locations. System runtime/interpreter bytes are OS dependencies rather than
fully hermetic pinned artifacts. This is a bounded local trial boundary, not a hostile-native-code
security certification. Sandboxed script probes verify denied host-file reads, writes and sockets.
The first probe uses `/usr/bin/ruby --disable-gems` to avoid ambient gem discovery, and no credentials.

The host sends one JSON document on stdin: fixed episode instructions, exact prepared package,
invocation identity, previously committed results/receipts and read replies. Retrieved text is data;
it cannot change the host authority or operation set. Stdout carries strict newline-delimited frames:

```json
{"schema_version":1,"operation":"submit_result","submission":{"schema_version":1,"request_id":"checkpoint-1","result":{"schema_version":1,"episode_id":"the-spec-id","package_sha256":"the-exact-digest","status":"partial","summary":"What is established so far","recorded_outputs":[],"checks":[],"uncertainty":[],"stop_reasons":[],"additional_reads":null,"host_execution_receipt":null}}}
```

The illustration omits actual check entries: a real result must report **every spec acceptance
check** using task 11's statuses and limits. Recording proves authorship, not check truth.
`recorded_outputs` must be empty: this path cannot mint/assert kernel artifact receipts. Output
identity comes from the host's separate `EpisodeSubmissionReceipt`, never an authored host receipt.
The result summary/check evidence can carry observations and citations; structured kernel claim
promotion/disposition remains outside this path.

Other frames are `retrieve_context` with the exact task-10 query and `usage` with optional
session/model/input tokens/output tokens/cost. Exactly one matching payload is required. No shell,
permission, decision, approval, task completion or arbitrary dispatch operation exists. The host
retains raw bounded provider lines separately from validated submissions, including refused output.
The protocol is one-way per process invocation: replies and submission receipts are available in
inspection immediately and in the next invocation's stdin, not synchronous stdout acknowledgments.

## Dispatcher and bounds

Only a successful **new read of an input already listed in the package** can automatically start
another invocation of the same executable, package, principal and grants. No different objective,
model, input package or capability can be selected by provider output. A duplicate read returns its
preserved reply without charging another read or authorizing another turn. A reserved read whose
reply was lost may be retried within the remaining read budget. Explicit authored blocked/unknown
results or stop reasons stop dispatch even if a read succeeded. Business choices and material
ambiguity therefore remain visible for the user. The host cannot recognize an unreported ambiguity.

Limits: 512 KiB start/frame JSON; task-11 package/result limits; 2 MiB complete provider stdin;
1 MiB combined stdout/stderr and 64 frames per invocation; 128 tool attempts and 32 submissions per
execution; 1–8 invocations; 1–3 explicitly requested attempts; existing additional-read budget;
1 byte–16 MiB executable; 128 entries/16 MiB changed-file inventory; 2,048 journal records/32 MiB.
Elapsed time is one persisted deadline capped by the spec, including pauses and recovery. A resume
cannot refresh it. MaximumCostUsd stays zero; missing usage never authorizes a spend assumption.
A new execution/key is an explicit operator action, not an automatic way around an exhausted limit.

## Receipts, interruption and retry

The store is `<store>/episodes-v1/<principal-and-request-hash>/`. It contains immutable numbered
JSON envelopes with sequence, predecessor hash and record content hash. Content and its generated
`ES_…` receipt are in the same flushed record, atomically renamed and directory-synchronized on
Unix before acknowledgement. The same lease covers an entire execution/attempt. Inspection reads
only committed records. An incomplete `.pending` file is counted and preserved, never promoted or
reported as a submission. Corrupt committed content or a sequence gap fails closed.

Result idempotency is `(store, principal, execution request, submission request_id)`. Retrying the
same result returns its original receipt. Different result content under that key conflicts. A
submission reference is `ailedger-episode:<execution>:<submission>` in the explicitly selected store;
it is not a filesystem path or globally unique repository reference. Its SHA-256 is over the exact
canonical EpisodeResult JSON, using task 8's UTF-8 content hash primitive.

Every invocation records the actual executable/profile, input digest/bytes, OS/runtime, timeout,
start/end observation, exit code when observed, truncation when measured, raw tool attempts and
submitted IDs. Provider usage is optional, provider-reported, and allowed once per invocation;
missing usage is null/`not_reported`, not zero. Read reservations/replies and changed-file inventory
have their own records. Inventory coverage is the isolated provider directory; it is not a claim
that every file on the machine was inspected. Unexpected changes or unavailable inventory block
delivery even if the process exited zero. After an unobserved ending, reconciliation re-inspects
that directory and explicitly records unavailable telemetry; deleted scratch remains unknown.

| Observation | Meaning and recovery |
|---|---|
| `execution_outcome=succeeded` | Observed zero exit plus a recorded `reported_complete` result; acceptance remains `not_assessed`. |
| `failed` | Observed provider/process failure; partial results remain discoverable. |
| `blocked` | Grant/input/resource/budget/loop/stop condition prevents continuing. The process can separately have succeeded. |
| `cancelled` | Caller cancellation was observed and the existing process runner terminated/reaped the process tree. |
| `unknown` | Host ending/cleanup was unobserved or uncertain; never inferred as completion. |

The summary has separate `execution_outcome`, `process_outcome`, `authored_status`, `acceptance`
and `reconciliation_required`. An authored complete result can coexist with a failed/unknown
process. An active invocation also has no ending receipt yet; inspection reports unknown rather
than inferring liveness from a journal. The execution lease prevents concurrent resume/reconciliation. CLI run/resume exits 0 only for succeeded execution, 3 for other recorded outcomes;
inspect/reconcile exit 0 after successful inspection, so inspect the returned fields. Input/storage
errors exit 2. No output is silently accepted on an unknown ending.

`episode run` repeated with the original body/key returns current inspection without relaunching.
`episode resume` explicitly requests another bounded attempt on that same binding and includes
previous partial output. Changed packages/grants under an admitted key conflict. For an unobserved
ending, inspect first, establish that the old provider stopped, then use `episode reconcile
--request-id ID --confirm-provider-stopped`. This is an **operator attestation**, not invented process
telemetry. Reconciliation preserves unknown outcome and enables a budgeted resume; it cannot mark
completion. No PID-based automatic termination/reconciliation is claimed. A hard-killed host may
leave a sandboxed child alive; stop it before attesting, identified by the recorded executable path.

A line never received by the host cannot be recovered. Submit small partial results early.
Raw refused/truncated output is not a canonical submission. After storage uncertainty, inspect
and retry unchanged; do not delete pending files or regenerate IDs to disguise the uncertainty.

## Measurement and acceptance

Experimental records are excluded from historical task/run readers and reports; they are an
explicitly different population. Host records expose elapsed execution, attempts, read bytes,
provider coverage and output identities. They do not fabricate coordinator time, stages, old run
costs or an assurance score. Existing frozen report/cost outputs must match unchanged.

See [validation](bounded-execution-v1/validation.md), [installation](bounded-execution-v1/installation.md)
and [trial instructions](bounded-execution-v1/trial.md). The first trial is an offline package audit,
not proof of improved judgment, elapsed delivery or cost. Real model integration requires a separately
specified provider/spend boundary; no such episode is authorized or run here. Task 13 is untouched.
