# Orchestration driver: scope and delivery parts

Status: Part 1 complete; Part 2 implemented for macOS with documented platform/provider limits.
Parts 3–5 shared dispatch, bounded driver, governed coordination and durable recovery are implemented
for the documented fixture/deployment profiles. Part 6 implements bounded acceptance, repair and
completion/closeout recovery. Part 7 adds startup diagnostic retention, infrastructure stops, behavioral
regressions and adoption observations; see [its delivery record](orchestration-driver-part-7.md).
Authenticated live-provider lifecycle use and measured adoption benefit remain unestablished. Validation
and limits of implemented parts are recorded in each delivery document.
Base: main at 24d724fb91b8ffa69c3201716e8e1f91cc08e260.
Current checkout branch: codex/orchestration-review; Parts 1–6 originated on codex/orchestration-driver.

## Objective

Replace conversational coordination of the governed workflow with a .NET execution driver.
Keep documentation, claims, evidence, recon, research, lessons, planning, independent verification,
review, scoped agent execution and multiple providers. Reduce routine user intervention without
abandoning unresolved defects or moving orchestration chores onto the user.

The target includes consequential decisions before and between dispatches. Reading the brief is one
symptom: the system must also prevent an outer coordinator from silently settling intent, skipping
investigation, inventing approval, dismissing findings or treating a refusal as permission to change
the workflow. Relevant judgment belongs to bounded, accountable roles, with evidence and explicit
unresolved states. Structural enforcement does not prove judgment quality; both need validation.

This change is being developed directly at the user's request, outside the governed task workflow.
That development choice does not weaken the governance of tasks executed by the resulting product.

## Boundaries

- Core owns domain rules and admission. It must not depend on the driver or provider implementations.
- The driver owns scheduling, waiting, recovery and invoking existing governed operations.
- Agents own bounded cognitive work, including planning and evidence-based finding disposition.
- The execution host owns trusted identity, tool grants, context delivery and provider processes.
- The ledger remains authoritative for task facts and acceptance. Driver persistence records
  execution coordination only; it cannot create competing task truth.
- Routine valid actions remain automatic. Business choices and exceptional authority remain with
  the user through a trusted interface, not by asking the model to represent user approval.

## Confirmed integration surfaces

- `src/AILedger.Core/ContextBriefing/Delivery/NextActionContract.cs` explicitly describes an
  observation, not authority, acceptance, a reservation or a freshness guarantee.
- `NextActionContracts.cs` offers multiple explanatory actions and deliberately leaves dispatch
  admission unknown. Its text and ordering must not become an executable command protocol.
- `src/AILedger.Core/Runs/Logic/ProviderLaunchPreflight.cs` delegates to existing authorization,
  entry-stage and run-admission rules. Preserve these owning rules rather than copying them.
- `src/AILedger.Cli/Providers/ProviderLauncher.cs` already handles admission, brief preparation,
  assurance configuration, provider invocation and recording. Extract reusable seams carefully.
- `src/AILedger.Core/Domain/AuthorizationPolicy.cs` currently identifies actors through task role
  assignments. Adding a coordinator role alone would not authenticate the human or prevent a
  process with unrestricted shell/filesystem access from choosing another actor.
- `src/AILedger.Core/WorkItems/Verification/WorkItemVerificationRules.cs` separates completed runs,
  chronology and provider independence. Inventory the complete acceptance path before changing it;
  do not assume historical telemetry describes every current assurance path.

## Delivery parts

Each part should be independently reviewable with its own behavioral tests. These are implementation
boundaries, not a request for parallel agents or separate user-owned tasks.

### 1. Contract and lifecycle inventory

Scope: orchestration design and tests characterizing current admission, outcomes and replay.
Map every workflow stage, legal transition, required artifact, supported role and recovery outcome.
Define typed proposed actions, state-version bindings, results and explicit unsupported cases.
Distinguish deterministic transitions from decisions needing an agent or the user.

Exit: the full target lifecycle and an initial executable slice have explicit coverage; no prose
parsing, implicit role widening or automatic interpretation of unknown outcomes as success.

Completed: [Part 1 inventory and contracts](orchestration-driver-part-1.md), including all stages,
roles and artifacts, responsibility boundaries, proposed typed contracts, initial slice, recovery
matrix and precise later-part handoffs. Four new characterization cases and 506 focused tests pass;
full `dotnet test` passes all 2,502 tests. Current acceptance and interrupted-work gaps are documented
for Parts 5/6; no production behavior changed. No unresolved decision prevents Parts 2/3 from starting.

### 2. Trusted orchestration authority

Scope: host bindings, role/capability rules and authorization tests.
Give the driver the narrow authority required for routine operations. Bind agent identity through
trusted sessions. Separate exceptional authorization from model-authored text and caller-supplied
actor names. Define how the conversational interface is prevented from invoking privileged paths.
Preserve authorized human use without requiring human approval for every routine action.

Exit: an agent cannot self-issue a waiver, reassign itself authority or masquerade as the operator
through an alternative exposed path. Document the actual OS/process trust boundary and its limits.

Implemented: [Part 2 authority boundary](orchestration-driver-part-2.md). Adds task-bound routine
host authority, expiring agent-session ceilings, protected acceptance and an inherited macOS OS
sandbox covering direct CLI/shell/filesystem bypasses. The local OS owner remains trusted. Other
platforms refuse confined launch; live provider authentication/cache compatibility remains untested.
Unsupported cognitive operations require a typed host handoff in Parts 3/4, not unrestricted child
CLI access. No scheduler or acceptance-correctness change is included. Part 3 can start.
Validation: all 2,512 tests pass with serialized collections; the default concurrent full run has
one intermittent telemetry row-count failure, documented with its unchanged focused rerun.

### 3. Reusable governed dispatch

Scope: launch application service, existing CLI integration and launch tests.
Separate CLI argument parsing from launch execution while preserving preflight ordering, current
briefs, scope grants, assurance setup, reviewer isolation, provider independence and run receipts.
Keep the current CLI using the same service; avoid a second provider-launch implementation.

Exit: ordinary CLI and driver calls exercise the same admission and launch behavior. Refused
requests do not start providers, and existing provider integration tests remain green.

Implemented: [Part 3 shared dispatch and handoff](orchestration-driver-part-3.md). The CLI and direct
host callers share typed launch execution and receipts. Routine mutations remain restricted while
trusted host preparation supplies bounded subject context, recording and assurance. Typed cognitive
handoff contracts explicitly report unavailable support for Part 4; no driver, durable recovery or
automatic work-completion policy is included. Final default and serialized full runs each passed
all 2,540 tests; an earlier default run had two diagnostic failures, preserved in the delivery record.

### 4. Driver and decision handoffs

Scope: a cohesive orchestration component, CLI entry point and deterministic workflow tests.
Start with explicitly prepared tasks and advance through implementation, verification, review,
repair and completion using existing rules. Dispatch evidence interpretation and replanning to
bounded roles. Then extend coverage through discovery, recon, research, planning and closeout.
Expose supported coverage explicitly while those later stages are being added.

Exit: complete and repair paths run without manual routing; unresolved judgment reaches the
appropriate role; genuine user decisions are surfaced with their supporting record. Missing
configuration or unsupported scope is explicit and never silently skipped.

Implemented: [Part 4 driver and live handoffs](orchestration-driver-part-4.md). Adds restricted
routine CLI routing, candidate-bound verification/blind review, evidence-backed repair and judgment,
live governing artifacts and role-filtered lessons, plus bounded discovery/research/planning/closeout.
New scope/role preparation remains a trusted boundary. Every code path stops before independent
requirements-aware assurance acceptance and automatic work completion. This is a fixture-only,
in-memory driver pending Parts 5/6; no unattended real-work readiness is claimed.

### 5. Governed coordination, durable ownership and recovery

Scope: extend Part 4's actual delivered coverage, trusted intake and cognitive handoffs, driver
persistence, task ownership, cancellation and recovery. Audit Part 4's final delivery record first;
reuse its working paths and identify the remaining gaps rather than implementing a second router.
This is an explicit expansion of the original recovery-only part. Deliver the following boundaries
in independently reviewable increments, with tests for each before assembling the complete path.

Implemented: [Part 5 delivery record](orchestration-driver-part-5.md) documents trusted intake,
bounded decision/preparation paths, the macOS confined coordinator profile, durable ownership and
receipt reconciliation, refusal recovery, validation and hard-crash limits. Part 6 remains required
for automatic acceptance/completion and the supported live-use milestone.

#### 5A. Decision ownership from intake through closeout

Preserve the user's actual request and constraints through a trusted intake path. The conversational
interface may explain, brainstorm and submit proposals; its interpretation must not silently become
an approved goal, scope change, accepted decision or waiver. Host-bound provenance must distinguish
user input from agent-authored interpretation. Do not require the user to approve every routine
technical decision: route those decisions to the existing authorized roles and acceptance paths.

Cover the following decisions explicitly. A supported workflow must carry the whole chain, including
the decisions before the first implementation dispatch. A prepared-task entry remains useful, but
must validate its preparation and must not be advertised as covering intake it did not execute.

| Decision or action | Responsible path | Required basis before dependent work proceeds |
|---|---|---|
| Interpret intent and identify assumptions | Bounded discovery/lead role, grounded in trusted user input | Original request, constraints, recorded assumptions and unresolved questions; a model summary cannot replace the request. |
| Establish current behavior and investigate unknowns | Lead recon, Researcher where applicable, existing lesson consultations | Current claims and source evidence, relevant research, lesson applicability or evidenced rejection; retrieval alone does not settle a claim. |
| Choose design, scope and acceptance criteria | Authorized planning role and existing decision admission | Applicable recon/research/lessons, alternatives, dependencies and current governing artifacts; disputed claims remain visible. |
| Select roles, profiles and work; move stages | Driver through existing kernel/host operations | Admitted plan, preauthorized profiles, current state, scope and grants; the driver cannot invent missing decisions. |
| Interpret findings, choose repairs or replan | Bounded findings/planning roles through governed handoffs | Findings and supporting/refuting evidence, affected requirements and areas, outstanding defects; Part 6 owns disposition and acceptance correctness. |
| Diagnose a refusal or stalled execution | Typed recovery where known; bounded investigation where judgment is needed | Refused action, current brief/state, owning diagnostic and evidence of the attempted approach. |
| Synthesize closeout and lessons | Existing authorized closeout roles and kernel admission | Accepted work, retained limitations and evidence; an outcome summary cannot erase unresolved work or manufacture a reusable lesson. |

For every consequential handoff, retain the producing run/role, actual evidence and governing
artifact references, applicable task/work/candidate basis, unresolved prerequisites and disposition.
Use existing records where they express this; add narrow typed contracts only for demonstrated gaps.
The driver checks identity, authority, applicability and prerequisites through their owning services.
It routes semantic uncertainty to bounded judgment; neither valid JSON nor a completed role run proves
that a conclusion is sound. A generic routing assessment must not override conflicting findings or
substitute for the decision, evidence or acceptance record required by that action.

Brief consumption is one instance of this rule. A delivery hash or an acknowledgment that the brief
was read is not proof of understanding. Exercise contradictory, missing and stale prerequisites and
show that they change the resulting action or block it. Do not create a new summary ritual for the
user to police. Continue using typed contracts and kernel admission, never NextActionContract prose,
array order or readiness observations as an executable or authorizing protocol.

Close the authority boundary for the outer interface as well as provider children. Part 2 confines
children but trusts the local OS owner; it does not automatically confine an unrelated conversational
session with owner-level shell or filesystem access. Define and test a supported deployment in which
model-controlled entry points reach only bounded proposal/routine APIs and cannot invoke human host
composition, privileged CLI operations or direct protected writes. Reuse the existing isolation and
host boundaries. If the surrounding application cannot enforce this, declare that profile unsupported
for the coordinator-authority guarantee; a coordinator role name or written instruction is insufficient.
No broad UI redesign is required, and host/platform limitations must remain explicit. At least one
enforceable deployment profile must pass this boundary to complete 5A; declaring every profile
unsupported leaves the coordination objective unfinished.

Exit for 5A: routine engineering judgment and routing continue through governed roles without the
user reading every brief, approving each technical decision or supplying commands. Missing decisions
cannot be supplied by outer-interface prose. Business choices, exceptional authority and evidenced
external unknowns still reach the user through the trusted path. Unsupported tooling/configuration
is an explicit operational block, not a fabricated business decision or silent fallback.

#### 5B. Durable ownership and decision continuity

Ensure one routing owner per task, with durable ownership epochs and protection against a stale owner
resuming after takeover. Persist dispatch identity and intent, reconcile with actual ledger runs,
and handle concurrent changes. Recover across crashes before dispatch, after run creation, during
provider execution, after result retention and after ledger closure. An expired lease alone does
not prove the previous provider or check stopped.

Preserve cognitive handoff identity and acknowledged results across restart as well as dispatch
identity. Reconcile an uncertain write using its original request key, body and authenticated binding;
do not create a second decision, artifact or run to make uncertainty disappear. Revalidate the current
basis before acting on an earlier judgment or delayed reply. The ledger remains the authority for
judgments and acceptance; the recovery journal stores execution coordination and references only.
Keep uncertain outcomes uncertain until reconciled. Do not blindly relaunch a provider or rerun an
interrupted task-13 check; retain the existing trusted stop/reconciliation and new-attempt requirements.

Exit for 5B: crash and race tests demonstrate no duplicate dispatch or cognitive mutation in supported
recovery paths, no false completion, preservation of partial evidence and safe refusal of concurrent
or obsolete owners. Recovery cannot change decision authority or reuse a stale judgment to advance.

#### 5C. Refusal and stalled-work recovery

On refusal, retain the attempted action and owning diagnostic, refresh the relevant brief and state,
and use the supported recovery path. Repair known unmet prerequisites automatically when authorized;
route ambiguous causes to a bounded investigation. Establish whether the prescribed approach was
followed before classifying a kernel defect. An opaque refusal is unresolved diagnosis, not proof
that governance is broken and not permission to waive or weaken it.

An unchanged refusal must not produce repeated identical dispatches. Detect lack of progress and
retain its evidence; investigate or replan within the authorized workflow. Operational cancellation,
budget or capacity limits may pause execution with work explicitly unresolved. They cannot dispose
of defects or create acceptance. A repair-induced defect normally stays engineering work; the user
is involved only when a genuine business/authority decision or investigated external unknown arises.

Exit for Part 5: 5A, 5B and 5C work together, including recovery of a pending judgment. Unsupported
coverage remains explicit. This part provides durable governed coordination; technical acceptance
still requires Part 6. Part 7 broadens regression coverage and measures the experience in use.

### 6. Evidence-based acceptance and repair correctness

Scope: completion/assurance contracts, finding disposition, repair impact and compatibility tests.
Use Part 5's decision provenance and Part 4's handoffs. Audit and close current completion gaps through
applicable structured evidence and explicit acceptance. Keep process completion, artifact presence,
routing assessments, findings, technical acceptance and work completion as distinct facts. Consume
the owning structured contracts; do not route by parsing verdict words from report prose.

Delivery split: **6A + 6B are implemented for the bounded profile in the
[A+B delivery record](orchestration-driver-part-6-ab.md). 6C + 6D now have bounded implementation,
assembled fixture validation and operating instructions in the
[C+D delivery record](orchestration-driver-part-6-cd.md).** The [A+B implementation prompt](orchestration-driver-part-6-ab-prompt.txt)
authorizes direct development outside the kernel workflow; the resulting product must still enforce
its kernel, authority and assurance rules. Part 6 as a whole remains pending until all four exits
are met. Each increment must include its own focused, negative, integration and compatibility tests;
6D does not defer validation needed to establish 6A or 6B correctness.

#### 6A. Acceptance bridge and completion admission — bounded implementation delivered

- Bind acceptance to the actual current candidate, work members, requirements and policy. Associate
  task-13 area evidence/acceptance with governed candidate and work provenance explicitly. Preserve
  independent verification, blind governed review and the separate requirements-aware assurance path.
  Missing configuration, incompatible context or unsupported scope remains unresolved; no legacy
  completion path may substitute for missing assurance.
- Permit completion only through the owning explicit acceptance and existing completion gates.
  A completed verifier with FAIL, NEVER-TESTED, unknown or stale evidence cannot establish acceptance.
  Honest negative evidence must remain recordable. Recommendations to skip a stage, weaken a check
  or change workflow policy are proposals outside routine repair authority, never recovery commands.
- Consume current authorized evidence and revalidate applicability at the acceptance/completion
  boundary. Recovered receipts and routing judgments are not fresh acceptance. Preserve historical
  legal replay while tightening new command-time admission through the owning contracts.

Exit for 6A: a supported, explicitly accepted candidate can complete through the owning governed path;
missing, negative, stale, mismatched or incomplete acceptance cannot. Any unresolved repair-impact or
physical-state prerequisite still blocks completion; unsupported repair or reconciliation cannot become a bypass.

#### 6B. Evidence-based finding disposition and adjudication — bounded implementation delivered

- Validate findings before deciding to repair, refute or retain them. Use reproduction or a supported
  source trace, with uncertainties and affected requirements recorded. Preserve the original finding,
  the disposition, its evidence and the authorized producer. A repair agent's assertion cannot accept
  its own repair or erase an independent finding. Disagreement follows the supported challenge and
  adjudication path; an outer coordinator cannot resolve it by rewriting the brief.
- Keep every unresolved material defect in the repair/replanning path. Do not cap repairs to force
  closure, send routine repair decisions to the user, or blindly patch every reviewer assertion.
  No automatic dismissal of all low-severity findings: any permitted residual risk requires evidenced
  disposition under the applicable acceptance policy. Findings cannot be erased by superseding a report.
- Integrate disposition with 6A admission and the existing bounded Findings/repair/replanning paths.
  Generic Proceed, report shape and a completed role run cannot replace an applicable disposition.

Exit for 6B: independently raised findings can be upheld, evidenced as refuted, or otherwise disposed
under the applicable policy and authorized role; unresolved or unsupported dispositions block
acceptance. Demonstrate legitimate progress as well as refusal, without self-acceptance by repairers.

The historical A+B delivery used conservative full-basis invalidation and a single selected work member with explicit
acceptance for every declared area. The delivery record includes integration and historical-replay
coverage and concrete [6C/6D handoff items](orchestration-driver-part-6-ab.md#concrete-6c--6d-handoff),
including repair-impact reuse, interrupted physical-state reconciliation, multi-member lifecycle and
recovery between committed work completion and the Learn transition. Overall live-use readiness
remains limited to demonstrated profiles. The [C+D record](orchestration-driver-part-6-cd.md) supersedes
those handoffs with selective review reuse, required checks, positive reconciliation and completion/closeout
restart; it records actual validation and remaining deployment limits.

#### 6C. Repair impact, required checks and physical-state reconciliation — bounded implementation delivered

- Have the authorized role assess repair impact, dependencies and required checks. Ensure the
  declared candidate checks have run and their actual outcomes are available before launching
  downstream assurance that requires them. Compilation alone cannot stand in for required tests.
  Define applicable checks from the contract and affected scope; do not invent a universal line-count
  exemption or require every possible suite for every change. Preserve a failing candidate when a
  bounded diagnostic run is warranted, without labeling that run as release-ready assurance.
- Reconcile physical candidate state after failed, cancelled or interrupted work. Invalidate evidence
  affected by changes, even when the modifying run never completed. Reuse unaffected evidence only
  when applicability is established; neither discard everything nor assume everything remains valid.
- Repeated defect classes and repair-induced failures must inform bounded root-cause investigation,
  scope/design reconsideration and lesson consultation. Reuse 6B's evidenced disposition and Part 5's
  recovery paths; routine engineering decisions remain with authorized roles.

Exit for 6C: applicable required checks precede dependent assurance, interrupted changes cannot reuse
stale acceptance, and unaffected evidence is reused only with established applicability. Repairs
continue or pause explicitly with unresolved work retained; no arbitrary repair cap forces closure.

#### 6D. Assembled lifecycle, compatibility and live-use validation — bounded fixture validation delivered

- Exercise the complete 6A–6C workflow with Part 5: trusted intake, independent assurance, disputed
  findings, repair, interruption/restart, explicit acceptance, completion and closeout. Include
  prepared-task entry and resumed execution, not only a happy-path fixture.
- Run critical negative authority, candidate/requirement/policy applicability, unknown-result,
  stale-evidence, interrupted-check and acceptance-bypass cases through the supported built CLI.
  Preserve historical legal replay and the independent/blind review boundaries in regression tests.
- Run focused and default full tests plus the additional serialized check, report actual failures,
  and deliver supported setup/operating instructions with explicit limitations. Do not defer essential
  integration or missing core behavior to Part 7. Real providers, installation and live use still
  require separate user authorization.

Exit for 6D: the assembled supported profile demonstrates both safe refusal and legitimate completion,
with operating instructions and compatibility evidence. Only then assess the Parts 5+6 live-use
milestone; completing 6A+B alone does not establish unattended real-work readiness.

Exit: failed, unknown, stale or incomplete assurance cannot become accepted work through any supported
automatic path. Legitimate finding disposition remains possible with evidence and authorized judgment;
repair continues until the applicable acceptance conditions are met or execution is explicitly paused
with unresolved work retained. Historical valid logs still replay. Tighten new command-time admission
without retroactively requiring fields or prerequisites absent from older legal events. This part is
a prerequisite for automatic work completion in Part 4; a happy-path driver run is not its substitute.

Live-use milestone: Parts 5 and 6 must deliver a usable supported workflow, including the decision
boundaries, recovery and acceptance behavior above. Before declaring that milestone complete, run
required automated checks and integrated built-CLI checks for that profile, including critical
negative authority, recovery and acceptance cases. Deliver its setup and operating instructions.
Essential integration or missing core behavior cannot be deferred to Part 7. After this milestone,
the user can use the supported setup on real work and assess the difference directly; actual provider
use and installation remain subject to the user's authorization, not completion of Part 7.

### 7. Behavioral integration, telemetry and adoption

Implemented candidate behavior, regression results, the Claude startup investigation and remaining
live-use limits are recorded in [Part 7](orchestration-driver-part-7.md). Fixture coverage does not
close the authenticated live-use or measured-benefit milestone.

Scope: end-to-end fixtures, authority and failure scenarios, driver telemetry, usage documentation
and packaging validation. Extend the existing integration part rather than add another delivery part.
This work can proceed alongside live use after Parts 5-6. It expands regression coverage, investigates
problems exposed by use and measures the improvement; it is not a gate to first live use or the part
that finally implements the promised coordination behavior.
The user-supplied validity report motivates these scenarios; its historical counts are not audited
results for this branch and do not prove the comparative benefit of a proposed replacement.

Test the assembled supported lifecycle from trusted intake through closeout, as well as prepared-task
entry and resume. Include the following negative cases, asserting the admitted actions, durable
records and user-intervention boundary rather than just process exit or a printed status:

| Scenario | Required behavior |
|---|---|
| Outer interface supplies an altered goal, unsupported scope interpretation, self-issued waiver or alleged user approval | Preserve the actual request and route unresolved interpretation; refuse forged authority through every exposed path in the supported deployment. |
| Brief is delivered but its prerequisite is ignored; recon is stale, research unresolved or an applicable lesson conflicts with the plan | No dependent forward action; refresh or dispatch the appropriate investigation/planning role. Show the evidence changes the plan or receives an authorized, evidenced disposition. |
| Planning output is well formed but contradicts a settled constraint or lacks a required decision | Shape/run success alone cannot advance the workflow; preserve the conflict and route its resolution without asking the user to police the artifact. |
| Dispatch is refused and the coordinator proposes retrying unchanged or weakening a kernel rule | Retain the refusal and diagnose prescribed use; recover through the supported path without a waiver, repeated blind dispatch or automatic kernel modification. |
| Reviewer finding is disputed or false; a repair creates another defect or repeats a defect class | Investigate and retain evidence-based disposition; route necessary repair/replanning without an arbitrary repair cap or routine human approval. |
| A required candidate test failed, verifier said NEVER-TESTED, or a routing assessment says proceed despite unresolved findings | Preserve the negative evidence and prevent technical acceptance and automatic completion. |
| Crash, duplicate request, delayed judgment, concurrent owner, lost write acknowledgment or interrupted check | Reconcile identities, ownership, physical state and applicability; no duplicate side effect, stale decision reuse or automatic rerun of an uncertain check. |
| Candidate changes after verification or during a failed repair | Prior acceptance cannot complete changed work; establish applicability and obtain the required fresh evidence. |
| Genuine business decision or external unknown remains after investigation | Present the supporting record and options where applicable, await trusted resolution, then resume without requiring manual routing. |
| Work is described as finished while members, findings or acceptance remain unresolved | Report the actual remaining work; neither a summary nor task-stage progress establishes completion or closeout. |

Test multiple providers through existing adapters, including unavailable providers and cancellation.
Exercise the real authority boundary with confined fixture processes, old histories, and the actual
built CLI. Fake provider declarations alone do not demonstrate confinement or semantic judgment
quality. Full `dotnet test` and integrated CLI checks must pass; record limitations and observed
failures honestly, including unsupported host/provider combinations.

Measure user corrections, manual routing steps, routine engineering escalations, genuine user
choices, refusals by cause, repeated dispatch, repair-induced findings, recovery outcomes and missed
or invalid acceptance. Record observed user attention/time and provider usage/cost where available;
otherwise leave them unknown. Tool-call percentages and lesson citation counts do not by themselves
measure time saved or decision quality. Preserve links from observations to the underlying runs and
records so reductions cannot be manufactured by skipping checks or leaving work unresolved.

Development exit: expanded regression fixtures and telemetry pass, and the delivery record states
exactly which lifecycle and deployment profiles were exercised. Report material defects found during
live use and their resolution; an unresolved defect limits the affected profile regardless of part
number.

Evaluation: use observations from authorized real work to assess human intervention and delivered
correctness together. A separate controlled comparison, if needed for broader value claims, should
state its scenarios, quality criteria and baseline. It is not a prerequisite for using the product or
for the user to notice improvement. Do not infer that review alone supplies all value, that planning
or lessons are dispensable, or that fewer refusals means better compliance. Additional provider trials
require explicit spend authorization. Keep measured results distinct from expected benefits; fixture
success alone does not prove real-world benefit.

## Dependency order and release boundaries

1 precedes 2 and 3; both precede driver integration. Part 4 delivery and its limits are recorded
above. Parts 5-7 are revised follow-on requirements, not a request to rewrite Part 4.
Part 5 begins by reconciling Part 4's final coverage and implements the missing decision/authority
paths alongside durable recovery. Part 6 can begin from the completed contract inventory, but its
integration must use the actual Part 4 handoffs and Part 5 provenance/recovery behavior. Part 7
validates the assembled result and reports remaining gaps.

Parts 4 and 5 provide durable routing; Part 6 completes the acceptance boundary. Completion of
Parts 5-6 includes the integration checks needed for live use of at least one supported profile.
The user should then be able to use it on real work and experience the intended reduction in manual
coordination. Part 7 proceeds alongside that use with broader testing, telemetry and validation of
the actual benefit. It does not postpone the live-use milestone. Expose unsupported areas and stop
boundaries explicitly; real-provider use stays within the user's authorized scope and spend. Extract
new projects only where the resulting dependency boundaries justify them.

## Excluded scope

No Python/LangGraph migration, provider rewrite, replacement ledger, rewritten methodology,
UI redesign or generalized workflow framework. No edits to the live ledger, global tool reinstall,
provider spend, deployment or publication as a side effect of preparing this worktree. Any change
to production acceptance rules must preserve previously legal replay histories; tests must cover
both old records and new command-time restrictions.
