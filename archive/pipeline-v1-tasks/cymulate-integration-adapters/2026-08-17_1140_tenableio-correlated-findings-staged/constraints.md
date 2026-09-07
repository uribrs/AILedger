# Constraints

## Repo boundaries

- Adapters repo only. No changes under `IntegrationInfra` — the substrate is consumed as
  `Cymulate.IntegrationInfra` 1.2.0-preview.0, pinned in `Directory.Packages.props`.
- `Cymulate.IntegrationInfra` is the only Cymulate package this project may declare; `Integration.Client`
  and `Http.Package.Session` arrive transitively and must not appear in any `.csproj`.
- Do not re-implement the `AdapterBusEntrypointRunner` pipeline in the collector. The substrate owns
  sequencing, session lifecycle, the DONE payload and default error/cancellation handling.
- Boundary heuristic: decides what to do next ⇒ Conducting/Recovery; classifies a failure or picks a
  wait ⇒ FaultGovernance; reads/transforms/writes a record ⇒ Kernel/Emission; about the wire ⇒
  Conversation.

## Output contract

- Record grammar is fixed: `{ uuid, chunk, isLastChunk, findingsInChunk, host, findings[] }`.
  `host` on chunk 0 only, OMITTED (not null) otherwise. 2,000-findings cap per envelope. Zero-vuln
  assets emit one empty envelope. No field stripping on `host` or `findings` — verbatim passthrough.
- `CollectFindings` publishes `findings_*.json` only. No assets lane. Standalone `CollectAssets`
  unchanged, including its own pagination.

## Egress

- One publish call = one atomic object; objects are size-unbounded and stream as multipart parts.
  `MaxBytesPerBatch` is in-flight part discipline only — never treat it as an object-size cap, and do
  not reintroduce collector-side byte-budget pagination.
- Publish through `NdjsonBatchEmitter` (or `ResultsBatchPublisher`), never ad hoc files or payload
  shapes. Never publish through the `Ingestion` façade — that is the read side, and doing so skips
  NDJSON validation and object atomicity.
- Batch-scoped storage is opted into as `NdjsonBatchEmitter.Create(services, batchScopedStorage: true)`.
  The emitter owns the scope lifecycle; the collector must not name `BatchScopedStorage`.
- Caller-side limits that the emitter cannot enforce: page N+1 must not publish before page N's
  `AdvancePage`, and scoped publishes sharing one `AdapterProgressContext` must be sequential.
- `instanceBatchId` stays the deterministic UUIDv5 of `{base}/batch_{page:D6}` under the frozen
  namespace. Never a random UUID.

## Staging (if the chosen spool backend stages)

- Reach the object store only through `GuardedObjectStore`; never the raw `IAdapterObjectStore`.
  One instance per run, shared — the concurrency limit lives on the instance.
- The store arrives from host DI. A collector must never construct a cloud SDK client. A missing
  `IAdapterObjectStore` is a hard failure, not a silent fallback.
- Staging lives inside the run prefix, under a `_`-prefixed segment (`{storageUrl}/_staging/...`).
  A leading `_` is the reserved marker for non-parser artifacts.
- The staging path constant must not match `^(assets|findings)` — parser input discovery is an
  undelimited prefix match, so `assets_staging/` would be ingested as final input. Assert this in a
  unit test on the producing side.
- Deletion is garbage collection via `IAdapterObjectPruner`, never a cursor. Correctness must never
  depend on a delete having happened. A deployment without delete permission simply has no pruner.
- No conditional write exists in the contract: read-modify-write is last-writer-wins, and
  single-writer-per-key is the caller's responsibility.

## Recovery

- Publish and checkpoint are one step: nothing awaitable or cancellable between the successful publish
  and the checkpoint write, so an object never exists without the position that covers it.
- Bump the checkpoint format version and refuse foreign/older formats outright (no cross-version
  resume). Old two-lane and `CombinedRun` checkpoints force a fresh collection.
- Never persist a position that is also an object name. Object names come from
  `progressContext.CurrentPage`; a resume position is a coordinate.
- Checkpoint staleness stays at the existing ~24 h rule (conservative bound on Tenable chunk retention).
- The recovery budget (`attemptCount`) rides the same checkpoint `AdapterState` as the watermark;
  `MaxRetries` is a lifetime bound across resumes. Only the executor enforces budget exhaustion —
  policies must never gate deferred recovery on a raw attempt count.
- Transient retries stay in-process inside the runner's Polly loop so a long scan keeps its watermark.
  Never sleep in-process for vendor-supplied delays above the threshold; return
  `AdapterResult.PartialResult(...)` and let ISB schedule the resume.
- Existing resilience wiring stays: `MappedFailurePolicy` + `TenMinuteSingleShot` transient backoff,
  in-process server-delay threshold 60 s, session retry / rate-limiter / circuit-breaker config.

## Vendor requests

- Vulns export: `since=baseDate`, `state=[OPEN,REOPENED]`, `num_assets` default 50 (bounds 50–5000).
- Assets export: `chunk_size=1000`, `filters.last_assessed=baseDate`.
- Always send the time filters — the 30-day default trap.
- Both exports created up front so Tenable prepares them concurrently (limit: 10 per container).
- Keep the existing 409 `active_job_id` reuse, progressive chunk loop, per-chunk retry/exclusion, and
  the `MaxSkippedChunkRatio` guard.

## Conventions

- Weigh behaviour over shape: existing machinery carries over unless the redesign obsoletes it.
- Naming/folders mirror the existing collector layout (`Flows/`, `Processing/`, `Recovery/`, `Dtos/`).
- Opt-in capabilities are hardcoded constants on the collector's `*Configuration` record — never a
  backend/payload rollout knob.
- No `CollectorVersion` in the csproj. Version defaults live in `Collectors/Directory.Build.props`, and
  the bump is the operator's call — suggest, do not set.
- No identifier may be named "Legacy". Name for behaviour; put "temporary" in a doc comment.
- `Nullable` and `ImplicitUsings` are on. Internals reach tests via `InternalsVisibleTo`.
- Central package management: `PackageReference` without a version; versions in
  `Directory.Packages.props`.
- C# style: small methods, helpers over long procedural bodies, mirror neighbouring collectors, no
  speculative abstractions.

## Process

- Tests: xUnit + Moq + FluentAssertions.
- `dotnet test` hangs in this harness — build, then `dotnet vstest` against `artifacts/bin/ut/...` dlls.
- Test cadence at phase boundaries only; parallel workers must partition by file touch so they do not
  collide.
- Never run the FalconCollector suite wholesale (risky simulations hang) — filter to change-relevant
  test classes. Never run the ISBLoad or Dummy collector suites.
- Docs sync is part of done: `TenableIoCollector/Documentation/*`, `ai/skills/collector-flow-patterns`,
  `ai/skills/collector-recovery`, and `Collectors/README.md` if it names the TenableIo output shape.
- NuGet restore needs AWS CodeArtifact auth; a 401/403 on `Cymulate.*` is an auth issue, not a code
  issue.
