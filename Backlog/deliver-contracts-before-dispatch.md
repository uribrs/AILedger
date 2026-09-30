# Deliver the next contract before dispatch

**Backlog ID: 64. Priority: 1. Status: open — phases 1–3 complete; Phase 4 integrated validation passed; user-owned observation pending. Kind: feature.**

Created 2026-09-30; narrowed after review of the preparation protocol and feedback from the closed
Glue task. This item takes priority over [item 63](structured-agent-interface.md). V1 is one concrete
delivery slice, not authorization for the deferred extensions below.

## Authorization and objective

Uri authorized creating and revising this backlog design directly, without the kernel. This edit
creates no governed task, ledger mutation, development agent, implementation, installation or client
trial. The initial exception applied to this backlog/design exercise. The subsequent
[Part 1 request](../docs/handoffs/contract-delivery-part-1.md) explicitly authorized direct-session
Phase 1 implementation and disposable-fixture testing, without kernel governance, installation or
provider episodes. Phase 1 is complete. A subsequent explicit direct-session exception authorized
Phase 2 implementation and disposable-fixture testing under the same restrictions. Its
[implementation and validation record](../docs/contract-delivery-phase-2.md) documents passing focused
and full-suite checks, along with earlier intermittent diagnostic failures. A further explicit
direct-session exception authorized Phase 3 under the same restrictions; its
[implementation and validation record](../docs/contract-delivery-phase-3.md) records completion.
The subsequent Phase 4 request explicitly authorized direct-session integrated validation and
observation preparation under the same restrictions. Integrated engineering validation and observation preparation are complete;
installation was subsequently explicitly authorized and completed as **2.0.186-dirty from 1ba8aa0**;
the real-task observation remains pending. Uri owns the observation
and final judgment. See the [Phase 4 record and observation plan](../docs/contract-delivery-phase-4.md).

Mechanically surface downstream requirements on the ordinary coordinator path for the Glue-derived
handoffs. The coordinator receives the applicable contract **before it prepares and dispatches the
next step**, without remembering an optional lookup or learning requirements through late refusals.
Share definitions with enforcement where needed, and move four demonstrated checks to the earliest
boundary with sufficient information.

V1 combines two distinct improvements:

1. **Proactive delivery:** the preceding response already contains the next contract.
2. **Early validation:** malformed inputs are caught before downstream work or a provider launch.

A refusal that explains the contract is useful recovery, but does not substitute for proactive
delivery. Printing a contract inside a call that immediately starts the next provider is too late.

## Evidence and limits

The medium development task `2026-09-29_1754-collectors-done-double-glue` closed with 21 runs,
76 recorded agent-minutes and 15 journaled refusals. Recon found useful implementation hazards;
retain that scrutiny. Interface failures included a malformed plan discovered during verification,
missing assurance preparation and incompatible reviewer inputs. Final closure explicitly proceeded
without task-13 review/acceptance; it did not demonstrate the complete assurance path.

Sources: [closeout and retrospective](../.ailedger/tasks/2026-09-29_1754-collectors-done-double-glue/events.jsonl),
[refusal journal](../.ailedger/tasks/2026-09-29_1754-collectors-done-double-glue/refusals.jsonl).

Do not treat the feedback estimate of eight avoidable runs and two to three hours saved as measured
savings. Some recon revisions contained substantive findings and research consultation remained
required. Recon refresh and consultation duties were documented. RL3, the oversized-brief attempt,
was recorded **failed**, not completed. Better delivery may improve sequencing; it does not remove
investigation or demonstrate savings before an observed trial.

## V1 coverage

Cover the ordinary coordinator using `context build`, ordinary `provider launch` and its completed
return, and the existing artifact/stage responses between launches. Reuse the shared host code for
Claude and Codex; do not build a new coordinator transport for this feature. A future structured
coordinator adapter can consume the same application result.

The covered sequence is recon/research return into planning, planning into worker dispatch, worker
return into verification, and verifier return into isolated review. Surface relevant repair or
replanning requirements when the existing outcome requires them. Include the applicable stage,
role, output and binding requirements for these handoffs, not the whole kernel manual.

Initial coordinator context includes the imminent contract before the first covered dispatch.
Subsequent run returns and relevant artifact/stage responses refresh that guidance before the
coordinator's next request. If the next branch is undecided, show its small applicable set and what
is missing; do not decide technical acceptance mechanically.

Resume, batch fan-out, arbitrary direct launches that skip coordinator context, reconnect protocols
and all other ingress paths are outside V1's delivery-order claim. Existing enforcement still applies
on those paths. Document this boundary rather than claiming universal delivery. The normal path must
work without a new prepare command, acknowledgment, receipt or extra discovery round trip.

## Identify concrete return boundaries, not a new step engine

Use existing events: a durable run termination, a filed relevant artifact, or a completed stage
transition. They trigger recomputation and delivery. No background model, polling agent, generalized
step identity, new dependency graph or workflow scheduler is needed.

At a run return, inspect its existing status, relevant outputs and current next-action prerequisites.
Keep these meanings separate in the response:

- The process ended: an observation from the existing run record.
- Required outputs are present, missing, or their outcome is unknown: an observation from references
  and available typed results, not proof of technical success.
- The next action's requirements are satisfied, missing or unknown at this observation.

A completed run with a blocked report must not be described as successful work. Use existing
structured assurance/artifact outcomes where available. Do not infer a semantic verdict from a
free-text `BLOCKED` match, zero writes or unchanged files. Where the covered normal path lacks an
explicit outcome, add only a minimal producer return field for declared outcome and existing
output/blocker references through an authorized recording path. Pair it with the affected role's
tool/brief guidance; do not introduce broad recording authority or a universal output schema.
The declaration is attributed self-report, not independent acceptance. Historical ambiguity stays
`unknown`; useful guidance can still be delivered without certifying that the current step succeeded.

Example: "Verifier run ended; required output is missing; review is not ready. Here is the verifier
output contract and the supported recovery." Delivery does not depend on a success verdict. This
identifies and handles the current return without treating `Completed` as "all done."

## Minimal contract and automatic delivery

The packet contains only:

| Field | Content |
|---|---|
| action | Imminent action and applicable actor/subject role |
| requirements | Concrete inputs and prerequisites, with small stable identifiers where needed |
| required shape | Exact relevant fields, columns or a compact authoring template |
| status | Per requirement: satisfied, missing or unknown, with supporting references |
| bindings | Relevant work, candidate, producer/verifier and assurance identifiers |
| source | Authoritative code definition or applicable workflow guidance |

Stamp the observed task version. Reuse existing freshness/admission checks at execution; a delivered
packet is neither authority nor a reservation. Do not introduce contract acknowledgments, dependency
hash receipts or a new launch exit status. Relevant later mutations refresh guidance on the normal
path; a concurrent change can still cause a legitimate refusal at execution.

Keep definitions in the owning components and share applicable schema/predicates with validation.
Extract only what these flows need. Do not build a generic rules language, reflective exception
parser or elaborate registry. Attribute skill-only obligations as guidance rather than pretending
the kernel already enforces them. Surface policy/runtime conflicts and unknowns explicitly.

Inline material is limited to the imminent action and essential prerequisites. Reference larger
templates/supporting material without hiding critical requirements behind a mandatory extra lookup.
Measure packet bytes/tokens from the first fixture and trial. Avoid repeating the complete plan or
manual on each return. The coordinator's packet must not be copied into a blind reviewer's context.

```text
current run ends / relevant artifact or stage operation returns
    -> read existing outcome and next-action requirements
    -> include concise next contract in the coordinator-facing response
    -> return control to coordinator
    -> coordinator prepares and requests next dispatch
    -> existing admission plus early boundary checks
    -> provider starts only if those checks pass
```

Test response/dispatch ordering on this path. This establishes automatic delivery, not comprehension,
and does not prove that every conceivable launch path received a contract.

## Four early checks

| Check | Required boundary and behavior |
|---|---|
| Plan shape | Deliver the plan contract before authoring. Validate newly filed plans against the shared attention-table definition at filing, naming the offending plan. Do not wait for a verifier submission. |
| Brief size | Prepare and size the required role-filtered brief before `run.started` and provider invocation. Reuse actual assembly/budget logic; do not rely on a smaller approximation or omit required inputs. |
| Reviewer binding and compatibility | Before launch, check frozen candidate/verifier bindings and compatibility of the requested review with its context and assurance profile. Supply configured area/check identifiers needed to discover inputs. Diagnose missing or incompatible setup; do not invent it or widen a blind review. |
| Assurance grants | Before launch, check configured assurance inputs against existing provider grants and protected-ledger restrictions. Report the correction without expanding authority automatically. |

Share checks with the real execution path. If final briefing currently requires an active run,
extract only the read-only preparation needed to validate the prospective launch; do not record a
fictitious run to discover the contract. Recheck mutable inputs at use and preserve authorization,
role filtering, candidate binding and historical replay. Newly enforced plan shapes apply
prospectively: legacy artifacts remain readable and receive a precise diagnosis when selected.

Moving checks earlier does not make task-13 assurance and blind review inherently compatible,
provide Elastic access, or make Jest executable in a sandbox. Report those limits; their repair is
not absorbed into this item. Expose unsatisfied setup before paid dispatch.

## Implementation seams and bounded increments

Existing building blocks:

- [StageTransitionPolicy](../src/AILedger.Core/Stages/Logic/StageTransitionPolicy.cs),
  [StagePrerequisiteRules](../src/AILedger.Core/Stages/Logic/StagePrerequisiteRules.cs) and
  [readiness](../src/AILedger.Storage/Inspection/FileGovernedTaskService.Readiness.cs): select and
  evaluate covered requirements; a legal edge alone is not readiness.
- [VerifierOutputRules](../src/AILedger.Core/Artifacts/Logic/VerifierOutputRules.cs) and
  [InternalReconDocuments](../src/AILedger.Core/Artifacts/Logic/InternalReconDocuments.cs): shared
  document shapes and an existing state-specific authoring-template precedent.
- [ProviderLauncher](../src/AILedger.Cli/Providers/ProviderLauncher.cs),
  [ProviderLaunchPreflight](../src/AILedger.Core/Runs/Logic/ProviderLaunchPreflight.cs) and
  [assurance setup](../src/AILedger.Cli/Assurance/ProviderLauncher.Assurance.cs): earlier checks and
  the ordinary coordinator return. Reuse them rather than introducing a launch protocol.
- [CliCommandExecutor](../src/AILedger.Cli/Routing/CliCommandExecutor.cs) and coordinator context:
  deliver the packet in relevant existing responses. Preserve existing fields and launch outcomes.
- [Coordinator guidance](../cognitive/skills/workflow-coordinator/SKILL.md): align covered procedural
  obligations and minimal outcome reporting. Global skill installation needs separate rollout authority.

## Implementation phases and completion criteria

These are four delivery phases of backlog item 64, **not new kernel workflow stages**. Each phase
includes its own appropriate tests. Phase 4 adds integrated validation and real-task observation;
it is not permission to postpone testing phases 1–3. Phases 1–3 are complete; Phase 4 integrated validation has passed, installation is complete, while user-owned real-task observation remains pending.

### Phase 1 — Shared contract definitions and plan validation

**Status: complete (2026-09-30).** Shared attention contract, prospective plan filing validation,
historical replay/repair diagnostics and bounded inventory implemented. Focused checks passed
47/47; full .NET suite passed 2,306 main + 99 memory tests, with no skips. See the linked validation
record below for the initial sandbox limit and corrected test fixture.

**Entry:** this V1 scope and its four early checks. Read the current source before choosing changes.

**Work:** map the covered recon/research → planning → worker → verifier → reviewer handoffs to their
actual rules and guidance. Record inputs, outputs, relevant bindings, ownership and known gaps. Extract
only the minimal shared definitions needed by V1, beginning with the plan attention-table contract.
Expose its authoring shape for phase 2 and make both producer filing and verifier consumption use
the same definition. Enforce new-plan shape at command time, with an upstream-plan diagnostic.

**Complete when:** the covered-flow inventory names authoritative sources and policy/runtime gaps;
the plan's required shape has one shared definition; valid plans remain accepted and malformed new
plans fail at filing before downstream verification; historical plans still replay; focused and full
.NET checks pass. No claim of automatic delivery or reduced coordination effort is made at this point.

[Phase 1 contract inventory, compatibility boundary and validation](../docs/contract-delivery-phase-1.md).

**Boundary:** no coordinator delivery wiring, new producer outcome declaration, launch-preparation
changes, receipt protocol or generalized contract framework. Do not refactor every prerequisite just
to build the inventory. [Part 1 implementation prompt](../docs/handoffs/contract-delivery-part-1.md).

### Phase 2 — Automatic delivery and minimal outcome reporting

**Status: complete (2026-09-30).** Automatic coordinator-response delivery, producer declarations
and role guidance are implemented. Focused checks passed **81/81**; the final sequential full run
passed **2,334 main + 99 memory tests**, with zero failures or skips. Normal measured packets are
below 8 KiB, with an explicit defensive 64 KiB bound. No assertions or diagnostic deadlines were
weakened. Earlier intermittent diagnostic-count failures and the interrupted run remain documented;
their cause is not claimed resolved. See the [implementation and validation record](../docs/contract-delivery-phase-2.md#validation).

**Entry:** phase 1's shared definitions, inventory and validated producer check.

**Work:** wire the imminent contract into initial coordinator context and concrete run/artifact/stage
return responses on the covered ordinary path. Use existing outcomes first; add only the missing
producer return declaration needed for these handoffs, together with its role/tool guidance.
Keep process termination, available output and next-action readiness distinct.

**Complete when:** fixtures show the coordinator receives the appropriate contract in a completed
response before its next launch request, without an optional lookup or acknowledgment; blocked and
missing outcomes remain honest; packet size is measured; reviewer isolation is preserved. Applicable
focused and full tests pass. The phase does not claim every ingress path is covered.

**Boundary:** no generalized step identity, parallel completion engine, separate coordinator transport,
durable preparation receipt or new launch exit status.

### Phase 3 — Early launch checks

**Status: complete (2026-09-30).** Ordinary fresh launch prepares the real role-filtered brief,
reviewer bindings/profile compatibility and configured assurance inputs/grants before version
probing and `run.started`, while retaining admission and mutable-input checks at use. Focused
checks passed **251 + 10** tests; the full sequential suite passed **2,392 main + 99 memory**,
with zero failures, skips or runner errors. Both builds had zero warnings/errors.
See the [Phase 3 record](../docs/contract-delivery-phase-3.md) for exact coverage and limitations.
Resume/batch/reconnect coverage, compatible requirements-aware blind review, installation and
observed real-task savings are not claimed.

**Entry:** shared definitions and normal-path contract delivery from phases 1–2.

**Work:** check required brief size, reviewer bindings/context/profile compatibility and assurance
input grants before run start and provider invocation. Reuse actual host preparation and validation,
including configured discovery identifiers; expose unsatisfied setup without inventing configuration.

**Complete when:** fixtures for both provider host paths demonstrate these failures occur before
`run.started` or provider invocation; valid launches retain existing behavior; actual execution still
checks changed authority/bindings; reviewer isolation and historical replay remain intact. Applicable
focused and full tests pass.

**Boundary:** no launch transactionality/crash-recovery redesign, sandbox repair, automatic grant
expansion or universal launch/resume/batch/reconnect unification.

### Phase 4 — Integrated validation and observed use

**Status (2026-09-30): open — integrated validation and observation preparation complete;
user-owned observation pending.** Focused validation passed **278 tests**; the required full
sequential suite passed **2,398 main + 99 memory tests**, with zero failures, skips or runner errors.
Both builds had zero warnings/errors. Six new cases cover the positive external-research/recon-refresh flow
with both provider assignments, actual response/probe/execution ordering, exact delivered briefs,
plan revision, reviewer retrieval isolation and cancellation. No production change was required.
The [Phase 4 record](../docs/contract-delivery-phase-4.md) includes evidence mapping, current packet
sizes, preserved fixture failures, pinned source/build identities and a concrete observation plan.
Installation is complete as **2.0.186-dirty from 1ba8aa0**; the installed smoke passed.
User-owned real-task observation has not run. Repository role skills are current; no global skill
update is required. No measured benefit is claimed.

**Entry:** phases 1–3 implemented and individually validated.

**Work:** prove the complete normal-path ordering and all V1 acceptance cases below in disposable
fixtures. When separately authorized, install the exact verified version and observe a user-chosen
medium development task. Compare all exchanges, early/late failures, unusable launches and packet
cost, alongside substantive findings and assurance completion.

**Complete when:** integrated checks pass and the real-task observation is recorded with its limits.
If installation or an ordinary task has not been authorized/run, report implementation validated and
observation pending; do not mark the observation complete or invent savings. No extra billable model
experiment is authorized by this design.

The four phases together constitute V1. Boundary checks alone do not complete automatic delivery.

## Acceptance and measurement

Required fixtures, using stub providers and historical inputs copied outside the live ledger:

- The ordinary coordinator receives the applicable contract in the preceding completed response,
  before its next launch request, without an optional inspection call. Cover initial context,
  planning, worker and verifier returns, and relevant stage/artifact changes between them.
- Plan requirements arrive before authoring; a malformed new plan fails at filing with a precise
  diagnostic. The verifier consumes the same definition. Old histories still replay.
- Required brief overflow, missing/incompatible reviewer bindings or setup, and out-of-grant assurance
  inputs are diagnosed before run start/provider invocation. Both adapters use the same host checks.
- Completed blocked reports, missing outputs and failed runs with partial findings yield honest
  guidance. Missing typed outcomes remain unknown. No process status becomes technical acceptance.
- Claim/consultation obligations, independent assurance and reviewer exclusions remain intact.
  Packets cannot leak verifier narratives into blind review or grant missing capabilities.
- Execution revalidates changed authority, bindings and prerequisites. Guidance is not a promise that
  a later command will succeed; unknown host capabilities remain unknown.
- Packet size is measured and bounded with critical information present. Coverage is explicit;
  tests do not imply a receipt protocol or universal ingress guarantee.

Run focused tests and the full .NET suite during implementation, not for this backlog revision.
Measure total coordinator exchanges, early and late refusals, repeated unmet requirements, unusable
dispatches, time from return to valid next dispatch, packet size and available usage. Count early
failures in total friction: shifting a refusal earlier is not eliminating it. Preserve substantive
findings and assurance completion as outcome checks. Label counterfactuals and absent coordinator
cost; do not claim the estimated eight runs or hours as demonstrated savings.

## Deferred extensions and related work

These are **not V1 acceptance requirements** and need separate scope/authorization if pursued:

- Durable preparation receipts, acknowledgment/freshness handshakes and new CLI preparation statuses.
- Generalized step identities, preparation envelopes, multi-run/parallel completion aggregation and
  a comprehensive outcome state machine.
- Launch idempotency, duplicate-dispatch prevention and uncertain-process-start/crash recovery:
  separate launch-reliability work, not a prerequisite of contract delivery. Preserve current behavior.
- Universal launch/resume/batch/reconnect coverage, a new structured coordinator transport, and
  complete contract indexing across every command.
- A cognitive whisperer; revisit only if observed failures require interpretation mechanical delivery
  cannot provide. No automatic technical planning, risk acceptance or waiver authority is added.
- Requalifying verification after a blocked, no-change worker. Unchanged bytes alone are insufficient:
  requirements, claims, evidence and authority may have changed. Keep current freshness rules until
  separately scoped safe reuse is designed; V1 reports the requirement rather than waiving it.

Related entries retain their own status: **63** for interface trials/evaluation; **57** for general
refusal identities; **47/48** for broader producer-output/review-outcome semantics; **26/55** for wider
isolation/action enforcement; **28/38/58** for authentication, sandbox capabilities and failed-run
recovery. None is automatically closed by this narrower delivery slice.
