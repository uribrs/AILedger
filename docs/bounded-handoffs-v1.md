# Bounded handoffs v1 — task 11

Task 11 prepares inspectable data packages. It does not execute episodes. Tasks 1–6 remain
accepted; tasks 7–10 implementation/installation and their pending client trials are unchanged.
Task 11 implementation, installation and client acceptance have separate receipts.

## Contract and entry points

The typed contracts live in `src/AILedger.Core/Handoffs/EpisodeContracts.cs`.
`HandoffPreparer(ITaskInspector)` composes task 10's existing authorized inspection and retrieval.
It never reads an arbitrary source path, registers an artifact, edits a ledger or dispatches work.
Operator CLI adapters are `handoff index`, `handoff prepare --body-stdin` and
`handoff retrieve --body-stdin`; all accept explicit `--root --task --actor` and optional `--run`.
Index also accepts `--selection task|relevant` (default task). Retrieve takes the exact returned
`RetrievalQuery`, preserves task 10's status/diagnostics/chunk semantics, and requires callers to
inspect `status` even if the CLI exits successfully. No provider tool list or grant changed.

`EpisodeSpec` v1 names an objective, non-goals, expected outputs, acceptance checks, requested
read grants with scopes, budgets, stop conditions and a tested profile. Versioned inputs live
in the enclosing `HandoffRequest`/`HandoffPackage`: ledger identity, task/actor/run snapshot,
ledger version, every record's SHA-256 and complete retrieval query, and any pinned sources.
The spec has its own digest; the package identity binds both spec and exact inputs.
There is no new generic artifact kind or content database.

An author inspects the complete authorized index, then marks **every visible record** included
or omitted at the indexed digest. Each choice names its reason, omission risk and retrieval
trigger. Missing, duplicate, stale or invented selections fail. Declared material inputs cannot
be omitted. Included records retain exact complete record JSON; oversized records use task 10
chunks and are reassembled and digest-checked. No text is silently truncated to fit a budget.
The final read checks ledger freshness and authorization again. This is an observed snapshot,
not a reservation: source or cognitive files can change after preparation.

Eight explicit curator assessments address decisions, alternatives, constraints, contradictions,
lessons, corrections, uncertainty and provenance. Each cites included keys (`Kind:id` or
`source:key`) or declares an absence/unknown with an assessment. The preparer verifies coverage
and links, **not semantic completeness**. A curator can still misjudge relevance or omit a
material fact without recognizing it. Frozen loss tests diagnose concrete instances of that risk.
Full included data retains claim/evidence direction and status, decision rationale, related IDs,
producer information, uncertainty and existing lesson correction/verification language.

Pinned source entries carry location, version, exact UTF-8 SHA-256, optional complete content,
materiality and omission/retrieval explanations. They are curator-supplied evidence. Hashes
verify supplied bytes, not source authenticity, permission, freshness or the truth of a version
label. No URL/file is fetched. Use this explicit path for version-pinned cross-task dependencies
or history outside the task-10 selector, after authorized source inspection. Do not invent a
version or hide an unavailable historical candidate. An omitted source keeps its locator/hash;
unavailable or unauthorized material is a stop condition, never a retrieval bypass.

Task 8's content identity remains authoritative: original `ArtifactSubmissionReceipt` inputs
expose their existing `ailedger-artifact:` reference, body SHA-256 and byte count. All original
receipt links remain in the complete JSON. Task 10 renders title/candidate text into ordinary
artifact context views; their record digest is deliberately **not** called the artifact body
hash. A context view without an accessible original receipt carries only its context identity.
Original receipt reads still require that actor/run's current recording capability and host grant.
CLI preparation grants inspection only and does not expose another actor's receipt. Task 8's
supported recording kinds remain VerifierOutput and CodeReviewOutput; a handoff is an exported
file, not a newly registered kernel artifact.

## Bounds and authority

JSON only; YAML offered no authoring benefit for these fixtures. Strict JSON rejects duplicate,
unknown and attribution/waiver fields. Authoring request: 512 KiB; 512 visible records; 32 pinned
sources; 128 KiB per source; 4,096 Unicode scalars per explanatory text; eight preservation
categories with at most 64 references each. Spec lists have 1–32 entries. Package UTF-8 budget:
1–512 KiB, including metadata and omission inventory; the outer JSON envelope and optional
Markdown rendering have additional bytes. Preparation permits 1–1,024 reads. Index alone
permits at most 32 pages. An exceeded budget produces no successful package.

Preparation actually enforces package and preparation-read budgets. Additional-read budget
(0–128), time (1–240 minutes) and zero additional model spend describe the requested trial;
there is no task-12 runtime to enforce them. Zero cost does not claim free cognition: the package
authorizes no new model/API launch. Uri must authorize any later trial separately. Actual host
permissions always apply. Only `read_context`/`read_source` requests are accepted by these two
profiles, and requested grants confer no authority.

The CLI principal is selected outside authored JSON, as in existing operator commands. For
an ordinary bound task use its real recipient actor/run; the existing BuildContext capability,
active-run checks and reviewer isolation remain enforced on every index/read. A package made
for an operator is **not** proof that another recipient may see all its data. The curator must
use the appropriate visibility boundary; these profiles are not blind-review packages.
Content, source prose and retrieved text cannot override the episode instructions or expand
authority. Old dispatch/filing commands in historical content are quoted evidence only.
No new replay, authorization, dependency, assurance, stage or completion rule exists.

## Package and result identity

`PreparedHandoff` contains `schema_version`, `package_sha256`, `package_bytes` and `package_json`.
Hash/size cover the exact UTF-8 string in `package_json`, not a reserialized outer envelope.
Retain the envelope unchanged. Markdown is only an inspection rendering. Digest identity is
integrity, not a signed producer attestation or endorsement; repeated preparation can differ
because snapshot observation time and ledger location are included.

`EpisodeResult` is an authored report bound to episode ID and package digest. Status is
`reported_complete`, `partial`, `blocked` or `unknown`; each acceptance check has
`observed_pass`, `observed_fail`, `not_checked` or `unknown`, evidence and a limitation.
It preserves uncertainty, stop reasons, optional observed additional reads and existing task-8
output metadata. `EpisodeResultValidation` checks bounds/identity and all expected checks;
it does not certify observations, verify output receipt authenticity or accept a deliverable.
Result JSON is capped at 128 KiB, 32 output links and 32 uncertainty/stop entries. A task-11
host execution receipt must be null. No submission, persistence, recovery or process-completion
mechanism for this report is introduced. Those are task-12 concerns.

## Selection limits and measurement

Task 10's exact role/work/assurance selector and omission policy are retained. Protected records
are neither listed nor counted. Superseded/history/challenge/transcript gaps remain explicit;
source locators do not defeat those boundaries. Cognitive skills are absent from this standalone
CLI binding; use the repository-local profiles as technical guidance, not a refreshed governed
brief. Existing governed provider contexts continue their original policy unchanged.

Package measurements count visible/included/omitted records and sources, included-content bytes,
preparation inspection/retrieval calls and retrieved bytes. Reference metadata and full package
bytes are separate from content bytes. Unknown client additional reads, provider tokens/cost and
elapsed delivery remain null. The executable probe separately measures its deliberate omitted
record retrieval; that mechanical read is not an agent's observed need for more context.
Historical measurement output and expected baselines are unchanged.

See [trial instructions](bounded-handoffs-v1/trial.md),
[profiles](bounded-handoffs-v1/profiles.md) and [validation](bounded-handoffs-v1/validation.md).
