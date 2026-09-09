work cannot leave the directory the ledger sits in — not even into a worktree of the same repository

a work item's scope is stored as an absolute path. `ResolveExistingScope` calls `Path.GetFullPath`
against whatever directory the operator was standing in when `work add` ran, and `provider launch`
then requires the working directory to sit inside that resolved path (`CliApplication.cs:1342`).

the kernel has no concept of a repository root, so it cannot tell that
`/…/AILedger/src/AILedger.Core` and `/…/AILedger-memory/src/AILedger.Core` are the same governed
area in two checkouts of one repository. they are two unrelated strings.

**this was first read as a cross-repository limit and it is not.** `AILedger-memory` is a git
worktree — its `.git` is a file pointing at `AILedger/.git/worktrees/AILedger-memory`, on branch
`codex/standalone-semantic-memory`. so is `AILedger-provider-preflight`. the kernel refuses the
exact isolation pattern the operator adopted to run agents in parallel on one repository, and it
refuses it at every launch rather than when the scope is declared.

this is the only thing in the ledger with a 33% loss rate.

## the measurement

`2026-09-08_1909-standalone-memory-index` built its subsystem in the `AILedger-memory` worktree,
governed by the ledger in the primary checkout. every scope refusal in the entire ledger — thirteen
of them — is in that one task, and they are all the same refusal:

    20:15  Provider directory '.../AILedger/.ailedger/tasks' is outside work item 'W0' scope.
    21:02  Provider directory '.../AILedger-memory'            is outside work item 'W7' scope.
    21:55  Provider directory '.../AILedger-memory'            is outside work item 'W1' scope.
    21:55  Provider directory '.../AILedger-memory'            is outside work item 'W3' scope.
    21:55  Provider directory '.../AILedger-memory'            is outside work item 'W2' scope.
    22:50  Provider directory '.../AILedger/.ailedger/tasks'   is outside work item 'W2' scope.
    22:50  Provider directory '.../AILedger/.ailedger/tasks'   is outside work item 'W8' scope.
    22:50  Provider directory '.../AILedger/.ailedger/tasks'   is outside work item 'W3' scope.
    23:39  Provider directory '.../AILedger/.ailedger/tasks'   is outside work item 'W8' scope.
    00:23  Provider directory '.../AILedger/.ailedger/tasks'   is outside work item 'W9' scope.
    04:23  Provider directory '.../AILedger-memory'            is outside work item 'W9' scope.
    05:01  Provider directory '.../AILedger-memory/src/AILedger.Memory' is outside work item 'W3' scope.
    07:45  Provider directory '.../AILedger-memory'            is outside work item 'W4' scope.

eleven and a half hours, eight different work items, one message.

what it cost that task, against every other task in the ledger:

| task | agent runs | completed | failed | cancelled | loss |
|---|---|---|---|---|---|
| standalone-memory-index | 120 | 80 | 14 | 25 | **33%** |
| ledger-learning | 32 | 26 | 4 | 1 | 19% |
| run-cost | 24 | 22 | 0 | 2 | 8% |
| decompose-command-handler | 23 | 22 | 1 | 0 | 4% |
| stage-arms | 22 | 22 | 0 | 0 | 0% |
| ledger-artifacts | 8 | 8 | 0 | 0 | 0% |
| **all agent runs** | **289** | **224** | **30** | **33** | **22%** |

that one task is 42% of every run ever made and **63% of every run ever lost** — 40 of 63. it also
holds thirteen of the nineteen runs that died inside twenty seconds. take it out and the rest of the
system runs at about 9%.

operator-held runs with provider `none` are excluded throughout; ten of them read as `cancelled` and
are not agents that died. see item 24.

## the two halves of the problem

**the scope check itself is correct.** `ScopeOccupancyRules.IsSameOrInside` compares path text with
a separator guard, exactly as its comment says it should, and `PathsOverlap` is symmetric. nothing
here is a bug. the check answers the question it was asked. the question is too small.

**the second half is that the ledger is already a special case.** `ResolveProviderGrants` grants
the ledger root separately from the work item's scope, with a comment saying why: without it "an
agent could only record truth when the ledger happened to sit inside its own scope — which two
concurrent agents on disjoint scopes can never both satisfy". a second repository is the same shape
of problem one step out. yet five of the thirteen refusals above are the operator trying to grant
`.ailedger/tasks` by hand, which suggests the separate grant did not cover what was needed either.

## what it should do

the smallest correct fix is the worktree case, because it is not a new capability — it is the same
repository, and `git worktree list` names every valid root in one call. resolve a scope relative to
the checkout the launch is running in, or record the checkout root on the work item and compare the
path below it. occupancy is unaffected either way: it stays text comparison, and two agents in two
worktrees of the same area still overlap and still should.

the larger question — may a task hold scope in a genuinely different repository — is separate and
this entry does not settle it. the end goal names cross-repo work, so it has to be answered, but it
should not be answered by accident while fixing the worktree case.

what must not survive any answer is the current behaviour: the scope is accepted at `work add` and
refused at every `provider launch` that follows it, forever, with no way to see it coming. the task
that hit this also spent a work item — W7, abandoned, then W7B — on making the kernel's own version
tests worktree-aware. the tax showed up twice inside one task and was paid locally both times.

## why this one is first

`ailedger-2-end-goal` says the tool exists for large cross-repo tasks. the only task that worked
outside the primary checkout produced two thirds of all the run losses in the ledger, and it never
left the repository. everything else in this backlog improves a system that works; this is a
capability that does not exist, and the worktree half of it is a small fix.
