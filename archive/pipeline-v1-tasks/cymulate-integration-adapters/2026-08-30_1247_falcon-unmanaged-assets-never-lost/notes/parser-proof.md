# Parser proof — does an AID-less correlated record survive the Falcon parser?

**Date:** 2026-08-30
**Repo under test:** `/Users/user/Dev/cymulate-integration-parsers` (clean, no source modified)
**Method:** executed. Every claim below is an observed run output, not a reading of the code.
**Reproducer:** `/Users/user/Dev/cymulate-integration-adapters/notes/parser-proof-probe.py`
(drop into `tests/` in the parsers repo and run; it was removed after the run so the repo is left clean).

---

## VERDICT (read this first)

**The change is NOT safe in the collector alone if the collector emits `aid` as null, `""`, or an
absent key.** In that form the parser does not raise — it **silently collapses every AID-less record
in the batch into ONE asset row and drops the rest**. Measured at lab-tenant scale: **249 AID-less
assets in → 1 asset out, 248 silently dropped, exit code 0, no error, no warning.** That is the worst
possible outcome and it is the one that happens.

**There is a collector-only path that IS safe**, and it needs no parser change: emit a **unique
per-asset `aid`** for unenriched assets. `AidExtractor.ExtractAid` already produces exactly such a
value — the `<cid>_<hash>` suffix of the Discover combined `id` — for unmanaged/unsupported entities
(`.../Flows/Findings/Hosts/AidExtractor.cs:71-83`). Proven empirically below: 1 managed + 20
unenriched with unique derived AIDs → **21 assets out, 21 expected**, findings and policy edges
correct.

So the decision is not "one repo or two". It is: **choose the emitted identity in the collector.**
A unique key ships in one repo. A null/empty/absent key ships in two, and must not ship in one.

---

## 1. Existing tests pass

Environment note: the repo's documented `uv` is not installed on this machine and there is no
`java` on PATH. I used the repo's committed `.venv` (Python 3.9.6, pyspark 3.3.0, pytest 8.3.4) and
Homebrew `openjdk@11`. Real local Spark, same `tests/conftest.py` session fixture as CI.

```
cd /Users/user/Dev/cymulate-integration-parsers
JAVA_HOME=/opt/homebrew/opt/openjdk@11 PYTHONPATH=libs/packages \
  .venv/bin/python -m pytest tests/test_crowdstrike_assets_findings.py \
                             tests/test_crowdstrike_policy_projection.py -q
```
```
.............................................                            [100%]
45 passed in 73.36s (0:01:13)
```

Re-run after all probing, with the engine unit tests added, to confirm nothing was left behind:

```
JAVA_HOME=/opt/homebrew/opt/openjdk@11 PYTHONPATH=libs/packages \
  .venv/bin/python -m pytest tests/test_crowdstrike_assets_findings.py \
                             tests/test_crowdstrike_policy_projection.py tests/yaml_engine/ -q
```
```
124 passed in 71.72s (0:01:11)
```
`git status --porcelain` in the parsers repo: empty. Nothing was modified.

**Entry point exercised:** `CrowdstrikeAssetsFindingsParser` (`libs/packages/parsers/deprecated/
crowdstrike/crowdstrikeAssetsFindings.py`), reached through the `crowdstrike-assets-findings.yaml`
delegate spec. `detect_correlated_shape` routes a record carrying `isLastChunk` + `host` to
`CrowdstrikeAssetsFindingsCorrelated`. Full `pre_process → process → post_process` was run each time.

---

## 2. Per-variant result: what the parser actually did

Input in each case: one NDJSON lane, **2 managed hosts** (aid-1 with 2 findings, aid-2 with 1
finding, each with a `device_policies` prevention assignment) **+ 3 unenriched hosts** (`findings: []`,
distinct Discover `id`, distinct hostname/IP, `device_policies: {schema_version:1,
collection_status:"complete", prevention:null}`).

Expected: 5 assets, 3 findings, 1 policy with 2 edges.

### (a) `aid: null`

```
assets rows = 3
  ASSET {'value': 'unmanaged-13', 'type': 'host', 'ip_address': ['10.10.1.13'], 'fqdn': ''}
  ASSET {'value': 'host-1', ...}
  ASSET {'value': 'host-2', ...}
findings rows = 3
policies rows = 1
  POLICY external_id= policy-win  edges= [{"asset_match_key":"aid-1",...},{"asset_match_key":"aid-2",...}]
```
**3 assets, not 5.** `unmanaged-11` and `unmanaged-12` were silently dropped. No exception. Managed
hosts, findings and policy edges are all correct.

### (b) `aid: ""`

```
assets rows = 3
  ASSET {'value': 'unmanaged-13', ...}   ASSET {'value': 'host-1', ...}   ASSET {'value': 'host-2', ...}
findings rows = 3
policies rows = 1
```
Identical outcome. Two of three unenriched assets silently dropped.

### (c) `aid` key absent from the record

```
assets rows = 3
  ASSET {'value': 'unmanaged-13', ...}   ASSET {'value': 'host-1', ...}   ASSET {'value': 'host-2', ...}
findings rows = 3
policies rows = 1
```
Identical, **as long as at least one record in the batch carries `aid`** (Spark's JSON key-merge
supplies the column as null). See §3 for what happens when none does.

### Why: the exact line

`CrowdstrikeAssetsFindingsCorrelated._build_asset_spine`
(`libs/packages/parsers/deprecated/crowdstrike/CrowdstrikeAssetsFindingsCorrelated.py:215-221`):

```python
latest_per_aid = Window.partitionBy("aid").orderBy(F.col("_record_order").desc())
return (spine.withColumn("_rn", F.row_number().over(latest_per_aid))
             .filter(F.col("_rn") == 1)
             .drop("_rn", "_record_order"))
```

`partitionBy("aid")` puts **every null-aid row in one partition**, and `row_number() == 1` keeps
exactly one. The dedupe was written to absorb the collector's watermark re-anchor replay of a host's
`chunk == 0` record. It cannot tell "the same host twice" from "different hosts that share a
degenerate key", and it treats the latter as the former. `""` behaves the same (all empty strings are
one partition); `null` and `""` are two *separate* partitions, so a batch mixing both yields
**two** survivors, not one.

Mixed-form probe — 1 managed + 2 null-aid + 2 empty-aid:
```
### MIXED null + empty aid
    assets emitted: ['emptyaid-2', 'host-1', 'nullaid-2']
    findings emitted: 1
```
One survivor per falsy form.

### At realistic scale

49 managed + 249 unenriched (`aid: null`), mirroring the lab tenant:
```
### SCALE: input 49 managed + 249 unenriched (aid=null)
    assets emitted           = 50
    managed assets emitted   = 49 of 49
    unenriched assets emitted= 1 of 249  -> ['unmanaged-248']
    findings emitted         = 49 (expect 49)
```
The parser logged `CrowdStrike correlated parser: asset spine rows (chunk==0)=50` and exited
successfully. **Nothing in the run tells an operator that 248 assets vanished.** The fix that was
meant to raise the tenant from 49 assets to 298 would deliver 50.

### Which one survives is decided by lane order

Same 20 AID-less hosts, file written in forward vs reverse order:
```
### LANE ORDER asc:  20 AID-less hosts in -> survivors = ['unmanaged-19']
### LANE ORDER desc: 20 AID-less hosts in -> survivors = ['unmanaged-0']
```
Stable across 3 identical repeated runs of the same file (`unmanaged-19` each time), but the winner is
`_record_order` = `monotonically_increasing_id()`, which the module's own docstring (lines 256-265)
states is "**not** [deterministic] across runs — it encodes the partition index, which moves with file
listing order, split sizing and AQE." So in production the surviving unmanaged asset changes identity
between runs as Discover paging order and shard layout shift. Downstream that is one asset created
and deleted per run — churn, not a stable inventory.

---

## 3. The one case that fails loudly instead

A batch in which **no record at all** carries an `aid` key (entirely-unenriched page — routine for
Discover once managed hosts are a minority) has no `aid` column for Spark to merge, and the whole
Glue job dies:

```
### ALL-AIDLESS (aid key absent everywhere): EXCEPTION AnalysisException:
Column 'aid' does not exist. Did you mean one of the following?
[host, chunk, findings, isLastChunk, findingsInChunk];
```

Raised in `_build_asset_spine` at `select_exprs.append(F.col("aid"))` (line 209), during analysis,
before a row is read. The same batch with **`aid: null` on every record** does not raise — it emits
**1 asset for 3 input hosts**. So the absent-key form is strictly less dangerous than the null form:
it fails the run instead of publishing a wrong inventory.

---

## 4. `asset_match_key` and the identity question

This is where the prior "policy edges failed to resolve" evidence is confirmed, and it is a
**pre-existing defect independent of this change**.

`crowdstrike_policy_projection.py:70` — `ASSET_MATCH_KEY = "aid"`, read from the record-level `aid`
(the Falcon sensor AID). Observed edge for a managed host:

```
external_id= policy-win
edges= [{"asset_match_key":"aid-1","policy_type":"prevention","policy_slot":"prevention",
         "applied":true,"settings_hash":"settings-hash","is_effective":true,"via_host_group":null}]
```

Now the asset side, same run. The asset spine carries both identifiers:
```
--- spine aid/id/hostname rows:
    {'aid': None,    'id': 'DISCOVER-ID-2', 'hostname': 'UNMANAGED-2', 'current_local_ip': '10.10.1.99'}
    {'aid': 'aid-1', 'id': 'DISCOVER-ID-1', 'hostname': 'HOST-1',      'current_local_ip': '10.0.0.10'}
```
After `process()`, the asset frame's `aid` **column** is populated from the host's Discover `id`, not
from the Falcon AID (`crowdstrikeAssets.py:47` — `"aid": {"path": "id", ...}`):
```
--- assets_df AFTER process(), BEFORE post_process():
    intermediate: {'id': '25df58de-...', 'aid': 'DISCOVER-ID-2', 'value': 'unmanaged-2'}
    intermediate: {'id': 'a5adf366-...', 'aid': 'DISCOVER-ID-1', 'value': 'host-1'}
```
And then `BaseParser.create_asset_source` **drops it entirely**:
```
--- FINAL asset output columns: ['id', 'client_id', 'client_integration_id', 'instance_id',
    'integration_setting_id', 'integration_setting_flow_id', 'connector_flow_name', 'created_at',
    'first_seen', 'last_seen', 'ip_address', 'tags', 'type', 'value', 'os_type', 'os_version',
    'os_build', 'fqdn', 'group_names', 'site_names', 'additional_fields', 'agent_metadata',
    'device_metadata', 'risk_score']
--- 'aid' present in FINAL asset output? -> False
```
`aid` is not in `BaseParser._ASSET_OUTPUT_SCHEMA` and not in `parsers/schemas/db_schema.py` (grep for
`aid` there returns nothing). `integration.parser_output_assets` has no `aid` column.

**So:**

| | value |
|---|---|
| policy edge `asset_match_key` | the Falcon sensor AID (`aid-1`) |
| asset row's intermediate `aid` | the Discover combined `id` (`DISCOVER-ID-1`) — a *different* value |
| asset row's persisted identity | `id` (a fresh UUID per run) + `value` (hostname/IP) |
| `aid` in `parser_output_assets` | **absent** |

The policy edge's `asset_match_key` has **nothing to join to in the parser's asset output**, for
managed and unmanaged hosts alike. That is the prior failure, reproduced. It is orthogonal to the
AID-less change and will not be fixed by it.

**For an AID-less record specifically:** `asset_match_key` never gets a value at all, and correctly so —
`project_prevention_policies` filters on `_external_id.isNotNull()` (line 271) and again on
`_asset_match_key.isNotNull()` (line 369), and an unenriched host emits `prevention: null`, so it
contributes no policy row and no edge. Observed: with 2 managed + 3 unenriched, `policies rows = 1`
with exactly the 2 managed edges. **The policy lane is correct for AID-less assets today.** The
asset-spine collapse is the only defect the change introduces.

---

## 5. The ASSETS lane already carries AID-less assets — and identifies them without an AID

Run of the `crowdstrike-assets` spec over the committed real fixture
`test_files/crowdstrike/assets/assets.json`:

```
fixture rows=50  with-aid=12  aid-less=38
ASSETS LANE emitted rows = 50
ASSETS LANE final output columns: ['id', 'client_id', ..., 'value', 'os_type', ..., 'risk_score']
'aid' in final asset output columns? -> False
first 5 emitted: [{'id': 'd783d83a-...', 'value': 'win-01',                'type': 'host', 'fqdn': 'win-01.tbcsecurity.lab'},
                  {'id': '01006fbc-...', 'value': 'users-macbook-pro.local','type': 'host', 'fqdn': ''},
                  {'id': '681a1c8b-...', 'value': 'dc01',                  'type': 'host', 'fqdn': 'dc01.tbcsecurity.lab'},
                  {'id': '9184bcfd-...', 'value': 'win11-evandro',         'type': 'host', 'fqdn': ''},
                  {'id': '08e5f15b-...', 'value': 'desktop-ibe6812',       'type': 'host', 'fqdn': ''}]
distinct values count: 44 of 50
```

The fixture's `entity_type` breakdown is 12 managed / 12 unmanaged / 26 unsupported, and 38 of 50
rows have no `aid` key at all. **All 50 emit.** Confirmed by executing the parser, not by reading it.

The assets lane's identity scheme is **`value`**, derived by
`crowdstrike_asset_value` / `CrowdstrikeAssetsParser.asset_mandatory_fields["value"]`: prefer
`hostname`, use `current_local_ip` when the hostname itself contains "ip", fall back to
`current_local_ip` when there is no hostname. Plus a per-row generated UUID `id`. **No AID is
involved anywhere.** The lane has no `aid` output column and works fine without one.

**So yes — the identity scheme the findings lane needs already exists and is already in production
on the assets lane.** The blocker is not identity; it is that the correlated findings lane inserts an
`aid`-keyed dedupe *before* it ever reaches that identity, and that dedupe is what destroys the rows.
Note `distinct values count: 44 of 50` — the assets lane happily emits 6 rows sharing a `value`,
because it does not dedupe on identity at all. The findings lane does, on the wrong key.

---

## 6. Collector side: which `aid` form would actually be emitted

`FalconCorrelatedRecord.Build(JsonObject host, string aid, ...)`
(`.../Flows/Findings/Correlated/FalconCorrelatedRecord.cs:50-72`) takes a **non-nullable `string aid`**
and writes `["aid"] = aid` unconditionally, so **the key is always present**. The absent-key variant
would require a deliberate code change; passing null would serialize `"aid": null`.

`FalconSpotlightBatchPump.SeedAccumulators` (line 598) keys accumulators by `host.Aid`, a
non-nullable `string` populated from `AidExtractor.ExtractAid` (`FalconDiscoverHostScroller.cs:82`),
which **already falls back to the `<cid>_<hash>` suffix of the Discover combined `id` for unmanaged
entities** — a unique-per-asset value. `ExtractSensorAid` is the narrower one that returns null for
unmanaged entities, and it is used for the policy/device endpoints that reject a derived id.

That fallback is the lever. Two candidate emissions, both tested:

| collector emits as record-level `aid` | parser result |
|---|---|
| `null` / `""` / absent | **249 → 1**, silent (this document, §2) |
| `ExtractAid`'s derived `<cid>_<hash>` (unique per asset) | **correct** (below) |

Probe: 1 managed + 20 unenriched, each unenriched carrying a unique `derived-<hex>` record-level `aid`
and `host.aid` still absent:
```
### UNIQUE DERIVED AID: input 1 managed + 20 unenriched
    assets emitted = 21 (expect 21)
    findings       = 1 (expect 1)
    values         = ['host-1', 'unmanaged-0', ..., 'unmanaged-9']
    policies       = 1
      edges: [{"asset_match_key":"aid-1", ...}]   # only the managed host — correct
```
Every asset emitted, the managed host's finding attributed correctly, and the policy edge set is
exactly the managed host — the unenriched hosts correctly contribute no edge.

---

## 7. Where the parser produces a WRONG result rather than failing

Ranked worst first. These are the answers to "what do you most want to know about".

1. **Silent mass asset loss.** N AID-less records in a batch → 1 asset row out. No exception, no
   log line, successful exit. The parser even logs a confident count (`asset spine rows
   (chunk==0)=50`) that is the *post*-collapse number, so the log corroborates the wrong answer.
   Measured 249 → 1.
2. **Non-deterministic survivor identity.** Which single AID-less asset survives is decided by
   `monotonically_increasing_id()` ordering, which the code's own docstring says is not stable across
   runs. Proven order-sensitive here (asc → `unmanaged-19`, desc → `unmanaged-0`). Downstream, the
   surviving unmanaged asset is a different real machine each run: create/delete churn on every sync,
   with no signal that anything is wrong.
3. **Two survivors, not one, if the collector ever mixes null and `""`.** `null` and `""` are
   distinct Spark partition keys. Proven. A future refactor that changes which falsy form is emitted
   silently changes the output row count — a "cosmetic" collector change with an invisible data
   effect.
4. **`asset_match_key` cannot resolve, for any host.** Policy edges carry the Falcon sensor AID;
   `parser_output_assets` carries no AID column at all (its intermediate `aid` is the Discover `id`,
   and it is dropped before write). Pre-existing, orthogonal to this change, but it means "policy edges
   resolve" is not a property this change can be validated against until it is fixed separately.
5. **Findings are never mis-attributed.** Worth stating because it was a plausible fear and it is
   false: correlation is an equi-join on `aid`, and Spark equi-joins do not match null to null, so an
   AID-less asset cannot inherit another host's findings. In every mixed run the finding counts and
   `asset_id` attributions were exactly right. `findings emitted = 49 (expect 49)`.

The only loud failure — `AnalysisException: Column 'aid' does not exist` on a wholly-AID-less batch
with the key absent — is, perversely, the *good* outcome. It stops the run instead of publishing a
50-asset inventory for a 298-asset tenant.

---

## 8. What this means for shipping

- **If the collector emits a unique per-asset `aid` for unenriched assets** (the value
  `AidExtractor.ExtractAid` already computes): **collector-only change, no parser change required.**
  Proven by execution. Recommended.
- **If the collector emits `null` / `""` / absent:** **must not ship without a coordinated parser
  change first.** The parser change is small and localised — `_build_asset_spine` must not collapse
  rows that share a degenerate key; it needs a per-row fallback key (the host's Discover `id`, which
  is present on every record and is already what the asset `value`/identity derives from) so the
  replay dedupe still works for real AIDs without merging distinct hosts. It would also need to stop
  requiring the `aid` column to exist (the wholly-AID-less-batch crash).
- **Independently of either:** the `asset_match_key` ↔ asset-output mismatch in §4 is a real,
  reproduced defect. Fixing it is a separate piece of work in the parsers repo and should not be
  folded into this change.
