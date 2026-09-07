AILedger 2.0 — Automatic Workflow Retrospective

Goal

After a governed task reaches successful closeout, automatically evaluate how well AILedger itself performed.

This retrospective must NOT block commit, push, PR creation, merge, or normal task completion.

It is a learning mechanism for improving AILedger’s own workflow behavior over time.

It is not the same thing as a repository/domain lesson.

Examples of normal lessons:
- “This repo publishes artifacts to S3.”
- “TaskTransitionValidator duplicates command-time rules intentionally.”
- “This API requires pagination token X.”

Examples of workflow-retrospective learning:
- “Research should have activated earlier for this task shape.”
- “The verifier caught a defect that the planning gate should have exposed.”
- “Cross-provider verification added value here.”
- “Three verifier iterations repeated the same root cause and indicate poor repair convergence.”
- “The operator had to manually request decomposition even though the task characteristics warranted it.”
- “A role fired but contributed no meaningful value.”

Execution behavior

1. Trigger automatically after successful task closeout.
2. Run asynchronously/non-blocking.
3. Failure of the retrospective must never invalidate or reopen an otherwise successfully completed task.
4. Record retrospective failure separately if it cannot complete.
5. Use the completed task’s event history, artifacts, claims, decisions, research, run metadata, verifier/reviewer results, escalations, lessons, waivers, operator interventions, and final outcome as evidence.
6. Do not rely on an agent simply rating its own performance from memory. Scores and findings must be tied to observable task evidence wherever possible.

Retrospective dimensions

Evaluate at least:

1. Capability utilization
   - Which roles/capabilities should reasonably have activated?
   - Which actually activated?
   - Which required capabilities were missed?
   - Which capabilities activated unnecessarily or ceremonially?
   - Did the operator have to manually request something AILedger should have inferred?

2. Epistemic quality
   - Were assumptions identified and tested where appropriate?
   - Did research meaningfully confirm, refine, or refute assumptions?
   - Was evidence used correctly?
   - Were decisions revised when evidence changed?
   - Treat a justified change of mind as success, not failure.
   - Do not reward being accidentally correct without sufficient validation over being initially wrong and correctly self-correcting.

3. Planning and decision quality
   - Did scope and decomposition match the actual work?
   - Were important architectural or environmental constraints discovered early enough?
   - Did decisions follow evidence?
   - Were invalidated decisions/work items correctly revisited?
   - Did planning create avoidable rework?

4. Execution quality
   - Did implementation remain within governed scope?
   - Was work unnecessarily duplicated?
   - Was there avoidable scope drift?
   - Were escalations raised instead of silently inventing missing requirements?
   - Did workers converge efficiently after defects were identified?

5. Verification and review quality
   - Did verifier/reviewer roles activate when required?
   - Did they find meaningful defects?
   - Were their findings correctly repaired?
   - Distinguish productive discovery from process failure.
   - A verifier finding a real bug before completion is evidence that verification worked.
   - Penalize repeated failure to fix the same root cause more than the initial discovery.
   - Identify defects that should have been caught by an earlier stage.

6. Operator burden
   - How often did the operator need to intervene?
   - Which interventions were legitimately authority-requiring?
   - Which interventions indicate AILedger failed to use its own capabilities?
   - Did the system interrupt the operator unnecessarily?
   - Did it fail to interrupt when operator authority was actually needed?

7. Workflow efficiency
   - Were there unnecessary agent runs, repeated searches, duplicate verification, idle waits, noisy monitoring, or avoidable context consumption?
   - Did parallelism help or hurt?
   - Were provider/model choices sensible given the task?

8. Learning behavior
   - Were relevant prior lessons recalled?
   - Did recalled lessons materially affect current work?
   - Were new lessons worth preserving identified?
   - Were redundant, trivial, or overly task-specific lessons avoided?
   - Did any stale or misleading lesson reduce quality?

9. Outcome quality
   - Did the delivered result satisfy the task contract?
   - Were important success criteria merely assumed rather than demonstrated?
   - Did any known compromise or waiver materially reduce confidence?

Scoring

Do not initially collapse everything into one opaque scalar.

Produce a scorecard with each dimension scored on a defined scale, for example 0–5:

0 = failed / absent
1 = seriously deficient
2 = weak
3 = acceptable
4 = strong
5 = excellent

Each score must include:
- evidence
- explanation
- confidence
- whether the issue was under AILedger’s control

An optional overall score may be computed, but the dimensional scores and findings are authoritative.

Do not optimize for “no errors.”

The scoring philosophy must reward:
- discovering wrong assumptions
- changing decisions when evidence warrants it
- verifier/reviewer finding real issues before release
- raising uncertainty rather than inventing answers
- appropriate escalation
- efficient use of roles
- low unnecessary operator burden

It must penalize:
- ignored or untested important assumptions
- missed required capabilities
- ceremonial role invocation
- repeated same-root-cause repair failures
- avoidable operator intervention
- false-success conditions
- unnecessary workflow cost
- stale/incorrect recalled knowledge
- unjustified confidence
- bypassing governance without explicit waiver

Output

Create a dedicated retrospective artifact, separate from normal task lessons.

Suggested conceptual type:
WorkflowRetrospective

Include:
- task ID
- timestamp
- task characteristics
- participating providers/models/roles
- dimension scores
- evidence references
- strengths
- failures/misses
- unnecessary work
- operator interventions
- suggested workflow improvements
- candidate self-improvement lessons
- confidence
- version of the scoring rubric used

Self-improvement lessons

Workflow-retrospective findings should not be mixed directly into repository/domain lessons.

Maintain a distinct category/store for AILedger self-improvement knowledge.

Suggested distinction:

DomainLesson
- describes the repository, product, API, architecture, environment, or task domain

WorkflowLesson
- describes how AILedger itself should plan, route, research, verify, review, escalate, recall, monitor, or interact with the operator

Examples of WorkflowLesson:
- “When a task modifies duplicated command-time/replay logic, require explicit invariant verification.”
- “If only one provider is available, relax cross-provider verification transparently rather than requiring an operator waiver.”
- “Claim-by-claim coordinator feed monitoring creates unnecessary turns; prefer milestone event families.”
- “Tasks with unresolved external behavioral assumptions should activate TechnicalResearcher before implementation.”

WorkflowLesson lifecycle

Workflow lessons must remain evidence-based and revisable.

A retrospective may propose a WorkflowLesson, but one task should not automatically turn a weak observation into permanent policy.

Track:
- source retrospective/task
- evidence
- recurrence count
- confidence
- supporting/refuting future tasks
- last observed
- status: candidate / accepted / superseded / rejected

Repeated evidence should strengthen a workflow lesson.
Contradictory evidence should weaken or refine it.
A single anomalous task should not cause broad workflow mutation.

Self-improvement behavior

Initially:
- generate retrospective
- score behavior
- propose WorkflowLessons
- propose changes to skills/kernel/routing
- require operator approval before changing AILedger behavior

Later, after sufficient evidence exists, low-risk heuristic changes may become eligible for automatic adjustment, but hard governance rules and authority boundaries should remain operator-controlled unless explicitly designed otherwise.

Success criterion

The feature succeeds when normal use of AILedger produces enough structured evidence to answer:

- Did AILedger use the capabilities it should have used?
- Did it miss anything its own roles or policies implied?
- Did it waste effort?
- Did it interrupt the operator appropriately?
- Did it learn from being wrong?
- Did prior learning improve this task?
- What should AILedger do differently on the next similar task?

The purpose is not to prove that AILedger performed perfectly.

The purpose is to make AILedger progressively better at governing work.