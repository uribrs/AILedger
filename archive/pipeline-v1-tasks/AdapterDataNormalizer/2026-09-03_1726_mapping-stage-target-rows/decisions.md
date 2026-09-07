# Decisions

- Use `~/Dev/cymulate-exposure-analytics/cybi-db-models/prisma/schema/enum.prisma` as the enum
  source. Newer commit (2026-08-02 vs 2026-05-06), and the only copy whose member counts match the
  task specification.
- Vendor a generated copy of the enum member sets into this repo rather than reading the Prisma
  schema at runtime. `cybi-db-models` has no `package.json`, so it is not installable, and a
  runtime read would make the normalizer depend on a sibling checkout.
- Keep the generation step in the repo so the vendored copy is reproducible and its provenance
  (source repo, commit, date) is recorded in the generated file.
- Emit `@map` values, never Prisma identifiers.
- Target the prod-eu column set and record that choice in the generated artifact, since prod-eu
  lacks the stg-only nullable `batch_id`.
- Do not modify `src/reader.ts` or the semantics of `src/manifest.ts`. The mapping stage consumes
  what the reader yields.
- Proceeding on unverified: the EA-nested `cybi-db-models` copy reflects the live cluster's enum
  members. If wrong: enum casts fail at load time for whichever members drifted, and the
  `cloud_resource` disagreement between the two copies is the visible symptom.
- Proceeding on unverified: the committed pg_catalog dumps give the correct
  `parser_output_*_enrich` column sets. If wrong: rows are shaped against a stale boundary and
  fail on first real insert, which this slice does not attempt.
- Proceeding on unverified: a declarative mapping format expressive enough for Falcon and Tenable
  is expressive enough to matter. If wrong: the format needs an escape hatch to arbitrary code,
  which forfeits load-time checkability — the property the manifest exists to provide.

## Drift recorded during planning (orchestrator, post-recon)

- **FLIPPED — target column set now comes from Prisma, not the pg_catalog dumps.** The earlier
  decision leaned on the dumps. Recon L5, verified: six asset columns (`sub_type`,
  `cloud_platform`, `region`, `cloud_account_id`, `cloud_account_name`, `cloud_provider_url`) exist
  in `prisma/schema/integration.parser-output-assets-enrich.prisma` and in the promotion boundary's
  own asset fingerprint, but in neither dump. Newest migration `20260731090100` postdates the
  prod-eu dump (2026-07-10) by ~3 weeks. Prisma carries the enum types, nullability, defaults and
  `@@id([id, instance_id])`, so it is sufficient as well as current.
- **Corollary — the dumps stay, for type precision only.** Prisma renders both timestamp columns as
  `DateTime`; only the dumps say `timestamp(3) without time zone`. Cite the dump for precision and
  Prisma for the column set.
- **SUPERSEDED — "target the prod-eu column set".** Prisma is the stg superset: it includes a
  nullable `batch_id`. Because `batch_id` is nullable, emitting nothing satisfies both clusters.
  Declare it excluded with the reason "the manifest carries no batch identity", mirroring the C#
  discipline at `AssetRowMapper.cs:54`.
- **CORRECTED — table names are plural.** `parser_output_assets_enrich`, not
  `parser_output_asset_enrich`. The singular table does not exist (recon L4). `prompt_contract.md`,
  `constraints.md` and `task.md` were corrected in place.
- **NEW — the six cloud columns are declared out of scope for this slice, not silently skipped.**
  Falcon and Tenable hosts are not cloud resources, and no mapping reads them. They must be
  declared excluded-with-reason so a column-coverage check passes rather than being absent by
  accident.
- **NEW — mapping-as-data is new design, not a port.** Recon G4, verified: the C# `AssetRowMapper`
  and `ExposureRowMapper` contain no vendor knowledge at all — they bind on target column names,
  and the vendor knowledge lives one stage upstream in the collector as hand-written C#
  (`ThinFalconCollector/Labels/FindingLabeler.cs:18-31`). The prototype moved the vendor code, it
  did not eliminate it. So the mapping-spec schema has no prior art to copy and is the highest-risk
  work in this slice. What does port: id derivation, canonical JSON, content composition,
  column-coverage discipline, and the `additional_fields` carry rule.
- **NEW — do not port the endianness swap.** `RowIdDerivation.SwapEndianFields` exists only because
  .NET `Guid` stores the first three fields in host order. A Node port building a hex string
  straight from the big-endian SHA-1 digest must not swap. Recon G5 verified a 10-line
  `node:crypto` implementation against the published RFC 4122 vector.

## Operator decisions after the three-repo audit (2026-09-03)

Production's mapping is **five stages**, not one per-column mapping
(`cymulate-integration-parsers/libs/packages/parsers/common/base_parser.py`): extract → mandatory-field
injection → per-parser casts → `post_process` (row drops, enum normalization, CVE explode) →
`create_*_source` (fixed projection, JSON-encode structs, `_lower_string_values`, cast). The last
stage lowercases **every** string and `array<string>` column, JSON-encoded structs included, so
production's `additional_fields` has lowercased keys *and* values. Verified against committed real
output (`test_files/crowdstrike/assets/assets.expected_assets.json`: `value` is `dc01`).

Production's `additional_fields` is also a **curated label map**, not a whole-record carry — nine
labels for Falcon assets (`crowdstrikeAssets.py:127-139`: "OS Build", "Kernel Version",
"External IP", "Product Type Desc", …), lowercased into `"os build"`, `"kernel version"`.

**DECISION 1 — match production only where identity requires it.** Case-fold `value` and `type`;
keep correct case everywhere else. Rationale the operator accepted: ids are instance-scoped and
deliberately not stable across runs, so there is no id continuity to preserve — every collection
already mints new ids and EA demotes the prior generation via `latest = false` on `(value, type)`.
That pair is therefore the only identity-critical output, and `value` is lowercased in production.

Measured stakes: **only 11 of 298 Falcon hostnames are already lowercase.** 76 assets have no
hostname at all and fall back to IP. So without case folding, ~211 of 222 hostname-bearing assets
would present as new identities and never demote the prior generation.

**DECISION 2 — keep counted rejections; do not reproduce production's silent drops.** Production
drops a row whose `value` is null/blank/control-char (`base_parser.py:184-198`) and EA drops a
non-member enum at its filter. This slice reports column, offending value and line number and
continues the lane. The new pipeline therefore surfaces data problems the current one hides.

### Consequent mapping changes

| Lane / column | Change | Reason |
|---|---|---|
| findings `display_name` | `$.remediation.entities[0].title` → `$.vulnerability_id` | Production duplicates `name` (`crowdstrikeAssetsFindings.py:73`). `apps` was a C# prototype invention and production prunes it as a heavy field. |
| findings `severity` | drop `fallback: 'info'` | Production has no fallback. Measured: the real lane carries only HIGH/MEDIUM/CRITICAL/LOW — 0 NONE, 0 UNKNOWN — so the fallback was invented and never exercised. |
| findings `status` | **no change** | Already matches production's final output: `open`→`opened`, `closed`→`resolved`, `reopen`→`reopened`. Measured: those are exactly the three values in the real lane (206,596 / 36,843 / 2,773). |
| assets `value` | adopt production's logic and add `caseFold: 'lower'` | Production: `coalesce(when(hostname contains 'ip', current_local_ip).otherwise(hostname), current_local_ip)`, then lowercased. Only 1 of 298 assets has 'ip' in its hostname, so that branch is low-impact but is matched for exactness. |
| assets `type` | add `caseFold: 'lower'` | Already the literal `host`; the fold makes the identity guarantee explicit rather than incidental. |

### Divergences from production kept deliberately, each with its reason

- **No global lowercasing.** `name`, `display_name`, `description`, `mitigation`, `os_version`,
  `fqdn`, `os_build` and `additional_fields` keep vendor case. Production lowercases them; that is
  destructive and, for JSON keys, plainly unintended (`"os build"`, `"product type desc"`). None is
  identity-critical.
- **`additional_fields` carries the whole raw record under vendor field names**, not a curated
  lowercased label map. Consequence, stated plainly: this column is one of EA's twelve fingerprint
  columns, so exposure *content* differs from production's for every row. Not identity-critical for
  demotion, which keys on `(value, type)`.
- **`os_type` is derived, not passed through.** Ours reads `platform_name` *and* `os_version` and
  emits `windows-server`; production passes `platform_name` through a lower-and-coerce and so never
  emits `windows-server` for Falcon (that branch needs the string to already start with
  "windows-server"). Ours is more specific. `os_type` sits in EA's twenty-column asset fingerprint
  but not in its identity pair.
- **`ip_address` is richer.** Production uses `current_local_ip` only; ours unions
  `local_ip_addresses` and `external_ip` with case-insensitive dedup.
- **Rejections counted, not dropped.** Per decision 2.
- **The enum member list is generated, not hand-copied.** Production hardcodes all 15
  `asset_host_os_type` members in `base_parser.py:234-237`. Ours is generated from
  `enum.prisma` with a pinned commit — the cross-repo drift this removes is real.

## REVERSAL — the Prisma-over-dumps flip was wrong (orchestrator, after reading the submodule's own skills)

`cybi-db-models` ships two skills under `.claude/skills/` that I never read. One of them,
`db-schema-snapshots`, states the rule directly:

> `database-schemas-up-to-date/<cluster>/` … the ground-truth record of what tables/columns/indexes
> /constraints/sizes ACTUALLY exist on each live Aurora cluster … read straight from `pg_catalog` by
> `generate.mjs` — **ground truth**, unlike `prisma/schema/` which can drift, is the same for every
> cluster, and can't faithfully represent partial / expression / `INCLUDE` indexes.

So the committed snapshots are per-cluster deployment truth and Prisma is the intended shape. I had
it exactly backwards.

**The six "missing" cloud columns are not a stale dump — they are UNDEPLOYED.** Verified across every
committed cluster:

```
prod-eu   22 columns   sub_type=0  cloud_platform=0  batch_id=0  risk_score=1
stg       23 columns   sub_type=0  cloud_platform=0  batch_id=3  risk_score=1
```

`sub_type`, `cloud_platform`, `region`, `cloud_account_id`, `cloud_account_name` and
`cloud_provider_url` exist in **neither** live cluster. A migration sitting in `migrations/` is in the
repo, not on the cluster; the snapshot is what says whether it landed.

**Consequence of my flip:** `src/target/contract.generated.ts` declares 29 asset columns against
clusters that have 22 and 23. Nothing writes the six — the mappings declare them excluded with a
reason — so no row is malformed. But `assertColumnCoverage` has been validating against a superset
that exists nowhere, which is the opposite of the load-time guarantee it is supposed to provide.

**Reverted decision:** the column SET comes from the per-cluster snapshot, targeting one named
cluster. Prisma remains useful for enum members (the snapshots do not carry enum members at all) and
as a statement of intent, and it stays the corroborating source for nullability and defaults. The
generated artifact must name its cluster and must not silently union the two.

This also supersedes the earlier note that "prod-eu is superseded by the Prisma superset". It is not.
Prisma is a superset of both clusters and of neither cluster's reality.

## CORRECTION to the reversal above — the outcome is right, my evidence was not

The reversal section states the six cloud columns are "UNDEPLOYED. Verified across every committed
cluster." That claim is unsupportable from these artifacts, and verifier-2 was right to reject it.

```
prod-eu snapshot   generated 2026-07-10 21:05:17+00   22 asset / 20 exposure
stg     snapshot   generated 2026-07-12 09:36:07+00   23 asset / 21 exposure
migrations/20260729120100_add_cloud_columns_to_parser_output_assets   dated 2026-07-29
```

The migration postdates **both** snapshots by 17-19 days. So the snapshots' silence about those
columns is indistinguishable from "the snapshot was taken before the migration existed". The
submodule's own skill says precisely this — a mismatch means a migration is **pending** — and I read
"pending" as "not deployed" when it also covers "deployed after this snapshot was taken".

"Every committed cluster" was also two of four: `rfqa` and `prod-us` are named by the skill but have
no committed snapshot tree, so nothing here says anything about them.

**The retarget stands, on a narrower and defensible basis.** The contract should describe columns
known to exist rather than a superset containing six unconfirmed ones, and 22/20 is what the newest
committed snapshot of the named cluster reports. What it is NOT is proof that those six columns are
absent from prod-eu today. `prismaOnlyUndeployed` is therefore a slightly optimistic field name; read
it as "declared by Prisma and absent from this cluster's last snapshot".

To settle it properly, someone must regenerate the snapshot against the live cluster —
`generate.mjs` needs live `PG*` credentials, which this session does not have.
