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

## what the journal bought, read in full

33 rows across five tasks, every one read. This is the first direct evidence for the rubric's central
question — did a governance mechanism actually change behaviour — and it separates productive from
avoidable friction the way `self-scoring` asks.

    productive              26 of 33
    avoidable ergonomics     4 of 33      one bug, four times
    unclear                  1 of 33
    one productive rule fired four times because its requirement was undiscoverable

### the rule that fires most is the epistemic one, and it fires at the coordinator

Five rows read `Evidence 'X' does not support claim 'Y'`. **All five were the operator** — the
coordinating session, resolving a claim with evidence that did not name it. Not one launched agent
tripped this rule; the coordinator tripped it five times across two tasks.

That is the single most valuable rule in the kernel by this count, and its target is the actor with
every capability. `self-scoring` asks whether governance prevented an actor from using its own
confidence as a substitute for evidence. Here is the answer, five times, against the actor least
constrained by anything else.

### the one row worth the whole journal

    Work item cannot be completed while an escalation on it is open.

Two escalations had been open for four hours. `owed` reported all zeros the whole time, because
`TaskDebt` does not count escalations — `owed-does-not-say-what-blocks.md`. The refusal was the first
thing in the system that mentioned them. Both questions had already been answered by other routes, so
an agent escalated twice, was right twice, and got no acknowledgement until a gate fired.

### four refusals are one bug

    Actor 'claude-impl' lacks capability 'RecordAlternative'.   ×4, two tasks, two days

`RoleDefaults` grants `RecordAlternative` to the two lead roles only. Worker, Researcher, Verifier and
CodeReviewer are all refused — four of the six non-operator roles. `CLAUDE.md` documents the command
with `--actor claude-impl`, a worker, in its own example, and the manifest tells a launched agent to
record what it discarded.

So the worker did the right thing four times and was refused four times. What is lost is the most
evidence-bearing kind of discarded approach there is: one an implementer actually tried. Recorded as
C28 with E38 and E39. `RoleDefaults.cs` sits in `src/AILedger.Cli`, held by item 6's W2, so it is
recorded rather than fixed on the spot.

`EnsureSafe` in the same file names the four capabilities deliberately withheld from non-operators —
`ManageRoles`, `ManageScope`, `ResolveEscalation`, `ManageConstraints`. `RecordAlternative` is not
among them, and the file carries no comment about it while the rest of this codebase comments every
rule. It reads as an omission, not a decision.

### a productive rule can still be avoidable friction

Four consecutive rows on `2026-09-08_0910-tenableio-export-slot-teardown` read:

    Artifact body is missing the required 'id | name | failure mode | causal path and impact
    | planned handling | source' table

The rule is right — a verifier must dispose every attention item. Firing it four times against the
same agent is the other thing the rubric asks about: the obligation was correct and undiscoverable,
so the cost bought no new confidence after the first refusal. That is `attention-items-are-task-wide-but-work-is-not.md`
and it is the clearest ergonomics signal in the journal after C28.

### what the productive 26 actually prevented

An unresumable run recorded as completed. A lead authoring the user's own request. Design entered
with no researcher run. Execution entered with no work item and no `UserRequest`. A verifier run
closed with no verifier output. An illegal `Verification → Discovery` transition. A work item owned by
an actor with no role. Research entered with nothing to investigate. A malformed escalation whose
recommendation named none of its options. A second verifier and a second reviewer output silently
replacing the first without superseding it. A duplicate evidence id — the exact collision `CLAUDE.md`
warns two concurrent writers about. Two provider launches reaching outside their work item's scope. A
cross-task claim reference, and the dependent resolve that followed it. A verifier output that left a
dependent claim undisposed.

Fifteen distinct mechanisms, each of which changed what happened. None of them is ceremony.
