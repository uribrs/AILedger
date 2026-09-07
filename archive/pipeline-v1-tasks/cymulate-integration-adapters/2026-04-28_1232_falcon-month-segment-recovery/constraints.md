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

