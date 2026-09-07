# Orchestration Plan

## Complexity Decision

- Path: **direct**
- Rationale: Four of six steps (T1, T2, T3, T5) modify one class, and they are
  sequentially dependent — T2 carries the source columns that T3 reads, T3's
  override must not collide with the `type` const T1 sets, and T5's redundancy
  call depends on what T2/T3 ended up emitting. T4 is a two-line DDL edit. Worker
  boundaries cannot be drawn without two agents editing the same method.

## Rubric Scores

| Axis | Score |
|---|---|
| Complexity | medium — one new override, four carried columns, a DDL edit |
| Separability | low — one class carries four of six steps |
| Coupling | high — T2 → T3 → T5 chain through the same frame |
| Dependency order | high |
| Execution risk | medium — the override sits *after* the base's lowercase/cast pass (A6) |
| Worker clarity | low |

## Research Decisions

None needed. A1–A5 are VALIDATED from direct reads of the EA contract and the
real STG payload. A6 and A7 are OPEN but concern *local* Spark/base-parser
behaviour, not external systems, and are resolvable by running the code — which
this environment can now do. Research would add nothing.

## Worker Plan

Not applicable — direct path.

## Synthesis Approach

Not applicable — direct path.

## Verification Obligations

- Cross-check every Success Criterion in `prompt_contract.md`.
- **A6 is the sharp risk**: `super().create_asset_source(df)` ends with
  `_lower_string_values` then `cast_columns_to_schema`. Columns appended after it
  get neither. Verify by *value*, not by presence: `cloud_platform` must read
  `'aws'` and `sub_type` `'virtual_machine'` on real data, per decision D-A.
- **A7**: confirm the four source columns survive the `process()` select — a
  column not named there is dropped before `create_asset_source` ever runs.
- Confirm `type` = `cloud_resource` and that the carried vendor type did not
  collide with, or overwrite, the mandatory `type` column.
- `git status` must show exactly three modified files. `base_parser.py` and every
  `*.expected_*.json` snapshot must be untouched — this is the load-bearing
  constraint behind decision D-C.
- A non-Wiz golden-snapshot case must pass with no snapshot regeneration.
- Real-data run must reproduce the predecessor invariants (4,809 asset rows,
  107,653 exposure rows, 0 duplicate ids, 0 null/dangling `asset_id`) and show
  the expected non-null counts on the four populated cloud columns.
- Tests must be genuinely executed. Spark works; an "unverified" claim is a
  contract violation this time.
