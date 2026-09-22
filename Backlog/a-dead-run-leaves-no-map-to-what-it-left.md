a dead run leaves no map to what it left

A run that ends `Cancelled` or `Failed` does not roll anything back. It leaves two kinds of leftover:

- **files in the working tree** — source, tests, scratch, sometimes build output;
- **partial ledger writes** — claims and evidence it filed before it died.

Neither is announced anywhere. `AgentRunStatus` says how it ended. `endedAt` says when. Nothing says
**what it had already accomplished**, so the next coordinator either re-reads the whole tree by hand
or guesses — and a coordinator under time pressure guesses that a dead run produced nothing.

## measured on 2026-09-20_0817-read-the-refusal-back

Three runs died on one work item, and all three left something different:

| run | ended | min | files left | ledger left |
|---|---|---:|---|---|
| `RW4` | cancelled at its timeout | 60 | production code + 14 tests | **nothing** |
| `RW6` | failed on a provider spend limit | 10 | all three repairs `D13` asked for | **nothing** |
| `RW7` | cancelled by the operator | 63 | the same, plus a comment fix | **5 claims, 8 evidence records** |

`RW7` was about 95% finished. Its record carried the candidate identity, the suite at 1310/1310, the
full mutation matrix with each mutant mapped to the tests it kills, the no-install check, and a
finding that `cognitive/RULES.md`'s in-process xunit host has two defects beyond the four it
documents. Only `execution_notes.md` was missing.

The coordinator relaunched anyway, briefed the new run to redo the mutation work, and let it run 25
minutes before noticing `W7C1`–`W7C5` already existed. It then wrote a cost analysis asserting that
133 minutes had died "leaving no record", which was wrong about the largest of the three.

## the shape — either half fixes it

**Breadcrumbs.** When a run ends other than `Completed`, record on the run what it left: the event
ids it wrote, and the paths it changed. The launcher already holds both — it knows the run id, so the
ledger writes are a filter over the log, and a `git status` delta against the work item's `BaseRef` is
one command in the same working directory. A dead run then hands its successor a map instead of a
puzzle.

**Or the coordinator checks.** Cheaper and needs no kernel change: before relaunching on a work item
whose previous run did not complete, read what that run filed. `status` already shows it. The kernel
could go further and refuse a relaunch on such an item until the coordinator has built context since
the death — the same shape as the existing brief gate, which refuses `work add` and `provider launch`
from an actor whose brief is stale.

The second is a discipline the first makes unnecessary. Do the first.

## why this is not rows 36 or 24

Row 36, *the ledger is written at the end or not at all*, measures the same wound from the other
side: 50 runs recorded nothing, median first write at 72% of run duration. It argues for writing
earlier. This row is about the runs that **did** write and were treated as though they had not.

Row 24, *`Cancelled` is four endings wearing one status*, is about the status being ambiguous. Even
with four precise statuses, none of them would say what the run left behind.
