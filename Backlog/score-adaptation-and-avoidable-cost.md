# Score adaptation and avoidable cost without rewarding clean-looking histories

Status: deferred at the operator's request until October 2026, when the token budget replenishes.
Recorded: 2026-09-24. Kind: change. No governed task opened; recording this item does not authorize
starting the work or scheduling an automatic run.

## Problem and intended outcome

Self-scoring has already helped identify valuable improvements. Preserve that capability while
correcting instructions that can confuse substantial work, justified adaptation and productive
assurance with poor planning or inefficient execution.

The operator mainly uses AILedger for substantial, multi-agent work, including continuity through
handoffs and chat compaction. Such work inherently costs more and exposes more uncertainty than a
small repair. Lower duration, fewer turns and fewer plan revisions are not independently evidence
of better governance. Nor does catching a defect establish that every associated expense was useful.

The desired distinction is between:

- investigation and assurance that produce evidence, correction or justified confidence;
- revision prompted by genuinely new evidence;
- preventable repetition, weak briefs, ignored existing evidence and operational confusion.

The goal is better discrimination, not higher grades. Preserve separate visibility of governance
effectiveness and cost efficiency. A useful mechanism may protect the work well and still operate
expensively.

## Evidence from the initial inspection

Read the current versions before implementation; these observations describe the 2026-09-24 checkout.

- [D4 in the rubric](../docs/self-scoring-rubric.md#d4--planning-and-decision-quality) requires an
  avoidability judgment on mis-scoping, distinguishing foreseeable error from reasonable revision.
  Its later instruction nevertheless defines effect by whether the work "landed first time" and
  treats corrected briefs as low-scoring. These instructions can pull a scorer in opposite directions.
- [The maintained rubric](../docs/self-scoring-rubric.md) recognizes detected wrongness,
  evidence-backed correction, and productive friction. The proposed revision should restore
  consistency with that intent rather than invent a new objective.
- D8 already requires four cost classifications: productive, necessary but non-discriminating,
  avoidable, and ceremony candidate. Strengthen their application; do not create a competing taxonomy.
- D6 already credits pre-completion defect discovery and distinguishes repair histories. It also
  stresses defects missed by earlier stages. Explain the opportunity and responsibility of each
  stage before assigning a penalty; a later discovery alone does not prove the earlier stage failed.
- The rubric already requires explanations beneath the score table. That provides room for finer
  judgments without adding dimension IDs or changing the artifact schema.

The operator compared the Axonius bundle and parser-fixes retrospectives: the larger task scored
worse on several dimensions. This is a useful calibration question, not proof of size bias. Their
records may also contain real differences in avoidable mistakes and learning. Inspect the evidence.

## Mechanism boundary and size

The initial inspection found these relevant areas (line counts include comments and declarations;
they are orientation estimates, not a complete dependency graph):

| Area | Files | Approximate lines | Role |
|---|---|---:|---|
| Measurement | `src/AILedger.Core/Application/TaskRetrospective.cs`, `CoordinatorMeasurement.cs` | 2,925 | Deterministic facts, joins, durations and named absences |
| Validation and CLI | `src/AILedger.Core/Artifacts/Logic/WorkflowRetrospectiveRules.cs`, `src/AILedger.Cli/Retrospectives/RetrospectiveCliCommands.cs` | 220 | Build/file the report and validate its vocabulary |
| Scoring guidance | `docs/self-scoring-rubric.md` | 880 | Agent interpretation and grading |
| Directly related tests | Projection, coordinator measurement, retrospective artifact/context/replay and CLI tests | 5,150 | Existing behavior and compatibility |

The projection computes no grades. The artifact validator requires exactly D1–D10, scores 0–5 or
`unmeasured`, and the existing confidence/controllability/evidence fields. The test
`WorkflowRetrospectiveArtifactTests.NoScoreChangesTheOutcomeOfAnything` establishes that score
values do not change task outcomes. Workflow routing already points the scorer to the rubric in
`cognitive/skills/workflow-coordinator/SKILL.md`.

Prefer a rubric revision with calibration. Do not enlarge the runtime mechanism unless a specific,
demonstrated evidence gap prevents the desired assessment.

## Proposed scope

### D4: distinguish planning, decisions and adaptation

Retain the D4 row but require three explicitly labeled assessments in its explanation:

1. **Initial planning:** Were scope, decomposition, dependencies and briefs appropriate to the
   evidence reasonably available at that time? Were foreseeable constraints investigated?
2. **Decision quality:** Did choices follow evidence, acknowledge uncertainty, consider relevant
   alternatives and communicate consequences?
3. **Adaptation:** When evidence changed, were affected decisions, scope, briefs and dependent work
   reconsidered promptly, with the reasoning preserved?

Define how these assessments justify the single D4 grade. Avoid an unexplained average or a rule
that the worst incident automatically determines the whole task. A material planning failure must
remain visible even if recovery was excellent. Good recovery does not erase avoidable mistakes.

Replace first-pass success as the governing criterion. For substantial revisions, identify the
trigger, when the relevant evidence became available, what changed and why any resulting rework
was avoidable or justified. Lack of evidence about avoidability must reduce confidence rather than
be converted into blame. Do not reward staying with a plan after contrary evidence appears.

### D6: credit assurance while locating genuine misses

Explain both the contribution of the full assurance process and any failure within an individual
stage. Establish the earlier stage's remit, available evidence, candidate and realistic opportunity
to detect a finding. Distinct reviewer findings can demonstrate complementary assurance.

Retain penalties for ignored findings, false repairs, repeated same-root-cause failures and
demonstrated misses within an earlier stage's responsibility. A clean, appropriately scoped check
can provide warranted confidence without discovering a defect. Do not invent a counterfactual that
a defect could never have been found without the kernel.

### D8: grade what expenditure bought

Make the existing four cost classifications the basis of the explanation and grade, with cited
run/record evidence for substantial assignments. Keep unattributable cost explicitly unknown;
do not force every expense into a confident classification or invent precise bucket percentages.

Report task size, repositories/work items, uncertainty, dependencies, risk and execution constraints
before interpreting totals. These provide context, not a blanket complexity exemption. Describe
avoidable repetition and convergence after findings rather than penalizing elapsed time alone.

Distinguish provider-reported turns, tool calls, shell commands and user interventions. They are not
interchangeable units. State provider/model, coverage, missing telemetry and denominator for any
comparison. Token reductions are not automatically equal dollar reductions; changed task/provider
mix and partial coordinator usage limit conclusions. Do not turn instrumentation quality into an
efficiency grade.

### Versioning and interpretation

Give the revised rubric an explicit revision identifier and require it in new retrospective prose.
Preserve the ten dimensions and independent effectiveness/cost reporting. The current rubric
prohibits a weighted overall grade; the conversational 1–10 assessments are not a proposed schema.
Do not add a weighted total as part of this item.

Identify whether each assessed mechanism was available to that task. A task that built the manual,
tool inventory or container verification route did not necessarily begin with their benefits.
Preserve uncertainty about causality and compare similar work where possible.

## Bounded calibration plan

Use four to six existing task histories, selected for contrasting evidence rather than desired
grades. Candidate starting points:

- `2026-09-23_1034-axonius-yaml-bundle` — substantial multi-repository work;
- `2026-09-23_2014-axonius-parser-fixes` — smaller follow-up with inherited lessons;
- `2026-09-23_1406-container-verification` — environmental constraints and assurance/repair cycles;
- `2026-09-23_1920-coordinator-operating-manual` — smaller documentation task;
- an older task with evidenced repeated avoidable coordination failures, if needed for contrast.

Confirm that the sample actually contains justified adaptation, preventable rework and productive
assurance. Do not assume those classifications from names, duration or this entry.

1. Freeze task evidence inputs and record kernel/rubric identities, telemetry coverage and selection
   rationale. Hold the task evidence constant between rubric versions.
2. Define discrimination cases before scoring: new evidence prompting revision; ignored available
   evidence causing rework; complementary assurance finding a defect; repeated failed repair;
   justified clean verification; genuinely unnecessary duplicate work.
3. Compare old and revised rubric readings. Historical grades are a baseline, not an answer key;
   where their inputs cannot be reproduced, state that limitation.
4. Use independent scoring passes for the revised rubric, with prior scores and the other pass's
   results withheld. Do not claim independent agreement from sequential self-scoring in one context.
   Apply the supported calibration procedure; avoid leaking scores through shared ledger briefs.
5. Report disagreements and their evidence, especially D4/D6/D8. Set acceptable agreement criteria
   before seeing results. If independent runs are unavailable, label a single-reader exercise
   exploratory, not a completed calibration.
6. Explain each material score change. Successful calibration separates justified work from waste;
   it does not require large tasks to outrank small ones or every revised score to rise.

Keep the exercise bounded. After one planned comparison, fix a demonstrated ambiguity and rerun
only affected cases where possible. Document unresolved limitations rather than expanding into a
full corpus audit or repeated unbounded review cycles.

## Compatibility and non-goals

- Preserve D1–D10, the 0–5/`unmeasured` vocabulary, required table header and existing CLI surface.
- Keep measured facts separate from grading judgments; retain named absences and confidence rules.
- No score thresholds for completion, release, cleanup or reopening a completed task.
- Preserve original filed retrospectives and append-only histories. Calibration outputs must be
  distinguishable from canonical historical reports; never silently overwrite or relabel them.
- Do not compare grades across rubric versions as an execution trend without explicit qualification.
- Leave D10's outcome-measurement policy unchanged; changing it needs a separate evidence design.
- Do not change provider telemetry, introduce a cost prediction system, audit every task or claim
  demonstrated savings from the latest tools before subsequent tasks supply evidence.
- Decomposing `CoordinatorMeasurement.cs` (2,061 lines at inspection) and related large classes is
  separate maintenance. File size warrants inspection, not an automatic god-class diagnosis. No
  structural refactor is a prerequisite for this rubric work.

## Acceptance criteria

- D4 guidance no longer contradicts itself about reasonable revision versus first-pass success;
  planning, decision and adaptation assessments are explicit and the combined grade is explained.
- D6 distinguishes complementary detection from an evidenced earlier-stage miss, and retains
  credit for justified assurance without demanding a dramatic defect find.
- D8 distinguishes productive/necessary expense from avoidable repetition using cited evidence,
  task context and correctly defined measurement units.
- Calibration includes contrasting real cases and reports score changes, disagreements, missing
  evidence and remaining limitations. It demonstrates discrimination rather than grade inflation.
- New reports identify their rubric revision; old reports and histories remain intact.
- Representative revised bodies remain compatible with the existing artifact validator. If code
  proves necessary, document why and run the relevant tests plus repository-required checks.
- The scorer's routing references the authoritative rubric; no competing scoring instructions are
  copied into multiple skills. No runtime or replay change is needed unless explicitly justified.

## Related work

- [Initial scoring capability](Priorities.md#9--score-the-governance)
- [Current self-scoring rubric](../docs/self-scoring-rubric.md)
- [Coordinator operating manual](../docs/coordinator-operating-manual.md)
- [Tool and environment inventory](../docs/tool-inventory.md)

Retain the successful evidence-bound approach. Improve how it interprets the work before enlarging
the measurement machinery.
