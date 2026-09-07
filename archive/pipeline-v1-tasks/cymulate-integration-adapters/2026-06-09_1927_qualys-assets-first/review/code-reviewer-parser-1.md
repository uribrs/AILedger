# Code Review — Qualys assets-first decoupling

Scope: uncommitted change to `libs/packages/parsers/qualys/qualysAssetsAndFindings.py`
plus new `tests/test_qualys_assets_first.py`. Reviewed in isolation against PySpark
correctness and the rest of the parser package. No external spec assumed.

Environment note: no JRE / no pyspark on PATH in this sandbox, so the pytest suite
was **not** executed. All findings below are static reasoning, cross-checked against
the sibling split parser `crowdstrike/crowdstrikeAssetsFindings.py` and `base_parser.py`.

## Verdict

**Approve with minor reservations.** The core change is correct and idiomatic: it
mirrors the established crowdstrike split-parser pattern (`assets_source_df` /
`findings_source_df`, inner `F.explode`, `BaseParser.safe_struct`). The assets lane
now genuinely yields one row per host, and the inner explode correctly drops
exposure-less hosts from findings without leaking null rows. The FQDN fix is sound.
The reservations are robustness/maintainability, not correctness regressions — and
the two latent issues I found also existed in the pre-change code.

## Findings (severity-ranked)

### MEDIUM — All-exposureless input file will throw (latent, not a regression)
`qualysAssetsAndFindings.py:251-256`. The findings frame does
`selectExpr("HOST_DETAILS.*", "explode(DETECTION_LIST) as DETECTION")` then
`selectExpr("*", "DETECTION.*")`. If a file's hosts *all* have empty/null
`DETECTION_LIST`, Spark infers `DETECTION_LIST` as `array<string>` (or omits it),
making `DETECTION` a string — and `DETECTION.*` then fails with an AnalysisException
("can only star-expand struct types"). The new test masks this by always seeding at
least one host with a concrete detection (see the test's own comment at lines
100-101). This failure mode is identical in the old code (same explode chain), so it
is **not introduced** by this change — but the change is explicitly about making
exposure-less hosts first-class, so the all-empty file is now a realistic input and
deserves a guard (e.g. detect a non-struct `DETECTION_LIST` element type and short-
circuit `findings_source_df` to an empty frame). Recommend a follow-up, not a blocker.

### LOW — Curated `asset_additional_fields` / `finding_additional_fields` are dead
`qualysAssetsAndFindings.py:138-151, 211-224`. Both `@property` definitions are
required by the abstract base but are **never consumed** — `process()` builds
additional_fields by dumping every raw column lowercased (lines 285-288, 320-323),
not via `extract_module_data(df, self.asset_additional_fields)` the way crowdstrike
does (`crowdstrikeAssetsFindings.py:146-147`). This is pre-existing (the old code also
dumped `all_columns`), so not a regression, but it is misleading dead code: a reader
will assume the curated 11-field maps drive the output. Either wire them up (matches
house style, gives a stable additional_fields schema) or delete them. The raw-dump
approach also means additional_fields schema drifts with whatever HOST_DETAILS keys
the vendor sends — acceptable for a JSON blob column, but worth a conscious decision.

### LOW — Orphaned findings when NETBIOS and IP are both null
`qualysAssetsAndFindings.py:340-344` + `finding_mandatory_fields` asset_value
(lines 201-207). If a host has null NETBIOS and null IP, finding `asset_value` is
null; `lower(null)=null` never matches a lookup key, so the left join yields
`asset_id=null` and the finding survives orphaned. Symmetrically the asset `value`
is null and is dropped by `post_process` (`base_parser.py:168`). Net: orphaned
findings + a dropped asset. This is unchanged pre-existing semantics and low-
probability for Qualys hosts, but flagging since the review asks about null
asset_value behaviour. No row explosion risk: the lookup is deduped per
`_dedup_key` upstream (line 311), and the join keys (type, value) are unique per
asset after dedup, so the left join is at-most-1:1 — findings cannot multiply.

## Correctness items explicitly checked — PASS

- **One row per host:** assets frame is `selectExpr("HOST_DETAILS.*")` with no
  explode (line 244). Empty/null DETECTION_LIST hosts are unaffected. Correct.
- **Inner explode drops empties cleanly:** `F.explode` (not `explode_outer`) emits
  zero rows for empty/null arrays — no null-detection junk rows. Correct, and
  matches crowdstrike's documented choice (`crowdstrikeAssetsFindings.py:100`).
- **No top-level column collision:** mandatory output columns are aliased
  (`type`, `value`, `fqdn`, ...); raw host columns live *inside* the
  `additional_fields` struct. No HOST_DETAILS-vs-detection name clash at the
  select level. Correct.
- **FQDN null/empty-string safety:** transform (lines 116-119) is
  `coalesce(when(col.isNotNull() & col != "", col), col("DNS"))`. Empty string is
  treated as absent and falls back to DNS; both-empty yields default "". The dotted
  path `DNS_DATA.FQDN` resolves via `get_spark_full_schema` (base_parser.py:378),
  consistent with the findings' `VULNERABILITY_INFO.TITLE`. Correct.
- **Join case symmetry:** asset `value` is lowercased by `lowercase_transformation`
  (base_parser.py:115-118 wraps the value transform), and the lookup key `_lk_value`
  is that already-lowercased `value`; the finding side uses `lower(asset_value)`.
  Both sides lowercase — symmetric. `asset_type` ("Host") vs asset `type` ("Host"):
  the join runs in `process()` *before* `post_process` lowercases type, so both are
  "Host". Correct (but fragile — see note below).
- **additional_fields source separation:** asset additional_fields is built from
  `assets_source_df.columns` (HOST_DETAILS only); detection columns (qid,
  unique_vuln_id, results) cannot appear. The test asserts this directly
  (test lines 184-186). Correct.
- **Output schema drift:** final shaping goes through `create_asset_source` /
  `create_finding_source`, which project to fixed `_ASSET_OUTPUT_SCHEMA` /
  `_FINDING_OUTPUT_SCHEMA`. additional_fields is `to_json`'d to a single StringType
  column, so internal struct shape changes don't drift the table schema. Correct.

## Idiomatic / consistency — PASS
Matches the crowdstrike sibling: same field names, same `safe_struct` usage, same
inner-explode rationale, house `extract_module_data` for mandatory fields. The
`safe_struct` swap (from raw `F.struct(*cols)`) is a strict improvement — it guards
the empty-children encoder error. Comments are accurate and explain the *why*.

## Tests — GOOD, with one fragility note
The new test is meaningful and covers the stated behaviors:
- empty-DETECTION_LIST host becomes an asset with zero findings (test 1, a/b),
- additional_fields populated from HOST_DETAILS with no detection leakage (test 2),
- fqdn resolves from DNS_DATA.FQDN then falls back to DNS (test 3).

Fragility: every fixture deliberately includes one host *with* a detection to keep
`DETECTION_LIST`'s inferred element type a struct (test acknowledges this at lines
100-101). That means the all-exposureless file path (the MEDIUM finding above) is
never exercised. Consider one xfail/guarded test for that case once a guard is added.

## Maintainability note (non-blocking)
The join's correctness depends on asset `type` and finding `asset_type` both being
the literal "Host" at `process()` time, before `post_process` lowercases asset type.
That is a temporal coupling: if anyone moves type-lowercasing earlier, the join
silently produces all-null `asset_id`. A `lower()` on both `asset_type` and
`_lk_type` in the join predicate (mirroring the value side) would make it robust and
self-documenting at near-zero cost.

## Certainty
High on the PASS items and on the dead-code / orphaned-findings findings (pure static
trace, corroborated by the sibling parser and base_parser). Medium on the all-empty
explode failure — I could not execute pyspark here to confirm the exact exception,
but the `DETECTION.*` star-expand on a non-struct is well-established Spark behaviour.
