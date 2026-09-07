# Verifier-1 — Group 2: resume / fingerprint integrity

Independent, adversarial verification. Suite re-run by the verifier.

## Per-criterion

### 1. `dotnet build CollectorBase.slnx` clean — PASS
`0 Error(s)`, 12 warnings (all pre-existing NU1507 central-package-management source-mapping warnings, unrelated to this change).

### 2. `dotnet test` — 77 prior + 2 new — PASS
`Passed! - Failed: 0, Passed: 79, Skipped: 0, Total: 79`. 79 = 77 prior + the two new tests:
- `ResumeAsync_FingerprintMismatch_StartsFresh_NotStaleCursor` (line 747)
- `ResumeAsync_SchemaVersionBump_StartsFresh` (line 776)

### 3. docs/multi-step-flows.md:227 corrected — PASS
Lines 227-233 now state `CanResumeFrom` is a COARSE pre-check (SDK gives it only the `AdapterCheckpoint`, not the
event → cannot compute the fingerprint; checks `v==current` + non-empty stream), and that the AUTHORITATIVE gate is
`Restore`/`CanUseState` (has the event; checks v + fingerprint + step shape + step-index; any mismatch → start
fresh, never wrong data). The prior false "CanResumeFrom checks the fingerprint" claim is gone. Accurate.

### 4. No regression — PASS
Full suite green incl. the matching-fingerprint happy-path resume (`ResumeAsync_ContinuesFromCheckpoint_NoOverlap`,
yields 1 record). No source logic touched outside the new tests + one comment.

### 5. Task-dir note — PASS
`execution_notes.md` documents the structural coarseness ("CanResumeFrom has no dispatch event, so it cannot compute
the fingerprint — necessarily coarse"), the no-wrong-data argument, and both locked invariants
(fingerprint/inputs mismatch → fresh; schemaVersion bump → fresh).

## Behavior-change check (CheckpointManager) — CONFIRMED COMMENT-ONLY
`CollectorExecutorCheckpointManager.cs`: the only edit is the comment block at lines 114-117 above `CanResume`.
- `CanResume` (118-122): `state is { V: CurrentVersion } && !string.IsNullOrEmpty(state.Stream)` — purely
  structural, no fingerprint. Matches doc/comment.
- `CanUseState` (146-154): `V==CurrentVersion && state.Fingerprint == fingerprint && StepIndex in bounds &&
  Strategy == step strategy` — unchanged authoritative gate.
- `Restore` (17) calls `CanUseState(state, steps, fingerprint)` and starts fresh on false. Unchanged.
`CollectorExecutorAdapter.CanResumeFrom` (line 298) delegates to `CollectorExecutorRunner.CanResume`. Confirmed.

## Trivial-pass scrutiny — NOT TRIVIAL (genuine differential)

The decisive evidence is the contrast with the pre-existing happy-path test:
- `ResumeAsync_ContinuesFromCheckpoint_NoOverlap` uses checkpoint {cursor="2", NextPage=1} with a MATCHING
  fingerprint `Fingerprint("crowdstrike-falcon", 1, "findings", Inputs)` → asserts **1 record** (resume from
  cursor=2, only vuln-2 remains).
- Both new tests use the **identical checkpoint shape** {cursor="2", NextPage=1} and assert **3 records**. The ONLY
  difference is the fingerprint:
  - mismatch test: `Fingerprint(..., 1, "findings", staleInputs)` where `staleInputs` has `start_time=2020-01-01`
    vs the live event's `Inputs` `start_time=2026-06-01` → fingerprint differs.
  - bump test: `Fingerprint("crowdstrike-falcon", 2, ...)` while FalconYaml is `schema_version: 1` (verified at
    test line 1792) → the runner computes the fingerprint at schemaVersion 1 → differs.

Could 3 arise from a broken gate? No. If `CanUseState` wrongly accepted the mismatch, it would resume at
cursor=2/NextPage=1 → 1 record (proven by the happy-path test). 3 is the full fresh set
(= `ProcessAsync_Findings_..._Emits3Records`). So 3-vs-1 cleanly discriminates fresh from stale-cursor resume —
the test would fail if the gate were broken. Not a trivial pass.

Forcing-the-mismatch check: confirmed. `Fingerprint(vendor, schemaVersion, stream, inputs)` (signature at
Runner.cs:785 → CheckpointManager.cs:124) hashes a canonical JSON of all four. Both tests perturb exactly one input
that the live `Restore` recomputes from the dispatch event, so the seeded checkpoint fingerprint cannot match.

Pre-check-says-yes path: the mismatch test asserts `Assert.True(adapter.CanResumeFrom(checkpoint))` (line 765).
`V` defaults to `CurrentVersion`=2 and `Stream="findings"` is set, so `CanResume` genuinely returns true — the test
truly exercises "pre-check yes, Restore says fresh". The mismatch test additionally asserts a non-`after=2`
(from-start) page was fetched (line 772), independently corroborating fresh re-entry from step 0.

Minor note (non-blocking): the schemaVersion-bump test does not itself assert `CanResumeFrom` true. The mismatch
test does, and both share the same checkpoint shape, so the coarse-yes path is covered. The task only required the
assertion in the mismatch test. No action needed.

## Doc accuracy (coarse vs authoritative) — PASS
The doc, the CheckpointManager comment, and the actual code agree: coarse pre-check = structural (v + stream);
authoritative gate = v + fingerprint + step-shape + step-index, fresh-on-mismatch.

## VERDICT: PASS
All 5 success criteria met. Build clean, 79/79 (77+2), doc corrected, CheckpointManager change is comment-only with
gate logic unchanged, task-dir note complete. The two new tests are a genuine fingerprint-mismatch differential
(3 fresh vs 1 stale-cursor), not a trivial pass, and exercise the pre-check-yes/Restore-fresh path.
