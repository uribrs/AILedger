# Priorities

Status as of 2026-09-08. `done` means the governed task reached stage `archive`.

| # | backlog entry | task id | status | notes |
|---|---------------|---------|--------|-------|
| 1 | record-that-context-was-built | `2026-09-07_2136-run-manifest-hash` | **done** | archived with one waiver on Learn: W1 was completed before any reviewer run, and RC4/RC5 prove that closes every route to a governed review. 11 runs, 12 claims, 4 lessons. |
| 2 | did-the-lesson-matter | `2026-09-07_2136-lesson-citations` | **done** | archived with no waiver. Reviewer ran before `work complete`. 7 runs, 10 claims all validated, 4 lessons. Its code reviewer also found and fixed three defects in item 3's code. |
| 3 | make-status-say-what-is-owed | `2026-09-07_2136-status-owed` | **done** | archived with no waiver. 5 runs, 10 claims all validated, 4 lessons. The verifier found VC1 (one citation cleared the whole lesson debt) and the reviewer found KC4 (cited and recalled counts drawn from different populations) and KC5 (no test that the projection agrees with the gate). The waiver count is withdrawn from scope by accepted decision LD1: state carries no durable waiver record. |
| 4 | kernel-version-stamp | `2026-09-07_2136-kernel-version-stamp` | **done** | archived with no waiver. The version half already shipped in `install.sh`; this added `ailedger version` and the staleness warning. 5 runs, 4 lessons. Eight defects found by the working, verifier and reviewer runs — including the feature being entirely non-functional on a clean tree, and four separate tests that passed while proving nothing. Scoped to `src/AILedger.Cli` and `tests` only, ahead of self-scoring, because scope occupancy is per-task (LC2). |
| 5 | record-the-refusals | `2026-09-08_1048-refusal-journal` | **done** | archived with no waiver. 16 runs, 2 work items, 42 claims, 62 evidence, 8 decisions, 11 alternatives, 2 challenges, 10 lessons. Both write sites shipped: 141 command-time rules at the service, and the seven authority-and-scope refusals `ResolveProviderGrants` decides before `run.start`. Two repair cycles — a concurrent append that unit tests passed over, and a public method that verification passed over. Seven new backlog entries came out of it, items 21 to 24 plus three earlier. |
| 6 | see-inside-a-run | `2026-09-08_1428-run-cost` | W1 blocked on host memory | **blocks 9.** `AgentRunResult` carries the provider's own event stream — turns, tool calls, tokens — and `CliApplication.cs:939` writes it to stdout and drops it. 114 runs, 0 models recorded. the governance-cost dimension is unmeasurable without it. |
| 7 | measure-before-scoring | not opened | open | **blocks 9.** the deterministic half of the retrospective: counts, durations and the causal chains the log can already join. no model, no scores. also the diagnostic — 198 of 205 resolved claims validated, 11 refuting evidence records in 558, 2 causal-chain events in the whole corpus. reads 5 and 6. |
| 8 | route-the-workflow-lesson | not opened | open | **blocks 9's output half.** a WorkflowLesson has no kind field and no recall route; recall is repo-tag filtered with 10 slots, so one either never matches or displaces the domain lessons that describe the code. independent of 5, 6 and 7. |
| 9 | score-the-governance | `2026-09-07_2136-workflow-retrospective` | open | the self-scoring capability. `self-scoring.md` is the rubric and the authority on what the dimensions mean; `score-the-governance.md` is the shape and the order. blocked on 5 to 8: its two headline dimensions — governance effectiveness and governance cost — are the two the ledger currently cannot measure. |
| 10 | single-agent-relaxation | `2026-09-07_2136-single-provider-mode` | open | |
| 11 | attention-items-as-a-gate | `2026-09-07_2136-attention-item-gate` | open | |
| 12 | resolve-claims-where-the-evidence-lands | `2026-09-07_2136-claim-resolution-ergonomics` | open | `status` now reports 76 of 80 open claims on `ledger-learning` are clearable in one pass, which is higher than the entry estimated. |
| 13 | the-append-is-quadratic | `2026-09-07_2136-append-in-place` | open | |
| 14 | mirror-the-replay-validator | `2026-09-07_2136-mirror-replay-validator` | open | |
| 15 | waivers-need-a-floor | `2026-09-07_2136-waiver-floor` | open | |
| 16 | open-claims-block-archive | `2026-09-07_2136-archive-open-claims` | open | see 20, `research-needs-an-open-claim` — closing every claim currently locks a task out of Archive, which this entry would make worse. |
| 17 | decompose-the-cli | `2026-09-07_2136-cli-file-scope` | open | |
| 18 | archived-memory-conservation | `2026-09-07_2136-archive-compaction` | open | |
| 19 | review-before-complete | not opened | open | `work complete` requires a verifier but not a reviewer, and completing the item makes the review impossible. cost item 1 its only waiver. found while working items 1 and 2; unranked until now. |
| 20 | research-needs-an-open-claim | not opened | open | Discovery's only exit is Research, and that arm refuses without an open claim. hit on both of items 1 and 2. see 16 — closing every claim currently locks a task out of Archive, which 16 would make worse. found while working items 1 and 2; unranked until now. |
| 21 | the-coordinators-run-cannot-close | not opened | open | filing a PromptContract or OrchestrationPlan needs an active producer run, and an operator-held run can never be closed `completed` because it has no provider session. found at Design on item 5, which had to close its run `cancelled` after the run filed two artifacts successfully. |
| 22 | the-reviewers-approval-goes-stale | not opened | open | the completion gate asks `HasVerifierRunAfterLatestWork`; the Learn arm asks only whether *some* reviewer run completed. found on item 5: R9 reviewed W1, R10 repaired what R9 asked for, and `work complete` was accepted with an approval that describes different code. |
| 23 | attention-items-are-task-wide-but-work-is-not | not opened | open | `ValidateVerifierOutput` reads attention ids from the one current plan and demands every verifier dispose all of them. item 5 had six for W1 and three for W2, so its second verifier either writes six `not-applicable` rows or the plan stops describing the task. |
| 24 | cancelled-means-four-different-things | not opened | open | item 5 holds five `Cancelled` runs: four operator filing runs that succeeded, and one nine-minute verification the host killed for memory. `self-scoring` asks for failed and retried runs as a cost signal and would read five where the true number is one. |
| 25 | a-productive-task-starves-its-successor | not opened | open | **blocks 8, and degrades every task now.** recall orders by recency and takes ten. item 5 minted ten lessons and consumed item 6's entire budget: 38 lessons matched its tags, all ten slots went to lessons twenty minutes old, and the four closest matches in the store — including *do not put a field on run.started that the launcher learns later* — were crowded out and had to be copied in by hand. |
| 26 | the-reviewers-isolation-is-artifact-deep-only | not opened | open | **blocks 9's capability-utilization dimension.** `ReviewerExclusions` withholds five artifact kinds but the manifest still hands a reviewer the task goal, every claim, decision, alternative and their evidence — and the skill forbids the user's intent by name. Worse, a coordinator writing verifier findings into a constraint routes them past the filter: item 6's reviewer refused to review for exactly that reason and was right to. |

## Ordering notes

Items 1 to 4 keep their numbers because the notes above and `measure-before-scoring.md` refer to
them by number. Everything else is renumbered.

Items 5 to 9 are the self-scoring cluster. 5 and 6 are independent of each other and both feed 7.
8 is independent of all three and gates only 9's output half. None of the four needs the scoring
agent to exist, and all four are useful without it.

Items 19 and 20 were found while working items 1 and 2 and sat unranked in their own section until
now. Placing them at the end preserves every existing relative order rather than asserting a new
one; both are small and both are live pain, so moving them up is a reasonable call to make.

Items 7, 8 and 19 to 26 have no governed task yet.

Item 5 is done. Its own numbers are the first measured baseline for what a governed pass costs:
1h51m of agent execution across 16 runs, 3h20m wall clock, for a 78-line writer plus two
catch blocks. Five of those runs were `Cancelled` and four of the five had succeeded — see item 24. Only an operator may open one.

`self-scoring.md` has no row of its own. It is the rubric item 9 is built to satisfy, not a
separate piece of work, and it stays the authority on what the dimensions mean.

## Item 6, paused mid-flight

`2026-09-08_1428-run-cost` holds W1's Core half: six nullable trailing fields on `run.completed`,
`RunCostReader`, and the per-provider mapping. 512 tests pass and every guard is proven load-bearing
by reversion. It is **not committed** and **not completed**, because `work complete` needs a verifier
run after the latest work and the last pass predates the last repair.

Blocked by host memory, not by the ledger — swap 26.4GB of 27.6GB, 106MB free, 19.4GB compressed.
Three verifier runs killed: R14 on the sibling task, R11 and R16 here. Resume by relaunching
`codex-verify` on W1 once there is memory.

What W1 cost, and it is the sharpest cost datapoint this repository has:

    16 runs   4 repair cycles   3 verifier passes   3 reviews, one of which refused to review
    6 runs recorded Cancelled — 4 operator filing runs that succeeded, 2 memory kills
    4 plan supersessions, each costing one of those filing runs

Four of the findings came from **review after verification had passed**, and one class-level claim
(C15) predicted a fourth instance of a defect that then appeared inside the repair for its third
(C16). I would have shipped this after the first verifier pass.

## Kernel state

`2.0.29-dirty from 7db26d5` as of 12:05 UTC, reinstalled by concurrent work outside this task — `ailedger version` reports it and warns when the
tree has moved past the installed build. Carries the manifest hash on `run.completed`,
`--from-lesson` on claim/decision/alternative, and the `owed` block on `status`.

## Estimates

The cost lines in the backlog entries describe the diff, not the task. Item 1's diff was two nullable
fields; the governed pass took 7h32m wall clock, of which 53 minutes was agent execution.
