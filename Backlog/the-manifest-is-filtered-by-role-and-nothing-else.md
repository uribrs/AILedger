the manifest is filtered by role and nothing else, and every turn pays for it

`context build` serves an actor the rules artifact, the skills for its role, the task goal, every
active constraint, every claim, every piece of evidence, every decision, every rejected alternative,
the artifact bodies and the stop conditions. The only filter is the subject's **role** — a reviewer
loses six artifact kinds, and that is the whole of it.

There is no relevance filter, no size bound, and no ordering that puts the work item's own dependency
closure first.

## what it costs, measured on 2026-09-20_0817-read-the-refusal-back

Every turn of a governed run re-reads the entire context. Cached, so it is cheap in money and not in
wall-clock.

| run | turns | cache-read per turn | output per turn | minutes |
|---|---:|---:|---:|---:|
| `RW1` | 108 | 104,239 | 470 | 28 |
| `RW2` | 96 | 106,320 | 572 | 31 |
| `RW3` | 112 | 107,680 | 516 | 21 |
| `RR3` | 92 | 101,232 | 622 | 19 |
| **`RW5`** | **32** | **267,012** | **838** | **39** |

Roughly **100k tokens in and 500 out, a hundred times a run, at 9–19 seconds a turn.** That is
~20 minutes of latency per run before any work is done, and this task spent 391 agent-minutes across
21 runs.

By the end of the task the manifest carried **130 claims, 145 evidence records, 15 decisions, 28
constraints and 12 artifacts**. A worker dispatched to change twelve lines in `RefusalJournal.cs`
received all of them — including the twenty claims about increment 1's render, the six
governance-adherence claims the coordinator was measuring itself with, and every superseded decision.

## `RW5` is the counter-example worth reading

32 turns at 267k read and 838 out. Fewer, larger turns: it batched its work instead of round-tripping
one file at a time, and it delivered more than `RW3` did in 112 turns. The run that looks anomalous on
seconds-per-turn is the efficient one.

So the lever is **turn count**, and turn count is driven by how much an agent has to page through
before it can act.

## the shape

Not less governance — the same gates, a smaller floor:

- order the manifest by the work item's dependency closure, so what the brief actually names comes
  first and the rest is tail;
- drop superseded and invalidated records by default, the way a reader would;
- bound the size, and when the bound bites, say so in the manifest rather than truncating silently —
  an agent that does not know its brief was cut is worse off than one that knows;
- keep the role filter exactly as it is.

## what this is not

It is not an argument against the manifest. The briefing is why a fresh codex process can verify work
it has never seen, and why a reviewer arrives knowing what was already decided and deferred — which
is what produced this task's four defect finds, each one caught by the pass after the one that missed
it. The cost is real and so is the thing it buys; this row is about paying less for the same thing.

## measurement note

Two earlier explanations for the same latency were wrong and are recorded here so nobody re-derives
them: it is not `dotnet build` (compilation is a handful of turns), and it is not the forced
full-rebuild-per-mutant policy (`RW5` did eleven mutants inside 32 turns). The `turns`,
`tokensInCacheRead` and `outputTokens` fields were on every run record the whole time.
