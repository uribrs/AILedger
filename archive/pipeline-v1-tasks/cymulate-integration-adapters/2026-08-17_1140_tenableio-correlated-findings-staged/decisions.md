# Decisions

## Carried in from prior art — do not re-litigate

- Record grammar `{ uuid, chunk, isLastChunk, findingsInChunk, host, findings[] }`, 2,000-findings cap
  per envelope, `host` on chunk 0 only and omitted rather than null, zero-vuln assets still emit one
  envelope. (`2026-07-05_1802_tenableio-correlated-findings`, mirroring
  `2026-07-02_1705_falcon-correlated-findings-redesign`.)
- Asset key field is `uuid` (Tenable's identifier), not Falcon's `aid`. (same)
- `CollectFindings` drops the separate assets lane; correlated envelopes are the asset spine; standalone
  `CollectAssets` unchanged. (same)
- `host` = full `/assets/export` record verbatim; no field stripping. Payload trim is a later product
  decision. (same)
- Miss lane: thin host synthesized from the finding's embedded `asset` sub-object; counted and logged;
  never dropped, never buffered. Unifies orphans, snapshot skew, hypothetical straddles and
  post-resume gaps. (same)
- `num_assets` default 50, config-overridable — measured chunk p50 22 MB / max 43 MB at 50. (same)
- Both exports created up front; the concurrency limit is 10. (same)
- Zero-vuln sweep runs only after all vuln chunks complete — asset-partitioning makes absence global.
  (same)
- One publish call = one atomic size-unbounded object. Collector-side byte-budget pagination stays
  deleted. (`2026-07-07_1507_egress-atomic-streamed-objects`.)
- Staging, if used, lives inside the run prefix under a `_`-prefixed segment, and a producing-side unit
  test asserts the staging path cannot match `^(assets|findings)`.
  (`2026-08-05_1937_s3-capability-falcon-two-phase-correlation`.)
- Deletion is garbage collection through the separate `IAdapterObjectPruner` contract; the checkpoint
  remains the cursor and correctness never depends on a delete having happened. (same)
- Publish and checkpoint are one step, with nothing awaitable between them. (same)
- A resume position is a coordinate, never an object name; object names come from
  `progressContext.CurrentPage`. (same)

## Taken for this task

- This is a **re-implementation, not a merge or rebase** of
  `origin/feature/tenableio-correlated-findings`. That branch is a specification and an evidence
  record; its code targets the pre-Infra substrate and three of its mechanisms are obsolete.
- Branch `feat/tenableio-correlated-findings-staged`, cut from `dev` at `1f7a2ba6`.
- Batch-scoped storage is opted into through the emitter
  (`NdjsonBatchEmitter.Create(services, batchScopedStorage: true)`). The collector does not name
  `BatchScopedStorage`. Exemplar is Qualys, not Falcon — Falcon's hand-rolled `BeginPage` on dev is the
  older style and is not the pattern to copy.
- The manual `BatchScopedStorage.BeginPage` in the prior branch's `TenableIoCorrelatedPagePublisher` is
  dropped rather than ported.
- The prior branch's `TenableIoOverflowChannelPublisher`, `overflowMarkers` set, and host-less chunk-1
  case exist only to serve the RAM budget's overflow degrade. They are ported **only if** Fork A keeps
  a bounded RAM spool. Under a staged or spilled backend they are deleted, not preserved.
- Scope assumed **adapters-only**, with the parser change recorded as a cross-repo rollout dependency
  rather than a deliverable — matching the prior task's pinned scope. Flagged to the operator for
  confirmation; see Fork C.
- `CollectorVersion` is not set in the csproj. The bump belongs in
  `Collectors/Directory.Build.props` and its magnitude is the operator's call. The output contract
  change and the checkpoint format bump argue for MAJOR; the prior branch proposed 5.0.0.
- Checkpoint format version is bumped and old formats are refused outright. Whether the version string
  stays `"2"` or advances depends on whether the persisted field set changes under the chosen resume
  model — decide at S5, not now.

## Resolved forks

- **Fork A — spool backend: STAGED, one object per asset uuid.** DECIDED by the operator (2026-08-17)
  after the rationale was argued down and rebuilt. The asset spine is staged through
  `GuardedObjectStore` over `IAdapterObjectStore`; it never lives in memory. Candidates keep-RAM,
  inverted-join, and RAM-plus-spill are all dropped.
  - **The deciding argument is single-path-ness, not heap and not resume.** RAM needs a bound; a bound
    needs a behaviour at the bound; that behaviour is either a failure or a degrade, and the operator
    rules out both ("I don't want failures and I don't want fallbacks based on clever architectures,
    those also mean fail points"). RAM therefore cannot be made single-path — it always carries a size
    cliff. A staged spine has no bound to hit, so there is nothing to budget, overflow, or degrade.
    Fewest branches wins.
  - **Two earlier justifications are withdrawn, so they are not re-derived later.** (a) Heap: the
    measured peak was 54.4 MB compressed on the largest tenant and overflow never engaged, so heap
    alone would not justify this. (b) Resume: re-spooling costs ~108 assets chunks / ~435 MB, i.e.
    minutes — the cheap half. The expensive 17 GB vuln side already resumes via
    `processedTenableChunkIds` and staging does not touch it. The earlier framing of re-spool as the
    expensive half was an overstatement.
  - Staging's own risks are preconditions and retries, not runtime alternate paths: a half-written
    spine leaves no manifest and phase 1 is redone; a parser-prefix collision is prevented by a path
    constant plus a test; an absent pruner means storage accumulates (deletion is already off the
    correctness path); an unregistered `IAdapterObjectStore` is a hard stop at flow entry, as in Falcon.
  - **Layout is forced, not chosen: one object per asset**, keyed `_staging/{generation}/spine/{uuid}`.
    Read amplification for any page/bucket layout is bucket-size ÷ record-size, so pages (~200 MB per
    vuln chunk) and hash buckets (~85 MB per chunk) both lose; shrinking buckets to fix it converges on
    one asset per object. The only alternative preserving addressed lookup is a uuid index, which is
    the RAM spool with extra steps.
  - Read cost is bounded by A1: at `num_assets=50` a vuln chunk needs at most 50 addressed GETs — the
    key IS the uuid, so nothing is scanned or listed on the hot path — and claim-on-use means each
    asset is read exactly once per run. **If A1 is false this cost model degrades**, so A1 is
    load-bearing for the staged design's economics as well as its correctness.
  - Consequently deleted, not ported: the compressed budget, the overflow degrade, `overflowMarkers`,
    the host-less chunk-1 case, `TenableIoOverflowChannelPublisher`, and the `IAssetSpool`
    windowed-join escape hatch.
  - **Back-read primitive: `GuardedObjectStore` directly, NOT `PriorStateStore`.** The latter
    serializes/deserializes through `TState`, which would break verbatim `host` passthrough, and its
    own guard states the intent — "Per-key state is a watermark, not a copy of the entity" — while
    capping at `MaxControlArtifactBytes` (1 MiB). Use `WriteAsync(ObjectWriteRequest.FromBytes(...))`
    in and `ReadAllBytesAsync(location, cap, ct)` out; the latter returns `null` for a missing object,
    which is exactly the thin-host miss signal, so no 404 handling reaches the flow.
  - ~~The prior branch's `IAssetSpool` seam (`TryAdd` / `TryTake` / `DrainRemaining`) is the right shape
    and is retained, made async, with the implementation swapped. `BuildRecordsForChunk`'s three-way
    host resolution is unchanged.~~ **NOT TRUE OF THE CODE — corrected 2026-08-17.** No `IAssetSpool`
    interface exists: phase 1 depends on the concrete `TenableIoAssetSpine` directly, and a
    one-implementation interface is exactly the speculative abstraction the constraints forbid. Host
    resolution is also **two**-way, not three — spine hit or thin-host miss — because the overflow
    marker case died with the RAM budget. Recorded as drift rather than edited away, because the
    original sentence is what a later reader would otherwise trust.
  - Write concurrency is the collector's job: the façade's guard is `MaxConcurrentReads` (reads only),
    so phase 1 needs its own bounded write fan-out (~108 K PUTs serially would be ~30 min).
- **Traversal stays vuln-chunk-driven, NOT spine-driven.** Settled 2026-08-17 against the vendor docs
  (assumptions A17, REJECTED): `/vulns/export` cannot be scoped to a set of asset UUIDs, so Falcon's
  traversal — walk the frozen key list, query findings per asset batch — is unavailable here. Phase 2
  is still "stream vuln chunks and look the host up", and correlation stays chunk-local and blind.
  What we take from Falcon is its **storage and recovery** mechanisms, not its traversal. Anyone reading
  the Falcon flow as a template must not port `FalconFrozenKeyList`-style batch-driven enrichment.
- **Fork B — resume model: Falcon's.** Follows from Fork A. Manifest as the phase-1 completion proof,
  a coordinate resume position (never an object name), publish-and-record as one step, staged pages
  GC'd strictly after the checkpoint that retires them, and the orphaned-leg guard that fails rather
  than silently re-collecting. The prior branch's full-re-spool / at-least-once model is superseded.

## Open forks — operator-gated
- **Fork C — parser ordering.** Unresolved. Correlated output requires per-uuid replay-idempotent
  aggregation on the parser side, and the parser is believed to have shipped the split two-lane shape
  instead. S2 settles it by reading `cymulate-integration-parsers`.

## Proceeding on unverified

- `Proceeding on unverified: vuln export chunks are asset-complete, so correlation can stay chunk-local.
  If wrong: an asset's findings split across chunks produce a second thin chunk-0 host and a duplicate
  isLastChunk, and correctness then depends entirely on parser per-uuid aggregation.`
- `Proceeding on unverified: the tenable parser does not yet consume the correlated shape. If wrong in
  the other direction — it already does — Fork C's rollout dependency disappears and S2 is cheap.
  If right, this collector cannot reach production until a parser change ships first.`
- `Proceeding on unverified: S3AdapterObjectStore is registered wherever this collector runs. If wrong:
  a staged spool backend hard-fails at flow entry in that environment, by design and with no fallback.`
- `Proceeding on unverified: a keyed staged spool is affordable at ~108K objects per run. If wrong:
  Fork A option 2 is dead on request cost or latency and the choice collapses to (1) or (4).`
- `Proceeding on unverified: the measured 54.4 MB compressed spool peak is representative of the worst
  tenant. If wrong: option (1) reintroduces exactly the unbounded-heap risk the substrate now solves.`

## Operator rulings, 2026-08-17 (post repair round 1)

- **S3 concerns are closed, not deferred.** The operator: *"about anything S3 related — no need to raise
  these concerns — falcon uses this code in production."* Falcon's two-phase correlated flow runs the same
  `IAdapterObjectStore` / `GuardedObjectStore` path in production, and it depends on the same properties this
  design does — absence-as-`null` from `StatAsync` (its manifest probe and `ExistsAsync` resume primitive),
  prefix listing, and prefix delete. Production use is the evidence. So **A12** (store registered),
  **A12a** (`s3:ListBucket` so a miss answers 404 rather than 403) and **PA4** (delete grant) are settled by
  operational precedent and are not to be raised again as risks or gates.
  Consequence for reporting: the staged design's remaining risks are vendor-side and parser-side, not
  storage-side.
- **Version: the recommendation stands and the bump will happen.** MAJOR relative to the current
  `Collectors/Directory.Build.props` default of `6.2.3` — i.e. `7.0.0` — for the output-contract change plus
  the checkpoint format bump. Still not set from here: it belongs in `Directory.Build.props` and the act is
  the operator's.
- **Phase 1 is to be completed fully and phases 2–3 run forward through the pipeline** rather than stopping
  at the slice boundary. Wiring the correlated flow into `TenableIoCollector` therefore becomes in scope, and
  with it the local end-to-end run that the unwired state had made structurally impossible.

## Open decisions surfaced by phase 2, recorded rather than silently taken

- **The vulns export is never re-created mid-pass**, deliberately unlike the assets export. Chunk ids are only
  meaningful inside one export, so a replacement invalidates every processed id the run recorded; a 404 is
  therefore terminal with "a fresh collection is required". The spec described re-creation for phase 1 and was
  silent on phase 2 — treating them alike would have been a correctness bug.
- **A spine lookup that THROWS fails the chunk; it does not degrade to a thin host.** Absence is a value
  (`null`); a store that is refusing or unreachable is not. The consequence is intended: a systemically failing
  store fails the run instead of quietly converting a whole tenant's hydrated hosts into thin ones. Currently
  **untested** — see the test-gap list.
- **A spine may be reused with a non-zero `SkippedChunkCount`.** `ResolveExportsAsync` re-spools when the
  manifest's base date does not match the leg's, but not when the manifest records skipped chunks. Those assets
  become thin-host misses, which is the designed degradation — but this is an open decision, not a closed one.
- ~~**Resume produces duplicate empty-findings envelopes by construction.** Claims live in RAM, so a resumed leg
  has not claimed the uuids of chunks it skipped, and the sweep re-emits them. They carry no findings and are
  absorbed by per-uuid aggregation; the count is logged. This is accepted behaviour and must not be "fixed"
  later as a bug.~~
  **FALSE AS OF 2026-08-17 — SUPERSEDED, and the original wording was dangerous.** It told a future reader to
  preserve as intended behaviour something that was in fact a defect: the re-emitted envelope collided at the
  same `(uuid, chunk)` key with the earlier leg's REAL chunk 0, so whether findings survived depended on the
  parser's aggregation tie-break — a rule this repo cannot see. "Absorbed by per-uuid aggregation" was an
  assumption about someone else's code, not a property of ours.
  Replaced by a **durable claim ledger**: one marker object per published vuln chunk at
  `_staging/claims/chunk_{id}.ids`, newline-delimited asset ids (safe without a parser because the id validator
  rejects control characters), written BEFORE the chunk's data object. Ordering is the guarantee — a crash
  between marker and data leaves a claim with no data (safe: skipped, then re-emitted when the chunk re-runs)
  rather than data with no claim (unsafe: the sweep would contradict published findings). A marker is written
  for **every** processed chunk including empty ones, so "processed but no marker" stops being an ordinary case
  and becomes a usable anomaly signal that suppresses the sweep. Cost: one PUT per chunk against ~100K spine
  PUTs, and one GET per already-published chunk per resumed leg.
- **"Byte verbatim" means value-verbatim.** `NormalizedUtf8Json` escapes non-ASCII to `\uXXXX` on the way in —
  the same normalizer the passthrough flow used, so no regression — and the envelope then embeds those bytes
  untouched. Both halves are pinned by tests.
- **No vuln-complete checkpoint between phases 2 and 3.** The prior branch wrote a zero-record `AdvancePage(0,0)`
  snapshot there; it was dropped because the last chunk's checkpoint already carries every processed id, so a
  sweep-time crash re-enters phase 2 with nothing to download and re-sweeps. The snapshot only shifted page
  numbering.
