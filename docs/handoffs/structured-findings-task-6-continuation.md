# Continue task 6 — live trial and delivery decision pending

Work in `/Users/user/.codex/worktrees/structured-findings-contract/AILedger`, branch
`codex/structured-findings-contract`. Inspect git status first and preserve subsequent changes.
Task 5 is `5d22e6b`; discovery fix is `cd3f2d9`. Find the subsequent prepared-pilot checkpoint commit
with `git log`. **Task 6 is not complete; do not start task 7.**

Read [task-6 pilot](../structured-findings-v1/task-6-pilot.md),
[validation checkpoint](../structured-findings-v1/task-6-validation.md),
[pilot README](../../tools/FindingsPilot/README.md), and the original
[task-6 scope](structured-findings-task-6.md). The frozen baseline and trust/measurement boundaries
remain mandatory. Kernel development workflow remains OFF; no ai-kernel, governed development tasks,
development governance records, agent dispatch or global instruction/skill changes. Build externally.
No merge/publish/tasks 7–8. Keep old validation records and expected outputs unchanged.

## Completed

- Read-only RN1 extraction from the first prepared script and its forty accepted canonical records,
  exact provenance/hashes, and a separate text-difference inventory.
- 21 real local-MCP prepared-data trials: two batch layouts across seven repetitions each, an
  accepted-text counterpart, six failure/recovery scenarios, early persistence, exact fidelity,
  no rejected prefix/duplicate writes, and existing retrospective/measurement reports.
- Standard and repository-aware full suites: 1,901 main + 99 Memory passing, no skips/failures.
  All 11 frozen hashes and four report/four cost baselines unchanged. Extraction reproduces exactly.
- Report and recommendations, with no causal speedup/cognition claim.
- Prepared one bounded Codex trial with four tool attempts and a 240-second launch timeout.

## User authorization and pending work

The user asked to start task 6 and later explicitly asked to install the verified findings interface
so they could try a governed task. Installed version is **2.0.163 from cd3f2d9**, built from a clean
archive outside the checkout. Do not reinstall merely because the checkpoint adds pilot tools.

An approval question was presented for one billable Codex episode capped at 240 seconds. At this
checkpoint **no answer has been received and no provider has run**. Check subsequent user messages:
if they approved this exact trial, retain that authorization; do not ask again. Otherwise obtain the
explicit approval before execution. Installation approval is not provider-spend authorization.

The concrete prepared plan is `/tmp/ailedger-task6-live-prepared/trial-plan.json`; the goal is beside
it. It inspects six actual source snapshots about external-output test discovery and limitations.
It records an early finding before later inspection, then deliberately tries a missing existing
reference, corrects it, and identically replays the successful second body. This live replay does
not simulate a lost response; deterministic lost-response recovery has already passed.

After authorization, the exact execution command is:

```sh
python3 tools/FindingsPilot/live_trial.py codex --cli /tmp/ailedger-task6-build/bin/AILedger.Cli/debug/AILedger.Cli.dll --output /tmp/ailedger-task6-live-prepared --execute
```

The harness checks CLI/goal/source hashes, creates a fresh disposable ledger, uses production provider
composition and existing grants, retains logs, and refuses a repeated execution directory. If temporary
artifacts expired, build externally and prepare a new equivalent plan first. Do not bypass hash checks
or retry a billable episode implicitly. Live execution has not yet tested this new harness; retain
failures honestly and fix local mechanical issues without broadening grants.

After the episode:
1. Inspect the actual transcript for early recording before later reads, tool attempts and other
   permission/search denials. Mechanical receipt counts alone do not prove ordering or semantic truth.
2. Review both observations/citations against frozen source snapshots. Record uncertainty and any
   lost/altered content; distinguish expected kernel refusal from execution failure.
3. Inspect `retrospective.out`, original completion and provider sidecar. Preserve per-run actual
   usage once, timings/manifest/session attribution, collection gaps and absent provider fields.
4. Append a live validation record/report update with actual results. Do not erase this pending
   checkpoint or earlier failed runs. Re-run checks appropriate to any subsequent code changes.
5. Present the delivery decision for operator review, including whether to keep the interface as-is
   or scope a next operation. Task 7 remains conditional; do not infer approval for it.
6. Commit only the remaining task-6 changes and update backlog acceptance honestly.

The user requested a heads-up before context becomes tight and is willing to move to a fresh chat.
Use this handoff rather than redoing the completed extraction or benchmark.
