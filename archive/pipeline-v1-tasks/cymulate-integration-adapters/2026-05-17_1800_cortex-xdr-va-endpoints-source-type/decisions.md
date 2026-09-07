# Decisions

Signed-off design decisions for the Cortex XDR `va_endpoints` stage + `sourceType`
discriminator task. These are inputs to the executor — they are not re-litigated
during execution.

## Stage shape

- Add `va_endpoints` as a third XQL stage in `CortexXdrFindingsFlow`, between the
  existing va_cves stage and the endpoint REST stage. Final flow:
  `FindingsCves` → `FindingsEndpoints` → `Assets`.
- Each stage has its own per-stage cursor and its own checkpoint state. Resume
  re-enters whichever stage the previous run ended in.
- Stage rename: `CortexXdrFindingsStage.Findings` → `FindingsCves`; add
  `FindingsEndpoints`; keep `Assets`. The string literal values are
  `"findingsCves"`, `"findingsEndpoints"`, `"assets"`.

## XQL query

- Exact query:
  ```
  dataset = va_endpoints
  | fields endpoint_id, endpoint_name, cves, severity, severity_score
  | sort asc endpoint_id
  | limit 50000
  ```
- Same 50k cap as the va_cves stage. Same risk acceptance applies (50k truncation
  warning is a separate hygiene item out of scope).

## Publishing

- `va_endpoints` rows publish to `findings_*.json` via the existing
  `CortexXdrFindingsPagePublisher`. Page numbering continues sequentially in
  source order: va_cves pages first, then va_endpoints pages, both under the
  `findings_*` prefix. Upstream routes by `sourceType`, not by file name.
- Endpoint REST rows continue to publish to `assets_*.json` via the existing
  `CortexXdrAssetsPagePublisher`.

## `sourceType` discriminator

- Field name: `sourceType` (lowerCamel).
- Three values (lowercase, snake-case to match XQL dataset naming for the two XQL
  sources):
  - `"va_cves"` on va_cves stage rows
  - `"va_endpoints"` on va_endpoints stage rows
  - `"endpoint"` on endpoint REST rows
- The field MUST appear as the **first** property in every published row.
- All three streams get the field added retroactively in this task. There is no
  outer envelope (no `"type"`/`"data"` wrapper).
- Stamping pattern: mirror `DefenderVmRecordFormatter.AddSourceType` —
  `ArrayBufferWriter<byte>` + `Utf8JsonWriter`, write `sourceType` property
  first, then enumerate source object properties.
- Stamping location: a new local helper
  `CortexXdrCollector/Processing/CortexXdrRecordFormatter.cs`. Not Shared
  (collision policy differs from Defender's; see below).
- Field collision policy: if a vendor row already contains a property literally
  named `sourceType` (case-sensitive byte-level match — Cortex's three sources
  produce no such field today), throw `InvalidOperationException` with a
  message identifying the source. This differs from
  `DefenderVmRecordFormatter.AddSourceType`, which silently skips. The guard
  exists to prevent silent overwrite if vendor schema ever grows a colliding
  field.

## `cves` field passthrough

- The `cves` field on va_endpoints rows passes through as raw JSON bytes.
- The adapter does NOT normalize array/object/string variants here. Upstream
  owns parsing. The on-prem `expandCveIds` logic stays in on-prem.

## Dataset-availability probe

- Run an XQL `get_datasets` probe immediately before the va_endpoints XQL query
  kicks off. Endpoint:
  `POST /public_api/v1/xql/get_datasets` with body `{"request":{}}` (singular
  `request`, matching on-prem `PaloAltoCortexApiBase.FetchXqlDatasetsAsync`).
  Cortex XDR's other XQL endpoints take `request_data`, but `get_datasets`
  is the exception — preserve the on-prem body shape verbatim.
- If `va_endpoints` is missing from the response, or is explicitly empty
  (matching on-prem's `isDatasetReadyForQuery`/`isDatasetExplicitlyEmpty`
  heuristic), log info, skip the FindingsEndpoints stage, transition directly to
  Assets. The va_cves and Assets stages continue normally — do NOT fail the flow.
- The probe lives on `CortexXdrXqlClient` as a new public method. URL knowledge
  and JSON parsing for control responses already live there.

## Checkpoint

- Bump `FindingsCheckpointVersion` from `"2"` to `"3"`.
- Add `NextVaEndpointIndex` (long) to `CortexXdrFindingsCheckpointState` and
  `CortexXdrCheckpointHelper.SaveFindingsState`. Use `long` to match
  `IngressStream.Skip`'s parameter type — semantic alignment with the byte-row
  streaming primitive, even though the 50k cap fits int.
- Resume from a `checkpointVersion = 2` checkpoint is REJECTED as legacy with a
  log message instructing the operator to start a new collection. Same pattern
  the prior staged-findings task used to reject the v1 joined format.
- The legacy-stage-string rejection path (already in `TryLoadFindingsStateCore`
  for v1 joined format) also covers v2 stage names — the rename from
  `"findings"` to `"findingsCves"` would naturally fail `IsValidFindingsStage`,
  but the explicit version-check rejection is the primary guard.

## Streaming + memory

- The va_endpoints stage uses the same streaming primitives as va_cves:
  `IngressStream.ReadNdjsonLinesAsync` (over the XQL stream branch) and
  `IngressStream.Skip(stream, NextVaEndpointIndex)` for resume.
- The inline-branch (results.data path on small results) reuses the existing
  `CortexXdrXqlClient` `Inline` outcome — no special handling needed beyond
  routing the bytes through the new formatter.
- No new buffers, no row materialization beyond the current page (same as the
  va_cves stage post-streaming refactor).

## Sort determinism

- Trust XQL's `| sort asc endpoint_id` for the va_endpoints stage. Same
  risk-accepted basis as the va_cves stage. No SHA-256 tie-breaker. If
  `endpoint_id` collides across rows (which would be a Cortex schema surprise),
  upstream dedup is the fallback.

## DryRun

- DryRun continues to probe **only** the va_cves XQL path + endpoints REST.
  DryRun is a connectivity check, not a feature check. We do not exercise the
  va_endpoints dataset probe or the va_endpoints XQL in dry-run. Surfacing
  Host-Insights absence in dry-run is a follow-up if operator requests it.

## Commits

- One PR. Multiple commits are permitted at the executor's discretion. A
  natural split is:
  - Commit 1: sourceType stamping infrastructure (CortexXdrRecordFormatter +
    its application across the va_cves and endpoint REST streams). Tests:
    sourceType-as-first-property and collision fail-fast.
  - Commit 2: va_endpoints stage addition (XQL query, dataset probe, stage
    rename, checkpoint v3, resume semantics). Tests: new stage transitions,
    Host-Insights-missing path, v2 rejection.
- Both commits must build green and pass tests independently. Executor's call
  on whether to split.

## Out of scope (do not touch)

- `Shared/DataPipeline/Ingress/`, `Shared/DataPipeline/Egress/`,
  `Shared/DataPipeline/Json/` — consumers only, no shape change.
- `Session/`, `Recovery/`, `Orchestration/`.
- The SDK contract (`IAdapterDataPublisher`).
- Upstream consumer code.
- On-prem `expandCveIds` normalization.
- 50k cap warning log (separate hygiene item).
