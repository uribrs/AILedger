measure the task before scoring it

`self-scoring` is ten dimensions scored 0-5 by an agent reading a task's history. most of what it
asks for needs no agent at all, and the half that does should not be built until the half that
does not has been read.

`TaskDebt.cs:8` already settled the principle for `status`: the moment a projection makes a
judgement it becomes a number an agent can move without doing the work, which is worse than no
number at all. that is why `status --owed` reports counts and no score.

this entry is the same shape, over the event log instead of state, and it is `self-scoring`'s
first half rather than a separate feature.

## what is already linked and can be read today

the kernel records causation in the event, not in prose:

| the document asks for | the event that already says it |
|---|---|
| assumption refuted → decision changed | `decision.invalidated` carries `rejectedClaimId` |
| refuted claim → dependent work reconsidered | `work.invalidated` carries `rejectedClaimId` |
| claim superseded → dependents repointed | `claim.dependencies-repointed`, `SupersessionOutcome` |
| challenge overturned a decision | `decision.overturned` carries `challengeId` |
| recalled lesson → claim/decision/alternative changed | `fromLesson` on all three |
| verifier did not re-run after the latest work | `WorkItemRules.HasVerifierRunAfterLatestWork` |
| one provider verified its own work | `WorkItemRules.ProviderThatVerifiedItsOwnWork` |
| completion gate waived | `work.completed` carries `withoutVerificationReason` |
| stage arm waived | `stage.prerequisites-waived` carries the stage and reason |
| repair cycles | repeated `verification → repair → verification` in `stage.transitioned` |
| runs, failures, cancellations by role | `run.started` / `run.completed` with `subjectRole` |
| a brief actually reached the adapter | `run.completed` carries `manifestHash` |
| wall clock and run duration | event timestamps, `startedAt` / `endedAt` |

none of that requires a model. all of it is derivable, testable and unforgeable by prose.

## the measurement that should change how the feature is built

across all twenty task logs:

    claim.resolved              205    validated 198   rejected 6   superseded 1
    evidence.added              558    supports-only 519   refutes-only 11   both 8   neither 20
    decision.invalidated          1
    decision.overturned           1
    work.invalidated              0
    claim.dependencies-repointed  0
    escalation.raised            17    businessDecision 17   trueUnknown 0
    work.completed               29    verified 21   waived 8
    stage.prerequisites-waived   15
    lesson.recalled              23    cited via --from-lesson  2

96.6% of resolved claims were validated. 2% of evidence refutes anything. the causal chains the
document wants to record are present in the schema and almost absent from the history: two events
in 2,125.

`self-scoring` is explicit that it must reward detected and corrected wrongness and must not reward
clean-looking histories. this repository's own ledger is the clean-looking history it warns about.

there are two readings and they need different work:

- governance is catching little, because most claims here are read off source and are simply true.
- governance is catching things and they are not being recorded as refutations. a verifier finding
  a defect currently lands as a challenge, an artifact, or a new validated claim about the fix —
  not as a rejected claim with refuting evidence.

item 4 in `Priorities.md` is the case against the first reading: eight defects found by its working,
verifier and reviewer runs, including a feature entirely non-functional on a clean tree and four
tests that passed while proving nothing. the ledger for that task records six rejected claims
across the whole repository, so most of those eight defects are not in the record as wrongness at
all.

so the deterministic projection is not just cheaper than the scoring agent. it is the thing that
tells us whether the evidence a scoring agent would read exists yet.

## the shape

    ailedger retrospective build --task <id>

json out, no scores, no prose. counts, durations, and the causal chains it can actually join. it
reads a finished task and refuses nothing.

it must not be part of the archive transition. archiving mints lessons inside one event batch, and
`self-scoring` requires the retrospective never invalidate a completed task; a detached process
fired from that path buys a half-written retrospective on a successfully archived task. an operator
command run after the fact has no such failure mode.

## validation set

four archived tasks with known verdicts, items 1 to 4 in `Priorities.md`, and the notes already say
what happened in each: item 1's single waiver and why, item 3's `VC1`, `KC4` and `KC5`, item 4's
eight defects.

if the projection cannot surface those from the logs alone, the scoring agent has nothing to read
and the rubric cannot be calibrated. that check costs one afternoon and should happen before any
scoring prose is written.

## cost

one projection over an already-parsed event stream, in `AILedger.Core/Application` beside
`TaskDebt`. no new event type, no state field, no replay counterpart — it is a read, like
`TaskDebt`, and for the same reason.

what it deliberately excludes: anything requiring judgement. "did the role contribute or merely
run", ceremony candidates, epistemic quality, brief concreteness. four dimensions of the ten, and
they wait for the scoring agent.
