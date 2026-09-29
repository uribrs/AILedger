# Explicit claim dispositions v1

Task 9 only. `record_claim_dispositions` calls `IClaimDispositionsRecorder` directly through the
trusted Codex and Claude recording endpoints. It records an authorized actor's explicit judgments;
it does not establish that those judgments are correct. Supporting evidence never validates a claim.

## Supported operation

```json
{
  "schema_version": 1,
  "request_id": "judgments-1",
  "dispositions": [
    {
      "key": "j1",
      "claim": { "claim_id": "C1" },
      "expected_status": "open",
      "status": "validated",
      "rationale": "The cited observation establishes the specific claim because …",
      "evidence": [{ "evidence_id": "E1" }]
    }
  ]
}
```

One request holds 1–32 judgments about distinct existing claims in the bound task. Each item has a
unique local key, an exact typed claim reference, 1–32 unique typed existing evidence references,
an expected current status (`open` or `validated`), and a target (`validated` or `rejected`).
A nonblank rationale is required, at most 8,192 Unicode scalar values; its exact text, including
outer whitespace, survives in the resolution event and receipt. References are at most 256 scalars
and cannot contain outer whitespace. Keys/request IDs follow findings-v1 syntax. Both raw and
normalized bodies are bounded to 256 KiB. Duplicate/unknown JSON properties, numeric/unknown statuses,
unpaired surrogates, null items and arbitrary commands are refused.

[Request schema](structured-claim-dispositions-v1/request.schema.json) and
[response schema](structured-claim-dispositions-v1/response.schema.json) are advertised by MCP.
Record evidence separately with `record_findings`; retain returned durable IDs. This operation
creates no claims/evidence, allocates no claim IDs, and never infers a judgment from evidence.

## Authority and consequences

Task, actor, run, correlation, causation and operation grant come from the host. Provider sessions
keep this binding in the launch process, protected by the existing authenticated local relay.
Changing agent arguments, client metadata, or files cannot confer authority. The default provider
configuration grants exactly four named recording tools; each still checks recorded capabilities.
No role defaults, existing assignments or assurance rules change in task 9.

Every item goes through the existing `ResolveClaimCommand`, authorization policy, reducer and
replay validation. ResolveClaim capability is required (normally operator/planning lead). The grant
to call the endpoint alone is insufficient, including for receipt lookup after capability revocation.
All cited evidence must already support the identified claim for validation, or refute it for rejection.
Unknown references and direction mismatches retain the kernel's actionable explanation.

Open claims may validate or reject; validated claims may reject with refuting evidence. Rejected and
superseded claims remain terminal. Same-status new judgments are refused. Existing accumulated
supporting evidence survives reversal. `expected_status` prevents a stale request from silently
changing a claim whose status advanced since inspection; it is not a snapshot or truth guarantee
and does not assert that no other evidence has arrived.

Rejection emits the existing decision/work invalidations against progressively updated candidate
state. Proposed/accepted dependent decisions become invalidated; active work becomes blocked;
other non-stale dependent work (including completed and manually blocked work) becomes stale.
Runs are not closed. Validation neither accepts a decision nor restores invalidated work.
Two rejections sharing dependents follow exactly the existing sequential-command semantics.
The service preserves the current claim command's lifecycle admission, including no new stage or
active-run requirement: a run binding must identify an existing run owned by the actor, and new
judgments still require current capability and valid claim transitions. Providers normally use their
active launch connection; the same trusted binding can recover a receipt after run completion.

## Atomicity, diagnostics and recovery

The coherent unit is the entire bounded batch, including every emitted dependency consequence.
Under the task mutation lease, the service looks up retry identity, builds the full candidate using
real command rules, validates it and appends one marked canonical group. Any failing item prevents
all new canonical writes; there is no accepted prefix. Validation is fail-fast, with `item_path`
identifying the first failing item or reference. Correct it and resubmit the whole batch. Uninspected
later items are not reported as valid. Shape, reference, state conflict, binding, kernel, capacity,
history corruption and storage/transport errors remain distinguishable. Best-effort diagnostics may
be written for refusals; they cannot establish a canonical commit.

The receipt lists every accepted local key/claim, previous and resulting status, verbatim rationale,
typed evidence references, resolution event and exact dependency events with resulting statuses.
It also carries task/actor/run/correlation/cause, transaction, all event IDs, committed version/time,
and a canonical payload fingerprint. The receipt describes that commit, not current task state on retry.

Retry keys are scoped to operation, task and actor. Fingerprints include the immutable host attribution
and ordered request content, excluding request ID itself. Equivalent JSON spelling replays; changed
rationale/evidence/order/status/binding under a committed key conflicts and is never applied. A fresh
key cannot evade current state/authority checks. Receipt and events share one append group; there is
no separately committed sidecar. Complete-prefix, partial-line and unterminated tails are invisible,
and a retry removes an uncommitted tail before appending. A complete group with a lost response or
failed flush is re-flushed on lookup, then returns its original receipt without rerunning transitions.

On `outcome_unknown`, storage failure with `retry: same_request`, or a lost response, retain and retry
exactly the original body/key/trusted binding. Do not invent a new key. `commit_state: committed` on
idempotency conflict refers to the prior request, not the changed submission. `state_conflict` requires
reading the current claim and reconsidering the judgment; change the proposed disposition only after
that review. Corrupt history fails closed and requires operator storage investigation. Revoked access
must be restored by an authorized operator before receipt recovery. Existing task event/byte limits
include the dependency fan-out; hitting a limit accepts nothing. Receipt access survives reduced
mutation budgets. Projection or telemetry failures do not revoke committed truth.

## Compatibility and measurement

`ClaimResolved.Rationale` is additive and omitted when absent. Old events/commands remain valid;
only a present rationale is required to be nonblank on replay. Existing event types and consequence
rules remain unchanged. A new `_ailedgerClaimDispositionsReceipt` belongs exclusively to index zero
of its marked group. The reader checks all fields, event mappings, causal chains and fingerprint;
malformed, duplicated, mixed or moved receipts fail closed. Historical task bytes are not migrated.
Findings-v1, alternatives-v1 and artifact-submission-v1 schemas remain unchanged.

Each tool interaction and application admission has its own attempt ID. Durable request, transaction,
event, run and provider-session identities remain distinct. Run correlation is never replaced with
the request key. `claim_dispositions-attempts.jsonl` and `claim_dispositions-transport-*.jsonl` use the
existing measurement joins, timing and missing-data definitions. No rationale is copied to telemetry.
`retrospective build --dispositions [--dispositions-telemetry <directory>]` adds an opt-in
`claimDispositions` section. Default reports remain unchanged; provider usage stays once per run,
never per judgment, event or retry. Operation sections' run populations must not be summed together.
Unobserved attempts stay unknown; a canonical request is proven by its receipt, never by telemetry.
Provider permission/search-hook denials remain their separate existing observation boundaries.

## Remaining CLI work and client checkpoint

Formal decision proposal/acceptance retains its distinct ResolveDecision authority and semantics.
Claim supersession/refinement, challenges, role assignment, task/context inspection, workflow/work/run
lifecycle operations, and unsupported artifact kinds remain on their existing CLI paths. No generic
command wrapper, automatic closure of old claims, retrospective approval or tasks 10–14 are included.

Client trials for tasks 7–9 remain pending. Installation and scripted fixture checks are not client
acceptance. During Uri's ordinary task, inspect whether eligible agents actually use this operation,
whether each rationale warrants its judgment over the cited evidence, whether refusals help recovery,
whether dependency changes are understood, and which CLI interactions still need manual repair.
Inspect real receipts/transcripts, response-loss retries and once-per-run usage alongside the output.
