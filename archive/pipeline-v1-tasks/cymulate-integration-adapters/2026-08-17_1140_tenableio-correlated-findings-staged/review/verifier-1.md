# Verifier pass 1 — slice 1 (asset spine staging, phase 1 only)

Verifier: independent verifier subagent, 2026-08-17.
Branch `feat/tenableio-correlated-findings-staged`, baseRef `1f7a2ba6`.
Scope verified: phase 1 only, per the operator's explicit narrowing ("bring over the concept, using
falcon's S3-cloud-disk technique for the hosts (assets) and then reevaluate"). Criteria that pertain to
phases 2–3 are marked OUT-OF-SLICE, not failed.

---

## 1. Verdict

**Accept the slice, with one code defect that must be fixed before this phase runs against a real
tenant.**

The staged spine is real, correctly shaped, and honestly reported. Every structural claim in
`execution_notes.md` that I could check, I checked, and it held: purely additive diff, no
`PriorStateStore`, no typed-DTO round trip, no budget/overflow/degrade machinery anywhere in the tree,
deletion off the correctness path, write concurrency bounded by the collector.

The defect is in the one place phase 1 owns end to end — chunk download retry. **A `429` / `502` / `503`
/ `504` on an assets-export chunk is not retried and the chunk is permanently skipped on the first
occurrence**, silently dropping ~1,000 assets from the spine per affected chunk. The `MaxSkippedChunkRatio`
guard the constraints say to keep is also absent, so an arbitrarily incomplete spine still gets a manifest
written over it and is then treated as a completed spool. Details in §5, D1 and D2.

Two reporting gaps (not code): the solution build is red at baseRef and the notes do not say so, and the
docs obligation (`Documentation/01-05`, `ai/skills/*`) is untouched with the A1a documentation gap the
contract asked to close in this rebuild still open.

---

## 2. What actually landed

`git diff 1f7a2ba6 --stat` is **empty**. `git rev-parse HEAD` = `1f7a2ba6…` — nothing is committed.
Everything is untracked:

```
?? src/Cymulate.Integration.Adapters/Collectors/TenableIoCollector/Flows/Findings/Correlated/
?? src/…/TenableIoCollector.Test/InMemoryTenableIoStagingStore.cs
?? src/…/TenableIoCollector.Test/TenableIoAssetSpineTests.cs
```

| File | Lines |
|---|---|
| `Correlated/TenableIoSpinePaths.cs` | 174 |
| `Correlated/TenableIoAssetSpine.cs` | 189 |
| `Correlated/TenableIoAssetSpineManifest.cs` | 49 |
| `Correlated/TenableIoAssetSpoolPhase.cs` | 314 |
| `Correlated/TenableIoAssetSpoolReader.cs` | 69 |
| `Correlated/TenableIoExportPollHelpers.cs` | 41 |
| `Correlated/TenableIoChunkRetry.cs` | 38 |
| **production total** | **874** |
| `InMemoryTenableIoStagingStore.cs` | ~174 |
| `TenableIoAssetSpineTests.cs` | ~363 (28 test cases) |

Nothing outside `Correlated/` and the new test file references any of the new types
(`grep -rln` over the whole repo for all seven type names returns only those files). The new code is
therefore **unreachable from any production path** — which is the intended state per
`orchestration_plan.md`, and which also does the work of proving §4.4 below.

---

## 3. Success Criteria, one by one

| # | Criterion | Status | Citation |
|---|---|---|---|
| SC1 | `CollectFindings` publishes correlated envelopes only, no assets lane | **OUT-OF-SLICE** | phase 2. Nothing publishes in this slice; `TenableIoFindingsFlow.cs` untouched (`git diff 1f7a2ba6 --stat` empty) |
| SC2 | Every distinct in-window asset appears on exactly one chunk-0 host-bearing envelope; no asset silently dropped | **NOT MET (phase-1 half)** | the emission half is out of slice, but the *spine* half is in slice and is breached: `TenableIoAssetSpoolPhase.cs:226` retries only `IsRetryableStreamFailure`, which excludes 429/5xx (§5 D1), and `:152-158` logs skipped chunks without the ratio guard (§5 D2). A skipped assets chunk = ~1,000 assets absent from the spine |
| SC3 | One atomic object per vuln chunk / per sweep; chunk marked processed only after its object materializes | **OUT-OF-SLICE** | phase 2. No checkpoint writer exists in the slice |
| SC4 | Batch scoping emitter-owned; `batch_{page:D6}`; deterministic `instanceBatchId` | **OUT-OF-SLICE** | phase 2. The only `BatchScopedStorage` reference is the read-only `ResolveBaseUrl` (see §4.6) |
| SC5 | Resume per Fork B; old-format and `CombinedRun` checkpoints refused with a log line | **OUT-OF-SLICE**, primitive present | `TenableIoAssetSpine.TryReadManifestAsync` (`:128`) + `WriteManifestAsync` (`:136`) + manifest-last test (`TenableIoAssetSpineTests.cs:272-273`). No consumer, no decision logic, no checkpoint version work |
| SC6 | Spool backend is Fork A's outcome; the machinery it obsoletes is deleted from the tree | **MET** | staged spine at `_staging/spine/{assetId}.json` (`TenableIoSpinePaths.cs:110`); absence verified by grep in §4.2 |
| SC7a | Solution builds 0 warnings / 0 errors | **NOT MET — pre-existing, not this slice** | `dotnet build Cymulate.Integration.Adapters.sln` → `0 Warning(s) / 81 Error(s)`, **all 81 in `Cymulate.Integration.Adapters.YamlAdapter.Test.csproj`** (CS0106/CS1022 syntax errors from an unclosed brace, first at `YamlAdapterTests.cs:565`). That file is byte-identical to baseRef (`git diff 1f7a2ba6 --stat -- UnitTests/YamlAdapter/` empty) ⇒ the solution is red **on dev**. TenableIo collector + test projects build `0 Warning(s) / 0 Error(s)` |
| SC7b | TenableIo test project green via vstest | **NOT MET — pre-existing failure only** | see §4.7 |
| SC7c | New tests cover host resolution three ways, 2,000-cap slicing, checkpoint refusal, batch layout, staging-path guard | **PARTIAL** | staging-path guard **MET** (`TenableIoAssetSpineTests.cs:44-61`, 9 assertions incl. `assets_spine`, `findings_tmp`, `FINDINGS_UPPER`). The other four are phases 2–3 ⇒ OUT-OF-SLICE |
| SC8 | `Documentation/01-05` + `ai/skills/collector-flow-patterns` + `collector-recovery` reflect the design | **NOT MET** | `grep -rlni "spine\|_staging\|correlated"` over `Collectors/TenableIoCollector/Documentation/` and `ai/skills/` returns nothing; both trees are unmodified. Includes the **A1a** gap the contract asked to close "in this rebuild" |
| SC9 | LocalAdapterRunner `CollectFindings` run completes end to end with counts | **NOT MET, declared blocked** | `state.json` S9 `blocked` — needs operator Tenable credentials + AWS access. Honestly reported |
| SC10 | Fork C resolved with a citation and the rollout ordering stated | **NOT MET, declared out of slice** | `state.json` S2 `pending`, blocker B2 open |

Net for the slice as scoped: **SC6 met, SC7c's in-slice half met, SC2's in-slice half breached, SC7a/b
red for reasons that predate the slice, SC8 open.**

---

## 4. The mandated checks — each actually run

### 4.1 Phase-1 criteria — see §3.

### 4.2 Absence of the not-ported machinery — verified by grep, not by the notes

```
grep -rn -i -E "SpoolBudget|overflowMarker|IAssetSpool|OverflowChannelPublisher|windowed.?join|
                InMemoryGzipAssetSpool|DrainRemaining" --include=*.cs --include=*.md .   → exit 1 (no hits, whole repo)
grep -rn -i -E "gzip|GZipStream|Compress|Deflate" Collectors/TenableIoCollector/          → exit 1 (no hits)
grep -rn -i -E "budget|degrade|fallback|overflow" .../Correlated/                         → 3 hits, all prose
```
The three prose hits are doc comments explaining why none of it exists
(`TenableIoAssetSpoolPhase.cs:25-26`, `TenableIoAssetSpineManifest.cs:14`). **Verified absent** — and,
consistent with `orchestration_plan.md`'s scoping correction, absent because never written, not because
deleted. There was nothing on dev to delete.

### 4.3 No `PriorStateStore` on the spine path; no typed-DTO round trip

`grep -rn "PriorStateStore" Collectors/` → **2 hits, both doc comments** explaining why it is not used
(`TenableIoAssetSpine.cs:32`, `TenableIoSpinePaths.cs:139`). No call site.

Byte path verified end to end:
- in: `TenableIoAssetSpoolReader.cs:42` yields `NormalizedUtf8Json.SerializeToSingleLine(item)` →
  `TenableIoAssetSpine.StageAsync` → `ObjectWriteRequest.FromBytes(...)` (`TenableIoAssetSpine.cs:91-93`).
  No typed asset DTO anywhere in the path; only `id` is *read* off the element
  (`TenableIoAssetSpoolReader.cs:47`), never rewritten.
- out: `_store.ReadAllBytesAsync(...)` → `ReadOnlyMemory<byte>?` (`TenableIoAssetSpine.cs:105-106`).
- asserted: `TenableIoAssetSpineTests.StageThenRead_ReturnsTheRecordBytesVerbatim` uses a record with
  non-alphabetical key order, non-ASCII, an emoji and a nested object — content that fails if anything
  re-serializes. Passes.
- the reader's "fresh array per record" claim is safe: `NormalizedUtf8Json.SerializeToSingleLine(JsonElement)`
  ends in `buffer.WrittenSpan.ToArray()`
  (`IntegrationInfra/src/IntegrationInfra/Kernel/Json/NormalizedUtf8Json.cs:14`). See §5 D5 for a
  correction to the *reason* given for it.

### 4.4 The live `CollectFindings` path is genuinely unchanged

Three independent proofs: (a) `git diff 1f7a2ba6 --stat` empty; (b) `git status --porcelain` shows only
`??` entries; (c) no production file references any new type, so even a mis-scoped compile could not
reach the live flow. **Confirmed additive-only.**

### 4.5 Deletion is off the correctness path

- `DeleteSpineAsync` no-ops with a debug log when `_pruner is null` (`TenableIoAssetSpine.cs:148-155`)
  and swallows any non-cancellation failure with a warning (`:167-173`).
- No caller treats a delete as progress; nothing reads a "deleted" state.
- Tested both ways: `DeleteSpine_WithNoPrunerRegistered_IsANoOpAndDoesNotThrow` asserts the objects
  survive and `CanPrune` is false; `DeleteSpine_WithAPruner_CollectsTheWholeStagingArea` asserts the
  prefix `_staging/` is what gets deleted. **Confirmed.**

### 4.6 Write concurrency is bounded by the collector, not assumed from the façade

Verified against the substrate source, not the README: `GuardedObjectStore.WriteAsync` takes **no** slot
and simply forwards to `_store.WriteAsync` after a payload-size check
(`IntegrationInfra/.../Ingestion/GuardedObjectStore.cs:274-290`); the semaphore
(`_readSlots`, `MaxConcurrentReads` default 4) is on the read path only. So the collector *must* bound
writes, and it does: `SpineWriteConcurrency = 32` as a `const`
(`TenableIoAssetSpoolPhase.cs:52`) driving `Parallel.ForEachAsync`'s `MaxDegreeOfParallelism`
(`:207-218`). Not a config knob — matches the "capability constants, not rollout knobs" convention.
**Confirmed.**

On `BatchScopedStorage`: the collector *does* name the type, at `TenableIoAssetSpine.cs:81`
(`BatchScopedStorage.ResolveBaseUrl`). This is **not** the constraint violation it looks like. The
constraint targets the opt-in/`BeginPage` lifecycle; `ResolveBaseUrl` is explicitly public *for this use*
— "Public because collectors need exactly this answer… a collector that re-implemented it dropped the
`StripBatchSegment` step" (`Envelopes/Common/BatchScopedStorage.cs:205-213`) — and Falcon's reference
staging area does the identical thing (`FalconStagingArea.cs:137-138`). Correct call.

### 4.7 Tests, re-run by me

Build:
```
dotnet build …/TenableIoCollector.Test.csproj   →  0 Warning(s), 0 Error(s)
dotnet build Cymulate.Integration.Adapters.sln  →  0 Warning(s), 81 Error(s)  (all YamlAdapter.Test, red at baseRef)
```

Filtered, re-run by me:
```
dotnet vstest …TenableIoCollector.Test.dll --TestCaseFilter:"FullyQualifiedName~TenableIoAssetSpineTests"
Passed! - Failed: 0, Passed: 28, Skipped: 0, Total: 28, Duration: 213 ms
```
**28/28 confirmed** — matches the executor's claim exactly.

Full TenableIo suite, re-run by me (unfiltered, one dll, ~5m20s):
```
Failed!  - Failed: 1, Passed: 42, Skipped: 0, Total: 43, Duration: 5 m 14 s
[FAIL] TenableIoCollectorTests.ResumeAsync_Findings_WhenTransportResponseEndsPrematurely_ReturnsRetryableAdapterFailure
       Assert.Empty() Failure: Collection was not empty
       Collection: [CompletionRequest { Context = AdapterProgressContext, Success = False }]
       at TenableIoCollectorTests.cs:line 696
```
**42/43 confirmed**, with the identical test, identical assertion and identical line the executor
reported. No new failure, no flake in the 28 new tests.

On the "pre-existing" claim for
`TenableIoCollectorTests.ResumeAsync_Findings_WhenTransportResponseEndsPrematurely_ReturnsRetryableAdapterFailure`:
I did not re-run a clean `origin/dev` worktree, but the claim is provable more cheaply and more strongly
than by that route. `TenableIoCollectorTests.cs` and every production file it exercises are byte-identical
to baseRef, and no production file references any type this slice added (§2), so this slice cannot be in
that test's causal path. **Accepted as pre-existing.** Blocker B3's two readings (stale expectation vs a
real false-terminal-DONE on a retryable transport error) are correctly left for the operator; note the
second reading is a recovery-plane defect on the *live* collector and is worth its own task.

### 4.8 Consistency between contract, plan, decisions and what landed — drift found

1. **Contract Execution Rules say the key is `_staging/{generation}/spine/{uuid}`; the code has
   `_staging/spine/{assetId}.json`.** The generation segment was dropped during execution. Assessed in §6
   as *justified with a stated cost*, not a shortcut.
2. **`state.json` S3 is marked `complete` with outcome "Ported the phase-1 core (spool reader, poll
   helpers, chunk retry)", but S3's own `notes` list the record writer, chunk bucketer, 2000-cap slicing,
   miss lane, `num_assets=50`, both-exports-up-front, `MaxSkippedChunkRatio` and the dry-run probe** —
   none of which landed. S3 is *partially* complete; marking it `complete` overstates. The outcome line
   is accurate; the status field is not.
3. **`assumptions.md` has two entries numbered `A10`** — one `OPEN` (the Fork A gating unknown) and one
   `REJECTED`. The `OPEN` one is superseded and should be retired rather than left to collide.
4. Constraint "*Keep* … the `MaxSkippedChunkRatio` guard" is unmet in the new phase (§5 D2), and nothing
   in `decisions.md` or `execution_notes.md` records dropping it. Undocumented drift.
5. `execution_notes.md` reports the two project builds as `0/0` and does not mention that the **solution**
   build — the wording the contract actually uses — is red. Not a false claim; an omission that would
   have surprised the operator.

### 4.9 Missing edge cases a phase-1 slice should have covered

See §5. Ranked: D1 (429/5xx chunk skip) and D2 (no ratio guard) are in-slice defects; D3 (silent
under-sweep on a base-URL mismatch) is a real gap Falcon explicitly guards against; D4 (no hard-failure
entry point) and D6 (manifest never validated on read) are phase-2 obligations that this slice should at
minimum have recorded.

---

## 5. Defects and gaps found

### D1 — HIGH, in slice. A rate-limited or 5xx assets chunk is permanently skipped on the first hit.

`TenableIoAssetSpoolPhase.TryStageChunkAsync` retries only on
`TenableIoChunkRetry.IsRetryableStreamFailure(ex)` (`TenableIoAssetSpoolPhase.cs:226`), which is
`IsRetryableTransportFailure || IsCircuitBreakerException` (`TenableIoChunkRetry.cs:32-34`).
`HttpTransportFailureClassifier.IsRetryableTransportFailure` covers only `TimeoutException`, transient
`SocketException`, and `HttpRequestException`/`IOException` carrying a transport marker
(`IntegrationInfra/.../Kernel/Transport/HttpTransportFailureClassifier.cs:50-79`). It does **not** cover an
`AdapterHttpRequestFailedException` carrying `429 / 502 / 503 / 504`.

So a `429` on a chunk download falls through to the terminal
`catch (Exception ex)` at `:236` → `"chunk {ChunkId} permanently skipped"` → `return null` → the caller
adds it to `excludedChunkIds` (`:136`). At `chunk_size=1000` that is **~1,000 assets missing from the
spine**, which phase 2 will later emit as thin-host misses. The run still succeeds.

Three things make this an oversight rather than a decision:
- the **poll loop in the same class** does check it: `TryHandlePollFailure` tests
  `TenableIoAssetsExportClient.IsTransientOrRateLimited(httpEx)` first (`:251`);
- the **live** assets flow has a dedicated branch for exactly this, passing the exception through:
  `catch (AdapterHttpRequestFailedException ex) when (!isLastAttempt && …IsTransientOrRateLimited(ex))` →
  `ComputeChunkRetryDelay(attempt, ex)` (`Flows/Assets/TenableIoAssetsFlow.cs:567-573`);
- `TenableIoChunkRetry.ComputeDelay`'s `ex` parameter is **dead** — its `RetryAfter` and 429 branches
  (`TenableIoChunkRetry.cs:17-25`) are unreachable, because the single call site passes `ex: null`
  (`TenableIoAssetSpoolPhase.cs:230`). Dead vendor-delay handling is the fingerprint of the missing branch.

Fix: add the `AdapterHttpRequestFailedException` + `IsTransientOrRateLimited` retry branch and pass `ex`
into `ComputeDelay` so `Retry-After` is honoured. Note this also intersects the repo's rate-limit law —
a sub-60s vendor delay should be slept in-process here (which it would be), and only a longer one
externalized.

### D2 — HIGH, in slice. `MaxSkippedChunkRatio` is absent, so an arbitrarily incomplete spine still gets a manifest.

Constraints, "Vendor requests": "*Keep the existing 409 `active_job_id` reuse, progressive chunk loop,
per-chunk retry/exclusion, and the `MaxSkippedChunkRatio` guard.*" Both live flows have it
(`TenableIoFindingsFlow.cs:27,391`; `TenableIoAssetsFlow.cs:33,367` — fail once permanent failures reach
50 % of expected). The new spool phase has **no equivalent**: it logs a warning at `:152-158` and then
writes the manifest unconditionally at `:162-171`.

Compounded with D1 this is the sharpest hazard in the slice: one rate-limit episode can skip most chunks,
the manifest is still written, `SkippedChunkCount` is recorded but **read by nobody**, and the manifest is
by design the completion proof — so a later leg *skips re-spooling* a spine that is mostly empty. The run
reports success; the customer gets a findings lane of thin hosts.

Fix: apply the same ratio guard before writing the manifest, and refuse to write a completion manifest
when the ratio is breached. (Whether a partially-skipped spool may be marked complete at all is a real
design question — currently the answer is silently "yes, at any ratio".)

### D3 — MEDIUM, in slice. A base-URL mismatch on listing silently under-sweeps.

`TenableIoAssetSpine.Relative(location)` returns `string.Empty` when a listed URL does not start with
`BaseStorageUrl + "/"` (`TenableIoAssetSpine.cs:183-188`), and `ListStagedAssetIdsAsync` then drops it via
`TryGetAssetId(...) is { } assetId` (`:120`). An `IAdapterObjectStore` whose listing convention yields
bucket-relative keys or full URIs would produce an **empty sweep with no exception and nothing in the
logs** — every zero-vuln asset silently absent from the output, which is precisely the "no asset is
silently dropped" invariant.

Falcon guards this deliberately and explains why in the same words:
`FalconStagingArea.WarnIfNotRelativeToBase` warns once per enumeration —
"*One warning turns a permanent invisible degradation into a five-second diagnosis*"
(`FalconStagingArea.cs:141-173`). The reference implementation was followed everywhere else; this is the
one place it was not. Cheap fix, and the slice already has the test double that would exercise it.

### D4 — MEDIUM, in slice by the contract's wording. The hard-failure entry point does not exist.

Constraint: "*treat a missing `IAdapterObjectStore` as a hard failure with no fallback*". Nothing in the
slice calls `GuardedObjectStore.Create(services)` (which is where the throw lives —
`GuardedObjectStore.cs:82-90`), and nothing calls `TenableIoAssetSpine.TryResolveBaseStorageUrl`, whose
`null` return is unhandled. The behaviour is therefore neither implemented nor tested; it is inherited
*potentially*, from a call site that does not exist yet. Acceptable if recorded; it currently is not.

### D5 — LOW, documentation accuracy. The reader's stated reason for a fresh array is wrong for the pinned substrate.

`TenableIoAssetSpoolReader.cs:19-25` and `execution_notes.md` both argue the reusable-buffer overload
"would be a correctness bug… shared buffer contents would be overwritten underneath it". In
`1.2.0-preview.0` **both** overloads copy: `SerializeToSingleLine(element, reusableBuffer)` also ends in
`reusableBuffer.WrittenSpan.ToArray()` (`NormalizedUtf8Json.cs:22-27`). The decision is right and the code
is safe; the *reason* given does not hold against the source, and a reason that does not hold is how a
future reader "corrects" a correct choice. Restate it as "returns a copy either way; the non-buffer
overload says so at the call site" or drop the claim.
(Verified against the local `IntegrationInfra` worktree, whose `Directory.Build.props:39` reads
`<Version>1.2.0-preview.0</Version>`, matching the pin at `Directory.Packages.props:123`. If that worktree
carries unpushed commits past the packed 1.2.0-preview.0, this one point is downgraded to unverified.)

### D6 — LOW, phase-2 obligation to record now. Nothing validates a manifest it reads.

`TenableIoAssetSpineManifest` carries `BaseDateUtc` "*so a manifest cannot be reused across windows*"
(`:42-44`) and `SkippedChunkCount`, and `TryReadManifestAsync` returns it — but no code compares either
against the current run. Since the manifest **is** the skip-the-spool proof, phase 2 must reject a
manifest whose `BaseDateUtc` differs from the leg's base date and must decide what a non-zero
`SkippedChunkCount` means. Absent a generation folder (§6), the path itself carries no such separation.

### D7 — INFORMATIONAL. `timeoutAt` is not reset after a mid-spool export re-creation.

`TenableIoAssetSpoolPhase.cs:64` computes `timeoutAt` once; the 404 re-creation path at `:82-97` restarts
the pass without extending it. Reads as deliberate (a total bound on the phase), and re-creation is rare,
but a 404 late in a long spool now guarantees a `TimeoutException` rather than a completed second pass.
Worth one sentence of doc either way.

---

## 6. Decision drift — `decisions.md`, entry by entry

Carried-in decisions (record grammar, 2,000 cap, `uuid` key, no assets lane, verbatim `host`, miss lane,
`num_assets=50`, both exports up front, sweep-after-vulns, one atomic object, staging under a `_` segment
with a producing-side test, deletion as GC, publish-and-checkpoint as one step, coordinate resume
position): all **phase 2–3 or already honoured**. The two that were in-slice landed as decided —
`_`-prefixed staging segment with the guard test, and deletion as pure GC.

| Decision | Outcome |
|---|---|
| Re-implementation, not merge/rebase | **As decided.** No prior-branch code present; all seven files are new prose and new structure. |
| Branch off `dev` at `1f7a2ba6` | **As decided.** `git rev-parse HEAD` = `1f7a2ba6`, branch correct. Note nothing is committed yet. |
| Batch scoping opted in via the emitter, exemplar Qualys | **Not yet exercised** (phase 2). No `BeginPage` anywhere in the new code — the pattern is intact. |
| Prior branch's manual `BeginPage` dropped | **As decided** — absent. |
| Overflow machinery ported only if Fork A keeps RAM | **As decided** — Fork A staged, machinery absent (§4.2). |
| Scope adapters-only, parser as rollout dependency | **As decided**, unresolved (Fork C / B2). |
| `CollectorVersion` not set in csproj | **As decided.** The TenableIo csproj has no version property at all. `execution_notes.md` correctly surfaces a recommendation (default now 6.2.3 ⇒ MAJOR would be 7.0.0) without setting it. |
| Checkpoint format version decided at S5 | **Deferred as decided** (S5 pending). |
| **Fork A — staged, one object per asset uuid via `GuardedObjectStore`** | **As decided**, with one divergence: the path is `_staging/spine/{assetId}.json`, **not** `_staging/{generation}/spine/{uuid}`. See below. |
| Fork A — `GuardedObjectStore`, never `PriorStateStore` | **As decided** (§4.3). |
| Fork A — `IAssetSpool` seam "retained, made async, implementation swapped" | **Abandoned.** There is no seam: `TenableIoAssetSpoolPhase` depends on the concrete `TenableIoAssetSpine`. Correct call — a one-implementation interface is the speculative abstraction the constraints forbid — but it *is* a divergence from a written decision and is not recorded as one. |
| Fork A — write concurrency is the collector's job | **As decided** (§4.6). |
| Traversal stays vuln-chunk-driven, not spine-driven | **As decided**; nothing in the slice presumes key-driven traversal. |
| Fork B — Falcon's resume model | **Deferred** (phase 2). Manifest primitive present and tested; no decision logic. |

### The "no generation folder" divergence — justified, with a cost the operator should see

The reasoning in `TenableIoSpinePaths.cs:17-36` is sound and specific: Falcon needs generations because its
keys are *positional* (`hosts_000001`) and page boundaries move between attempts, so a shared folder lets a
re-spool put *different* hosts under an existing key. Identity-derived keys make that hazard
unrepresentable — an overwrite can only replace an asset with a fresher copy of itself. Verified by test
(`Restaging_AnAsset_OverwritesItsOwnObjectInsteadOfAccumulating`: two writes, one key). Cross-run
contamination is separately excluded because `storageUrl` carries a per-run instance id. **Not a shortcut.**

The cost is real and is acknowledged only in a doc comment: without generations, **the residue of a failed
leg is reused rather than discarded**. An asset staged by leg 1 that leg 2's re-created export no longer
returns still sits in the spine and will be swept as a zero-vuln asset — a spurious empty envelope for an
asset no longer in window. The prior branch's RAM spool could not do this, because it started empty every
leg. `decisions.md` should carry this as an accepted behaviour (it sits comfortably under A9's
operator reframing — inter-export staleness is inherent — but it is a *new* instance of it, not a carried
one), and phase 2 must not re-derive "the spine equals this leg's snapshot".

---

## 7. Assumption disposition

NEVER-TESTED is the default. VALIDATED/REJECTED only where I could cite the hunk, test, source line or
fetched document that moves it. I re-checked every citation the executor supplied; all of the ones I could
reach hold, and none were downgraded.

| id | status | citation | actor |
|---|---|---|---|
| PA1 | NEVER-TESTED | no AgentService code read in this slice; bears on a later port only | verifier 2026-08-17 |
| PA2 | NEVER-TESTED | nothing in the slice emits arrays or sorts records; `host` bytes pass through verbatim (`TenableIoAssetSpine.cs:91-106`), so vendor array order is preserved and the exposure the assumption describes is still live and unexamined | verifier 2026-08-17 |
| PA3 | OPEN (parser side unverifiable here); producing-side mitigation VALIDATED | mitigation confirmed: `TenableIoSpinePaths.AssertNotParserVisible()` is called in the spine constructor before any write (`TenableIoAssetSpine.cs:64`), and `IsParserVisibleName` is asserted over 8 cases incl. `assets_spine`, `findings_tmp`, `FINDINGS_UPPER` (`TenableIoAssetSpineTests.cs:51-61`, passing). The parser-behaviour half rests on a citation from another repo's task and cannot be settled here | verifier 2026-08-17 |
| PA4 | NEVER-TESTED | no IAM read performed. Consequence bounded by design: `DeleteSpineAsync` no-ops without a pruner and swallows failures (`TenableIoAssetSpine.cs:148-173`), asserted by `DeleteSpine_WithNoPrunerRegistered_IsANoOpAndDoesNotThrow`. Worst case is storage accumulation | verifier 2026-08-17 |
| A1 (vendor half) | VALIDATED as an *implication only* — the doc does not promise non-straddling | independently re-fetched `developer.tenable.com/reference/exports-vulns-request-export`: "The vulnerabilities export is split up by number of asset IDs in a chunk. The exported data of a chunk is the sum of all the vulnerabilities for each asset in that chunk. The range… minimum of 50 (the default size) to a maximum of 5,000." No statement forbids an asset's vulns spanning chunks. Executor's citation confirmed verbatim | verifier 2026-08-17 |
| A1 (empirical half) | NEVER-TESTED in this slice — carried, stale | prior task's 150-chunk probe; nothing here re-probes, and per the contract nothing should | verifier 2026-08-17 |
| A1 (tolerance retained) | NEVER-TESTED | the straddle tolerance lives in `BuildRecordsForChunk`, which is phase 2 and does not exist. The tolerance therefore has **not** survived the rebuild yet — it is neither implemented nor tested | verifier 2026-08-17 |
| A1a | REJECTED as *closed* — the gap it names is still open | the contract asked to state the premise in `Documentation/01` and test the straddle case "in this rebuild". `grep -rlni "spine\|_staging\|correlated" Collectors/TenableIoCollector/Documentation/` → no hits; docs unmodified from baseRef; no straddle test exists | verifier 2026-08-17 |
| A2 | NEVER-TESTED | carried sizing figures; no measurement in this slice. Load-bearing for the write-concurrency choice at `TenableIoAssetSpoolPhase.cs:52` | verifier 2026-08-17 |
| A3 | NEVER-TESTED | carried; phase-2 read-cost model | verifier 2026-08-17 |
| A4 | VALIDATED (structurally, for assets) | the spool loop consumes `chunks_available` while status is not FINISHED (`TenableIoAssetSpoolPhase.cs:72,118,140`), and `SpoolPhase_StagesEveryAssetUnderItsOwnId_AndWritesTheManifestLast` drives it against a stub. This proves the *collector* streams progressively; the vendor-behaviour half stays carried | verifier 2026-08-17 |
| A5 | NEVER-TESTED | the 409 `active_job_id` reuse is inherited from the untouched `TenableIoAssetsExportClient.cs:63-79`; concurrency limit and 429 behaviour unexercised. Note **D1**: the 429 path on chunk download is now *wrong*, so this assumption's consequence is worse than when it was written | verifier 2026-08-17 |
| A6 | NEVER-TESTED | no staleness rule exists in the slice (phase-2 checkpoint work) | verifier 2026-08-17 |
| A7 | NEVER-TESTED | phantom allocations surface in phase 2's miss lane, which does not exist | verifier 2026-08-17 |
| A8 | NEVER-TESTED | carried scale figures; the local proof run (S9) that would touch them is blocked | verifier 2026-08-17 |
| A9 | NEVER-TESTED | the re-creation path exists (`TenableIoAssetSpoolPhase.cs:82-97`) and resets its counters, but no test drives a 404 mid-spool, and the "deltas surface in the miss lane" half is phase 2. **New instance created by this slice:** without a generation folder, leg-1 residue also surfaces as a spurious swept zero-vuln asset (§6) | verifier 2026-08-17 |
| A10 (the OPEN duplicate — `PriorStateStore` as a usable keyed spool at scale) | REJECTED, and the entry should be retired as a duplicate id | superseded by the REJECTED A10 below; two entries share the id `A10` in `assumptions.md` | verifier 2026-08-17 |
| A10 (REJECTED — `PriorStateStore` unusable for entity bodies) | REJECTED — citation verified exactly | `PriorStateStore.cs:47-75`: `ReadAsync<TState>` deserializes, `WriteAsync<TState>` serializes, and the >`MaxControlArtifactBytes` throw message reads "Per-key state is a watermark, not a copy of the entity." Executor's citation holds | verifier 2026-08-17 |
| A11 | VALIDATED — citation verified | `FrozenKeyList`'s entire public surface is `FreezeAsync` + `ReadBatchesAsync` (`FrozenKeyList.cs:51,91`) — no keyed access; `StagingManifest` is one control artifact (`StagingManifest.cs`); the keyed primitive is `GuardedObjectStore.ReadAllBytesAsync` (`GuardedObjectStore.cs:161`), which is what the spine uses (`TenableIoAssetSpine.cs:106`) | verifier 2026-08-17 |
| A12 | NEVER-TESTED | no ISB registration read performed; and per **D4** the slice contains no call site that would fail fast, so the stated "hard-fails at flow entry" behaviour is not yet true of this code | verifier 2026-08-17 |
| A12a (`s3:ListBucket`) | NEVER-TESTED — sharpest live risk | no IAM check. Mechanism confirmed: `ReadAllBytesAsync` returns `null` only because `StatAsync` returned `null` (`GuardedObjectStore.cs:167-171`); the contract forbids reporting a permission failure as `null`, so without the grant every ordinary miss becomes an exception on a hot path | verifier 2026-08-17 |
| A13 | NEVER-TESTED | emitter batch-scope release is phase 2; nothing in the slice publishes | verifier 2026-08-17 |
| A14 | MOOT / VALIDATED-as-irrelevant | the RAM overflow path was never written (§4.2 greps), so there is no unexercised code carried forward from it | verifier 2026-08-17 |
| A15 | NEVER-TESTED | `cymulate-integration-parsers` not read; S2 pending, blocker B2 open. Gates any rollout claim | verifier 2026-08-17 |
| A16 | NEVER-TESTED | `LocalAdapterRunner` untouched and not driven; S9 blocked on credentials | verifier 2026-08-17 |
| A17 | REJECTED — independently re-verified | I re-fetched `developer.tenable.com/reference/exports-vulns-request-export`: top-level body is `num_assets`, `include_unlicensed`, `include_software_vulns`, `include_plugin_output`, `properties`, `filters`; **no asset/UUID array**; and the full `filters` key list contains no UUID-keyed asset scope (only `cidr_range` and `tag.<category>` scope by asset at all). The executor's REJECTED disposition holds | verifier 2026-08-17 |
| A18 | OPEN → still OPEN, existence half VALIDATED | the same fetch confirms a top-level `properties` array exists. Whether `asset.uuid` alone is selectable — the part that matters — was not probed | verifier 2026-08-17 |
| A19 | VALIDATED — citation verified | `GuardedObjectStore.ReadAllBytesAsync` stats first and returns `null` when `stat is null` (`GuardedObjectStore.cs:167-171`); `TryRead_ForAnAssetTheSpineDoesNotHold_ReturnsNullRatherThanThrowing` passes against a double that throws `ObjectNotFoundException` on `OpenReadAsync` (`InMemoryTenableIoStagingStore.cs:70`), so the test genuinely exercises the stat-first path | verifier 2026-08-17 |
| A20 | VALIDATED — citation verified | `Restaging_AnAsset_OverwritesItsOwnObjectInsteadOfAccumulating` asserts 2 writes / 1 key and reads back `v:2`; passing in my 28/28 run | verifier 2026-08-17 |
| A21 | VALIDATED — citation verified | `SpoolPhase_StagesEveryAssetUnderItsOwnId_AndWritesTheManifestLast` asserts `store.Writes[^1] == "_staging/manifest.json"` and its absence from `Writes[..^1]` (`TenableIoAssetSpineTests.cs:272-273`); `SpoolPhase_WhenAChunkIsFailedServerSide…` re-asserts the last-write property. Both passing. **Scope note:** the manifest is written last *even when chunks were skipped* — see D2 | verifier 2026-08-17 |

---

## 8. Certainty

- **High** on everything checked by command in this repo or against `IntegrationInfra` source: the diff
  shape, the absence greps, the byte path, the build and filtered-test numbers, D1, D2, D3, D4.
- **High** on D1 being a defect rather than a decision (three independent corroborations, including a
  dead parameter).
- **Medium-high** on D5: it rests on the local `IntegrationInfra` worktree matching the packed
  `1.2.0-preview.0`. The version property matches; unpushed commits would break the inference.
- **Medium** on the "spurious swept zero-vuln asset" cost of dropping the generation folder — it follows
  from the code and is stated in the type's own remarks, but no test or run demonstrates it.
- **Not verified**: every NEVER-TESTED row above, in particular A12a (`s3:ListBucket`) and A15 (parser
  shape), which between them gate any claim that this design can run anywhere real.
