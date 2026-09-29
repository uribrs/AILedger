# Structured artifact submission v1

Task 8 only. Tasks 1–6 remain accepted; task 7's implementation and installation are
preserved and its client trial is still pending. Task 8 implementation, installation,
and client acceptance are tracked separately in the validation and installation records.

## Supported scope and reason

`submit_artifact` accepts **one VerifierOutput or CodeReviewOutput Markdown document**.
These are the existing work-scoped outputs of independent verification and code review,
including candidate-bound bundles. This slice removes the file/ID/registration sequence
for those outputs without creating a generic research-document kind or redefining assurance.
The existing artifact contract already carries content inline in `ArtifactRecorded`;
that is the authoritative content store, not a temporary file or a derived state projection.

Unsupported through this tool: UserRequest, PromptContract, OrchestrationPlan,
WorkflowRetrospective, CloseoutSynthesis, InternalRecon, invented generic research or
handoff artifacts, binaries, attachments, batches, external files/URLs/content references
as input, arbitrary metadata, and export. Existing artifact CLI support remains available
for existing kinds. No filesystem path from an agent is opened by this operation.

This scope is deliberately useful for the current verification/review workflow. It does
not claim every authored output can now be submitted through a structured tool.

## Typed boundary

`IArtifactSubmitter.SubmitArtifactAsync(binding, request, cancellationToken)` is implemented
by `FileGovernedTaskService`. MCP calls that service directly; no command strings execute.
The exact request and response shapes are in [request.schema.json](structured-artifacts-v1/request.schema.json)
and [response.schema.json](structured-artifacts-v1/response.schema.json).

```json
{
  "schema_version": 1,
  "request_id": "review-output-1",
  "kind": "code-review-output",
  "title": "Review of the scoped change",
  "content": "# Review\n\nThe actual review, findings and limitations.\n"
}
```

Required: schema version, stable request ID, supported kind, title, and nonblank inline
content. Optional nullable fields: `expected_content_sha256` and `supersedes_artifact_id`.
The expected digest, when provided, must equal SHA-256 over the exact UTF-8 body. Content
is not trimmed, normalized, rewrapped, interpreted as a shell command, or read from a path.
Title retains the existing domain's outer-whitespace trimming. Receipt title describes
the accepted title; fingerprinting retains the submitted title, including outer whitespace.

Bounds: 128 KiB UTF-8 content; 512 Unicode scalar values in title; 256 in a predecessor
ID; 256 KiB raw and canonical JSON request. Request IDs use the established 1–128 character
ASCII `[A-Za-z0-9._:-]` set. Digests are lowercase 64-character SHA-256 hex. Invalid Unicode,
duplicate/unknown JSON properties, caller-supplied IDs, actors, runs, scope or candidate
fields are rejected. The existing transport bounds remain 272 KiB per frame and eight
in-flight calls. Canonical event-count and log-byte capacity limits still apply.

## Attribution, links, and authority

The trusted host supplies task, actor, producer run, correlation, causation and the
`AllowSubmitArtifact` grant. There is no runless submission. The run must exist and belong
to the actor. A new submission also goes through existing command authorization, stage
admission, active producer, matching verifier/reviewer role, scope, document and assurance
rules. Neither client metadata nor writable provider configuration can rebind the host.

No capability/default-role change is needed: existing verification/review authors hold
RecordArtifact. Historical assignments are not migrated or augmented. Missing capabilities
still require an explicit authorized assignment change. A transport grant alone is not
permission to record; current ledger capabilities are checked for both writes and receipt
access. The producer actor remains the author of the event.

Scope is derived from the trusted run. For legacy runs this is the single work item.
For assurance runs the existing handler derives all members, working-run versions,
candidate identity, verifier pairing and member replacement edges. The agent does not
retype those links. Work dependencies and required verifier disposition tables continue
to be checked by the existing domain rules. No approval, claim resolution, run completion,
work completion, or endorsement operation is added. An accepted receipt proves recording;
the document participates in existing assurance rules in exactly the same way as the CLI.

## Versions and durable receipt

Artifact IDs are host-generated `AS_` plus 32 lowercase hex characters. One immutable
artifact is one version. Legacy revisions require `supersedes_artifact_id` naming the
current artifact of the same kind and work scope. Missing/stale/wrong-scope predecessors
retain the kernel's refusal, including the current ID when available. For candidate-bound
assurance the existing member-specific replacement rules remain authoritative; a supplied
predecessor is an assertion about a current intersecting predecessor, not a new replacement
policy. The receipt returns the actual member edges, not a fabricated scalar predecessor.

The receipt contains request/transaction IDs, operation fingerprint and algorithm,
task/actor/run/correlation/causation, commit time, ledger version, event IDs, and exactly
the accepted artifact's ID, kind, title, media type, content SHA-256/UTF-8 byte count,
content reference, work scope, producer, scalar predecessor, covered members, candidate,
paired verifier, working versions and member replacement edges. Absent links are explicit.

`ailedger-artifact:<percent-encoded-task>:<percent-encoded-artifact>` identifies content in
that bound ledger; it is not a URL, a filesystem location, or a globally unique repository
identifier. Retain ledger identity with the receipt. `artifact show` remains the retrieval
path. The reference and digest can identify later handoff inputs without implementing
handoff preparation or structured retrieval in task 8.

## Atomicity and retries

Content, artifact registration and `_ailedgerArtifactSubmissionReceipt` are serialized
into the same marked, newline-terminated event group under the existing cross-process
task mutation lease and durable flush path. There is no separately committed blob or
receipt sidecar. A truncated final line is invisible and is removed before a subsequent
append. Interior corruption fails closed. Derived projections and best-effort telemetry
can fail without revoking a committed artifact.

Idempotency scope is `(bound ledger/task, actor, submit_artifact, request_id)`. Canonical
fingerprint algorithm `artifact-submission-v1-c14n1` hashes a fixed-order UTF-8 JSON object:
operation, schema_version, task_id, actor_id, run_id, correlation_id, causation_id, kind,
title, content, expected_content_sha256, supersedes_artifact_id. Optional missing and null
values normalize to null; content text is exact. Request ID is the lookup key, not part
of the payload fingerprint. Bindings or content changes under a committed key conflict.
Findings and alternatives may independently use the same request key.

Retry the **unchanged body/key on the original trusted binding** after a lost response,
cancellation, or unknown outcome. Lookup precedes new document/stage/supersession checks,
so a committed output can still return its original receipt after the run completes or
the artifact is superseded. Current recording capability and host grant are still required.
Visible receipts are flushed again before a committed assertion. Receipt replay adds no
event and allocates no new artifact ID. Each attempt has a distinct observation ID.

Every canonical reader validates new receipt identity, shape, content digest/size and all
accepted links against the associated event. No new requirement applies to historical
artifact events without this receipt. Domain event shape and historical replay rules are
unchanged; findings-v1 and alternatives-v1 schemas and fingerprints remain unchanged.

Errors distinguish invalid request, unsupported kind, content identity mismatch, binding
denial, kernel refusal, key conflict, absent task, capacity, corruption, storage failure
and unknown outcome. `boundary`, `commit_state`, `retry` and `item_path` remain explicit.
Before lookup completes, an error cannot prove that an earlier attempt never committed.
Kernel refusal text is retained; in particular, fix its prerequisite rather than changing
the key to evade it. A corrupted history requires restoration/investigation, not resubmission
under a new ID. No speculative receipt is returned on failure.

## Providers and measurement

Trusted Codex and Claude launches expose exactly `record_findings`, `record_alternatives`
and `submit_artifact`. Provider guidance directs the two supported document kinds to the
new operation and retains the remaining CLI paths. Existing endpoints configured without
`allow_submit_artifact` do not advertise it and cannot admit it. Host grants can be revoked
without permitting attribution changes. No arbitrary-command or wildcard permission is added.

Application observations use `artifact_submission-attempts.jsonl`; transport observations
use `artifact_submission-transport-<session>.jsonl`. They retain original run correlation
and distinct attempt/request/transaction identities. `retrospective build --artifacts`
(optionally `--artifacts-telemetry DIR`) adds `artifactSubmissions`; default reports are
unchanged. The shared measurement reader/join preserves once-per-run provider usage,
missing-data coverage, failure boundaries and lost-response attribution. No attempt count
is presented as a complete census. Recording success does not demonstrate a successful
real-client task or reduced cost.

Remaining CLI work: task/actor/work preparation, scope and candidate preparation, dispatch,
context retrieval, stage changes, claim dispositions/decisions, completion and approval,
artifact inspection/export and unsupported artifact kinds. Tasks 9–14 remain unstarted.
The ordinary client trial and acceptance are pending; no billable episode was run.
