# Constraints

## Scope boundary

- Touch only `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/`
  and its tests under `src/Cymulate.Integration.Adapters/UnitTests/Collectors/Cymulate.Integration.Adapters.Collectors.CortexXdrCollector.Test/`.
- Do NOT modify `Shared/DataPipeline/Ingress/`, `Shared/DataPipeline/Egress/`,
  `Shared/DataPipeline/Json/`, `Shared/Session/`, `Shared/Recovery/`,
  `Shared/Orchestration/`.
- Do NOT modify the SDK contract (`IAdapterDataPublisher`).
- Do NOT modify or extend `DefenderVmRecordFormatter`. Mirror its pattern in a
  new Cortex-local helper.
- Do NOT introduce new shared types or move existing Cortex-internal helpers
  into Shared. The new record formatter stays Cortex-local.
- Do NOT add the `sourceType` stamp to `DefenderVmCollector` records as part
  of this task (it already exists there for the vulnerability/recommendation
  streams; do not regress its collision policy).

## Flow shape

- The findings flow must have exactly three stages in order:
  `FindingsCves` → `FindingsEndpoints` → `Assets`.
- Each stage runs to completion before the next begins.
- Each stage has its own per-stage cursor in `CortexXdrFindingsCheckpointState`:
  - `FindingsCves` → `NextCveIndex`
  - `FindingsEndpoints` → `NextVaEndpointIndex` (new, type `long`)
  - `Assets` → `NextSearchFrom`
- Page numbering: a single `findingsPage` counter spans the va_cves and
  va_endpoints stages (both write to `findings_*.json`). A separate
  `assetsPage` counter for the REST stage. `globalPage` continues to count all
  published pages.
- The `globalPage`, `findingsPage`, and `assetsPage` counters all increment in
  the existing stages exactly as today; the new `FindingsEndpoints` stage
  participates in `globalPage` and `findingsPage`.

## XQL query

- `va_endpoints` query string is exactly:
  ```
  dataset = va_endpoints
  | fields endpoint_id, endpoint_name, cves, severity, severity_score
  | sort asc endpoint_id
  | limit 50000
  ```
- The query is sent via the existing `CortexXdrXqlClient.ExecuteAsync`
  signature: `IAsyncEnumerable<ReadOnlyMemory<byte>>`.

## Dataset-availability probe

- A new `CortexXdrXqlClient.ProbeDatasetAvailableAsync(string dataset, CancellationToken)`
  method (or sibling helper if executor prefers) calls
  `POST /public_api/v1/xql/get_datasets` with body `{"request":{}}` (singular
  `request` per on-prem) and returns `true` when `dataset` appears in the
  response and is not explicitly empty.
- Add the new URL constant to `CortexXdrUrls`.
- When the probe returns `false`, log info ("Cortex XDR va_endpoints dataset
  not available; skipping va_endpoints stage. Likely no Host Insights
  add-on.") and skip the stage. Do NOT fail the flow.
- The probe runs once per `CollectAsync` invocation, immediately before the
  va_endpoints stage. Resume that re-enters `FindingsEndpoints` re-runs the
  probe (cheap, and Host Insights status can change between runs).
- DryRun does NOT call the probe. DryRun continues to exercise only the
  va_cves XQL path + endpoints REST path.

## `sourceType` discriminator

- Field name: `sourceType` (lowerCamel, exact case).
- Field type: JSON string.
- Three values, lowercase:
  - `"va_cves"`
  - `"va_endpoints"`
  - `"endpoint"`
- The field MUST appear as the FIRST property in every published row across
  all three streams.
- Stamping is done via a new
  `CortexXdrCollector/Processing/CortexXdrRecordFormatter.cs` helper that
  takes a source `JsonElement` (or `ReadOnlyMemory<byte>`, executor's call;
  the helper internally uses the `ArrayBufferWriter<byte>` + `Utf8JsonWriter`
  pattern from `DefenderVmRecordFormatter`).
- Field collision policy: if the source row already contains a property
  literally named `sourceType` (case-sensitive ordinal comparison), throw
  `InvalidOperationException` with a message naming the collision and the
  sourceType being stamped. This is the inverse of
  `DefenderVmRecordFormatter`'s silent-skip; the task brief mandates fail-fast.
- The `cves` field on va_endpoints rows passes through as raw bytes — no
  array/object/string normalization, no `expandCveIds` analog in the adapter.

## Checkpoint

- `FindingsCheckpointVersion` constant in `CortexXdrCheckpointHelper` changes
  from `"2"` to `"3"`.
- `CortexXdrFindingsCheckpointState` gains a `NextVaEndpointIndex` (long) init
  property, default `0`.
- `SaveFindingsState` serializes `nextVaEndpointIndex` as a string under the
  key `"nextVaEndpointIndex"`.
- `TryLoadFindingsStateCore` rejects any checkpoint with
  `checkpointVersion != "3"` (including `"2"` and missing values) with a log
  message instructing operator to start a new collection.
- `IsValidFindingsStage` accepts the new triple: `"findingsCves"`,
  `"findingsEndpoints"`, `"assets"` (and rejects the legacy `"findings"`).
- `CortexXdrFindingsStage` constants become:
  - `FindingsCves = "findingsCves"`
  - `FindingsEndpoints = "findingsEndpoints"`
  - `Assets = "assets"` (unchanged)

## Resume semantics

- Resume from a `checkpointVersion = 3` checkpoint where `Stage = FindingsCves`
  re-runs the va_cves XQL and skips `NextCveIndex` rows, then proceeds through
  `FindingsEndpoints` and `Assets`.
- Resume from `Stage = FindingsEndpoints` skips the va_cves stage, runs the
  va_endpoints probe + XQL, skips `NextVaEndpointIndex` rows, then proceeds
  through `Assets`.
- Resume from `Stage = Assets` skips both XQL stages, fetches endpoints from
  `NextSearchFrom`.
- Resume from `checkpointVersion = 2` is rejected — start a new collection.

## Streaming + memory

- Reuse `IngressStream.ReadNdjsonLinesAsync` for the va_endpoints XQL stream
  branch and `IngressStream.Skip` for resume.
- No new buffers beyond the in-page list already used by the va_cves stage.
- Per-row `sourceType` stamping uses the same `ArrayBufferWriter<byte>` +
  `Utf8JsonWriter` pattern `DefenderVmRecordFormatter` uses. One byte[]
  allocation per row, no shared buffer state.

## Tests

- All existing `CortexXdrFindingsFlowTests` assertions are preserved, with
  the following mechanical updates:
  - Stage name literals change from `"findings"` to `"findingsCves"`.
  - The va_cves rows in `findings_NNNNNN.json` now carry
    `sourceType:"va_cves"` as first property. Update relevant
    `.Should().Contain(...)` and JSON property assertions.
  - The endpoint REST rows in `assets_NNNNNN.json` now carry
    `sourceType:"endpoint"` as first property.
  - The expected request sequence on the happy path now includes the
    `xql/get_datasets` probe and the second `start_xql_query` +
    `get_query_results` pair for va_endpoints.
- New tests cover:
  - `sourceType` appears as the first property on each of the three streams.
  - `va_endpoints` rows publish to `findings_*.json` after the va_cves pages
    in stage order.
  - Stage transitions write a checkpoint at each boundary
    (`FindingsCves` → `FindingsEndpoints`, `FindingsEndpoints` → `Assets`).
  - Resume from `Stage = FindingsEndpoints` with `NextVaEndpointIndex > 0`
    skips va_cves XQL, re-issues va_endpoints XQL, and skips already-published
    rows.
  - Host-Insights-missing: probe returns false, va_endpoints stage is skipped,
    flow continues to Assets. No findings_*.json from va_endpoints. Log
    message asserted.
  - v2 checkpoint rejected by `CortexXdrCheckpointHelper.CanResumeFrom` /
    `TryLoadFindingsState` with the documented log message.
  - Fail-fast collision: a vendor row with an existing `sourceType` property
    triggers `InvalidOperationException`.

## Build / verification

- `dotnet build` succeeds for the Cortex XDR collector project.
- `dotnet test` for the Cortex XDR test project passes 100%.
- Pre-commit hooks run on every commit. NO `--no-verify`.
- Format/lint: follow repo conventions used by `2026-05-17_1700_cortex-xdr-ingress-streaming`.

## Non-functional

- No new public types beyond what is strictly required for the stamping helper
  and the probe method. Both can be `internal`.
- Comments: only where the WHY is non-obvious. No restatement of what code
  does. Specifically, document the fail-fast collision policy and the
  Host-Insights graceful-degrade rationale.
- No introduction of new abstractions, interfaces, options classes, or DI
  registrations.
