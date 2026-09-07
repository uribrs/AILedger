Role:
You are a senior .NET engineer working in the cymulate-integration-adapters repository.

Goal:
Implement FalconCollector segmented collection so a host-provided base date is split into calendar month segments and collected from newest segment to oldest segment, with recovery checkpoint data containing both the existing next cursor and the current month segment.

Context:
FalconCollector currently receives a base date from the host, translates it into the collector request structure, sends requests to Falcon, and checkpoints the next cursor for recovery. The desired behavior is to collect recent assets first by processing monthly date segments in reverse chronological order, for example a six-month window spanning Nov25 through Apr26 should collect Apr26, then Mar26, then Feb26, and continue backward.

Constraints:

* Use existing FalconCollector conventions, folder structure, request models, orchestration, checkpoint, and recovery patterns where available.
* Keep implementation focused on FalconCollector behavior and shared code only if Falcon already uses a shared recovery/checkpoint contract that must be extended.
* Preserve the current next cursor checkpoint behavior.
* Extend recovery checkpoint data with the current month segment without breaking existing checkpoint consumers.
* Collect month segments from newest to oldest.
* Keep month segments deterministic, non-overlapping, and complete for the host-provided base date through the effective collection end date.
* Ensure recovery resumes within the same month segment before moving to older segments.
* Avoid forced abstraction; add helpers/classes only where they reduce local complexity or match existing patterns.
* Prefer small methods and single-responsibility components.
* Add focused tests for month segmentation order, request translation, checkpoint payload, and recovery resume behavior.
* Do not perform broad collector refactors unrelated to segmented collection and recovery.

Success Criteria:

* FalconCollector builds Falcon requests for one month segment at a time from the host-provided base date window.
* FalconCollector processes month segments newest-to-oldest.
* The recovery checkpoint includes the existing next cursor and the current month segment.
* Recovery resumes using both checkpoint values so an interrupted segment continues correctly before the collector advances to older segments.
* Existing recovery checkpoints that contain only a next cursor are handled intentionally and do not crash unexpectedly.
* Tests cover normal segmented collection, partial current/base months, checkpoint creation, checkpoint restore, and legacy checkpoint compatibility.
* Existing FalconCollector tests continue to pass.

Execution Rules:

* Do not assume missing data.
* Respect constraints strictly.
* Read `ai/active/2026-04-28_1232_falcon-month-segment-recovery/state.json` first.
* Read only the task files relevant to the current execution step after reading state.
* Update `state.json` when assumptions are validated or rejected, blockers are found or resolved, implementation steps change, or verification completes.
* Inspect existing FalconCollector request, pagination, checkpoint, and recovery code before editing.
* Prefer local helper methods/classes near FalconCollector unless an existing shared checkpoint abstraction requires extension.
* Keep all changes small, testable, and aligned with the repository's .NET conventions.

Output Format:
Provide:

* Summary of changed behavior.
* Files changed.
* Assumptions validated or rejected.
* Tests run and results.
* Remaining risks or follow-up work, if any.

Stop Conditions:

* When goal is achieved and verification is complete.
* When required data is missing and assumptions would cause wrong request ranges, wrong checkpoint semantics, unsafe recovery behavior, or broad unrelated refactoring.
