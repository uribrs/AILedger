# Item 64: Phase 1 contract coverage

Scope: ordinary recon/research → planning → worker → verifier → isolated reviewer handoffs.
This inventory is a direct development artifact authorized by the Part 1 request. It is not a
live-task record. Phase 1 supplies the shared plan shape and prospective filing check only;
automatic delivery, producer outcome declarations, early launch preparation and client observation
remain in phases 2–4. No general contract registry is introduced.

## Shared plan contract and compatibility boundary

[OrchestrationPlanDocuments](../src/AILedger.Core/Artifacts/Logic/OrchestrationPlanDocuments.cs)
owns immutable `AttentionColumns`, `AttentionTableTemplate` and `NoMaterialAttentionItems` for
future authoring delivery, and the parser used by filing and verifier consumption:

```markdown
| id | name | failure mode | causal path and impact | planned handling | source |
| --- | --- | --- | --- | --- | --- |
| R1 | descriptive-name | Failure | Cause and impact | test: named artifact | citation |
```

Alternatively, state `No material attention items — <concrete task-specific reason>`.
The reason is workflow guidance; the executable rule retains its existing case-insensitive
substring match. Presence of that phrase skips attention extraction, even if a table also exists.

The extraction deliberately preserves [MarkdownTableReader](../src/AILedger.Core/Artifacts/Logic/MarkdownTableReader.cs):
case-insensitive exact column order after cell trimming, outer pipes, first matching table,
unvalidated separator line, rows ending at the first wrong-width line. Empty tables are accepted.
Recognized IDs are uppercase `R`, one or more `char.IsDigit` digits, optionally one trailing
`char.IsLetter` letter. Other IDs are ignored, including a descriptive name embedded in the ID
cell; recognized duplicates are refused with ordinal comparison. No five-item cap, required
non-ID cell contents, minimum row count, stricter separator or new ID grammar was introduced.
These limitations are inventory gaps, not a claim that a structurally admitted plan is sufficient.

Call paths:

- New/revised plan: CLI `artifact record` → service → CommandHandler →
  [ArtifactRules.Record](../src/AILedger.Core/Artifacts/Logic/ArtifactRules.cs) → shared attention
  reader, after authorization, stage, scope, producer and revision checks, before any ArtifactRecorded.
- Verifier: CLI or structured submission → ArtifactRules →
  [ArtifactDocumentRules](../src/AILedger.Core/Artifacts/Logic/ArtifactDocumentRules.cs) →
  [VerifierOutputRules](../src/AILedger.Core/Artifacts/Logic/VerifierOutputRules.cs) → the same reader
  on the current plan. This includes multi-member assurance dependency unions.
- Replay: TaskReducer/TaskTransitionValidator →
  [ArtifactEventValidator](../src/AILedger.Core/Artifacts/Logic/ArtifactEventValidator.cs) →
  ArtifactDocumentRules. The new plan filing gate is deliberately absent here. Existing verifier
  document checks still run; their accepted shapes and identifier semantics are unchanged.

An unusable historical plan stays readable. When selected by verifier validation, the diagnostic
names the plan and offending contract, and directs supported replanning to Design/Scope and a
corrected artifact from an authorized active producer using `--supersedes PLAN-ID`. It neither
rewrites history nor accepts a verifier report without dispositions. Existing prerequisite,
consultation and stage requirements still apply to that repair.

## Covered actions and authoritative definitions

**E** means executable rule, **G** workflow guidance, **H** host/environment-dependent check.
The sources below remain the definitions for later delivery; prose here is a bounded inventory.
Common E bindings are task/actor authority, active subject-owned producer, artifact kind/scope,
current predecessor and stage. See [ArtifactAuthorityRules](../src/AILedger.Core/Artifacts/Logic/ArtifactAuthorityRules.cs),
[ArtifactScopeRules](../src/AILedger.Core/Artifacts/Logic/ArtifactScopeRules.cs),
[ArtifactRevisionRules](../src/AILedger.Core/Artifacts/Logic/ArtifactRevisionRules.cs) and
[EntryActionStageRules](../src/AILedger.Core/Stages/Logic/EntryActionStageRules.cs).

| Action / owner | Requirements, output shape and bindings | Authority and limits |
| --- | --- | --- |
| File recon; task-wide Operator/PlanningLead/ImplementationLead producer | E: Research/Design, active own run without work/assurance, recon consultation from that run against current claim hash. Strict JSON fields `schemaVersion: 1`, `taskId`, `claimSetHash`, `assessments`, `report`; each assessment has exactly `claimId`, `domain` (`internal`/`external`); every historical claim covered once, report nonblank. | [InternalReconDocuments](../src/AILedger.Core/Artifacts/Logic/InternalReconDocuments.cs) already exposes the state-specific template; [InternalReconRules](../src/AILedger.Core/Artifacts/Logic/InternalReconRules.cs) owns schema, hash, producer and consultation checks. G: orchestrator owns substantive mapping/behavior cases; researcher output cannot replace recon. |
| Research return → Design; Researcher investigation and lead reconciliation | E: forward Research requires an open claim. Forward Design requires current recon whose producer completed real cognition, no open externally classified claim, and a completed Researcher consultation for each external assessment in the current Research episode; also an alternative or accepted decision. G: `research/<topic-slug>.md`, claim/evidence answers to bounded decision-changing questions. | [StagePrerequisiteRules](../src/AILedger.Core/Stages/Logic/StagePrerequisiteRules.cs), InternalReconRules and [technical-researcher](../cognitive/skills/technical-researcher/SKILL.md). No enforced typed researcher success/blocked return or report artifact. Completed alone is not an answer. |
| Design → Scope and file planning; lead technical owner, operator/coordinator routing | E: current PromptContract; Design-to-Scope rechecks Design readiness and, after replanning, completed reconsideration consultation since that episode opened. Contract/plan are task-wide, active coordinating producer-owned; PromptContract filing at Design, plan at Design/Scope. New/revised plan attention shape above. | StagePrerequisiteRules, ArtifactRules and [task-orchestrator](../cognitive/skills/task-orchestrator/SKILL.md). PromptContract has no content schema beyond nonblank text. G: classification, scope, assumptions, ownership, preflight and obligation map, material attention handling and named verification artifacts. |
| Planning → Ready → worker dispatch; operator dispatch, Worker implementation | E: Ready requires current plan and staffed working/verifier/reviewer roles. `work add` at Ready has owner, current dependent claims, occupied directory scopes, multiple-scope justification and current actor brief. Execution requires work and current UserRequest/PromptContract/OrchestrationPlan. Worker run at Execution/Repair, viable work/ownership/dependencies and no active intersection; coordinating roles cannot hold work-scoped runs. | StagePrerequisiteRules, [WorkItemLifecycleRules](../src/AILedger.Core/WorkItems/Lifecycle/WorkItemLifecycleRules.cs), [RunAdmission](../src/AILedger.Core/Runs/Logic/RunAdmission.cs), [RunDispatchRules](../src/AILedger.Core/Runs/Logic/RunDispatchRules.cs). G: disjoint assigned files, contract-based tests, named R-item artifacts, bounded implementation and returned evidence in [contract-driven-execution](../cognitive/skills/contract-driven-execution/SKILL.md). No required worker return artifact or typed success/blocked declaration. |
| Worker return → verification; orchestrator reconciles, coordinator selects/freezes/dispatches Verifier | E: Verification entry requires completed Worker cognition and serial-execution justification when applicable. New assurance has schemaVersion, exact member IDs, latest working run per member, candidate hash, no verifier pair on a verifier; viable dependencies, no escalation/active intersection, independent verifier provider for every member, fresh session. | [StageSerialExecutionRules](../src/AILedger.Core/Stages/Logic/StageSerialExecutionRules.cs), [AssuranceRules](../src/AILedger.Core/Runs/Logic/AssuranceRules.cs), [AssuranceBinding](../src/AILedger.Core/Runs/Contracts/Models/AssuranceBinding.cs). G: associations, scope union, exclusions, recon seam accounting and findings reconciliation. H: frozen bytes, repository refs, configured tools/checks. |
| File verifier output and return; active Verifier owns it | E: Verification stage, matching work or inherited exact assurance members/candidate; current plan selected; dependent Open/Validated claims across member union disposed. Tables: `id | status | name | citation | actor` and `id | final disposition | name | evidence`. Claim statuses `VALIDATED`, `REJECTED`, `NEVER-TESTED`; attention values `handled`, `accepted-risk`, `not-applicable`, `unresolved`. Required cells nonblank; disposition IDs unique. Completed verifier requires its applicable output. | VerifierOutputRules, [AssuranceArtifactRules](../src/AILedger.Core/Artifacts/Logic/AssuranceArtifactRules.cs), [RunLifecycleRules](../src/AILedger.Core/Runs/Logic/RunLifecycleRules.cs). [ArtifactSubmissionContracts](../src/AILedger.Core/Artifacts/Submission/ArtifactSubmissionContracts.cs) already defines the inline Markdown submission envelope and receipt; host assigns attribution/IDs. G: dispose every supplied assumption, evidence-based checks and decision drift, not only dependent claims. `unresolved` is admitted as a finding, not a clean pass. |
| Verifier return → isolated review; coordinator dispatch, CodeReviewer owns report | E: Review entry needs completed Verifier; legacy work requires verification after latest work. New assurance requires identical candidate/members/working provenance and exact completed current independent verifier run ID. Review body is nonblank CodeReviewOutput, producer/member/replacement-bound; completed run requires its applicable output. | AssuranceRules, [WorkItemVerificationRules](../src/AILedger.Core/WorkItems/Verification/WorkItemVerificationRules.cs), ArtifactAuthorityRules. [ContextAssembler](../src/AILedger.Core/ContextBriefing/Logic/ContextAssembler.cs) excludes all task-state artifacts for bound review; [ContextRolePolicy](../src/AILedger.Core/ContextBriefing/Logic/ContextRolePolicy.cs) also excludes request/contract/plan/recon/verification/review/escalation narratives from legacy reviewer context. G: ranked actionable code findings with citations per [code-reviewer](../cognitive/skills/code-reviewer/SKILL.md). No general semantic review verdict schema in governed Markdown admission. |
| Findings → repair/replanning; orchestrator plans, Worker repairs | E: Repair entry needs open challenge or current VerifierOutput. Legal backward edges require reason; backward Design/Research opens replanning but forward Scope rechecks readiness/consultations. New working provenance invalidates old assurance applicability. | [StageTransitionPolicy](../src/AILedger.Core/Stages/Logic/StageTransitionPolicy.cs), [StageTransitionRequestRules](../src/AILedger.Core/Stages/Logic/StageTransitionRequestRules.cs), StagePrerequisiteRules, AssuranceRules. G: coordinator convergence check, fresh assurance and explicit findings disposition in [workflow-coordinator](../cognitive/skills/workflow-coordinator/SKILL.md); no automatic technical acceptance. |

## Guidance-only output shapes

These shapes remain owned by their skills; they are not additional Phase 1 admission rules.

- Researcher: the [output contract](../cognitive/skills/technical-researcher/references/output-contract.md)
  requires Research frame (target, decision supported, depth, task classification, documentation
  status, constraints, assumptions), numbered cited findings with evidence posture, Cross-reference
  and limitations, and Recommendation split into Implement now / Verify first. The final return is
  the report path and recommended next action, with triggering claim/evidence IDs and direction.
  Claim resolution belongs to a role holding ResolveClaim, not to the researcher.
- Prompt designer: [prompt-contract-designer](../cognitive/skills/prompt-contract-designer/SKILL.md)
  specifies Role, Goal, Context, Constraints, Success Criteria, Execution Rules, Output Format and
  Stop Conditions. The worker stops for missing constraints/success criteria or unresolved material
  assumptions. Nonblank artifact admission does not certify these semantic requirements.
- Worker: `execution_notes.md` records changes, validation, actual commands and residual risks;
  return changed outputs, assumption updates, blockers/risks and that report path. No new artifact
  kind or typed outcome field exists for this return.
- Reviewer: code-reviewer requires severity (Blocker/Major/Minor/Nit/Observation), problem, impact,
  recommended fix and refactor versus local patch for each issue. CLI fallback uses a fresh
  `review/code-reviewer-N.md`; structured submission takes the complete report inline. A nonblank
  CodeReviewOutput alone is not proof this guidance was met.

## Delivery and preparation gaps reserved for later phases

- **Guidance/runtime mismatch:** task-orchestrator and workflow-coordinator say new plan admission enforces four exact
  sections: Preflight Evidence, Source Obligation Map, Proposed Change Walkthrough, and Consequential
  Assumptions and Recon Stop. The current ArtifactRules/ArtifactDocumentRules path does not implement
  those schemas. Phase 1 only adds the existing attention contract. Do not advertise those broader
  checks as executable or implement them incidentally. Also, Ready's `IsWorkingRole` accepts any role
  other than verifier/reviewer; Verification entry specifically requires Worker, while work completion
  recognizes Worker/Researcher. Preserve these distinct predicates when delivering requirements.
- **Meaning of return:** RunLifecycleRules records termination/session/output presence. Markdown
  findings and a completed process are not typed success; workers/researchers have no mandatory
  terminal outcome field. Phase 2 must preserve unknown/blocked distinctions without free-text verdict
  guessing. Task-13 [AssuranceContracts](../src/AILedger.Core/Assurance/AssuranceContracts.cs) already
  provides richer criterion/report/acceptance records when configured; reuse those where applicable.
- **Readiness is bounded:** [FileGovernedTaskService.Readiness](../src/AILedger.Storage/Inspection/FileGovernedTaskService.Readiness.cs)
  supports findings, alternatives, verifier/review submission, claim dispositions, work preparation/
  completion and stage transition at an expected version. It reuses command candidate admission;
  physical assurance candidate checks can return `unknown`. Dispatch and arbitrary plan submissions
  are unsupported here. Legal stage edges are not readiness; ready is not authority or a reservation.
- **Host checks and order:** [ProviderLaunchPreflight](../src/AILedger.Core/Runs/Logic/ProviderLaunchPreflight.cs)
  (inventory at Phase 1; superseded launch ordering is documented in
  [Phase 3](contract-delivery-phase-3.md))
  reuses full RunAdmission for assurance; the legacy overload previews only dispatch rules.
  [ProviderLauncher](../src/AILedger.Cli/Providers/ProviderLauncher.cs) builds the role-filtered manifest
  after run start. [ContextManifestBudget](../src/AILedger.Cli/ContextBriefing/ContextManifestBudget.cs)
  measures serialized UTF-8 with a default 256 KiB limit, can omit unreferenced background lessons,
  and refuses when required records cannot fit. [ProviderLauncher.Assurance](../src/AILedger.Cli/Assurance/ProviderLauncher.Assurance.cs)
  then opens configured authority/store and checks governed role/input grants via
  [AssuranceHost](../src/AILedger.Cli/Assurance/AssuranceHost.cs). Required brief budget, protected paths,
  actual candidate bytes, area/check discovery IDs and review profile compatibility are host facts;
  ledger identifiers cannot prove them. Phase 3 owns moving supported checks earlier.
- **Blind review versus requirements:** workflow guidance calls for task-13 assurance, but a review
  expecting requirements cannot assume those inputs are available in a blind governed reviewer
  context. Identify incompatible configuration explicitly. No profile repair, grant expansion,
  narrative leakage or skipped assurance is authorized by this phase.
- **Delivery not implemented:** phase 2 must attach the imminent contract to initial coordinator
  context and relevant completed run/artifact/stage responses, with observed task version, before
  next dispatch. No packet or transport changes are in Phase 1. Resume, fan-out, reconnect, arbitrary
  direct launch and universal ingress coverage remain outside V1's delivery-order claim.

## Validation

Completed 2026-09-30 against the working-tree implementation:

- Focused plan/ownership/revision checks: **47 passed, 0 failed, 0 skipped**, including 21 new
  [plan filing cases](../tests/AILedger.Tests/Artifacts/Plans/OrchestrationPlanFilingTests.cs) using the
  [production CLI/file-store fixture](../tests/AILedger.Tests/Artifacts/Plans/PlanFilingFixture.cs).
- Full solution: **2,306 main + 99 memory tests passed**, no failures or skips. Builds and results
  stayed under `/tmp/ailedger-phase1-build`, `/tmp/ailedger-phase1-results` and
  `/tmp/ailedger-phase1-full-results`.
- `git diff --check` and repository links in this document/item 64 passed inspection.

```sh
dotnet test tests/AILedger.Tests/AILedger.Tests.csproj --artifacts-path /tmp/ailedger-phase1-build -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false --filter 'FullyQualifiedName~OrchestrationPlanFilingTests|FullyQualifiedName~ArtifactAuthorityTests|FullyQualifiedName~ArtifactRecordTests' --logger trx --results-directory /tmp/ailedger-phase1-results
dotnet test AILedger.sln --artifacts-path /tmp/ailedger-phase1-build -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false --logger trx --results-directory /tmp/ailedger-phase1-full-results
```

The first sandboxed VSTest invocation aborted before running tests because its local socket bind
was denied. Re-running with the required test-host access exercised the actual suite. An initial
focused run then passed 20/21: the new dependent-claim test had put its row after a blank line, which
correctly terminated table parsing. The fixture was corrected to insert the row inside the table;
production parsing was not changed. The final focused and full results above include that correction.

Coverage proves valid/shared authoring shape, case/CRLF and no-item formats, duplicate IDs,
unchanged row/identifier semantics, rejected initial/revised wrong-header plans with no committed
artifact, revision/producer rules, historical malformed-plan replay and supported supersession,
and preserved claim/attention disposition obligations. Existing authority, stage, assurance and
isolation suites remain green. Two older positive fixture bodies were adjusted to contain a valid
plan; their ownership/revision assertions were retained.

No live ledger/lesson mutation, real provider call, installation, merge or push was performed.
The repository's pre-existing changes remain preserved. Phase 1 is complete; automatic contract
delivery, launch-preparation improvements, client observation and measured time/cost benefits are
not claimed.
