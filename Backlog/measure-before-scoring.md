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

## the check was run by hand, and it changes the plan

three archived tasks, projected by hand from `state.json` and `events.jsonl` with no new code:
`2026-09-08_1048-refusal-journal` (309 events, 16 runs), `2026-09-08_1430-recall-diversity`
(104 events, 6 runs), `2026-09-07_0957-stage-arms` (336 events, 22 runs).

| rubric dimension | answerable from the log today |
|---|---|
| 1 governance effectiveness | partly. refusals are counted only for tasks after item 5 shipped |
| 2 capability utilization | yes for "role ran". a usable proxy exists for "role contributed" |
| 3 epistemic quality | yes, and it discriminates. see below |
| 4 planning and decision quality | partly. supersessions and invalidations are counted; brief quality is not |
| 5 execution quality | partly. scope is recorded; duplicated work is not derivable |
| 6 verification and review quality | yes. role, provider, order and repair cycles are all present |
| 7 operator burden | yes, and it is the most damning number available |
| 8 governance cost | **no.** every cost field is null on every run in all three tasks |
| 9 learning behaviour | yes, and it scores every task the same, which means it scores nothing |
| 10 outcome quality | no. nothing in the log says whether the delivered result works |

### dimension 8 is empty, which fixes the ordering

    runs with cost recorded    0 of 16     0 of 6      0 of 22

wall clock and agent minutes *are* derivable — 3.5h/183 agent-minutes, 2.3h/131, 3.1h/259 — so the
dimension is not entirely dark. tokens, turns and time-to-first-write are absent from every run ever
recorded. item 6 is a hard prerequisite of item 9 and this is the measurement that proves it.

### the coordinator is invisible, and it is the largest cost

events by actor:

    refusal-journal     operator 240 of 309   (78%)
    recall-diversity    operator  81 of 104   (78%)
    stage-arms          operator 140 of 336   (42%)

the coordinating session writes most of the record and holds no run, so it has no `startedAt`, no
`endedAt`, no provider, no model and no cost — by construction, for the reason in
`the-coordinators-run-cannot-close.md`. every number dimension 8 would report describes the agents
that were dispatched and none of the agent doing the dispatching.

that is not a rounding error. the coordinator is one of the three cognitions on the machine and the
only one whose spend the retrospective cannot see. a governance-cost score computed without it is
wrong in a known direction and will read as cheap governance.

### dimension 9 cannot discriminate yet

    recalled 10  cited 2      refusal-journal
    recalled 10  cited 0      recall-diversity
    recalled  0  cited 0      stage-arms

`--from-lesson` exists and is used twice in three tasks. a dimension that returns the same score for
a task that learned and a task that did not is not measuring learning, it is measuring whether
anyone typed the flag. either the citation becomes routine first, or dimension 9 is scored as
"not measurable" rather than as 0.

### "role contributed" has a cheap proxy after all

the entry above listed this as one of the four dimensions that must wait for a scoring agent. events
written per role, divided by runs held, separates them without judgement:

    refusal-journal   claude-impl 32 events / 4 runs     codex-verify 28 / 5
                      codex-research 5 / 1               codex-review 4 / 2

a code reviewer that wrote four events across two runs and a researcher that wrote five in one are
visibly thinner than the worker and the verifier. that is not a quality verdict and should not be
presented as one, but it is a count, and it is the count a scoring agent would be reading anyway.

### the validation set does not fully exist

the four archived tasks this entry proposed as the validation set cannot all be scored. `stage-arms`
predates the refusal journal and predates `manifestHash`, so two of dimension 1's sharpest inputs
are simply absent from it — `manifest delivered: 0 of 22`, `no journal`. retroactive scoring is
therefore partial by construction, and calibration has to happen forward, on tasks run after item 6
lands, not backward over the four already closed.

### one interop detail for whoever writes the projection

timestamps are written with however many fractional-second digits .NET had — `.45326+00:00`, five
digits. python's `datetime.fromisoformat` accepts three or six and rejects five, so every external
reader of this log needs a normalising step. the C# projection will not hit it; anything scripted
around the log will.
