# Priorities

## Counts

- **48 total**
- **40 open**
- **8 done**
- **4 delivered but not yet archived (12 made or archived)**
- **23 bugs, 18 features, 7 changes**

Status as of 2026-09-10. `done` means the governed task reached stage `archive`; delivered work awaiting verification or closeout remains open.
Every status was traceable to the supplied facts; no row was left unconfirmed.

## Open work, in priority order

| # | backlog entry | task id | status | kind | notes |
|---|---------------|---------|--------|------|-------|
| 26 | the-reviewers-isolation-is-artifact-deep-only | not opened | open | bug | **blocks 9's capability-utilization dimension.** `ReviewerExclusions` withholds six artifact kinds but the manifest still hands a reviewer the task goal, every claim, decision, alternative and their evidence — and the skill forbids the user's intent by name. Worse, a coordinator writing verifier findings into a constraint routes them past the filter: item 6's reviewer refused to review for exactly that reason and was right to. **The verifier needs the same missing input:** `baseRef` is a skill contract — `prompt-contract-designer` captures `git rev-parse HEAD` and the verifier reads it to scope its diff — and the kernel has no such field, so two verifier runs in a row degraded into the skill's documented fallback for a reason the fallback was not designed for. One nullable trailing field, captured at `work add`. On 2026-09-10, a code-reviewer run on `2026-09-10_0845-pipeline-mandatory` stopped rather than review because eight constraints carried verifier verdicts and one told it to read the verifier artifact by id. Validated claim C12 with evidence E15 establishes the mechanism in source: `ContextAssembler` applies its six-kind `ReviewerExclusions` only to a CodeReviewer, while Constraint is in `AlwaysIncludedKinds` and excluded from nothing. Superseding the eight briefs and filing one minimal bundle produced a manifest the reviewer accepted; it then found three defects that three verifier rounds missed. This is a role that can silently produce nothing, not a theoretical isolation concern. |
| 20 | research-needs-an-open-claim | not opened | open | bug | Discovery's only exit is Research, and that arm refuses without an open claim. hit on both of items 1 and 2. see 16 — closing every claim currently locks a task out of Archive, which 16 would make worse. found while working items 1 and 2; unranked until now. |
| 29 | the-arms-only-fire-if-you-walk-through-them | not opened | open | bug | the eleven stage arms fire on a transition, and two of the three largest tasks in this ledger have never transitioned: `decompose-command-handler` (333 events, 23 runs) and `run-cost` (302 events, 19 runs), 42 of 187 runs between them. not a waiver — nothing was waived because nothing was asked, so there is no event and `status` reads as compliant. blocks anything that keys behaviour on stage, which is 10, 11 and the stage-engagement design itself. |
| 47 | a-researcher-and-a-worker-cannot-prove-they-ran | `2026-09-10_1117-researcher-can-prove-it-ran` | **blocked** | feature | W1 is blocked. `GovernedArtifactKind` has five members and none is a research output or execution notes, while `RoleDefaults` grants `recordArtifact` to neither role. A run in either role can leave only claims and evidence, so a brief that asks it for a filed document is unsatisfiable. Minted as a lesson on 2026-09-10 from `2026-09-10_1007-rewrite-the-skills` claim XC4. |
| 48 | the-learn-arm-counts-a-reviewer-run-without-reading-what-it-produced | not opened | open | bug | On 2026-09-10, a code-reviewer run completed after filing a `CodeReviewOutput` whose entire content was a refusal to review. The Learn stage arm requires a completed CodeReviewer run for code-bearing work and would have accepted it. The arm cannot distinguish a review from a documented refusal, the one case where run completion means the opposite of what the arm is checking for. |
| 46 | scope-occupancy-does-not-cross-tasks-and-tests-is-one-area-everything-needs | `2026-09-10_0858-occupancy-crosses-tasks` | open | bug | `ScopeOccupancyRules` iterates one task's state, so two tasks can hold the same directory and the kernel refuses neither. Validated claim C5 with evidence E5 measures the second half: because scope is directory-level, `tests` is a single area every code-bearing work item needs, so all test-bearing work serialises whether or not it collides. Three launches were held by hand on 2026-09-10 for this reason alone; not one of the three pairs would have touched the same file. |
| 35 | the-timeout-is-set-at-the-median-run | not opened | open | change | 28 of 43 cancelled runs ended within three seconds of a configured wall — 300, 600, 900, 1200, 1800 — against a median completed run of 586s and a p90 of 1150s. cancellations are not failures, they are where the walls were placed. raising the default to 1800s is one number in a launch command and it is the cheapest win available. compounds with 36: a run killed at the wall loses its whole record, not its last minutes. |
| 31 | record-the-refusals — *four roles cannot record a discarded approach* | not opened | open | bug | **live defect, C28.** `RoleDefaults` grants `RecordAlternative` to the two lead roles only, so Worker, Researcher, Verifier and CodeReviewer are all refused — and `CLAUDE.md` documents the command with a worker actor in its own example. four journalled refusals across two tasks on two days. what is lost is the most evidence-bearing discarded approach there is: one an implementer actually tried. `EnsureSafe` names the four capabilities deliberately withheld and this is not one of them. |
| 30 | see-inside-a-run — *a valid-JSON non-object line kills a run* | not opened | open | bug | **live defect, C27.** one stdout line that is valid JSON but not an object throws `InvalidOperationException` out of `ProviderProtocol`; the line callback catches only `JsonException` and nothing between there and the launcher's catch-all holds it, so every event already collected is discarded and the child is killed. a line that is *not* JSON is tolerated and reported as "Malformed provider JSONL" — the designed tolerant path covers half the ways a line can be bad. one line at `AgentAdapterBase.cs:125`; ALT8 records the alternative that lost. first thing in W3. |
| 43 | read-the-refusal-back | not opened | open | feature | **the sharpest retrieval in the kernel and nothing uses it.** on refusal the kernel holds an exact key — command type, actor, rule, message — and 144 rows of prior occurrences whose following events record what each actor did next. so "who else hit this and what did they do" is a SQL join over existing rows: **no model, no embedding, no service, no evaluation to earn first**, unlike the semantic index. measured against 2026-09-09's four coordinator failures, two were catchable at a specific command and one at the refusal itself. carries the distinction the whole learning story rests on: retrieval handles what someone already paid for, cross-model verification handles what nobody has learned yet — and only the second is currently working. |
| 36 | the-ledger-is-written-at-the-end-or-not-at-all | not opened | open | bug | median first ledger write lands at **72% of run duration** across 249 runs (p25 36%, p75 83%); R26 wrote all ten of its records in 78 seconds after fifteen minutes of work. **50 runs recorded nothing at all** — 19 of 30 failed, 16 of 43 cancelled. `CLAUDE.md` asks for the claim before the work; measured, it arrives after. `millisecondsToFirstLedgerWrite` cannot support the follow-up because its null means four different things. |
| 34 | scope-cannot-follow-a-worktree | not opened | open | bug | **the largest single loss in the ledger.** scope is stored as an absolute path resolved at `work add` against wherever the operator stood, so the kernel cannot tell that two checkouts of one repository are the same governed area. `AILedger-memory` and `AILedger-provider-preflight` are git worktrees, not separate repositories — the kernel refuses the very isolation pattern used to run agents in parallel. all thirteen scope refusals in the ledger are in `standalone-memory-index`, over eleven hours; that task is 42% of all runs ever made and **63% of all runs ever lost**, 120 runs at a 33% loss rate against 0-19% everywhere else. the worktree half is a small fix; whether scope may leave the repository at all is a separate question the end goal still forces. |
| 44 | the-context-gate-and-its-two-doors | `2026-09-10_0845-pipeline-mandatory` | **delivered, in last repair round** | feature | No entry file; this came from the operator's instruction, not the backlog. No work item may be added and no provider launched until the acting actor holds a brief whose per-skill hashes match the cognitive root, with an operator door for no brief at all and an evidence-named door for a brief the change did not affect. Eleven runs: one worker, three verifier rounds, a reviewer that refused, a reviewer that found three defects, and repairs. |
| 13 | the-append-is-quadratic | `2026-09-10_0907-append-in-place` | **delivered, awaiting verifier then closeout** | bug | A mutation now appends one line instead of copying the whole log. The byte reduction is 1,072-fold, measured independently three times: 1,072.33, 1,072.31, and 1,072.31. Atomicity for multi-event commands was added after verification found the gap, and a torn command now replays as if it never happened. W1 was abandoned mid-way as a mis-scope; W2 carries the repair. |
| 8 | route-the-workflow-lesson | `2026-09-09_1606-workflow-lesson-routing` | **delivered, closeout owed** | feature | **blocks 9's output half.** a WorkflowLesson has no kind field and no recall route; recall reads tags now (item 25 fixed that today) but with 10 slots a workflow lesson either never matches or displaces the domain lessons that describe the code. four work items: kind plus audience on `Lesson` and `LessonMark`, a derived task shape, a separate recall budget keyed on shape, and — found by this task's own recall on its first minute — **a verify command that can fail.** `C1`: `RequireCheckableVerify` demands a runnable command and cannot demand a falsifiable one, so `stage-arms:C1` still passes its own grep while asserting the opposite of the code it cites. a stale lesson with a green check is worse than an empty slot. independent of 5, 6 and 7. Its capability is delivered: `LessonKind`, `Audience`, `--verify-expects`, and lesson recheck all shipped. On 2026-09-10, four lessons were minted live through that whole path with kind, audience, a runnable verify, and its expected direction—the first end-to-end proof it works. The task remains at Discovery with W2 paused and five open claims. |
| 12 | resolve-claims-where-the-evidence-lands | `2026-09-07_2136-claim-resolution-ergonomics` | open | feature | `status` now reports 76 of 80 open claims on `ledger-learning` are clearable in one pass, which is higher than the entry estimated. |
| 19 | review-before-complete | not opened | open | bug | `work complete` requires a verifier but not a reviewer, and completing the item makes the review impossible. cost item 1 its only waiver. found while working items 1 and 2; unranked until now. |
| 21 | the-coordinators-run-cannot-close | not opened | open | bug | filing a PromptContract or OrchestrationPlan needs an active producer run, and an operator-held run can never be closed `completed` because it has no provider session. found at Design on item 5, which had to close its run `cancelled` after the run filed two artifacts successfully. **also blocks 9's cost dimension**, measured: the coordinating session wrote 240 of 309 events on item 5 and 81 of 104 on item 25, and holds no run — so it has no provider, no model, no duration and no token cost. every cost number the retrospective can compute describes the agents that were dispatched and none of the one dispatching them. |
| 22 | the-reviewers-approval-goes-stale | not opened | open | bug | the completion gate asks `HasVerifierRunAfterLatestWork`; the Learn arm asks only whether *some* reviewer run completed. found on item 5: R9 reviewed W1, R10 repaired what R9 asked for, and `work complete` was accepted with an approval that describes different code. |
| 40 | staffing-is-a-name-not-a-run | `2026-09-09_1132-ready-arm-staffing` | open | bug | **C1, confirmed in source.** the Ready arm's three staffing refusals read `state.Roles` and ask only that a role has an assigned actor — no run, no engagement. on `2026-09-09_0812-cortex-asset-duplication` Ready passed with Worker, ImplementationLead and CodeReviewer staffed solely by `probe-*` actors created while discovering the role vocabulary, all `engaged=False`, none ever holding a run. `who` already computes engagement and means *a completed run* by it — Verifier read `engaged=False` there while `RV1` existed, because `RV1` failed — so the concept, the computation and the display are all present and the arm is the only place that does not ask. distinctness is safe by construction, not by check. same class as 22, and the mirror of 29: an arm passed by decoration and an arm never reached are one hole from two sides. **cheap half:** give `actor attach` a dry run or list the roles in `--help`; the junk actors existed only because the vocabulary could not be discovered otherwise, and both `planningLead` and `planning-lead` are accepted. |
| 27 | owed-does-not-say-what-blocks | not opened | open | bug | `TaskDebt` reports open claims, items awaiting verification and lesson debt, and nothing about escalations — while an open escalation on an item refuses `work complete` outright. Item 6's `owed` read all zeros for four hours with two escalations open; the refusal was the first thing that surfaced them, and both questions had already been answered by other routes without acknowledgement. |
| 41 | scope-cannot-be-widened-after-review | `2026-09-09_1010-retrospective-projection` | open | bug | **C10, measured on item 7's own closeout.** a work item's areas are fixed at `work add` and `ResolveProviderGrants` refuses any granted directory outside them, so the item that hosted a code review cannot host the repair when the finding crosses a project boundary. `W5` held `src/AILedger.Core` and `tests`; `RC1` lives half in `src/AILedger.Cli`. releasing the item threw away its completed verifier run and its completed review — **a reviewer's cross-project finding costs a full work-item cycle, and the deeper the review looked the likelier it crossed one.** nothing distinguishes an item abandoned as a dead end from one abandoned as a mis-scope. same field failing as 34, from the other direction: occupancy and brief are two jobs on one `--scope`. |
| 42 | one-long-line-destroys-a-whole-run | `2026-09-09_1010-retrospective-projection` | open | bug | **C5, and it killed two runs in that one task.** the adapter tolerates a line it cannot *parse* — `RunCostReader` catches `JsonException` and records no measurement — and kills the run for a line it cannot *hold*. the stream and retention caps both degrade and let the run finish; only the 1 MiB per-line cap raises `ProtocolError`. R6 and R14 died that way, R14 at 7m29s with **zero ledger events and no verifier file**, on the line the xunit host printed for a full suite run — the one command a code-change verifier is certain to run. truncate the line, count it on the run, let the run finish. compounds with 36: a run that files at the end loses everything to a line printed a minute earlier. |
| 32 | a-correction-is-one-command-away-and-cannot-be-undone | not opened | open | bug | **cost measured tonight, C30.** `claim resolve --status superseded` derives refinement-versus-correction from the replacement's status at that instant. superseding by a claim still `open` lands a correction, which invalidates dependent decisions, blocks dependent work items, and cannot be undone — and a blocked item can be neither unblocked nor completed, only replaced. it cost item 6's W2 a re-verification cycle after three completed runs. one predicate and one `--accept-correction` flag; the refusal must not become a rule that forces every supersession to look like a refinement. |
| 33 | the-suite-is-not-comparable-across-runs | not opened | open | bug | **C31.** `ACleanTreeAtADifferentCommitWarns` and `ADirtyTreeAtADifferentCommitStillWarns` are the only two tests that create a real git commit, so they fail in a run whose sandbox refuses `git commit` and pass in one that allows it. the same tree reported 526/2, 526/2 and then 534/0 across three runs. three agents recorded the count correctly and none asked what the two were, so every "same failures in both states" argument in this task rests on a coincidence of permissions. prove the fire path against the already-pure `Warning`; keep one git-backed test that names the environment in its failure. |
| 28 | the-launch-does-not-ask-if-the-host-can-hold-it | `2026-09-09_0858-provider-launch-preflight` | open | feature | **directly improves delivery throughput.** `provider launch` checks authority, scope, role and staffing, but not memory headroom, provider authentication or whether the effective sandbox can reach required provider state. The memory case left two cancelled runs; the standalone-memory close-out added three 3-4 second failed runs before equivalent host-access launches completed. Add one adapter-level readiness contract, exposed by `provider preflight` and reused by launch, that refuses with safe structured diagnostics before `run.started`, process creation or token spend. |
| 38 | *no entry file* | `2026-09-09_0858-provider-launch-preflight` | open | feature | provider launch authentication and sandbox preflight — refuse before the run exists when auth or sandbox access is unavailable. third of the three preflight concerns, alongside 28 (host capacity) and 34 (scope reachability); they are the same gate and should probably be one. |
| 25 | a-productive-task-starves-its-successor | not opened | open | bug | **blocks 8, and degrades every task now.** recall orders by recency and takes ten. item 5 minted ten lessons and consumed item 6's entire budget: 38 lessons matched its tags, all ten slots went to lessons twenty minutes old, and the four closest matches in the store — including *do not put a field on run.started that the launcher learns later* — were crowded out and had to be copied in by hand. |
| 23 | attention-items-are-task-wide-but-work-is-not | not opened | open | bug | `ValidateVerifierOutput` reads attention ids from the one current plan and demands every verifier dispose all of them. item 5 had six for W1 and three for W2, so its second verifier either writes six `not-applicable` rows or the plan stops describing the task. |
| 14 | mirror-the-replay-validator | `2026-09-07_2136-mirror-replay-validator` | open | bug | |
| 11 | attention-items-as-a-gate | `2026-09-07_2136-attention-item-gate` | open | feature | |
| 15 | waivers-need-a-floor | `2026-09-07_2136-waiver-floor` | open | change | |
| 16 | open-claims-block-archive | `2026-09-07_2136-archive-open-claims` | open | change | see 20, `research-needs-an-open-claim` — closing every claim currently locks a task out of Archive, which this entry would make worse. |
| 17 | decompose-the-cli | `2026-09-07_2136-cli-file-scope` | open | change | |
| 18 | archived-memory-conservation | `2026-09-07_2136-archive-compaction` | open | feature | |
| 24 | cancelled-means-four-different-things | not opened | open | bug | item 5 holds five `Cancelled` runs: four operator filing runs that succeeded, and one nine-minute verification the host killed for memory. `self-scoring` asks for failed and retried runs as a cost signal and would read five where the true number is one. |
| 10 | single-agent-relaxation | `2026-09-07_2136-single-provider-mode` | open | change | |
| 39 | standalone-semantic-memory | `2026-09-08_1909-standalone-memory-index` | **delivered, merged, disengaged** | feature | phase 0/1 delivered and now on this branch: an explicit seven-command SQLite/FTS5 shadow index over ledger histories, lessons and refusal journals, with exact-vector fusion and an evaluation harness whose baseline is the current tag-and-recency recall. Nothing in the kernel reads it — the boundary is a package boundary and `ShadowBoundaryTests` asserts it. **The value question is untouched:** no embedding model is installed (`ollama list` holds only `phi4`, a chat model), the three database identities under the platform data directory are empty, `ailedger-memory` is not installed, and the evaluation set is ten fixtures rather than real records. Phase 2 is repository ingestion, specified and unbuilt. Activation needs a live model-identified evaluation that beats the baseline, plus explicit authorization — see the entry's activation gates. Its activation task, `2026-09-09_2118-memory-index-activation`, carries one open escalation, XX1, for the operator: no sandbox here reaches loopback, so only the operator can measure whether the chunk bound reaches full coverage on the live corpus. |
| 37 | what-a-run-actually-costs | measurement | **read for 7 and 9** | change | not work to do. the first five runs with cost fields, read: the **fixed brief is ~56%** of a run's cost, the agent's own output ~24%, everything it read with tools 22% — of which whole-file reads are 70% on a coding run and raw `events.jsonl` digging is 96% on an analysis run. the manifest measured 36,000 tokens, 92% artifacts, re-read every turn. summing the buckets overstates 7.2-8.7×, confirming C7. also carries the settled decision that the embedding index updates at run close, not per edit, and why. |
| 9 | score-the-governance | `2026-09-07_2136-workflow-retrospective` | open | feature | the self-scoring capability. `self-scoring.md` is the rubric and the authority on what the dimensions mean; `score-the-governance.md` is the shape and the order. blocked on 5 to 8: its two headline dimensions — governance effectiveness and governance cost — are the two the ledger currently cannot measure. and blocked on 21 for the same reason 8 is empty: the coordinator's own cost is unrecordable while a coordinator holds no closeable run. |

## Done

| # | backlog entry | task id | status | kind | notes |
|---|---------------|---------|--------|------|-------|
| 1 | record-that-context-was-built | `2026-09-07_2136-run-manifest-hash` | **done** | feature | archived with one waiver on Learn: W1 was completed before any reviewer run, and RC4/RC5 prove that closes every route to a governed review. 11 runs, 12 claims, 4 lessons. |
| 2 | did-the-lesson-matter | `2026-09-07_2136-lesson-citations` | **done** | feature | archived with no waiver. Reviewer ran before `work complete`. 7 runs, 10 claims all validated, 4 lessons. Its code reviewer also found and fixed three defects in item 3's code. |
| 3 | make-status-say-what-is-owed | `2026-09-07_2136-status-owed` | **done** | feature | archived with no waiver. 5 runs, 10 claims all validated, 4 lessons. The verifier found VC1 (one citation cleared the whole lesson debt) and the reviewer found KC4 (cited and recalled counts drawn from different populations) and KC5 (no test that the projection agrees with the gate). The waiver count is withdrawn from scope by accepted decision LD1: state carries no durable waiver record. |
| 4 | kernel-version-stamp | `2026-09-07_2136-kernel-version-stamp` | **done** | feature | archived with no waiver. The version half already shipped in `install.sh`; this added `ailedger version` and the staleness warning. 5 runs, 4 lessons. Eight defects found by the working, verifier and reviewer runs — including the feature being entirely non-functional on a clean tree, and four separate tests that passed while proving nothing. Scoped to `src/AILedger.Cli` and `tests` only, ahead of self-scoring, because scope occupancy is per-task (LC2). |
| 5 | record-the-refusals | `2026-09-08_1048-refusal-journal` | **done** | feature | archived with no waiver. 16 runs, 2 work items, 42 claims, 62 evidence, 8 decisions, 11 alternatives, 2 challenges, 10 lessons. Both write sites shipped: 141 command-time rules at the service, and the seven authority-and-scope refusals `ResolveProviderGrants` decides before `run.start`. Two repair cycles — a concurrent append that unit tests passed over, and a public method that verification passed over. Seven new backlog entries came out of it, items 21 to 24 plus three earlier. |
| 6 | see-inside-a-run | `2026-09-08_1428-run-cost` | **done** | feature | **blocks 9.** `AgentRunResult` carries the provider's own event stream — turns, tool calls, tokens — and the launcher wrote it to stdout and dropped it. 187 runs, 0 with cost recorded, which is the measurement that proves the governance-cost dimension is dark. W1 shipped the event fields and the reader; W2's worker run landed the launcher half and 16 tests and is with the verifier. Two follow-ons and two live defects came out of it: rows 30 and 31, plus claude's `total_cost_usd` and `subagent_stats` on the same event. |
| 7 | measure-before-scoring | `2026-09-09_1010-retrospective-projection` | **done** | feature | archived with four waivers — Research, Design, Scope, Execution — the same four as 6 and for the same reasons: no open claim left, no Researcher run staffed, no `PromptContract` or `UserRequest` ever filed. delivered `TaskRetrospective` and `retrospective build`: counts, durations, causal chains, refusals, stages with waiver reasons verbatim, evidence by source type, and a required `notMeasured` list. **15 runs, 4 of which bought no finding; 40 claims all validated; 58 evidence across four source types; 4 lessons minted.** the code reviewer found RC1 after three verifier passes over the same code — a present-but-partly-unreadable refusal journal read as a complete measurement — and repairing it cost a whole extra work item because scope cannot be widened (row 41). two runs died to the 1 MiB line limit (row 42). its own first output names exactly two things it cannot measure: `coordinatorCost` and `outcomeQuality`. |
| 45 | the-six-pipeline-skills-rewritten-against-the-kernel | `2026-09-10_1007-rewrite-the-skills` | **done** | change | archived 2026-09-10 with four lessons minted and six stage arms waived with reasons. The skills no longer point at the pre-kernel Python ledger, tell an agent to hand-edit state, or restate rules the kernel refuses. Its residue is validated claim WC5: `cognitive/RULES.md` is outside the context freshness gate because `ContextSkills.From` filters to `ContextArtifactKind.Skill`, so the one artifact served to every role can change without invalidating any brief. |

## Where to resume, 2026-09-10 afternoon

Installed `2.0.83` from `565e3f3`. `main` and the branch were level at that commit; everything since
is uncommitted. **The skills need no install** — `cognitive/` is read from disk at runtime, so
`install.sh` moves C# only.

### The five things that changed today

    the context gate    work add and provider launch refused without a context.built event
                        carrying per-skill content hashes. Built, FAILED verification on three
                        counts, repaired (JC1-JC6), and it has refused the coordinator five times
    two doors           --without-brief REASON (operator only) and --with-stale-brief EVIDENCE-ID
                        (the --not-split-because shape: the kernel checks the record exists, never
                        that it is a good reason). Used live to launch the doors' own verifier
    six skills          rewritten against the kernel, verified twice, W1 completed. Pre-kernel
                        paths gone, instructions to hand-edit projections replaced with `ailedger`
                        commands, the brain pattern kept verbatim
    RULES.md + rubric   the two served artifacts the first pass missed, now rewritten. RULES.md
                        reaches every role, so it mattered more than any skill
    D1                  the pipeline skills are kernel-invoked only. Six copies retired from
                        `$CODEX_HOME/skills` to `skills-retired-2026-09-10` with a note

### In flight when this note was written

    2026-09-10_0845-pipeline-mandatory   R6  codex verifier on the doors
    2026-09-10_0907-append-in-place      R5  codex worker, the atomicity repair

A persistent Monitor watches every run in every task and prints `CLOSED <task>/<run> -> <status>`.
Its script is `runwatch.py` in the session scratchpad; re-arm it first thing if it is gone, because
three stalls today came from hand-curated watchers.

### The merge gate

    R6 passes                → commit Core, Cli, tests, cognitive; merge; push; install
    src/AILedger.Storage       stays uncommitted until R5 finishes AND verifies — a half-written
                               atomicity fix is exactly what must not ship

### Open, and each is one command or one run

    XX1  memory-index    does the chunk bound reach full coverage on the live corpus?
                         D1 there makes this the OPERATOR's measurement: no sandbox reaches loopback
    X1   overlong-line   which Core field carries the truncated-line count? Core is free now,
                         so re-scope W1 over Providers + Core + Cli and unblock it
    W1   researcher-can-prove-it-ran   BLOCKED on Core, brief is K1, ready to launch
    C11  RULES.md is outside the freshness gate — one line, fold into the next Core item
    zero lesson marks on any live task. Every closeout is still owed

### Item 9, and my estimate for it is void

`2026-09-07_2136-workflow-retrospective` now holds `PA-CONTRACT-1` (PromptContract) and
`PA-PLAN-1` (OrchestrationPlan), produced by a planning lead — the first planning-lead run of the
session. Seven claims, all validated, three of which overturn the framing I gave it:

- `PC3` — **the projection scores no dimension at all.** "Six of ten are counts and joins" is
  wrong; ten of ten need a model, and what varies is how much evidence the log supplies per
  dimension. My 4-5h narrow / 8-12h full estimate rests on the refuted premise.
- `PC1` — items 1 to 4 are the **darkest-instrumented** archived tasks in the ledger. All four
  predate the refusal journal, the run-cost fields and the projection, so calibrating against them
  would pass by measuring nothing.
- `PC7` — filing a `WorkflowRetrospective` through a producer run **reproduces item 21 on every
  task forever**. `UserRequest` is the one operator-authored kind that forbids a producer run, and
  that branch is the model to copy.

Read the plan before re-estimating. It chose `decompose` over four disjoint file sets.

### The behavioural correction that explains most of today

`C3` on `2026-09-10_0907-append-in-place`: **the coordinator has been writing prompt contracts and
filing them as constraints.** Every `K`-numbered brief carries a goal, constraints, success criteria
and stop conditions, recorded through `constraint add`. No task opened this session held a
`PromptContract` until `A2`. That is why Scope was waived four times, and skipping pipeline step 2
is the measured cause of four mis-scoped work items in one day.

## Ordering notes

Every item keeps its original number because the notes above and `measure-before-scoring.md` refer
to items by number. New items begin at 44; numbers are never reused or renumbered.
Items 1 to 4 keep their numbers because the notes above and `measure-before-scoring.md` refer to
them by number.

The open table is ordered by blockage first, then cheap fixes with high return, then measured cost
that is active today. Item 26 leads because a small isolation fix restores an entire required role;
20 and 29 follow because they block the pipeline or every stage-driven feature. Items 47 and 48 are
next because one makes two roles unable to satisfy their briefs and the other lets a refusal satisfy
the Learn gate. Item 46 follows because it is actively serialising unrelated work across tasks.

Items 35, 31, 30 and 43 are the cheapest high-return repairs: one default, one role grant, one
protocol guard, and one exact-key retrieval path. Items 36 and 34 follow on measured loss: fifty
runs recorded nothing, while one task accounts for 63% of all lost runs. Delivered items 44, 13 and
8 remain high because their remaining verification or closeout is cheaper than starting new work.
The remaining rows descend by stated blockage, live cost, evidence strength and expected effort;
measurement-only rows and deliberately disengaged work are last, followed by item 9 because the
self-scoring target is blocked by almost everything above it.

Items 5 to 9 are the self-scoring cluster. 5 and 6 are independent of each other and both feed 7.
8 is independent of all three and gates only 9's output half. None of the four needs the scoring
agent to exist, and all four are useful without it.

Items 19 and 20 were found while working items 1 and 2 and originally sat unranked. Item 20 is now
near the top because Discovery's only exit refuses without it; item 19 remains below delivered
closeouts because it is a smaller completion-order defect.

Items 19 to 27 have no governed task of their own.

Item 39 is delivered and deliberately disengaged. Its code is merged so the merge cost stops
growing — 26 commits had landed since its branch point and only this file conflicted — not
because activation is near. Shadow mode is what makes landing it safe; the activation gates in
its own entry are unchanged and unmet.

Item 5 is done. Its own numbers are the first measured baseline for what a governed pass costs:
1h51m of agent execution across 16 runs, 3h20m wall clock, for a 78-line writer plus two
catch blocks. Five of those runs were `Cancelled` and four of the five had succeeded — see item 24. Only an operator may open one.

Items 34 to 37 came out of reading item 6's own output — the first five runs with cost fields —
together with every refusal and every non-completed run in the ledger. They are the first findings
in this backlog derived from measurement rather than from working a task. 37 is not work: it is the
baseline 7 and 9 were waiting for, and it holds the numbers so the next reading has something to
compare against. 34 was ranked above the rest of that measured group on the strength of one figure
— one task holds 63% of all run losses — and it was first written as a cross-repository limit before
the worktree was confirmed; the entry carries the correction. The current table places the broader
role and pipeline blockers, then the cheapest high-return repairs, ahead of it.

Item 38 has a governed task and no entry file. Together with 28 and 34 it is the third of three
preflight concerns — auth and sandbox, host capacity, scope reachability — which are one gate asked
three times.

`self-scoring.md` has no row of its own. It is the rubric item 9 is built to satisfy, not a
separate piece of work, and it stays the authority on what the dimensions mean.

## Item 7, as it closed

`2026-09-09_1010-retrospective-projection` is archived. Six work items: W2, W3, W4 and W6
completed; W1 released for naming an alternative that justified nothing about scope; W5 released
because its two areas did not reach where its own code review's finding lived.

It shipped `TaskRetrospective` and `retrospective build` — counts, durations, the causal chains the
log can join, refusals, stage transitions with their waiver reasons verbatim, evidence keyed by
source type, and a required `notMeasured` list. No score, no grade, no model.

Its first output on its own record: 40 claims all validated, 58 evidence across four source types
(test-run 18, source-read 17, live-run 13, local-probe 10), 16 refusals with zero unreadable rows,
11 stage transitions with 4 waivers, 4 lessons minted. `notMeasured` names exactly two things —
`coordinatorCost` and `outcomeQuality`.

**Four lessons minted:** C5 (one overlong line destroys a run), C6 (outcome quality belongs to the
scorer — a retracted coordinator judgement, kept so the correction is inherited), C7 (evidence by
source type), C9 (completing every item closes every route to a review).

**Two defects found and filed rather than fixed:** row 41, scope cannot be widened, so a
cross-project review finding costs a whole work-item cycle; row 42, one line above 1 MiB ends a run
with nothing recorded, which killed two verifiers here.

**One thing that paid off unasked:** verifier R15 used W6's `baseRef` — the field this task's own
W3 added — to define its comparison tree without being told to. A role that never requested the
feature made it load-bearing.

### What item 7 cost

    15 runs   5 worker, 8 verifier, 1 code reviewer, 1 operator filing run
              11 completed, 1 cancelled, 1 failed, 2 protocolError — a 27% loss rate
    findings  verifier 3 on W2, plus a FAIL on W4 discharged by reinstalling and proving it;
              reviewer 1 Major after three verifier passes; worker 2 volunteered as
              unpinnable-and-here-is-why (IC10, IC18)
    tokens    94,512,182 cache read against 895,044 uncached — 106 : 1
              389,790 output; turns measured on 5 of 12, because codex reports none
    span      4.3h elapsed, 3.0h of it with an agent actually running

Five of the fifteen runs bought no finding: the operator run that structurally cannot close (item
21), **two** verifiers killed by the line limit (C5, and row 42), a verifier refused its artifact
because the coordinator had filed no plan, and a reviewer launch refused at a completed item (C9).

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
