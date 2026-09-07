# Assumptions

Status vocabulary: OPEN / VALIDATED / REJECTED / NEVER-TESTED. Anything other than OPEN requires an
actor and a citation. This file was written at contract time, so every entry below started OPEN.

**Verifier pass 1 (verifier, 2026-08-17)** dispositioned every entry against the code that actually
landed in slice 1. Full reasoning and the command evidence are in `review/verifier-1.md` §7. Rules
applied: NEVER-TESTED is the default; a status moved only on a citable diff hunk, test, source line or
re-fetched document. Every executor-supplied citation was re-checked and none were downgraded. Statuses
below carry the verifier's disposition appended to the executor's where the two differ in scope.

## Prior Art

Recall tags: `cymulate-integration-adapters`, `tenable`, `correlated-findings`, `checkpoint-resume`,
`s3-staging`.

Ledger hits in `~/codex-state/lessons.md` (3 matched; 2 task directories read, within the cap of 3):

- **PA1 — NEVER-TESTED** (verifier, 2026-08-17: no AgentService code read in slice 1; bears on a later port
  only) — The AgentService Tenable collector has no checkpoint/resume path to mirror: it waits
  for export FINISHED then uploads, so mid-chunk-resume duplication is structurally impossible there.
  Bears on any later AgentService port of this work.
  source: lessons.md#2026-04-29-checkpoint-resume (researcher, 2026-06-12)
- **PA2 — NEVER-TESTED** (verifier, 2026-08-17: nothing in slice 1 emits arrays or sorts records, and `host`
  bytes pass through verbatim — `TenableIoAssetSpine.cs:91-106` — so vendor array order is preserved and the
  exposure this describes is live and unexamined) — Array ordering instability is not identity instability. On Falcon, remediation entity
  IDs were identical across runs and only array order differed; sorting at emission was still adopted to
  fix parser `entities[0]` nondeterminism. Relevant because this collector passes vendor records through
  verbatim, so any array the parser indexes positionally is exposed to vendor ordering.
  source: lessons.md#2026-07-02-entity-ids (executor, 2026-07-02)
- **PA3 — OPEN (belief unchanged; the mitigation it required is now implemented)** — A middle-ground folder
  inside the run's `storageUrl` is invisible to parser input discovery **only if its name does not begin with
  `assets` or `findings`**. Discovery is an undelimited boto3 prefix match across the whole subtree —
  `_staging/` is excluded, `assets_staging/` would be ingested. The exclusion is a naming coincidence in
  another repo, unenforced in the resolver.
  Stays OPEN because it is a claim about the *parser's* behaviour, which nothing in this repo can settle.
  What is now settled is this side of the coupling: the producing-side guard exists and has teeth —
  `TenableIoSpinePaths.AssertNotParserVisible()` runs in the spine's constructor before a byte is staged, and
  `IsParserVisibleName` is asserted against 8 cases including `assets_spine` and `findings_tmp`.
  source: prior art `…/2026-08-05_1937_s3-capability-falcon-two-phase-correlation/assumptions.md#A1`
  (researcher, 2026-08-05); mitigation `TenableIoSpinePaths.cs` +
  `TenableIoAssetSpineTests.IsParserVisibleName_FlagsExactlyThePrefixesTheParserMatches` (executor, 2026-08-17)
  **Verifier 2026-08-17: mitigation half VALIDATED, parser half stays OPEN.** The guard runs in the spine's
  constructor before any write (`TenableIoAssetSpine.cs:64`) and the theory covers 8 cases including
  `assets_spine`, `findings_tmp` and `FINDINGS_UPPER` (`TenableIoAssetSpineTests.cs:51-61`, passing in a
  re-run 28/28). The parser-behaviour half cannot be settled from this repo.
- **PA4 — NEVER-TESTED** (verifier, 2026-08-17: no IAM read performed. Consequence is bounded by design —
  `DeleteSpineAsync` no-ops without a pruner and swallows failures, `TenableIoAssetSpine.cs:148-173`, asserted
  by `DeleteSpine_WithNoPrunerRegistered_IsANoOpAndDoesNotThrow`; worst case is storage accumulation) — The
  IRSA policy grants the ISB pod read **and delete** on the data bucket, not only
  write. Delete is a distinct IAM action. Recorded as still-open in the Falcon S3 task; if wrong, a
  staged design works in tests and fails in the cluster at the first prune.
  source: `…/2026-08-05_1937_s3-capability-falcon-two-phase-correlation/assumptions.md#A3`
  (researcher, 2026-08-05)

## Carried vendor facts — validated on the prior branch, re-entering as OPEN

These were validated by probe and official docs during the 2026-07 task. They are stale operational
evidence about a live tenant and a live API, so they re-enter OPEN rather than as facts. Full figures
are in `research/branch-technique-and-dev-diff.md` §5. Do **not** re-derive them from scratch; confirm
cheaply or accept the citation.

- **A1 — OPEN (partially validated; the vendor half is an implication, not a guarantee)** — Vuln export
  chunks are asset-complete: an asset's findings never straddle two chunks. This is what makes
  correlation chunk-local, **and** what bounds the staged backend's read cost to one GET per asset.
  - Vendor half, re-verified live 2026-08-17: `developer.tenable.com/reference/exports-vulns-request-export`
    states verbatim — "Specifies the number of assets used to chunk the vulnerabilities. The
    vulnerabilities export is split up by number of asset IDs in a chunk. The exported data of a chunk
    is the sum of all the vulnerabilities for each asset in that chunk." It does **not** explicitly
    state that an individual asset's vulnerabilities cannot be split across chunks. Non-straddling is
    implied by the split-by-asset-ID model, not promised.
    source: developer.tenable.com/reference/exports-vulns-request-export (executor, 2026-08-17)
  - Empirical half, stale: probe over 150 chunks / 7,486 assets / 526,519 findings, zero straddles,
    assets/chunk min 48 max 50.
    source: `ai/active/2026-07-05_1802_tenableio-correlated-findings/assumptions.md#A1` (executor, 2026-07-06)
  - Tolerance is designed in and must be retained: a straddle surfaces as a second thin chunk-0 host on
    the later chunk (the miss lane), absorbed by parser per-uuid aggregation — never a dropped asset.
  - **Verifier 2026-08-17.** Vendor half **VALIDATED as an implication only** — independently re-fetched
    `developer.tenable.com/reference/exports-vulns-request-export`, which confirms the executor's quote
    verbatim and confirms that nothing on the page forbids an asset's vulnerabilities spanning chunks.
    Empirical half **NEVER-TESTED in this slice** (carried, stale, and per the contract not re-derived).
    **Tolerance half NEVER-TESTED and not yet retained:** it lives in `BuildRecordsForChunk`, which is
    phase 2 and does not exist — so the tolerance is currently neither implemented nor tested.
- **A1a — PARTIALLY CLOSED after repair round 1** (verifier pass 2, 2026-08-17).
  *Pass 1 finding, now superseded:* neither gap was closed — the Documentation tree was byte-identical to
  baseRef and no straddle test existed.
  *Pass 2:* the **documentation half is closed, and better than asked.**
  `Documentation/01-collection-strategy.md` gained an "In progress: the correlated model" section stating the
  premise, the 150-chunk probe, the fact that non-straddling is "implied by the split-by-asset-ID model, **not
  promised**", that `POST /vulns/export` accepts no asset-UUID parameter, and the thin-host tolerance.
  `ai/skills/collector-flow-patterns/SKILL.md` records the look-up-vs-enumerate divergence from Falcon.
  The **test half remains open**: no test asserts that a straddled asset yields a thin second host rather than a
  lost one, because that lives in phase 2's `BuildRecordsForChunk`. Carry it as a phase-2 obligation. — Documentation gap carried from the prior branch: the premise is stated in exactly one
  code comment (`TenableIoVulnPhase.cs:193-197`, added as review minor **O3** because the reviewer found
  it undocumented) and in the prior task's `assumptions.md`. It appears in **none** of the six
  `TenableIoCollector/Documentation/*.md` files — where `num_assets` is discussed only as a memory bound
  — and **no test asserts it**. Close both gaps in this rebuild: state it in `Documentation/01`, and
  test that a straddled asset yields a thin second host rather than a lost one.
  source: `git grep` over the prior branch's collector tree + `execution_notes.md:101` (executor, 2026-08-17)
- **A2 — NEVER-TESTED** (verifier, 2026-08-17: carried figures, nothing measured in slice 1; load-bearing for
  the `SpineWriteConcurrency = 32` choice at `TenableIoAssetSpoolPhase.cs:52`) — Spool sizing: ~4 KB raw per asset, gzip 2.9×, 108 K-asset projection 435 MB raw /
  152 MB compressed; measured peak on the largest tenant 54.4 MB compressed.
  source: same file #A2 + the 20260706-102603 run record (executor, 2026-07-06)
- **A3 — NEVER-TESTED** (verifier, 2026-08-17: carried; feeds the phase-2 read-cost model, untouched here) —
  At `num_assets=50` a vuln chunk is 10–43 MB (p50 22 MB); densest asset 842 findings.
  source: same file #A3 (executor, 2026-07-06)
- **A4 — VALIDATED structurally for the assets export; vendor half carried** (verifier, 2026-08-17) — Chunks
  stream while the export is still PROCESSING.
  source: same file #A4 (executor, 2026-07-06). Verifier: the spool loop consumes `chunks_available` while
  status is not FINISHED (`TenableIoAssetSpoolPhase.cs:72,118,140`) and
  `SpoolPhase_StagesEveryAssetUnderItsOwnId_AndWritesTheManifestLast` drives that against a stub — which
  proves the *collector* streams progressively, not the vendor's behaviour.
- **A5 — NEVER-TESTED** — 10 concurrent exports per container; 429 + Retry-After on excess; duplicate-filter
  exports deduped server-side via the 409 `active_job_id` path.
  source: same file #A5 (executor, 2026-07-06). Verifier 2026-08-17: the 409 reuse is inherited from the
  untouched `TenableIoAssetsExportClient.cs:63-79`; concurrency and 429 behaviour are unexercised. **The
  consequence is now worse than when this was written** — see `review/verifier-1.md` D1: a 429/5xx on an
  assets *chunk download* is not retried and permanently skips the chunk.
  **Verifier pass 2, 2026-08-17: the 429/5xx handling half is now VALIDATED against a stub.** The dedicated
  retry branch at `TenableIoAssetSpoolPhase.cs:283-291` honours `Retry-After`, and
  `SpoolPhase_WhenAChunkIsRateLimitedOnce_RetriesItAndLosesNoAssets` plus
  `SpoolPhase_RetriesTheTransient5xxFamilyToo` (502/503/504) assert a real second HTTP call and zero lost
  assets. The *vendor-side* facts (10 concurrent exports, when 429s actually occur) remain NEVER-TESTED.
- **A6 — NEVER-TESTED** (verifier, 2026-08-17: no staleness rule exists in slice 1 — that is phase-2
  checkpoint work) — Chunk retention: docs say 24 h in one place and 3 days in another; ~24 h is the
  conservative bound the staleness rule already uses.
  source: same file #A6 (executor, 2026-07-06)
- **A7 — NEVER-TESTED** (verifier, 2026-08-17: phantom allocations surface in phase 2's miss lane and in the
  sweep, neither of which exists) — Phantom allocations are benign: ~0.2 % of allocated asset slots yield zero rows, and
  absence from the vuln export means zero-vuln, handled by the sweep.
  source: same file #A7 (executor, 2026-07-06)
- **A8 — NEVER-TESTED** (verifier, 2026-08-17: the local proof run S9 that would touch these figures is
  blocked on operator credentials) — Reference tenant at a 90-day window: ~2,168 chunks ≈ 108 K assets, ~7.6 M findings.
  source: same file #A8 (executor, 2026-07-06)
- **A9 — OPEN, and reframed** — Assets-export re-creation on resume with identical filters is safe; new
  snapshot deltas surface in the miss lane or as extra empty envelopes.
  Reframing (operator, 2026-08-17): `/assets/export` is de facto an **asset inventory snapshot**, so
  staleness between the asset spine and the vuln feed is inherent to the vendor's model, not a
  consequence of any spool design. The miss lane and re-created-export deltas are therefore permanent
  properties of correlating two independently-snapshotted exports — they must not be treated as defects
  to engineer away, and no backend choice removes them.
  source: same file #A10 (never validated) + operator framing 2026-08-17
  **Verifier 2026-08-17 — NEVER-TESTED, and slice 1 created a new instance of the staleness it describes.**
  The re-creation path exists and resets its counters (`TenableIoAssetSpoolPhase.cs:82-97`) but no test drives
  a mid-spool 404, and the "deltas surface in the miss lane" half is phase 2. New instance: with no
  generation folder, an asset staged by an earlier leg that the re-created export no longer returns lingers in
  the spine and will be swept as a zero-vuln asset — a spurious empty envelope for an out-of-window asset.
  The prior branch's RAM spool could not do this because it started empty every leg. Phase 2 must not assume
  "the spine equals this leg's snapshot".

## New to this task

- **A17 — REJECTED, independently re-verified by the verifier (2026-08-17).** A fresh fetch of
  `developer.tenable.com/reference/exports-vulns-request-export` confirms the top-level body is exactly
  `num_assets`, `include_unlicensed`, `include_software_vulns`, `include_plugin_output`, `properties`,
  `filters`, with **no** asset/asset-UUID array; and the full `filters` key list contains no UUID-keyed asset
  scope — only `cidr_range` and `tag.<category>` scope by asset at all. The executor's disposition holds. —
  That the vulns export can be scoped to a specific set of assets, which would let
  phase 2 be driven from the staged spine (Falcon's shape: walk the frozen key list, query findings per
  asset batch) and remove blind chunk-order correlation entirely.
  It cannot. `POST /vulns/export` has exactly six top-level body properties — `num_assets`,
  `include_unlicensed`, `include_software_vulns`, `include_plugin_output`, `properties`, `filters` —
  and **no** asset/asset-UUID array. Within `filters`, the only asset-scoping keys are `cidr_range`
  ("assets assigned an IP address within the specified CIDR range") and `tag.<category>` ("assets with
  the specified asset tags"); neither accepts a UUID key list. Per-asset vulnerability retrieval exists
  only on the workbenches endpoint (`GET /workbenches/assets/{asset_uuid}/vulnerabilities`), which
  Tenable explicitly steers away from — "Tenable recommends the POST /vulns/export endpoint for large or
  frequent exports of vulnerability data" — and which would cost one request per asset (~108 K at
  reference scale) against 2,168 chunk downloads today.
  **Consequence: A1 stays load-bearing.** Correlation remains chunk-local and blind; it cannot be made
  key-driven. We mirror Falcon's *storage and recovery* mechanisms, not its traversal.
  source: developer.tenable.com/reference/exports-vulns-request-export (top-level body properties and
  the full `filters` property list), developer.tenable.com/reference/workbenches-asset-vulnerability-info
  (executor, 2026-08-17)
- **A18 — OPEN; existence half VALIDATED, the part that matters is unprobed** (verifier, 2026-08-17: the same
  re-fetch confirms a top-level `properties` array exists; whether `asset.uuid` *alone* is selectable was not
  probed) — `POST /vulns/export` accepts a top-level `properties` array: "Specifies an array of
  property names to include in the export. When specified, the export only returns the properties you
  select." If `asset.uuid` alone is selectable, **a cheap uuid-only projection of a vuln chunk exists** —
  which directly contradicts the stated reason the prior task deleted its claimed-set resume rebuild
  ("no uuid projection exists, so it re-downloaded every processed chunk's full bytes"). Not needed under
  the staged spine (the spool survives the leg), but it changes the option space and should be probed
  before anyone re-derives that constraint. `include_plugin_output` and `include_software_vulns` are
  additional payload-size levers on the same endpoint, currently unset by the collector.
  source: developer.tenable.com/reference/exports-vulns-request-export (executor, 2026-08-17)

- **A10 (duplicate id — RETIRE THIS ENTRY) — REJECTED, superseded** (verifier, 2026-08-17: two entries in this
  file share the id `A10`; this one is subsumed by the REJECTED `A10` below, which settles the primitive
  question outright. Keeping both invites a stale reference) — `IntegrationInfra.Ingestion`'s
  `PriorStateStore` is a usable keyed spool at reference
  scale: one object per asset uuid, ~108 K objects per run, read back inside the vuln loop under
  `MaxConcurrentReads = 4`. Unmeasured for latency, cost, and S3 request throttling. This is the
  gating unknown for Fork A option 2.
  Note the *cost/throughput* question this entry raised is still open on its own terms and now belongs to
  the staged spine: ~108 K PUTs per run at `SpineWriteConcurrency = 32` is unmeasured (S1a's remaining
  operational measurement).
- **A11 — VALIDATED** — `Staging/FrozenKeyList` and `Staging/StagingManifest` offer no keyed random access
  (`FrozenKeyList.ReadBatchesAsync` yields sequential batches; `StagingManifest` is a single control
  artifact), so Falcon's staging shape does not by itself satisfy Tenable's join. The keyed primitive is
  `GuardedObjectStore.ReadAllBytesAsync` against an identity-derived key, which is what the spine uses.
  source: `IntegrationInfra/src/IntegrationInfra/Ingestion/Staging/FrozenKeyList.cs` (public surface),
  `StagingManifest.cs`, `GuardedObjectStore.cs:161` (executor, 2026-08-17)
  **Verifier 2026-08-17: citation verified.** `FrozenKeyList`'s entire public surface is `FreezeAsync` +
  `ReadBatchesAsync` (`FrozenKeyList.cs:51,91`) — no keyed access; `StagingManifest` is one control artifact;
  and the keyed primitive `ReadAllBytesAsync` is what the spine actually calls (`TenableIoAssetSpine.cs:106`).
- **A10 — REJECTED** — `PriorStateStore` is usable as the keyed spool. It is not: `ReadAsync<TState>`
  deserializes and `WriteAsync<TState>` serializes through a caller type, which would break the verbatim
  `host` passthrough the correlated envelope requires, and it enforces `MaxControlArtifactBytes` (1 MiB) on
  both sides with an error message stating the intent — "Per-key state is a watermark, not a copy of the
  entity." Do not re-propose it for entity bodies without a raw-bytes path on that type.
  source: `IntegrationInfra/src/IntegrationInfra/Ingestion/Staging/PriorStateStore.cs:48-75` (executor, 2026-08-17)
  **Verifier 2026-08-17: citation verified exactly.** `PriorStateStore.cs:47-75` — `ReadAsync<TState>`
  deserializes, `WriteAsync<TState>` serializes, and the over-cap throw message reads "Per-key state is a
  watermark, not a copy of the entity." No `PriorStateStore` call site exists anywhere under `Collectors/`
  (only two doc-comment mentions explaining the avoidance).
- **A19 — VALIDATED** — Absence of a staged object is reportable as a value, not an exception, so the
  thin-host miss lane needs no 404 handling: `GuardedObjectStore.ReadAllBytesAsync` stats first and returns
  `null` when the object does not exist.
  source: `GuardedObjectStore.cs:161-176`;
  `TenableIoAssetSpineTests.TryRead_ForAnAssetTheSpineDoesNotHold_ReturnsNullRatherThanThrowing`
  (executor, 2026-08-17)
  **Verifier 2026-08-17: citation verified.** `ReadAllBytesAsync` stats first and returns `null` when
  `stat is null` (`GuardedObjectStore.cs:167-171`), and the test double throws `ObjectNotFoundException` from
  `OpenReadAsync` (`InMemoryTenableIoStagingStore.cs:70`) — so the passing test genuinely exercises the
  stat-first path rather than a lenient double. Note this is exactly the mechanism **A12a** endangers.
  **Verifier pass 2, 2026-08-17: narrowed, correctly, by the repair itself.** "Needs no 404 handling" holds for
  *absence*, but `TenableIoAssetSpine.cs:128-144` now documents the one case where the read path is not
  exception-free: under memory pressure the façade tightens the buffered-read cap to
  `MaxControlArtifactBytes` (1 MiB), so a 1–8 MiB record throws `DataPipelineException` instead of returning
  bytes. Measured records are far below that (p95 6.9 KB, max 59 KB), so it is a tail risk — but phase 2 must
  decide whether to treat it as a miss or fail the run.
- **A20 — VALIDATED** — Identity-derived keys plus replace-on-write make re-staging idempotent, which is what
  removes the need for Falcon's per-generation folders: two writes of one asset leave one object carrying the
  latest bytes.
  source: `TenableIoAssetSpineTests.Restaging_AnAsset_OverwritesItsOwnObjectInsteadOfAccumulating`
  (executor, 2026-08-17)
  **Verifier 2026-08-17: citation verified** (asserts 2 writes / 1 key and reads back `v:2`; passing in a
  re-run 28/28). Scope note: this removes the *corruption* reason for generation folders, not their
  discard-the-previous-leg property — see the A9 addendum.
- **A21 — VALIDATED** — The manifest is written strictly last, so it cannot mark a spool complete before its
  assets landed.
  source: `TenableIoAssetSpineTests.SpoolPhase_StagesEveryAssetUnderItsOwnId_AndWritesTheManifestLast`
  asserts the manifest key is the final write and absent from every earlier write (executor, 2026-08-17)
  **Verifier 2026-08-17: citation verified** (`TenableIoAssetSpineTests.cs:272-273`, plus the same assertion
  in `SpoolPhase_WhenAChunkIsFailedServerSide…`; both passing). **Scope correction:** the manifest is written
  last, but it is written *unconditionally* — including when chunks were permanently skipped, at any ratio
  (`TenableIoAssetSpoolPhase.cs:152-171`). So "cannot mark a spool complete before its assets landed" holds
  for ordering and **not** for completeness. See `review/verifier-1.md` D2.
  **Verifier pass 2, 2026-08-17: the completeness half is now bounded against *download failure* and still
  unbounded against *non-delivery*.** `EnforceSkippedChunkCeiling` runs before the manifest write
  (`TenableIoAssetSpoolPhase.cs:92` vs `:96`) and `SpoolPhase_WhenTooManyChunksAreLost_…` asserts no manifest
  exists when it trips. But the ceiling counts only chunks that *failed*: nothing reconciles
  `Processed.Count + Excluded.Count` against `TotalChunks`, and `FindNewChunkIds` only ever attempts chunks in
  `chunks_available`. A `FINISHED` export declaring 4 chunks while listing one produces a quarter-full spine
  with a completion manifest and no warning. Demonstrated by a *passing* test:
  `SpoolPhase_WhenAChunkIsFailedServerSide_…` stubs `total_chunks:4, chunks_available:[1], chunks_failed:[4]`
  and asserts success — chunks 2 and 3 are unaccounted for. See `review/verifier-2.md` N1.
- **A12 — NEVER-TESTED** — `S3AdapterObjectStore` is registered in every environment this collector runs in, not
  only where Falcon runs. Registration is conditional in `IntegrationServiceBus/…/Infrastructure.AWS/
  DependencyInjection.cs:160`; a staged design hard-fails without it.
  **Verifier 2026-08-17:** no ISB registration was read. Sharper point: the "hard-fails at flow entry"
  behaviour is **not yet true of this code** — nothing in slice 1 calls `GuardedObjectStore.Create(services)`,
  where the throw lives (`GuardedObjectStore.cs:82-90`), and nothing calls
  `TenableIoAssetSpine.TryResolveBaseStorageUrl`, whose `null` return is unhandled. See
  `review/verifier-1.md` D4.
  **Verifier pass 2, 2026-08-17: the guards now exist and are proven to throw** —
  `TenableIoAssetSpine.RequireObjectStore` (`:104-109`) and `RequireBaseStorageUrl` (`:91-95`), asserted by
  `RequireObjectStore_WithNothingRegistered_FailsLoudlyRatherThanFallingBack` and
  `RequireBaseStorageUrl_WhenTheRunCarriesNoStorageUrl_FailsLoudly` (the latter over a real
  `AdapterProgressContext.FromPlatformEvent`). **Still not invoked by any production path**, so the claim to make
  is "the guard exists and throws", not "the flow hard-fails" — that becomes true when phase 2 wires the entry
  point, and whatever wires it must call one of these (or `GuardedObjectStore.Create`) and must not hold the raw
  `IAdapterObjectStore` that `RequireObjectStore` returns. The environment-registration half of this assumption
  is still NEVER-TESTED.
- **A12a — NEVER-TESTED, and the sharpest live risk in the slice** (verifier, 2026-08-17: no IAM check
  performed; mechanism confirmed at `GuardedObjectStore.cs:167-171` — `null` is returned *only* because
  `StatAsync` returned `null`, so without the grant every ordinary miss becomes an exception on a hot path) — The pod's principal has **`s3:ListBucket`** on the data bucket.
  The contract is explicit that this is a deployment requirement, not an optimization: "without it a HEAD
  on a missing key answers 403, not 404. Implementations must not paper over that by reporting a
  permission failure as `null` … Throw `ObjectStoreAccessDeniedException` instead."
  **Why this matters more than the delete grant:** our design uses absent-object → `null` → thin-host miss
  lane as a *hot, expected* path — misses occur on every run from snapshot skew and phantom allocations
  (A7, A9). Without `s3:ListBucket`, every ordinary miss surfaces as an access-denied exception instead of
  a miss, so the flow fails rather than degrades. Delete (PA4) only costs storage; this costs correctness.
  Must be confirmed before the staged design runs anywhere real.
  source: `Cymulate.Integration.Client/Contracts/IAdapterObjectStore.cs` StatAsync remarks
  (executor, 2026-08-17)
- **A13 — NEVER-TESTED** (verifier, 2026-08-17: emitter batch-scope release is phase 2; nothing in slice 1
  publishes, and the only `BatchScopedStorage` reference is the read-only `ResolveBaseUrl` at
  `TenableIoAssetSpine.cs:81`) — The `NdjsonBatchEmitter` in the pinned 1.2.0-preview.0 package releases the batch
  scope on a throwing publish as well as on a 0-record page, per `Emission/README.md`. Falcon and
  InsightVmCloud on dev still hand-roll `BeginPage`/`RestoreBase`, so the emitter-owned path is
  documented but only exercised by Qualys in this repo.
- **A14 — MOOT (VALIDATED as irrelevant)** (verifier, 2026-08-17: the RAM overflow path was never written into
  this tree — `grep -rn -i -E "SpoolBudget|overflowMarker|IAssetSpool|OverflowChannelPublisher|
  InMemoryGzipAssetSpool|DrainRemaining"` over the whole repo returns no hits — so no unexercised code was
  carried forward from it) — The RAM spool's overflow path has never executed in any run (requires ~700 K
  in-window assets at the 1 GiB budget). Whatever is carried forward from it is unexercised code.
- **A15 — NEVER-TESTED** (verifier, 2026-08-17: `cymulate-integration-parsers` was not read; S2 pending,
  blocker B2 open. Gates any rollout claim) — The tenable parser today implements the **split two-lane** shape, not the correlated
  shape, so correlated support is a new parser change rather than a pending one. Believed from operator
  memory of a July rfqa validation run; **not** verified against `cymulate-integration-parsers` at
  contract time. Gates any rollout claim (Fork C).
- **A16 — REJECTED** (verifier pass 2, 2026-08-17). It *did* need a structural change. Repair round 1 added
  `Publishing/LocalFileAdapterObjectStore.cs` (194 lines) and a 16-line fallback registration in
  `Program.cs:219-234`, because the concrete `S3AdapterObjectStore` lives in the ISB repo behind `HAS_ISB_AWS`,
  which no committed build defines — so before this, a staged flow could not be driven locally at all. The
  existing `Publishing/S3ObjectStoreWiring.cs` cited as evidence for this assumption is exactly the piece that
  is compiled out. Note the local store deliberately cannot answer **A12a**; its own remarks say to read a green
  local run as "the mechanics are right", never as "this works in the cluster". —
  `LocalAdapterRunner` needs no structural change to drive the rebuilt flow end to end.
  Was OPEN on the prior branch too; its S3 object-store wiring already exists
  (`Publishing/S3ObjectStoreWiring.cs`).

## Operator-closed by production precedent (2026-08-17)

- **A12, A12a, PA4 — CLOSED, not open.** Operator ruling: Falcon's two-phase correlated flow runs this exact
  `IAdapterObjectStore` / `GuardedObjectStore` path in production, exercising the same properties this design
  needs — absence reported as `null` by `StatAsync`, prefix listing, prefix delete. Storage-side registration,
  the `s3:ListBucket` grant, and the delete grant are therefore settled by operational precedent and must not
  be re-raised as risks or release gates.
  source: operator ruling 2026-08-17, grounded in FalconCollector/Flows/Findings/TwoPhase/ running in
  production (operator, 2026-08-17)
