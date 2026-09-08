the kernel's refusals leave no trace

`self-scoring` asks whether governance materially affected the work. its strongest evidence class is
the refusal:

- "an agent attempted an invalid transition and the kernel refused it"
- "did any gate prevent premature or unsupported completion?"
- "an authority-requiring decision remained unavailable to an unauthorized actor"
- "inability to discover how to satisfy an already-correct gate"
- "repeated attempts to satisfy the same obligation"
- "work spent discovering what the kernel required"

none of that is in the ledger. a refused command throws `GovernanceException` and appends nothing.

## the size of the hole

    new GovernanceException in src/AILedger.Core/Application    141
    new GovernanceException in src/AILedger.Core/Domain         150

291 refusals, 141 of them reachable at command time. the twenty task logs in this repository hold
2,125 events between them and not one of those events is a refusal.

they are not absent from the record, though. 366 of those 2,125 events mention a refusal in their
prose — 135 evidence records, 96 claims, 40 constraints, 35 artifacts:

    evidence.added   135      claim.added        96
    constraint.added  40      artifact.recorded  35
    alternative.recorded 15   decision.proposed  14

so agents already treat refusals as the most citable thing about this kernel. having no event for
them, they write them into the records that do exist, as claims to be validated and evidence to be
cited. that is a hand-transcription of telemetry, it is only as accurate as the agent that noticed,
and a refusal nobody wrote a claim about is gone.

so the record says a gate exists and never says it fired. the completion gate is the clearest case:
`work complete` is refused until a verifier run has completed, which is the mechanism `self-scoring`
principle 3 is built on — model confidence is not system confidence. the log shows 21 verified
completions and cannot show a single refused attempt at one.

## why the two weakest dimensions are the two that matter most

`self-scoring` insists governance effectiveness and governance cost stay independently visible.
both are measured mostly from refusals.

effectiveness is refusals that changed an outcome. cost is refusals an agent could not satisfy —
the flailing. today the ledger shows only the eventual success, so a work item completed on the
first try and one completed after eleven refused attempts are the same two events.

`2026-09-07_1352-decompose-command-handler` is the illustration: 333 events, 23 completed runs,
seven work items, still at Discovery, never transitioned once. every arm in
`StageTransitionRules.cs` cost that task nothing because it never asked them anything. whether
that is a task that did not need stages or an agent that gave up on them is not answerable from
the log.

## the shape

a refusal journal, not an event.

    .ailedger/tasks/<id>/refusals.jsonl

per line: the timestamp, the actor, the command, the refusal message, and the task version the
attempt was made against. append-only, written by `CommandHandler` when it throws.

it must stay out of the event log and out of `GovernedTaskState`. `TaskDebt.cs:14` records why —
`state.json` is byte-compared against a fresh replay, so every field added to state changes every
task on disk. and `CLAUDE.md`'s twin rule is that replay must accept every history that was ever
legal; a log that grows with failed attempts is a log replay has to be taught to skip.

a journal read by nothing at replay time avoids both. it is telemetry, not truth.

## what it must not become

a punishment record. a refusal is usually the kernel working, and an agent that hits three of them
learning what the task requires is doing the right thing badly-informed, not misbehaving.

so nothing may gate on the journal, nothing may count refusals into a score without saying which
ones were productive, and no agent should be shown its own refusal count. the whole value is that
it distinguishes a gate that taught from a gate that could not be found.

## cost

one write path in `CommandHandler`, one file per task, one reader. no new event type, no state
field, no replay change, no gate.

the thing it is worth measuring against: if this ships and the journals stay near-empty, most
`self-scoring` dimensions are cheaper than the document assumes. if they fill up, dimension 8 is
where the ergonomics work is, and that is the answer the feature exists to give.
