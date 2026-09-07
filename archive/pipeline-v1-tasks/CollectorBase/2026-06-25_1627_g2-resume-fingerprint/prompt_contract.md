Role:
You are a .NET engineer hardening a checkpoint-resume guard's test coverage and correcting its documentation.

Goal:
Lock the authoritative resume gate's start-fresh paths (fingerprint mismatch; schemaVersion bump) with tests,
and correct the doc that wrongly claims the coarse SDK pre-check (`CanResumeFrom`) enforces the fingerprint —
without changing the (correct) authoritative `Restore` gate.

Context:
- `CheckpointManager.CanResume` (`:114`) is coarse (`V`+`Stream` only) and STRUCTURALLY cannot check the
  fingerprint — `CanResumeFrom(AdapterCheckpoint)` has no dispatch event. `Restore` (`:17`) → `CanUseState`
  (`:142`) is the authoritative gate (V+Fingerprint+StepIndex+Strategy); mismatch → fresh seed (`:39-41`) → no
  wrong-data. `docs/multi-step-flows.md:227` overstates CanResumeFrom.
- Existing resume tests only cover the matching-fingerprint happy path.

Constraints:
- See constraints.md. Salient: do NOT change Restore/CanUseState behavior; do NOT fake a fingerprint check into
  CanResume; keep it coarse + document; no vendor identity; net8.0/CollectorBase.slnx; 77 prior tests stay green.

Success Criteria:
1. `dotnet build CollectorBase.slnx` clean.
2. `dotnet test CollectorBase.slnx` — 77 prior pass + two new tests: (a) a v-current but fingerprint-MISMATCHED
   checkpoint → Restore starts fresh (from-start fetch, not the resumed stale cursor); (b) schemaVersion bump →
   fingerprint changes → start fresh.
3. `docs/multi-step-flows.md:227` corrected: CanResumeFrom = coarse pre-check; fingerprint + step-shape +
   schemaVersion authoritative in Restore.
4. No regression (full suite green).
5. note.md / execution_notes records: why CanResumeFrom is structurally coarse, why no wrong-data results
   (Restore authoritative), the two locked invariants, and the A1/A2 resolutions.

Execution Rules:
- Pin A1 (harness test preferred) + A2 (keep CanResume coarse) up front.
- Construct checkpoints whose fingerprint deliberately differs (changed input or stream; bumped schemaVersion).
- Do not re-investigate; do not alter the authoritative gate. Respect constraints.

Output Format:
- Doc edit (docs/multi-step-flows.md), optional CanResume comment, tests in Tests/CollectorExecutor.Test,
  note + execution_notes in the task dir.

Stop Conditions:
- Locking the invariant would require changing the authoritative Restore gate — stop and surface (it shouldn't).
- Goal achieved and full suite green.
