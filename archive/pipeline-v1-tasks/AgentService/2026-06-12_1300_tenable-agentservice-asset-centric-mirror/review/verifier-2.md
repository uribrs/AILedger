# Verifier-2 — post-repair confirmation

Scope: confirm the code-reviewer-1 repairs (B1/M1/M2/M3 + cleanups) are correctly applied, nothing regressed, build green. Verified from the actual code (`git diff` + targeted reads).

## Repairs
- **B1 — terminal export status — FIXED.** `waitForExportCompletionAsync`: `FINISHED` now case-insensitive (:643); `FAILED`/`ERROR`/`CANCELLED` (case-insensitive) throw immediately (:660–664) instead of polling to the 180-min timeout. Matches the reference adapter's terminal set.
- **M1 — upload failure no longer swallowed — FIXED.** `uploadBatchAsync` (:329–363) throws on exception (:350–353) AND on uploader `false` (:356–360). Findings lane aborts the run on a dropped batch; assets lane stays best-effort via `CollectFindingsAsync`'s try/catch (intended asymmetry).
- **M2 — counter after successful upload — FIXED.** `UploadFindingsBatchAsync`/`UploadAssetsBatchAsync` (:313–327) compute `batchNumber = counter+1` for the filename, then `Interlocked.Increment` only after `uploadBatchAsync` returns (it throws on failure). No `_NNN` gap, no overcount. Safe because uploads are awaited serially per lane (noted in code; would need CAS if uploads are ever parallelized).
- **M3 — fail-hard made deliberate + documented — DONE.** Comment at the findings chunk site states the deliberate fail-loud choice and that transient failures are absorbed by base-class retry/circuit-breaker before a hard fail; no resume/ratio-gate added (out of scope).
- **Cleanups — DONE.** Dead `using System.Threading.Channels;`, unused `rProcessedAssets`, and the dead double-parse guard removed.

## Build
`dotnet build ... -c Debug` → Build succeeded, 0 errors, 2 pre-existing NU1903 transitive advisories (Microsoft.Kiota.Abstractions), unrelated.

## Success Criteria
All 8 from verifier-1 still hold (the repairs harden write-path/terminal-state robustness; they do not alter the assets-lane / no-info-filter / no-enrichment / verbatim-ACR / co-location / no-resume behaviors). 

## Overall verdict
**SATISFIED-WITH-RISKS.** Implementation conforms to the adapter spec and is now robust against silent write-path / terminal-state data loss. Remaining risks are cross-repo / host (unchanged):
1. The consuming Tenable parser must be the DUAL_MODE split parser (left-join + risk_score ← ratings.acr.score). Verify before STG ship.
2. Backend `cybi/{instanceId}` re-run folder semantics bound idempotency (host behavior).
3. All-severity (~3×) volume on a blocking 180-min poll — unvalidated at platform scale (mitigated by per-chunk streaming + flush-at-1000).
4. No local run possible — STG validation by the user is the final gate.
