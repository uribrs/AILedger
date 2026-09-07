# Constraints

- Preserve the single combined-feed shape `{HOST_DETAILS, DETECTION_LIST}`; do NOT add a separate `IAssetsCollectorAdapter`/assets flow.
- Every host (incl. findings-less) must be emitted; findings-less hosts carry `DETECTION_LIST: []`.
- Asset spine = host list `?action=list&details=All&show_asset_id=1` (superset of detection HOST_DETAILS). Asset records built uniformly from host-list details.
- Maintain bidirectional correlation: `asset.finding_ids[]` ↔ `exposure.id`; `exposure.asset_type`/`asset_value` join to `asset.type`/`value`.
- Asset identity (`value`) = NETBIOS else IP; a host with neither must be surfaced (logged/counted), not silently emitted as a broken asset.
- MEMORY (hard): stream the host list page-by-page (page → detections for that page → left-join → publish → release). Never materialize all host details for the whole run. Must run in bounded memory in a K8s pod.
- Remove the empty-`DETECTION_LIST` drop in collector parsers (A: `QualysFindingsXmlParser.ParseDetectionHosts`; B: equivalent in `processDetectionListAsync`/`parseVulnerabilityDetectionsBatchAsync`).
- SPARK: decouple asset extraction (from `HOST_DETAILS.*`, pre-explode, dedup) from findings extraction (inner `explode`). Do NOT use `explode_outer` (creates junk null-detection findings).
- SPARK: findings-less rows must produce an asset and ZERO findings.
- Extra host fields flow into the parser's existing `additional_fields` struct — no schema change to assets/findings tables.
- Knowledge-base (VULNERABILITY_INFO) enrichment behavior unchanged for hosts that have detections.
- Resumability/checkpoint semantics preserved; the page-cursor model must survive resume on any pod.
- Qualys 1960 concurrency handling and existing rate-limit/retry behavior must not regress.
- Collector A and Collector B must reach behavioral parity (same hosts emitted, same correlation), despite different JSON libs (System.Text.Json vs Newtonsoft) and infra.
- Build pinned to net8.0; do not pass -f net9.0. Existing tests must pass.
- Two collectors live in different repos/branches; do not assume identical code — confirm each independently.
