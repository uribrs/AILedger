# Task: Qualys assets-first switch

Switch Qualys collection from findings-perspective to assets-first across both C# collectors and the SPARK parser.

Today: assets are derived from detections. Hosts with no active detection (New/Active/Re-Opened) are dropped and never become assets. The asset inventory is therefore incomplete — only vulnerable hosts exist.

Target: every host becomes an asset; detections/exposures are left-joined onto their asset and remain bidirectionally correlated. The combined feed shape `{HOST_DETAILS, DETECTION_LIST}` is preserved (one feed → one parser → both assets + findings); findings-less hosts are emitted with an empty `DETECTION_LIST`.

## Code targets
- **Collector A** — platform adapters (C#, System.Text.Json): `src/Cymulate.Integration.Adapters/Collectors/QualysCollector`. Findings-only (`IFindingsCollectorAdapter`); `QualysFindingsFlow` enumerates host IDs, batches, fetches detections, publishes only detection-returned hosts.
- **Collector B** — agent service (C#, Newtonsoft JObject, `Cymulate.Agent.*` infra): `/Users/user/Dev/AgentService/Source/CybiCollectors/QualysCollector` (branch `qualys-no-fixed`). Single-file `QualysCollector : QualysApi, IAsyncFindingsCollector`. Same enumerate→batch→detections→enrich shape; same loss. Must reach behavioral parity with A.
- **SPARK parser** — Python: `/Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/qualys/qualysAssetsAndFindings.py`. `pre_process` does `explode(DETECTION_LIST)` which drops findings-less hosts; assets are derived post-explode.

## Reference fixtures (live, this session)
Probe at `/Users/user/Dev/Uri/localprojects/IntegrationProbes` — commands `qualys-assets` and `qualys-exposureless` produce the canonical target shapes: `combined.ndjson`, `asset_with_exposures.json`, `exposureless_asset.json`. Memory: `project_qualys_assets_perspective.md`.
