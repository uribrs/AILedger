the owed block omits the one debt that hard-blocks completion

`make-status-say-what-is-owed` shipped so an operator could ask a task what it still owes.
`TaskDebt` reports five things:

    OpenClaims   OpenClaimsWithSupportingEvidence   WorkItemsAwaitingVerification
    LessonsRecalled   LessonsCited

it reports nothing about escalations, and an open escalation on a work item refuses
`work complete` outright.

## measured, by hitting it

`2026-09-08_1428-run-cost` at 21:07 UTC:

    owed: openClaims 0, openClaimsWithSupportingEvidence 0, workItemsAwaitingVerification 0

all clear. then:

    ailedger work complete --id W1
    error: Work item cannot be completed while an escalation on it is open.

two escalations, `IX1` and `IX2`, had been open since run `R1` finished four hours earlier. the
refusal was the first thing that surfaced them. the coordinator had read `status` repeatedly across
those four hours and was reading claims, runs and work items — the `owed` block said the item was
finishable and it was not.

worse, the two questions had both been answered by then. `IX1` asked whether `Turns` can mean one
thing for both providers and recommended the answer that later became accepted decision `D4`;
`IX2` asked whether codex `output_tokens` contains `reasoning_output_tokens`, which `C9` settled
from documentation. an agent escalated twice, was right twice, and got no acknowledgement because
nothing surfaced the escalations where the coordinator was looking.

## why this is the sharp case and not a nice-to-have

every other line in `owed` is a debt that shapes a judgement. an open escalation is the only one
that is a hard refusal. so the projection is silent about exactly the thing that cannot be worked
around, while reporting four things that can.

it also inverts the escalation's purpose. `CLAUDE.md` says an open escalation on a work item blocks
completing it, *which is the point: the question is answered before the work is called done.* that
only works if the question is visible. an escalation nobody reads is a block nobody understands
until it fires.

## the shape

one count, and probably two:

- `OpenEscalations` — the number blocking completion.
- `OpenEscalationsAwaitingOperator` — all of them, since only an operator resolves one; the
  distinction is worth having if a future role can resolve any kind.

`IsClear` gains one term. `TaskDebt` is a projection over state already in memory, written by
nothing and read by `status`, so there is no event, no state field and no replay counterpart.

the existing comment on `TaskDebt` is the constraint to respect: it counts and does not judge. a
count of open escalations is a count. do not add a severity, an age, or a "stale escalation" flag —
the moment it judges, it becomes a number an agent can move without answering the question.

## cost

one `Count` and one term in `IsClear`. smaller than the entry describing it.
