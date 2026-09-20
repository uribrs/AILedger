# The refusal is the best query in the system and nothing reads it

## The observation

When the kernel refuses a command it holds a perfectly formed query: the command type, the actor,
the rule that fired, and the exact message. It knows precisely what someone tried and precisely why
it said no.

It says no, writes a row, and forgets.

    144 refusal rows across 35 tasks
    0 of them ever read back by anything

Re-measured 2026-09-20: **667 rows across 52 tasks**. Still 0 read back at the moment of refusal.

The refusal journal shipped as item 5. Item 7's retrospective counts refusals after the fact. Nothing
uses one at the moment it happens, which is the only moment it is worth anything to the actor who
caused it.

## Why this is the sharpest retrieval in the kernel

Every other recall question is fuzzy. *What is this task about?* — four tags typed at opening.
*What is this work item like?* — a title and a scope. Those need semantic search, an embedding
model, a local service and an evaluation to prove they help.

A refusal is an **exact key**. The rule text is a literal string, the command type is an enum, and
the events following each refusal in the log record what that actor did next. So the question

> who else hit this rule, and what did they do about it?

is a SQL join over rows that already exist. No model, no embeddings, no new dependency, no
evaluation to earn first.

## What it would look like

    $ ailedger stage transition --task T --actor operator --stage research
    error: Research requires at least one open claim to investigate.

           3 other actors hit this rule.
             2 waived it with a reason, 1 opened a claim and proceeded.
             most recent: 2026-09-09_1010 — "the investigation is finished,
             nothing left to open. Filing one now would be a claim written to
             satisfy a gate."

The refusal text is unchanged. What follows is what the corpus knows about that exact rule.

## Measured against real failures

Four things went wrong for the coordinator on 2026-09-09. Scored against where retrieval could have
caught them:

| what happened | catchable at | by what |
|---|---|---|
| `C10` — a work item's scope cannot be widened, costing a released item and a discarded verification | `work add` | the scope list is the query; row 34 already recorded the same field failing from the worktree side |
| a malformed orchestration plan cost verifier `R2` its entire run | **the refusal** | the message named the missing table verbatim, and prior tasks had filed correct ones |
| `set -- $pair` failing silently in zsh, fourth occurrence | nothing | outside the kernel's reach — a harness failure, not a ledger one |
| `truncate: false` rejecting oversized embedding input | nothing | genuinely novel; no corpus contained it |

Two of four were catchable at a specific command, and one of those two at the refusal itself. That
is the honest hit rate, and it is worth the small cost of getting it.

## The distinction this entry rests on

Two different machines have been called "learning" in this repository:

    already learned      retrieval keyed to the command being issued    mostly unbuilt
    not yet learned      cross-model verification                       built and working

The second is measured: 7 defects across three tasks were the verifier's to find and the code
reviewer found them, and on `2026-09-09` a verifier found a defect living inside the fix for another
defect. The pipeline handles the genuinely novel.

Retrieval handles what someone already paid for. Conflating them is why the first has looked
healthier than it is.

## Scope

Small, and deliberately narrower than the semantic index:

- On refusal, look up prior rows with the same rule and command type.
- Report how many, how they were dispositioned — waived, worked around, escalated, abandoned — by
  reading the events that follow each prior refusal in its own task.
- Print it after the refusal message, never instead of it.
- Cap it. Three prior occurrences is a hint; twenty is noise that trains people to ignore the
  refusal itself. **Amended below:** this cap is correct for prior occurrences by other actors and
  wrong for an actor's own repetition of one rule inside one task, which must not be capped at all.

## What it must not become

- **A suggestion the kernel endorses.** It reports what others did, not what you should do. Two of
  the three prior actors waiving a rule is a fact, not a recommendation, and a refusal that reads
  like permission is worse than one that reads like a wall.
- **A reason to soften the refusal.** The message stays exactly as it is. This is added context, not
  a negotiation.
- **Cross-repository by default.** A rule fires the same way everywhere, but the dispositions are
  local judgement. Start with this ledger.

## Amendment, 2026-09-20 — one cap cannot answer two questions

Re-measured after `2026-09-17_1440-closeout-synthesis-governed`, a task that produced 32 refusals
and whose journal was read back only by hand, days later, because the operator asked why there were
32.

    667 refusal rows across 52 tasks           (144 across 35 when this entry was written)
    343 distinct rule-in-one-task clusters
    176 rows — 26% — past the third occurrence of their rule inside their own task
     28 rows in the largest single cluster     2026-09-15_0656-parser-retirement-normalizer
      0 read back at the moment of refusal

Clusters are keyed by task and rule template — the message with quoted identifiers and numbers
normalised — because `site` reads `service` on every row and identifies nothing.

A refusal can answer two questions and the scope above caps both at three. The cap is right for one
of them.

**Who else hit this rule.** Ambient, historical, another actor's judgement. Three is a hint and
twenty is noise. Cap at three, exactly as written.

**How many times have you hit this rule, here, now.** Not ambient. This is the only mechanical form
of *Repeated failure is a signal, not a queue* (`cognitive/RULES.md`), and a cap of three mutes it
at the point it starts being true. On the task that prompted this amendment, 16 of the 32 refusals
were one rule — `Evidence 'X' does not support claim 'X'` — arriving in paired rounds: issue a
batch, fix the single pair the refusal named, re-issue, hit the next. The third occurrence was the
beginning of that pattern, not the end of it. Ledger-wide, 176 rows sit past a third occurrence, so
this is not one task's accident.

So two counters, not one:

| counter | key | cap | what it prints |
|---|---|---|---|
| prior occurrences | rule, other tasks, other actors | 3 | how many hit it and how they dispositioned it |
| own repetition | rule + actor + task | none | `refusal N of this rule for you on this task` |

The second is nearly free: the journal being written is the journal being counted. It needs no
dispositions, no cross-task join and no corpus, so it can ship before the retrieval half.

## Sequence and where to install, 2026-09-20

Three changes, smallest first, each useful alone.

1. **The refusal names the state of the records it just rejected.** Not retrieval and not in this
   entry's original scope — introspection of what the command handler already loaded.

        Evidence 'EV-RR3-2' does not support claim 'CLM-RR3-2'.
          EV-RR3-2 supports: (none)
          CLM-RR3-2 is supported by: (none)

   Alone among the three it fires on a rule's **first** occurrence, which is where the 16-row
   cluster began. No journal read, no cap, no prior row required. Deserves its own backlog row if it
   is to be tracked separately.

2. **The own-repetition counter.** One journal read, uncapped, no dispositions.

3. **This entry as originally scoped** — cross-task prior occurrences with dispositions, capped at
   three.

Ordered this way because 1 and 2 together would have collapsed that 16-row cluster into one
informative refusal and one visible count, and neither waits on the corpus work 3 requires. If only
one ships, ship 1.

### Install once, at the end of 1

Installing replaces the tool every other live task is using, so the install point is a decision, not
a step. One install, after 1 is verified. Build and test 2 and 3; do not install them until the task
archives.

**Why 1 is safe to install under live work.** It changes the text of a `GovernanceException` and
nothing else — no rule, no legality, no exit status, no event, no state. A task on the old kernel and
a task on the new one accept and refuse exactly the same commands. The data it prints is already in
hand at the throw site: `ClaimRules.cs:48-63` holds `evidence.Supports`, `evidence.Refutes` and
`state.Evidence`, so the reverse lookup is a loop over a dictionary the rule already has. No file is
read, so a task whose journal is absent, stale or truncated is unaffected.

**Why the benefit is highest there.** Increments 2 and 3 are themselves delivered through governed
runs, which will resolve claims and transition stages and therefore hit these rules. Installing after
1 makes the task the first user of its own first increment, and the refusals it produces are the
measurement of whether the change works.

**Why not after 2.** It reads `refusals.jsonl` from inside a command path. That file's own header
(`RefusalJournal.cs`) states that nothing in the kernel may refuse, gate or score on it and that
replay never reads it — a read that only prints is permitted, but it is the first such read, and
`2026-09-17_1440`'s C18 lost a repair round to misreading exactly this contract. Verify it before it
is under every other live task.

**Why not after 3.** It opens journals belonging to tasks the command is not operating on, outside
the mutation lock. A new access pattern does not go under live work unverified.

## The constraint this entry must not break

`RefusalJournal.cs` states its own contract in its header: nothing in the kernel may refuse, gate or
score on `refusals.jsonl`, replay never reads it, and a task whose journal is deleted materialises
exactly the state it did before. Changes 2 and 3 read that file from inside a command path, which is
the first time anything has. Permitted, because printing is not refusing — but the property that
makes it safe to write from a failure path is the same property that makes it unavailable as input
to any decision. A counter that is printed is fine. A counter that changes what the kernel does is
the defect C18 already cost this repository a round to learn.

## Acceptance criteria

- A refused command prints prior occurrences of the same rule with their dispositions, capped at
  three, and prints nothing when the rule has no prior occurrence outside this task.
- A refused command prints the actor's own count for that rule in that task, uncapped, from the
  second occurrence onward. The two counters are separate lines and the own count is never capped.
- The refusal message and exit status are byte-identical to today's.
- A refusal with no prior occurrence prints nothing extra.
- Change 1 reads no file and prints on a rule's first occurrence, so it holds when the journal is
  absent entirely.
- Reading the journal cannot fail the command: a corrupt or absent journal degrades to today's
  behaviour, and — per `2026-09-09_1010`'s `RC1` — a partly unreadable journal says so rather than
  reporting a short count as complete.
- No embedding model, no external service, no new dependency.
