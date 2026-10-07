# Orchestration driver — Part 6C + 6D

## Delivery and evidence boundary

This increment implements repair-impact reassessment, conservative selective review reuse, required-check
ordering, positive process-stop reconciliation, and recovery across completion, Learn and archive. It
extends the existing assurance journal, storage admission, shared dispatch and durable driver. Parts
1–5 and 6A/B remain uncommitted alongside this change; no live ledger, real provider, global tool,
commit or publication was used.

**6C is implemented for the bounded declared-input profile. 6D has assembled lifecycle and recovery
fixture coverage, built-CLI boundary coverage, and operating instructions below.** The supported
single-member fixture reaches legitimate archive after explicit acceptance, including repaired and
replanned candidates. This is substantially beyond the A/B fixture that stopped with other members
unresolved. It is not evidence of real-provider compatibility, measured productivity or unattended
operation on arbitrary repositories. The milestone assessment below preserves that distinction.

The subsequent [Part 7 record](orchestration-driver-part-7.md) adds startup failure retention,
provider infrastructure stops across restart, adoption observations and bounded real-provider results.
Its troubleshooting guidance supersedes assumptions that version/help probes establish launch compatibility.

## Contracts and ownership

| Contract / owner | Responsibility |
| --- | --- |
| Optional `record_assurance.repair_impact`, schema 1 | Independent synthesis judgment associating original evidence with a newly observed candidate. The existing report journal retains it. |
| Optional snapshot `physical_binding_sha256` | Host-observed area/input/requirement/transitive-dependency identity before the aggregate governed binding is added. Old snapshots remain readable; absent hashes cannot establish reuse. |
| `AssuranceService.Repair` | Validate impact, proposed invalidation/reuse and actual required-check outcomes. No new check executor or acceptance ledger. |
| `GovernedAssuranceContext` | Check current role, grant, run/session, coordination owner and expiry under the task lock, including after a bounded check. |
| Optional coordination `assurance_anchor`, schema 1 | Pin task to the original case ID, canonical store path and member set. Ownership takeover preserves it. It contains no decision/evidence payload. |
| Optional evidence `TrustedTerminationVersion = 1` | Command owner attests that process-termination evidence was authored by an Operator at recording time. Later promotion cannot manufacture that provenance. |
| Storage completion lease and stage admission | Fresh acceptance observation for completed-work recovery into Learn and archive; revalidate immediately before append. |

The command-time evidence owner mints trusted termination provenance; replay validates it only when
present. Historical evidence without the field still replays, but cannot satisfy this new recovery
prerequisite. Provider sessions, including an Operator-named provider, cannot author reserved
`process-termination` evidence. New completion-continuation admission is process-local and is never
accepted from serialized provider commands. Existing stage events have no new mandatory fields;
historical legal replay does not acquire new prerequisites.

## Repair impact and selective reuse

The synthesis principal must be independently authorized, scoped to the assessed areas, and distinct
from the implementer and the reused review's producer. It supplies a schema-1 `repair_impact` alongside
an ordinary report. Its `before_receipt` identifies immutable original area evidence; the new report's
host snapshot supplies the actual after basis. Required fields are:

- `affected_areas`, all current `requirement_ids`, the complete transitive `dependency_areas`, and an
  authored `dependency_rationale`.
- `required_checks`, exactly the check IDs attached to the area's applicable criteria.
- `addressed_findings`, retaining original `<report-id>:<key>` identities; this list never disposes them.
- `reuse_receipts`, `invalidate_receipts`, and explicit `uncertainty`.
- `impact_read_receipts`: this assessor's current-session host reads covering other affected areas
  and the complete transitive dependency closure. The report's normal reads cover its own area.

The full request schema is [record_assurance.schema.json](handoff-assurance-v1/record_assurance.schema.json).
These fields are judgments. The host separately checks observed bytes, policy, governing context,
member set, run associations and live authority. Equality alone cannot establish independence; the
assessor must inspect and explain the declared dependency/affected closure. Missing or incorrect
closure, unsupported impact, or uncertainty remains unresolved. A changed acceptance basis with
historical evidence requires a complete current independent impact assessment before acceptance.

Reuse is deliberately narrow. An unaffected, complete requirements-aware `review` report can remain
applicable across a new working run when its area and transitive physical inputs, requirements,
scoped policy, governing artifacts, authority digest and task/member set are unchanged. The original
producer must remain live and authorized; the review must have no findings, judgments, missing
inspection, failed checks or uncertainty. A separate current synthesis association names that exact
old receipt. Its original run/session, candidate, timestamp and payload are never relabeled.

Changed source/dependencies, requirements, governing decisions or artifacts, policy, authority, or
members defeat reuse. Current independent invalidations defeat selection even if an older association
proposed reuse. Affected evidence remains visible as stale, and original findings remain obligations.
The positive regression changes area A, invalidates its old evidence, and legitimately reuses an
independent unchanged area B review after inspecting the affected area. Dependency and context
negative cases reject superficially similar but inapplicable evidence.

Verification reports, host checks, current-session read receipts and acceptance are **never transferred**
by this contract. A new candidate still needs a fresh paired independent verifier and blind review,
actual current required checks, independently evidenced finding dispositions and new explicit
acceptance. This profile reduces repeated requirements-aware review; it does not claim to skip
verification for unaffected areas. Original finding refutation without repair remains supported by
A/B's independently observed source/reproduction evidence. Severity and impact prose grant no waiver.

## Checks, repeated failures and physical reconciliation

Applicable criteria select checks from trusted configuration. The verifier runs those checks through
its existing `run_assurance_checks` grant during verification. Before proceeding into blind review, including prepared/restarted Review entry,
the driver observes that every declared area's required checks succeeded for the current verifier
session and current binding. A later negative check also blocks. Requirements-aware passing reviews
likewise require successful actual outcomes. Compilation, generic Proceed, narrative success and an
older passing report cannot substitute. Negative/unknown reports remain recordable for diagnosis.

No worker or lead acquires verifier authority. No extra ceremonial check phase is added. The bounded
profile runs all declared required checks for each new candidate; it does not attempt unsupported
semantic test-subset selection. Checks outside the applicable declared criteria are not mandated.
The case still permits at most eight check batches; exhaustion preserves unresolved work. Changing
case ID, store or member association is refused by the pinned coordination anchor. Successor-case
budget extension/migration is unsupported, not an automatic reset or permission to discard findings.

A failed/cancelled worker can have changed files without producing a completed result. Active or newer
uncertain runs block previous evidence. Even after a later completed worker, earlier failed/cancelled
work requires positive, attributable stopped-process evidence. Unchanged bytes, missing output and
expired leases do not establish termination. After trusted reconciliation the authorized Findings
route can request repair/replanning; a new completed candidate receives fresh physical observation,
verification, impact assessment, independent review/disposition and explicit acceptance.

A repair that fails verification again routes through bounded Findings investigation. Only the
supported Replan decision enters Design reconsideration and its existing lesson-consultation gate.
Current governing artifacts and preparation are then reestablished before implementation. The
assembled tests exercise repeated defects and newly introduced defects, retain all original finding
IDs, and finish only after actual checks and independent dispositions succeed. Generic Proceed at
this boundary pauses unresolved; no repair counter forces closure.

Interrupted checks use the existing journal reservation/reconciliation path. A recovered key retains
its original interrupted/unknown outcome and never launches another check. After the trusted operator
has established termination, owning reconciliation permits an explicitly requested **new** key within
the remaining budget. It does not turn the original result into a pass.

Physical snapshots are bounded observations, not filesystem exclusion. Host checks operate on captured
declared inputs; assessment and acceptance reread live inputs and final admission revalidates before
append. Tests mutate inputs during checks and immediately before completion/closeout. Arbitrary
external writes after the final read are not atomically excluded. Undeclared inputs or unknown
semantic dependencies are unsupported and must not be hidden behind a green snapshot.

## Ownership and recovery

The lock order remains task then assurance journal. Checks can hold the task lock, so a heartbeat may
wait. Driver setup now requires owner lifetime of at least:

```
1.5 * (min(300, sum of the eight longest configured check timeouts) + 30) seconds
```

`--owner-lease-seconds 600` covers the maximum supported 300-second batch plus the configured margin;
the CLI allows 1–3600, default 120. Provider timeout is separate. The margin is a supported timing
configuration, not an unlimited IO guarantee. Controlled gate/time fixtures show heartbeat contention,
success within the lease and refusal after expiry. Owner expiry is rechecked after execution before a
result can become current passing evidence. Retained check observations stay reconcilable.

| Boundary | Recovery |
| --- | --- |
| Acceptance reply lost | Retry exact original body/key/session; returned receipt is historical recording, not fresh authority. |
| Work completion committed before Learn | Recognize the completed member; obtain fresh acceptance admission and transition to Learn without reopening, redispatching or duplicating completion. |
| Closeout completed before Archive reply/append | Reobserve accepted physical candidate and match the original durable closeout intent; do not dispatch Closeout again. |
| Governing/physical/authority changes during those gaps | Fresh owning admission refuses stale acceptance; leave work/receipts intact and unresolved. |
| Owner loss or stale epoch | Fence writes and preserve execution uncertainty; no automatic inference of process termination. |
| Case/store change after prior findings/checks | Refuse the changed anchor; original journal and obligations remain authoritative. |

A restart fixture exposed a real duplicate-closeout bug: the restarted driver had no candidate identity
and could not match its prior durable intent. Recovery now observes completed work under fresh host
admission and restores the actual candidate before looking up closeout. A separate mutation fixture
stops archive when that physical basis changes. Launcher completion secrets remain transient; only
the launcher closes normal runs.

## Supported setup and operation

This operating profile is one selected governed work member, with up to eight bounded declared UTF-8
areas and explicit acyclic dependencies, in the existing macOS confined deployment. Policy limits
remain 32 paths per area, 256 KiB total exact input bytes per area, 128 KiB per input, 32 criteria,
principals/check definitions, and 1–8 checks per batch. The driver does not schedule a multi-member
bundle. Completing member A cannot complete member B or archive a task containing unresolved members;
that boundary is tested. Dependent areas require separate explicit acceptance in dependency order.

Build and inspect the local CLI (no global reinstall):

```sh
dotnet build AILedger.sln --nologo
dotnet src/AILedger.Cli/bin/Debug/net8.0/AILedger.Cli.dll orchestrate coverage
```

Use a trusted setup outside provider write roots for the profiles, authority JSON and canonical
assurance store. Keep material candidate, requirement, source and dependency inputs inside the
selected work's existing resource-directory grants and outside the ledger. Declare the full input
closure before launch; the host never widens a grant to make a check pass. Check executables are
pinned by SHA-256 with exact arguments and timeouts. They must work on the captured input layout;
a project build requiring undeclared files is not supported merely because its command can be named.

The strict profile and intake formats are in the [Part 5 setup](orchestration-driver-part-5.md#enforceable-deployment-and-setup).
Configure profiles for `discovery`, `recon`, `design`, `scope`, `implementation`, `verification`,
`review`, `repair`, `findings` and `closeout` as needed, with existing preparation roles/work templates. Use the
actual configured actor names; Worker, Verifier and CodeReviewer keep their existing capabilities.
The verifier provider must differ from the implementer. The blind reviewer receives its restricted
review context and no requirements-aware tools, plans or verifier narratives.

Separately configure a live PlanningLead/Researcher for requirements-aware `review`, an independent
PlanningLead/ImplementationLead for `synthesis` where needed, and a trusted external Operator for
`acceptance`. The acceptor must differ from implementation and selected evidence producers. Every
principal needs its complete scoped area/input grants. Provider children never receive acceptance
role sessions. Add this explicit association to the existing schema-1 authority policy:

```json
"governed": { "schema_version": 1, "task_id": "example", "work_item_ids": ["W1"] }
```

Use [the current assurance contract](handoff-assurance-v1.md) for complete area, criterion, principal
and check shapes. Each criterion's `check_id` names the actual required check. `implementer` is the
real working actor. `case_id` and canonical store remain fixed throughout repair and restart. A
protected policy may be changed by trusted setup, but changed policy invalidates affected applicability;
editing it is not a receipt renewal.

After trusted actor/grant/profile preparation, use the built CLI for intake and execution:

```sh
dotnet src/AILedger.Cli/bin/Debug/net8.0/AILedger.Cli.dll orchestrate intake \
  --root /absolute/fixture/tasks --lesson-root /absolute/fixture/lessons \
  --task example --actor operator --request-file /absolute/protected/request.json

dotnet src/AILedger.Cli/bin/Debug/net8.0/AILedger.Cli.dll orchestrate run \
  --root /absolute/fixture/tasks --lesson-root /absolute/fixture/lessons \
  --task example --actor operator --work W1 --profiles /absolute/protected/profiles.json \
  --working-directory /absolute/repository --cognitive-root /absolute/cognitive \
  --assurance-authority /absolute/protected/authority.json \
  --assurance-store /absolute/protected/assurance-store \
  --acceptance-principal operator --owner-lease-seconds 600
```

These are operating instructions, not authorization to launch real providers in this development task.
A prepared task enters with `orchestrate run`; it does not need recreated intake. Serial execution
requiring a recorded justification uses driver `--serial-justification ALTERNATIVE_ID`; the separate
stage CLI uses `--serial-because`. Never invent a justification receipt.

`AwaitingAcceptance` is an expected explicit pause, not success. The separate trusted client uses
`assurance inspect_assurance`, `read_assurance`, and `record_assurance` with current schema bodies.
For governed calls add `--governed-root /absolute/fixture/tasks` (the directory containing the task's
`events.jsonl`), along with `--authority`, `--store`, `--principal`, `--session` and `--body-stdin`.
Read/inspection supplies the exact `expected_binding`; do not manufacture hashes or receipt IDs.
The requirements-aware reviewer reads current input and records its report after required checks.
For repairs, synthesis reads the affected/dependency closure, records `repair_impact` and separate
evidenced `finding_judgments`. Reuse names the immutable original review receipt explicitly.

The independent trusted acceptor then submits the current decision:

```sh
dotnet src/AILedger.Cli/bin/Debug/net8.0/AILedger.Cli.dll assurance accept_assurance \
  --authority /absolute/protected/authority.json --store /absolute/protected/assurance-store \
  --governed-root /absolute/fixture/tasks --principal operator \
  --session explicit-acceptance-session --body-stdin < /absolute/protected/decision.json
```

The [acceptance schema](handoff-assurance-v1/accept_assurance.schema.json) requires the current binding,
applicable report receipts, rationale and independent dispositions of every original finding. Open
challenges, disputes, uncertainty and defects block; no residual-risk waiver exists. Resume the same
driver command afterward. It observes current acceptance, completes the selected member through the
kernel, performs Learn/closeout, and archives only when all owning prerequisites hold. CLI exit 0
means Archived; ordinary unresolved outcomes exit 4, unknown execution 5, cancellation 130.

On hard crash, first establish actual provider/check termination and use trusted orphan Failed/Cancelled
closure if its original launcher is gone. Record new Operator evidence with source `process-termination`,
citation `<task>/<run>`, and observed termination details. Use built CLI `orchestrate reconcile-stopped
--root ... --task ... --actor operator --run ORIGINAL_RUN --termination-evidence EVIDENCE_ID`.
Old unmarked evidence remains history; it is not retroactively upgraded by changing the actor's role.
For interrupted host checks use built CLI `assurance reconcile` with the original `--request-id`,
`--check-principal`, trusted operator `--principal`/`--session`, policy/store/governed root and
`--confirm-provider-stopped`. Reconciliation preserves unknown; a new check needs a new explicit key.

## Validation record

Only temporary ledgers, fake adapters and controlled local subprocesses were used. Integrated tests
exercise the same driver/shared-dispatch/assurance/storage owners. Built CLI subprocess tests cover
acceptance exact replay, ordinary and already-committed completion restart, repair-association replay,
and existing authority/refusal/transport boundaries. The full intake-to-archive fixtures inject fake
adapters into the CLI host; they are not real model-provider executions.

The main integration evidence is reviewable in:

| Fixture | Boundary exercised |
| --- | --- |
| [RepairContinuationTests](../tests/AILedger.Tests/Orchestration/RepairContinuationTests.cs) | Changed/unchanged failed or cancelled work, independent impact, affected invalidation/unaffected reuse, transitive/context negatives and receipt replay. |
| [AssuranceOwnershipTests](../tests/AILedger.Tests/Orchestration/AssuranceOwnershipTests.cs) | Deterministic check/heartbeat contention, expiry/reconciliation, required-check ordering on normal and resumed entry, case/store continuity. |
| [GovernedCoordinationTests](../tests/AILedger.Tests/Orchestration/GovernedCoordinationTests.cs) | Trusted intake and preparation through repair/replanning, independent acceptance, Learn/archive, lost archive append and changed closeout candidate. |
| [AcceptanceCompletionTests](../tests/AILedger.Tests/Orchestration/AcceptanceCompletionTests.cs) | Built-CLI exact acceptance/completion restart, incorrect-finding refutation, dependent-area acceptance, member isolation, revoked/reassigned authority and last-moment physical mutation. |
| [TerminationAttributionTests](../tests/AILedger.Tests/Core/TerminationAttributionTests.cs) | Historical evidence replay, forged provenance, role promotion and provider-authored stop-evidence refusal. |

| Check | Actual result |
| --- | --- |
| Final focused repair/acceptance/ownership/lifecycle selection before canonical anchor | 50 passed, 0 failed/skipped; 1m53s. |
| Canonical anchor and assembled integration selection | 52 passed, 0 failed/skipped; 1m55s. |
| First default full `dotnet test --nologo` | 2,673 passed (2,574 main + 99 memory), 0 failed/skipped; main 7m42s. |
| Final default full after prepared-Review guard | **2,673 passed** (2,574 main + 99 memory), 0 failed/skipped; main 7m26s. |
| Additional serialized full suite | **2,673 passed** (2,574 main + 99 memory), 0 failed/skipped; main 9m41s. |
| Final diff whitespace check | Clean at delivery. |

Focused failures during development were corrected rather than hidden:

- Initial new test compilation lacked the `StandardInput` support import; corrected.
- First selection: 20 passed / 9 failed due to missing fake provider-session identity; corrected.
- Next integration: 26 passed / 10 failed, comprising missing serial justification and a fixture
  reusing an artifact request key across runs; corrected with current run identities.
- Next: 11 passed / 9 failed after using the driver's flag on the stage CLI; corrected to `--serial-because`.
- Next: 12 passed / 10 failed: strict fixture JSON included null `supersedes`, and one constraint
  lacked its required source. Optional nulls are omitted and source supplied.
- A diagnostic repeated-defect case failed (0/1): the fixture confused final acceptance Findings with
  repair-investigation Findings. It now distinguishes actual invocation purpose. Subsequent focus: 27/0.
- Broader selection: 311 passed / 4 failed. Two repeated/introduced cases had that same fixture issue;
  two existing inventory cases exposed changed refusal precedence. Original newer/uncertain-run
  gate precedence was preserved, keeping existing assertions intact.
- Next broad run: 317 passed / 1 failed. Archive restart exposed the product's duplicate-closeout
  defect described above. Candidate restoration fixed it; final integrated runs passed.
- Final review found that direct/prepared Review entry also needed the required-check observation before
  blind dispatch. The guard was added. Its first focused run had 23 passes / 2 fixture failures because
  a forward stage transition was given an unsupported reason. Correcting that syntax yielded 2/0;
  both missing and failed outcomes now block resumed Review without dispatch. The final full suites
  include this change.

The default and serialized commands are both required; a serialized pass does not erase a default
failure. A/B's separate historical telemetry failure and all its earlier results remain in
[the A/B record](orchestration-driver-part-6-ab.md#validation-record). No telemetry assertions,
collection deadlines or unrelated logging were weakened, and no tests were skipped.

```sh
dotnet test --nologo
dotnet test --no-build --no-restore --nologo -- xUnit.ParallelizeTestCollections=false
```

## Milestone assessment and limits

The bounded implementation demonstrates positive repair/reuse, actual required checks, independently
refuted incorrect findings, repeated/introduced-defect replanning, explicit acceptance, completed-work
restart and legitimate single-member closeout. Dependent areas and multi-member isolation are tested.
Historical replay and unassociated task-13 behavior remain regression obligations in the full suite.
No essential missing implementation is relabeled as Part 7 measurement work.

The evidence does not establish unrestricted Part 6/live-use readiness. Full lifecycle execution is
fixture-based; real-provider credential/cache compatibility under confinement and user experience
have not been validated. The supported boundary excludes automatic multi-member scheduling,
successor-case budget extension, undeclared input/dependency knowledge, arbitrary external filesystem
writers and non-macOS confined deployments. Those exclusions are explicit refusal/profile boundaries,
not permission to widen grants. Real-provider execution and installation require separate authorization.
No productivity gain is claimed. Final test outcomes above determine the automated validation status;
a live-use endorsement beyond this demonstrated profile remains unestablished.
