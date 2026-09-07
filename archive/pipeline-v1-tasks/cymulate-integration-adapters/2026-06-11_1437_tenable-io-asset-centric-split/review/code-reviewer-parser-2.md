# Code Review — Tenable parser split-model alignment

**Scope reviewed:** `libs/packages/parsers/tenable/tenableAssetsAndFindings.py` (modified),
`libs/packages/parsers/tenable/tenableAssetsAndFindingsNotHydrated.py` (new),
`libs/packages/parsers/preparation.py` (modified), `tests/test_tenable_assets_findings_split.py` (new).
Surrounding context read: `utilities/input_resolver.py`, `parsers/common/base_parser.py`,
`utilities/helpers.py` (`fetch_df_from_file` / `fetch_df_from_strategy` / `_read_ndjson_via_text`),
and sibling DUAL_MODE parsers (Cortex XDR façade + NotHydrated, Defender VM presence).

**Change type:** shared library code (parser used by both LOCAL and AWS Glue jobs).
**Risk level:** Medium–High — data-plane code on a persistence boundary (asset/finding output),
schema inference over vendor NDJSON, a behavior fork in a shared input-resolution path.

**Environment note:** PySpark/pytest are not installed in this sandbox, so the new test was not
executed. Findings below are from static analysis + cross-reading the sibling implementations.

---

## Findings (ranked)

### 1. `ratings.acr.score` nested-field access is not schema-safe — AnalysisException risk on real exports — Major
**Problem.** In `_build_asset_envelope_df` the ACR v3 score is read as:
```python
ratings_acr_score = (F.col("ratings.acr.score") if "ratings" in cols else F.lit(None)).cast("double")
```
The guard only checks that the top-level `ratings` column exists, then navigates `ratings.acr.score`.
Spark infers the schema from the data it scans. If, in a given batch, every `ratings` value is
`null` (or the reparse path's single sampled row has `ratings: null`), `ratings` is inferred as a
null/non-struct type with no `acr` child — and `F.col("ratings.acr.score")` fails at *analysis time*
with `AnalysisException` (cannot resolve / not a struct), not as a null. The bulk `/assets/export`
realistically contains assets with absent or null ACR (the test's own A3 no-vuln asset models exactly
this), so a real export where the *whole* lane lacks populated ACR — or where the corrupt-record
reparse path samples such a row first (`_read_ndjson_via_text` infers from one row via
`schema_of_json`) — will hard-fail pre_process.
**Impact.** Job crash on a plausible production input shape; the very edge case the feature targets
(assets with no vuln/ACR data). The test passes only because A1/A2 carry `ratings`, so `ratings` is
inferred as a struct.
**Recommended fix.** Mirror the `_asset_has_field`/`opt` discipline used elsewhere in this same file:
resolve `ratings` against the schema and confirm `acr.score` is a navigable nested struct field
before emitting `F.col("ratings.acr.score")`; otherwise emit `F.lit(None).cast("double")`. A small
helper that walks `df.schema` for the nested path is sufficient. Local patch, not a refactor.

### 2. `F.col("asset.uuid")` on findings lane is unguarded — crashes on an empty/ACR-less findings batch — Major
**Problem.** In split mode `process()` does (line ~541):
```python
findings_columns_for_selection += [F.col("asset.uuid").alias("_finding_asset_uuid")]
```
unconditionally, and later `_normalize_tags`/field-maps assume the `asset` struct. If the findings
lane is empty or Spark fails to infer an `asset` struct (all-corrupt batch falling back to
`_read_ndjson_via_text`, which can return an empty `StructType([])`), `F.col("asset.uuid")` raises
AnalysisException.
**Impact.** Crash on an empty/degenerate findings lane — a normal state for an asset-centric pull
where some scan windows return assets but no new vulns.
**Evidence threshold.** Likely risk. The hydrated path shares the same latent assumption, so this is
not a *new* regression, but the split path widens exposure (an empty findings lane is now a supported,
expected input per the feature's own premise: "assets with no findings survive"). Worth a guard:
if `asset` is not a struct in `finding_source_df`, short-circuit findings to an empty frame.
**Recommended fix.** Local guard around the `asset.uuid` capture + the `asset.netbios_name`/`ipv4`
reads, consistent with `_asset_has_field`.

### 3. `assets_df.cache()` is never forced before reuse; split path materializes raw lanes twice — Minor
**Problem.** `assets_df` is `.cache()`d lazily and then consumed twice in split mode (build
`asset_id_lookup` for the join, and the `.drop("_asset_correlation_id")` publish frame) with no
intervening action to populate the cache. Separately, NotHydrated calls `.count()` on each raw lane
purely for a log line, forcing a load that the subsequent transforms re-trigger.
**Impact.** Redundant recompute/IO on large exports. Not incorrect — matches the pre-existing hydrated
pattern — but the sibling Cortex parser deliberately `repartition(1).persist()` + `count()` to
materialize once. Inconsistent with the better-behaved sibling.
**Recommended fix.** Either drop the bare `.cache()` (it buys nothing without an action) or force it
once after the dedup, as Cortex does. Defer-able; low operational cost at current data volumes.

### 4. Two divergent DUAL_MODE patterns now coexist — maintainability drift — Minor / Observation
**Problem.** Cortex/Defender use a clean façade: a `_get_preprocessor()` dispatcher returning
Hydrated/NotHydrated handlers, dedicated `assets_source_df`/`findings_source_df`, mode-agnostic
`process()`, and an explicit `raise ValueError` on unknown `input_mode`. Tenable instead inlines an
`is_split` boolean through a monolithic `process()` plus `_asset_schema_df`/`_finding_schema_df`
fallback properties and an inline `if mode == "split"` in `pre_process()` with **no** explicit
rejection of unknown modes (any non-"split" value silently falls through to hydrated).
**Impact.** Higher cognitive load and divergent mental model across the four DUAL_MODE parsers; an
unexpected `input_mode` (typo, future third mode) is silently treated as hydrated rather than failing
loud. The chosen seam is defensible (it reuses the canonical `asset_mandatory_fields` mapping without
duplicating it, which Cortex pays for by re-declaring fields), so this is a tradeoff, not a defect —
but the silent unknown-mode fallthrough is a small correctness wart worth a one-line guard.
**Recommended fix.** Add an explicit `elif mode not in (None, "hydrated"): raise ValueError(...)` in
`pre_process`, matching the sibling façades. Style/consistency otherwise — not merge-blocking.

### 5. Docstring overstates "preserving hydrated behaviour exactly" — Nit
`parse_tenable_timestamp` adds a 5th coalesce branch (`F.to_timestamp(column)`, ISO fallback) that now
also runs on the hydrated path. It is strictly additive (localized strings still fail the ISO parse →
coalesce unchanged), so behavior is a *superset*, not "exactly" preserved. Harmless; tighten the
comment.

---

## Correctness assessment of the core mechanism (positive)

- **LEFT-join direction is correct.** Findings LEFT-join onto the assets lane by
  `findings.asset.uuid == assets._asset_correlation_id` (raw `/assets/export` `id`). Assets are built
  independently from the assets lane, so no-vuln assets survive and no phantom/empty findings are
  fabricated — verified against `base_parser.post_process` (no finding-row → asset derivation), and
  the test asserts 3 assets / 2 findings with the no-vuln asset unlinked. Sound.
- **Exact-match (not lowercased) UUID correlation is the right call** — these are opaque Tenable
  UUIDs, unlike the hydrated `(type, value)` join which correctly lowercases user strings.
- **`options` plumbing is sound.** `Config` is a dataclass carrying `input_mode`/`assets_strategy`/
  `findings_strategy`/etc., so attribute access works; the strategy→path fallback in `_load_strategy`
  correctly resolves to `assets_file_path`/`findings_file_path` when strategies are None (the test's
  path), and `preparation.py` populates strategies in the real Glue flow.
- **`preparation.py` change is one registry line** — zero blast radius to the other three DUAL_MODE
  parsers or `input_resolver` (split-precedence, incomplete-contract `ValueError`, hydrated fallback
  all unchanged). No regression risk observed there.
- Null-safety helpers (`_first_or_null`, `_normalize_tags`, `opt`/`opt_array`) are correctly typed and
  empty-array-safe; tag reshape matches the embedded-asset `tag_*` shape `process_tenable_tags_udf`
  expects.

## Verdict
Mechanism is correct and the LEFT-join semantics match the asset-centric contract. **Findings #1 and #2
are the merge-blockers in spirit** — both are unguarded nested/struct field accesses that crash on
input shapes the feature explicitly claims to support (ACR-less / vuln-less assets, empty findings
lane), and the happy-path test doesn't exercise them. Fix those two with local schema guards
(reusing the file's own `_asset_has_field` pattern) before merge; #3–#5 are deferable.
