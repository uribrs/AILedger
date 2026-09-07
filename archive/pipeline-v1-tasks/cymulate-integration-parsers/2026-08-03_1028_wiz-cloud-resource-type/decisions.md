# Decisions

- **D-A — Accept lowercase; `_lower_string_values` is NOT changed.** EA's schema comments say `cloud_platform` and `cloud_resource_sub_type` are stored verbatim as the vendor reported them. This repo lowercases every string before write, so EA will receive `cloud_provider='aws'` and `cloud_resource_sub_type='virtual_machine'`. The operator chose this over a per-column exemption affecting 24 parsers. **This is a deliberate, informed divergence from EA's stated intent — do not "fix" it as a bug.** (Operator, 2026-08-03.)

- **D-B — Wiz asset `type` becomes const `cloud_resource`.** Wiz becomes the first non-`Host` parser in the repo. Correct per the EA contract: EA labels it "Cloud resource" and its create-entities-tp documents this path as built for Wiz first. (Operator, 2026-08-03.)

- **D-C — The six cloud columns are emitted by a Wiz-local `create_asset_source` override, NOT by `base_parser`.** Adding them to `BaseParser.create_asset_source` would change the output schema of all 24 parsers and invalidate all 17 committed `*.expected_assets.json` golden snapshots (snapshots are produced by `row.asDict(recursive=True)`, so they carry the exact select list). The override gives zero blast radius and zero snapshot churn. **Accepted cost:** the Wiz override duplicates knowledge of the base select, and the next cloud connector (Orca / Prisma Cloud / Cloud Guard) will repeat it until someone promotes the columns to `base_parser` in a dedicated change that also regenerates the snapshots. (Operator, 2026-08-03.)

- **D-D — `region` and `cloud_provider_url` emit NULL.** Neither exists on the Wiz assets lane. `region` is only on the findings lane (`asset_region`); the collector's `cloudResourcesV2` GraphQL query never requests it. Fixing that is collector work in another repo. (Note: this retro-justifies removing the dead `"Region"` additional field in the predecessor task — the mapping pointed at a field the collector never fetched.)

- **D-E — Branch is `feat/wiz-cloud-resource-type`, off `feat/wiz-parser` (commit f230eee).** Not created. No branch creation, commit or push without explicit operator approval. (Operator, 2026-08-03.)

## Still binding from the predecessor task

- Asset `value` stays `coalesce(nonblank(name), nonblank(external_id), id)`; the `(value, type)` collision is accepted. Measured on real data: 661 of 4,809 rows collide, worst case `'default'` ×72. Out of scope here.
- Do not regress commit `f230eee`: envelope correlation, timestamp casts, tags, group_names, spine dedup.
- Predecessor D8 still applies: `BaseParser.process_asset_mandatory_fields` iterates a fixed name list and silently discards extra keys, so the cloud columns must be carried as explicit columns, never declared in `asset_mandatory_fields`.
