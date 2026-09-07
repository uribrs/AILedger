Role:
You are a senior .NET integration engineer working in the cymulate-integration-adapters repository.

Goal:
Build and run a local Falcon findings recovery simulation that uses a local fake CrowdStrike-compatible HTTP server to exercise checkpoint/resume behavior across month segments, with 2-3 controlled interruption/recovery scenarios and inspectable logs/published batches.

Context:
The previous task `ai/active/2026-04-29_1450_falcon-partial-page-success` proved a local fake HTTP run can drive FalconCollector through normal token, probe, page, and terminal-response paths. This task should use that as a concept reference, but target recovery behavior instead of partial-exit behavior. The user specifically wants a local run that passes through months, exits/restarts/recoveries, and leaves logs plus collected items for inspection.

Constraints:

* Use `ai/active/2026-04-29_1450_falcon-partial-page-success` only as a concept reference.
* Build a local runnable simulation, not only unit tests.
* Use a local mock server or equivalent local HTTP endpoint that the Falcon collector can call through normal HTTP/session paths.
* Exercise recovery behavior, not partial-exit/DONE-with-error strategy.
* Include month segmentation behavior in the simulated data.
* Include 2-3 interruption and recovery scenarios.
* Preserve normal collector request, checkpoint, publish, and local runner logging paths where practical.
* Produce inspectable local logs and published batch outputs under `logs/`.
* Do not use real CrowdStrike credentials or real vendor network calls.
* Keep product-code changes minimal and scoped to simulation support if needed.
* Do not change unrelated collectors.
* Do not silently assume recovery semantics; document observed behavior from logs and outputs.

Success Criteria:

* A local mock Falcon recovery simulation can be run from the repository with a documented command.
* The simulation covers at least two recovery scenarios and preferably three:
  * resume within the active month with an existing Spotlight `after` cursor,
  * resume after cursor rejection/fallback using watermark inside the same month segment,
  * resume after completing one month and continuing into older month segments.
* The simulation produces local runner logs under `logs/` and published batch outputs under `logs/published-batches/` or a clearly documented simulation-specific folder.
* Logs show the resume point, checkpoint state used, month segment start/end, after-token presence, and collected page/batch progression.
* Published outputs can be inspected to confirm records continue in the expected order without unexpected duplication across recovery.
* The simulation can demonstrate whether the active cursor filter ceiling changes on resume.
* Any confirmed bug or ambiguous behavior is summarized with exact evidence from the simulation logs/output.
* Targeted build/test or simulation command completes, or any blocker is documented with the command and failure.

Execution Rules:

* Read `state.json` first, then relevant markdown task files before execution.
* Do not assume missing data.
* Respect constraints strictly.
* Prefer the existing LocalAdapterRunner and existing mock-run patterns before introducing new tooling.
* Keep the mock dataset small but shaped like the real issue: multiple months, many records sharing timestamps, and cursor tokens that encode progression.
* Keep changes small, local, and reversible; avoid broad collector refactors.
* Update `state.json`, `execution_notes.md`, `assumptions.md`, and `decisions.md` as assumptions are validated or rejected.

Output Format:
Report the created/changed files, the simulation command(s), where logs and published batches were written, scenario results, and any remaining blocker or risk.

Stop Conditions:

* When the local recovery simulation is runnable and its results are analyzed.
* When required runner/mock capabilities are missing and guessing would waste implementation.
* When sandbox/approval restrictions block a required command.
