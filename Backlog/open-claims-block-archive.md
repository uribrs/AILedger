make a claim something the task has to answer before it closes

`CLAUDE.md` opens with: anything you assume and then build on is a claim, record it first, then earn
it. the kernel enforces the recording rigorously. it enforces the earning not at all.

across the five tasks on disk:

    validated  142
    rejected     4
    open       183

56% of claims were never resolved. three tasks reached stage `archive` carrying 165 open claims
between them.

## why nothing stopped it

`EnsureStagePrerequisites`, Archive arm, `StageTransitionRules.cs`:

    no active runs
    no open challenges
    at least one lesson-bearing mark

it does not look at claims. a task can archive with every assumption it recorded still unexamined,
and the mint step will happily produce lessons from the validated minority while the open majority
disappears.

## the trend is the finding, not the number

    ledger-selfhost              40 validated    0 open
    ledger-artifacts             11             20
    stage-arms                   15 (+2 rej)    48
    ledger-learning              38 (+2 rej)    80
    decompose-command-handler    38             35

the first task closed everything. discipline decayed as volume rose, which is what discipline does,
and is the reason to move it into the kernel rather than to try harder.

## the mechanical cause

`resolveClaim` is held by an operator and a planning lead. it is not held by an implementation lead,
which is the role that writes most of the claims. resolving one means switching actor mid-flow, so
in practice nobody does. the capability split is correct — a lead should not grade its own homework —
but nothing routes the ungraded pile anywhere, so it just grows.

## the shape

two arms and one command.

- Archive refuses while any claim is `open`. this is the whole of it; everything else is how to make
  that refusal survivable.
- `claim close --id C7 --reason "..."` — a third disposition beside validated and rejected, for a
  claim that turned out not to matter. it needs no evidence, because it is not asserting the claim
  was true or false; it asserts the task stopped depending on it. it needs the same `resolveClaim`
  capability and it stays in the log, the way a waiver does.
- `status` grows an open-claim count, so the pile is visible before Archive is attempted rather than
  at the moment it is refused.

without the third disposition the arm is unusable: 183 claims cannot all be researched, and an arm
that can only be satisfied by work nobody will do becomes an arm that is always waived. that is the
failure mode `waivers-need-a-floor` describes from the other side.

## why this and not a warning

a warning is what `status` already effectively is. the point of the ledger is that a task cannot
quietly finish in a state it should not finish in, and an unanswered assumption is the specific
state this whole kernel was built to prevent. it is also the cheapest possible check — a dictionary
scan on an aggregate already in memory.

## relation to attention items

`attention-items-as-a-gate` puts the same kind of refusal on `work complete` for named tests. this
puts it on `stage transition --target archive` for claims. same mechanism, different record, and
they should probably be built in the same pass so the disposition vocabulary matches:
handled / accepted-risk / not-applicable there, validated / rejected / closed here.
