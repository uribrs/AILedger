score how the kernel governed the work, once there is something to score

the capability: after a governed task reaches closeout, produce an evidence-bound retrospective
saying whether AILedger's governance materially affected the work, what it cost, and which of that
cost bought confidence. it blocks nothing — not commit, push, pr, merge or task completion — and its
own failure must never reopen a task that completed successfully.

the rubric is `self-scoring.md`. that document is the operator's, it is the authority on what the
dimensions mean, and it is not a specification. this entry is the specification's shape and, more
importantly, the order.

## the three ideas in the rubric that are worth building for

- governance effectiveness and governance cost stay independently visible. `5 / 2` is a meaningful
  reading — the system protected the work well and made doing so painful — and one scalar destroys
  it.
- detected and corrected wrongness scores above accidental correctness. most retrospective systems
  have this backwards and reward clean histories.
- "role ran" is not "role contributed". that distinction is the whole value of the capability
  utilization dimension and nothing in the kernel currently answers it.

## why it comes last

it is telemetry. nothing waits on it, so it can be built slowly and correctly. what it cannot do is
score evidence that is not being recorded, and four things it needs to read do not exist yet.

**`record-the-refusals`** — the rubric's strongest evidence class is the refusal: a gate that
prevented premature completion, an authority boundary that held, an obligation an agent could not
find. 291 refusal sites in the kernel and 0 of 2,125 events is a refusal. blocking, and it is the
one that gates both headline dimensions at once.

**`see-inside-a-run`** — the cost dimension asks for turns, retries, repeated searches, idle waits
and work spent discovering what the kernel required. `AgentRunResult` already carries the provider's
own event stream and `CliApplication.cs:939` writes it to stdout and drops it. without this, cost is
measured over the fraction of cost the ledger happens to see, which reads as a measurement and is
not one.

**`measure-before-scoring`** — the deterministic half. counts, durations and the causal chains the
event log can already join, with no model and no scores. it is also the diagnostic: 198 of 205
resolved claims validated, 11 refuting evidence records in 558, two causal-chain events in the
entire corpus. if the projection comes back near-empty, the scoring agent has nothing to read and
the answer is to fix recording, not to write better prose.

**`route-the-workflow-lesson`** — a WorkflowLesson has no kind field and no recall route. by the
rubric's own definition a mechanism contributing no correction, evidence, confidence or durable
reasoning is a ceremony candidate, and a retrospective nothing reads qualifies.

## what is left after those four ship

four dimensions of the ten, and they are the ones that genuinely need a model reading a history:

- did a role contribute, or merely run
- ceremony candidates — a mechanism that repeatedly buys nothing
- epistemic quality — assumptions identified, tested, revised
- brief concreteness — whether enumerated obligations produced better work than vague ones

the other six are mostly counts and joins, and `measure-before-scoring` produces them.

## the decision this entry reverses, which should be recorded as one

`TaskDebt.cs:8`:

> the moment it makes a judgement it becomes a number an agent can move without doing the work,
> which is worse than no number at all.

that is why `status --owed` reports counts and no score. this capability is ten scored dimensions
with evidence, explanation, confidence and controllability on each. it is the thing that comment
refused, at ten times the surface.

the reversal may well be right — a retrospective read by an operator after closeout is not a number
an agent can move mid-task. but it is a reversal, and it belongs in the ledger as an accepted
decision or a recorded alternative rather than as a feature that quietly contradicts a comment.

## the question the rubric does not ask of itself

rubric principle 3: model confidence is not system confidence. an agent believing its work is
correct is not evidence that the work is correct.

the rubric does not apply that to the scoring agent. the retrospective is an agent rating the
performance of agents, with no verifier, no independent contradiction, and its dimensional scores
declared authoritative. every argument the document makes for why implementation confidence needs
independent verification applies to it.

this needs an answer before any score is called authoritative. the cheapest one is that the
retrospective is a proposal an operator disposes, the same way an escalation is — which is already
the model the document uses for WorkflowLesson promotion.

## calibration

four archived tasks with known verdicts, items 1 to 4, and the notes already say what happened in
each: item 1's single waiver and why, item 3's `VC1`, `KC4` and `KC5`, item 4's eight defects
including a feature non-functional on a clean tree and four tests that passed while proving nothing.

score those four before writing scoring prose. if the rubric cannot reproduce verdicts that are
already written down, it does not work, and ten dimensions of drift-free-looking numbers across
twenty future tasks is the failure mode. this costs one afternoon.

## the shape

    ailedger retrospective build  --task <id>          # measurement, no model, no scores
    ailedger retrospective record --task <id> ...      # the scored artifact, an operator disposes

a `WorkflowRetrospective` artifact kind, and a retrospective is never part of the archive
transition. archiving mints lessons inside one event batch; a detached process fired from that path
buys a half-written retrospective on a successfully archived task, which is the failure the rubric
itself warns against. an operator command run after the fact has no such failure mode.

## what it must not become

- a gate. nothing in the kernel may refuse on a score.
- a single scalar. the rubric is explicit and it is right.
- a number an agent sees about itself. a score an actor can read is a score an actor will optimise.
- a policy change on one task's evidence. one expensive occurrence is not ceremony; the rubric says
  so and the lifecycle should enforce it by accumulating citations rather than by editing a record.

## cost

the largest entry in this backlog, and the only one whose cost is mostly not code. the four
prerequisites are small and independently useful. what is expensive is the rubric work: writing the
four judgement dimensions so that two runs over the same history agree, and calibrating them
against tasks whose verdicts are known.

that expense is the point. `self-scoring` is the telemetry the kernel needs to improve itself, and a
retrospective that produces plausible numbers is worse than no retrospective, because plausible
numbers get acted on.
