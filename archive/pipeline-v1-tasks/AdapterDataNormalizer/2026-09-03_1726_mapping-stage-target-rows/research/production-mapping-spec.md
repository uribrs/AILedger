# Production column mapping — CrowdStrike/Falcon and Tenable.io

Extracted read-only from `/Users/user/Dev/cymulate-integration-parsers` on 2026-09-03. This is the
authoritative production mapping (PySpark ETL, AWS Glue 4.0, writes
`integration.parser_output_assets` / `integration.parser_output_exposures`).

Throughout, `$R` = `/Users/user/Dev/cymulate-integration-parsers`.

**Ignore `$R/libs/packages/build/lib/**`** — it is a stale build copy of the same files and will
produce duplicate, possibly outdated, grep hits.

**Evidence levels used below.** Source code is level 1. The committed output snapshots under
`$R/test_files/**` are level 3 (operational evidence): `$R/tests/test_run_parsers.py:238-255` diffs
real Spark output against them byte-for-byte, so a value present there is real production output.
One item is explicitly **UNRESOLVED** and labelled as such — see §9.

---

## 1. The five-stage pipeline

A per-column mapping is only stage 1 of 5. Order matters; later stages overwrite earlier ones.

| stage | where | what it does |
|---|---|---|
| 1 | `extract_module_data` — `$R/libs/packages/parsers/common/base_parser.py:426-462` | Resolves each column. If `path` (a string) exists in the flattened schema → `transformation(F.col(path))`, else the raw column. If `path` is a list → `transformation([cols])`, or `coalesce(*cols)` with no transformation; a listed path missing from the schema is substituted with `F.array().cast("array<string>")` (`:437-440`). If `path is None` **and** a transformation exists → `transformation(F.lit(None))` (`:454-455`). Otherwise → the default. **Always** finishes with `F.coalesce(field_col, default_column)` (`:459`), then `_anchor_expr_to_dataframe` (`:460`, a Spark BoundReference workaround, semantically a no-op). |
| 2 | `process_asset_mandatory_fields` — `base_parser.py:77-134` | Asset lanes only. Raises if a required mandatory field is unmapped (`:113`). Emits `ColumnSchema(None, None, None)` for an unmapped optional field — today only `risk_score` (`:106`, `:126`). **Injects an extra `F.lower()` around the `value` transformation only** (`:116-119` + `$R/libs/packages/utilities/helpers.py:619-629`). |
| 3 | per-parser `process()` casts | `withColumn(... .cast(...))` for timestamps, `array<string>`, `StringType`; `id` generation via `helpers.uuid_udf()`. |
| 4 | `BaseParser.post_process` — `base_parser.py:219-324` | Row drops, `type`/`os_type`/`status` enum normalization, `severity` null-fill, `cve_ids` lowercase + CVE explode. See §6 and §7. |
| 5 | `create_asset_source` / `create_finding_source` — `base_parser.py:589-661` / `541-587` | Fixed output projection (drops any column not listed), struct→JSON encoding, then `_lower_string_values` (`:791-806`) and `cast_columns_to_schema` (`:769-788`). |

### Three global rules

**R1 — everything is lowercased at the very end.** `_lower_string_values`
(`base_parser.py:791-806`) walks the final schema and, for **every** `StringType` column and every
`ArrayType(StringType)` column, applies `regexp_replace(lower(col), '[\x00-\x1F\x7F]', '')`. It runs
*after* the struct→JSON encoding, so JSON **keys as well as values are lowercased** — see §3.

**R2 — `aid` and `finding_ids` are mapped and then thrown away.** Neither appears in the asset
output projection (`base_parser.py:634-659`). `aid` survives only as an in-memory dedup key on the
Tenable hydrated path (`tenableAssetsAndFindings.py:538-542`). Any effort spent mapping
`finding_ids` is wasted.

**R3 — `client_id` and `instance_id` mappings are dead.** Both are re-stamped from env vars at
`base_parser.py:636-638` (assets) and `:567-569` (findings), overwriting whatever the field map
produced.

### Output schemas (the projection allow-lists)

`_ASSET_OUTPUT_SCHEMA` — `base_parser.py:718-743`:
`id, client_id, client_integration_id, instance_id, integration_setting_id,
integration_setting_flow_id, connector_flow_name, created_at, first_seen, last_seen, ip_address,
tags, type, value, os_type, os_version, os_build, fqdn, group_names, site_names, additional_fields,
agent_metadata, device_metadata, risk_score`.

`_FINDING_OUTPUT_SCHEMA` — `base_parser.py:745-766`:
`id, asset_id, client_id, client_integration_id, instance_id, integration_setting_id,
integration_setting_flow_id, connector_flow_name, created_at, first_seen, last_seen, name,
display_name, description, mitigation, type, severity, status, cve_id, additional_fields`.

Note the finding output carries a **scalar `cve_id`**, not the `cve_ids` array.

---

## 2. Lane 1 — CrowdStrike/Falcon assets

Two live entry points, one language:

- **assets-only lane** — `$R/libs/packages/parsers/yaml_engine/specs/crowdstrike-assets.yaml`
  (`parser_key: crowdstrike-assets`). No `process_hook`, so the generic `YamlParser` executes this
  spec directly. This is the live implementation for the asset-only flow.
- **assets+findings lane** — `$R/libs/packages/parsers/deprecated/crowdstrike/crowdstrikeAssets.py`,
  inherited verbatim by `CrowdstrikeAssetsFindingsParser` (`crowdstrikeAssetsFindings.py:50`), which
  **is live** via the delegate hook at `crowdstrike-assets-findings.yaml:32-38`.

The two are field-for-field identical by design —
`crowdstrikeAssetsFindings.py:24-27` states `crowdstrikeAssets.py` is the source of truth, and
`crowdstrikeAssetsFindings.py:61-64` restates that the asset maps are inherited unchanged.

### 2a. `asset_mandatory_fields` — `crowdstrikeAssets.py:30-122`

| target column | source path | default_value | transformation (written out in full) | later stages → final DB value |
|---|---|---|---|---|
| `client_id` | `None` (`:34`) | `options.env_vars["CLIENT_ID"]` (`:35`) | none | overwritten by `F.lit(env CLIENT_ID)` — `base_parser.py:636` |
| `instance_id` | `None` (`:39`) | `options.env_vars["INSTANCE_ID"]` (`:40`) | none | overwritten by `F.lit(env INSTANCE_ID)` — `base_parser.py:638` |
| `type` | `None` (`:44`) | `"Host"` (`:45`) | none | `F.lower` at `base_parser.py:231` → **`"host"`** |
| `aid` | `id` (`:48`) | `""` | none | **dropped** — not in `_ASSET_OUTPUT_SCHEMA` |
| `value` | `None` (`:50`) | `None` | `F.coalesce(F.when(F.col("hostname").contains("ip"), F.col("current_local_ip")).otherwise(F.col("hostname")), F.col("current_local_ip"))` (`:57-62`), then wrapped in `F.lower(...)` by `base_parser.py:119` | cast `StringType` (`:218`); lowercased again by R1; **null / blank / control-char → whole row dropped** (`base_parser.py:184-198`) |
| `os_type` | `platform_name` (`:65`) | `"Other"` (`:66`) | none | enum-normalized at `base_parser.py:240-253`: `"Windows"`→`windows`, `"Mac"`→`mac`, `"Linux"`→`linux`, `"Other"`/anything unmatched→`other` |
| `os_version` | `os_version` (`:70`) | `"Other"` (`:71`) | none | `F.lower` at `base_parser.py:256`; e.g. `"Windows Server 2019"` → `"windows server 2019"` |
| `first_seen` | `first_seen_timestamp` (`:75`) | `None` | none | `cast(TimestampType())` (`:214`) |
| `last_seen` | `last_seen_timestamp` (`:80`) | `None` | none | `cast(TimestampType())` (`:216`) |
| `finding_ids` | `None` (`:85`) | `F.array()` (`:86`) | none | `cast("array<string>")` (`:211`); then **dropped** — not in `_ASSET_OUTPUT_SCHEMA` |
| `ip_address` | `current_local_ip` (`:90`) | `None` | `F.when(column.isNotNull(), F.array(column)).otherwise(F.array())` (`:92-94`) | `cast("array<string>")` (`:220`); `coalesce(col, array())` (`base_parser.py:645` via projection); elements lowercased by R1 |
| `tags` | `tags` (`:97`) | `F.array()` (`:98`) | none | `create_asset_tags` (`base_parser.py:530-539`): `when(size(tags)==0, array()).otherwise(transform(tags, tag -> named_struct('value', tag, 'source', 'integration', 'isDeleted', false)))`, then `F.to_json` (`:618`), then lowercased by R1 → key becomes `isdeleted`. Falcon Discover carries no `tags` key at all, so the default wins and the DB value is the literal string `"[]"` |
| `os_build` | `None` (`:102`) | `""` (`:103`) | none | `F.lower` at `base_parser.py:258` → `""` |
| `fqdn` | `fqdn` (`:107`) | `""` (`:108`) | none | `F.lower` at `base_parser.py:257` |
| `group_names` | `None` (`:112`) | `F.array()` (`:113`) | none | `coalesce(col, array()).cast(array<string>)` — `base_parser.py:653` → `[]` |
| `site_names` | `site_name` (`:117`) | `F.array()` (`:118`) | none | **Observed always `[]`** even when the source `site_name` is a non-empty string. Mechanism UNRESOLVED — see §9. Recommendation: hard-code `[]`; do not wire `site_name` here |
| `risk_score` | not supplied → `ColumnSchema(path=None, default_value=None, transformation=None)` (`base_parser.py:126`) | `None` | none | `null`, `DoubleType` |

Mirrored declaratively at `crowdstrike-assets.yaml:22-46`. In the spec, `value` is expressed as
`transform_fn: { module: parsers.crowdstrike.crowdstrike_yaml_fns, function: crowdstrike_asset_value }`
(`crowdstrike-assets.yaml:31-34`) because the two-column derivation with the `contains` guard is not
expressible in the declarative vocabulary; the implementation is byte-identical
(`$R/libs/packages/parsers/crowdstrike/crowdstrike_yaml_fns.py:30-35`).

Two spec-vocabulary notes so the YAML reads correctly against the table:
`{ path: X, const: [] }` compiles to `path=X, default_value=array()` (`transforms.py:163-166`) —
`const` is just a default, it does not suppress the path. `{ path: X, to_array: true }` compiles to
the `_to_array_transform()` primitive (`transforms.py:180-181`).

### 2b. `asset_additional_fields` — `crowdstrikeAssets.py:126-139`

**This is a curated label→value map, not the raw vendor record.** Nine entries. The dict key is the
struct field name, so it is what appears verbatim as the JSON key — human-readable labels with
spaces and capitals, then lowercased by R1.

| declared struct key | **JSON key in the DB** (after R1) | vendor source path | default | transformation |
|---|---|---|---|---|
| `OS Build` | `os build` | `os_build` (`:128`) | `None` | none |
| `Instance ID` | `instance id` | `instance_id` (`:129`) | `None` | none |
| `Kernel Version` | `kernel version` | `kernel_version` (`:130`) | `None` | none |
| `OS Product Name` | `os product name` | `os_product_name` (`:131`) | `None` | none |
| `Default Gateway IP` | `default gateway ip` | `default_gateway_ip` (`:132`) | `None` | none |
| `External IP` | `external ip` | `external_ip` (`:133`) | `None` | none |
| `Service Provider` | `service provider` | `service_provider` (`:134`) | `None` | none |
| `Product Type Desc` | `product type desc` | `product_type_desc` (`:135`) | `None` | none |
| `System Manufacturer` | `system manufacturer` | `system_manufacturer` (`:137`) | `None` | none |

That is the **complete** set — nine keys, nothing else. Mirrored at `crowdstrike-assets.yaml:50-60`.

Two extra transforms apply to `additional_fields` and to nothing else:

1. **`sanitize_additional_fields_struct`** — `base_parser.py:679-716`, applied through a Spark UDF at
   `base_parser.py:610-614`, recursing into nested dicts/Rows. Per string field it calls
   `sanitize_string_value` (`base_parser.py:663-677`): `re.sub(r'[^\x20-\x7E\t\n\r]', '', str(value))`,
   then `.strip()`, and **returns `None` if the result is empty or whitespace-only**. So a
   non-ASCII-only value becomes `null`, not `""`.
2. `F.to_json` (`base_parser.py:620`), then R1 lowercases the whole JSON string.

**Field order in the DB.** `F.to_json` preserves struct field order, which is the declaration order
above (`OS Build` first). The alphabetical ordering visible in
`$R/test_files/crowdstrike/assets/assets.expected_assets.json` is a **test artifact** — `_canon_json`
re-serializes the `additional_fields` / `agent_metadata` / `device_metadata` strings with
`sort_keys=True` (`$R/tests/yaml_engine/_fixture_utils.py:62`, `:80-95`). Do not read production key
order off the snapshot. (`tags` is *not* in that `_JSON_COLUMNS` list, so the snapshot's `tags`
string *is* raw production output — which is how the lowercased `"isdeleted"` key is directly
visible.)

Likewise, the snapshot's `client_id: null`, `instance_id: null`, `client_integration_id: null`,
`created_at: null` are blanked by `_normalize_record` (`_fixture_utils.py:61`, `:106-107`) — not
production nulls.

### 2c. `agent_metadata_fields` — `crowdstrikeAssets.py:142-147`

Top-level output column of its own (`agent_metadata`), JSON-encoded. Three entries, complete set.

| struct key | vendor source path | default | transformation |
|---|---|---|---|
| `agent_version` | `agent_version` (`:144`) | `None` | none |
| `protection_status` | `reduced_functionality_mode` (`:145`) | `None` | none |
| `onboarding_status` | `entity_type` (`:146`) | `None` | none |

Mirrored at `crowdstrike-assets.yaml:66-69`. Assembled with `F.struct(*cols).alias("agent_metadata")`
(`crowdstrikeAssets.py:202`), `to_json`'d at `base_parser.py:622`. **Not** sanitized — the sanitize
UDF only targets `additional_fields`.

Production example (raw, from the snapshot):
`{"agent_version": "7.24.19607.0", "onboarding_status": "managed", "protection_status": "no"}` —
note `reduced_functionality_mode: "no"` surfacing as `protection_status`.

### 2d. `device_metadata_fields` — `crowdstrikeAssets.py:150-156`

Top-level output column `device_metadata`, JSON-encoded. Four entries, complete set.

| struct key | vendor source path | default | transformation |
|---|---|---|---|
| `device_role` | `product_type_desc` (`:152`) | `None` | none |
| `form_factor` | `form_factor` (`:153`) | `None` | none |
| `mac_addresses` | `mac_addresses` (`:154`) | `None` | none — stays a JSON array inside the struct |
| `serial_number` | `system_serial_number` (`:155`) | `None` | none |

Mirrored at `crowdstrike-assets.yaml:70-73`. Ordering in the spec is load-bearing and commented as
such (`crowdstrike-assets.yaml:62-64`): `agent_metadata` before `device_metadata`.

Note `product_type_desc` is mapped **twice** — as `additional_fields["Product Type Desc"]` and as
`device_metadata.device_role`.

---

## 3. Which columns are JSON-encoded structs before lowercasing

Encode-then-lower is what mangles the keys. The complete list and the exact order:

**Asset frame — `create_asset_source`, `base_parser.py:589-661`:**

| order | column | encode step | source shape |
|---|---|---|---|
| 1 | `additional_fields` | sanitize UDF `:610-614`, then `F.to_json` `:619-620` | `F.struct` of the curated label map |
| 2 | `tags` | `F.to_json` `:617-618` | `array<struct{value, source, isDeleted}>` from `create_asset_tags` `:530-539` |
| 3 | `agent_metadata` | `F.to_json` `:621-622` | `F.struct` |
| 4 | `device_metadata` | `F.to_json` `:623-624` | `F.struct` |

Then the fixed `df.select(...)` at `:634-659`, then **`_lower_string_values` at `:660`**, then
`cast_columns_to_schema` at `:661`. All four are `StringType` by the time `_lower_string_values`
runs, so all four have **keys and values lowercased and control chars stripped**.

`ip_address`, `group_names`, `site_names` stay `ArrayType(StringType)` and get their **elements**
lowercased (`:801-805`) — they are not JSON-encoded.

**Finding frame — `create_finding_source`, `base_parser.py:541-587`:**

| order | column | encode step |
|---|---|---|
| 1 | `additional_fields` | `F.to_json` `:544-545` |

Then `select(...)` at `:564-585`, then **`_lower_string_values` at `:586`**, then
`cast_columns_to_schema` at `:587`. So finding `additional_fields` also has its JSON keys
lowercased. The finding frame has no `tags` / `agent_metadata` / `device_metadata`.

**Nested structs inside a dynamic `additional_fields` are pre-encoded to JSON strings and therefore
double-escaped.** Tenable does this deliberately (`tenableAssetsAndFindings.py:494-495`, `:573-574`,
`:586-587`), producing values like
`"cvss_vector": "{\"access_complexity\":\"low\",...}"` inside the outer `additional_fields` JSON —
visible in `$R/test_files/tenable/assets_and_findings/findings.expected_findings.json`.

**Complete list of JSON-encoded columns: `additional_fields`, `tags`, `agent_metadata`,
`device_metadata` (assets); `additional_fields` (findings).** Nothing else.

---

## 4. Lane 2 — CrowdStrike/Falcon findings

`$R/libs/packages/parsers/deprecated/crowdstrike/crowdstrikeAssetsFindings.py`, live via the
delegate at `crowdstrike-assets-findings.yaml:32-38`.

### 4a. `finding_mandatory_fields` — `crowdstrikeAssetsFindings.py:67-100`

| target column | source path | default_value | transformation (written out in full) | later stages → final DB value |
|---|---|---|---|---|
| `client_id` | `None` (`:69`) | `env CLIENT_ID` | none | overwritten `base_parser.py:567` |
| `instance_id` | `None` (`:70`) | `env INSTANCE_ID` | none | overwritten `base_parser.py:569` |
| `name` | `vulnerability_id` (`:72`) | `""` | none | **OVERWRITTEN** by `add_unified_finding_name_column_for_vulnerability` (`base_parser.py:521-527`, called at `:308`): `F.concat(F.lit("detected finding with cve id: "), F.expr("concat_ws(',', sort_array(cve_ids))"))`. Because `super().post_process()` already exploded `cve_ids` to one element per row, the DB value is `"detected finding with cve id: <single cve>"`. The mapped `vulnerability_id` never reaches `name` |
| `display_name` | `vulnerability_id` (`:73`) | `""` | none | lowercased by R1. **This is the only output column carrying the vendor vulnerability id** |
| `type` | `None` (`:74`) | `"vulnerability"` (`:74`) | none | `"vulnerability"` |
| `severity` | `cve.severity` (`:75`) | `None` | `F.when(column == "LOW", "low").when(column == "MEDIUM", "medium").when(column == "HIGH", "high").when(column == "CRITICAL", "critical")` (`:76-80`) — **no `otherwise` branch**, case-sensitive exact match | unmatched (incl. `NONE`, `UNKNOWN`, `"High"`, `null`, `""`) → `null` → `F.coalesce(severity, F.lit("info"))` at `base_parser.py:274-276` → **`"info"`** |
| `mitigation` | `remediation.entities` (`:81`) | `None` | `column[0].action` (`:82`) — first entity only, remaining entities discarded | lowercased by R1 |
| `status` | `status` (`:83`) | `"open"` (`:84`) | `F.when(column == "open", "open").when(column == "closed", "resolved").when(column == "close", "resolved").when(column == "reopened", "reopened").when(column == "reopen", "reopened").otherwise("open")` (`:86-92`) | remapped at `base_parser.py:279-285`: `open`→**`opened`**, `resolved`→`resolved`, `reopened`→`reopened`. See §6 |
| `first_seen` | `created_timestamp` (`:94`) | `None` | none | `cast(TimestampType())` by `cast_columns_to_schema` (`base_parser.py:753`) |
| `last_seen` | `updated_timestamp` (`:95`) | `None` | none | `cast(TimestampType())` (`base_parser.py:754`) |
| `cve_ids` | `cve.id` (`:96`) | `None` | `F.when(column.isNotNull(), F.array(column)).otherwise(F.array())` (`:97-98`) | see §8; published as scalar `cve_id = cve_ids[0]` (`base_parser.py:556-559`, `:583`) |
| `description` | `cve.description` (`:99`) | `None` | none | `cast(StringType())` (`base_parser.py:578`), lowercased by R1 |
| `asset_id` | — | — | `F.col("_row_asset_id").alias("asset_id")` (`:235`) — the correlated parent asset's generated uuid | passthrough |
| `id` | — | — | `helpers.uuid_udf()` (`:238`), then **regenerated per CVE row** after the explode (`base_parser.py:314`) | passthrough |

### 4b. `finding_additional_fields` — dynamic, `crowdstrikeAssetsFindings.py:103-107` + `243-295`

The declared property returns `{}` (`:107`). The real map is computed per run from the projected
findings schema by `_build_finding_additional_fields`. Three groups:

**Group 1 — top-level scalars** (`:263-271`). Every field of the projected findings schema, output
key = the source field name verbatim, `default=None`, `transformation=None`. Excluded:

- every `path` used by a mandatory field (`:250-253`) → `vulnerability_id`, `status`,
  `created_timestamp`, `updated_timestamp` (the dotted `cve.severity`, `cve.id`, `cve.description`,
  `remediation.entities` matter for groups 2 and 3);
- the hard-coded set `excluded_top_level = {"host_info", "cve", "remediation", "assetId",
  "_row_asset_id", "vulnerabilities", "id", "cid", "aid"}` (`:259-262`) — `id`/`cid`/`aid` are the
  feed's own identity/correlation keys, deliberately kept out (`:256-258`);
- **every field whose type is `StructType` or `ArrayType`** (`:267-268`). This is why an `apps`-style
  array could never appear here even if it survived the read.

Asserted by `$R/tests/test_crowdstrike_assets_findings.py:218-225` — `id`/`cid`/`aid` absent,
`vulnerability_metadata_id` present.

**Group 2 — the whole `cve` block** (`:274-282`). One key `cve_<subfield>` per subfield of the `cve`
struct, except those already mapped as mandatory. Given `_REQUIRED_CVE_FIELDS = ("id", "severity",
"description")` (`CrowdstrikeAssetsFindingsNotHydrated.py:61`) and the fixture shape, the observed
keys are `cve_base_score`, `cve_exprt_rating`, plus whatever else the vendor sends. Asserted by
`tests/test_crowdstrike_assets_findings.py:255-263` (`base_score` present).

**Group 3 — the whole `remediation` block** (`:285-293`). One key `remediation_<subfield>` per
subfield of the `remediation` struct, except `remediation.entities` (already mandatory).

The output key set is therefore **not knowable from the spec** — it is the run's input schema minus
the exclusions. Contrast this with the Falcon **asset** lane, whose `additional_fields` is a fixed
curated nine-key label map (§2b).

---

## 5. Lane 3 — Tenable.io assets and findings

**The Tenable.io parser is `$R/libs/packages/parsers/deprecated/tenable/tenableAssetsAndFindings.py`**
(688 lines), reached via the delegate at
`$R/libs/packages/parsers/yaml_engine/specs/tenable-assets-findings.yaml:29-35`
(`parser_key: tenable-assets-findings`, `flow_name: Tenable.io - Tenable.io Assets and Findings`).

**Do not conflate with Tenable.sc.**
`$R/libs/packages/parsers/yaml_engine/specs/tenable-sc-assets-findings.yaml` is Tenable **Security
Center** — a different product, a different input shape (`vulnerabilities.*`, `asset.os`), a live
*declarative* spec, with `$R/libs/packages/parsers/deprecated/tenable_sc/tenableScAssetsAndFindings.py`
as its Python reference. Not tabled here.

Source paths below are relative to an embedded `asset` struct. In split and correlated modes that
struct is **synthesised** from the raw `/assets/export` row — see §5e.

### 5a. `asset_mandatory_fields` — `tenableAssetsAndFindings.py:233-331`

| target column | source path | default_value | transformation (written out in full) | later stages → final DB value |
|---|---|---|---|---|
| `client_id` | `None` (`:235`) | `env CLIENT_ID` | none | overwritten `base_parser.py:636` |
| `instance_id` | `None` (`:240`) | `env INSTANCE_ID` | none | overwritten `base_parser.py:638` |
| `type` | `None` (`:245`) | `"Host"` (`:248`) | none | `F.lower` → **`"host"`** |
| `aid` | `asset.uuid` (`:251`) | `""` | none | **dropped** from output; used as the hydrated-mode dedup key (`:538-542`) |
| `value` | `None` (`:255`) | `None` | `self._asset_name_column(self._asset_schema_df)` (`:258`). That method (`:216-230`) builds `F.coalesce(*[F.when(F.trim(c) != "", c) for c in candidates])` where `candidates` = `F.col(f"asset.{f}")` for each `f` in `ASSET_NAME_PRECEDENCE = ("agent_name", "netbios_name", "fqdn", "hostname", "ipv4")` (`:153`) **that is actually present** in the schema (`:226`); if no candidate exists at all → `F.lit(None).cast(StringType())` (`:229`). Then wrapped in `F.lower(...)` by `base_parser.py:119` | null/blank → **row dropped** (`base_parser.py:184-198`) |
| `os_type` | `asset.operating_system` (`:261`) | `None` | `F.when(column.isNotNull() & (F.size(column) > 0), F.element_at(column, 1)).otherwise(None)` (`:263-266`) — first element of the array | `cast(StringType())` (`:522-523`); enum-normalized (`base_parser.py:240-253`). `"Microsoft Windows"` does **not** match any `startswith` branch → **`"other"`** (snapshot-confirmed) |
| `os_version` | `asset.operating_system` (`:269`) | `None` | identical first-element transform (`:271-274`) | `cast(StringType())` (`:524-525`); `F.lower` → `"microsoft windows"` |
| `first_seen` | `asset.first_seen` (`:277`) | `None` | `parse_tenable_timestamp(column)` (`:126-144`): `F.when(column.isNotNull(), F.coalesce(F.to_timestamp(norm(column), "MM/dd/yyyy h:mm:ss a"), F.to_timestamp(norm(column), "MM/dd/yyyy H:mm:ss"), F.to_timestamp(norm(column), "dd/MM/yyyy h:mm:ss a"), F.to_timestamp(norm(column), "dd/MM/yyyy H:mm:ss"), F.to_timestamp(column))).otherwise(None)` where `norm = normalize_date_udf` (`:79-123`) | timestamp |
| `last_seen` | `asset.last_seen` (`:282`) | `None` | identical | timestamp |
| `finding_ids` | `None` (`:287`) | `F.array()` (`:288`) | none | `cast("array<string>")` (`:518-519`); **dropped** from output |
| `ip_address` | `None` (`:292`) | `F.array()` (`:293`) | `F.when(F.col("asset.ipv4").isNotNull(), F.array(F.col("asset.ipv4"))).otherwise(F.array())` (`:294-297`) | `cast("array<string>")` (`:520-521`); elements lowercased |
| `tags` | `asset.tags` (`:300`) | `F.array()` (`:301`) | `process_tenable_tags_udf(column)` (`:74-77`), a Python UDF returning `array<string>` (`:57-72`): for each element, read `tag.tag_key` / `tag.tag_value` via `hasattr`, and **only if both are non-None** append `f"{tag_key}: {tag_value}"`; `None`/empty input → `[]` | `create_asset_tags` → `{value, source:"integration", isDeleted:false}` per string → `to_json` → lowercased. Production example: `[{"value":"sistema operativo: windows","source":"integration","isdeleted":false}, ...]` |
| `os_build` | `None` (`:305`) | `""` (`:306`) | none | `""` |
| `fqdn` | `asset.fqdn` (`:310`) | `""` (`:311`) | none | `F.lower` |
| `group_names` | `None` (`:315`) | `F.array()` (`:316`) | none | `[]` |
| `site_names` | `None` (`:320`) | `F.array()` (`:321`) | none | `[]` — genuinely never mapped, no ambiguity here |
| `risk_score` | `asset.acr_score_v3` (`:325`) | `None` | `lambda column: column.cast(DoubleType()) if self._asset_has_field(self._asset_schema_df, "acr_score_v3") else F.lit(None).cast(DoubleType())` (`:327-329`) | `DoubleType`; `null` throughout the current snapshot |

`normalize_date_string` (`:79-121`), the UDF inside `parse_tenable_timestamp`, in full: replace
`" a. m."` → `" AM"` and `" p. m."` → `" PM"`; split on the first space into date and time; split
the date on `/` into three components; zero-pad the first and second component if single-digit;
re-join as `f"{first}/{second}/{year} {time_part}"`. Any exception or unexpected shape returns the
input unchanged. It exists because the legacy hydrated combined file carries localized US/EU strings
(`"19/06/2020 04:04:18 a. m."`, visible in the snapshot) while the raw `/assets/export` lane carries
ISO-8601 — the final `F.to_timestamp(column)` in the coalesce chain handles the latter (`:126-134`).

### 5b. `asset_additional_fields` — dynamic, `tenableAssetsAndFindings.py:334-335` + `473-502`

Declared `{}` (`:335`). Built per run — and **it is the whole `asset` struct, not a curated map.**

The exclusion set is computed as `mandatory_asset_root_paths = {cs.path.split('.')[0] for cs in
self.asset_mandatory_fields.values() if cs.path is not None}` (`:476-480`). **Every mandatory asset
path starts with `asset.`**, so that set evaluates to exactly `{"asset"}` — and the loop at `:487-496`
compares *subfield names* against it. Consequence: **no asset subfield is ever excluded.** Every
field of the `asset` struct lands in `additional_fields`, including ones already published as
first-class columns.

| output JSON key | vendor source | note |
|---|---|---|
| `uuid` | `asset.uuid` | duplicates the (dropped) `aid` |
| `ipv4` | `asset.ipv4` | duplicates `ip_address[0]` |
| `fqdn` | `asset.fqdn` | duplicates the `fqdn` column |
| `hostname` | `asset.hostname` | a `value` precedence candidate |
| `netbios_name` | `asset.netbios_name` | a `value` precedence candidate |
| `agent_name` | `asset.agent_name` | a `value` precedence candidate (absent from the current fixture) |
| `operating_system` | `asset.operating_system` | array kept as an array (`:492`) |
| `first_seen` | `asset.first_seen` | **raw un-parsed localized string**, e.g. `"19/06/2020 04:04:18 a. m."` |
| `last_seen` | `asset.last_seen` | raw un-parsed localized string |
| `tags` | `asset.tags` | raw `array<struct{added_at, added_by, tag_key, tag_uuid, tag_value}>`, kept as an array of objects — **not** the `"key: value"` string form used by the `tags` column |
| `acr_score` | `asset.acr_score` | |
| `acr_score_v3` | `asset.acr_score_v3` | duplicates `risk_score` |
| `acr_drivers` | `asset.acr_drivers` | |
| `agent_uuid`, `bios_uuid`, `device_type`, `ipv6`, `last_authenticated_results`, `last_scan_target`, `mac_address`, `network_id`, … | the corresponding `asset.*` subfield | whatever else the lane's `asset` struct carries |

Rules for the loop: a `StructType` subfield is pre-encoded with `F.to_json` (`:494-495`); an
`ArrayType` subfield is left as an array (comment at `:492-493`). Then
`ACR_ASSET_ADDITIONAL_FIELDS = ("acr_score", "acr_score_v3", "acr_drivers")` (`:147`) are appended as
`F.lit(None).cast(StringType())` if not already present (`:498-500`), so those three keys are always
in the output. The struct is built with plain `F.struct(*cols)` (`:502`), not `safe_struct`.

Verified against every key in `$R/test_files/tenable/assets_and_findings/findings.expected_assets.json`.

### 5c. `finding_mandatory_fields` — `tenableAssetsAndFindings.py:338-383`

| target column | source path | default_value | transformation (written out in full) | later stages → final DB value |
|---|---|---|---|---|
| `client_id` | `None` (`:340`) | `env CLIENT_ID` | none | overwritten `base_parser.py:567` |
| `instance_id` | `None` (`:341`) | `env INSTANCE_ID` | none | overwritten `base_parser.py:569` |
| `name` | `plugin.name` (`:343`) | `""` | none | **OVERWRITTEN** by the unified vulnerability name (`:680`) → `"detected finding with cve id: cve-1999-0524"` (snapshot-confirmed) |
| `display_name` | `plugin.name` (`:344`) | `""` | none | lowercased → e.g. `"icmp timestamp request remote date disclosure"` |
| `type` | `None` (`:345`) | `"vulnerability"` (`:345`) | none | `"vulnerability"` |
| `severity` | `severity_id` (`:346`) | `"low"` (`:346`) | `F.when(column == 4, "critical").when(column == 3, "high").when(column == 2, "medium").when(column == 1, "low").when(column == 0, "low").otherwise("low")` (`:348-353`) | never null; **anything unmapped → `"low"`**, so Tenable never produces `"info"` |
| `mitigation` | `plugin.solution` (`:354`) | `None` | none | lowercased |
| `status` | `state` (`:356`) | `"opened"` (`:358`) | `F.when(F.upper(column) == "OPEN", "open").when(F.upper(column) == "REOPENED", "reopened").when(F.upper(column) == "FIXED", "resolved").when(F.upper(column) == "RESURFACED", "reopened").when(F.upper(column) == "NEW", "open").otherwise("open")` (`:359-365`) | `open`→**`opened`** at `base_parser.py:281`. Snapshot statuses across 22 rows: `{opened, reopened}` |
| `first_seen` | `first_found` (`:366`) | `None` | none | `cast(TimestampType())` (`:654-655`) |
| `last_seen` | `last_found` (`:367`) | `None` | none | `cast(TimestampType())` (`:656-657`) |
| `cve_ids` | `plugin.cve` (`:368`) — already an array | `F.array()` (`:368`) | `F.when(col.isNotNull(), col).otherwise(F.array())` (`:369`) | `cast("array<string>")` (`:652-653`); see §8. A multi-CVE plugin **fans out into several exposure rows** |
| `description` | `plugin.description` (`:370`) | `None` | none | lowercased |
| `asset_type` | `None` (`:371`) | `"Host"` (`:373`) | none | internal join key only; `drop`ped at `:648` |
| `asset_value` | `None` (`:378`) | `None` | `self._asset_name_column(self._finding_schema_df)` (`:381`) — the same precedence chain as the asset lane's `value`, deliberately so hydrated-mode linkage matches (comment `:376-377`) | internal join key only; `drop`ped at `:648` |
| `asset_id` | — | — | split / correlated: `left` join on `_finding_asset_uuid == assets._asset_correlation_id` (`:618-632`). Hydrated: `left` join on `(asset_type == assets.type) AND (F.lower(asset_value) == assets.value)` (`:636-647`) | may be `null`; such rows are then killed by the cascade drop (§7 #2) |
| `id` | — | — | `helpers.uuid_udf()` (`:649`), regenerated per CVE row after the explode | passthrough |

### 5d. `finding_additional_fields` — dynamic, `tenableAssetsAndFindings.py:386-387` + `551-592`

Declared `{}` (`:387`). Built per run in two groups. Exclusion set here is the set of **exact dotted
mandatory paths** (`:554-559`), not root paths — so the `plugin` block is filtered precisely.

**Group 1 — the `plugin` block** (`:562-575`). One key per subfield of `plugin` whose full dotted
path is not mandatory. Output key = the **bare subfield name** (`:575`), so `plugin.risk_factor`
becomes `risk_factor`. Excluded: `plugin.name`, `plugin.solution`, `plugin.cve`,
`plugin.description`. `StructType` subfields are pre-encoded with `F.to_json` (`:573-574`); arrays
kept as arrays (`:571-572`). Observed keys in the snapshot include `bid`, `checks_for_default_account`,
`checks_for_malware`, `cpe`, `cvss3_base_score`, `cvss3_temporal_score`, `cvss3_temporal_vector`,
`cvss3_vector`, `cvss_base_score`, `cvss_temporal_score`, `cvss_temporal_vector` (JSON string),
`cvss_vector` (JSON string), `exploit_available`, `exploit_framework_canvas`,
`exploit_framework_core`, `exploit_framework_d2_elliot`, `exploit_framework_exploithub`,
`exploit_framework_metasploit`, `exploitability_ease`, `exploited_by_malware`, `exploited_by_nessus`,
`family`, `family_id`, `has_patch`, `has_workaround`, `id`, `in_the_news`, `modification_date`,
`patch_publication_date`, `publication_date`, `risk_factor`, `see_also`, `stig_severity`, `synopsis`,
`type`, `unsupported_by_vendor`, `version`, `vpr` (JSON string), `xrefs`.

**Group 2 — root-level fields** (`:577-588`). Every root field except
`excluded_root_fields = {"asset", "plugin"} | {p for p in mandatory_finding_paths if "." not in p}`
(`:578`) — i.e. except `asset`, `plugin`, `severity_id`, `state`, `first_found`, `last_found`.
`StructType` roots are pre-encoded with `F.to_json` (`:586-587`). Observed keys include
`epss_score`, `finding_id`, `indexed`, `last_fixed`, `output`, `port` (JSON string),
`resurfaced_date`, `scan` (JSON string), `severity` (the vendor's own string severity, distinct from
the mapped `severity` column), `severity_default_id`, `severity_modification_type`, `source`,
`time_taken_to_fix`.

Wrapped with `BaseParser.safe_struct` (`:590-592`), which substitutes a single anchored null
`_empty` field when the column list is empty (`base_parser.py:405-418`).

Note `severity` appears in `additional_fields` as the vendor's raw value while the published
`severity` column is derived from `severity_id` — two different values under the same name in the
same row.

### 5e. Split-lane field renames — `tenableAssetsAndFindingsNotHydrated.py:151-209`

`_build_asset_envelope_df` reshapes a raw `/assets/export` row into the `{ "asset": {...} }` envelope
the field maps read. This is what makes §5a and §5b mode-agnostic. It also runs on the correlated
lane (`tenableAssetsAndFindingsCorrelated.py:90-92`).

| synthesised `asset.*` field | from raw assets-export field | transformation |
|---|---|---|
| `uuid` | `id` (`:192`) | passthrough; also kept top-level as `_asset_correlation_id` (`:209`) |
| `ipv4` | `ipv4s` (`:193`) | `_first_or_null`: `F.when(column.isNotNull() & (F.size(column) > 0), F.element_at(column, 1)).otherwise(F.lit(None).cast(StringType()))` (`:61-66`) |
| `fqdn` | `fqdns` (`:194`) | `_first_or_null` |
| `netbios_name` | `netbios_names` (`:195`) | `_first_or_null` |
| `hostname` | `hostnames` (`:196`) | `_first_or_null` |
| `agent_name` | `agent_names` (`:197`) | `_first_or_null` |
| `operating_system` | `operating_systems` (`:198`) | passthrough, stays an array |
| `first_seen` | `first_seen` (`:199`) | passthrough |
| `last_seen` | `last_seen` (`:200`) | passthrough |
| `tags` | `tags` (`:201`) | `_normalize_tags` (`:80-97`): `F.when(column.isNotNull(), F.transform(column, lambda tag: F.struct(tag.getField("uuid").alias("tag_uuid"), tag.getField("key").alias("tag_key"), tag.getField("value").alias("tag_value")))).otherwise(F.array().cast(_NORMALIZED_TAG_STRUCT))` — renames `{uuid,key,value}` → `{tag_uuid,tag_key,tag_value}` so `process_tenable_tags_udf` reads both shapes identically |
| `acr_score` | `acr_score` (`:202`) | passthrough |
| `acr_score_v3` | **`ratings.acr.score`** (`:203`) | `.cast("double")`, guarded by `_has_nested_field(df, "ratings.acr.score")` (`:37-58`, `:185-189`); missing nested path → `F.lit(None).cast("double")` |
| `acr_drivers` | `acr_drivers` (`:204`) | passthrough |

Any absent source field becomes `F.lit(None).cast(...)` or `F.array().cast(array<string>)`
(`:169-177`), so the `_asset_name_column` precedence chain silently skips it rather than throwing.

---

## 6. Q3 — `status`: this repo writes `opened`, definitively

There **is** a later normalization step, in the shared base. `BaseParser.post_process`,
`$R/libs/packages/parsers/common/base_parser.py:277-285`, verbatim:

```python
# Normalize status to valid exposure_status enum values
# mirrors the SQL: open→opened, close/closed→resolved, re-opened→reopened
self.findings_df = self.findings_df.withColumn(
    "status",
    F.when(F.lower(F.col("status")) == "open",  F.lit("opened"))
     .when(F.lower(F.col("status")).isin(["close", "closed"]), F.lit("resolved"))
     .when(F.lower(F.col("status")) == "re-opened", F.lit("reopened"))
     .otherwise(F.lower(F.col("status")))
)
```

Falcon's mapper emits `open` / `resolved` / `reopened` (`crowdstrikeAssetsFindings.py:87-92`); the
base then rewrites `open` → `opened`, and `resolved` / `reopened` pass through the `otherwise`
branch unchanged. `crowdstrikeAssetsFindings.py:305` calls
`super(CrowdstrikeAssetsParser, self).post_process()`, which resolves to `BaseParser.post_process` —
the Falcon lane is not exempt. Tenable reaches the same code via `super().post_process()`
(`tenableAssetsAndFindings.py:677`).

Two independent confirmations:

- `$R/tests/test_crowdstrike_assets_findings.py:504` —
  `assert row["status"] == "opened"  # BaseParser normalizes "open" → canonical "opened"`.
- `$R/test_files/tenable/assets_and_findings/findings.expected_findings.json` — the only statuses
  present across 22 rows are `opened` and `reopened`.

`base_parser.py:281` is the **only** place the literal `"opened"` is produced anywhere in the repo
(repo-wide grep over `libs/`, `jobs/`). The literal `"open"` is a pre-normalization intermediate that
never reaches the DB. The `cybi.exposure_status` enum is satisfied.

Also documented in `$R/CLAUDE.md` under "Automatic post-processing (every parser, in
`BaseParser.post_process`)".

Reachable status values: **`opened`, `resolved`, `reopened`** for both lanes.

---

## 7. Q7 — every row-drop and filter condition

### Shared base — `$R/libs/packages/parsers/common/base_parser.py`

| # | location | exact predicate | effect | logged? |
|---|---|---|---|---|
| 1 | `_enforce_asset_output_contract:184-198` | keep only `F.col("value").isNotNull() & (F.trim(F.col("value")) != "") & ~F.col("value").rlike(r"[\x00-\x1F\x7F]")` | **asset row dropped.** Only `value` is enforced — per the `parser_output_assets` schema only `id` and `instance_id` are NOT NULL, so a null `type`/`os_version`/`fqdn`/`first_seen` is valid and must **not** cause a drop (`:174-178`) | **yes** — `f"[{integration_name}] output-contract dropped {dropped}/{before} asset(s) with null/blank/control-char value"` (`:200-204`) |
| 2 | `_enforce_asset_output_contract:206-217` | `findings_df.join(assets_df.select(col("id").alias("_surviving_asset_id")), col("asset_id") == col("_surviving_asset_id"), "left_semi")` | **cascade-drops every finding of a dropped asset**, and every finding whose `asset_id` is null | **no** |
| 3 | `post_process:286-295` | `filter(~( (F.col("type") == "vulnerability") & (F.size(F.expr("filter(cve_ids, x -> x is not null AND x != '')")) == 0) ))` | **finding row dropped.** Both Falcon and Tenable hard-code `type = "vulnerability"`, so **every finding with no usable CVE is discarded** | **no** |
| 4 | `post_process:298-319` | `with_cve = filter(cve_ids.isNotNull() & (size(cve_ids) > 0))` → `withColumn("_cve", F.explode("cve_ids"))` → rebuild `cve_ids` as a one-element array → `withColumn("id", uuid_udf())`; `without_cve` unioned back with `coalesce(cve_ids, array())` | row **multiplication**, one row per CVE, each with a fresh `id`. (After #3 the `without_cve` branch is unreachable for these two lanes) | no |
| 5 | `post_process:322-324` | `assets_df is None or not dataframe_has_rows(assets_df)` while `findings_df` has rows | raises `FindingsWithoutAssets("No assets found will findings where found")` — **the whole Glue job fails** | exception |
| 6 | `sanitize_string_value:663-677` | inside `additional_fields` only: `re.sub(r'[^\x20-\x7E\t\n\r]', '', str(value))`, then `.strip()`; if the result is falsy → `None` | **field value nulled**, row kept | no |
| 7 | `_lower_string_values:791-806` | `regexp_replace(lower(col), r"[\x00-\x1F\x7F]", "")` on every string / array-of-string column | **characters stripped**, row kept | no |

### CrowdStrike/Falcon-specific

| # | location | exact predicate | effect | logged? |
|---|---|---|---|---|
| 8 | `CrowdstrikeAssetsFindingsNotHydrated.py:217` | `df.filter(F.col("_corrupt_record").isNull()).drop("_corrupt_record")` | malformed findings NDJSON lines dropped. Asserted `tests/test_crowdstrike_assets_findings.py:133-149` | no |
| 9 | `CrowdstrikeAssetsFindingsCorrelated.py:204-205` | same predicate on the correlated lane | malformed correlated records dropped | no |
| 10 | `_FINDINGS_CORRELATION` (`NotHydrated.py:67-72`, `JoinType.LEFT`) via `correlate` (`$R/libs/packages/utilities/correlation.py:72-103`) | a finding whose `aid` matches no asset row | **orphan findings dropped.** Documented at `NotHydrated.py:13-15` — "the same convention every other split parser follows (asset-spine LEFT join; `correlation.py` has no RIGHT/FULL join)". Asserted `tests/test_crowdstrike_assets_findings.py:129-146` | no |
| 11 | `crowdstrikeAssetsFindings.py:127-131` | `F.explode(F.col("vulnerabilities"))` — deliberately **not** `explode_outer` (`:120-122`) | an asset with an empty/null `vulnerabilities` array contributes **no finding rows**; the asset itself survives | no |
| 12 | `CrowdstrikeAssetsFindingsCorrelated.py:226-237` | `filter(F.col("chunk") == 0)` then `row_number().over(Window.partitionBy("aid").orderBy(F.col("_record_order").desc())) == 1` | continuation envelopes discarded; a replayed host collapsed to its freshest copy; **but two genuinely distinct hosts sharing one `aid` → one is silently lost** | drift is **reported** by `_report_spine_identity_drift:239-335` (`ASSET COLLAPSE` / `ASSET DUPLICATION` at `logger.error`); the rows are still lost |
| 13 | `CrowdstrikeAssetsFindingsCorrelated.py:419-428` | `row_number().over(Window.partitionBy("id").orderBy(F.to_timestamp("updated_timestamp").desc_nulls_last(), F.to_timestamp("created_timestamp").desc_nulls_last(), F.col("_record_order").desc())) == 1` | duplicate vendor findings collapsed to the freshest copy. The docstring is explicit that the final `_record_order` tiebreak is **not** stable across runs (`:368-379`) | no |
| 14 | `CrowdstrikeAssetsFindingsNotHydrated.py:176-182` | a `_REQUIRED_FINDING_FIELDS` entry absent from the shard-inferred schema | raises `ValueError("... is missing required field(s) ...")` — job fails loudly rather than nulling the field. Asserted `tests/…:279-295` | exception |
| 15 | `crowdstrikeAssetsFindings.py:154-159` | input is neither correlated-shaped nor `input_mode == "split"` | raises `ValueError` | exception |

The Falcon **assets-only** lane has **no dedup at all.** `$R/test_files/crowdstrike/assets/assets.json`
contains `DESKTOP-IBE6812` three times with different IPs and all three survive as separate asset
rows in the snapshot.

### Tenable.io-specific

| # | location | exact predicate | effect | logged? |
|---|---|---|---|---|
| 16 | `tenableAssetsAndFindings.py:535` | `dropDuplicates(["_asset_correlation_id"])` | split / correlated: one asset row per assets-export id | no |
| 17 | `tenableAssetsAndFindings.py:538-542` | `_dedup_key = F.when(F.col("aid").isNotNull() & (F.trim(F.col("aid")) != ""), F.col("aid").cast(StringType())).otherwise(F.concat_ws("\t", F.col("type"), F.col("value")))`, then `dropDuplicates(["_dedup_key"])` | hydrated: duplicate assets collapsed by `aid`, falling back to `(type, value)` | no |
| 18 | `tenableAssetsAndFindingsCorrelated.py:89` | `full_df.filter(F.col("host").isNotNull())` | host-less continuation envelopes dropped. Commented as hygiene (`:79-88`): they would die at #17 then #1 anyway, and the filter suppresses a per-run "dropped N asset(s)" log line for a shape the collector legitimately emits | no |
| 19 | `tenableAssetsAndFindingsCorrelated.py:112` | `filter(F.col("findings").isNotNull() & (F.size(F.col("findings")) > 0))` then `F.explode` | envelopes with no findings contribute no finding rows; their asset survives | no |
| 20 | `tenableAssetsAndFindings.py:628-632` / `:643-647` | `"left"` join | a finding resolving no asset is **kept** with `asset_id = null` — then killed by #2 | no |
| 21 | `tenableAssetsAndFindings.py:441` | `input_mode` not in `{"split", "hydrated"}` | raises `ValueError` | exception |

### Not a drop

`helpers.fetch_df_from_file` (`$R/libs/packages/utilities/helpers.py:300-356`) does **not** filter
corrupt rows. It only reports counts (`:216-267`) and re-reads via text + `from_json` if *every*
column is `_corrupt_record` (`:347-354`). On the Falcon assets-only lane a corrupt row therefore
survives the read, produces a null `value`, and is dropped at #1 instead.

### For a rejection-counting normalizer

The drops with **no production log line at all** are **#2, #3, #10, #11, #19** — those are what
production silently discards. #1 logs a count. #12 logs a diagnostic but still loses the rows.
#3 is likely the largest by volume: any Falcon or Tenable finding without a CVE is dropped outright.

---

## 8. Q8 — `severity` and `cve_ids`, exact transformations

### Falcon `severity` — `crowdstrikeAssetsFindings.py:75-80`

```python
"severity": ColumnSchema(path="cve.severity", default_value=None,
                         transformation=lambda column: F.when(column == "LOW", "low")
                         .when(column == "MEDIUM", "medium")
                         .when(column == "HIGH", "high")
                         .when(column == "CRITICAL", "critical")),
```

Case-sensitive exact match, **no `otherwise` branch**. Anything outside the mapped set — including
Falcon's `NONE` and `UNKNOWN`, plus `null`, `""`, and casing variants like `"High"` — falls out of the
chain as `null`, is `coalesce`d against a `None` default (`base_parser.py:459`) so stays `null`, and
is then rewritten by `base_parser.py:274-276`:

```python
self.findings_df = self.findings_df.withColumn("severity", F.coalesce(F.col("severity"), F.lit("info")))
```

→ **`"info"`**.

**Refuting the other source:** this repo does **not** emit `NONE` or `UNKNOWN` as output severities.
Those are *input* values that land on `info`. There is no severity **enum validation** anywhere —
`base_parser.py:275` is the only severity transform in the shared base (repo-wide grep for `severity`
in `base_parser.py` returns only `:275`, `:581`, `:762`), so a mapped-but-invalid value would pass
through untouched. The reachable Falcon severity set is exactly
**`{critical, high, medium, low, info}`**.

Tenable.io by contrast **never** produces `info`: `.otherwise("low")` on `severity_id`
(`tenableAssetsAndFindings.py:346-353`) means unmapped ids and nulls become `low`. Reachable set:
**`{critical, high, medium, low}`**.

### Falcon `cve_ids` — `crowdstrikeAssetsFindings.py:96-98`

```python
"cve_ids": ColumnSchema(path="cve.id", default_value=None,
                        transformation=lambda column: F.when(column.isNotNull(), F.array(column))
                                                        .otherwise(F.array())),
```

A single-element array, or `[]`. Then in `BaseParser.post_process`:

1. `:262-269` — `F.coalesce(F.col("cve_ids").cast(ArrayType(StringType())), F.array().cast(ArrayType(StringType())))`, so null → `[]`.
2. `:270-273` — `F.expr("transform(cve_ids, x -> lower(cast(x as string)))")`.
3. `:286-295` — **row dropped** if `type == "vulnerability"` and `size(filter(cve_ids, x -> x is not null AND x != '')) == 0` (drop #3 above).
4. `:298-319` — `F.explode`: one row per CVE, `cve_ids` rebuilt as a one-element array, `id` regenerated.
5. `create_finding_source:556-559`, `:583` — the **published** column is the scalar
   `cve_id = F.col("cve_ids").getItem(0)`; the array is not written.

Asserted end-to-end at `tests/test_crowdstrike_assets_findings.py:56`
(`cve_id == "cve-2026-0001"`, lowercased) and `:143-145` (orphan dropped).

### Tenable.io `cve_ids` — `tenableAssetsAndFindings.py:368-369`

```python
"cve_ids": ColumnSchema(path="plugin.cve", default_value=F.array(),
                        transformation=lambda col: F.when(col.isNotNull(), col).otherwise(F.array())),
```

`plugin.cve` is already an array, so the transform only null-guards it. Cast to `array<string>` at
`:652-653`. Steps 1-5 above then apply identically — so a Tenable finding whose plugin lists several
CVEs **fans out into several exposure rows**, each with its own `id` and a single `cve_id`.

---

## 9. UNRESOLVED — the Falcon `site_names` mechanism

**Status: unresolved, not inferred.** Flagged explicitly because I could not settle it.

The CrowdStrike Discover asset feed **does** carry a scalar string `site_name` — present on 4 of the
50 rows in `$R/test_files/crowdstrike/assets/assets.json`, e.g. `"Default-First-Site-Name"` on the
`WIN-01`, `DC01`, and `ADCS` rows. Both the Python map (`crowdstrikeAssets.py:117-120`) and the YAML
spec (`crowdstrike-assets.yaml:46`) declare `path = site_name`, `default = F.array()`, and
`transforms.py:163-166` confirms the YAML `const: []` compiles to exactly that default. So
`extract_module_data` should evaluate `F.coalesce(F.col("site_name"), F.array())` — a `StringType`
coalesced against an `ArrayType`.

Yet **every** row of the asserted snapshot
`$R/test_files/crowdstrike/assets/assets.expected_assets.json` has `"site_names": []`, including the
rows whose `site_name` is a non-empty string.

What I could not determine:

- how Spark resolves `coalesce(<StringType>, <ArrayType>)` here rather than failing type coercion;
- whether the `[]` originates in that coalesce or later in
  `col_or_null_array` (`base_parser.py:629-632`:
  `F.coalesce(F.col(name), F.array()).cast(ArrayType(StringType()))`).

I could not test it: **there is no Java runtime installed in this environment**, so
`SparkSession.builder` fails with `RuntimeError: Java gateway process exited before sending its port
number` and no local Spark check is possible.

**What is certain:** the *behaviour* is snapshot-proven. `$R/tests/test_run_parsers.py:238-255` diffs
real Spark output against that file byte-for-byte, so `site_names == []` is real production output
for this lane, whatever the mechanism.

**Recommendation for the Node normalizer:** emit `site_names: []` for CrowdStrike/Falcon. Do **not**
wire `site_name` into it — doing so would diverge from production. If someone later wants
`site_name` in the output, that is a deliberate behaviour change and needs a re-blessed snapshot,
not a mapping fix.

---

## 10. Q1 — `display_name` on Falcon findings, in one line each

**Is Falcon findings `display_name` sourced from `vulnerability_id`?**
**Yes** — `$R/libs/packages/parsers/deprecated/crowdstrike/crowdstrikeAssetsFindings.py:73`:
`"display_name": ColumnSchema(path="vulnerability_id", default_value="", transformation=None)`.

**Does any parser anywhere in the repo source `display_name` from `apps` or a
product-name-plus-version field?**
**No** — `apps` is in `_HEAVY_FINDING_FIELDS = frozenset({"host_info", "apps", "suppression_info"})`
(`CrowdstrikeAssetsFindingsNotHydrated.py:51`) and is pruned from the findings read schema (`:169`),
so Spark never parses it; `product_name_version` occurs exactly once repo-wide, in
`$R/tests/test_crowdstrike_assets_findings.py:429`, as the payload the parser must skip.

### Supporting detail

`display_name` sources across the whole repo (grep over `libs/`, excluding the stale `build/lib`
copy): `vulnerability_id` (Falcon, `crowdstrikeAssetsFindings.py:73`), `plugin.name` (Tenable.io,
`tenableAssetsAndFindings.py:344`), `VULNERABILITY_INFO.TITLE` (Qualys,
`qualysAssetsAndFindings.py:162`), `Name` (Nessus Pro, `nessusProfessionalAssetsAndFindings.py:153`),
`vulnerabilityDetails.title` (InsightVM, `InsightVmAssetsAndFindings.py:133` and
`InsightVmCloudAssetsAndFindings.py:172`), `vulnerability.pluginName` (Tenable.sc,
`tenableScAssetsAndFindings.py:128`), `ruleName` (CloudGuard, `cloudGuardAssetsAndFindings.py:140`),
`name` (Wiz, `wizAssetsAndFindings.py:318`), `exposureName` (Defender VM,
`defenderVmAssetsFindings.py:172`), `display_name` (Cortex XDR, `cortexXdrAssetsAndFindings.py:193`).
Not one of them is `apps`-derived or a product-name-plus-version composite.

**`name == display_name` is a repo-wide convention, and it is not real duplication in the output.**
`name` is overwritten in post-processing by `add_unified_finding_name_column_for_vulnerability`
(`base_parser.py:521-527`):

```python
df = df.withColumn(
    "name",
    F.concat(F.lit("detected finding with cve id: "),
             F.expr("concat_ws(',', sort_array(cve_ids))"))
)
```

Called at `crowdstrikeAssetsFindings.py:308` **after** `super().post_process()` (`:305`) has already
exploded `cve_ids`, so the DB value is
`"detected finding with cve id: <single lowercased cve>"`. Thirteen call sites repo-wide, including
the YAML engine itself (`$R/libs/packages/parsers/yaml_engine/yaml_parser.py:512`),
`tenableAssetsAndFindings.py:680`, `qualysAssetsAndFindings.py:402`,
`nessusProfessionalAssetsAndFindings.py:351`, `InsightVmAssetsAndFindings.py:371`,
`defenderEndpointAssetsAndFindings.py:652`, `cortexXdrAssetsAndFindings.py:363`,
`tenableScAssetsAndFindings.py:329`, `defenderVmAssetsFindings.py:384`,
`InsightVmCloudAssetsAndFindings.py:394`, `csvExposuresParser.py:140`,
`wizAssetsAndFindings.py` family.

Snapshot proof of the split (same code path, Tenable):
`$R/test_files/tenable/assets_and_findings/findings.expected_findings.json` —
`name = "detected finding with cve id: cve-1999-0524"`,
`display_name = "icmp timestamp request remote date disclosure"`.

Most parsers give `name` and `display_name` the same source path
(`qualysAssetsAndFindings.py:161-162`, `nessusProfessionalAssetsAndFindings.py:152-153`,
`InsightVmAssetsAndFindings.py:132-133`, `tenableAssetsAndFindings.py:343-344`); the one exception is
`tenableScAssetsAndFindings.py:127-128` (`vulnerability.pluginInfo` vs `vulnerability.pluginName`).

**No comment anywhere explains the duplication.** Looked in `crowdstrikeAssetsFindings.py`
(lines 61-107 comment the inheritance and the dynamic additional_fields, nothing about
`display_name`), the `crowdstrike-assets-findings.yaml` header block, `$R/CLAUDE.md`, and `$R/docs/`.

---

## 11. Q2 — Falcon asset `value`, full coalesce logic

`crowdstrikeAssets.py:57-62`, mirrored byte-for-byte at
`$R/libs/packages/parsers/crowdstrike/crowdstrike_yaml_fns.py:30-35`:

```python
F.coalesce(
    F.when(F.col("hostname").contains("ip"), F.col("current_local_ip"))
     .otherwise(F.col("hostname")),
    F.col("current_local_ip"),
)
```

Then wrapped in `F.lower(...)` by `process_asset_mandatory_fields` (`base_parser.py:116-119`), and
lowercased again by R1.

`.contains("ip")` is a **case-sensitive substring test on the raw hostname**, not an IP-format check.
An uppercase hostname such as `"SHIP-01"` does not match; a hostname containing the lowercase
sequence, e.g. `"equipment-1"`, does.

Evaluation order:

1. `hostname` non-null and contains the literal `ip` → `current_local_ip`. If that is also null, the
   second coalesce arm is null too → `value` null → **row dropped**.
2. `hostname` non-null, no `ip` substring → `hostname`.
3. `hostname` null → the `when/otherwise` yields null → coalesce falls to `current_local_ip`.
4. Both null → `value` null → **row dropped** (`base_parser.py:184-198`).

**`fqdn` is NOT in the chain.** It is a separate output column (`crowdstrikeAssets.py:106-110`).
Contrast Tenable.io, where `fqdn` *is* third in `ASSET_NAME_PRECEDENCE`
(`tenableAssetsAndFindings.py:153`).

**Documented reason for the `current_local_ip` fallback** — `crowdstrikeAssets.py:52-56`, verbatim:

> Prefer hostname (using current_local_ip when the hostname is itself an IP), then fall back to
> current_local_ip when there is no hostname at all. Unmanaged/unsupported passive-discovery hosts
> carry only an IP; without this fallback their value is null and BaseParser.post_process drops them.
> Keeping the fallback here ensures no discovered asset is omitted.

Restated at `crowdstrikeAssetsFindings.py:61-64` and asserted by
`$R/tests/test_crowdstrike_assets_findings.py:101-126` (three assets survive with values
`{"host-1", "10.10.1.223", "192.168.1.59"}`). The production fixture makes the case: **37 of 50** rows
in `$R/test_files/crowdstrike/assets/assets.json` have `hostname: null` and land on their IP.

---

## 12. Q4 — where the lowercasing happens

Three separate mechanisms, all in `$R/libs/packages/parsers/common/base_parser.py`.

**`type`** — `:231`: `self.assets_df = self.assets_df.withColumn("type", F.lower(F.col("type")))`.
Comment at `:230`: *"Lowercase type to match asset_type enum (e.g. "Host" → "host")"*. Asserted at
`tests/test_crowdstrike_assets_findings.py:61` and `:508`.

**`os_type`** — `:233-253`, a full enum normalizer, not just a lowercase:

```python
_VALID_OS_TYPES = [
    "windows", "windows-server", "linux", "mac", "macos",
    "android", "ios", "gcp", "aws", "azure", "kubernetes",
    "active-directory", "ubuntu", "debian", "other",
]
_os = F.lower(F.col("os_type"))
F.when(_os.isin(_VALID_OS_TYPES), _os)
 .when(_os.startswith("windows-server"), F.lit("windows-server"))
 .when(_os.startswith("windows"),  F.lit("windows"))
 .when(_os.startswith("ubuntu"),   F.lit("ubuntu"))
 .when(_os.startswith("debian"),   F.lit("debian"))
 .when(_os.startswith("linux"),    F.lit("linux"))
 .when(_os.startswith("macos"),    F.lit("macos"))
 .when(_os.startswith("mac"),      F.lit("mac"))
 .when(_os.startswith("android"),  F.lit("android"))
 .when(_os.startswith("ios"),      F.lit("ios"))
 .otherwise(F.lit("other"))
```

Falcon `platform_name` outcomes, snapshot-confirmed across all 50 rows: `"Windows"` → `windows`,
`"Mac"` → `mac`, `"Linux"` → `linux`, and the `"Other"` default → `other`.

**Everything else** — `os_version` / `fqdn` / `os_build` explicitly at `:256-258`, then
`_lower_string_values` (`:791-806`) blanket-lowercases every remaining `StringType` and
`ArrayType(StringType)` column of both output frames, JSON blobs included (R1, §3).

---

## 13. Q5 — no parser here produces `windows-server` or `active-directory`

Production does pass `platform_name` straight through (`crowdstrikeAssets.py:64-68`,
`crowdstrike-assets.yaml:35`); the normalizer then folds it into `windows` / `mac` / `linux` / `other`.

I surveyed every `os_type` mapping in the repo — all 15 YAML specs and every `deprecated/` parser.
**Not one emits either hyphenated enum member.**

- **`windows-server`.** The branch at `base_parser.py:243` requires a **hyphen**. The closest producer
  is Defender VM (`$R/libs/packages/parsers/deprecated/defender_vm/defenderVmAssets.py:57-72`, mirrored
  at `$R/libs/packages/parsers/defender_vm/defender_vm_yaml_fns.py:25-30`), which extracts the leading
  alpha run and inserts a space at lower→upper boundaries: `"WindowsServer2019"` → `"Windows Server"`.
  Lowercased that is `"windows server"` — a **space**, so `startswith("windows-server")` misses and it
  falls through to `windows`. Falcon's own `"Windows Server 2019"` arrives on `os_version`, not
  `os_type`, and the snapshot shows `os_type = "windows"` / `os_version = "windows server 2019"`.
- **`active-directory`.** The only hits repo-wide are the *parser key* `active-directory`
  (`$R/libs/packages/parsers/yaml_engine/specs/active-directory.yaml:2`) and a URL inside a comment
  (`$R/libs/packages/parsers/deprecated/active_directory/activeDirectoryAssets.py:264`). That parser's
  own `os_type` map (`:142-155`) emits `"Windows"` when the first token of `operatingsystem` starts
  with `WINDOWS`, else `"Other"`; its user-type assets are hard-coded `"Other"` (`:560-563`).

**Stated plainly: both enum members exist for a producer other than these parsers.** Nothing in this
repo can emit them, and the `startswith("windows-server")` branch at `base_parser.py:243` is dead code
as the repo stands.

---

## 14. Q6 — what this repo does that a per-column mapping cannot express

`crowdstrike-assets-findings.yaml:16-21` and `tenable-assets-findings.yaml:14-19` both say the
shaping is "bespoke and not expressible in the declarative YAML vocabulary". Concretely, that means:

### Falcon findings

- **Run-time dual-shape detection.** `detect_correlated_shape`
  (`CrowdstrikeAssetsFindingsCorrelated.py:106-135`) reads the first non-empty NDJSON line of the
  first findings shard and tests for `_CORRELATED_MARKER_FIELDS = {"isLastChunk", "host"}` (`:88`).
  Any resolution/read/parse failure returns `False` and falls back to split mode (`:130-134`).
  Neither shape → `ValueError` (`crowdstrikeAssetsFindings.py:154-159`).
- **Schema-on-read pruning with a fail-loud guard.** The findings schema is inferred from one bounded
  shard, `host_info`/`apps`/`suppression_info` stripped (~69% of each doc,
  `NotHydrated.py:21-24`), then `_REQUIRED_FINDING_FIELDS = ("aid", "vulnerability_id", "status",
  "created_timestamp", "updated_timestamp", "cve", "remediation")` (`:57-60`) asserted present or the
  run **raises** (`:176-182`). Missing `cve` / `remediation.entities` sub-fields are back-filled as
  nullable string fields so the mandatory paths still resolve (`:226-293`).
- **Malformed-row isolation** via an explicit `_corrupt_record` column added to the read schema, then
  filtered; with a text + `from_json` fallback if nothing parses cleanly (`:199-223`, `:296-305`).
- **Embedded correlation + explode.** `correlate(assets, findings, CorrelationSpec(primary_key="aid",
  secondary_key="aid", join_type=LEFT, embed_as="vulnerabilities"))` (`:67-72`) collects matching
  findings into a per-asset array without duplicating the asset, `fill_missing_array`
  (`correlation.py:156-178`) coalesces unmatched nulls to a typed empty array, then
  `_explode_vulnerabilities` (`crowdstrikeAssetsFindings.py:109-131`) emits one finding row per
  vulnerability carrying its parent `_row_asset_id`.
- **Idempotence machinery on the correlated lane** — the per-`aid` spine dedup and the freshest-wins
  finding dedup (drops #12, #13 in §7), including the documented non-determinism of the final
  tiebreak (`Correlated.py:368-379`).
- **Empty-batch schema fabrication.** `_EMPTY_FINDINGS_SCHEMA` (`Correlated.py:95-97`) is handed back
  when a batch contains no finding objects at all, because `_finding.*` star-expansion otherwise dies
  at analysis time before a single row is read (`:380-406`).
- **Identity-drift reporting.** `_report_spine_identity_drift` (`Correlated.py:239-335`) counts
  distinct `host.id`, distinct `aid`, and distinct `(host.id, aid)` pairs over the `chunk == 0`
  records and logs `ASSET COLLAPSE` (pairs > aids) or `ASSET DUPLICATION` (pairs > hosts). It reports
  only; it never changes what is emitted.
- **A third output frame.** `_project_policies` (`crowdstrikeAssetsFindings.py:172-180`) emits
  `policies_df` via `project_prevention_policies` when `device_policies` is present, and
  `BaseParser._result_frames` (`base_parser.py:370-374`) then returns three frames instead of two.
- **Schema-driven dynamic `additional_fields`** (§4b) — the output key set is a function of the run's
  input schema, not of any spec.

The Falcon **assets-only** lane, by contrast, is fully declarative apart from the two-column `value`
derivation (`crowdstrike-assets.yaml:27-34`).

### Tenable.io

- **Three input generations behind one `parser_key`** with run-time routing:
  `hydrated` (legacy combined file), `split` (`/assets/export` + `/vulns/export`), and
  hydrated-but-correlated (collector v5+ envelopes), the last sniffed off the loaded schema by
  `is_correlated_envelope` (`tenableAssetsAndFindingsCorrelated.py:68-73`,
  `_ENVELOPE_COLUMNS = {"host", "findings", "isLastChunk"}` at `:54`); routed at
  `tenableAssetsAndFindings.py:412-441`.
- **Lane synthesis:** `_build_asset_envelope_df` (§5e) and `explode(findings)`
  (`Correlated.py:99-115`), plus the `host IS NOT NULL` filter (`:89`) — a whole reshaping layer
  before any field mapping runs.
- **Two different asset↔finding join strategies** (`tenableAssetsAndFindings.py:618-647`) and **two
  different dedup strategies** (`:527-542`), chosen at run time.
- **Schema-guarded field references throughout** — `_has_asset_struct` (`:196-207`),
  `_asset_has_field` (`:209-214`), `_has_nested_field` (`NotHydrated.py:37-58`). A mapping that
  references a missing nested path throws at Spark **analysis** time, so presence has to be tested
  before the column expression is even constructed. `_asset_name_column` (`:216-230`) and the
  `risk_score` transform (`:327-329`) are both written this way.
- **Python UDFs:** `process_tenable_tags` (`:57-77`), `normalize_date_string` (`:79-123`),
  `_process_ref_info_for_cves_internal` (`:21-55`).
- **Dynamic `additional_fields` for both assets and findings**, with per-sub-field flattening and
  selective `to_json` (§5b, §5d).

---

## 15. File index (absolute paths)

**Authoritative mapping sources**

- `/Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/common/base_parser.py` — answers Q3, Q4, Q7, Q8 and holds the output contract
- `/Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/deprecated/crowdstrike/crowdstrikeAssets.py`
- `/Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/deprecated/crowdstrike/crowdstrikeAssetsFindings.py`
- `/Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/deprecated/crowdstrike/CrowdstrikeAssetsFindingsNotHydrated.py`
- `/Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/deprecated/crowdstrike/CrowdstrikeAssetsFindingsCorrelated.py`
- `/Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/crowdstrike/crowdstrike_yaml_fns.py`
- `/Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/crowdstrike/crowdstrike_policy_projection.py`
- `/Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/deprecated/tenable/tenableAssetsAndFindings.py`
- `/Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/deprecated/tenable/tenableAssetsAndFindingsNotHydrated.py`
- `/Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/deprecated/tenable/tenableAssetsAndFindingsCorrelated.py`
- `/Users/user/Dev/cymulate-integration-parsers/libs/packages/utilities/correlation.py`
- `/Users/user/Dev/cymulate-integration-parsers/libs/packages/utilities/helpers.py`

**Specs and engine**

- `/Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/yaml_engine/specs/crowdstrike-assets.yaml`
- `/Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/yaml_engine/specs/crowdstrike-assets-findings.yaml`
- `/Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/yaml_engine/specs/tenable-assets-findings.yaml`
- `/Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/yaml_engine/specs/tenable-sc-assets-findings.yaml` — different product, not tabled here
- `/Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/yaml_engine/yaml_parser.py`
- `/Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/yaml_engine/transforms.py`
- `/Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/yaml_engine/delegate_hook.py`
- `/Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/schemas/db_schema.py`

**Ground-truth output snapshots** (diffed byte-for-byte by `tests/test_run_parsers.py:238-255`)

- `/Users/user/Dev/cymulate-integration-parsers/test_files/crowdstrike/assets/assets.json`
- `/Users/user/Dev/cymulate-integration-parsers/test_files/crowdstrike/assets/assets.expected_assets.json`
- `/Users/user/Dev/cymulate-integration-parsers/test_files/tenable/assets_and_findings/findings.expected_assets.json`
- `/Users/user/Dev/cymulate-integration-parsers/test_files/tenable/assets_and_findings/findings.expected_findings.json`
- `/Users/user/Dev/cymulate-integration-parsers/tests/test_crowdstrike_assets_findings.py`
- `/Users/user/Dev/cymulate-integration-parsers/tests/test_run_parsers.py`
- `/Users/user/Dev/cymulate-integration-parsers/tests/yaml_engine/_fixture_utils.py` — read this before trusting any snapshot detail; it blanks `client_id`/`instance_id`/`client_integration_id`/`created_at` and re-sorts JSON keys in `additional_fields`/`agent_metadata`/`device_metadata`

**Not found / not determined**

- No comment anywhere explaining the `name == display_name` duplication.
- No `product_name_version` or `apps` mapping source in any parser.
- No producer of `os_type` `windows-server` or `active-directory`.
- The Spark mechanism behind Falcon `site_names == []` — **UNRESOLVED**, see §9; behaviour is
  snapshot-proven, mechanism unexplained, no JRE available to test.
