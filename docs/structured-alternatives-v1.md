# Structured alternatives v1 — task 7

Implementation contract, 2026-09-28. Additive to the accepted findings-v1 contract.

`record_alternatives` takes `schema_version: 1`, `request_id`, and a required
`alternatives` array of 1–32 objects. Each has `key`, `statement`,
`rejection_rationale`, optional nullable `replaced_by_decision_id` and `from_lesson`.
Keys are unique request-local ASCII identifiers (letter followed by up to 63 letters,
digits, underscores or hyphens). Request IDs use the findings-v1 1–128 character
alphabet. Statement and rationale each allow 8,192 Unicode scalars; existing decision
and lesson IDs allow 256. Non-null text must be nonblank. Wire and compact normalized
request limits are 256 KiB. Unknown/duplicate members, invalid Unicode and wrong types
are refused. No task, actor, run, grants or durable IDs are accepted from the body.

The trusted host binds destination, actor, optional run, correlation and causation,
with a separate alternatives operation grant. Bound run ownership and current actor
capabilities are checked on every call, including receipt retries. Runless hosts must
explicitly allow it. The operation does not confer any kernel capability.

Only surrounding whitespace is trimmed by existing domain rules. Quotes, internal
whitespace/newlines and Unicode are retained. Fingerprints compare decoded text *before*
trimming: omitted optionals and null are equal; JSON member order and equivalent
escapes are equal. Array order, local keys, whitespace, newline spelling, reference
spelling and Unicode composition are significant.

`alternatives-v1-c14n1` hashes compact fixed-order UTF-8 JSON with SHA-256 lowercase
hex and UnsafeRelaxedJsonEscaping, as findings-v1 does. Order: operation
(`record_alternatives`), schema_version, binding (task_id, actor_id, run_id,
correlation_id, causation_id), alternatives. Item order: key, statement,
rejection_rationale, replaced_by_decision_id, from_lesson. Nullable members are explicit.
The request ID is excluded. Retry scope is ledger/task/actor/operation/request ID;
changed body or binding under a committed key is an idempotency conflict.

Within the existing task lease, allocate collision-checked `AF_<guid-N>` IDs and
validate every RecordAlternativeCommand against candidate state before any append.
Existing decision existence and lesson-recall requirements remain. Commit one marked
event group with `_ailedgerAlternativesReceipt` on its first line. Receipt validation
checks schema, attribution, mappings, event types, provenance and boundaries; corrupt
metadata fails closed. Findings receipt metadata and schema remain unchanged.

Success/error envelopes follow findings-v1 semantics with an alternatives receipt:
the same identity/version/event fields, plus ordered `alternatives` maps containing
key, alternative_id, event_id (no findings/evidence arrays). Each call has its own
attempt_id. Identical retry returns the immutable original receipt, including its
original ledger version. Retries recheck grants/capability, not current mutation-stage
or reference rules. No prefix is committed on refusal. Lost/uncertain append responses
require the original request and binding; complete groups are re-flushed before replay.

Errors retain findings-v1 code/boundary/commit_state/retry/item_path/rule_id semantics.
Kernel refusal messages are preserved and identify `alternatives[i]`; rule_id remains
null. A conflict reports the earlier commitment, not acceptance of changed content.
Shape/binding errors cannot prove an earlier unknown request did not commit.

New default researcher, worker, verifier and code-reviewer assignments include only
one additional capability: RecordAlternative. Leads/operator retain their support.
Existing recorded assignments are unchanged. An operator must explicitly reassign
an existing actor with its intended existing capabilities plus record-alternative;
do not use reassignment as an automatic refusal recovery or reset customized grants.
Recording grants no claim resolution, decision acceptance, scope or approval powers.
The unchanged command and replay authorization both require the actual recorded grant.

Codex and Claude receive exact findings and alternatives tool grants on the trusted
provider endpoint. Tool guidance supersedes alternative shell examples in supplied
skills; CLI remains operator/debug support. No global skills/configuration change.

Implementation validation, installation and Uri's ordinary-task trial are distinct
checkpoints. No client trial or billable episode is implied by fixture success.

## Observations and inspection

`retrospective build --task ID --alternatives` adds only an opt-in `alternatives`
section. `--alternatives-telemetry DIRECTORY` selects a standalone host journal
location; the normal provider path uses task telemetry. Default historical reports
are unchanged. It can be combined with `--findings`; populations stay separate.

Application attempts are in `alternatives-attempts.jsonl`; recognized alternative
tool attempts are in `telemetry/alternatives-transport-<session>.jsonl`. They reuse
the bounded findings observation format and joins, including distinct attempts,
replays, refused item paths in responses, kernel refusal journal entries, nested
host/application timing and canonical transaction/event identities. No prose or
cost is duplicated in attempt journals. Canonical receipts prove commitments;
proposed IDs in failed observations do not. Missing/unreadable/truncated journals,
failed collectors, unmatched attempts and missing run/session observations remain
coverage gaps. A best-effort collection timeout cannot change commitment.

Runs and their completion observations remain one per run within each section;
the existing retrospective cost population is authoritative. Do not sum completion
copies across the optional sections or multiply cost by calls, retries or events.
Findings counts never include alternative receipts or recognized alternative calls.
Malformed frames whose tool identity cannot be parsed remain shared connection/
protocol observations in the existing findings transport journal. They cannot be
attributed to alternatives and are not manufactured into alternative tool attempts.

Still unobserved: client acknowledgement, complete attempt census, exact negotiated
MCP versions, relay admission denials before entry, provider permission/search-hook
denials, isolated model reasoning time and user usefulness. A local written response
is not proof that a client read it. Session identity joins from actual run completion,
not from the requested session or request ID. No performance or client acceptance
claim follows from fixture tests.

## Existing assignments and supported recovery

`actor attach --task ID --actor OPERATOR --target AUTHOR --role ROLE` is the
existing explicit assignment operation. Omitting capability arguments selects the
new defaults and replaces the prior list, so do not do that automatically on an
existing customized assignment. Inspect it, retain each intended capability using
repeated `--capability` arguments, and include `--capability record-alternative`.
Only the operator may make that assignment. A revoked grant or different host
binding cannot be repaired by changing tool arguments, inventing IDs, impersonating
another author, or relaunching a more privileged transcriber.

## Compatibility and installation

No domain event, capability enum, historical assignment or replay prerequisite was
changed. Command admission and replay already check the actual RecordAlternative
capability; tests cover all four newly defaulted roles with old assignment, explicit
upgrade, recording, approval refusal and revocation. Existing alternative text/link
rules remain untouched. The new operation has its own envelope metadata, leaving
both frozen findings schemas and original receipt serialization intact.

Provider service decorators must now forward both IFindingsRecorder and
IAlternativesRecorder; unsupported provider compositions fail before model launch.
Standalone findings-only hosts remain source-compatible and do not advertise the
new operation without its explicit host grant and recorder support. A trusted file
host can opt in with `allow_record_alternatives: true`; provider launches bind it in
the owning process and never through an agent-writable configuration file.

Older tools do not understand or validate alternatives receipt metadata. Keep all
writers to a task using the new operation at this version or newer. Retaining the
old package enables rollback for old ledgers, not a promise that an old writer can
safely continue a task that now contains alternatives receipts. Recovery retains the
existing local-filesystem process-crash guarantees, not distributed or power-loss
transactions. The explicit disk flush remains the established durability boundary.

An ordinary client task to exercise the increment: review an existing stored lesson
against today's implementation, compare ways to test/correct it, record the rejected
approaches directly as their author, and recommend keeping, revising or retiring the
lesson. This is a task suggestion for Uri, not an instruction to launch an episode.
Inspect its actual text/provenance, refusals/retries, remaining CLI work and usage
before declaring the client checkpoint accepted.
