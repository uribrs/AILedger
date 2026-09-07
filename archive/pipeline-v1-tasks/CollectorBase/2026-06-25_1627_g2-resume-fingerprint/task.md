# Task: Resume / fingerprint integrity

Lock the authoritative resume-gate's start-fresh paths with tests, and correct a doc that overstates the
coarse SDK pre-check.

## Code-grounded facts (established)
- `CheckpointManager.CanResume(checkpoint)` (`:114`) = `V==CurrentVersion && Stream!=""` only. Exposed as SDK
  `IResumableAdapter.CanResumeFrom` (`Adapter.cs:298` → `Runner.cs:715`). Receives only the checkpoint, NOT the
  dispatch event → structurally CANNOT compute the fingerprint → necessarily coarse.
- `Restore(...)` (`:17`) has the event; `CanUseState` (`:142`) checks V + Fingerprint + StepIndex + Strategy
  (step-shape); on mismatch → "starting fresh" (`:39-41`) → fresh seed. AUTHORITATIVE gate → mismatch → fresh →
  no wrong-data.
- Doc drift: `docs/multi-step-flows.md:227` claims CanResumeFrom checks fingerprint + step shape (it can't).

## Scope
1. Correct the doc: CanResumeFrom = coarse "resumable-shape present" pre-check; fingerprint/step-shape/
   schemaVersion enforced authoritatively in Restore (start-fresh on mismatch). No faked fingerprint check in
   CanResume.
2. Add the two missing tests on Restore: fingerprint-mismatch → fresh; schemaVersion-bump → fresh.

## Out of scope
No behavior change to the (correct) authoritative Restore gate.
