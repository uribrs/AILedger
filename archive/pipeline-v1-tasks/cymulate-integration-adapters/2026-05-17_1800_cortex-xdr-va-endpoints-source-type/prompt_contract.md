Role:
You are a senior .NET collector engineer working in the `cymulate-integration-adapters` repository, extending the Cortex XDR findings flow.

Goal:
Add a third XQL stage (`va_endpoints`) to `CortexXdrFindingsFlow` and stamp a `sourceType` discriminator on every published row across the three Cortex findings/assets streams. Bump the findings checkpoint to `version = 3` and reject prior versions as legacy. Land in one PR; multiple commits permitted at executor's discretion.

Context:
- Today the Cortex XDR findings flow has two upstream-hydration stages: `va_cves` rows over XQL → `findings_*.json`, and endpoint REST rows → `assets_*.json`. Stage state machine is `Findings → Assets`.
- The on-prem reference (`/Users/user/Dev/AgentService/Source/CybiCollectors/CortexXdrCollector`) additionally queries XQL's `va_endpoints` dataset to provide an authoritative `endpoint_id`-keyed CVE→endpoint mapping. The cloud adapter currently discards that source; upstream is reduced to reverse-deriving the mapping from `va_cves.affected_hosts → endpoint_name`, which loses endpoints absent from `va_cves` and `va_endpoints` CVEs absent from `va_cves`.
- This task adds the missing producer side: a new `FindingsEndpoints` stage between `FindingsCves` and `Assets`, plus a `sourceType` field on all three published streams so upstream can route by source rather than infer it.
- Pattern reference: `src/Cymulate.Integration.Adapters/Collectors/DefenderVmCollector/Processing/DefenderVmRecordFormatter.cs` — specifically the `AddSourceType` helper. Mirror the byte-level `ArrayBufferWriter<byte>` + `Utf8JsonWriter` pattern that writes `sourceType` as the first property and then copies the rest. NOT the `Wrap` envelope.
- Memory: reuse `IngressStream.ReadNdjsonLinesAsync` + `IngressStream.Skip` from `Shared/DataPipeline/Ingress/`. The va_cves stage already streams; the new va_endpoints stage follows the same pattern. No new buffering, no row materialization beyond the current page.
- `decisions.md`, `assumptions.md`, and `constraints.md` in the task directory contain the full design ground truth. Read them.

Constraints:

* Touch only the Cortex XDR collector and its test project. Do NOT modify `Shared/DataPipeline/*`, `Shared/Session/`, `Shared/Recovery/`, `Shared/Orchestration/`, or the SDK contract.
* The new stage must run in order: `FindingsCves → FindingsEndpoints → Assets`. Rename the existing `CortexXdrFindingsStage.Findings` to `FindingsCves`; add `FindingsEndpoints`; keep `Assets`. String literal values: `"findingsCves"`, `"findingsEndpoints"`, `"assets"`.
* `va_endpoints` XQL query is exactly:
  ```
  dataset = va_endpoints
  | fields endpoint_id, endpoint_name, cves, severity, severity_score
  | sort asc endpoint_id
  | limit 50000
  ```
* Add a dataset-availability probe to `CortexXdrXqlClient` — `POST /public_api/v1/xql/get_datasets` with body `{"request":{}}` (singular `request`, matching on-prem `PaloAltoCortexApiBase.FetchXqlDatasetsAsync`; this endpoint is the exception to Cortex's usual `request_data` envelope). Run it once per non-dry-run invocation, immediately before the va_endpoints stage kicks off (after the va_cves stage completes; or before the stage runs on a resume into `FindingsEndpoints`). If the probe returns `false` (dataset missing or explicitly empty per on-prem's `isDatasetReadyForQuery` heuristic), log info ("Cortex XDR va_endpoints dataset not available; skipping va_endpoints stage. Likely no Host Insights add-on."), skip the stage, transition directly to `Assets`. The va_cves and Assets stages continue normally — do NOT fail the flow.
* `va_endpoints` rows publish to `findings_*.json` via the existing `CortexXdrFindingsPagePublisher`. Page numbering is sequential under the `findings_*` prefix: va_cves pages first (in stage order), then va_endpoints pages.
* Endpoint REST rows continue to publish to `assets_*.json` unchanged in routing.
* `sourceType` field: lowerCamel name, JSON string value, stamped as the FIRST property of every published row. Three values: `"va_cves"`, `"va_endpoints"`, `"endpoint"`. Apply retroactively to all three streams in this task.
* Stamping helper: new internal `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Processing/CortexXdrRecordFormatter.cs`. Use the `ArrayBufferWriter<byte>` + `Utf8JsonWriter` pattern from `DefenderVmRecordFormatter.AddSourceType` — write `sourceType` first, then enumerate the source row's object properties.
* Field collision policy: if the source row already contains a property literally named `sourceType` (case-sensitive ordinal comparison), throw `InvalidOperationException` with a message identifying both the colliding property and the sourceType being stamped. This DIFFERS from `DefenderVmRecordFormatter.AddSourceType`, which silently skips. Do NOT add a corresponding fail-fast to `DefenderVmRecordFormatter` — that helper has its own caller expectations.
* The `cves` field on `va_endpoints` rows passes through as raw JSON bytes. The adapter does NOT normalize array/object/string variants. Upstream owns parsing.
* Checkpoint: bump `FindingsCheckpointVersion` from `"2"` to `"3"`. Add `NextVaEndpointIndex` (type `long`) to `CortexXdrFindingsCheckpointState`. Extend `SaveFindingsState` to write `nextVaEndpointIndex` and `TryLoadFindingsStateCore` to load it. `IsValidFindingsStage` accepts only the new triple `"findingsCves"`/`"findingsEndpoints"`/`"assets"`. Reject a `checkpointVersion = 2` (or older, or missing) checkpoint with a clear log message ("Cortex XDR findings checkpoint version '{v}' is from a previous staged-flow version and cannot be safely resumed. Start a new collection.").
* Resume semantics: from each stage, re-enter that stage with the corresponding cursor and continue through subsequent stages. From `FindingsCves` with `NextCveIndex = k`: skip first `k` va_cves rows, run va_endpoints stage, run Assets stage. From `FindingsEndpoints` with `NextVaEndpointIndex = k`: skip va_cves stage entirely, run probe + va_endpoints with first `k` rows skipped, run Assets. From `Assets` with `NextSearchFrom = k`: skip both XQL stages, fetch endpoints from offset `k`.
* DryRun preserves today's behavior: drain one row of the va_cves XQL dry-run query + fetch one endpoints page. Do NOT add a va_endpoints dataset probe or va_endpoints XQL to dry-run.
* Trust XQL's `| sort asc endpoint_id` for va_endpoints determinism. No SHA-256 tie-breaker. Same risk-acceptance basis as the va_cves stage.
* Reuse `IngressStream.ReadNdjsonLinesAsync` for the va_endpoints stream branch and `IngressStream.Skip(stream, NextVaEndpointIndex)` for resume. No new buffering beyond the existing per-page list.
* All existing `CortexXdrFindingsFlowTests` assertions must remain green after mechanical updates for the stage rename and the new sourceType prefix.
* New tests required (executor MAY add more if useful):
  - sourceType appears as the first property on every published row across all three streams.
  - va_endpoints rows publish to `findings_*.json` after va_cves pages in stage order, with `sourceType:"va_endpoints"`.
  - Stage transitions emit checkpoints at each boundary: `FindingsCves → FindingsEndpoints` and `FindingsEndpoints → Assets`.
  - Resume from `Stage = FindingsEndpoints` with `NextVaEndpointIndex > 0` skips va_cves XQL, re-issues va_endpoints XQL, skips already-published rows, then proceeds through Assets.
  - Host-Insights-missing path: probe returns false, va_endpoints stage skipped, flow completes through Assets, no findings_NNNNNN.json from va_endpoints, info log present.
  - `CortexXdrCheckpointHelper` rejects a `checkpointVersion = 2` checkpoint via `CanResumeFrom` / `TryLoadFindingsState` with the documented log message.
  - `CortexXdrRecordFormatter` fail-fast: a vendor row carrying a `sourceType` property triggers `InvalidOperationException`.
* No new public types beyond what is strictly required. `CortexXdrRecordFormatter` can be `internal`. The probe method on `CortexXdrXqlClient` is `internal`.
* No `--no-verify` on commits. Pre-commit hooks must pass.
* One PR. Multiple commits permitted; executor decides whether to split (a natural split is sourceType stamping infrastructure first, then va_endpoints stage).

Success Criteria:

* `CortexXdrFindingsFlow.CollectAsync` runs three stages in order on the happy path, with the `va_endpoints` XQL stage inserted between the existing va_cves and endpoint REST stages.
* `CortexXdrXqlClient` exposes a dataset-availability probe method that issues `POST /public_api/v1/xql/get_datasets` and returns true/false per on-prem's `isDatasetReadyForQuery` heuristic. `CortexXdrUrls` gains a new constant for the endpoint.
* `va_endpoints` rows are published to `findings_*.json` via `CortexXdrFindingsPagePublisher`. Page numbers continue sequentially after the va_cves pages.
* Every published row across all three streams has `sourceType` as its first JSON property with the correct lowercase value (`"va_cves"`, `"va_endpoints"`, `"endpoint"`).
* `CortexXdrRecordFormatter.cs` exists with an internal helper that mirrors `DefenderVmRecordFormatter.AddSourceType`'s `ArrayBufferWriter<byte>` + `Utf8JsonWriter` pattern, but throws `InvalidOperationException` on `sourceType` field collision.
* `CortexXdrFindingsCheckpointState` has a new `NextVaEndpointIndex` (long) property.
* `CortexXdrCheckpointHelper.FindingsCheckpointVersion = "3"`. `SaveFindingsState` writes `nextVaEndpointIndex`. `TryLoadFindingsStateCore` rejects v2 and loads `nextVaEndpointIndex` for v3.
* `CortexXdrFindingsStage` constants are exactly `FindingsCves = "findingsCves"`, `FindingsEndpoints = "findingsEndpoints"`, `Assets = "assets"`.
* `IngressStream.ReadNdjsonLinesAsync` and `IngressStream.Skip` are used in the va_endpoints stage for the stream branch and resume; no new buffers added to the flow.
* The Host-Insights-missing path skips the va_endpoints stage without throwing; flow completes normally through Assets.
* Resume from each of the three stages re-enters the correct stage with the correct cursor.
* DryRun behavior unchanged: va_cves XQL probe + endpoints REST probe; no va_endpoints probe or query.
* `dotnet build` succeeds for the Cortex XDR collector project.
* `dotnet test` for the Cortex XDR test project passes 100% (all pre-existing assertions preserved + all new tests green).
* Pre-commit hooks pass on every commit. No `--no-verify`.
* No new public types beyond what's strictly required. No modifications to `Shared/DataPipeline/*`, `Shared/Session/`, `Shared/Recovery/`, `Shared/Orchestration/`, or the SDK contract.

Execution Rules:

* Do not assume missing vendor behavior. The `xql/get_datasets` probe response shape comes from the on-prem reference (`PaloAltoCortexApiBase.FetchXqlDatasetsAsync` + `findDataset`/`isDatasetExplicitlyEmpty`). Mirror the on-prem heuristic exactly for "is the dataset ready to query".
* Respect constraints strictly. Do not introduce new abstractions; do not move helpers to Shared; do not modify `DefenderVmRecordFormatter`.
* If implementation reveals that `CortexXdrXqlClient` has a caller other than `CortexXdrFindingsFlow` and adding the probe method would force unscoped edits in another collector, surface as a blocker.
* If the stage rename causes string-comparison breakage outside the Cortex collector + its tests, surface as a blocker rather than working around silently.
* If `va_endpoints` rows in tests reveal an existing `sourceType` field in the vendor sample data — meaning Cortex's schema already has the property — surface as a blocker. The fail-fast guard is meant to protect against future schema regressions, not to mask a current collision.
* Verify by running the build and test commands the orchestrator confirms with repo conventions. Report the exact commands used.

Output Format:

* List of changed files grouped by commit (if commits split).
* Verification commands run, with pass/fail.
* The new `CortexXdrRecordFormatter.cs` public/internal surface (method signatures).
* The new `CortexXdrXqlClient` probe method signature and the new `CortexXdrUrls` constant.
* The updated `CortexXdrFindingsStage` constants and `CortexXdrFindingsCheckpointState` shape.
* Any unresolved blockers or accepted technical risks.
* Final task directory path.

Stop Conditions:

* The upstream consumer applies strict-schema validation that would reject the additive `sourceType` field. (Default assumption: tolerant; surface concrete evidence to the contrary as a blocker.)
* `CortexXdrXqlClient` has a caller other than `CortexXdrFindingsFlow` that would force unscoped edits.
* The `xql/get_datasets` response shape diverges from on-prem in a way that requires a non-trivial parsing change beyond the documented heuristic.
* A test failure cannot be resolved by mechanical updates to the stage rename, the sourceType prefix, or the new stage's request sequence, and would require a checkpoint/recovery contract change beyond the documented v2→v3 bump.
* A vendor row in test fixtures or sample data already carries `sourceType`, indicating the collision-fail-fast guard would trip in production.
* The goal is achieved and verification is complete.
