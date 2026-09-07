# A1 — Does a staging subfolder inside the read prefix get ingested by the parser?

Actor: researcher (P0 investigation, read-only) · Date: 2026-08-05 · Repo: cymulate-integration-parsers

## Verdict

**WOULD-BE-IGNORED — but conditionally, by naming coincidence rather than by structure.**

The parser never hands Spark a directory and never relies on Spark's recursion. Every read path is an
explicit file list assembled by boto3 (or `glob`) *before* Spark sees it. Discovery is a literal
**string-prefix** match with **no `Delimiter`**, which means it is a flat match across the whole
subtree — a subfolder is excluded only if its name does not share the matched prefix.

`.../{instanceId}/_staging/assets_000001.json` is tested against `.../{instanceId}/assets` and
`.../{instanceId}/findings`. It fails both, because the character after `{instanceId}/` is `_`.

## Evidence

| citation | what it shows |
|---|---|
| `libs/packages/utilities/helpers.py:216` | `multi_file_prefix = f"{key_prefix}/{prefix}_"` then `paginate(Bucket=..., Prefix=multi_file_prefix)` — literal prefix, no `Delimiter` |
| `libs/packages/utilities/helpers.py:239-243` | single-file path is `head_object(Key=f"{key_prefix}/{prefix}.json")` — exact key, not a listing |
| `libs/packages/utilities/helpers.py:281-296` | pattern strategy: `prefix = key_pattern.split("*", 1)[0]`, same undelimited listing; only extra filter is `key.endswith('.json')` |
| `libs/packages/parsers/yaml_engine/yaml_parser.py:239-243` | generic YAML parsers read via `helpers.fetch_df_from_file(input_path, …)`, never `spark.read.json(<dir>)` |
| `libs/packages/parsers/deprecated/crowdstrike/CrowdstrikeAssetsFindingsCorrelated.py:146-160` | dual-mode/delegate handlers use `helpers.fetch_df_from_strategy(...)` — same resolver |
| `libs/packages/utilities/input_resolver.py:44-60,140` | `assets*.json` / `findings*.json` joined as a single path segment under `base_file_path`, no `**/`; existence probe uses the same prefix-before-first-`*` check |
| repo-wide grep | zero hits for `recursiveFileLookup`, `pathGlobFilter`, `Delimiter=` under `libs/packages` — Spark directory recursion never enters the picture |

## The catch

The exclusion is not documented or enforced anywhere in the resolver. A staging folder named
`assets_staging/` or `findings-tmp/` **would** collide and be ingested as real input. `_staging/` is
safe today purely because it does not share the `assets`/`findings` prefix.

## Mitigations, ranked

1. **Put staging outside the read prefix entirely** — e.g. a sibling `.../{instanceId}-staging/`, or a
   separate top-level staging prefix. Makes the answer structurally WOULD-BE-IGNORED instead of
   "ignored because the name happens not to collide." Requires no parser change; it is purely a
   collector-side path choice. **Recommended.**
2. If it must live inside the prefix: reserve a leading `_` for non-parser artifacts and enforce it in
   CI/lint rather than leaving it as tribal knowledge, since nothing in the resolver documents it.
3. `pathGlobFilter` / `recursiveFileLookup` — **not applicable.** The codebase never calls
   `spark.read.json` on a directory, so there is nothing to configure.
4. Delete-before-parse ordering — fragile, and unnecessary if 1 or 2 is taken.

## Test

Cheap and Spark-free, at the resolver level. Write `assets.json` / `findings_000001.json` plus a
`_staging/assets_000001.json` decoy into `tmp_path`, then assert the decoy is absent from the resolved
file list via `resolve_assets_and_findings_contract(...)` or the local
`_resolve_local_paths_from_pattern`. Lives in `tests/test_input_resolver.py` or a new
`tests/test_helpers_file_resolution.py`.

The local-glob path is directly assertable (`glob.glob` does not recurse). In-process coverage of the
S3 branch (`_resolve_s3_paths_from_pattern`, `_resolve_s3_file_paths`) would need a `moto`-mocked
bucket — **`moto` is not currently a dependency; check before adding it.**
