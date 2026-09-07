Role:
You are a senior .NET engineer working on Cymulate's integration adapters, responsible for the
TenableIo collector's data plane and its recovery plane.

Goal:
Re-implement the correlated (asset-driven) `CollectFindings` flow for the TenableIo collector on
current dev mechanisms, so that one run emits self-complete correlated NDJSON envelopes
`{ uuid, chunk, isLastChunk, findingsInChunk, host, findings[] }` to a single findings lane instead of
two independent passthrough lanes, with a spool backend and resume model chosen on evidence rather
than ported from the pre-Infra design.

Context:
- Base: branch `feat/tenableio-correlated-findings-staged` off `dev` at `1f7a2ba6`.
- The technique already exists, fully built and run against the largest tenant, on
  `origin/feature/tenableio-correlated-findings` @ `999289e0`. It is a specification and an evidence
  record, not code to merge — it targets the pre-IntegrationInfra substrate.
- TenableIo on dev is functionally that branch's merge base: two-lane combined run,
  `NumAssetsPerChunk = 500`, no batch-scoped storage, per-chunk byte-budget pagination.
- Read `research/branch-technique-and-dev-diff.md` before writing code. It carries the branch's
  technique phase by phase, the mechanical rename map, the four architectural changes on dev, the
  three open forks, and the validated vendor facts.
- Read `constraints.md` and `decisions.md` in full. Several things in the prior branch are
  deliberately **not** to be ported.
- Three substrate facts drive the rebuild: batch scoping is now emitter-owned; `IAdapterObjectStore`
  now gives collectors S3 read **and** write behind `IntegrationInfra.Ingestion`'s
  `GuardedObjectStore`; and Falcon's correlated flow was rebuilt as a two-phase staged design whose
  recovery discipline (manifest as completion proof, coordinate position, orphaned-leg guard,
  publish-and-record as one step, GC-after-checkpoint) is the current reference.
- Reference implementations to read, in this order:
  `FalconCollector/Flows/Findings/FalconFindingsFlow.cs` and `Flows/Findings/TwoPhase/*` (staged
  correlated mechanism and its recovery), `QualysCollector/Flows/Findings/QualysFindingsBatchPublisher.cs`
  (emitter-owned batch scoping), `TenableIoCollector/Flows/Findings/TenableIoFindingsFlow.cs`
  (the current two-lane flow being replaced), `IntegrationInfra/src/IntegrationInfra/Emission/README.md`
  and `Ingestion/README.md`.

Constraints:
- Every constraint in `constraints.md` applies. It is not a summary — read it.
- Adapters repo only. No `IntegrationInfra` changes. Substrate pinned at 1.2.0-preview.0.
- The record grammar, the 2,000-findings cap, `host`-on-chunk-0-only-and-omitted-otherwise, verbatim
  passthrough of both `host` and `findings`, and the zero-vuln empty envelope are fixed and not open
  for redesign.
- Standalone `CollectAssets` is unchanged, including its own pagination.
- One publish call = one atomic size-unbounded object. Do not reintroduce collector-side byte-budget
  pagination. `MaxBytesPerBatch` is in-flight part discipline only.
- Opt into batch-scoped storage through `NdjsonBatchEmitter.Create(services, batchScopedStorage: true)`.
  The collector must not name `BatchScopedStorage`.
- If the chosen backend stages: go through `GuardedObjectStore` only, one instance per run; stage under
  a `_`-prefixed segment inside the run prefix; assert in a unit test that the staging path cannot match
  `^(assets|findings)`; treat a missing `IAdapterObjectStore` as a hard failure with no fallback;
  deletion is GC via `IAdapterObjectPruner` and correctness never depends on it.
- Publish and checkpoint are one step. A resume position is a coordinate, never an object name.
- Bump the checkpoint format version and refuse foreign/older formats outright. Old two-lane and
  `CombinedRun` checkpoints force a fresh collection.
- Do not set `CollectorVersion` in the csproj, and do not choose the bump — surface a recommendation.
- Do not port the overflow degrade machinery (`TenableIoOverflowChannelPublisher`, the marker set, the
  host-less chunk-1 case) unless Fork A's outcome keeps a bounded RAM spool.
- Do not re-derive the carried vendor facts in `assumptions.md` A1–A9 from scratch. Confirm cheaply or
  accept the citation.

Execution Rules:
- Do not assume missing data. Respect constraints strictly.
- **Fork A is decided and fully specified: the asset spine is STAGED, one object per asset uuid**, at
  `_staging/{generation}/spine/{uuid}`, through `GuardedObjectStore` — never `PriorStateStore` (it
  round-trips through `TState` and would break verbatim `host` passthrough). Writes go through
  `ObjectWriteRequest.FromBytes`; reads through `ReadAllBytesAsync`, whose `null` return IS the
  thin-host miss signal. Fork B follows: Falcon's manifest-as-completion-proof plus a coordinate resume
  position. The deciding argument was single-path-ness — RAM needs a bound, a bound needs a behaviour,
  and both failure and degrade are ruled out. Do not re-litigate; do not reintroduce a budget.
- The key IS the uuid, so a host lookup is one addressed GET. Nothing on the hot path scans or lists.
  The only listing is once, for the sweep: enumerate the spine prefix and skip claimed uuids.
- Claim tracking is in-RAM (~4 MB of uuids); deletion stays pure GC and never gates correctness.
- Phase 1 needs its own bounded write fan-out — the façade guards reads only.
- The overflow degrade machinery is deleted, not ported: the compressed budget, `overflowMarkers`, the
  host-less chunk-1 case, `TenableIoOverflowChannelPublisher`, and the `IAssetSpool` windowed-join
  escape hatch all exist to serve a RAM budget that no longer governs.
- S2 (parser shape) remains operator-gated.
- A1 is load-bearing twice over — for correctness (chunk-local correlation) and for the staged
  backend's read cost (at most 50 keyed lookups per vuln chunk, never a scan). The vendor doc implies
  it but does not guarantee it, so the miss-lane tolerance must survive the rebuild intact.
- Prefer deleting the prior branch's workarounds over porting them. Complexity that exists to work
  around a problem the substrate now owns is not carried forward.
- When an assumption is settled, update `assumptions.md` with the status, the actor and the citation,
  and update `state.json`.
- Run tests at phase boundaries only. Build, then `dotnet vstest` against `artifacts/bin/ut/...` —
  `dotnet test` hangs in this harness. Never run the Falcon suite wholesale, nor the ISBLoad or Dummy
  suites.
- Fix defects surfaced by review directly during execution; reserve questions for genuine forks.

Success Criteria:
- `CollectFindings` publishes correlated `findings_*.json` envelopes only, matching the fixed grammar
  byte-for-byte in shape, and no assets lane.
- Every distinct in-window asset appears on exactly one chunk-0 host-bearing envelope per run, whether
  it has findings, has none (sweep), or missed the spool (thin host). No asset is silently dropped.
- One atomic object per vuln chunk; one per sweep; the chunk is marked processed in the checkpoint only
  after its object materializes.
- Batch scoping is emitter-owned; published objects land under `batch_{page:D6}` with the deterministic
  `instanceBatchId` announced, and a dud or throwing publish announces no batch folder.
- Resume behaves per the signed-off Fork B outcome, with a stated and tested duplicate-emission
  guarantee. Old-format and `CombinedRun` checkpoints are refused with a log line, forcing a fresh run.
- The spool backend is the signed-off Fork A outcome, and the machinery its choice obsoletes is deleted
  from the tree rather than left unused.
- Solution builds with 0 warnings and 0 errors; the TenableIo test project is green via vstest;
  new tests cover host resolution three ways, 2,000-cap slicing, checkpoint refusal, batch layout, and
  the staging-path guard if staging is used.
- `TenableIoCollector/Documentation/01-05`, `ai/skills/collector-flow-patterns`, and
  `ai/skills/collector-recovery` reflect the implemented design.
- A LocalAdapterRunner `CollectFindings` run completes end to end against the real tenant, with
  envelope, asset, finding and miss counts reported for the operator's comparison.
- Fork C is resolved with a citation, and the rollout ordering it implies is stated explicitly.

Output Format:
- Code changes in the repo on `feat/tenableio-correlated-findings-staged`.
- `research/<topic>.md` per investigation (Fork A assessment, Fork C parser shape).
- `assumptions.md` updated with statuses, actors and citations.
- `execution_notes.md` recording what was built, what was deleted and why, and the run evidence.
- `state.json` kept current on every step, blocker and assumption transition.
- A closing report: what changed, what the two gates decided, test and build results, and the
  rollout dependency.

Stop Conditions:
- When the goal is achieved and every success criterion is met.
- When required data is missing.
- At the S1 and S2 operator gates, before implementing the choices they decide.
- If the chosen spool backend turns out to require an `IntegrationInfra` change.
- If a fix causes more errors than it resolves — revert and report.
