# Priorities

Status as of 2026-09-09. `done` means the governed task reached stage `archive`.

| # | backlog entry | task id | status | notes |
|---|---------------|---------|--------|-------|
| 1 | record-that-context-was-built | `2026-09-07_2136-run-manifest-hash` | **done** | archived with one waiver on Learn: W1 was completed before any reviewer run, and RC4/RC5 prove that closes every route to a governed review. 11 runs, 12 claims, 4 lessons. |
| 2 | did-the-lesson-matter | `2026-09-07_2136-lesson-citations` | **done** | archived with no waiver. Reviewer ran before `work complete`. 7 runs, 10 claims all validated, 4 lessons. Its code reviewer also found and fixed three defects in item 3's code. |
| 3 | make-status-say-what-is-owed | `2026-09-07_2136-status-owed` | **done** | archived with no waiver. 5 runs, 10 claims all validated, 4 lessons. The verifier found VC1 (one citation cleared the whole lesson debt) and the reviewer found KC4 (cited and recalled counts drawn from different populations) and KC5 (no test that the projection agrees with the gate). The waiver count is withdrawn from scope by accepted decision LD1: state carries no durable waiver record. |
| 4 | kernel-version-stamp | `2026-09-07_2136-kernel-version-stamp` | **done** | archived with no waiver. The version half already shipped in `install.sh`; this added `ailedger version` and the staleness warning. 5 runs, 4 lessons. Eight defects found by the working, verifier and reviewer runs — including the feature being entirely non-functional on a clean tree, and four separate tests that passed while proving nothing. Scoped to `src/AILedger.Cli` and `tests` only, ahead of self-scoring, because scope occupancy is per-task (LC2). |
| 5 | record-the-refusals | `2026-09-08_1048-refusal-journal` | **done** | archived with no waiver. 16 runs, 2 work items, 42 claims, 62 evidence, 8 decisions, 11 alternatives, 2 challenges, 10 lessons. Both write sites shipped: 141 command-time rules at the service, and the seven authority-and-scope refusals `ResolveProviderGrants` decides before `run.start`. Two repair cycles — a concurrent append that unit tests passed over, and a public method that verification passed over. Seven new backlog entries came out of it, items 21 to 24 plus three earlier. |
| 6 | see-inside-a-run | `2026-09-08_1428-run-cost` | **done** | **blocks 9.** `AgentRunResult` carries the provider's own event stream — turns, tool calls, tokens — and the launcher wrote it to stdout and dropped it. 187 runs, 0 with cost recorded, which is the measurement that proves the governance-cost dimension is dark. W1 shipped the event fields and the reader; W2's worker run landed the launcher half and 16 tests and is with the verifier. Two follow-ons and two live defects came out of it: 30 and 31 below, plus claude's `total_cost_usd` and `subagent_stats` on the same event. |
| 7 | measure-before-scoring | not opened | open | **blocks 9.** the deterministic half of the retrospective: counts, durations and the causal chains the log can already join. no model, no scores. also the diagnostic — 198 of 205 resolved claims validated, 11 refuting evidence records in 558, 2 causal-chain events in the whole corpus. reads 5 and 6. **the diagnostic has now been run by hand over three archived tasks** and the entry carries the result: 4 of 10 dimensions answerable, cost null on all 44 runs, the coordinator writing 78% of events with no run to hold them, dimension 9 scoring every task alike, and the proposed validation set only partly scoreable because `stage-arms` predates both the refusal journal and `manifestHash`. calibration has to run forward, not backward. |
| 8 | route-the-workflow-lesson | not opened | open | **blocks 9's output half.** a WorkflowLesson has no kind field and no recall route; recall is repo-tag filtered with 10 slots, so one either never matches or displaces the domain lessons that describe the code. independent of 5, 6 and 7. |
| 9 | score-the-governance | `2026-09-07_2136-workflow-retrospective` | open | the self-scoring capability. `self-scoring.md` is the rubric and the authority on what the dimensions mean; `score-the-governance.md` is the shape and the order. blocked on 5 to 8: its two headline dimensions — governance effectiveness and governance cost — are the two the ledger currently cannot measure. and blocked on 21 for the same reason 8 is empty: the coordinator's own cost is unrecordable while a coordinator holds no closeable run. |
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
| 21 | the-coordinators-run-cannot-close | not opened | open | filing a PromptContract or OrchestrationPlan needs an active producer run, and an operator-held run can never be closed `completed` because it has no provider session. found at Design on item 5, which had to close its run `cancelled` after the run filed two artifacts successfully. **also blocks 9's cost dimension**, measured: the coordinating session wrote 240 of 309 events on item 5 and 81 of 104 on item 25, and holds no run — so it has no provider, no model, no duration and no token cost. every cost number the retrospective can compute describes the agents that were dispatched and none of the one dispatching them. |
| 22 | the-reviewers-approval-goes-stale | not opened | open | the completion gate asks `HasVerifierRunAfterLatestWork`; the Learn arm asks only whether *some* reviewer run completed. found on item 5: R9 reviewed W1, R10 repaired what R9 asked for, and `work complete` was accepted with an approval that describes different code. |
| 23 | attention-items-are-task-wide-but-work-is-not | not opened | open | `ValidateVerifierOutput` reads attention ids from the one current plan and demands every verifier dispose all of them. item 5 had six for W1 and three for W2, so its second verifier either writes six `not-applicable` rows or the plan stops describing the task. |
| 24 | cancelled-means-four-different-things | not opened | open | item 5 holds five `Cancelled` runs: four operator filing runs that succeeded, and one nine-minute verification the host killed for memory. `self-scoring` asks for failed and retried runs as a cost signal and would read five where the true number is one. |
| 25 | a-productive-task-starves-its-successor | not opened | open | **blocks 8, and degrades every task now.** recall orders by recency and takes ten. item 5 minted ten lessons and consumed item 6's entire budget: 38 lessons matched its tags, all ten slots went to lessons twenty minutes old, and the four closest matches in the store — including *do not put a field on run.started that the launcher learns later* — were crowded out and had to be copied in by hand. |
| 26 | the-reviewers-isolation-is-artifact-deep-only | not opened | open | **blocks 9's capability-utilization dimension.** `ReviewerExclusions` withholds five artifact kinds but the manifest still hands a reviewer the task goal, every claim, decision, alternative and their evidence — and the skill forbids the user's intent by name. Worse, a coordinator writing verifier findings into a constraint routes them past the filter: item 6's reviewer refused to review for exactly that reason and was right to. **The verifier needs the same missing input:** `baseRef` is a skill contract — `prompt-contract-designer` captures `git rev-parse HEAD` and the verifier reads it to scope its diff — and the kernel has no such field, so two verifier runs in a row degraded into the skill's documented fallback for a reason the fallback was not designed for. One nullable trailing field, captured at `work add`. |
| 27 | owed-does-not-say-what-blocks | not opened | open | `TaskDebt` reports open claims, items awaiting verification and lesson debt, and nothing about escalations — while an open escalation on an item refuses `work complete` outright. Item 6's `owed` read all zeros for four hours with two escalations open; the refusal was the first thing that surfaced them, and both questions had already been answered by other routes without acknowledgement. |
| 28 | the-launch-does-not-ask-if-the-host-can-hold-it | not opened | open | `provider launch` checks authority, scope, role and staffing, never whether the machine can hold the process it is about to start. two runs today ended `Cancelled` because the host killed the provider, each leaving a stranded active run that blocked the next run on its item and now reads as a governance event. the check belongs beside the seven grant refusals, before `run.started` exists. |
| 29 | the-arms-only-fire-if-you-walk-through-them | not opened | open | the eleven stage arms fire on a transition, and two of the three largest tasks in this ledger have never transitioned: `decompose-command-handler` (333 events, 23 runs) and `run-cost` (302 events, 19 runs), 42 of 187 runs between them. not a waiver — nothing was waived because nothing was asked, so there is no event and `status` reads as compliant. blocks anything that keys behaviour on stage, which is 10, 11 and the stage-engagement design itself. |
| 30 | see-inside-a-run — *a valid-JSON non-object line kills a run* | not opened | open | **live defect, C27.** one stdout line that is valid JSON but not an object throws `InvalidOperationException` out of `ProviderProtocol`; the line callback catches only `JsonException` and nothing between there and the launcher's catch-all holds it, so every event already collected is discarded and the child is killed. a line that is *not* JSON is tolerated and reported as "Malformed provider JSONL" — the designed tolerant path covers half the ways a line can be bad. one line at `AgentAdapterBase.cs:125`; ALT8 records the alternative that lost. first thing in W3. |
| 31 | record-the-refusals — *four roles cannot record a discarded approach* | not opened | open | **live defect, C28.** `RoleDefaults` grants `RecordAlternative` to the two lead roles only, so Worker, Researcher, Verifier and CodeReviewer are all refused — and `CLAUDE.md` documents the command with a worker actor in its own example. four journalled refusals across two tasks on two days. what is lost is the most evidence-bearing discarded approach there is: one an implementer actually tried. `EnsureSafe` names the four capabilities deliberately withheld and this is not one of them. |
| 32 | a-correction-is-one-command-away-and-cannot-be-undone | not opened | open | **cost measured tonight, C30.** `claim resolve --status superseded` derives refinement-versus-correction from the replacement's status at that instant. superseding by a claim still `open` lands a correction, which invalidates dependent decisions, blocks dependent work items, and cannot be undone — and a blocked item can be neither unblocked nor completed, only replaced. it cost item 6's W2 a re-verification cycle after three completed runs. one predicate and one `--accept-correction` flag; the refusal must not become a rule that forces every supersession to look like a refinement. |
| 33 | the-suite-is-not-comparable-across-runs | not opened | open | **C31.** `ACleanTreeAtADifferentCommitWarns` and `ADirtyTreeAtADifferentCommitStillWarns` are the only two tests that create a real git commit, so they fail in a run whose sandbox refuses `git commit` and pass in one that allows it. the same tree reported 526/2, 526/2 and then 534/0 across three runs. three agents recorded the count correctly and none asked what the two were, so every "same failures in both states" argument in this task rests on a coincidence of permissions. prove the fire path against the already-pure `Warning`; keep one git-backed test that names the environment in its failure. |

| 34 | scope-cannot-follow-a-worktree | not opened | open | **the largest single loss in the ledger.** scope is stored as an absolute path resolved at `work add` against wherever the operator stood, so the kernel cannot tell that two checkouts of one repository are the same governed area. `AILedger-memory` and `AILedger-provider-preflight` are git worktrees, not separate repositories — the kernel refuses the very isolation pattern used to run agents in parallel. all thirteen scope refusals in the ledger are in `standalone-memory-index`, over eleven hours; that task is 42% of all runs ever made and **63% of all runs ever lost**, 120 runs at a 33% loss rate against 0-19% everywhere else. the worktree half is a small fix; whether scope may leave the repository at all is a separate question the end goal still forces. |
| 35 | the-timeout-is-set-at-the-median-run | not opened | open | 28 of 43 cancelled runs ended within three seconds of a configured wall — 300, 600, 900, 1200, 1800 — against a median completed run of 586s and a p90 of 1150s. cancellations are not failures, they are where the walls were placed. raising the default to 1800s is one number in a launch command and it is the cheapest win available. compounds with 36: a run killed at the wall loses its whole record, not its last minutes. |
| 36 | the-ledger-is-written-at-the-end-or-not-at-all | not opened | open | median first ledger write lands at **72% of run duration** across 249 runs (p25 36%, p75 83%); R26 wrote all ten of its records in 78 seconds after fifteen minutes of work. **50 runs recorded nothing at all** — 19 of 30 failed, 16 of 43 cancelled. `CLAUDE.md` asks for the claim before the work; measured, it arrives after. `millisecondsToFirstLedgerWrite` cannot support the follow-up because its null means four different things. |
| 37 | what-a-run-actually-costs | measurement | **read for 7 and 9** | not work to do. the first five runs with cost fields, read: the **fixed brief is ~56%** of a run's cost, the agent's own output ~24%, everything it read with tools 22% — of which whole-file reads are 70% on a coding run and raw `events.jsonl` digging is 96% on an analysis run. the manifest measured 36,000 tokens, 92% artifacts, re-read every turn. summing the buckets overstates 7.2-8.7×, confirming C7. also carries the settled decision that the embedding index updates at run close, not per edit, and why. |
| 38 | *no entry file* | `2026-09-09_0858-provider-launch-preflight` | open | provider launch authentication and sandbox preflight — refuse before the run exists when auth or sandbox access is unavailable. third of the three preflight concerns, alongside 28 (host capacity) and 34 (scope reachability); they are the same gate and should probably be one. |

## Ordering notes

Items 1 to 4 keep their numbers because the notes above and `measure-before-scoring.md` refer to
them by number. Everything else is renumbered.

Items 5 to 9 are the self-scoring cluster. 5 and 6 are independent of each other and both feed 7.
8 is independent of all three and gates only 9's output half. None of the four needs the scoring
agent to exist, and all four are useful without it.

Items 19 and 20 were found while working items 1 and 2 and sat unranked in their own section until
now. Placing them at the end preserves every existing relative order rather than asserting a new
one; both are small and both are live pain, so moving them up is a reasonable call to make.

Items 7, 8 and 19 to 27 have no governed task yet.

Item 5 is done. Its own numbers are the first measured baseline for what a governed pass costs:
1h51m of agent execution across 16 runs, 3h20m wall clock, for a 78-line writer plus two
catch blocks. Five of those runs were `Cancelled` and four of the five had succeeded — see item 24. Only an operator may open one.

Items 34 to 37 came out of reading item 6's own output — the first five runs with cost fields —
together with every refusal and every non-completed run in the ledger. They are the first findings
in this backlog derived from measurement rather than from working a task. 37 is not work: it is the
baseline 7 and 9 were waiting for, and it holds the numbers so the next reading has something to
compare against. 34 is ranked above everything else here on the strength of one figure — one task
holds 63% of all run losses — and it was first written as a cross-repository limit before the
worktree was confirmed; the entry carries the correction.

Item 38 has a governed task and no entry file. Together with 28 and 34 it is the third of three
preflight concerns — auth and sandbox, host capacity, scope reachability — which are one gate asked
three times.

`self-scoring.md` has no row of its own. It is the rubric item 9 is built to satisfy, not a
separate piece of work, and it stays the authority on what the dimensions mean.

## Item 6, as it closed

`2026-09-08_1428-run-cost` is archived. 28 runs, three work items: W1 and W3 completed, W2 abandoned
and superseded by a narrower split. It shipped six nullable trailing fields on `run.completed`,
`Model`, `RunCostReader` with the per-provider input mapping, the launcher half that populates them,
and the sidecar at `.ailedger/tasks/<id>/runs/<run-id>.json` holding the whole `AgentRunResult`.

Five runs carry cost fields. What they say is item 37, and it is the first real answer to 9's cost
dimension. `RunCostReader`'s mapping was re-verified against both providers' actual terminal events
while writing that entry and is correct.

Still open from it: nothing renders the six fields — `MarkdownTaskProjectionWriter` in
`src/AILedger.Storage` — and claude's `total_cost_usd` and `subagent_stats` sit unread on the same
terminal event. Both are carried in item 37.

### What W1 cost, the sharpest datapoint here

    18 runs   4 repair cycles   4 verifier passes   4 reviews, two of which refused to review
    6 runs recorded Cancelled — 4 operator filing runs that succeeded, 2 host memory kills
    4 plan supersessions, each costing one of those filing runs
    2 escalations open for four hours because `owed` does not report them (item 27)

Four findings came from **review after verification had passed**. One class-level claim (C15)
predicted a fourth instance of a defect, and it appeared inside the repair for the third (C16). Two
of my four decisions were superseded — one because I decided without researching, one because I had
committed the exact defect I was guarding against. I would have shipped this after the first
verifier pass.

## Kernel state

`2.0.61 from 6f0af45`. `ailedger version` reports the stamp and warns when the ledger home has moved
past it. Carries the manifest hash on `run.completed`, `--from-lesson` on claim/decision/alternative,
the `owed` block on `status`, and the run cost fields and sidecar from item 6.

## Estimates

The cost lines in the backlog entries describe the diff, not the task. Item 1's diff was two nullable
fields; the governed pass took 7h32m wall clock, of which 53 minutes was agent execution.
