# Constraints

- No behavior change to the authoritative `Restore`/`CanUseState` gate — it is correct (mismatch → fresh → no wrong-data).
- Do NOT fake a fingerprint check into `CanResume`/`CanResumeFrom` — it has no dispatch event and structurally cannot compute the fingerprint. Keep it coarse; make the doc honest.
- Strengthening `CanResume`'s coarse check (e.g. StepIndex sanity) is OPTIONAL only (A2) — not required, no event-dependent logic.
- Generic engine, no vendor identity; net8.0; CollectorBase.slnx.
- No regression of the 77 passing tests (especially the existing resume/checkpoint tests).
- Tests use the existing in-process harness; prefer end-to-end (ResumeAsync) proof over a pure unit assertion (A1).
