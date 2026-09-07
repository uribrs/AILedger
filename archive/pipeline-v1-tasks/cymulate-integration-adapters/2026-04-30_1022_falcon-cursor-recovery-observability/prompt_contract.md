Role:
You are a senior .NET collector engineer.

Goal:
Fix Falcon findings recovery cursor correctness and add observability that shows where the collector is progressing by date/segment while preserving safe cursor semantics.

Context:
The Falcon findings flow uses inverted month segmentation and CrowdStrike Spotlight `after` cursors. Two review findings must be addressed:

* Resume currently rebuilds active/open month segments with `DateTime.UtcNow`, which can change the filter ceiling while reusing a checkpointed `after` cursor.
* The Spotlight runner does not guard against a repeated non-empty `after` token after publishing a non-empty page.

The existing local mock simulator under `--falcon-recovery-simulation` should be used and extended as reference evidence.

Constraints:

* Preserve identical filter and sort while continuing a CrowdStrike `after` cursor chain.
* Resume an active/open month cursor chain using the checkpointed `MonthSegmentEndExclusiveUtc`.
* Only rebuild a findings date filter when dropping `after` and falling back to watermark-based recovery.
* Prevent infinite loops if Spotlight returns the same non-empty `after` token for a non-empty page.
* Prefer structured logs with segment, cursor, watermark, page, and progress fields.
* Avoid logging full cursor tokens at information level.
* Keep logs useful for local runner and production investigations.
* Extend the local mock simulator as reference evidence for future debugging.
* Keep code changes scoped to Falcon findings recovery, runner safety, simulator coverage, and related tests.
* Do not call real CrowdStrike during validation.

Success Criteria:

* Resuming a checkpointed findings cursor uses the checkpointed segment start and end, not a newly computed active-month end.
* Repeated non-empty Spotlight `after` tokens cannot cause an infinite publish loop.
* Cursor rejection or repeated cursor fallback uses last successful watermark and boundary IDs when available.
* Logs clearly indicate resume position, active segment, page, watermark, cursor presence, and approximate segment/day progress.
* Cursor-derived date hints are best-effort and do not affect correctness.
* Local mock simulator includes a repeated-cursor scenario or equivalent evidence.
* Relevant unit tests and/or local simulator runs validate the fixes.
* Task state files are updated with decisions, validation, residual risks, and commands run.
* A verifier subagent reviews the completed work against this contract before final response.

Execution Rules:

* Do not assume missing data.
* Respect constraints strictly.
* Keep cursor correctness separate from observability.
* Prefer small methods and local helper types over broad abstractions.
* Work with existing uncommitted changes and do not revert unrelated edits.

Output Format:
Final response should include:

* Files changed.
* Verification commands and results.
* Behavior summary for both review findings.
* Any residual risk.

Stop Conditions:

* Stop when the goal is achieved and verifier review is complete.
* Stop if required task state conflicts with code or user instruction.
* Stop if validation requires unavailable external services.
