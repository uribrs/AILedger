# Orchestration driver — Part 1: contract and lifecycle inventory

Status: inventory and characterization complete; no production driver or admission change.
Inspected base: `24d724fb91b8ffa69c3201716e8e1f91cc08e260`, branch `codex/orchestration-driver`.
Parent: [scope and delivery parts](orchestration-driver-scope.md). All driver types below are
**proposals**, not existing APIs. Source links identify the owning implementation and named methods;
test links identify executable evidence. This development session used direct source inspection and
isolated fixtures, not the live ledger or provider sessions.

## 1. Existing lifecycle

### Stages and transitions

The graph is owned by [StageTransitionPolicy](../src/AILedger.Core/Stages/Logic/StageTransitionPolicy.cs)
(`LegalTargets`, `EnsureAllowed`). The following table exhausts the current
[TaskStage enum](../src/AILedger.Core/Stages/Contracts/Models/TaskStage.cs).
Entry prerequisites are a separate question from whether an edge exists. They are owned by
[StagePrerequisiteRules](../src/AILedger.Core/Stages/Logic/StagePrerequisiteRules.cs), not the driver.

| Current stage | All legal next stages | Prerequisites when entering this stage; phase work |
|---|---|---|
| Discovery | Research | Initial task opening; capture user request, constraints, claims and unknowns. Returning here has no additional target prerequisite. |
| Research | Discovery, Design | Forward entry needs an open claim. Lead performs internal recon; Researcher investigates external claims. Returning backward opens investigation without that forward gate. |
| Design | Research, Scope | Forward entry needs one current, claim-set-bound InternalRecon from a completed real lead run; no open external claims; every external claim has a current-research-episode lesson consultation from a completed real Researcher; at least one alternative or accepted decision. Backward entry opens replanning without those forward prerequisites. |
| Scope | Design, Ready | Current PromptContract. Design→Scope rechecks the full Design gate, including recon/research currency; after replanning, requires a completed real run's reconsideration consultation since the latest return to Design/Research. Prepare OrchestrationPlan and role split. |
| Ready | Scope, Execution | Current OrchestrationPlan; assigned working, Verifier and CodeReviewer roles. The working-role predicate here means any role except Verifier/CodeReviewer, which is broader than the completion predicate. Work creation occurs here. |
| Execution | Research, Design, Scope, Verification | At least one governed work item and current UserRequest, PromptContract and OrchestrationPlan. Dispatch Worker; task-wide coordinating runs cannot count as work. |
| Verification | Execution, Repair, Review | Completed real **Worker** run somewhere in the task, plus serial-execution justification if needed. Dispatch per-work/bundle Verifier; these stage predicates alone do not establish each member's acceptance. |
| Repair | Execution, Verification | An open challenge or current VerifierOutput. Worker investigates/repairs bounded scope; leads settle impact and replanning. |
| Review | Repair, Learn | A completed real Verifier run somewhere in the task. Per-work reviewer admission additionally requires verification after the latest work. |
| Learn | Review, Archive | Completed real CodeReviewer run if any work has nonempty resource scope. Leads synthesize closeout and mark lessons. |
| Archive | None | No active runs or open challenges, a lesson-bearing mark, and at least one eligible lesson actually minted. Archive is terminal in the stage graph, not a blanket prohibition on every later command. |

[StageTransitionRequestRules](../src/AILedger.Core/Stages/Logic/StageTransitionRequestRules.cs)
requires a nonblank reason on **every ordinal backward edge**, including Repair→Verification;
forward edges reject a reason. A serial justification names an existing alternative and is allowed
only when entering Verification. [StageSerialExecutionRules](../src/AILedger.Core/Stages/Logic/StageSerialExecutionRules.cs)
requires it when two or more worked items have pairwise disjoint scopes and their working runs
never overlapped. It checks that an explanation exists, not whether its judgment is good.

Only an operator can waive target prerequisites, with a recorded nonblank reason. The waiver does
not make an illegal edge legal or remove backward-reason requirements.
[StageTransitionRules.Archive](../src/AILedger.Core/Stages/Logic/StageTransitionRules.cs) still requires
eligible minted lessons. No automatic routing path should manufacture a waiver.

[EntryActionStageRules](../src/AILedger.Core/Stages/Logic/EntryActionStageRules.cs) separately restricts
Worker start to Execution/Repair, Researcher to Research, Verifier to Verification and CodeReviewer
to Review. Work addition is Ready-only, lesson marking Learn-only; recon/research/reconsideration
consultations enter at Research or Design / Research / Design respectively. Coordinating runs have
no corresponding entry-stage restriction. There is no universal rule that advancing stages means
all work items are done. Archive's gate does not require all items terminal; the later retrospective
gate does. Keep task closeout distinct from stage movement.

Evidence: [stage prerequisites](../tests/AILedger.Tests/Stages/StagePrerequisiteTests.cs),
[direction](../tests/AILedger.Tests/Stages/StageDirectionTests.cs),
[serial execution](../tests/AILedger.Tests/Stages/StageSerialExecutionTests.cs),
[entry actions](../tests/AILedger.Tests/Stages/EntryActionStageTests.cs),
[recon recovery](../tests/AILedger.Tests/Stages/InternalReconRecoveryTests.cs),
[research/reconsideration lessons](../tests/AILedger.Tests/Lessons/ReconsiderationConsultantTests.cs).

### Roles, capabilities and actual identity

The complete roles are Operator, PlanningLead, ImplementationLead, Researcher, Worker, Verifier and
CodeReviewer. [RoleDefaults](../src/AILedger.Cli/RoleDefaults.cs) supplies CLI defaults; the kernel
checks the **persisted assignment**, which may differ. Let `B` denote AddClaim, AddEvidence,
RaiseChallenge, BuildContext, RaiseEscalation and RecordAlternative.

| Role | CLI default capabilities | Bounded responsibility and current restrictions |
|---|---|---|
| Operator | All 18 capabilities | Role/scope/constraint ownership, cross-actor dispatch, decision/challenge/escalation resolution and exceptional waivers. It is an assigned role, not proof that a human is calling. |
| PlanningLead | B + ResolveClaim, ProposeDecision, ManageRuns, RequestTransition, RecordArtifact | Recon, planning and evidence-based claim judgment; own task-wide runs. Cannot dispatch another subject merely by holding ManageRuns. |
| ImplementationLead | B + ProposeDecision, ManageRuns, RequestTransition, RecordArtifact | Execution planning/replanning and bounded synthesis. Default lacks ResolveClaim and ManageWork. |
| Researcher | B | External research, findings and attributed producer outcomes; a completed real work-bound run can satisfy work completion, but not Execution→Verification's Worker prerequisite. |
| Worker | B | Scoped implementation/repair and attributed producer outcomes. Does not author governing artifacts or close its launcher-managed run. |
| Verifier | B + RecordArtifact | Independent verification and work-bound VerifierOutput; phase and provenance requirements below. |
| CodeReviewer | B + RecordArtifact | Blind technical review and work-bound CodeReviewOutput; requires prior verification and isolated context. |

The complete [capability vocabulary](../src/AILedger.Core/Roles/Contracts/Models/Capability.cs) is
ManageRoles, ManageScope, AddClaim, ResolveClaim, AddEvidence, ProposeDecision, ResolveDecision,
RaiseChallenge, DisposeChallenge, ManageWork, ManageRuns, RequestTransition, BuildContext,
RaiseEscalation, ResolveEscalation, RecordAlternative, ManageConstraints and RecordArtifact.
[AuthorizationPolicy](../src/AILedger.Core/Domain/AuthorizationPolicy.cs) maps each command to its
capability(s) and additionally reserves role assignment, work add/abandon, constraints and escalation
resolution to operators. [RoleAssignmentRules](../src/AILedger.Core/Roles/Logic/RoleAssignmentRules.cs)
forbids assigning one's own authority and giving ManageRoles/ManageScope to nonoperators; the CLI
also rejects their ResolveEscalation/ManageConstraints grants. Do not infer policy from defaults alone.

Three identity boundaries must remain distinct:

1. Kernel commands accept `ActorId` and look it up. A caller able to issue arbitrary operator CLI
   commands or edit authoritative files is already inside the trusted local boundary. A new driver
   role, role-name check or model instruction cannot authenticate that caller.
2. [ProviderFindingsSession](../src/AILedger.Cli/Findings/ProviderFindingsSession.cs) supplies host-bound
   task/actor/run and scoped authenticated tools. Payloads cannot choose a different identity.
   [RunCompletionAuthorization](../src/AILedger.Core/Runs/Logic/RunCompletionAuthorization.cs) separately
   requires the launcher's secret for successful managed-run closure; only its hash is persisted,
   and it is withheld from the child. An operator can close an orphan only Failed/Cancelled without it.
3. [AssuranceHost](../src/AILedger.Cli/Assurance/AssuranceHost.cs) binds actual principal/session/grants,
   protects authority/store from known provider write roots and rechecks live authority. External
   `assurance serve` assumes a locally authenticated recipient supplied by the host. It is not a
   remote authentication service or protection against the OS owner.

[ProviderGrantResolver](../src/AILedger.Cli/Providers/ProviderGrantResolver.cs) also matters: scoped
launches append the ledger directory as a separate additional provider directory, and file scopes
require directory grants. Work scope/manifest filtering is not an OS authentication boundary.
Task-wide provider launches require an operator dispatcher even though lead roles can hold task-wide
manual runs. Part 2 must cover **every exposed privileged route**, including shell/CLI and filesystem
access; wrapping a tool while leaving arbitrary operator paths reachable is insufficient.
Existing evidence: [authorization](../tests/AILedger.Tests/Core/AuthorizationTests.cs),
[role assignment](../tests/AILedger.Tests/Roles/RoleAssignmentTests.cs),
[operator dispatch](../tests/AILedger.Tests/Runs/Dispatch/OperatorDispatchTests.cs),
[assurance transport identity](../tests/AILedger.Tests/HandoffAssurance/AssuranceBoundaryTests.cs),
[provider grants](../tests/AILedger.Tests/Cli/ProviderGrantCliTests.cs).

### Governing artifacts and other task records

[ArtifactRules](../src/AILedger.Core/Artifacts/Logic/ArtifactRules.cs),
[ArtifactAuthorityRules](../src/AILedger.Core/Artifacts/Logic/ArtifactAuthorityRules.cs),
[ArtifactScopeRules](../src/AILedger.Core/Artifacts/Logic/ArtifactScopeRules.cs) and
[ArtifactDocumentRules](../src/AILedger.Core/Artifacts/Logic/ArtifactDocumentRules.cs) own this table.
It exhausts [GovernedArtifactKind](../src/AILedger.Core/Artifacts/Contracts/Models/GovernedArtifactKind.cs).

| Kind | Producer and filing phase | Required use and validation |
|---|---|---|
| UserRequest | Operator, no producer run; Discovery through Ready | Task-wide, current before Execution. Nonblank content; not semantic approval. |
| InternalRecon | Own active task-wide Operator/lead run, no assurance; Research/Design | Strict v1 JSON, task/claim-set hash and internal/external classification of **every** claim, including superseded ones. Producer must consult recon lessons against that claim set before filing. Design requires current revision and completed real producer; no fallback to older successful recon. |
| PromptContract | Own active Operator/lead run; Design | Task-wide; current before Scope and Execution. Nonblank content, not an executable instruction protocol. |
| OrchestrationPlan | Own active Operator/lead run; Design/Scope | Task-wide; current before Ready/Execution. New filings validate the attention table through OrchestrationPlanDocuments. Historical malformed attention data is not newly rejected on replay. |
| VerifierOutput | Own active Verifier run; Verification | Work-bound, or exact producer bundle coverage. Required for Completed run. Disposes dependent open/validated claims and plan attention items with allowed vocabularies/citations. NEVER-TESTED, REJECTED and unresolved are legal report values, not acceptance. |
| CodeReviewOutput | Own active CodeReviewer run; Review | Work-bound, or exact producer bundle coverage. Required for Completed run and current applicable code-work completion. Current document validation does not interpret a prose verdict. |
| CloseoutSynthesis | Operator/lead, no run; Review/Learn/Archive | Task-wide findings and retention tables with constrained vocabularies. A retained judgment, not an acceptance or deletion grant. Not required by the Archive stage gate. |
| WorkflowRetrospective | Operator/lead, no run; Archive with no active run or live item | Task-wide D1–D10 table, valid scores/confidence/controllability/evidence. Stale and abandoned items count as non-live. Not an agent performance target or an Archive prerequisite. |

Task-wide and legacy work artifacts use explicit supersession; candidate bundles use persisted
per-member replacement edges. [ArtifactApplicability](../src/AILedger.Core/Artifacts/Logic/ArtifactApplicability.cs)
computes current members; replacing A in an A/B output need not invalidate B. Presence, currentness,
producer completion and acceptance answer different questions. Current artifacts from failed
producers may remain visible; visibility alone does not qualify their producer as successful.

Claims/evidence/decisions/alternatives/constraints/challenges/lesson consultations are additional
ledger facts, not missing enum artifacts. [ClaimRules](../src/AILedger.Core/Claims/Logic/ClaimRules.cs)
and [EvidenceRules](../src/AILedger.Core/Evidence/Logic/EvidenceRules.cs) require directional evidence
for validation/rejection. [ClaimDependencyRules](../src/AILedger.Core/Claims/Logic/ClaimDependencyRules.cs)
invalidates dependent decisions and blocks active work or marks other work stale. Open claims can
remain dependencies; “current” does not mean “validated.” Supersession/repointing must follow its
own rules, not text edits. [ChallengeRules](../src/AILedger.Core/Challenges/Logic/ChallengeRules.cs)
applies target-specific consequences when a challenge is supported.
[EscalationRules](../src/AILedger.Core/Escalations/Logic/EscalationRules.cs) requires two options plus
a recommendation for business choices, or attempted-investigation evidence for true unknowns.

[LessonConsultationRules](../src/AILedger.Core/Lessons/Logic/LessonConsultationRules.cs) and
[LessonMintingRules](../src/AILedger.Core/Lessons/Logic/LessonMintingRules.cs) preserve consultations,
reconsideration episodes and eligible closeout lessons. Recalled lessons are prior evidence to
re-establish, not newly validated claims. Findings interpretation, classifications, meaningful
alternatives and lesson selection remain cognitive work.

Evidence: [artifact authority](../tests/AILedger.Tests/Artifacts/Authorization/ArtifactAuthorityTests.cs),
[recon gates](../tests/AILedger.Tests/Stages/InternalReconGateTests.cs),
[plan filing](../tests/AILedger.Tests/Artifacts/Plans/OrchestrationPlanFilingTests.cs),
[bundle artifacts](../tests/AILedger.Tests/Assurance/BundleArtifactTests.cs),
[closeout eligibility](../tests/AILedger.Tests/Artifacts/Retrospectives/TaskCloseoutEligibilityTests.cs).

### Dispatch, briefing and completion

The current application sequence is in
[ProviderLauncher.LaunchAsync](../src/AILedger.Cli/Providers/ProviderLauncher.cs):

1. Read launch state, subject, skills, work and optional assurance binding. Resolve existing resource
   grants and invoke [ProviderLaunchPreflight](../src/AILedger.Core/Runs/Logic/ProviderLaunchPreflight.cs).
   Its full overload calls authorization, entry-stage rules and
   [RunAdmission](../src/AILedger.Core/Runs/Logic/RunAdmission.cs). Its older overload is explicitly a
   dispatch-only preview. Fresh launches also run full preview; do not substitute the legacy subset.
2. Check declared verification environment (including Docker), prepare optional task-13 configuration
   and required role-filtered brief **before** resolving/probing the adapter or recording a run.
   [Launch preparation tests](../tests/AILedger.Tests/ContractDelivery/LaunchPreparationTests.cs)
   cover invalid setup, reviewer bindings, overflow and changes during the version probe.
3. Probe provider version, allocate a launcher secret and submit StartRun through the service, which
   revalidates against current replayed state. The subject role is captured on the run; subsequent
   role reassignment does not rewrite it. Only an operator dispatches on another actor's behalf.
4. Open configured assurance, build and record the actual subject's context, recheck at use, start the
   authenticated findings session and hand the exact serialized manifest to the adapter. Hash/count
   describe delivered bytes. Preparation is neither a reservation nor proof of future readiness.
5. Verify the returned run identity, retain the provider result sidecar **before** closing the ledger
   run, then record observed session/model/termination/usage and manifest metadata. Missing usage or
   unobserved timeout attribution stays null. An exception before a result causes attempted
   Failed/Cancelled cleanup. A result retained but unrecorded completion is a recovery case.
6. A provider Completed result without required verifier/reviewer output is reclassified to ledger
   Failed, while its original provider result survives. Other completion failures can leave Active
   ledger state. A successful process return is never a work-completion command.

[ContextGateRules](../src/AILedger.Core/ContextBriefing/Logic/ContextGateRules.cs) gates work creation
and provider dispatch on the dispatcher's recorded current skill hashes. Missing/unreadable/empty
skills refuse. The absent-brief operator waiver and cited stale-brief door are explicit recorded
exceptions; rebuilding is the routine route. These hashes are not a general task-version fence.
[ContextAssembler](../src/AILedger.Core/ContextBriefing/Logic/ContextAssembler.cs),
[ContextRolePolicy](../src/AILedger.Core/ContextBriefing/Logic/ContextRolePolicy.cs) and
[ContextManifestBudget](../src/AILedger.Cli/ContextBriefing/ContextManifestBudget.cs) own selection,
review isolation and mandatory content budgets. Never truncate mandatory inputs to make launch fit.

The review role filter excludes UserRequest, prompt/plan, recon, verifier/prior-review artifacts and
escalations. Legacy unbound projection can still include task goal, claims and decisions; the
exclusion list is not a blanket code-only guarantee. Candidate-bound review uses a deliberately
restricted code-oriented manifest with
work scope/base refs and permitted skills; coordinator packets never go to reviewers. Closeout
synthesis/retrospective artifacts are withheld from all agent briefs.
[ReviewerAmbientInputTests](../tests/AILedger.Tests/Providers/ReviewerAmbientInputTests.cs) and
[BundleProjectionTests](../tests/AILedger.Tests/Assurance/BundleProjectionTests.cs) cover additional
provider/manifest boundaries. The driver cannot put excluded narrative into a new side channel.

[WorkItemLifecycleRules](../src/AILedger.Core/WorkItems/Lifecycle/WorkItemLifecycleRules.cs) and
[WorkItemStateProjector](../src/AILedger.Core/WorkItems/Logic/WorkItemStateProjector.cs) define:

- Add creates Proposed; admitted run activates its covered members; terminal run pauses still-eligible
  members. Failed and Cancelled runs do not undo findings, source edits or blocked/stale status.
- Add requires current dependencies, available absolute scope, an assigned owner if provided, and an
  alternative explaining multiple scopes. Proposed/Active/Paused/Blocked items hold scope;
  Completed/Stale/Abandoned release it. Path occupancy is textual; host canonical grants are separate.
- Start requires owner or operator dispatch and current dependencies. Covered members cannot already
  have an Active run. Terminal/blocked/stale items refuse. Assurance checks all bundle members,
  including open escalations. These checks do not establish a task-wide routing-owner lease.
- Complete refuses Completed/Abandoned/Blocked/Stale, active covered runs and open work escalations.
  Without a waiver it requires real completed Worker/Researcher work, verification after latest
  work by an independent provider, and current work-bound review after verification for code scope
  (or any member with new assurance). No completion-stage restriction is added by this command.
- Unblock only moves Blocked→Paused and refuses rejected/superseded dependencies; replace invalid
  work through supported planning. Abandon requires operator/reason and no active run or work
  escalation; Completed/Stale cannot be abandoned. Waivers do not bypass these state protections.

[RunLifecycleRules](../src/AILedger.Core/Runs/Logic/RunLifecycleRules.cs) requires a terminal status and
exact resumable session for Completed (except a manual declared `provider none` record). No-provider
runs never satisfy real-work gates. The complete
[AgentRunStatus vocabulary](../src/AILedger.Core/Runs/Contracts/Models/AgentRunStatus.cs) is Active,
Completed, Failed, Cancelled and ProtocolError; the last four are terminal, and only Completed can
qualify as successful work. Here “real” names an existing predicate: legacy DidWork checks Completed
and not declared no-provider; candidate assurance additionally requires a nonblank session. A manual
trusted command can supply provider/session values; these predicates are not independent proof that
a process was observed. Completed verifier/reviewer runs must file matching current
outputs, including all assurance members. Worker/Researcher
[ProducerOutcome](../src/AILedger.Core/Runs/Outcomes/ProducerOutcomeRules.cs) is independently
`reported-complete`, `partial` or `blocked`, attributable to owning-run evidence and exactly retryable;
it is not process termination or acceptance.

Evidence: [verification sequencing and historical replay](../tests/AILedger.Tests/WorkItems/Verification/VerificationSequenceTests.cs),
[provider return recording](../tests/AILedger.Tests/Cli/ProviderRunRecordingCliTests.cs),
[producer outcomes](../tests/AILedger.Tests/ContractDelivery/ProducerOutcomeTests.cs),
[launch timeout termination](../tests/AILedger.Tests/Providers/LaunchTimeoutTerminationTests.cs),
[scope occupancy](../tests/AILedger.Tests/WorkItems/Scope/ScopeOccupancyTests.cs).

### The two assurance paths and evidence applicability

These paths coexist; they must not be merged by vocabulary alone.

| Boundary | Current enforcement | What it does not establish |
|---|---|---|
| Legacy governed work | Completed working/verifier runs; chronology; independent verifying provider; current code-review artifact where required | Physical candidate identity, semantic report success or task-13 acceptance |
| Candidate-bound governed assurance | [AssuranceRules](../src/AILedger.Core/Runs/Logic/AssuranceRules.cs): schema 1, exact normalized membership/latest real working run per member, candidate hash shape, no ambiguous latest work, independent verifying provider, fresh session; reviewer pairs identical candidate/members/work versions with qualified verifier. Completed runs require applicable outputs. Once new assurance exists, legacy assurance cannot silently replace it. | The caller-supplied candidate hash alone is not a live filesystem observation. Qualifying output is not a semantic pass. |
| Task-13 handoff assurance | [AssuranceService.Acceptance](../src/AILedger.Providers/Assurance/AssuranceService.Acceptance.cs): explicit current scoped acceptor; independent complete review/verification on exact binding; all selected criteria pass, no uncertainty/uninspected paths; own read/check receipts; dispositions for every finding; other current disagreement cannot be hidden; dependencies accepted and endorsements current | Does not complete a kernel work item, authorize release or observe implementation execution. Implementer is host-attested. Distinct principals/sessions are required; universally distinct providers are not a task-13 rule, unlike governed work's verifier-provider rule. |

Task 13 is the [default for supported implementation work](handoff-assurance-v1.md). It supports
bounded UTF-8 input closures, not arbitrary repository builds: up to 8 areas, 32 paths/256 KiB per
area and 128 KiB per input; bounded configured checks and journal capacity apply. The host must
supply complete candidate/requirement/source/dependency inputs and independent actual principals.
Missing configuration or an unsupported closure is an unresolved gap, not permission to use the
legacy path and call it equivalent. Task 12 remains opt-in, offline and read-only.

[AssuranceSnapshotReader](../src/AILedger.Providers/Assurance/AssuranceSnapshotReader.cs) captures exact
bytes; [AssuranceService.Reports](../src/AILedger.Providers/Assurance/AssuranceService.Reports.cs) and
[Checks](../src/AILedger.Providers/Assurance/AssuranceService.Checks.cs) validate observations and
receipts. Input, criteria, source, policy, dependent-area or endorsement changes require affected
areas to be reassessed. Unrelated area evidence can remain valid. An explicit `not_applicable`
disposition needs rationale and applicable independent report evidence; there is no defect/missing-
test waiver converting fail/unknown into pass. Synthesis may interpret disagreements but cannot accept.

A governed blind CodeReviewer cannot use the current requirements-aware task-13 review profile.
[ProviderLauncher.Assurance](../src/AILedger.Cli/Dispatch/ProviderDispatchService.Assurance.cs) and AssuranceHost
refuse that pairing before provider start. A separately authorized requirements-aware review context
(such as an independent external client) is needed **in addition to** the governed blind review.
Removing requirements or broadening the blind brief is not a recovery path.

Existing evidence: [task-13 acceptance, contradictions and freshness](../tests/AILedger.Tests/HandoffAssurance/AssuranceOperationTests.cs),
[host/provider boundaries](../tests/AILedger.Tests/HandoffAssurance/AssuranceProviderTests.cs),
[bundle lifecycle and per-member invalidation](../tests/AILedger.Tests/Assurance/BundleLifecycleTests.cs),
[readiness candidate uncertainty](../tests/AILedger.Tests/Inspection/InspectionAssuranceTests.cs).

### Replay and persistence

[CommandHandler](../src/AILedger.Core/Application/CommandHandler.cs) and focused command rules decide
new admission; [TaskTransitionValidator](../src/AILedger.Core/Domain/TaskTransitionValidator.cs) and
focused event validators protect compatible replay. New command gates must not be retroactively
applied to older events. Examples deliberately accepted on replay include completion without runs,
old runs without SubjectRole, reviewer starts before verification, old stage transitions without
new prerequisites/reasons, and historical scope/brief shapes. New assurance fields carry their own
structural validation rather than silently falling back to legacy interpretation.

[FileGovernedTaskService.ExecuteAsync](../src/AILedger.Storage/FileGovernedTaskService.cs) takes the
per-task mutation lease, replays, admits and appends. Derived projections are repairable; committed
history is authoritative. Command envelopes have causation/correlation, **not** a universal expected-
version compare-and-swap or generic idempotency key. Existing structured writes do have exact
request/binding receipts and recovery protocols; reuse them for their supported operations.
The mutation lease serializes a command, not an entire provider session or driver ownership epoch.
[CoordinatorSessionLifecycleRules](../src/AILedger.Core/CoordinatorSessions/Logic/CoordinatorSessionLifecycleRules.cs)
allows one open telemetry bracket per actor; the actor or operator closes it, and linked dispatch
checks that it is usable. It is not a task-wide exclusive driver lease. A second actor's bracket and
unbracketed dispatch remain possible.
[BatchPreflight](../src/AILedger.Core/Runs/Batch/BatchPreflight.cs) previews work/launch members on a
snapshot using owning rules, including a legacy dispatch-only subset where applicable. Its in-memory
planned-work projection and admissible rows do not reserve scopes, start runs or authorize fan-out.

[Readiness](../src/AILedger.Storage/Inspection/FileGovernedTaskService.Readiness.cs) uses typed proposals
and owning admission for findings, alternatives, artifact submission, claim dispositions, prepare
work, complete work and transition stage. It explicitly does **not** cover dispatch, run completion,
decision acceptance or waivers. A candidate-bound completion can return `unknown` even when ledger
admission passes because physical input was not inspected. `ready` is an observation, not a reservation.

[NextActionContract](../src/AILedger.Core/ContextBriefing/Delivery/NextActionContract.cs) and
[NextActionContracts](../src/AILedger.Core/ContextBriefing/Delivery/NextActionContracts.cs) are advisory
coordinator projections. `observed` describes delivery; dispatch is intentionally `unknown`.
Action prose, ordering, required-shape text, output presence and any “first action” are never dispatch
authorization. Missing/oversize observations return unknown/incomplete, not an empty successful plan.
[ContractBoundaryTests](../tests/AILedger.Tests/ContractDelivery/ContractBoundaryTests.cs) already
exercise stale bindings, unknown acceptance, budget limits and noncoordinator isolation.

Compatibility evidence: [stage replay](../tests/AILedger.Tests/Stages/StagePrerequisiteReplayBoundaryTests.cs),
[artifact replay](../tests/AILedger.Tests/Artifacts/Replay/ArtifactReplayBoundaryTests.cs),
[context replay](../tests/AILedger.Tests/ContextBriefing/ContextReplayBoundaryTests.cs),
[storage recovery](../tests/AILedger.Tests/Storage/RecoveryTests.cs),
[concurrent append](../tests/AILedger.Tests/Storage/ConcurrencyTests.cs),
[receipt recovery](../tests/AILedger.Tests/Artifacts/Submission/ArtifactSubmissionRecoveryTests.cs).

## 2. Responsibility map

This is the proposed division of work, not a new authorization grant. “Driver” means deterministic
scheduling through the owning kernel/host operation. It never reproduces admission predicates.

| Responsibility | Driver | Kernel / trusted execution host | Bounded agent judgment | User / exceptional authority |
|---|---|---|---|---|
| Intake and discovery | Detect uncovered phase, request bounded output | Record authorized request/claims/constraints | Identify assumptions, unknowns, material scope | Goal, business priorities, missing external authority |
| Recon, research, lessons | Route required episode/role, deliver current context | Claim-set/episode binding, evidence direction, consultation gates | Classify internal/external, research, assess evidence and lessons | Only true unknown after evidenced investigation |
| Design/scope/plan | Deliver planning findings, request readiness | Artifact/role/scope admission and replacement | Architecture, alternatives, decomposition, acceptance criteria | Business tradeoffs; exceptional scope/authority changes |
| Role/provider/scope selection | Match preauthorized role/provider profiles and nonconflicting work | Authenticate identities, constrain grants, enforce independence | Decide necessary expertise or changed scope when ambiguous | Configure authority; new spend or unsupported execution profile |
| Dispatch and stage movement | Choose supported action from settled facts; invoke shared admission; await result | Legal graph, prerequisites, current grants/brief and run rules | Explain backward transition, justify serial work, replan when needed | Waivers remain explicit trusted actions |
| Verification/review | Schedule independent passes, retain exact provenance | Candidate binding, isolated brief, outputs, checks and grants | Test adequacy, findings, technical review | Approve a new compatible host setup when needed |
| Findings and repair | Route affected areas and await bounded disposition; continue unresolved repairs | Directional evidence, challenge effects, claim invalidation, acceptance invariants | Validate/refute findings, targeted synthesis, repair and reassess impact | Business risk decision only where supported; cannot invent assurance override |
| Acceptance/work completion | Request explicit acceptance through granted authority, then governed completion once all applicable boundaries pass | Owning assurance acceptance and work-completion gates | Evidence-backed dispositions, acceptance rationale under authorized role | Release/deploy/merge and exceptional authority stay separately authorized |
| Waiting/cancel/recovery | Persist intent, reconcile actual runs/receipts, back off; preserve partial evidence | Lease, process supervision, token-protected closure, original receipt replay | Interpret meaningful partial work or uncertain repair scope | Orphan/check-stop attestation when trusted host cannot establish it |
| Learn/closeout | Route synthesis, invoke eligible marking/archive, retain receipts | Lesson eligibility/minting, stage and retention admission | Lessons, retrospective and retention judgments | Destructive retention or other exceptional authorization |

Routine deterministic prerequisites do not need a model to narrate them or a user to approve them.
Conversely, a driver must not invent a claim disposition, a backward reason, an exception or technical
acceptance just because it can construct the corresponding command. Admission remains Core-owned;
physical grants/processes/bytes remain host-owned. No new project is justified in Part 1.

## 3. Proposed typed contracts (design only)

Driver persistence records ownership, dispatch intent, attempts and waiting/recovery cursors only.
The ledger remains task truth; agent findings/decisions must be recorded through existing governed
operations, and task-13 receipts stay with their owning service. Cached task/acceptance observations
must always carry provenance and may not overwrite either source of truth. Core must not depend on
the future driver or provider implementation to evaluate its rules.

Use closed discriminated variants with explicit schema versions. The notation below specifies data
and invariants; it does not add C# records, interfaces or another executable policy engine. Existing
TaskId/WorkItemId/RunId and public owning operations should be reused where suitable.

### Proposals and bindings

| Proposed type | Required data / variants | Invariant |
|---|---|---|
| `DriverActionProposal` | SchemaVersion, ActionId, trusted ledger binding, TaskId, CoverageProfileVersion, Basis, typed Payload, Origin references | A proposal is untrusted intent until the owning operation admits it. Origin is settled ledger facts/authorized judgments, never packet text or array index. |
| `ActionPayload` | `RefreshContext(subject, work?)`, `Transition(target, reasonRef?, serialAlternative?)`, `Dispatch(subjectBinding, role, workSet, providerProfile, runId, assurance?)`, `RequestJudgment(kind, boundedScope, evidenceRefs, expectedOutput)`, `RequestAcceptance(areaBindings, reportRefs, dispositionRef)`, `CompleteWork(work, acceptanceRefs)`, `Reconcile(dispatchId)` | Each variant has exactly its fields. Waive/reassign/approve-business/release are absent from routine actions. Unsupported variants/schema versions refuse; no string-to-CLI translation. |
| `JudgmentKind` | Recon, Research, Plan, Replan, FindingDisposition, TargetedSynthesis, Closeout | Role and output schema are explicit; the host launches a bounded existing role. The driver's own prose cannot stand in for its result. |
| `ActionBasis` | TaskVersion, last committed EventId, kernel build/contract version, stage, work IDs and latest working run IDs, required current artifact IDs/replacement edges, claim/decision refs, context identity and skill hashes, host policy/profile digest | Observed state is named exactly. Any intervening event forces reload/re-evaluation for the first slice; do not infer that an unchanged stage means an unchanged basis. |
| `CandidateBasis` | Separate **governed** candidate/member/work-version/verifier-run binding and **task-13** area candidate/requirements/source/dependency/policy hashes and receipts | Neither hash namespace substitutes for the other. A physical reinspection and explicit association are required before combining their evidence. Part 6 owns that association. |
| `SubjectBinding` | Opaque host-issued principal/session/authority reference, assigned actor and role snapshot, granted operations/scope, expiry/revocation identity | Only trusted host constructs it. Actor/role values in model payloads are assertions, never authentication. No secrets in manifests, logs or proposal content. |
| `DispatchIntent` | ActionId, stable DispatchId/RunId, canonical payload hash, exact basis, provider/model/profile, scope/candidate refs, owner epoch, attempt ID | Persist before side effects. Retries retain logical dispatch identity; attempts are separately identified. Never use an unconfirmed retry to allocate another run. |

There is currently no universal atomic `ExpectedTaskVersion` command envelope. A driver-side equality
check followed by `ExecuteAsync` is still racy. Parts 3/5 must define a supported application-service
boundary that verifies the action's binding with durable admission/ownership, invokes existing rules
and returns a typed stale-basis result. Do not hold a ledger write lock across a provider session.
Candidate files can change independently of ledger version; recheck captured bytes/policy at use.
No ownership token may confer kernel authority, and no application seam may duplicate kernel rules.

### Execution results and waits

| Proposed type | Closed variants / fields | Interpretation |
|---|---|---|
| `ActionExecutionResult` | `Committed(receipt, fromVersion, toVersion)`, `Started(dispatchId, runId, startEvent)`, `AlreadyRecorded(originalReceipt)`, `Refused(owner, typedCode?, diagnostic, observedVersion)`, `StaleBasis(observedBasis)`, `Unsupported(stage/action/profile, missingCapability)`, `OutcomeUnknown(attemptId, reconciliationRefs)` | Unknown and refusal never collapse to success. Only actual owners supply typed reason codes. Preserve raw diagnostic where none exists. |
| `RunExecutionResult` | Dispatch/Run IDs; provider termination separately from recorded ledger termination; actual session/version/model if observed; manifest hash/count; retained-result receipt; required-output refs/applicability; producer declaration; evidence/judgment status; failure/timeout attribution; nullable usage | Completed provider, Completed ledger run, filed artifact and accepted work are separate dimensions. Provider exception before result leaves unobserved fields absent. |
| `ProcessObservation` | `NotStarted`, `Running(hostExecutionId)`, `Returned(providerStatus, exitCode?, session?)`, `Threw(failure)`, `Unobserved`; separately `TerminationCause = UserCancellation / LaunchTimeout / OtherObserved / Unknown` | ProviderStatus preserves the existing terminal enum, including ProtocolError. Exit 0 and elapsed duration never synthesize Completed or timeout cause. |
| `OutputObservation` | `NotRequired`, `Missing(requiredKinds)`, `Present(currentArtifactRefs)`, `Stale(replacementRefs)`, `Unknown` | Present describes recording/applicability only; the independent acceptance dimension is mandatory. |
| `AcceptanceObservation` | `NotAssessed`, `Pending`, `Rejected(evidence)`, `ReassessmentRequired(changes)`, `Accepted(exactBinding, authority, receipt)` for each applicable assurance boundary | A report's complete/pass prose cannot create Accepted. A historical receipt must be revalidated for current applicability. Accepted is neither work completion nor release. |
| `DriverState` | `Runnable(proposal)`, `Waiting(wait)`, `Blocked(block)`, `Recovering(dispatchId)`, `Unsupported(coverageGap)`, `Stopped(reason)`, `Finished(completionReceipts)` | Finished requires receipts for all promised scope, not an empty advisory action array. Stopped on cancellation is not Finished. |
| `Wait` | Kind `ActiveRun` / `ExternalEvidence` / `Judgment` / `UserDecision`; correlation IDs, owner, observed version, wake condition, next check/deadline, cancellation binding | Event/deadline-driven resumption revalidates basis. Waiting is execution coordination, not a forged WorkItemBlocked event. |
| `Block` | Kind `Admission` / `AuthorityOrConfiguration` / `UnresolvedFinding` / `UnavailableInput` / `Capacity` / `UnknownOutcome`; owning diagnostic and evidence, supported next operation | Known recovery is automatic; user escalation only for business/authority decisions or evidenced unknowns. Repeated unchanged refusal is not progress. |
| `RecoveryOutcome` | `NoEffectConfirmed`, `AttachedToActiveRun`, `RecordedResultRecovered(receipt)`, `OriginalWriteReplayed(receipt)`, `ReinspectionRequired(changes)`, `OrphanClosureRequired`, `CheckStopConfirmationRequired`, `StillUnknown`, `CorruptState` | Every outcome names evidence and the next allowed action. Unknown/corrupt states prohibit fresh dispatch of the same logical action until resolved. |

Existing APIs are `IGovernedTaskService`, `ProviderLaunchPreflight`/`RunAdmission`, the CLI-owned
`ProviderLauncher`, typed inspection/readiness, structured recording services, and
`IAssuranceService`. The tables do not claim those APIs already expose these driver contracts.
`GovernanceException` carries prose; [RefusalRuleKey](../src/AILedger.Core/Application/RefusalRuleKey.cs)
is text-normalized telemetry, not a policy routing ID. Unknown rule identity must remain an opaque
refusal routed for bounded diagnosis. Add stable classifications at owning seams in later parts;
never build a second engine from message matching.

## 4. First executable slice and total coverage

The first **testable dispatch slice** is one explicitly prepared, single-work-item task at Execution:
verify coverage/authority, obtain current brief, persist one dispatch intent, invoke the shared launch
service with a Worker, wait, retain the result and reconcile it. It stops at a typed return boundary;
it does not claim verification, task-13 acceptance, work completion or end-to-end usefulness.
Fake providers establish correctness of routing, not real-provider benefit.

Prerequisites before implementing this slice:

- Part 2 supplies authenticated routine driver authority and child isolation across all exposed paths.
- Part 3 extracts the existing launcher without changing its preflight/recording/cleanup ordering;
  supplies typed receipts/results and injectable fake-provider execution through the same service.
- Prepared state already has current request/contract/plan/recon/research/lesson evidence as required,
  assigned roles/capabilities, current dispatcher brief, one ready scoped item, base reference and
  resolved necessary business decisions. Preparation cannot be fabricated by the driver.
- The full cycle has a host-approved task-13 input closure, protected configuration/store, independent
  principals/checks and separate compatible requirements-aware review. Missing setup is explicit.
- Part 5 owns durable dispatch identity, crash reconciliation and exclusive routing ownership. A Part 4
  in-memory prototype is fixture-only until Part 5 passes. Automatic completion additionally waits
  for Part 6; it cannot rely on the presently permissive completion behavior characterized below.

The first **operational slice** is the prepared single-item Execution→Verification→Review loop,
including Repair/reverification/review, explicit task-13 acceptance and governed work completion.
It uses a different verifying provider from the working provider. It preserves blind governed review
and separately authorized task-13 review. Scoped finding disposition/replanning goes to bounded roles;
material unresolved defects keep the repair loop open. No fixed repair count turns them into success.
Scope changes that require creating work return a typed planning handoff (work add is Ready-only).

| Stage/operation | First operational coverage | Later handoff / unsupported behavior |
|---|---|---|
| Discovery | Unsupported intake | Part 4 extension: user intent, claims/constraints, unknowns and request artifact. Never jump to Execution. |
| Research | Unsupported new episode | Part 4 extension: lead recon, Researcher and lesson consultation gates, claim disposition. Preserve backward investigation handoff. |
| Design | Unsupported new/revised plan | Part 4 extension: bounded design/replanning, recon currency, reconsideration and PromptContract. |
| Scope | Unsupported new scope plan | Part 4 extension: decomposition, OrchestrationPlan, role/provider preparation. |
| Ready | Prepared-input boundary only | Part 4 extension: authenticated work/role/scope creation through kernel. A missing prerequisite stays unsupported/blocked. |
| Execution | One prepared Worker dispatch, then bounded sequencing | Multiple items/bundles and concurrency require explicit additional coverage and ownership tests. |
| Verification | One member, independent provider, applicable output and configured assurance | Unsupported input closures/profiles block; do not revert to legacy assurance. |
| Repair | Same prepared scope, bounded finding disposition and Worker rerun; then fresh affected evidence | Changed scope/claims/plans hand back to replanning. Uncertain bytes after failed work require inspection. |
| Review | Paired blind governed review plus separate requirements-aware task-13 review/acceptance | Profile mismatch is explicit. Reading one review's narratives in another is not permitted recovery. |
| Work completion | Enabled only after Part 6 acceptance bridge plus existing completion gates | Phase progress alone never completes work. |
| Learn | Unsupported closeout | Part 4 extension: synthesis, retention judgment and eligible lesson marks. Work-complete return does not claim task closeout. |
| Archive | Terminal observation only | Part 4 extension: archive/minting, retrospective and governed retention; Part 7 integration. Never mutate terminal stage automatically to “resume.” |
| Task-12/offline episodes, arbitrary builds, multi-task routing | Unsupported | Separate explicitly declared profiles; no accidental generic support through CLI passthrough. |

Every stage enum value must map to a coverage entry when the driver is implemented. A new enum value,
unknown action/result/schema or unconfigured profile returns Unsupported/Unknown explicitly. The
support profile is versioned and surfaced before work starts, including closeout exclusions.

## 5. Recovery matrix

This maps current facts to proposed driver behavior. Existing subsystem recovery is reused;
missing cross-boundary coordination belongs to Parts 3/5/6.

| Observation / failure window | Current evidence or boundary | Required recovery; owner |
|---|---|---|
| Refused before run start | Owning admission diagnostic; no run/provider invocation for prepared fresh-launch refusals | Refresh/rebuild known prerequisites, or bounded judgment for opaque refusal. No blind retry of unchanged input; Parts 3/4. |
| Missing provider/environment/configuration | Preflight/probe failure; may be before run start | Preserve unavailable, select only another authorized compatible profile or surface configuration gap. No invented session/usage; Parts 3/7. |
| Basis changes between observation and admission | Durable command revalidates, but no universal expected-version fence | Return StaleBasis/reobserve via future shared seam; never treat prior observation as a lease; Parts 3/5. |
| Crash before durable intent | No known side effect | After ownership/state reconciliation, propose again; Part 5. |
| Intent stored, uncertain StartRun acknowledgement | Run ID/event history and launch attempt | Look up exact run before dispatch. Missing acknowledgement is not absence. No second provider/run while uncertain; Part 5. |
| Run Active, provider launch/brief delivery uncertain | Start event may exist without manifest/result/session | Attach only to an authenticated surviving host execution; otherwise reconcile process and orphan. Never relaunch from Active alone; Part 5. |
| Provider active, user cancellation/timeout | Process supervisor termination observation | Cancel through owning host; await process-tree termination and persist Cancelled/Failed truth. Timeout and user cancellation remain distinct. Unknown process ending stays unknown; Parts 3/5. |
| Provider result retained, completion refused or storage failed | Sidecar, start event, ledger run, launcher authority | Reconcile exact result and committed history. Retry closure only with supported authority; lost token permits operator Failed/Cancelled orphan closure, not forged Completed; Parts 3/5. |
| ProtocolError, mismatched returned run/session or malformed/truncated protocol | Adapter/launcher validation and retained attributable observations; foreign-run result is rejected | Keep terminal failure distinct from unknown delivery; diagnose compatibility in bounded scope, then re-admit any new run after reconciliation. Never infer success from exit code or repair a foreign result's identity; Parts 3/5/7. |
| Provider Completed, required output missing | Ledger Failed reclassification and original result both retained | Dispatch bounded recovery/fresh independent pass as needed. Never synthesize output to close successfully; Parts 4/6. |
| Partial/blocked producer output, even on Completed process | Attributed declaration and evidence; absence is unknown | Interpret via bounded role, preserve findings; resolve actionable blockers or repair. No automatic acceptance or text inference; Parts 4/6. |
| Failed/cancelled worker after previous assurance | Later run may have changed files; latest-completed-work qualification can still refer to older work | Inspect candidate/requirements/source/dependencies; affected evidence requires reassessment before completion; Parts 5/6. |
| New working run / changed candidate / replaced member | Working provenance, candidate binding, explicit per-member replacement edges | Reverify/review affected members; retain unrelated applicable evidence. Do not globally discard a bundle or reuse stale pair; Parts 4/6. |
| Lost structured-write acknowledgement | Original request/binding/session and durable receipt | Retry exact body/key/binding through owning service. Changed payload conflicts. Receipt replay proves recording, not current acceptance; Parts 3/5. |
| Interrupted task-13 host check with no observed ending | Pending attempt and retained observations | Never automatically rerun. Trusted stop confirmation/reconcile keeps old outcome unknown; an explicitly requested new key starts a new check; Part 5. |
| Corrupt committed ledger/receipt or unreadable inputs | Store validation failure/unavailable snapshot | Fail closed, preserve material, request supported recovery/authority. Never edit history, replace with empty state or mark success; Part 5. |
| Conflicting findings, partial/unknown task-13 reports | Independent current reports and findings persist through supersession | Targeted independent reinspection/synthesis, then explicit authorized disposition/acceptance only when existing gates pass; Parts 4/6. |
| Rejected/superseded dependency, blocked/stale work | Kernel invalidation events | Replan/replacement work; never blindly unblock or reopen Completed/Abandoned. Original evidence retained; Parts 4/6. |
| Unsupported stage/profile/candidate closure or journal capacity | Coverage profile, current host bounds/capacity diagnostic | Explicit Unsupported/Blocked handoff; no skip, omission or unbounded command passthrough; Parts 4/7. |
| Business decision or actual external unknown | Escalation options/recommendation or attempted-investigation evidence | Wait for trusted user resolution, then refresh basis. An agent cannot author user approval; Parts 2/4. |

Recovery evidence already exists in [AssuranceRecoveryTests](../tests/AILedger.Tests/HandoffAssurance/AssuranceRecoveryTests.cs),
[FindingsRecoveryTests](../tests/AILedger.Tests/Findings/FindingsRecoveryTests.cs),
[ArtifactSubmissionRecoveryTests](../tests/AILedger.Tests/Artifacts/Submission/ArtifactSubmissionRecoveryTests.cs),
[run recording](../tests/AILedger.Tests/Cli/ProviderRunRecordingCliTests.cs) and
[process termination](../tests/AILedger.Tests/Providers/SystemProcessRunnerTests.cs).
No fixture result establishes crash-safe driver ownership that has not yet been implemented.

## 6. Current gaps and precise handoffs

These are current source/fixture findings, not a renewed telemetry investigation. Existing protection
against missing outputs, stale work versions, mismatched bundles, same-provider verification and
blind-profile leakage must survive extraction.

| ID | Reproduction / observed boundary | Owner and exit criterion |
|---|---|---|
| G1 | Issue an operator-named command through the trusted local CLI: AuthorizationPolicy looks up that name; it does not authenticate the human caller. Scoped MCP has stronger host binding but is not all possible access. | **Part 2:** bind routine authority to trusted host sessions; deny agent access to alternative operator/waiver/filesystem paths; test actual trust boundary and its OS-owner limit. |
| G2 | Launcher is CLI-owned; general commands have no expected-version envelope; refusal messages lack stable typed identity. NextActionContract and readiness cannot authorize launch. | **Part 3:** shared application service calling existing owners, typed execution receipts and opaque refusal fallback; **Part 5:** stale-basis/ownership coordination. Preserve existing CLI compatibility and admission ordering. |
| G3 | New `ExistingWorkCompletionDoesNotInterpretNeverTestedVerifierDisposition` runs on both legacy and candidate-bound paths: file a legal VerifierOutput disposing dependent CA as NEVER-TESTED, finish verifier/reviewer, complete A. It succeeds while CA stays Validated and observed technical acceptance stays unknown. No task-13 acceptance/configuration was used. | **Part 6:** applicable explicit acceptance and evidence-based disposition must gate automatic completion; test negative/unknown report cases, preserved human judgment and old replay. Do not “fix” by rejecting honest negative report filing. |
| G4 | New `UnsuccessfulLaterWorkDoesNotInvalidateExistingCandidateQualification`: after candidate-bound verification/review, start a repair Worker and close Failed or Cancelled; existing completion still succeeds against older completed work. Fixture establishes missing invalidation, not an actual filesystem mutation. | **Parts 5/6:** interrupted work triggers physical candidate/provenance reconciliation. Show a changed candidate cannot reuse acceptance and unchanged applicable evidence need not be discarded. |
| G5 | Task-13 explicit acceptance already rejects partial/unknown/failed/uninspected evidence, but its receipts are separate from kernel work completion. Its current requirements-aware review profile cannot run inside blind CodeReviewer. | **Parts 2/3:** configure compatible independent host profile; **Part 6:** explicit association of task-13 area bindings/acceptance with governed members/candidate and completion; preserve both reviews and fail closed on unsupported scope. |
| G6 | Per-command ledger lock / per-work Active-run checks do not identify one durable task router. Crash windows separate intent, run start, process start, result retention and run closure. | **Part 5:** ownership epoch, persisted intent, exact reconciliation and no duplicate dispatch tests at each window; Parts 4+5 required for real use. |
| G7 | Stage gates often ask for any completed role in the task; Ready uses a broader working-role predicate; Archive does not ensure all work terminal. Report shape and output applicability do not establish semantic success. | **Part 4:** explicit total coverage and per-item scheduling; **Part 6:** correctness of automatic completion; **Part 7:** full lifecycle fixture. Do not align distinct kernel predicates or tighten old replay incidentally. |

The two new tests above live in
[OrchestrationInventoryTests](../tests/AILedger.Tests/ContractDelivery/OrchestrationInventoryTests.cs).
They characterize current gaps deliberately. A later prospective fix should replace their permissive
command assertions with the new expected refusal and retain separate historical replay coverage.
No production defect is fixed in Part 1.

Parts 2 and 3 can start from this inventory without a new business decision. Their engineering work
must settle host authority and reusable dispatch seams. Before an operational pilot, an authorized
host must select the actual protected identities, complete bounded task-13 input closure/checks and
compatible independent review context. If those cannot be supplied, execution remains explicitly
blocked/unsupported; no policy exception is inferred. Part 6 is a hard prerequisite for automatic
completion, and Part 5 for durable real-work routing.

## 7. Validation record

All characterization uses temporary ledgers and fake adapters. Existing suites are reused for stage,
role, artifact, isolation, assurance, recovery and historical-replay boundaries rather than copied.
No real provider, live-ledger mutation, global-tool installation or production source change is part
of this work.

Validation on 2026-10-07 in this worktree:

| Command / check | Result |
|---|---|
| `dotnet test tests/AILedger.Tests/AILedger.Tests.csproj --filter 'FullyQualifiedName~OrchestrationInventoryTests' --nologo` | 4 passed; 0 failed/skipped. |
| Targeted `dotnet test tests/AILedger.Tests/AILedger.Tests.csproj --no-restore --filter … --nologo` | 506 passed; 0 failed/skipped. Filter covered ContractDelivery, Stages, WorkItems.Verification, Assurance, ContextManifestIsolation, ProviderRunRecordingCliTests, ReadinessTests, RecoveryTests, AuthorizationTests, RoleAssignmentTests and the new inventory tests. |
| `dotnet test --nologo` | Full solution: 2,403 AILedger.Tests + 99 AILedger.Memory.Tests = **2,502 passed**, 0 failed/skipped; exit 0. |
| Local documentation checks | All source/test links resolve; all 11 stages, 7 roles and 8 artifact kinds have table entries; no trailing whitespace in new files. |

No environmental blocker or existing test failure was encountered. The known completion gaps above
are passing characterization cases, not failures hidden or repaired to obtain a green suite.
