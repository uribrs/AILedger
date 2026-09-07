# Execution Notes

Appended during execution. One section per step id (T1-T6).

## T1 — Asset type -> cloud_resource

_pending_

## T2 — Carry the four cloud source columns

_pending_

## T3 — Wiz-local create_asset_source override

_pending_

## T4 — DDL columns

_pending_

## T5 — additional_fields redundancy decision

_pending_

## T6 — Tests + real-data proof

_pending_

---

# Execution — DIRECT path, all steps complete

Three files changed: `wizAssetsAndFindings.py`, `db_schema.py`,
`tests/test_wiz_assets_findings_split.py`. `base_parser.py` untouched; zero
snapshot files changed (verified by `git status`).

## T1 — Asset type -> cloud_resource — DONE

Mandatory `type` const `"Host"` -> `"cloud_resource"`. Comment replaced: the old
one asserted the opposite ("cloud_resource is written by nothing"), which was true
of this repo but wrong about EA.

## T2 — Carry the four cloud source columns — DONE

`process()`'s asset select now also carries `cloud_platform`, `cloud_account_id`,
`cloud_account_name`, and `type` aliased to **`sub_type`**.

The collision hazard was real and is handled by construction: the select runs
against `assets_src`, so `F.col("type")` resolves to the SOURCE vendor type
(`VIRTUAL_MACHINE`, `BUCKET`, ...), while the mandatory output `type` column is an
alias produced inside `asset_cols`. They never reference each other. Proven on real
data — `type='cloud_resource'` while `sub_type` carries 20+ vendor values.

`type`, `cloud_account_id` and `cloud_account_name` were added to
`_ASSET_REQUIRED_COLS` so a sparse batch cannot crash the select.

## T3 — Wiz-local create_asset_source override — DONE

The override had to solve a two-sided problem that assumption A6 only half
captured:

- The cloud columns cannot ride *through* `super()`: its select is a fixed column
  list, so it DROPS them.
- They cannot simply be appended *after* `super()` either: it ends with
  `_lower_string_values` then `cast_columns_to_schema`, so anything attached
  afterwards keeps vendor casing and skips the control-character strip.

So the override lifts `id` + the six columns off the input frame first, calls
`super()`, re-attaches on `id` (unique — it is the spine UUID), then applies
`_normalize_output_string` (lower + control-strip, mirroring the base) to each.
`region` and `cloud_provider_url` are emitted as typed NULL string columns so
EA's data-gated satellite INSERT sees them.

## T4 — DDL columns — DONE

Six nullable `text` columns added to `parser_output_assets_create_table_schema`.

## T5 — additional_fields redundancy — DECIDED: removed three of five

Removed `"Cloud Resource Type"` (path `type`), `"Cloud Platform"` (path
`cloud_platform`) and `"Cloud Account"` (path `cloud_account_name`) — each now has
a first-class column feeding EA's satellite, so keeping them would duplicate the
same value into the `additional_fields` JSON blob.

Kept `"Cloud Provider"` (source `cloud_provider`) and `"Subscription"` (source
`subscription_external_id`): neither is among the six staging columns, so removing
them would lose data. Note the Wiz lane carries BOTH `cloud_platform` and
`cloud_provider`; only the former is EA's staging column.

## T6 — Tests and real-data proof — DONE, ALL EXECUTED

```
pytest tests/test_wiz_assets_findings_split.py -v      6 passed in 31.08s
pytest tests/test_run_parsers.py -k qualys -v          2 passed, 48 deselected
git status --short --untracked-files=all               exactly 3 files, no snapshots
```

New test `test_wiz_split_emits_cloud_resource_type_and_cloud_columns` asserts
`type == 'cloud_resource'`, all six columns present, the four populated and
lowercased, the two NULL, and `sub_type != type`.

Real-data run (4,809 assets / 107,653 findings from the STG lanes):

```
ASSET ROWS    : 4809          EXPOSURE ROWS : 107653
duplicate asset ids           : 0
findings with NULL asset_id   : 0
findings pointing at no asset : 0
assets with ZERO findings     : 4724

asset.type : ['cloud_resource']

sub_type            non-null 4809/4809   bucket, secret, route_table, replica_set,
                                         raw_access_policy, kubernetes_custom_resource_definition
cloud_platform      non-null 4789/4809   aws, azure, github, kubernetes, wiz
region              non-null 0/4809
cloud_account_id    non-null 4752/4809
cloud_account_name  non-null 4752/4809   (includes '' — EA NULLIFs it)
cloud_provider_url  non-null 0/4809
```

Every predecessor invariant from commit f230eee is intact.

## Assumptions resolved

- **A6 — VALIDATED, and it was worse than written.** The write-up assumed the only
  hazard was skipping lowercase/cast on appended columns. The larger problem is
  that `super()`'s fixed select drops them entirely, so a naive append would have
  produced a `column not found` failure, not merely wrong casing. Closed by
  execution: real data shows `cloud_platform='aws'` and
  `sub_type='virtual_machine'` — lowercased exactly per decision D-A.
- **A7 — VALIDATED.** The four source columns survive `process()`'s select;
  non-null counts match the source (4,789 / 4,752 / 4,752 / 4,809).

## Observations worth the operator's attention

- `cloud_platform` real values include **`github`** and **`wiz`**, not just the
  AWS/Azure/GCP/Kubernetes assumed when scoping. Harmless — EA stores this column
  as free text precisely so a new vendor platform needs no migration — but it does
  mean the parser's `os_type` mapping folds both to `other`.
- `cloud_account_name` contains empty strings for some accounts. Passed through
  as-is; EA applies `NULLIF(x,'')`.
- 20 assets have no `cloud_platform` and 57 no `cloud_account_id`. Those still get
  a satellite row via the other non-null columns, since EA's gate is per-row across
  all six.

---

# Repair Round 1 — after code-reviewer-1

Code review returned 1 blocker, 4 major, 4 minor, 3 observations. Applied:

## B1 (blocker) — DOWNGRADED after verification, mitigation added

The reviewer concluded the DDL change "does not migrate existing databases" and so
"100% failure on every deployed tenant". The mechanism is right — `CREATE TABLE IF
NOT EXISTS` is a no-op on an existing table, so `db_schema.py` alone adds nothing —
but the severity is wrong, for a fact the reviewer was isolated from: the deployed
`integration.parser_output_assets` is owned by the cybi-db-models migrations, and
that model ALREADY declares all six columns. That is precisely why EA can read
them. Production and staging are unaffected.

The real exposure is a pre-existing LOCAL/dev/test database created from this
repo's own DDL before this change. Mitigated by documenting the additive
`ALTER TABLE ... ADD COLUMN IF NOT EXISTS` in `db_schema.py`, following the
convention already established there for out-of-band ALTERs, and by the new
column-set test below which turns this class of drift into a red test.

## M4 (major) — join-key asymmetry — FIXED

Sharp catch. `super()` runs `_lower_string_values` over its output, which rewrites
`id` itself, so the left join key was `lower(regexp_replace(id))` while the carried
right key was raw. It worked only because `uuid()` happens to emit lowercase hex
with no control characters — an invariant nothing stated or enforced. The carried
key is now normalized identically via `_match_base_string`.

## M5 (major) — blank values defeated EA's non-null gate — FIXED

`''` was passing through, so an asset with all-blank cloud fields would still
create a satellite row of empty strings. Now folded to NULL by `_cloud_value`.
**Confirmed on real data:** `cloud_account_name` non-null went 4752 → 4745, i.e.
seven assets really did carry `''`.

## m6 — control-char regex duplicated by value — FIXED

Now references `BaseParser._UNSTORABLE_TEXT_RE` instead of re-spelling the pattern.

## m7 — test gaps — FIXED (4 new tests, 6 → 10)

- `test_wiz_asset_columns_are_writable_to_parser_output_assets` — asserts the
  emitted column set is a subset of the DDL's. This is the reviewer's highest-value
  suggestion: it converts a B1-class persistence failure into a red test. Wiz never
  reaches the generic `test_run_parsers` column check because its spec sets
  `run: false`.
- `test_wiz_cloud_columns_survive_a_sparse_batch` — pins the `_ensure_columns`
  backfill and the absent-column branch of `_cloud_column`.
- `test_wiz_blank_cloud_values_become_null` — pins M5.
- `test_wiz_cloud_columns_do_not_fan_out_assets` — 3 assets, asserts cardinality
  and id uniqueness; the previous single-asset fixture could not detect fan-out
  from the re-attach join.
- Removed the vacuous `sub_type != type` assertion (both operands were already
  pinned to distinct literals); replaced with an assertion on the actual values.

## m8 / m9 / O10 — FIXED

Stale `_carry_cloud_columns` doc reference corrected. The inert
`_CLOUD_SOURCE_COLUMNS` / `_CLOUD_NULL_COLUMNS` split collapsed into one tuple,
reordered to match the DDL (O10). O11's point folded in: the comment now states
*why* `region` was declined (data exists but only per-finding, and would be null
for the ~98% of assets with no findings) separately from `cloud_provider_url`
(never collected).

## M2 — non-deterministic uuid join key — NOT FIXED, pre-existing class

Correct and worth recording: the re-attach join keys on `uuid()`, held only by
`cache()`, so a lost block could desynchronize the two branches and silently NULL
all six columns. This is the same hazard `_enforce_asset_output_contract` already
carries on the same id, and the predecessor task recorded it too. The reviewer's
own preferred fix is a deterministic spine id — a repo-wide change, explicitly an
operator decision. Not taken unilaterally.

## M3 — the base_parser hook — NOT APPLIED, ESCALATED TO OPERATOR

The reviewer argues the join is avoidable: extract `create_asset_source`'s select
list into an overridable hook whose default returns today's exact list. All 24
parsers stay byte-identical, no snapshot changes, and Wiz appends six projections
that then flow through `_lower_string_values` and `cast_columns_to_schema`
naturally — eliminating the join, M2, M4 and m6 outright.

**This is materially better, and it undercuts the premise of decision D-C.** D-C
was chosen because "adding the columns to base changes all 24 parsers and
invalidates 17 snapshots" — true for adding *columns*, false for adding a *hook*.
The operator decided on that framing, so this is escalated rather than actioned.

## Verification after repairs — all re-run

```
pytest tests/test_wiz_assets_findings_split.py    10 passed in 26.92s
pytest tests/test_run_parsers.py -k qualys         2 passed, 48 deselected
git status                                         3 files, no snapshots
```

Real data unchanged except the intended M5 effect: 4809 assets / 107653 exposures,
0 duplicate ids, 0 null/dangling asset_id, 4724 zero-vuln assets,
`type=['cloud_resource']`, sub_type 4809/4809, cloud_platform 4789/4809,
cloud_account_id 4752/4809, cloud_account_name 4745/4809 (was 4752),
region 0, cloud_provider_url 0.
