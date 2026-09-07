# Verifier-1 — TenableIo Correlated Findings

## VERDICT: PASS-WITH-GAPS

The rewrite is functionally complete and correct on inspection: build green (0/0), 30/30
tests pass, the correlated record grammar, spool/overflow/miss/sweep mechanics, resume
claimed-first rebuild, checkpoint v2 format guard, export request shapes, carried-over
machinery, version bump, docs sync, and runner wiring all match the contract. The gaps are
in **test coverage of an explicitly-required behavior (the 2,000-cap slicing) and of the
carried-over ratio gate**, plus one accepted output-contract asymmetry (zero-vuln overflow
asset). No correctness defects found in the shipped code. No `Shared/` changes.

Findings: 1 HIGH, 2 MEDIUM, 4 LOW/INFO.

---

## Success Criterion table

| # | Criterion (prompt_contract.md) | Status | Evidence |
|---|---|---|---|
| 1 | Solution builds, TargetFramework pinned | PASS | `dotnet build ...sln` → `Build succeeded. 0 Warning(s) 0 Error(s)`; csproj `<TargetFramework>net8.0</TargetFramework>` |
| 2 | TenableIo tests green via `dotnet vstest` | PASS | `Passed! Failed:0, Passed:30, Skipped:0` on built test dll |
| 2a | coverage: spool hit/miss/budget-overflow | PASS | TenableIoCorrelatedUnitTests.cs:16,30,37 |
| 2b | coverage: bucketing + **2,000-cap flush** + isLastChunk | **PARTIAL** | bucketing @ TenableIoCorrelatedUnitTests.cs:128; isLastChunk @ TenableIoCollectorTests.cs:374,443. **2,000-cap slicing path (sliceCount>1) has NO test** — see F1 |
| 2c | coverage: zero-vuln sweep | PASS | TenableIoCollectorTests.cs:380-383 |
| 2d | coverage: thin-envelope miss lane | PASS | TenableIoCollectorTests.cs:376-378 |
| 2e | coverage: claimed-first resume rebuild | PASS | TenableIoCollectorTests.cs:638-707 |
| 2f | coverage: checkpoint round-trip @ new version | PASS | TenableIoCheckpointV2Tests.cs:26,41,51 |
| 2g | coverage: phase-transition + combined-run resume | PASS | combined-run refused @ TenableIoCheckpointV2Tests.cs:60; standalone still resumes @ :82; old two-lane findings ckpt refused via format guard @ :41,51 |
| 3 | LocalAdapterRunner runs CollectFindings end-to-end | PASS | CollectorRegistry.cs:115-120 constructs `TenableIoCollector`, alias `tenableio` + `CollectorNames.TenableIoCollector`; findings dispatch @ Program.cs:283; signatures unchanged |
| 4 | Docs synced, no stale two-lane descriptions | PASS | Documentation/01,02,03,04 describe correlated model; every "two-lane" mention is historical/"replaced" context; Collectors/README.md does not describe output shape (no update needed) |
| 5 | Checkpoint format version bumped | PASS | TenableIoCheckpointKeys.cs:28 `CurrentFindingsFormatVersion = "2"` |
| 6 | MAJOR CollectorVersion bump | PASS | csproj `<CollectorVersion>5.0.0</CollectorVersion>` (from default 4.5.3) |
| 7 | No modifications under `Shared/` | PASS | `git status` lists only Collectors/TenableIoCollector, its tests, docs, ai/skills |

## Record grammar & export requests (team-lead §2/§3)

- Grammar `{uuid, chunk, isLastChunk, findingsInChunk, host, findings[]}` — exact, fixed field
  order, host omitted (not null) when host-less. TenableIoCorrelatedRecordWriter.cs:36-56.
- host = full assets-export record verbatim on chunk 0 (TenableIoVulnPhase.cs:229-234,271-272);
  zero-vuln envelope shape chunk 0 / isLastChunk true / findingsInChunk 0 / findings []
  (TenableIoFindingsFlow.cs:174-176).
- Assets export: `chunk_size` (default 1000) + `filters.last_assessed=baseDate`
  (TenableIoAssetsExportClient.cs:41-45). Vulns export: `since=baseDate` +
  `state=[OPEN,REOPENED]` + `num_assets` default 50, bounds 50–5000
  (TenableIoVulnsExportClient.cs:32-42; Configuration.cs:36, Builder.cs:72). Both created up
  front (TenableIoFindingsFlow.cs:79-80). 409 active_job_id reuse retained (both clients).
  Progressive download + per-chunk retry/exclusion + MaxSkippedChunkRatio + byte-budget
  re-pagination all retained (TenableIoVulnPhase.cs:120-127,145-212,347-366;
  TenableIoCorrelatedPagePublisher.cs:38-59).

## Spool (team-lead §4)

- Compressed bytes, not DOM; gzip Fastest; remove-on-use (InMemoryGzipAssetSpool.cs:27-57).
- Hard budget enforced BEFORE exceeding: `if (_compressedBytes + compressed.Length > _budgetBytes) return false;`
  (line 32) — allows reaching exactly the budget, rejects the record that would exceed it. Correct boundary.
- Overflow degrade = immediate chunk-0 host publish + marker set + host-less findings
  (TenableIoAssetSpoolPhase.cs:157-163; TenableIoVulnPhase.cs:235-240). Miss lane = thin host
  from embedded `asset`, counted (AddMiss) + surfaced in final log, never buffered
  (TenableIoVulnPhase.cs:242-246; PagePublisher.cs:84-87; FindingsFlow.cs:106-108).

## Resume (team-lead §5)

- Claimed set rebuilt FIRST via uuid-only re-scan (TenableIoFindingsFlow.cs:139-141 →
  TenableIoVulnPhase.RebuildClaimedSetAsync:36-58), THEN spool rebuild skipping claimed
  (AssetSpoolPhase.cs:154). Both export UUIDs in checkpoint (CheckpointKeys.cs:32;
  Helper.cs:100-101). v2 guard rejects old/mismatched findings checkpoints (Helper.cs:134-141)
  and obsolete combined-run assets checkpoints (Helper.cs:56-61). Crash-during-spool → no v2
  checkpoint → fresh restart (D7, no checkpoint written in spool phase). Assets export
  re-created on 404 mid-spool (AssetSpoolPhase.cs:65-76).

---

## Findings (by severity)

### F1 — HIGH — 2,000-cap slicing is a required-coverage item with no test
- **What:** Success Criterion 2 explicitly requires coverage of "2,000-cap flush"; the
  orchestration plan and team-lead §2 require boundary cases (2000 exactly, 2001, 0). No test
  in the suite produces >2,000 findings for one asset, so the multi-slice path
  (`sliceCount>1`) in `AppendSlicedRecords` is never executed. Largest per-asset findings in
  any test is 2.
- **Where:** logic at TenableIoVulnPhase.cs:254-284 (`AppendSlicedRecords`); absent from
  TenableIoCorrelatedUnitTests.cs / TenableIoCollectorTests.cs.
- **Why it matters:** This is the single novel numeric-boundary behavior of the "2,000-cap"
  feature and it is entirely unexercised. A future off-by-one in `sliceCount`,
  `GetRange(offset,length)`, or the `isLast`/`chunk` numbering would pass CI silently. The
  criterion as written is not satisfied.
- **On inspection the logic is correct:** `sliceCount=(total+1999)/2000`; 2000→1 slice,
  2001→2 slices (2000 + 1), `isLast=i==sliceCount-1`, `chunk=startChunk+i`, host only on
  i==0. The 0-findings case cannot reach this method (only UUIDs with ≥1 finding are bucketed;
  zero comes from sweep/overflow paths, which are covered).
- **Suggested repair:** Add a unit test over a synthetic vuln chunk with 2000, 2001, and 4001
  findings for one asset; assert slice count, per-slice `findingsInChunk`, `chunk` sequence,
  single `isLastChunk:true`, and host present only on chunk 0.

### F2 — MEDIUM — MaxSkippedChunkRatio gate lost its test (deviation c)
- **What:** The executor removed `ResumeAsync_Findings_TooManyChunksFail` (was ~3m30s due to
  the Shared UnknownFlowRetryPolicy wrapping the throw). No replacement covers
  `EnforceSkippedRatio`. Grep confirms no test references the gate.
- **Where:** TenableIoVulnPhase.cs:347-366; no coverage in the test project.
- **Why it matters:** constraints.md keeps `MaxSkippedChunkRatio` as required carried-over
  machinery. The gate is intact by inspection (permanent-failure ratio ≥0.5 throws, transport
  failures excluded), but it is now a regression-untested guard on data-loss protection.
- **Deviation assessment:** removing the *slow* test was reasonable (the 210s came from a
  Shared retry pipeline, not the gate). But the fix is to test `EnforceSkippedRatio` directly
  (a pure method — no flow/retry wrapper, so it is fast), not to drop coverage. The executor's
  claim that "fast failure coverage exists via invalid-config/unsupported-topic/invalid-checkpoint"
  is inaccurate: those exercise unrelated paths.
- **Suggested repair:** Unit-test the ratio path by driving `TenableIoVulnPhase` (or extract
  `EnforceSkippedRatio` as internal) with a status feed that fails >50% of chunks; assert the
  `InvalidOperationException`.

### F3 — MEDIUM — zero-vuln overflow asset never emits isLastChunk:true (deviation d)
- **What:** An asset that overflows the spool AND has no findings in the vuln export receives
  exactly one record: chunk 0, host present, `isLastChunk:false`, empty findings
  (AssetSpoolPhase.cs:191-197). It is not in the spool, so the sweep never emits it; it has no
  findings, so the vuln phase never emits a host-less continuation. No `isLastChunk:true` is
  ever produced for it.
- **Where:** TenableIoAssetSpoolPhase.cs:191-197 (overflow record `isLastChunk:false`) vs.
  sweep at TenableIoFindingsFlow.cs:171-185 (drains spool only).
- **Why it matters:** Output-contract asymmetry: every other asset terminates with
  `isLastChunk:true`. Any consumer/parser that treats `isLastChunk:true` as the finalization
  signal will see this asset as perpetually open. Documented as accepted (03-current-concerns
  C2) and absorbed by the future per-UUID parser (A9), and probability is low (requires the
  compressed spool budget — default 1 GiB — to be exhausted). Real but bounded.
- **Suggested repair:** either mark the overflow chunk-0 record `isLastChunk:true` when the
  asset yields no findings (not known at spool time — would require a post-vuln reconciliation
  pass), or explicitly document the "chunk-0 host record is self-terminating" rule as a parser
  contract invariant so the comparison/parser phase handles it deliberately.

### F4 — LOW — `totalUniqueVulnerabilities` wire key repurposed as miss count (deviation b)
- **What:** The persisted checkpoint key `totalUniqueVulnerabilities` now stores `MissCount`
  (FindingsFlow.cs:42-43; CorrelatedCheckpoint.cs:28; Helper.cs:96). Round-trips cleanly
  (restored into `MissCount` via RestoreResumeState, StatsRestore at FindingsFlow.cs:132).
- **Why it matters:** No functional risk (diagnostic-only, not used for control flow), but the
  key name is misleading for anyone reading a raw checkpoint. Acceptable; flag for awareness.
- **Suggested repair (optional):** rename the wire key to `spoolMissCount` in a follow-up
  (would itself be a checkpoint-shape change, so defer to a future version bump), or leave a
  comment at the read/write sites (already partially present).

### F5 — LOW — monotonic page numbering relies on parser per-UUID idempotence (deviation a)
- **What:** Resume continues page numbers after `LastPublishedPage` instead of the old
  chunk-start-base overwrite. Reprocessed in-flight chunk / overflow / sweep records land in
  new files; pre-crash pages become orphaned duplicates.
- **Assessment:** Sound and correctly documented (03 C1). Nothing downstream in the *collector*
  depends on page-number overwrite (files are additive NDJSON pages). The dependency moves to
  the parser's per-UUID replay-idempotent aggregation (A9), which is an out-of-scope later
  phase per the pinned rollout order. No action for this task.

### F6 — LOW — spool budget floor of 1 byte (deviation e)
- **What:** Builder min for `spoolBudgetBytes` is 1 (Builder.cs:75). At 1 byte every asset
  overflows.
- **Assessment:** Safe degrade (chunk-0 host + host-less findings, no crash/data loss),
  intentional so tests and operators can force the overflow path. Default remains 1 GiB. Sane.

---

## Deviations summary
a (monotonic paging) — sound, documented (F5). b (repurposed wire key) — harmless, mildly
confusing (F4). c (removed slow ratio test) — removal reasonable, but coverage genuinely lost;
should be re-added fast (F2). d (zero-vuln overflow no isLastChunk:true) — real contract
asymmetry, accepted/documented, low probability (F3). e (1-byte spool min) — sane (F6).

## Out of scope (per contract / not defects)
- Parser dual-shape support and per-UUID aggregation (A9) — later phase, adapters-repo-only
  scope.
- Carried-over concerns C3 (FINISHED-with-pending), C4 (circuit-breaker poll counter), C5
  (no-op initial-run classifier) — pre-existing, not introduced here.
- Standalone CollectAssets flow — unchanged (not in diff; TenableIoAssetsFlow.cs untouched);
  ProcessAsync_Assets test still green.
- S6 large-tenant comparison run — explicitly the user's step.

## Determinism note (team-lead §9)
Output is deterministic for a given input (fixed envelope field order; verbatim
normalized host/findings; gzip is deterministic). Envelope ordering within a page follows
dict insertion / spool-drain order, which is not a stable sort but is irrelevant to the
"compare against current" step since reconciliation is per-UUID and order-independent. No new
nondeterminism vs. the old lane.
