# Coordinator operating manual

For a Claude or Codex session that coordinates a governed task. Read the quick reference before you
act. Read the section for a command before you issue it.

Every rule carries one label:

- **enforced** — kernel source refuses the command. The citation is `path:line` at source commit
  `b985593d967f4b7f4fe1df3795279600659b75c9`. Paths are relative to `src/`. When you re-check the
  manual against source, update this commit.
- **policy** — a skill or `cognitive/RULES.md` asks for it. Nothing refuses it.
- **judgment** — a person or role decides. The label names who.

Two words mark text that is not a rule. **Guidance** is operating advice under coordinator routing
judgment; nothing refuses it. **Observed** marks what governed runs have shown; it is historical
evidence, not a kernel rule.

Refusal texts are quoted exactly. `<angle brackets>` mark text the kernel fills in. `…` marks where a
quoted refusal continues beyond what is shown.

---

## Quick reference

### 1. How to behave, and where your authority ends

| you may | you may not |
|---|---|
| Route the task: pick the next run, dispatch it, read its result, move the stage (judgment: coordinator routing) | Do the work item yourself. A coordinating role cannot hold a run on a work item (enforced, `AILedger.Core/Runs/Logic/RunDispatchRules.cs:138-146`) |
| Change course when evidence changes: backward stage, superseding recon/contract/plan, new claim (see [Changing course](#changing-course)) | Issue a waiver. `--without-prerequisites`, `--without-verification`, `--without-brief`, `--with-stale-brief` are the operator's decision (policy, `cognitive/RULES.md` "Working inside the kernel") |
| Use the operator actor id to carry out a decision the operator made | Make the operator's decision because you hold its id (policy, same section) |
| Raise an escalation for a business decision or a true unknown | Resolve an escalation. Operator only (enforced, `AILedger.Core/Domain/AuthorizationPolicy.cs:34-37`) |
| Report a refusal and stop | Retry the same command in a different spelling. A refusal is law (policy, `cognitive/RULES.md` "Working inside the kernel") |

Three kinds of authority are separate (judgment):

- **Coordinator routing** — order of runs, when to go back, when to stop and report.
- **Planning-lead technical judgment** — claim resolution, recon domains, direct or decompose, plan
  content. A planning lead holds `ResolveClaim` by default; an implementation lead does not (enforced,
  `AILedger.Cli/RoleDefaults.cs:10-23`).
- **Operator business authority** — scope (`work add`, `work abandon`), constraints, escalations,
  waivers, acceptance of decisions (`ResolveDecision` is held only by the operator by default,
  `AILedger.Cli/RoleDefaults.cs:9-31`).

### 2. Before you act: check these first

| command | check before issuing it |
|---|---|
| any mutation | Your actor has a role (`AuthorizationPolicy.cs:10-13`) and the capability (`AuthorizationPolicy.cs:50-56`). `ailedger who --task T` shows roles. |
| `work add`, `provider launch` | You ran `context build` for **your own** actor, and the skills have not changed since (`AILedger.Core/ContextBriefing/Logic/ContextGateRules.cs:94-140`). |
| `stage transition` | The target is legal from here, and its arm is met: see [Stage arms](#stage-arms). Backward needs `--reason`; forward refuses it. |
| `work add` | Stage is Ready. Actor is the operator. Scope paths are absolute and exist. More than one scope needs `--not-split-because ALT`. |
| `artifact record` | Stage admits the kind. The kind needs (or refuses) `--run` and `--work`: see [Artifacts](#artifacts-and-producers). If one is current, pass `--supersedes` for a task-wide artifact or a legacy (no `--candidate`) verifier/reviewer output. Output from a `--candidate` run inherits its members and derives its replacements; `--supersedes` is optional there. |
| `provider launch` for a Worker | Stage is Execution or Repair. Item is not Blocked, Stale, Completed or Abandoned. No run is active on it. |
| `provider launch` for a Verifier | Stage is Verification. Choose a provider other than the worker's. With `--candidate`, the same provider is refused at launch (`AILedger.Core/Runs/Logic/AssuranceRules.cs:106-113`). Without it, the same provider is refused only at `work complete` (`AILedger.Core/WorkItems/Lifecycle/WorkItemLifecycleRules.cs:265-271`). |
| `provider launch` for a CodeReviewer | Stage is Review. `--work` is named. A verifier completed after the latest work. With `--candidate`, pass `--verifier-run`. |
| `work complete` | Worker run, verifier run (other provider, after the work), and for a scoped item or any item with new assurance a current CodeReviewOutput. No active run, no open escalation. |
| `lesson mark` | Stage is Learn. Actor is operator or lead. |
| Archive | No active run, no open challenge, at least one lesson mark that mints a lesson. |
| a new id | Ids are unique per kind per task. Read `status` for taken ids. Use your own prefix (policy, `CLAUDE.md` "Identifiers"). |

`ailedger preflight batch` checks a planned set of launches against one snapshot without starting
them. It reserves nothing and does not promise a later launch will pass.

### 3. Reading and tools

- C# symbol questions: Roslyn first. This is the only tooling rule the code enforces (enforced,
  `AILedger.Providers/Navigation/RoslynSearchGuard.cs:37-52`). See [Reading and tooling](#reading-and-tooling).
- Known file, known lines: a targeted read. Not a whole-directory dump (guidance).
- Shell is for the `ailedger` CLI, builds, tests and git (guidance).
- Redirect large output to `$TMPDIR` and read byte-bounded windows (guidance, launch brief).
- Wait on a run with a bounded interval. Do not poll unchanged state in a tight loop (guidance).
- Do not re-read the whole skill set each step. Re-read the one skill when the same boundary fails
  twice (policy, `cognitive/RULES.md` "Repeated failure is a signal, not a queue").

### 4. The normal path, one line per step

| # | stage | action | who |
|---|---|---|---|
| 1 | Discovery | `task open`; `actor attach` each role; `context build` for every actor that issues gated commands, including the operator | operator |
| 2 | Discovery→Research | needs one Open claim | coordinator |
| 3 | Research | researcher runs for external claims; lead run files InternalRecon; record an alternative or accept a decision | researcher, lead, operator |
| 4 | Research→Design | recon current, its producer completed, external claims settled | coordinator |
| 5 | Design | lead run files PromptContract, then OrchestrationPlan; operator files UserRequest (any stage up to Ready) | lead, operator |
| 6 | Design→Scope→Ready | Design arm again, contract current; then plan current and roles assigned | coordinator |
| 7 | Ready | `work add` | operator |
| 8 | Ready→Execution | work item exists; UserRequest, PromptContract, OrchestrationPlan current | coordinator |
| 9 | Execution→Verification | a Worker run completed; verifier run files VerifierOutput | worker, verifier |
| 10 | Verification→Review | a Verifier run completed; reviewer run files CodeReviewOutput | reviewer |
| 11 | Review | `work complete`; lead or operator files CloseoutSynthesis | operator, lead |
| 12 | Review→Learn→Archive | CodeReviewer completed (scoped work); `lesson mark`; Archive | operator or lead |
| 13 | Archive | `retrospective record` | operator or lead |

Step 11 before step 12: the served coordinator and orchestrator skills put closeout synthesis
before lesson marking (policy, `workflow-coordinator`). The kernel admits CloseoutSynthesis at Review,
Learn or Archive (enforced, `AILedger.Core/Artifacts/Logic/CloseoutSynthesisRules.cs:17-18`).

---

## Roles and authority

Default capabilities, from `AILedger.Cli/RoleDefaults.cs:7-31`. `actor attach --capability` can grant
others, but a non-operator can never receive `ManageRoles`, `ManageScope`, `ResolveEscalation` or
`ManageConstraints` (enforced, `RoleDefaults.cs:33-44`).

| role | default capabilities | cannot, by default |
|---|---|---|
| operator | all | — |
| planning-lead | claim, resolve claim, evidence, propose decision, challenge, runs, transition, context, escalation, alternative, artifact | resolve decision, dispose challenge, manage work |
| implementation-lead | as planning-lead without resolve claim | resolve claim, resolve decision, dispose challenge, manage work |
| verifier, code-reviewer | claim, evidence, challenge, context, escalation, artifact | alternative, runs, transition |
| researcher, worker | claim, evidence, challenge, context, escalation | alternative, artifact, runs, transition |

Operator-only actions (enforced):

| action | source |
|---|---|
| `work add` | `AILedger.Core/Domain/AuthorizationPolicy.cs:21-24` |
| `work abandon` | `AuthorizationPolicy.cs:28-31` |
| `escalation resolve` | `AuthorizationPolicy.cs:34-37` |
| `constraint add`, `constraint supersede` | `AuthorizationPolicy.cs:39-42` |
| dispatch with `--subject` | `AILedger.Core/Runs/Logic/RunDispatchRules.cs:40-54` |
| `provider launch` without `--work` | `AILedger.Cli/Providers/ProviderGrantResolver.cs:18-23` |
| `--without-prerequisites` | `AILedger.Core/Stages/Logic/StageTransitionRequestRules.cs:93-102` |
| `--without-verification` | `AILedger.Core/WorkItems/Lifecycle/WorkItemLifecycleRules.cs:223-228` |
| `--without-brief` | `AILedger.Core/ContextBriefing/Logic/ContextGateRules.cs:55-60` |

`--with-stale-brief EVIDENCE-ID` is accepted from any actor that may run the command
(`ContextGateRules.cs:76-92`). Policy still reserves it to the operator (`cognitive/RULES.md`
"Working inside the kernel").

Operator or lead: `lesson mark` (enforced, `AuthorizationPolicy.cs:44-48`).

A run on a work item needs its owner or an operator as dispatcher (enforced,
`WorkItemLifecycleRules.cs:140-152`).

---

## Stage graph and arms

### Graph

Legal edges (enforced, `AILedger.Core/Stages/Logic/StageTransitionPolicy.cs:8-22`):

| from | to |
|---|---|
| Discovery | Research |
| Research | Discovery, Design |
| Design | Research, Scope |
| Scope | Design, Ready |
| Ready | Scope, Execution |
| Execution | Research, Design, Scope, Verification |
| Verification | Execution, Repair, Review |
| Repair | Execution, Verification |
| Review | Repair, Learn |
| Learn | Review, Archive |
| Archive | none; terminal |

- Scope→Research is not an edge. Go Scope→Design→Research.
- Backward means a lower stage in declared order (`StageTransitionPolicy.cs:50`). Repair comes after
  Verification, so Verification→Repair is forward and Repair→Verification is backward.
- An illegal edge is refused and the refusal lists the legal targets:
  `Transition from '<current>' to '<target>' is not legal. legal from '<current>': <list>.`
  (`StageTransitionPolicy.cs:66-70`). Read the list. Do not walk the stages to find it.

Transition flags (enforced, `StageTransitionRequestRules.cs:25-102`):

| flag | rule |
|---|---|
| `--reason TEXT` | Required and nonblank on a backward move. Refused on a forward move. |
| `--serial-because ALT` | Only when entering Verification. The alternative must exist. |
| `--without-prerequisites REASON` | Operator only, nonblank. Skips the target's arm. It does not skip Archive's lesson minting (`StageTransitionRules.cs:49-55`). |

### Stage arms

Command-time arms (enforced, `AILedger.Core/Stages/Logic/StagePrerequisiteRules.cs:11-190`). "Completed
run" in this table means status Completed and **not** a `--provider none` run
(`StagePrerequisiteRules.cs:65-71`; `AILedger.Core/WorkItems/Verification/WorkItemVerificationRules.cs:24-25`).
A no-provider run never satisfies an arm.

| target | refused unless | lines |
|---|---|---|
| Research | at least one Open claim. Forward entry only. | 24-27, 73-79 |
| Design | one current InternalRecon, task-wide, with a completed real producer, hash still current; if any assessment is `external`, a completed Researcher run and no Open external claim; at least one alternative or one accepted decision. Forward entry only. | 28-32, 81-92; `Artifacts/Logic/InternalReconRules.cs:68-87` |
| Scope | leaving Design: the whole Design arm again. Always: a current PromptContract. | 33-38 |
| Ready | a current OrchestrationPlan; one role assignment other than Verifier or CodeReviewer; a Verifier assignment; a CodeReviewer assignment. Assignments, not runs. | 94-112, 203-204 |
| Execution | at least one work item; current UserRequest, PromptContract and OrchestrationPlan. | 114-137 |
| Verification | a completed **Worker** run (not Researcher, not a lead); a serial justification when two or more disjoint worked items had no overlapping working runs. | 139-152; `Stages/Logic/StageSerialExecutionRules.cs:9-34` |
| Repair | an open challenge or a current VerifierOutput. | 154-164 |
| Review | a completed Verifier run. | 51-52 |
| Learn | a completed CodeReviewer run, when any work item has a scope. | 166-174 |
| Archive | no active run; no open challenge; at least one lesson mark; minting produces at least one lesson. | 176-190; `StageTransitionRules.cs:49-55` |

Notes:

- Backward entry into Research or Design skips their arms. That is the replanning door. Leaving
  Design for Scope runs the Design arm again, so a stale recon blocks you there, not on the way in.
- Replay enforces only two of these: Execution needs a work item, and Archive needs no active run and
  no open challenge (enforced, `Stages/Logic/StageEventValidator.cs:84-100`). Every other arm is
  command-time only. A history is never refused later for an arm added after it was written.

---

## Action gates

Admitting stages for stage-gated actions (enforced, `AILedger.Core/Stages/Logic/EntryActionStageRules.cs:24-76`,
unless another source is named). The refusal is
`Action '<action>' is not allowed at stage '<stage>'; admitting stage is: '<stage>'.`
(or `admitting stages are:` for several) (`EntryActionStageRules.cs:18-21`).

| action | admitting stages |
|---|---|
| `work add` | Ready |
| `lesson mark` | Learn |
| artifact UserRequest | Discovery, Research, Design, Scope, Ready |
| artifact InternalRecon | Research, Design |
| artifact PromptContract | Design |
| artifact OrchestrationPlan | Design, Scope |
| artifact VerifierOutput | Verification |
| artifact CodeReviewOutput | Review |
| artifact CloseoutSynthesis | Review, Learn, Archive (`Artifacts/Logic/CloseoutSynthesisRules.cs:17-18, 39-45`) |
| WorkflowRetrospective | Archive, then no live work item, then no active run (`Artifacts/Logic/WorkflowRetrospectiveRules.cs:25-50`) |
| run for a Researcher subject | Research |
| run for a Worker subject | Execution, Repair |
| run for a Verifier subject | Verification |
| run for a CodeReviewer subject | Review |

Runs whose subject is an operator or a lead are not stage-gated (`EntryActionStageRules.cs:68-75`).

A PromptContract can be filed only at Design. If you need to revise the contract from Execution, go
back to Design with `--reason` first.

---

## Artifacts and producers

| kind | `--run` | `--work` | filed by | source |
|---|---|---|---|---|
| UserRequest | refused | refused | operator | `Artifacts/Logic/ArtifactAuthorityRules.cs:17-26` |
| CloseoutSynthesis | refused | refused | operator or lead | `ArtifactAuthorityRules.cs:28-34, 75-94` |
| WorkflowRetrospective | refused | refused | operator or lead; CLI form is `retrospective record` | `ArtifactAuthorityRules.cs:28-34, 75-94` |
| InternalRecon | required: active, owned, task-wide, no assurance, operator or lead subject | refused | the run's own actor | `ArtifactAuthorityRules.cs:36-49`; `InternalReconRules.cs:16-21` |
| PromptContract, OrchestrationPlan | required: active, owned; operator or lead subject | refused | the run's own actor | `ArtifactAuthorityRules.cs:36-46, 67-72` |
| VerifierOutput | required: active Verifier run | required, or inherited from an assurance run | the verifier run | `ArtifactAuthorityRules.cs:56-65`; `ArtifactScopeRules.cs:8-17` |
| CodeReviewOutput | required: active CodeReviewer run | required, or inherited from an assurance run | the reviewer run | same |

Rules for artifacts (enforced):

- Task-wide and legacy artifacts: one current artifact per kind and work scope. A second without
  `--supersedes` is refused, and the refusal names the id: `A current '<kind>' artifact already exists
  for this scope; a revision must supersede it. Pass --supersedes <id>.`
  (`Artifacts/Logic/ArtifactRevisionRules.cs:19-29, 64-72`).
- Task-wide and legacy artifacts: `--supersedes` must name the current artifact of the same kind and
  scope (`ArtifactRevisionRules.cs:31-46`).
- Output filed by a `--candidate` (assurance) run bypasses those two rules. The kernel derives the
  per-member replacements. A supplied `--supersedes` must be a current intersecting predecessor, or it
  is refused: `Assurance artifact coverage: supersedes assertion is not a current intersecting
  predecessor.` (`Artifacts/Logic/ArtifactRules.cs:23-25, 75-98`).
- Only the producing run can file its artifact, and only while it is active. A contract, plan or
  review cannot be filed for a run after it ends.
- A lead dispatched on a work item cannot file PromptContract or OrchestrationPlan: those kinds are
  task-wide (`ArtifactScopeRules.cs:13-15`), and a lead cannot hold a run on a work item
  (`RunDispatchRules.cs:138-146`). File them from a task-wide lead run.
- A VerifierOutput needs a current OrchestrationPlan, a terminal row (`VALIDATED`, `REJECTED`,
  `NEVER-TESTED`) for every Open or Validated claim the item depends on, and a disposition row for
  every attention id in the plan (`Artifacts/Logic/VerifierOutputRules.cs:16-70`).
- A Verifier or CodeReviewer run cannot be completed without its matching artifact
  (`AILedger.Core/Runs/Logic/RunLifecycleRules.cs:111-126`).

### InternalRecon freshness

The recon binds a hash of every claim's id, statement, status, evidence ids and replacement
(`Artifacts/Logic/InternalReconDocuments.cs:15-29`). Any change to any claim makes the recon stale.
The refusal is `InternalRecon: claimSetHash does not match the current claim set; refresh and
supersede recon.` (`InternalReconRules.cs:37-39`).

The cycle:

1. The claim set changes (claim added, resolved, superseded).
2. `ailedger artifact recon-template --task T > recon.json` — the template carries the new hash.
3. Fill every claim's `domain` with `internal` or `external`, and write the `report`.
4. From an active task-wide lead run, at Research or Design:
   `ailedger artifact record --task T --actor LEAD --run R --id IR2 --kind InternalRecon --title "Internal recon" --supersedes IR1 --body-stdin < recon.json`
5. The launcher completes run R. Only then does the recon satisfy Design (`InternalReconRules.cs:80-81`).

Design never falls back to an older recon revision (`InternalReconRules.cs:67-73`). Resolving a claim
during Design therefore needs a superseding recon before Design→Scope. Plan for it (judgment:
coordinator routing).

---

## Work items, runs and assurance

### Work items

| command | refused when (enforced) | source |
|---|---|---|
| `work add` | not Ready; not operator; no current brief for the operator; a scope is relative, blank or duplicate; more than one scope without `--not-split-because ALT`; a scope overlaps a live item; a dependency claim is Rejected or Superseded | `WorkItemLifecycleRules.cs:17-50, 154-188`; `EntryActionStageRules.cs:27` |
| `provider launch` on an item | the item has no scope; a scope path does not exist | `AILedger.Cli/Providers/ProviderGrantResolver.cs:58-68, 198-199` |
| `work abandon` | not operator; item Completed, Stale or Abandoned; a run is active on it; an escalation on it is open | `WorkItemLifecycleRules.cs:69-91` |
| `work unblock` | item not Blocked; a dependency claim is Rejected or Superseded — add a replacement item | `WorkItemLifecycleRules.cs:116-138` |

- A file scope is granted as its parent directory (`ProviderGrantResolver.cs:111-113, 198-199`). A
  scope on a repository-root file grants the whole root. Scope the directory you mean.
- A scope must exist, so a new file cannot be its own scope. Scope the containing directory.

### Runs

Refusal order at start (enforced, `RunDispatchRules.cs:25-150`; `AILedger.Core/Runs/Logic/RunAdmission.cs:15-91`):

1. `--subject` by a non-operator; unknown subject.
2. No current brief for the dispatcher (provider launch only).
3. CodeReviewer subject without `--work`.
4. Item Blocked, Stale, Completed or Abandoned.
5. Coordinating subject on a work item.
6. Dispatcher is not the owner or an operator; a dependency claim is Rejected or Superseded.
7. A run is already active on the item — runs on one item are serial.
8. CodeReviewer before a verifier completed after the latest work.
9. Verifier or CodeReviewer without `--candidate` once the item has new assurance.

Run ids are unique per task. A reused id is refused: `A run with ID '<id>' already exists.`
(`AILedger.Core/Application/CommandHandler.cs:222-231`). Use a new id for every attempt.

The launcher closes the run. An agent inside a run never calls `run complete`, `stage transition`, or
completes its own work item (policy, `CLAUDE.md` "Launching another agent").

### `work complete`

Checks in this order (enforced, `WorkItemLifecycleRules.cs:190-281`):

1. Item already Completed; Abandoned; Blocked or Stale.
2. A run is active on it; an escalation on it is open.
3. No completed Worker or Researcher run on it.
4. No completed verifier run.
5. The verifier ended before the latest working run ended.
6. Every verifier used the worker's provider.
7. For a scoped item, or any item with new assurance: no current CodeReviewOutput from a reviewer
   that completed after that verifier.

`--without-verification REASON` (operator only) skips checks 3-7. It never skips 1-2
(`WorkItemLifecycleRules.cs:52-67, 212-235`).

### New assurance (`--candidate`)

`--candidate SHA256` binds verifier and reviewer runs to one frozen set of bytes. Rules (enforced,
`AssuranceRules.cs:54-126`):

- Only a Verifier or CodeReviewer subject may carry it.
- The id is 64 lowercase hex characters. The kernel checks equality only; the coordinator computes
  it over the actual candidate bytes (policy, `task-orchestrator`).
- Every member (`--work A --also-work B`) is live, owner-permitted, has current dependencies, no open
  escalation and no active run.
- Each member's recorded working version is its latest real Worker or Researcher run. Two latest runs
  with the same end time are refused as ambiguous.
- A verifier's provider differs from every member's latest working provider.
- A reviewer names `--verifier-run V`: a completed verifier with identical members, candidate and
  working versions, still current and independent.
- The run needs a fresh provider session: use `provider launch`, never `provider resume`.
- Once an item has any assurance run, later verifier and reviewer runs on it need `--candidate`
  (`RunAdmission.cs:87-88`), and a legacy `--work` VerifierOutput is refused
  (`Artifacts/Logic/ArtifactRules.cs:32-34`).
- An artifact filed by an assurance run inherits its members; supplied work flags must equal all of
  them (`ArtifactRules.cs:75-98`).

---

## Common refusals and recovery

Refusals met in task `2026-09-23_1406-container-verification`, re-checked against source, plus the
UserRequest refusal met in task `2026-09-23_1920-coordinator-operating-manual`. Each is avoidable
with the check in the quick reference.

| refusal (exact) | source | recovery |
|---|---|---|
| `Action 'work add' is not allowed at stage 'Scope'; admitting stage is: 'Ready'.` | `EntryActionStageRules.cs:18-21, 27` | Transition Scope→Ready, then `work add`. Stop a batch at its first refusal. |
| `Execution requires at least one governed work item.` | `StagePrerequisiteRules.cs:118-121` | `work add` at Ready first. |
| `Action 'lesson mark' is not allowed at stage 'Review'; admitting stage is: 'Learn'.` | `EntryActionStageRules.cs:18-21, 29` | Complete the items, transition Review→Learn, then mark. |
| `A user-request artifact must be operator-authored and cannot name a producer run.` | `ArtifactAuthorityRules.cs:19-23` | The operator files it without `--run`. |
| `A run with ID '<id>' already exists.` | `CommandHandler.cs:230` | New run id. |
| `Evidence '<E>' does not support claim '<C>'.` followed by two lines naming what each record points at | `Claims/Logic/ClaimRules.cs:58-63, 154-167` | Read the two lines. Add evidence with the right `--supports` or `--refutes`, then resolve with it. |
| `Unknown claim '<id>'.` | `CommandHandler.cs:216` | Read `status` for the real id before writing. |
| `Actor '<id>' lacks capability 'RecordAlternative'.` (a researcher) | `AuthorizationPolicy.cs:50-56`; `RoleDefaults.cs:27-29` | The researcher records claims and evidence; a lead records the alternative. |
| `Required context is <n> UTF-8 bytes; --max-context-bytes is <m>. No required records were truncated. Select a narrower --work brief, reduce its required inputs, or explicitly increase --max-context-bytes.` | `AILedger.Cli/ContextBriefing/ContextManifestBudget.cs:56-59` | Narrow `--work`, or raise `--max-context-bytes`. Default is 262144 (`ContextManifestBudget.cs:12`). |
| `Closeout synthesis finding '<id>' uses a value outside the frozen vocabulary. …` | `CloseoutSynthesisRules.cs:20-36, 76-83` | Use only the listed values for kind, severity, opportunity, repair and disposition. |

Other refusals a coordinator meets:

| refusal (exact) | source | recovery |
|---|---|---|
| `Actor '<id>' has not built its context on this task and cannot <action>. …` | `ContextGateRules.cs:99-102` | `context build` for that actor. |
| `The skills served to actor '<id>' have changed since its context was built, so it cannot <action>. …` | `ContextGateRules.cs:137-140` | Rebuild context. Report if skills are being edited during the task. |
| `A run against work item '<W>' cannot be held by a coordinating role, …` | `RunDispatchRules.cs:141-145` | Dispatch a Worker, Researcher, Verifier or CodeReviewer, or drop `--work` for task-wide artifacts. |
| `A code reviewer can only start on work item '<W>' after a verifier run has completed against the latest work done on it.` | `RunAdmission.cs:83-85` | Verify first, or again after new work. |
| `Work item '<W>' was verified by the same provider that did the work ('<p>'), …` | `WorkItemLifecycleRules.cs:267-270` | New verifier run with the other provider. |
| `Transition from '<a>' to '<b>' goes back in the pipeline and needs a reason. …` | `StageTransitionRequestRules.cs:32-34` | Add `--reason` saying what was learned. |
| `Transition from '<a>' to '<b>' goes forward and does not take a reason. …` | `StageTransitionRequestRules.cs:45-47` | Drop `--reason`. |
| `Design requires at least one recorded alternative or accepted decision.` | `StagePrerequisiteRules.cs:89-90` | A lead records the approach it rejected, or the operator accepts a decision. |
| `Completing <role> run '<R>' requires its matching '<kind>' artifact.` | `RunLifecycleRules.cs:114-126` | The run must file its output before it ends. |

After any refusal (policy, `cognitive/RULES.md` "Working inside the kernel" and "Repeated failure is
a signal, not a queue"):

1. Read the full text. Most refusals name the fix.
2. Do not retry with a waiver or a different spelling.
3. A second refusal on the same boundary: re-read the skill, then question the approach.

---

## Changing course

Design is expected to change as claims are confirmed or refuted. Each change below is a supported
path. None needs a waiver. The inefficiency is a retry loop or a skipped step, not the change itself.

| situation | supported path | source |
|---|---|---|
| New belief to act on | `claim add`, then `evidence add --supports` or `--refutes` | policy, `CLAUDE.md` "Record a claim" |
| Claim confirmed or refuted | `claim resolve --status validated\|rejected --evidence E`; evidence must name the claim in that direction | `ClaimRules.cs:43-64` |
| Claim replaced by a better one | `claim resolve --id C1 --status superseded --superseded-by C2`. If C2 is validated and nothing refutes C1, dependents move to C2 (refinement). Otherwise they invalidate (correction). | `ClaimRules.cs:66-84, 124-134` |
| A dependency claim was rejected or superseded | Dependent Proposed/Accepted decisions become Invalidated. An Active item becomes Blocked; other live items become Stale. Add a replacement item on a current claim. | `Claims/Logic/ClaimDependencyRules.cs:10-36`; `WorkItemLifecycleRules.cs:127-135` |
| Decision changes | `decision propose --supersedes D1`, then `decision resolve --status accepted`; accepting it supersedes D1 | `Decisions/Logic/DecisionRules.cs:26-38, 52-83` |
| Dispute a claim, decision or work item | `challenge raise --id X --target-type claim\|decision\|work --target-id ID --reason TEXT [--evidence E]`. Evidence is optional to raise. A challenge with no evidence cannot be supported. Supporting it applies the target's consequence. | `Challenges/Logic/ChallengeRules.cs:17-21, 35-61`; `Challenges/Targets/ChallengeTargetRules.cs:32-36` (no-evidence refusal), `38-51` |
| Approach discarded | `alternative record` (lead or operator by default) | `RoleDefaults.cs:10-23` |
| The plan or contract is wrong | Backward transition with `--reason` to Design (from Scope, Execution) or Research (from Design, Execution). File the new revision with `--supersedes`. | `StageTransitionPolicy.cs:8-22`; `ArtifactRevisionRules.cs:31-46` |
| Claims changed | Supersede the recon (see [freshness](#internalrecon-freshness)) before Design→Scope | `StagePrerequisiteRules.cs:33-36` |
| Verifier or reviewer found defects | Verification→Repair (forward), or Review→Repair (backward, `--reason`); Worker run; back to Verification with `--reason`; new verifier and reviewer runs. With assurance, a new `--candidate`. | `StageTransitionPolicy.cs:17-19, 50`; `StageTransitionRequestRules.cs:28-41`; `EntryActionStageRules.cs:71` |
| Item will never be done | Operator `work abandon --reason`; settle its active run and open escalation first | `WorkItemLifecycleRules.cs:69-91` |

Who decides (judgment): the planning lead judges whether evidence changes the design; the coordinator
routes the backward move and the new runs; the operator decides anything that changes business scope
or needs a waiver.

Task `2026-09-23_1406-container-verification` is evidence, not a template. Its external research completed before
its first Design admission, as the Design arm requires. Its later review findings went through
Repair and fresh assurance. That was recovery working, not waste.

---

## Reading and tooling

### Enforced

The provider PreToolUse Roslyn search guard is the only executable tooling rule
(`AILedger.Providers/Navigation/RoslynSearchGuard.cs:37-52`):

- It denies a Grep or shell search that may read C# unless the bridge recorded a Roslyn failure for
  that solution scope.
- One recorded failure allows one CLI search, within ten minutes
  (`AILedger.Providers/Navigation/RoslynFallbackStore.cs:33-61`).
- Observed in governed runs: a pipe into a search tool, or ledger text that names one, was denied,
  and a directory search without an explicit non-C# filter counted as a C# search.
- File-name discovery, targeted reads and explicit non-C# filters stay available.

### Guidance

Nothing refuses these. They are what keeps a session short.

- Load the solution in Roslyn before the first C# symbol query.
- Use `status`, `who`, `artifact list`, `preflight batch` and `closeout status` to learn state before
  acting, instead of learning it from a refusal.
- Issue ledger commands as literal single commands. Some governed harness sessions deny commands that
  need approval, such as loops and inline scripts; that denial is the harness's, not AILedger's.
- Tools differ between coordinator and child sessions. Do not assume a tool exists because another
  session had it.

---

## Sources

A short file name in a citation lives in the directory given at its first full citation. Directories
under `src/AILedger.Core/`: `Stages/Logic/`, `Artifacts/Logic/`, `Runs/Logic/`, `WorkItems/Lifecycle/`,
`Claims/Logic/`, `Decisions/Logic/`, `Challenges/`, `Domain/`, `Application/`,
`ContextBriefing/Logic/`. Command and flag spelling: `ailedger --help`.

Policy: `cognitive/RULES.md`, the `workflow-coordinator` and `task-orchestrator` skills, `CLAUDE.md`.
Command reference: [operator-guide.md](operator-guide.md).
