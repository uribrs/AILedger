# Priorities

Status as of 2026-09-08. `done` means the governed task reached stage `archive`.

| # | backlog entry | task id | status | notes |
|---|---------------|---------|--------|-------|
| 1 | record-that-context-was-built | `2026-09-07_2136-run-manifest-hash` | **done** | archived with one waiver on Learn: W1 was completed before any reviewer run, and RC4/RC5 prove that closes every route to a governed review. 11 runs, 12 claims, 4 lessons. |
| 2 | did-the-lesson-matter | `2026-09-07_2136-lesson-citations` | **done** | archived with no waiver. Reviewer ran before `work complete`. 7 runs, 10 claims all validated, 4 lessons. Its code reviewer also found and fixed three defects in item 3's code. |
| 3 | make-status-say-what-is-owed | `2026-09-07_2136-status-owed` | **done** | archived with no waiver. 5 runs, 10 claims all validated, 4 lessons. The verifier found VC1 (one citation cleared the whole lesson debt) and the reviewer found KC4 (cited and recalled counts drawn from different populations) and KC5 (no test that the projection agrees with the gate). The waiver count is withdrawn from scope by accepted decision LD1: state carries no durable waiver record. |
| 4 | kernel-version-stamp | `2026-09-07_2136-kernel-version-stamp` | **done** | archived with no waiver. The version half already shipped in `install.sh`; this added `ailedger version` and the staleness warning. 5 runs, 4 lessons. Eight defects found by the working, verifier and reviewer runs — including the feature being entirely non-functional on a clean tree, and four separate tests that passed while proving nothing. Scoped to `src/AILedger.Cli` and `tests` only, ahead of self-scoring, because scope occupancy is per-task (LC2). |
| 5 | self-scoring | `2026-09-07_2136-workflow-retrospective` | open | |
| 6 | single-agent-relaxation | `2026-09-07_2136-single-provider-mode` | open | |
| 7 | attention-items-as-a-gate | `2026-09-07_2136-attention-item-gate` | open | |
| 8 | resolve-claims-where-the-evidence-lands | `2026-09-07_2136-claim-resolution-ergonomics` | open | `status` now reports 76 of 80 open claims on `ledger-learning` are clearable in one pass, which is higher than the entry estimated. |
| 9 | the-append-is-quadratic | `2026-09-07_2136-append-in-place` | open | |
| 10 | mirror-the-replay-validator | `2026-09-07_2136-mirror-replay-validator` | open | |
| 11 | waivers-need-a-floor | `2026-09-07_2136-waiver-floor` | open | |
| 12 | open-claims-block-archive | `2026-09-07_2136-archive-open-claims` | open | see `research-needs-an-open-claim` — closing every claim currently locks a task out of Archive, which this entry would make worse. |
| 13 | decompose-the-cli | `2026-09-07_2136-cli-file-scope` | open | |
| 14 | archived-memory-conservation | `2026-09-07_2136-archive-compaction` | open | |

## Added while working items 1 and 2

| backlog entry | why |
|---------------|-----|
| review-before-complete | `work complete` requires a verifier but not a reviewer, and completing the item makes the review impossible. Cost item 1 its only waiver. |
| research-needs-an-open-claim | Discovery's only exit is Research, and that arm refuses without an open claim. Hit on both tasks. |

## Kernel state

`2.0.25-dirty from b088254`, 396 tests passing, installed and current. Carries the manifest hash on
`run.completed`, `--from-lesson` on claim/decision/alternative, and the `owed` block on `status`.

## Estimates

The cost lines in the backlog entries describe the diff, not the task. Item 1's diff was two nullable
fields; the governed pass took 7h32m wall clock, of which 53 minutes was agent execution.
