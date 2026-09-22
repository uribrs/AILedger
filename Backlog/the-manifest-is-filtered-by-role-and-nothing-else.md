the manifest is filtered by role and nothing else, and every turn pays for it

## investigation and implementation, 2026-09-22

The original diagnosis below overstated the missing filter. Before this change, a `--work` brief
already selected that item's claims, decisions, evidence and applicable artifacts. The additional
`IsRelevant` check was redundant: its set contained every projected artifact's own id.

Scoped briefs now place selected work and its dependency records first. Decisions selected for that
work bring their other prerequisite claims, and claims bring evidence referenced directly as well as
directionally. Superseded claims and superseded/invalidated decisions are omitted by default. A stale
claim explicitly referenced by selected work or a current decision remains visible with its replacement;
rejected claims remain because they record an approach that failed. Role and bound-review isolation
are unchanged.

`context build`, `provider launch` and `provider resume` enforce a default **262,144-byte** serialized
UTF-8 JSON limit, configurable with `--max-context-bytes`. The CLI includes its newline in the bound.
Background lessons and lesson marks may be omitted as whole records, with a bounded count and retrieval
notice in the manifest. Rules, skills, task records and lessons referenced by claims/decisions are never
truncated. If those required inputs do not fit, delivery fails with a measured size and instructions
to narrow the work brief or explicitly raise the limit. Provider execution does not start with that
oversized brief. The pure core assembler remains a projection; the delivery boundary enforces the limit.

### measured comparison, not a turn-count claim

A read-only comparison used the pre-change binaries from commit `54330e4` and the new binaries against
the same saved task state and cognitive files. The saved task had grown to 159 claims, 185 evidence
records and 15 decisions, so these are not reconstructed launch-time manifests. Assembly time was fixed
to the Unix epoch in both measurements; sizes include one newline.

| brief | before, bytes | after, bytes |
|---|---:|---:|
| worker, whole task | 261,384 | 257,947 |
| worker, W1 | 71,625 | 71,698 |
| worker, W2 | 74,023 | 74,096 |
| verifier, W1 | 147,783 | 147,856 |
| verifier, whole task | 337,542 | refused: required inputs alone exceed 262,144 |

The scoped briefs grow by 73 bytes of explicit budget metadata. This **does not demonstrate a
reduction in turns or provider spend** on that task. It demonstrates that its work scoping already
worked, that obsolete whole-task records can be removed, and that oversized initial manifests now
have a delivery guard. This limit does not cover provider conversation history, resumed sessions,
tool outputs, or total billed tokens. The separately implemented governed-test runner addresses the
permission/retry loops seen in the expensive runs.

Regression coverage is in `ContextDependencyTests`, `ContextManifestBudgetTests` and
`ContextBudgetDeliveryTests`: dependency selection, stale-record handling, exact byte boundaries,
escaped Unicode, whole-record omissions, protected records, and context/provider delivery failures.
The provider tests use an in-process adapter; they incur no provider charges.
Validation: 54 context tests passed through `dotnet test`; the full `scripts/test-governed.sh` run
passed 1,328 main tests and 97 memory tests with zero failures, skips or runner errors.

## original backlog diagnosis

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
