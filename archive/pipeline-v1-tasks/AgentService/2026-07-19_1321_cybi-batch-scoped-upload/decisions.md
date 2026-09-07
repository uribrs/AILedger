# Decisions

- Mechanism lives centrally in `CybiBatchUploader` (implementation only), not in collectors — zero collector redelivery, one flag, no binary-contract risk. (Operator-approved.)
- Run-global batch counter owned by the uploader, keyed per actionId — flows (assets/findings) can never collide on `batch_NNNNNN`; existing per-flow file names survive inside the folder. (Operator-approved.)
- `instanceBatchId` = UUIDv5 of `{instanceOid}/batch_{N:D6}` under the adapters' frozen namespace — unique across runs (unique index on batch doc `id`), replay-stable on re-run, cannot collide with adapter-minted ids (different name shape, v5). (Operator-approved.)
- UUID name base is the instance `_id` (instanceOid), not the agent action id — it is the run identity on both the wire (`cybi/{instanceId}`) and the consumer correlation. (Operator-approved.)
- Agent constructs the full ISB-shape `metadata` object itself from `IntegrationInstanceJson` — no server-side derivation needed; server dependency reduces to passthrough. (Operator-approved.)
- Opt-in is server-driven via the action payload, default off — agent and backend gates (`micro-batching` flag + `integrationSetting.isBatch`) flip together; avoids the empty-root legacy-parse failure mode adapters hit. (Operator-approved; exact key OPEN — see assumptions.)
- Batch granularity decoupled from flush granularity: k flushes (or byte threshold) per batch folder, announce-on-close. 1:1 is the degenerate k=1 case. Rationale: Hertz DefenderVm run = 1,634 flushes; 1:1 would mean 1,634 Glue triggers per run. (Operator-aware.)
- `batchedAssets`/`batchedFindings` split is a server-side mapping concern (from `assets_`/`findings_` fileName prefix); agent sends `itemCount` + `fileName` only. (Operator-approved.)
- Completion (`stop`) message shape unchanged; the enabled path closes any open batch folder before completion. (Derived from operator constraint + consumer behavior.)
