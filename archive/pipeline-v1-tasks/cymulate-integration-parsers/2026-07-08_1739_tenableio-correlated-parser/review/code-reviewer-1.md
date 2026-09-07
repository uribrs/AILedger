# Code Review — commit 56d2e45 (feature/tenableio-correlated-parser)

Reviewer: code-reviewer skill (isolated). Code-reading review only; no pytest/spark executed.

## Calibration

- **Change type:** feature logic in a production parser (shared parsing pipeline, third input generation for Tenable.io).
- **Risk level:** Medium-High — large-data Spark path (17GB-class inputs), schema-inference-driven routing, join/dedup semantics feeding persistence, but no auth/concurrency/distributed-coordination surface.
- **Scope reviewed:** `libs/packages/parsers/deprecated/tenable/tenableAssetsAndFindingsCorrelated.py` (new), `tenableAssetsAndFindings.py` (hydrated-branch sniff + `correlates_by_uuid` refactor), `tenableAssetsAndFindingsNotHydrated.py` (reused reshaper, unchanged), `yaml_engine/specs/tenable-assets-findings.yaml` (comment-only), `tests/test_tenable_assets_findings_correlated.py`, fixtures under `test_files/tenable/assets_and_findings/`, plus `common/base_parser.py`, `utilities/helpers.py`, `utilities/input_resolver.py`, `parsers/preparation.py`, `tests/test_run_parsers.py` as surrounding context.

## Verdict

**Approve.** No blockers, no majors. The routing is a schema-only sniff with a conservative grammar; the two pre-existing generations are bit-identical by construction; the empty-findings and corrupt-record edges are handled deliberately. Findings below are minors and observations.

## Regression trace (pre-existing generations)

Verified by reading, not asserted from the commit message:

- **Legacy hydrated:** the only new work on this path is `is_correlated_envelope(self.full_df)` — a `df.columns` / `df.schema["host"].dataType` inspection, no Spark action, no plan change — plus a log line. `_correlated_input` stays `False` (set in `__init__`, so `process()` is safe even if `pre_process()` is skipped), making `correlates_by_uuid == is_split == False`; every rewritten gate in `process()` (`tenableAssetsAndFindings.py:498,517,586,605`) reduces to the exact prior `is_split` behavior. Bit-identical.
- **Split:** `input_mode == "split"` short-circuits before the hydrated branch; `correlates_by_uuid == is_split == True`; same gates, same expressions. Bit-identical.
- The sniff cannot false-positive on legacy combined rows: they are finding-shaped (`asset`/`plugin`/`severity_id` roots) and never carry top-level `host` + `findings` + `isLastChunk`. Pinned by `test_tenable_correlated_sniff_leaves_legacy_hydrated_untouched`.

## Production-path check (beyond the tests' explicit paths)

The tests inject `findings_file_path` directly; I traced the production resolution too. A correlated run emitting `findings_<n>.json` (no literal `findings.json`) resolves `input_mode="hydrated"` with `findings_file_path = <base>/findings.json` (`preparation.py:103`, `hydrated_path` is `None`). That path does not exist on disk — but `helpers.fetch_df_from_file` → `_resolve_file_paths` transparently falls back to the `findings_*.json` multi-file pattern (local and S3), so multi-file correlated lanes load correctly and the sniff runs on the union-inferred schema. No gap found; noting it because the correctness here rests on that helper fallback, not on anything in this commit.

## Findings

### 1. Minor — multi-chunk asset dedup assumes identical `host` snapshots across chunks

- **Problem:** a multi-chunk host contributes one asset row per chunk envelope to the assets lane; `dropDuplicates(["_asset_correlation_id"])` (`tenableAssetsAndFindings.py:522`) keeps an arbitrary one. If the collector ever writes drifting `host` content across a host's chunks (e.g. `last_seen` refreshed mid-export), the surviving asset row's fields are nondeterministic run-to-run.
- **Impact:** none today if the collector duplicates the same host object per chunk (the tests and fixture assume exactly that); silent nondeterminism if that invariant ever breaks.
- **Fix:** document the invariant ("collector repeats an identical `host` object on every chunk of a host") in the `tenableAssetsAndFindingsCorrelated.py` module docstring where the collapse behavior is described. A deterministic pick (e.g. max by `chunk`) is not warranted now.
- **Refactor or patch:** local patch (doc comment).

### 2. Minor — sniff false-negative on a pathological batch falls through to a path that throws

- **Problem:** if a correlated lane's `host` is null on every row of a batch (or the batch is corrupt-dominated so `host` infers as non-struct), `is_correlated_envelope` returns `False` and the envelope data proceeds down the legacy hydrated path. That path has no `asset` column, and the `value` mandatory-field lambda resolves `F.col("asset.ipv4")` unguarded (`tenableAssetsAndFindings.py:236-243`) — AnalysisException at plan time.
- **Impact:** hard failure with a Spark analysis error rather than a targeted "unrecognized hydrated shape" message. The all-corrupt case (`_corrupt_record`-only frame) is already absorbed upstream by `helpers._read_ndjson_via_text`; the residual trigger is an all-null-`host` batch, which a healthy collector should never emit. The same failure shape pre-exists for any assetless legacy hydrated input — this commit does not introduce it, it inherits it.
- **Fix:** none required now. If envelope batches ever surface with degraded `host` columns, extend the sniff to also match `host` absent/non-struct when `findings`+`isLastChunk` are present, and fail with an explicit error.
- **Refactor or patch:** defer.

### 3. Minor — `_explode_findings` pre-filter is redundant

- **Problem:** `filter(findings.isNotNull() & (size(findings) > 0))` before `F.explode` (`tenableAssetsAndFindingsCorrelated.py:91-95`) is a no-op: non-outer `explode` already drops rows with null or empty arrays.
- **Impact:** none functionally; one extra predicate in the plan. Arguably it documents intent (no-vuln envelopes excluded by design).
- **Fix:** optional — drop the filter or keep it as intent documentation. Either is fine.
- **Refactor or patch:** local patch, optional.

### 4. Observation — `full_df` is scanned twice; not caching it is the right call

Both lanes derive from `parser.full_df` (assets via `select("host.*")`, findings via `explode`), so downstream actions re-read/re-parse the JSON lane once per lane (plus the schema-inference pass). For a 17GB lane that is real I/O, but caching a frame of that size is worse than re-scanning, and the shape matches split mode's two-file cost. The handler also deliberately avoids the `count()` log lines the NotHydrated path pays. No change requested.

### 5. Observation — `_EMPTY_FINDINGS_SCHEMA` anchor is a deliberate, slightly coupled trick

The single `state` column exists to satisfy `BaseParser.safe_struct`'s bindable-column requirement and to anchor literal expressions (`base_parser.py:377-390`); `state` is also a mandatory root path, so it is correctly excluded from `additional_fields` on the empty lane. The in-code comment explains the constraint. Fine as-is; if `safe_struct` semantics ever change this is the coupling point.

### 6. Observation — reaching into `TenableAssetsAndFindingsNotHydrated._build_asset_envelope_df`

Cross-class use of a name-private static method. Within this three-file family it is the intended reuse seam (the docstrings on both sides say so), and promoting it to a module-level function would be churn without benefit. Acceptable.

## Test quality

The new suite pins the load-bearing behaviors with exact-count and exact-linkage assertions, not smoke checks:

- multi-chunk collapse: 3 hosts in / 3 assets out, all three of C1's findings across both chunks resolve to the *same* asset row;
- no-vuln survival: asset present, provably zero findings pointing at it, null `risk_score` for absent ratings;
- all-zero-findings batch: exercises the `_EMPTY_FINDINGS_SCHEMA` branch end-to-end (assets kept, `findings_df.collect() == []`);
- envelope-field hygiene: `chunk`/`isLastChunk`/`host` asserted absent from both frames' `additional_fields`;
- legacy sniff regression: `_correlated_input is False` plus full legacy pipeline output.

The golden fixture is not dead weight: `tests/test_run_parsers.py` discovers every `findings*.json` per spec dir, so `findings_correlated.json` runs as its own harness case, hydrated-classified, and diffs against the two `expected_*` snapshots — this covers the production-style path the unit suite injects around.

Loose ends (none merge-blocking):

- `finding_af = json.loads(findings[0]["additional_fields"])` indexes an order-nondeterministic `collect()`; the assertion happens to hold for every row here, but an all-rows check would be self-evidently stable.
- No correlated-lane test with interleaved corrupt/garbage lines (partial `_corrupt_record` column alongside the envelope columns). The corrupt rows would flow as null-`host` assets and be dropped by `_enforce_asset_output_contract`; a test would pin that.
- `is_correlated_envelope`'s non-struct-`host` rejection branch has no direct test (only the legacy-shape negative case).

## Fixture hygiene

`findings_correlated.json` (27KB) and the two snapshots (5KB / 48KB) are properly sanitized: RFC-5737 TEST-NET IPs (`192.0.2.x`), `*.example.test` hostnames, placeholder tags, `"sanitized plugin output"`, no credentials/PII spotted in the sampled content. Sizes are proportionate to the four-envelope scenario.

## Maintainability of the three-way dispatch

The dispatch stays legible: `input_mode` owns split-vs-hydrated (resolver-decided, filesystem fact), and the schema sniff owns the hydrated sub-generation (content fact) — two different knowledge sources, so two-stage routing is the honest shape, not complexity creep. `correlates_by_uuid` as a derived local keeps `process()` reading as "two correlation strategies" rather than "three modes". The correlated handler mirrors the NotHydrated handler's structure (thin class over the parser, lane derivation only), so the extension pattern is now established rather than ad hoc.
