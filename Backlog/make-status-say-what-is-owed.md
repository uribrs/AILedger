`status` should say what the task still owes, without refusing anything

the ledger knows every number in my review. none of them are visible without a script.

- 183 open claims across five tasks; 80 in one
- 14 waivers against 29 stage transitions
- one waiver whose reason is `x`
- work items with a completed working run and no verifier run yet
- lessons recalled and never cited by anything

each of those took a `python3 -c` over `events.jsonl`. an operator running `status` sees none of
them.

## the argument for visibility over refusal

every other entry on this list that proposes a gate has the same weakness: a gate fires once, at the
end, when the cost of fixing is highest and the temptation to waive is strongest. that is how
`ledger-artifacts` ended up with `reason: "x"`.

a running count has the opposite shape. it is cheap, it fires continuously, and it lets the operator
decide when the pile is worth clearing. it makes discipline a choice rather than an oversight,
which is the most that can be asked of a tool that must not slow the work down.

it is also the only proposal here that costs nothing to ignore.

## the shape

a short block at the end of `status`, printed only when a count is non-zero:

    owed
      claims        48 open   (14 have supporting evidence and no refuting evidence)
      waivers        3 of 9 transitions waived
      verification   2 work items completed a working run, none has a verifier run
      lessons        6 recalled, 0 cited by a claim or decision

four lines, all derived from state already in memory, no new events, no new rules. the parenthetical
on the first line is the one that matters — it is the batch `resolve-claims-where-the-evidence-lands`
can clear in a single pass, so the two entries are the same feature seen from either end.

## the one thing to resist

do not add a score. do not colour it. do not say "healthy" or "at risk". the moment this block makes
a judgement it becomes something to optimise, and a number an agent can move without doing the work
is worse than no number. these are counts of things the task genuinely still owes, and the operator
is the one who decides whether any of them matter for this task.

`self-scoring` is where judgement belongs, after the fact, with evidence. this is just the receipt.

## cost

one projection function over `GovernedTaskState`, called from one place. no persistence, no schema,
no twin, nothing to replay. it is the smallest entry here and probably the highest ratio of value to
risk on the whole list.
