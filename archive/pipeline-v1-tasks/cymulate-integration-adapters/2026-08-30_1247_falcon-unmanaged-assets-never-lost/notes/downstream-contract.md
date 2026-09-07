# Downstream contract for AID-less correlated findings records

Read-only investigation, 2026-08-30. Repos:
`/Users/user/Dev/cymulate-integration-parsers` (Spark parsers) and
`/Users/user/Dev/cymulate-integration-adapters/.../FalconCollector/`.

## Verdict

**An AID-less record (null `aid` or absent `aid` key) is NOT safe to emit today. It breaks
the correlated parser in two distinct ways, one of them silent.** The change requires a
coordinated parser change, OR — much cheaper — the collector emits a non-null synthetic
correlation key instead of null.

Both failure modes were **reproduced empirically** against pyspark 3.3.0 (the repo's own
`.venv`, JDK 11) by replaying `_build_asset_spine`'s exact logic. Results below.

---

## 1. Which parser consumes the correlated records

Entry point chain:

- `libs/packages/parsers/yaml_engine/specs/crowdstrike-assets-findings.yaml:22-38` —
  parser_key `crowdstrike-assets-findings`, `dual_mode: true`, delegates via
  `parsers.yaml_engine.delegate_hook.delegate_to_original` to the hand-written class.
- `libs/packages/parsers/deprecated/crowdstrike/crowdstrikeAssetsFindings.py:50` —
  `CrowdstrikeAssetsFindingsParser`, the dual-shape façade.
- `crowdstrikeAssetsFindings.py:133-170` — `pre_process()` is the dual-shape branch.
  `detect_correlated_shape(self)` wins first; otherwise `input_mode` must be `"split"`
  or it raises `ValueError`.
- `CrowdstrikeAssetsFindingsCorrelated.py:91-120` — `detect_correlated_shape` peeks at the
  **first non-empty line of the first findings shard only** and tests
  `{"isLastChunk", "host"}.issubset(record.keys())`.
  An AID-less record still carries both keys, so **shape detection is unaffected**. Good.
- `CrowdstrikeAssetsFindingsCorrelated.py:123-315` — the correlated handler.
- Split (legacy) path: `CrowdstrikeAssetsFindingsNotHydrated.py`.

Job wrapper: `jobs/cybi-parser/script.py:95-105, 446-483` — accepts a 2- or 3-tuple
(`assets_df, findings_df[, policies_df]`).

## 2. What the parser does with `aid` — and what null does

`aid` is used for **four** things in the correlated path. Two are safe, two are not.

### 2a. FATAL, SILENT — asset-spine dedupe key (`Window.partitionBy("aid")`)

`CrowdstrikeAssetsFindingsCorrelated.py:192-221`:

```python
host_fields = [f.name for f in records_df.schema["host"].dataType.fields]
select_exprs = [F.col(f"host.{name}").alias(name) for name in host_fields if name != "aid"]
select_exprs.append(F.col("aid"))                      # :209  record-level aid replaces host.aid
...
latest_per_aid = Window.partitionBy("aid").orderBy(F.col("_record_order").desc())   # :215
return (spine.withColumn("_rn", F.row_number().over(latest_per_aid))
             .filter(F.col("_rn") == 1) ... )          # :216-221
```

This exists to make collector replay idempotent (one asset per replayed host). Spark groups
**all null keys into a single partition**, so every AID-less host collapses to exactly ONE
surviving asset row. 249 AID-less assets would land as **1**.

Reproduced (scratchpad `repro_nullaid.py`): 4 chunk-0 records (1 with `aid`, 3 with
`"aid": null`) → spine returned **2 rows**: `aid-1` and one arbitrary null-aid host (`h4`,
last in lane order). Two assets vanished with no error, no warning, no log line.

This is the single most dangerous finding: it is a **silent data-loss path**, not a crash.

### 2b. FATAL, LOUD — an all-AID-less shard has no `aid` column at all

If the `aid` key is **omitted** (rather than null) from every record Spark reads in a run,
JSON inference never creates the column and `F.col("aid")` at line 209 raises:

```
AnalysisException: Column 'aid' does not exist. Did you mean one of the following?
[host, chunk, findings, isLastChunk, findingsInChunk]
```

Reproduced. Note the asymmetry: in a **mixed** shard (some records state `aid`, others omit
the key) inference merges and the omitted ones become null — i.e. **omitting the key is
identical to null**, and you get failure 2a instead. Reproduced: 3 records, 1 with `aid`, 2
omitting it → 2 spine rows out of 3.

Relevance: `FalconCollectorConfiguration.BatchScopedStorage` scopes each aid-batch object
under `batch_%06d/`. If a parser run is ever scoped to one such batch and that batch is
entirely unmanaged, 2b fires. With the default flat run-root layout a managed host will
normally supply the column, so 2a is the realistic production mode.

### 2c. SAFE — the correlation join

`CrowdstrikeAssetsFindingsNotHydrated.py:64-72`:

```python
# Asset.aid <-> finding.aid. Embedded LEFT join keeps every asset (incl. the
# null-aid unmanaged/unsupported rows, which simply never match) and drops
# orphan findings whose aid has no asset row.
_FINDINGS_CORRELATION = CorrelationSpec(primary_key="aid", secondary_key="aid",
                                        join_type=JoinType.LEFT, embed_as="vulnerabilities")
```

`utilities/correlation.py:_embedded_join` uses a plain `==` equi-join, so `null != null`:
every null-aid asset survives with `vulnerabilities = []` after
`fill_missing_array`. **Verified empirically** — 3 assets in (1 aid, 2 null), 3 out, with
vuln counts 1/0/0. The join is not the problem, and the comment shows the split path was
written with null-aid unmanaged rows explicitly in mind.

### 2d. SAFE — the policy projection

`crowdstrike_policy_projection.py:70` — `ASSET_MATCH_KEY = "aid"`; read at `:263` as
`_asset_match_key`. Two guards make null-aid a no-op:
- `:271` `.filter(F.col("_external_id").isNotNull())` — an AID-less host has
  `prevention: null`, so it contributes no assignment row at all.
- `:369` `assigned.filter(F.col("_asset_match_key").isNotNull())` — even if it did, a null
  key emits no edge.
- `:243-247` — a batch where no host carries a Prevention assignment short-circuits to
  `_empty_policies` **before** `F.col("aid")` is ever selected. The comment at `:245-246`
  explicitly names this "the normal shape for an all-unmanaged page, not an error."

## 3. `asset_match_key` — and the real downstream asset identity

`asset_match_key` is a field inside the `policy_edges` JSON blob of
`integration.parser_output_policies`, built at `crowdstrike_policy_projection.py:349-375`.
Its value is `F.col("aid")` (`ASSET_MATCH_KEY = "aid"`, `:70`). The constant's own docstring
(`:66-69`) calls it "the join key ... most likely to need adjusting once create-entities
lands."

**It is not the asset identity.** Two prior REFUTED lessons establish that:

- `ai/done/2026-08-20_1101_.../lesson.md` L-f3058a37: "Real-artifact replay produced 47
  distinct AID edge keys against 43 distinct asset values with **zero matches**. EA joins
  `a.value = de.asset_match_key`, while the CrowdStrike parser emits hostname/current IP as
  the asset value — two different identity domains behind type-compatible columns."
- `ai/done/2026-08-27_0817_.../lesson.md` L-cc7eaeb4: re-refuted (verify query not re-run
  after the envelope emission site moved).

**The actual downstream asset identity is `value`.** Chain:

- `crowdstrikeAssets.py:49-63` — `value` = hostname, falling back to `current_local_ip`.
  The comment at `:53-56` is explicit: *"Unmanaged/unsupported passive-discovery hosts carry
  only an IP; without this fallback their value is null and BaseParser.post_process drops
  them. Keeping the fallback here ensures no discovered asset is omitted."*
- `base_parser.py:168-205` `_enforce_asset_output_contract` — the ONLY drop rule is on
  `value` (null / blank / control chars). Docstring: *"per the `parser_output_assets` schema
  only `id` and `instance_id` are NOT NULL — every other column ... is nullable, so a null
  there is valid and must NOT cause a drop."* **`aid` is not enforced.**
- `base_parser.py:589-661` `create_asset_source` final projection and `:718-743`
  `_ASSET_OUTPUT_SCHEMA`: **`aid` is not an output column.** It never reaches
  `parser_output_assets`. Inside the parser it is purely an internal join/dedupe key.
- The asset's own `id` is a generated UUID (`_row_asset_id`,
  `CrowdstrikeAssetsFindingsCorrelated.py:133`).

**Answer to "can an AID-less asset have a downstream identity at all": yes, unambiguously.**
Its identity is `value` = `current_local_ip`. The fix does NOT have to start downstream of
the parser. Exposure Analytics never sees `aid`.

Note also `crowdstrikeAssets.py:48` / `crowdstrike-assets.yaml:26`: the asset mandatory field
*named* `aid` maps from path `id` (the Discover combined id), **not** from the sensor `aid`
— and it is dropped by the output projection anyway.

## 4. `findings: []` — already works, and does NOT depend on aid

- `_explode_record_findings` (`CrowdstrikeAssetsFindingsCorrelated.py:224-315`) uses
  `F.explode`, not `explode_outer`, so empty arrays contribute no rows while the spine row
  stands (`:230-233`).
- `:280-292` handles the whole-batch case: if no record in the batch holds a finding object,
  the `findings` element type is not a struct and `_finding.*` would fail at analysis time;
  the handler returns an empty frame typed by `_EMPTY_FINDINGS_SCHEMA` instead. Covered by
  `tests/test_crowdstrike_assets_findings.py::test_correlated_all_zero_finding_batch_emits_assets_no_findings`.
- Zero-finding host covered by `::test_correlated_zero_finding_host_emits_asset_without_findings`
  — 2 records in, 2 assets out, 1 finding.
- None of this touches `aid`. **Confirmed: the `findings: []` path is aid-independent.**
- One caveat downstream of it: `base_parser.py post_process` raises
  `FindingsWithoutAssets` only when assets are empty while findings are not — not our case.

## 5. What the collector docs say about `aid`'s optionality

There is **no dedicated correlated-record contract doc** in `FalconDocs/CollectorDocs/`.
The record contract lives inline in two places, and both pin `aid` as present:

- `01-collection-strategy.md:104-114`, heading **"Record schema (pinned)"**:
  `{ aid, chunk, isLastChunk, findingsInChunk, host, findings[] }`. It documents `host` as
  the verbatim Discover record and the finding-stripping rules. **It says nothing about
  `aid` being optional or nullable.**
- `02-decision-making.md:66-77` "Chunked, self-complete correlated records" — same shape;
  the left-join guarantee is stated as *"Emitting one final record per host (even empty)
  gives downstream left-join semantics — every host in the batch appears as an asset spine
  whether or not it has findings."* Again silent on `aid` optionality.
- `06-prevention-policy-contract.md:233-234` covers only the policy envelope for a declined
  AID.

The parser's own docstrings restate the contract as `aid`-present:
`CrowdstrikeAssetsFindingsCorrelated.py:7-14` and `crowdstrikeAssetsFindings.py:14-16`.

The collector enforces it in code:
- `Flows/Findings/Correlated/FalconDiscoverHostScroller.cs:82-86` — a host whose extracted
  aid is null/whitespace is `continue`'d, i.e. dropped from the frozen work list.
- `Flows/Findings/Correlated/FalconCorrelatedRecord.cs:50-71` — `Build(..., string aid, ...)`
  is non-nullable and always writes `["aid"] = aid`.

**So: `aid` is currently a hard invariant of the correlated record. Nothing downstream was
built to tolerate its absence.**

### Correction to the task's premise, worth flagging

The scroller uses `AidExtractor.ExtractAid` (`AidExtractor.cs:71-83`), the **best-effort**
variant, not `ExtractSensorAid`. For a host with no sensor it derives a key from the combined
`id` when the suffix after `_` is exactly 32 hex chars (`:106-142`). So the record-level `aid`
is **already not always a sensor id** — for unmanaged entities it is a Discover *entity hash*.
The assets actually dropped are those whose combined `id` has no 32-hex suffix, which is a
narrower class than "has no sensor". This matters for the recommendation: the collector
already treats this field as "a per-host correlation key", not "the sensor AID".

## 6. The ASSETS lane already carries AID-less assets — and it is the clean precedent

**Yes, confirmed by fixture evidence.** `test_files/crowdstrike/assets/assets.json`:
50 rows, **38 with no `aid`**, 50 with an `id`; entity_type = managed 12 / unmanaged 12 /
unsupported 26. The golden output `assets.expected_assets.json` has **50 rows** — every
AID-less asset survives to output, and `aid` is not among its 24 keys.

How the assets lane identifies them: `value` (hostname → `current_local_ip` fallback), per
§3. No `aid`, no window dedupe on `aid`, no `aid` in the output schema.

The split assets+findings lane also handles them, with a dedicated test:
`tests/test_crowdstrike_assets_findings.py:100-124`
`test_crowdstrike_split_emits_unmanaged_asset_without_hostname` — 3 assets in (1 managed,
2 with `"aid": None`, no hostname), 3 assets out, values
`{"host-1", "10.10.1.223", "192.168.1.59"}`. Fixture builder `_unmanaged_asset_row`
(`:360-372`) is literally documented *"A passive-discovery row: no hostname, no aid, only
an IP."*

**The findings lane can absolutely reuse this identity scheme — the shaping code is shared
(`crowdstrikeAssetsFindings.py:61-64` inherits the asset field mappings verbatim from
`CrowdstrikeAssetsParser`). The only thing standing in the way is the correlated handler's
`Window.partitionBy("aid")` at `CrowdstrikeAssetsFindingsCorrelated.py:215`.**

Note: `_unmanaged_asset_row` is used only by the split test. **No correlated test covers a
null-aid record.** That is why 2a has gone unnoticed.

## 7. Test-coverage trap to be aware of

`tests/test_e2e_correlated_real_output.py:56-66` computes ground truth as
`expected_assets.add(rec["aid"])` for `chunk == 0` — a Python **set keyed on aid**. On
AID-less input this set collapses null-aid records exactly the way the parser's window
collapses them, so `assert assets_df.count() == len(expected_assets)` would **pass while
both sides are wrong**. It would also `KeyError` if the key were omitted rather than null.
Do not treat that test as evidence.

---

## Recommendation

Two options; the first is far cheaper and I'd take it.

**A. Collector fills `aid` with a stable non-null key for AID-less hosts (no parser change).**
Emit the Discover combined `id` (or `cid_id`) as the record's `aid` when no AID is derivable.
This is consistent with what the collector already does — `ExtractAid` already puts a derived
entity hash there for unmanaged hosts. Downstream consequences, all verified null:
- `aid` never reaches `parser_output_assets` (`base_parser.py:718-743`).
- `aid` is excluded from finding `additional_fields`
  (`crowdstrikeAssetsFindings.py:259-262`).
- A synthetic key cannot leak into `policy_edges.asset_match_key`, because an AID-less host
  has `prevention: null` and is filtered at `crowdstrike_policy_projection.py:271`.
- The correlation join simply finds no Spotlight findings for that key — the intended result.
Requires the key be **stable across runs** (Discover `id` is), or replay dedupe breaks.
Risk: it makes the pinned record contract's `aid` mean "correlation key", not "sensor id".
That is already true in practice; the docs at `01-collection-strategy.md:104-114` should be
updated to say so.

**B. Parser change first, then collector.** Replace the `partitionBy("aid")` dedupe key with
a null-safe per-host key (the host's `id`), guard `F.col("aid")` for the absent-column case,
and add correlated-lane null-aid tests mirroring the split lane's. Deploy-order matters:
the parser deploys ahead of the collector (stated at
`crowdstrike-assets-findings.yaml:4-5`), so this is the safe sequencing if you want `aid`
genuinely absent on the wire.

**Do not** emit null/absent `aid` from the collector alone. Failure mode 2a is silent
248-asset data loss with a green pipeline.

## What I could NOT determine

- **I did not run the real parser end-to-end on an AID-less correlated record.** The repo's
  Spark test harness needs `tests/conftest.py` fixtures and I was asked not to write code
  into the repo. What I reproduced is `_build_asset_spine`'s window logic verbatim, plus
  `correlate()` imported from `utilities.correlation` unmodified, on synthetic data
  (scratchpad `repro_nullaid.py` / `repro2.py`, pyspark 3.3.0 + JDK 11). The mechanism is
  proven; the full-pipeline row counts are inferred from it.
- **I did not verify the lab-tenant 249/298 figure or why those specific assets are dropped.**
  Given `ExtractAid`'s 32-hex-suffix fallback (`AidExtractor.cs:106-142`), the dropped set
  should be "combined `id` has no 32-hex suffix", which is *not* the same as "no sensor".
  Someone should confirm the actual `id` shape of the 249 before sizing the change — if some
  of them do have 32-hex suffixes they are already being emitted today.
- **Whether a parser run is ever scoped to a single `batch_%06d/` folder** (which would make
  failure 2b reachable) — that is job/orchestration config I did not trace.
- **Whether Exposure Analytics' `a.value = de.asset_match_key` join is still the live shape.**
  Both lessons that establish it are marked REFUTED/UNTESTED with their verify query never
  re-run. I read the parser side only; no database was touched.
- **Live EA behaviour for an asset whose `value` is a bare IP** (dedupe against assets from
  other connectors, etc.). Out of scope here; the assets lane already produces them today,
  so it is pre-existing behaviour, not new risk from this change.
