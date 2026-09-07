# Verifier Report — Falcon Correlated Findings Redesign

Verifier: verifier-1. Date: 2026-07-03.
Method: read all task-directory files; inspected the actual working-tree code in both repos; re-ran the adapters solution build, the Falcon test project, and the parser test file myself; independently re-counted the real E2E collector output.

## Overall verdict: PASS (with minor gaps and one documented contract-text drift; nothing blocking)

---

## Success Criteria

### SC1 — Adapters solution builds; Falcon tests green with new traversal/chunking/checkpoint/re-anchor coverage — PASS

- `dotnet build Cymulate.Integration.Adapters.sln`: re-run by verifier, **0 warnings / 0 errors**.
- Falcon test project: re-run by verifier, **99 passed / 0 failed / 0 skipped** (95 collector + 4 W4 hash tests; matches execution notes).
- New coverage verified by reading `FalconCorrelatedFindingsTests.cs` (9 tests, end-to-end through `FalconCollector.ProcessAsync` with mocked HTTP, real assertions on published NDJSON):
  traversal via after-cursor across pages; group-by-aid + orphan skip; chunk flush at cap with correct `chunk`/`isLastChunk`/`findingsInChunk`; zero-finding host emits one empty chunk-0 record; strip apps/suppression_info/host_info + sorted `remediation.entities`; checkpoint v2 round-trip + pre-correlated rejection; **Discover re-anchor with boundary-host non-re-emission asserted**; **Spotlight mid-batch re-anchor with seen-id dedupe asserted**; dry-run publishes nothing.
- `FalconResumeRunnerTests.cs` additionally covers: valid-checkpoint resume, `CanResumeFrom` rejection of absent/wrong `findingsFormatVersion`, v2 save/load round-trip, stale-checkpoint rejection.
- Lane/segment/staged/month-segment/cursor-sanitizer/depth-cap test files retired with their machinery, as promised.

### SC2 — Parser tests green for BOTH shapes — PASS

- Re-run by verifier: `pytest tests/test_crowdstrike_assets_findings.py` → **20 passed** (10 pre-existing split + 10 new correlated: detection/routing, single-chunk, zero-finding host, multi-chunk single-asset attribution, multi-host, absent-assets-lane tolerance, status mappings).
- `CrowdstrikeAssetsFindingsCorrelated.py` (new) + façade routing inspected: detection keys off `{"isLastChunk","host"}` in the findings lane's first shard (legacy rows carry `host_info`, no chunk framing — no false positive path); asset spine = chunk==0 `host` blocks; findings exploded across all chunks; reuses the split handler's `CorrelationSpec` so `process()`/`post_process()` are untouched. Split path byte-for-byte unchanged per diff.

### SC3 — End-to-end: LocalAdapterRunner run + fixed parser ingestion with parity — PASS (with caveats)

- Real correlated output exists and was independently re-counted by the verifier
  (`/private/tmp/.../scratchpad/e2e-correlated/collector-run/`): 2 `findings_*.json` pages, ~158 MB,
  **353 records, 335 chunk-0 assets, 59,086 findings, keys exactly `{aid, chunk, findings, findingsInChunk, host, isLastChunk}`** — matches execution_notes exactly. No assets lane.
- Spot-verified on real records (31,357 findings sampled): `apps`/`suppression_info`/`host_info` absent from every finding; all 14,615 multi-entity `remediation.entities` arrays in canonical sorted order.
- `tests/test_e2e_correlated_real_output.py` exists, is env-gated (`E2E_CORRELATED_DIR`), and encodes honest expectations: ground truth computed from raw records; the no-cve-id exclusion is explicitly attributed to the pre-existing `BaseParser` platform rule (stated as verified identical on the legacy path); asserts asset/finding counts, no orphan `asset_id`s, and field materialization.
- Parity-claim arithmetic is internally consistent (baseline 334 hosts + 1 new = 335; 59,040 + 46 overnight = 59,086; 58,990 = 59,040 − 50 no-cve).
- **Caveats (accepted, not failures):** parity was against an 18h-older production baseline (all diffs attributed to scan drift / vendor remediation refresh / cve.references enrichment per notes) rather than a same-time legacy run — the attribution itself is not re-verifiable from artifacts here. The temporary E2E test asserts ingestion correctness, not cross-shape parity; parity was the orchestrator's offline methodology.

### SC4 — Egress publish-completion hash, all collectors, no measurable slowdown — PASS

- Diff read in full: `NdjsonContentHasher` (IncrementalHash SHA-256), fed in `FlushAsync` of BOTH `NdjsonBatchSession` and `NdjsonUtf8BatchSession` right before upload (each part in append order → logical-object hash identical across single-upload/multipart); zero-copy via `TryGetBuffer`; disposed with the session. Both completion log lines in `ResultsBatchPublisher` carry `Hash=`. All collectors publish through these sessions → criterion's "all collectors" holds structurally.
- Zero-record publishes return early before the completion line (notes' claim verified in code).
- Tests verified present and passing: deterministic known-content hash, string/utf8 parity, different-content inequality, and **single-upload (50 MiB cap) vs forced multipart (6 MiB cap) identical hash**.
- "No measurable slowdown" is by-design (incremental hash over bytes already buffered in memory), not benchmarked. Acceptable: SHA-256 over the upload buffer is orders below upload I/O cost.
- W4 independence honored: change confined to `Shared/.../DataPipeline/Egress/**` + its two test files + READMEs.

### SC5 — Docs/skills updated — PASS

- `FalconDocs/CollectorDocs/01-collection-strategy.md`: correlated model described as current; month segmentation explicitly scoped to the (unchanged) assets flow; "no status lanes, no month segments, no AID pre-pass" for findings. 02–05 + README also touched; grep found no lane/segment content presented as the current findings model.
- `Collectors/README.md`: Falcon notes rewritten (format-v2 checkpoint, no persisted cursor, no cross-version resume; cursor expiry handled in-process by the flow, not the strategy).
- `ai/skills/collector-flow-patterns/SKILL.md` + `collector-recovery/SKILL.md`: describe the correlated flow, watermark-checkpoint pattern, and host-filter-not-AID-pre-pass guidance. No stale Falcon lane/segment references remain (grep-verified).

### SC6 — Major collector version bump + checkpoint version bump, no cross-version resume — PASS

- `FalconCollector.csproj`: `<CollectorVersion>5.0.0</CollectorVersion>` (from 4.5.3 — major).
- `FalconFindingsCheckpointState.CurrentFormatVersion = 2`; `FalconCheckpointDeserializer` rejects absent/mismatched `findingsFormatVersion` (so `CanResumeFrom` → false → fresh restart); serializer writes it; enforcement covered by two tests (`FindingsCheckpoint_V2_RoundTrips_AndPreCorrelatedFormatIsRejected`, `CanResumeFrom_FindingsCheckpoint_WithoutOrWrongFormatVersion_RejectsPreCorrelatedFormat`).

---

## Constraint findings

- **Never persist cursors — PASS.** `SaveFindingsState` writes no `AfterToken`; state carries watermark + boundary AIDs + batch size only. `FalconCursorSanitizer` + `ApplyCursorTtlForResume` deleted.
- **No transport-retry / token-refresh duplication — PASS.** The flow catches only `FalconCursorExpiredException` (vendor-semantic 404); transport stays in DefensiveToolkit, auth in the authenticator. Falcon keeps `CreateTransient()`.
- **Publish path — PASS.** `CollectorNdjsonPublisher.PublishFindingsUtf8PageAsync`, `findings_*.json` only; verified in code and in the real output directory.
- **Memory bounds — PASS with observation.** No whole-host buffering (chunk cap flushes at 2000) and no whole-batch buffering (records stream to egress). However, theoretical worst-case pending state is (cap−1)×AidBatchSize ≈ 500k shaped findings across accumulators, plus a per-batch `seenFindingIds` set proportional to the batch's total findings — both exceed "one Spotlight page" in a pathological dense tenant. This exactly mirrors the prototype (verified against `FalconCorrelatedFindingsProbe.cs` accumulators), which the same contract pins as the behavioral spec; the letter of the constraint ("never buffer a whole aid batch or a whole host's findings") is honored.
- **Contract-text drift (documented, resolved correctly):** prompt_contract/task.md say cursor-expiry re-anchor goes "through the existing Resilience layer"; the implementation re-anchors **in-process** in the flow, with the Resilience strategy retained for transport/401/5xx continuation. This mirrors the prototype (also contract-pinned) and is the right call given assumptions.md's finding that re-anchor is a hot path (a scheduler hop per 120s expiry would be pathological). Deviation is stated in execution_notes, Collectors/README, and the recovery skill. Not a defect; flagged for the record.
- **decisions.md adherence — PASS.** Record keys exact (verified on real output); `ChunkFindingsCap` default 2000 and `AidBatchSize` default 250 in `FalconCollectorConfiguration`; strip + sorted entities verified on real data; findings-only lane verified; boundary-aid dedupe is a stated, justified deviation from the prototype's unbounded `_processedAids` (memory-bounded, contract-conformant).
- **No "Legacy" identifiers — PASS** (grep clean in both repos' new/changed code).

## Gaps (minor, non-blocking)

1. **No test for the user-FQL filter path in the correlated flow.** `FalconHostFilters.BuildLastSeenTimestampGateFilter` has no direct unit test anywhere in UnitTests, and no correlated-flow test passes a user filter. The merge logic is trivial and shared with the assets flow, but the criterion-relevant behavior (user filter + watermark floor composition on re-anchor) is untested.
2. **No explicit empty-tenant (zero Discover hosts) test** for the correlated flow; the code path (first page empty → break, publish nothing) is simple but unasserted.
3. **Mid-batch resume re-emission** (Discover re-anchor can re-emit the in-progress aid batch) is accepted and documented, relying on downstream (aid, chunk) idempotency — consistent with prior-model semantics; no test pins it.
4. **Recovery-simulation harness retired without a correlated replacement** — acknowledged open risk in execution_notes; unit re-anchor tests + W6 stand in.
5. **state.json bookkeeping stale:** top-level `status` still `contract_ready`, `verification.status` `not_started`, worker A/B `complete` vs C `done`. Cosmetic.
6. Execution-note count discrepancy explained: Worker C's "171/171" was the pre-rewrite suite in its worktree; current merged suite is 99/99 (95 + 4 hash). Not an issue, recorded to avoid confusion.

## Original user request coverage (4 items)

1. **Match the prototype** — done; traversal/chunking/re-anchor mirror the probe; two deliberate, documented deviations (bounded boundary dedupe; per-batch watermark without page offset), both defensible.
2. **Clean defenses + fix checkpoint** — done; planned yields, retry policy, cursor sanitizer, lanes/segments/pre-pass, recovery simulation all removed; checkpoint reshaped to watermark-only v2 with cross-version rejection.
3. **Fix parser** — done; dual-shape auto-detect, split path unchanged, both shapes green.
4. **Egress hash** — done; streaming, logical-object-stable across upload modes, logged, tested.

## Certainty

High on everything re-run or read directly (builds, both test suites, code inspection, real-output recount, version/checkpoint enforcement). Medium on the W6 parity attribution (18h-gap baseline diffs attributed by the orchestrator; internally consistent but not independently re-derivable from surviving artifacts).
