# Decisions

- Manifest is written after the Discover scroll, not after enrichment. It stops meaning "everything finished" and becomes a per-stage ledger.
- `bool? PolicyEnrichmentEnabled` + `HasCompatiblePolicyContract` are replaced by staged terminal state plus coverage counters. A bool cannot express "frozen but not yet enriched".
- Policy work moves out of `SpoolAsync` into a post-freeze stage. `ApplyPolicyContractAsync` becomes the normal path rather than an upgrade path.
- Policy output is a plane: definitions once, per-host edges separately. Downstream already models it this way and currently dedupes our per-host copies.
- Per-host edge carries `policy_id`, `applied`, `settings_hash`, `collection_status`, `definition_status`. Per-policy record carries name, platform, enabled, description, settings, rules, precedence, is_default.
- Definitions come from `GET /policy/combined/prevention/v1`, host-independent, own `pgen` generation. ~12 rows for this tenant, so no pagination-ceiling exposure.
- Assignments stay batched at frozen-page size (~1000 AIDs), NOT at `aidBatchSize` (8). Spotlight's batch size is bounded by findings-per-host, an unrelated quantity.
- Spotlight gates on traversed, not succeeded. Operator ruling.
- Proceeding on unverified: the device-entities endpoint accepts ~1000 ids per request. If wrong: the assignment stage 400s per page and the partial-rejection path must degrade to a smaller batch rather than fail the stage. Mitigation is required regardless of the bound (L-16fed2de).
- Proceeding on unverified: `device_policies.prevention.settings_hash` exists and is populated. If wrong: the edge's `settings_hash` is null and Exposure Analytics loses settings-drift detection. Not made worse by this task — same call as today.
- Defense keys on the response body's `errors[]` and on requested-vs-returned comparison, NOT on the HTTP status alone. Falcon is documented to deliver a 404 under a 200 header for at least one endpoint (L-882455c1), so a status-only rule can silently never fire.
