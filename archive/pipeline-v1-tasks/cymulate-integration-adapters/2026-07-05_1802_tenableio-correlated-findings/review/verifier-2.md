# Verifier-2 — Repair Round 1 confirm

## VERDICT: PASS (repair round only)

Both flagged gaps are genuinely repaired, the gate extraction is behavior-preserving, and the
production surface touched this round is confined to one file. Build green, 40/40 tests pass
fast. One minor bookkeeping discrepancy (three visibility seams, not two) — benign, recorded
below.

---

## Item-by-item

1. **F1 repair — 2,000-cap slicing tests are real.** PASS.
   `TenableIoVulnPhaseTests.cs:41-85` (`[Theory]` 2000→1, 2001→2, 4001→3) asserts, per slice:
   `chunk == i` (:63), `findingsInChunk == 2000` for non-last / remainder for last AND
   `findings` array length matches (:65-67), host present iff `i==0` (:71), `isLastChunk` iff
   final slice (:73), `IsHostBearing` iff chunk 0 (:76), sum of findings == input (:79), and
   **exactly one** `isLastChunk:true` across all records (:80-84). Plus miss→thin-host-on-
   chunk-0 with slicing (:87-101) and overflow-marked→host-less-from-chunk-1 (:103-118). These
   are value assertions on parsed JSON, not shape-only.

2. **F2 repair — ExceedsSkippedRatio direct test is fast and covers the boundaries.** PASS.
   `TenableIoVulnPhaseTests.cs:122-133` `[Theory]`: at-ratio throws ×2 (2/1/1/1/0 → 0.5; and
   4/2/2/2/0 → 0.5) (:124-125), below-ratio ok (4/3/1/1/0 → 0.25) (:126), transport-only
   exclusion not permanent → ok (2/1/1/0/0) (:127), no exclusions → ok (:128). All five verified
   by hand against the formula. Pure static call, no flow/retry wrapper → full suite 224 ms
   (the 210 s hang that motivated removing the old test is gone).

3. **Gate extraction behavior-preserving.** PASS.
   `EnforceSkippedRatio` (TenableIoVulnPhase.cs:347-366) keeps the same early-return-on-zero-
   excluded, same `totalExpected`, same `transportFailedCount`, same warning log, and same throw
   message; it now delegates the decision to `ExceedsSkippedRatio` (:372-381). Arithmetic is
   identical: `permanentFailures = excludedCount - transportFailedCount = serverSideCount +
   dataFailedCount`, predicate `permanentFailures >= totalExpected * MaxSkippedChunkRatio`
   (unchanged 0.5). No semantic drift.

4. **Only-production-changes claim.** PASS with a note.
   mtime scan (`find ... -newermt "2026-07-05 19:00"`, non-test, non-obj) returns exactly one
   file: `Flows/Findings/Correlated/TenableIoVulnPhase.cs`. No other collector/production file
   changed. **Discrepancy:** the round opened **three** visibility seams, not the two claimed:
   - `MaxSkippedChunkRatio` `private const` → `internal const` (:28)
   - `EnforceSkippedRatio` → new `internal static ExceedsSkippedRatio` (:372)
   - `BuildRecordsForChunk` `private` → `internal` (:219) — required by the F1 tests, which call
     it directly.
   All three are visibility-only (`internal` + existing `InternalsVisibleTo`); no runtime
   behavior change. The undercount is cosmetic, but the third seam should be acknowledged in the
   record.

5. **Build + vstest re-run.** PASS.
   `dotnet build ...sln` exit 0. `dotnet vstest` on the built dll:
   `Passed! Failed:0, Passed:40, Skipped:0, Total:40, Duration: 224 ms` (30 prior + 10 new: F1
   3+1+1, F2 5).

---

## Accepted residuals (carried from verifier-1, still open by design)

- **F3 — zero-vuln overflow asset never emits `isLastChunk:true`.** An asset that both overflows
  the spool and has no findings gets only a chunk-0 host record with `isLastChunk:false`; not
  spooled → not swept, no findings → no continuation. Documented (03-current-concerns C2),
  low probability (requires spool budget exhaustion), absorbed by the later per-UUID parser.
  Not addressed this round; accepted.
- **Repurposed wire key** — checkpoint key `totalUniqueVulnerabilities` now carries the spool
  miss count. Round-trips cleanly; diagnostic-only; misleading name. Accepted.
- **Monotonic page numbering / A9 dependency** — resume continues page numbers instead of the
  old overwrite; reprocessed/overflow/sweep duplicates land in new files and rely on the
  later-phase parser's per-UUID replay-idempotent aggregation (A9). Sound, documented, out of
  this task's scope. Accepted.

Also still standing (not repair-round scope): F5/F6 low items and the out-of-scope carried
concerns C3/C4/C5 from verifier-1.
