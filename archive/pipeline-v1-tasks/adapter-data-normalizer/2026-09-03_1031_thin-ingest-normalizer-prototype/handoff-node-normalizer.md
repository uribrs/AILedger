# Task: Node normalizer for third-party ingest, in the Exposure Analytics repo

## What you are building

A **Node normalizer** that turns raw collector output into rows Exposure Analytics can create entities
from directly, replacing the Glue/Spark parser stage. It is intended to be merged into the EA repo
(`~/Dev/cymulate-exposure-analytics`), which is NestJS/TypeScript, so it lives alongside the schema it
targets.

The chain is: **collector → (event + manifest) → normalizer → create-entities.**

Changes from the earlier C# prototype, which are the point of this direction:

- Collectors **stop enriching assets with findings**. Each data type is written as its own file of **raw
  vendor data** — no correlated envelope, no target-column labeling.
- Collectors **stamp exposures that have no asset id** where the vendor gives none (Cortex XDR findings
  are the example).
- The collector publishes a **manifest of the fields the vendor schema contains**, and an **event** for
  EM to capture carrying that manifest.
- Vendor→target mapping therefore moves out of the collector and into the normalizer.

That last move is right: the mapping tracks **EA's schema, not the vendor API**, so a schema change
should touch one repo instead of forcing a re-release of every collector zip.

## Settle these two before writing code

**1. Is the manifest an inventory or a mapping?**
A list of the fields a vendor emits tells the normalizer that `platform_name` exists. It does not say
that `platform_name` becomes `os_type`, that `windows` plus a version saying Server becomes
`windows-server`, or that `hostname` falls back to `current_local_ip`. So exactly one of these is true:

- the manifest carries the **mapping** (field → target column + transform), and the normalizer stays
  generic; or
- the normalizer holds **per-vendor mapping config** and the manifest is only for discovery and
  validation, and the normalizer is vendor-aware by design.

Both are defensible. Wanting both is not. Option 2 is probably right here, but it means dropping
"generic, agnostic normalizer" as a stated goal.

**2. What does an asset-less exposure become?**
It cannot land as-is. `cybi.exposure.asset_id` is `uuid NOT NULL` with an FK to `cybi.asset(id)`
(`cybi-db-models/database-schemas-up-to-date/prod-eu/cybi/exposure.md:11-13,89`), and while
`integration.parser_output_exposures_enrich.asset_id` is nullable, create-entities resolves it with an
inner join and drops what does not resolve
(`cybi-db-models/migrations/20260527124613_drop_parser_output_pk/migration.sql:239-244`). Choose
deliberately: synthesize a thin asset, add a quarantine lane, or change the schema. "Silently dropped"
is the status quo and should be chosen, not inherited.

**For Cortex specifically, an asset id may be the wrong thing to want.** Its `va_cves` rows carry
`affected_hosts` as an array of endpoint **names**, not ids. EA's own asset identity is `(value, type)`
— `value` is the hostname, and the snapshot/demote path is keyed on that pair
(`apps/create-entities-tp/src/app/create-entities-third-party/repositories/asset.repository.ts:103-133`).
So the missing thing is the asset's `value`, which is what EA already identifies assets by.

## What is already established, with evidence

**EA never correlates.** It dereferences `asset_id` and drops the row when it does not resolve
(`migration.sql:239-244`). Today's `asset_id` is a **random** Spark uuid
(`cymulate-integration-parsers/libs/packages/utilities/helpers.py:53`); the link is made either by
stamping a uuid before exploding a nested findings array (`yaml_parser.py:341,460`) or by a Spark join
on hostname + type (`yaml_parser.py:488`). No vendor key reaches Postgres today.

**EA's exposure identity is content, not `(asset_id, cve_id)`.** Its dedup fingerprint is
`md5(ROW(asset_id, cve_id, type, severity, status, name, display_name, description, mitigation, first_seen, last_seen, additional_fields)::text)`
(`parsed-data.repository.ts:70-76`), `dedupMode` defaults to false (`:408`), and the content dedup is a
safety net gated on byte-identical duplicates (`:18`, `:453-460`). There is **no** unique constraint on
`(asset_id, cve_id)` anywhere — `parser_output_exposures_enrich` is unique on `(id, instance_id)`,
`cybi.exposure` on `id`, `cybi.exposure_risk_v2` on `exposure_id`.

**Target shape is the promotion boundary**, i.e. the `integration.parser_output_*_enrich` column sets,
not today's staging shape. PK `(id, instance_id)` on both; `cve_id` NOT NULL on exposures so the CVE
explode happens before the boundary; five Postgres enums to validate membership against
(`cybi.asset_type` 23 members, `cybi.asset_host_os_type` 15, `cybi.exposure_type` 12,
`cybi.exposure_source_severity` 5, `cybi.exposure_status` 3); six `timestamp(3) without time zone`
columns; `additional_fields` is `jsonb`.

**In the EA repo, several problems from the C# prototype disappear.** Types come from Prisma directly,
so the generated-and-version-pinned label contract is unnecessary; there is no cross-repo enum skew; and
the normalizer can call create-entities in-process rather than needing the push entry point that does
not exist today (create-entities is pull — it takes a `client_id`/`instance_id` pointer and reads
staging with keyset paging).

## Findings from the C# prototype that transfer

Proven against the live Falcon lab tenant — 294 assets, 122,359 findings, 0 orphan parent keys.

1. **Both records must carry the parent key, derived by one shared code path.** In this tenant only 49
   of 294 hosts state a sensor AID; the other 245 need the Discover combined `id`. Two lanes deriving it
   by different rules makes every exposure for a host unresolvable.
2. **Id derivation must be instance-scoped.** `uuidv5(namespace, instanceId + parentKey)`: within-run
   stable so a reparse reproduces byte-identical ids, deliberately **not** stable across runs, because
   EA demotes prior generations via `latest = false` keyed on `(value, type)` per flow and a
   cross-run-stable id collapses generations.
3. **Exposure identity needs content, not just (host, CVE).** 37,750 of 122,310 findings repeat a
   `(host, CVE)` pair across 27,027 distinct pairs, because Falcon's identity is (host, CVE, **product**)
   and the target has no product column. Keying on (host, CVE) alone collapses 31% of findings; 7,037 of
   those groups disagree on `status`, and no timestamp rule picks a meaningful survivor — 16,613 groups
   share one identical timestamp. Composing the record's own canonical content into the key reproduces
   EA's rule and loses nothing.
4. **Exclude `created_at` from any content hash.** It falls back to the wall clock when a record labels
   none, so including it derives new ids on every normalization and breaks reparse stability silently.
   Excluding it lands the hashed set on exactly the twelve columns EA fingerprints.
5. **The two lanes gate on different vendor clocks.** Assets on `last_seen_timestamp`, findings on
   `updated_timestamp`. A restrictive base date drops hosts from the asset lane while their findings
   still pass: measured 3,342 orphans across 8 keys at `--base-date 2026-08-30`, 0 at the default floor.
   Reconcile the gates or declare orphan tolerance as policy. The correlated envelope hid this
   structurally.

## Traps that cost real time

- `cybi.asset_host_os_type` spells two members `windows-server` and `active-directory` in the database
  and `windows_server` / `active_directory` in Prisma via `@map`
  (`prisma/schema/enum.prisma:78-96` vs `migrations/20250617115755_asset_host_os_type_add_enum/migration.sql:4-20`).
  Use the `@map` value or every Windows Server and AD host fails its cast.
- `tags` is `text` NOT NULL default `'{}'::text` — **not** an array — while `group_names`, `site_names`
  and `ip_address` are real `text[]`.
- prod-eu and stg enrich tables differ: stg has a nullable `batch_id`, prod-eu (dump dated 2026-07-10)
  does not. Record which cluster any generated artifact came from.
- `database-schemas-up-to-date/{prod-eu,stg}/` is pg_catalog ground truth and is Markdown only —
  `generate.mjs` emits no JSON and needs live `PG*` credentials to re-run.
- Enum members are **not** in those dumps. They are in `prisma/schema/enum.prisma`, corroborated by
  `CREATE TYPE` DDL in `migrations/`.

## Evidence you can read and run

- **Working C# prototype:** `~/Dev/Uri/localprojects/adapter-data-normalizer` — six projects, 54 tests,
  local Postgres on port **15532** via `docker-compose` (this machine has the standalone binary, not the
  `docker compose` plugin). Exit-bar SQL returns 0 unresolved parent keys against 294 assets and 122,310
  exposures.
- **A real uncorrelated batch on disk:** `~/Dev/Uri/localprojects/adapter-data-normalizer/out/full-001`
  — `assets_000001.json` (294 lines, 686 KB) and `findings_000001.json` (122,359 lines, 496 MB), both
  NDJSON, plus `manifest.json`. A Node normalizer has real input on day one. Note these lanes are
  *labeled* to target columns; the new direction wants raw vendor data, so treat them as a shape
  reference and a volume test, not as the input contract.
- **Full task record:** `~/Dev/Uri/localprojects/adapter-data-normalizer/ai/active/2026-09-03_1031_thin-ingest-normalizer-prototype/`
  — `prompt_contract.md`, `orchestration_plan.md`, `execution_notes.md` (the whole build with numbers),
  `research/internal-recon.md`, `review/`.
- **Raw vendor payloads from real collector runs:** `s3://cybi-data/Uri-Tests/` — `aws s3 cp` works,
  `aws s3 ls` may be denied. `falcon-with-policies/fix-with-subfolders-001/{assets_000001.json,findings_000001.json}`
  and `tenableio-two-phase/*`. These came from collectors that still correlate, so do not model the new
  contract on their envelope shape.

## Known weak points in the prototype's evidence, so you do not over-trust it

- The parent-key agreement test's divergence case is **synthetic**. No host in the tenant has the shape
  where the two derivation rules can diverge (49 state an AID, 245 carry a 56-char base64url suffix, 0 a
  hex one), so live data never exercises divergence.
- The exit-bar SQL **cannot** report non-zero: the normalizer throws before an unresolvable row reaches
  the writer. It proves the link survives the round trip; the lane measurement is what proves the two
  derivations agree.
- `instance_id` is pinned to a constant in the prototype, so EA's generation behaviour is unexercised.
- Only one vendor was ever run through it.
- The test suite is mutually destructive under concurrency — the fixture and the host both TRUNCATE the
  same two target tables — so it cannot run in CI or for two people at once as written.

## Operator's working rules

- No package versions in a `.csproj`; central package management only. Nothing named "Legacy".
- Short, scannable reporting: lead with the answer, tables over prose, action items last, and a one-line
  certainty read on research answers.
- Non-trivial work enters through the `workflow-coordinator` skill.
- Never commit or push without approval for that specific change.
- Do not run FalconCollector suite sweeps, or ISBLoad/Dummy collector suites, in the adapters repo.
