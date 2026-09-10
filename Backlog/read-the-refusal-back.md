# The refusal is the best query in the system and nothing reads it

## The observation

When the kernel refuses a command it holds a perfectly formed query: the command type, the actor,
the rule that fired, and the exact message. It knows precisely what someone tried and precisely why
it said no.

It says no, writes a row, and forgets.

    144 refusal rows across 35 tasks
    0 of them ever read back by anything

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
  refusal itself.

## What it must not become

- **A suggestion the kernel endorses.** It reports what others did, not what you should do. Two of
  the three prior actors waiving a rule is a fact, not a recommendation, and a refusal that reads
  like permission is worse than one that reads like a wall.
- **A reason to soften the refusal.** The message stays exactly as it is. This is added context, not
  a negotiation.
- **Cross-repository by default.** A rule fires the same way everywhere, but the dispositions are
  local judgement. Start with this ledger.

## Acceptance criteria

- A refused command prints prior occurrences of the same rule with their dispositions, capped.
- The refusal message and exit status are byte-identical to today's.
- A refusal with no prior occurrence prints nothing extra.
- Reading the journal cannot fail the command: a corrupt or absent journal degrades to today's
  behaviour, and — per `2026-09-09_1010`'s `RC1` — a partly unreadable journal says so rather than
  reporting a short count as complete.
- No embedding model, no external service, no new dependency.
