# Code review — g2 resume fingerprint

Scope: comment on `CanResume`, reworded doc bullet, two new resume tests. REVIEW ONLY.

## Verdict: CLEAN. No blockers, no majors. One minor, one nit.

---

## 1. CheckpointManager change is genuinely comment-only — confirmed

`CollectorExecutorCheckpointManager.cs:114-117` is a new 4-line comment above `CanResume`. The
method bodies are unchanged behaviorally:

- `CanResume` (`:118-122`) — still `state is { V: CurrentVersion } && !string.IsNullOrEmpty(state.Stream)`.
  Coarse, checkpoint-only, no fingerprint. Matches the comment.
- `CanUseState` (`:146-154`) — still the authoritative gate: `V==CurrentVersion` + `Fingerprint ==
  fingerprint` + step-index bounds + strategy match. Unchanged.
- `Restore` (`:17-73`) — unchanged; on `!CanUseState` returns the fresh seed `new ResumeSeed(0,0,default,null)` (`:42`).

The comment is accurate: it correctly states the SDK hands `CanResumeFrom` only the checkpoint (no
event), so the fingerprint can't be computed there, and that `Restore`/`CanUseState` is the
authoritative gate. No behavioral drift.

## 2. Doc bullet (multi-step-flows.md:227-233) — accurate

The reworded bullet correctly distinguishes the coarse pre-check (`v==current` + non-empty stream)
from the authoritative gate (`v` + fingerprint + step-shape/strategy + step-index bounds). It
correctly enumerates the mismatch triggers (changed inputs, bumped `schemaVersion`, edited step
shape) and that all start fresh at `Restore`, not at the pre-check. Consistent with the code at
`:118-122` and `:146-154`.

## 3. Both tests construct GENUINE mismatches and exercise the start-fresh path — confirmed

Dispatch-side fingerprint is computed in `CollectorExecutorRunPreparer.cs:74` as
`Fingerprint(loadedProfile.Vendor, loadedProfile.SchemaVersion, flowName, inputs.Inputs)`. For the
`Event("findings")` dispatch that is `("crowdstrike-falcon", 1, "findings", Inputs)` — identical to
the matching-resume fingerprint in the baseline test (`:724`), which is the correct control.

- **FingerprintMismatch** (`:747-773`): checkpoint fingerprint built from `staleInputs` (different
  `start_time`, `:756-757`) while everything else matches the resumable shape (cursor=2, v=current,
  strategy=cursor). Genuine input-driven divergence — only the fingerprint differs, so the gate
  rejects on fingerprint and nothing else. It also asserts `CanResumeFrom(checkpoint) == true`
  (`:765`), proving the pre-check-yes / Restore-fresh divergence is exactly what's exercised — not a
  trivial "no resume". Good.
- **SchemaVersionBump** (`:776-797`): checkpoint fingerprint built with `schemaVersion: 2` while the
  FalconYaml profile is `schema_version: 1` (`:3045`). Since schemaVersion participates in the hash,
  this is a genuine mismatch. Verified the profile is schema_version 1 and vendor crowdstrike-falcon.

I ran all three (incl. the baseline control): **Passed 3, Failed 0.** They pass for the right reason.

## 4. "3 records" as a proxy for "started fresh" — sound, lightly brittle

The proxy is anchored by two existing passing tests, so it is not an arbitrary constant:
`ProcessAsync_Findings...Emits3Records` (`:706`) establishes fresh = 3, and
`ResumeAsync_ContinuesFromCheckpoint_NoOverlap` (`:741`) establishes a cursor=2 resume = 1. 3 vs 1
is a real discriminator here. Acceptable.

### MINOR — schema-bump test lacks the `CanResumeFrom == true` assertion the sibling has
`ResumeAsync_SchemaVersionBump_StartsFresh` (`:776-797`) omits the `Assert.True(adapter.CanResumeFrom(checkpoint))`
that the fingerprint test carries at `:765`. The checkpoint there is identically resumable-shaped
(v=current, non-empty stream), so the pre-check *would* return true — but without the assertion this
test doesn't independently prove the pre-check-yes / Restore-fresh divergence; on its own "3 records"
could in principle mean "pre-check said no". Add the one line for parity and to make the test
self-evidently about the gate rather than the pre-check:
```csharp
Assert.True(adapter.CanResumeFrom(checkpoint));
```
(Fix: add at `:792`, before `ResumeAsync`.)

### NIT — fresh-page assertion is a URL-substring negative-match
`:772` asserts a from-start page was fetched via `u.Contains("vulnerabilities/v1") && !u.Contains("after=2")`.
This is fine and mirrors the baseline test's idiom (`:743`), but it couples the test to the Falcon
mock's URL shape (`after=` query param). If the mock's pagination param ever changes this passes/fails
for an incidental reason. The record-count assertion (3) already carries the load; the URL assertion
is belt-and-suspenders. No change required — noting for awareness.

## Build/test
`dotnet test --filter` on the three resume tests: Passed 3 / Failed 0. (Only NU1507 package-source
warnings, pre-existing and unrelated.)
