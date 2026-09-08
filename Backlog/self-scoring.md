AILedger 2.0 — Automatic Workflow Retrospective
Goal

After a governed task reaches successful closeout, automatically evaluate how well AILedger itself governed the work.

The retrospective exists to answer two separate questions:

Did the governance improve confidence in the delivered result?
What did that governance cost, and was the cost productive?

These questions must not be collapsed into one.

A workflow may be expensive and highly valuable because it discovered defects, invalidated bad assumptions, prevented false completion, or forced necessary repair.

A workflow may also be expensive because agents flailed, obligations were unclear, work was duplicated, roles repeated one another, or the system imposed ceremony that produced no useful evidence.

The purpose of the retrospective is to distinguish those cases.

It must NOT block commit, push, PR creation, merge, or normal task completion.

It is a learning mechanism for improving AILedger’s own governance behavior over time. It is not the same thing as a repository/domain lesson.

Examples of normal lessons:

“This repo publishes artifacts to S3.”
“TaskTransitionValidator duplicates command-time rules intentionally.”
“This API requires pagination token X.”

Examples of workflow-retrospective learning:

“Research should have activated earlier for this task shape.”
“The verifier caught a defect that the planning gate should have exposed.”
“Cross-provider verification added value here.”
“Three repair attempts repeated the same root cause and indicate poor convergence.”
“The operator had to manually request decomposition even though the task characteristics warranted it.”
“A role activated but contributed no distinct evidence or correction.”
“The completion gate correctly prevented an implementation agent from accepting its own defective stopping condition.”
“The gate was valuable, but its remaining obligations were insufficiently visible and caused avoidable agent flailing.”
Core scoring principles
1. Do not reward apparent infallibility

AILedger should not optimize for histories in which nothing went wrong.

A task in which:

an assumption was wrong,
research refuted it,
the decision changed,
dependent work was corrected,
verification found a defect,
the defect was repaired,
review established confidence,

may represent better governance than a task in which everyone immediately claimed success and no one challenged anything.

The retrospective must reward detected and corrected wrongness.

It must not reward accidental correctness, unchallenged confidence, or clean-looking histories merely because they contain fewer failures.

2. Immutable failure history is not itself failure

Cancelled runs, rejected claims, refuted assumptions, abandoned alternatives, failed verifier runs, and repaired defects are not negative merely because they exist in the task history.

They may be evidence that governance worked.

Score what happened after the failure became knowable:

Was it detected?
Was it recorded accurately?
Did the appropriate actor respond?
Were dependent decisions/work reconsidered?
Was the root cause repaired?
Was confidence re-established before completion?

Do not incentivize artificially clean ledgers.

3. Model confidence is not system confidence

An implementation agent believing its work is correct is not evidence that the governed task is complete.

Explicitly evaluate whether AILedger prevented an actor from using its own confidence as a substitute for required independent evidence.

Examples of positive governance evidence:

implementation agent considered work finished but completion remained unavailable;
verifier contradicted the implementing agent and the finding survived;
required repair occurred before completion;
reviewer independently found an issue after verification;
an agent attempted an invalid transition and the kernel refused it;
an authority-requiring decision remained unavailable to an unauthorized actor.

These are signs that governance boundaries had practical effect.

4. Governance effectiveness and governance efficiency are independent

Never infer:

expensive = ceremonial

or:

defect caught = workflow efficient

Evaluate both independently.

Example:

Verifier finds HIGH defect → implementation repaired → re-verification passes

Governance effectiveness: strong.

If this required one clear repair cycle, governance efficiency may also be strong.

If the same process required six attempts because the agent could not determine what the verifier required, governance effectiveness may remain strong while governance efficiency is weak.

Likewise:

Four mandatory roles run → none contributes new evidence, correction, challenge, or confidence

may indicate ceremonial activation even if the task succeeds.

5. Distinguish productive friction from avoidable friction

Some friction is intentional.

A completion gate preventing an implementation agent from overruling an independent verifier is productive friction.

An operator-authority boundary preventing an agent from silently waiving a requirement is productive friction.

Repeated searches caused by missing context, unclear owed work, duplicate review, unnecessary provider launches, or inability to discover how to satisfy an already-correct gate are avoidable friction.

AILedger should optimize for:

minimum process capable of establishing the required confidence

—not minimum process, and not maximum governance.

Execution behavior
Trigger automatically after successful task closeout.
Run asynchronously and non-blocking.
Failure of the retrospective must never invalidate or reopen an otherwise successfully completed task.
Record retrospective failure separately if it cannot complete.
Use the completed task’s event history, artifacts, claims, decisions, alternatives, research, run metadata, verifier/reviewer results, escalations, lessons, waivers, operator interventions, refusals, repairs, retries, stage transitions, and final outcome as evidence.
Do not rely on an agent simply rating its own performance from memory.
Scores and findings must be tied to observable task evidence wherever possible.
Distinguish observations from causal inference. For example, a verifier finding a defect proves that verification detected it; it does not automatically prove that no other mechanism would have detected it.
Where causal contribution is reasonably observable, record it explicitly:
assumption refuted → decision changed;
verifier finding → repair;
recalled lesson → constraint/decision changed;
kernel refusal → prohibited transition prevented;
operator intervention → workflow resumed or authority decision supplied.
Retrospective dimensions
1. Governance effectiveness

Evaluate whether AILedger’s governance mechanisms materially affected the work.

Questions include:

Did any gate prevent premature or unsupported completion?
Did authority boundaries prevent an actor from overriding evidence or making a decision it did not own?
Did independent verification or review contradict the implementing actor?
Did refusals prevent illegal or unsafe workflow transitions?
Did provenance make responsibility and causal history clear?
Did the governed process cause defects, invalid assumptions, or missing evidence to be addressed before completion?
Were governance mechanisms merely present, or did they actually change behavior or outcome?

Do not require every successful task to contain a dramatic catch.

A straightforward task that passes appropriately scoped governance with little friction can also score highly.

2. Capability utilization
Which roles/capabilities should reasonably have activated?
Which actually activated?
Which required capabilities were missed?
Which capabilities activated unnecessarily or ceremonially?
Did roles perform distinct functions, or merely repeat one another?
Did the operator have to manually request something AILedger should reasonably have inferred?
Were capabilities activated at the correct time?

Distinguish:

role ran

from:

role contributed.

3. Epistemic quality
Were important assumptions identified?
Were they tested where appropriate?
Did research meaningfully confirm, refine, or refute them?
Was evidence used correctly?
Were decisions revised when evidence changed?
Were rejected alternatives and their reasons preserved where useful?
Was uncertainty surfaced rather than hidden?
Did the workflow distinguish evidence from confidence?

Treat justified changes of mind as success.

Do not reward being accidentally correct without validation over being initially wrong and correctly self-correcting.

An open claim with substantial supporting evidence should not automatically be treated the same as an ignored, untested claim. Distinguish epistemic work performed from administrative disposition not completed.

4. Planning and decision quality
Did scope and decomposition match the actual work?
Were architectural/environmental constraints discovered early enough?
Did decisions follow evidence?
Were invalidated decisions/work items correctly revisited?
Did planning prevent or create rework?
Were agent briefs sufficiently concrete?
Did decomposition create useful independence?
Were attention items and required tests specific enough to guide execution?

Pay particular attention to cases where vague instructions produced weak work but enumerated obligations produced materially better work.

5. Execution quality
Did implementation remain within governed scope?
Was work unnecessarily duplicated?
Was there avoidable scope drift?
Were missing requirements escalated rather than invented?
Did workers respect recorded constraints and decisions?
Did workers converge efficiently after defects were identified?
Did implementation agents prematurely declare success?
If they did, did the governed environment prevent that declaration from becoming task completion?
6. Verification and review quality
Did verifier/reviewer roles activate when required?
Were they sufficiently independent from implementation?
Did they find meaningful defects?
Did verifier and reviewer contribute distinct value?
Were findings correctly repaired?
Was repair subsequently demonstrated?
Did review occur early/late enough to remain actionable?
Were defects found that should reasonably have been caught by an earlier stage?

A verifier/reviewer finding a real defect before governed completion is positive evidence for that mechanism.

Do not penalize the initial discovery as though the defect had escaped the process.

Penalize instead:

ignored findings;
unjustified dismissal;
false repair;
repeated failure to repair the same root cause;
verifier/reviewer ceremony with no meaningful examination;
defects escaping despite a stage explicitly responsible for detecting them.
7. Operator burden
How often did the operator need to intervene?
Which interventions legitimately required operator authority?
Which indicate AILedger failed to use its own capabilities?
Did the system interrupt unnecessarily?
Did it fail to interrupt when operator authority was actually needed?
Did the operator have to remind agents to continue an already-decided workflow?
Did the operator have to explain obligations the kernel/task state already knew?

Do not penalize necessary authority decisions.

Do penalize avoidable hand-backs such as:

agent stops and reports status when the governed next action is already determined.

8. Governance cost and convergence efficiency

Explicitly measure the cost imposed by governance.

Consider:

wall-clock time;
number of agent runs;
failed/retried runs;
verifier/reviewer iterations;
repeated searches;
duplicate work;
idle waits;
provider-launch failures;
unnecessary context construction;
event/record volume where relevant;
operator turns/interventions;
repeated attempts to satisfy the same obligation;
work spent discovering what the kernel required.

Classify substantial cost where possible as:

Productive governance cost

Cost that generated meaningful confidence, correction, evidence, or preserved reasoning.

Examples:

verifier run that found a real defect;
research that refuted an assumption and changed design;
review that found a second-order issue;
explicit alternative recording that prevented future re-derivation;
re-verification after meaningful repair.
Necessary but non-discriminating cost

Required work that produced no new finding but was reasonable given the task risk.

Example:

independent verification passes cleanly on a high-risk change.
Avoidable workflow cost

Cost caused by poor orchestration or ergonomics.

Examples:

agent repeatedly stopping before required stages;
multiple runs launched without sufficient briefing;
same search repeated because evidence was not surfaced;
unclear owed obligations causing flailing;
duplicate verifier/reviewer work with no distinct purpose;
stale tooling causing false failures;
workflow topology making a required action impossible.
Ceremony candidate

Repeated mechanism that consistently contributes neither correction, evidence, confidence, nor useful durable reasoning.

One occurrence should not automatically justify removing a governance mechanism. Accumulate evidence across tasks.

9. Learning behavior
Were relevant prior lessons recalled?
Did recalled lessons materially affect current work?
Were recalled lessons validated against current reality where necessary?
Were new lessons worth preserving identified?
Were redundant, trivial, or overly task-specific lessons avoided?
Did stale or misleading lessons reduce quality?
Did the task repeat a known failure mode despite recalling the relevant lesson?
If so, was the problem lack of knowledge, poor context delivery, weak enforcement, or execution failure?

A recalled lesson is not valuable merely because it appeared in context.

Prefer evidence of:

lesson recalled → claim/constraint/decision/verification changed.

10. Outcome quality
Did the delivered result satisfy the task contract?
Were important success criteria demonstrated rather than assumed?
Did semantic/invariant checks go beyond merely observing green tests where appropriate?
Did any known compromise or waiver materially reduce confidence?
Were defects known at completion?
Did the final result preserve required architectural invariants?
Is confidence proportional to the evidence actually collected?
Scoring

Do not initially collapse everything into one opaque scalar.

Score each dimension from 0–5:

0 — failed / absent
1 — seriously deficient
2 — weak
3 — acceptable
4 — strong
5 — excellent

Each dimension must include:

score;
supporting evidence;
explanation;
confidence;
whether observed problems were under AILedger’s control;
where useful, causal links to subsequent events.

An optional overall score may be computed later, but dimensional scores and evidence remain authoritative.

Most importantly, governance effectiveness and governance cost must remain independently visible.

A task may legitimately score:

Governance effectiveness: 5
Governance cost/convergence: 2

That means:

The system protected the work extremely well, but made doing so unnecessarily painful.

The correct response is then to improve ergonomics/convergence without removing the protection that earned the effectiveness score.

Do not optimize for “no errors”

Reward:

discovering wrong assumptions;
changing decisions when evidence warrants it;
verifier/reviewer finding real issues before release;
preventing an implementing actor from unilaterally declaring success;
preserving rejected alternatives and failure reasoning where useful;
raising uncertainty rather than inventing answers;
appropriate escalation;
efficient use of roles;
low unnecessary operator burden;
productive independent disagreement;
successful repair and re-verification;
governance mechanisms demonstrably changing behavior when needed.

Penalize:

ignored or untested important assumptions;
missed required capabilities;
ceremonial role invocation;
repeated same-root-cause repair failures;
avoidable operator intervention;
false-success conditions;
unnecessary workflow cost;
stale/incorrect recalled knowledge;
unjustified confidence;
bypassing governance without explicit waiver;
repeatedly knowing a relevant lesson but failing to operationalize it;
status reporting used as an unnecessary hand-back instead of continuing already-authorized work.
Output

Create a dedicated retrospective artifact separate from normal task lessons.

Suggested conceptual type:

WorkflowRetrospective

Include:

task ID;
timestamp;
task characteristics/risk shape;
participating providers/models/roles;
dimension scores;
evidence references;
causal chains where identifiable;
governance mechanisms that materially affected the task;
defects/incorrect assumptions caught before completion;
strengths;
failures/misses;
productive governance cost;
avoidable workflow cost;
ceremony candidates;
operator interventions;
convergence/repair history;
suggested workflow improvements;
candidate self-improvement lessons;
confidence;
scoring-rubric version.

Where useful, explicitly capture short causal chains such as:

Implementation claimed ready
→ independent verifier found defect
→ completion remained unavailable
→ implementation repaired defect
→ re-verification passed
→ completion became legal

This is stronger evidence than simply counting verifier runs.

WorkflowLesson vs DomainLesson

Workflow-retrospective findings must not be mixed directly into repository/domain lessons.

DomainLesson

Describes the repository, product, API, architecture, environment, or task domain.

WorkflowLesson

Describes how AILedger itself should plan, route, research, brief, verify, review, escalate, recall, monitor, constrain authority, or interact with the operator.

Examples:

“When a task modifies duplicated command-time/replay logic, require explicit invariant verification.”
“If only one provider is available, relax cross-provider verification transparently rather than requiring an operator waiver.”
“Claim-by-claim coordinator feed monitoring creates unnecessary turns; prefer milestone event families.”
“Tasks with unresolved external behavioral assumptions should activate TechnicalResearcher before implementation.”
“A code-bearing work item must not become completed before its required code-reviewer run, because completion otherwise makes review impossible.”
“When a launched agent repeatedly misses a known obligation, inspect whether the obligation is present in its actual brief before treating the problem as agent noncompliance.”
WorkflowLesson lifecycle

Workflow lessons must remain evidence-based and revisable.

A retrospective may propose a WorkflowLesson, but one task should not automatically turn a weak observation into permanent policy.

Track:

source retrospective/task;
evidence;
recurrence count;
confidence;
supporting future tasks;
refuting future tasks;
last observed;
affected task shapes;
status: candidate / accepted / superseded / rejected.

Repeated evidence strengthens a workflow lesson.

Contradictory evidence weakens, narrows, or refines it.

A single anomalous task should not cause broad workflow mutation.

A mechanism that is expensive in one task should not automatically be labelled ceremony.

Self-improvement behavior

Initially:

generate retrospective;
score behavior;
identify causal evidence;
propose WorkflowLessons;
propose changes to skills/kernel/routing/context assembly;
identify opportunities to reduce governance cost without weakening demonstrated protections;
require operator approval before changing AILedger behavior.

Later, after sufficient evidence exists, low-risk heuristic changes may become eligible for automatic adjustment.

Hard governance rules, authority boundaries, completion criteria, and safety-relevant invariants should remain operator-controlled unless explicitly designed otherwise.

The optimization target should be:

Preserve or increase confidence while reducing avoidable governance cost.

Not:

make the workflow faster.

And not:

make the workflow stricter.

Success criterion

The feature succeeds when ordinary use of AILedger produces enough structured evidence to answer:

Did AILedger use the capabilities it should have used?
Did its governance materially affect the work?
Did it prevent unsupported or premature completion?
Did independent disagreement expose anything valuable?
Did it miss anything its own roles or policies implied?
When something went wrong, did the system detect and recover from it?
Which friction produced confidence or correction?
Which friction was avoidable?
Did the system waste effort?
Did it interrupt the operator appropriately?
Did agents unnecessarily hand control back to the operator?
Did it learn from being wrong?
Did prior learning materially improve this task?
Did the workflow converge efficiently after defects were discovered?
What should AILedger do differently on the next similar task without weakening the protections that worked?

The purpose is not to prove that AILedger performed perfectly.

The purpose is not to minimize ceremony blindly.

The purpose is to make AILedger progressively better at producing trustworthy work with the least unnecessary governance cost.