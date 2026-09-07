# Verifier-3 — Repair Round 2 (final gate)

## VERDICT: PASS (whole task, post-round-2)

The three Majors are genuinely fixed, the superseding design change (claimed-first rebuild
removed) is coherent across code + decisions + docs, the only `Shared/` change is the single
accurate doc line, round-1 coverage is intact, build is green, and 41/41 tests pass. One minor
nit: a stale class-doc comment. Everything the team-lead asked to confirm checked out.

---

## Item-by-item confirm

1. **M1 — claimed rebuild removed, resume re-spools, test asserts it.** PASS.
   - `RebuildClaimedSetAsync`, `ExtractAssetUuidsAsync`, `EmptyClaimed`, and the `claimed`
     parameter are all gone (grep clean; `TenableIoAssetSpoolPhase.RunAsync` no longer takes a
     claimed set; `TenableIoVulnChunkBucketer` no longer has the uuid-only re-scan). No dead code.
   - Resume path re-spools the full inventory (TenableIoFindingsFlow.cs:136-146).
   - `decisions.md:11` marks D6 **SUPERSEDED** with the livelock rationale. Documentation
     (03-current-concerns.md:15, 02-decision-making.md:67) describes the removal, not a current
     claimed-first design.
   - Test `ResumeAsync_Findings_ReSpoolsFullInventory_...` (TenableIoCollectorTests.cs:640-714):
     chunk 1 already processed → its vuln chunk is never downloaded (returns NotFound if tried),
     a1 re-emerges via the **sweep as an empty-findings envelope** (findingsInChunk 0,
     isLastChunk true — :706-708), a2 hydrated from chunk 2 (:701-703), page numbering continues
     (findings_000006.json — :711). Real re-spool + duplicate-empty assertions.
   - **Nit (minor):** the class-doc summary at TenableIoFindingsFlow.cs:24 still reads "Resume
     rebuilds the claimed-asset set from processed vuln chunks first, then rebuilds the spool
     skipping them" — stale, contradicts the method body (:136-146) and the updated docs. Fix the
     one line. Non-blocking.

2. **M2 — buffer-then-publish, mark-after-publish, gate-on-marker; retry test forces re-stream
   and asserts exactly-once.** PASS.
   - Overflow candidates buffered per chunk in locals declared inside the try
     (TenableIoAssetSpoolPhase.cs:155-156), populated during streaming (:160-168), published only
     after the stream `await using` block closes (:172-177), markers added only **after** the
     publish returns (:178). Publish gated on marker absence (:164). A mid-chunk failure discards
     the locals; the retry rebuilds them.
   - Spool idempotency hardened: `InMemoryGzipAssetSpool.TryAdd` now short-circuits
     `if (_compressed.ContainsKey(uuid)) return true;` **before** the budget check (:33), so a
     re-streamed already-spooled asset cannot spuriously flip to overflow near the budget edge.
   - Test `ProcessAsync_Findings_Overflow_MidChunkStreamFailureThenRetry_PublishesEachOverflowOnce`
     (:717-787): assets chunk 1 throws `IOException` on attempt 1, succeeds on attempt 2
     (`assetsChunk1Attempts==2` asserted — real re-stream), budget=1 forces both assets to
     overflow, vulns empty. `SingleEnvelope("a1")`/`SingleEnvelope("a2")` assert **exactly one**
     envelope each despite the failed attempt.

3. **M3 — page upload precedes checkpoint persistence; final-page-only chunk inclusion; test.** PASS.
   - Read the publisher myself: `PublishOnePageAsync` awaits `PublishFindingsUtf8PageAsync` (the
     page upload, :74-81) and only after it returns runs `buildCheckpoint`→`SetState`→`AdvancePage`
     (:99-108). Upload-before-checkpoint confirmed in code (executor's claim holds).
   - Vuln phase includes the just-completed chunk only on the final page:
     `isFinalPage ? [.. processedSnapshot, chunkId] : processedSnapshot` (TenableIoVulnPhase.cs:155);
     `isFinalPage` is false on mid-loop flushes and true on the trailing flush
     (TenableIoCorrelatedPagePublisher.cs:46,59).
   - Test `PagePublisher_MultiPage_IncludesCurrentChunkOnlyInFinalPageCheckpoint` (:790-831):
     3 records / maxBytesPerPage 20 → 3 pages; asserts pages 1-2 are non-final and exclude the
     current chunk, page 3 is final and includes it.

4. **No regression to round-1 items.** PASS.
   `TenableIoVulnPhaseTests.cs` unchanged this round (mtime 19:30, round 1); still holds the
   2,000-cap `[Theory]` (`SlicesAt2000...`) and the ratio-gate `[Theory]` (`ExceedsSkippedRatio`).
   Both run within the passing suite.

5. **Only `Shared/` change is the one doc line, and it's accurate.** PASS.
   `git diff --stat -- 'Shared/*'` = 1 file, 2 insertions / 2 deletions:
   `Session/docs/Streaming-via-Http-Package-Session.md` renames the removed
   `TenableIoFindingsChunkProcessor.BuildChunkRecordsAsync` → `TenableIoVulnChunkBucketer.BuildBucketsAsync`
   and updates the description from "yields normalized records for publish" to "groups them by
   `asset.uuid` for correlated envelope emission." Matches the code.

6. **Build + vstest.** PASS.
   `dotnet build ...sln` exit 0. `dotnet vstest` on the built dll:
   `Passed! Failed:0, Passed:41, Skipped:0, Total:41, Duration: 3 s`.

---

## Success Criterion table (updated where round 2 changed something)

| # | Criterion | Status | Note |
|---|---|---|---|
| 1 | Builds | PASS | exit 0 |
| 2 | Tests green via vstest | PASS | 41/41 |
| 2e | claimed-first resume coverage | **RECHARACTERIZED** | claimed-first removed (M1); replaced by re-spool resume test (:640) — semantics changed by design, criterion still satisfied by the new test |
| 2b/2c/2d/2f/2g | cap / sweep / miss / checkpoint / combined-run | PASS | unchanged from verifier-1/2 |
| 3 | LocalAdapterRunner invocable | PASS | wiring unchanged (verifier-1 §3) |
| 4 | Docs synced, no stale two-lane | PASS | plus claimed-first now documented as superseded; **exception:** stale class-doc line at FindingsFlow.cs:24 (minor nit) |
| 5 | Checkpoint format v2 | PASS | unchanged |
| 6 | MAJOR version bump | PASS | 5.0.0 |
| 7 | No Shared/ code changes | PASS | one Shared **doc** line only (item 5); no Shared code |

---

## ACCEPTED RESIDUALS (merge record — these ship as-is by design)

1. **Duplicate re-emissions on resume.** Resume re-spools the full assets export and re-sweeps
   the full residue, so assets emitted before a crash re-emit — hydrated ones as empty-findings
   sweep envelopes, plus possibly one in-flight vuln chunk in the small window between page upload
   and checkpoint persistence (M3 shrank this from a guaranteed one-chunk lag to a narrow
   upload→checkpoint window), plus overflow hosts re-emitted if a 404 re-create/resume re-streams
   them. All bounded, none lose data.
2. **F3 — zero-vuln + overflow asset never emits `isLastChunk:true`.** Only a chunk-0 host with
   `isLastChunk:false`; not spooled → not swept, no findings → no continuation. Low probability
   (needs budget exhaustion), documented (03-current-concerns C2).
3. **Repurposed wire key.** Checkpoint key `totalUniqueVulnerabilities` carries the spool miss
   count; round-trips cleanly, diagnostic-only, misleading name.
4. **At-least-once counters.** On resume, restored counters plus duplicate re-emissions inflate
   `AssetsEmitted`/`FindingsEmitted` (code-review m2). Diagnostic only; not used for control flow.
5. **A9 parser dependency (load-bearing).** Every residual above relies on the later-phase
   correlated parser preserving **per-UUID replay-idempotent aggregation** (same property as the
   Falcon correlated parser). This MUST hold for the duplicates to reconcile. Out of this task's
   scope (parser-first rollout), but it is the single external invariant the whole resume design
   leans on — flag it prominently for the parser phase.

Also still standing from verifier-1: monotonic page numbering (sound, leans on A9), 1-byte spool
floor (safe degrade), and the out-of-scope carried concerns C3/C4/C5.
