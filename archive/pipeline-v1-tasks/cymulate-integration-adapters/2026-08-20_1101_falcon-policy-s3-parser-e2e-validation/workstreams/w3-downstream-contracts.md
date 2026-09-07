# W3 — downstream source-contract verification

## Scope and immutable refs

Read-only inspection was performed after refreshing only the requested remote refs. No checkout,
merge, or product-file edit was performed.

- `cybi-db-models origin/master`: `6ba627b08f2fb087e686886ca32fd4c77280d740`
- `cymulate-exposure-analytics origin/master`: `32c6f8ed3c6f172ae1668d5b3a99ac970037fc7f`
- parser projection compared from local branch HEAD:
  `83626813d1b90caf0b814908c05adf1703e97062`
  (`feature/crowdstrike-prevention-policy-parsing`)

The receiving repositories remained untouched. Their pre-existing working branches are not the
refs inspected: all evidence below comes directly from `origin/master:<path>`.

## Result

**Schema/source presence: PASS. End-to-end semantic compatibility: FAIL pending repair.**

Both receiving repositories contain the expected staging schema and the policy ingestion pipeline,
but the CrowdStrike parser producer and Exposure Analytics consumer disagree on two semantic
contracts:

1. **Blocker — policy edge asset key:** the parser emits Falcon `aid`; Exposure Analytics joins the
   edge to `cybi.asset.value`, while the same parser emits hostname/current-local-IP as asset
   `value`. Real Falcon edges therefore cannot normally resolve to assets.
2. **Major — rule value representation:** the parser JSON-encodes each rule value into a string
   before JSON-encoding the rules array. Exposure Analytics expects each `value` to already be a
   native JSON scalar/object for its `jsonb_typeof`, toggle, and slider projections. Its integration
   fixture uses native values and therefore does not cover the parser's actual representation.

The ordinary policy row, scoping, enum, and JSON-container contracts otherwise align.

## Database staging contract

The owning migration creates a deliberately keyless `integration.parser_output_policies` table.
Its exact columns are:

| Group | Columns |
|---|---|
| required UUIDs | `id UUID NOT NULL`, `instance_id UUID NOT NULL` |
| nullable scope | `client_id TEXT`, `client_integration_id UUID`, `batch_id UUID`, `integration_setting_id UUID`, `integration_setting_flow_id UUID`, `connector_flow_name TEXT` |
| timestamps | `created_at`, `first_seen`, `last_seen` as nullable `TIMESTAMPTZ(6)` |
| policy fields | `external_id`, `name`, `source_tool`, `type`, `platform`, `status`, `criticality`, `description` as nullable text; `enabled`, `is_default` as nullable boolean; `precedence` as nullable integer |
| JSON payloads | `settings JSONB DEFAULT '{}'`, `rules JSONB DEFAULT '[]'`, `policy_edges JSONB DEFAULT '[]'`, `additional_fields JSONB DEFAULT '{}'` (all nullable) |

Evidence: `cybi-db-models` `origin/master:migrations/20260802120000_add_parser_output_policies_tables/migration.sql:34-62`.

Indexes are exactly:

- `(instance_id, client_id, id)` for keyset ingestion;
- `(instance_id, batch_id)` for batch-scoped reads/cleanup.

Evidence: the same migration at `:64-70`; the keyless/nullable rationale is at `:14-32`.
The Prisma reference model matches all 26 columns, the two indexes, and explicitly uses `@@ignore`
because the table has no key:
`cybi-db-models origin/master:prisma/schema/integration.parser-output-policies.prisma:8-44`.

The parser's DDL is textually the same shape and indexes:
`cymulate-integration-parsers HEAD:libs/packages/parsers/schemas/db_schema.py:69-104`.
The job selects the same 26 columns in the same order
(`jobs/cybi-parser/script.py:56-83`), centrally supplies IDs/scope/timestamps, defaults JSON text,
casts the scalar types, and drops absent nullable UUID columns before JDBC
(`jobs/cybi-parser/script.py:218-308`). It writes and then counts the policy scope through the
production SparkDAL path (`jobs/cybi-parser/script.py:584-611`).

## Database normalized models

The next migration creates the three receiving tables used by this flow:

- `cybi.security_policy`: natural-keyed by
  `(client_id, source_tool, external_id, integration_setting_flow_id)`, with indexes for client,
  latest, source/type, status, and snapshot scope
  (`cybi-db-models origin/master:migrations/20260802120100_create_security_policy_tables/migration.sql:27-72`).
- `cybi.security_policy_rule`: composite PK `(policy_id, rule_id)` plus tenant/rule,
  tenant/category, and mixed-opclass JSONB GIN indexes (`:84-112`).
- `cybi.asset_policy`: composite PK `(asset_id, policy_id)` plus tenant/asset, tenant/policy, and
  tenant/asset/type/effectiveness indexes (`:120-143`).
- Foreign keys cascade policy deletion to rules/edges and restrict unexpected asset deletion
  (`:191-216`).

The accepted enums include the parser's exact discriminators: source tool `crowdstrike`, policy type
`prevention`, status `untested`, and rule types `toggle`, `slider`, `ml_slider_pair`:
`cybi-db-models origin/master:migrations/20260802115000_create_policy_enums/migration.sql:20-74`.

## Exposure Analytics consumer mapping

The policy phase is wired into `create-entities-tp`; it reads the staging table and writes
`cybi.security_policy`, `cybi.security_policy_rule`, and `cybi.asset_policy`
(`cymulate-exposure-analytics origin/master:apps/create-entities-tp/src/app/create-entities-third-party/repositories/policy.repository.ts:83-99`).

The consumer:

- scopes and keyset-pages by `client_id`, `instance_id`, and `id` (`:115-192`);
- accepts only non-empty `external_id` and enum-recognized `source_tool`/`type`, coalescing the three
  JSON containers (`:347-380`);
- upserts policy fields on the database natural key, defaults invalid/absent status to `untested`,
  criticality to `medium`, enabled to true, and is-default to false (`:490-608`);
- expands `rules` with `jsonb_to_recordset`, upserts `(policy_id, rule_id)`, and deletes vanished rules
  (`:611-705`);
- expands `policy_edges`, resolves them to current connector assets, upserts edges, and counts
  unmatched/collapsed edges (`:723-888`);
- snapshots vanished policies and removes edges for non-latest policies (`:891-1059`).

The service actually invokes that repository in bounded pages, accumulates policy/rule/edge counters,
derives policy exposures, and then performs final sweeps:
`cymulate-exposure-analytics origin/master:apps/create-entities-tp/src/app/create-entities-third-party/services/create-entities-third-party.service.ts:2122-2215`.

Integration coverage seeds the real staging table and exercises native JSON `rules`/`edges`
(`controllers/create-entities-third-party.controller.integration.spec.ts:4893-4924`). It covers
policy/rule/edge ingestion and cross-flow edge resolution (`:5063-5118`), idempotency (`:5167-5181`),
and vanished-policy demotion/edge cleanup (`:5183-5222`).

## Producer/consumer comparison

### Aligned fields

- Parser emits the exact 15 vendor-owned policy columns in the staging projection
  (`crowdstrike_policy_projection.py:87-108`), and the job supplies the remaining scope columns.
- Parser emits `source_tool='crowdstrike'`, `type='prevention'`, `status='untested'`
  (`crowdstrike_policy_projection.py:57-65,321-345`); all are accepted by the DB enums and EA gates.
- Parser nulls `criticality`, `is_default`, and `precedence`; EA deliberately defaults criticality
  and is-default and permits nullable precedence.
- Edge field names and scalar types otherwise match EA's recordset declaration:
  `asset_match_key`, `policy_type`, `policy_slot`, `applied`, `settings_hash`, `is_effective`,
  `via_host_group` (`crowdstrike_policy_projection.py:349-373` versus EA `policy.repository.ts:755-775`).
- Rule field names match: `category`, `rule_id`, `rule_name`, `rule_type`, `value`
  (`crowdstrike_policy_projection.py:74-85,199-214` versus EA `policy.repository.ts:635-655`).

### Blocker: edge identity mismatch

The parser explicitly defines the edge match key as Falcon `aid` and copies that field into every
edge (`crowdstrike_policy_projection.py:67-70,261-270,349-373`). The CrowdStrike asset parser,
however, derives asset `value` from hostname/current-local-IP rather than `aid`
(`yaml_engine/specs/crowdstrike-assets.yaml:21-41`); its asset ID is a generated UUID
(`yaml_engine/yaml_parser.py:409-427`). Existing parser tests also demonstrate that asset lookup is by
hostname-like `value` (`tests/test_crowdstrike_assets_findings.py:694-705`).

EA resolves only `cybi.asset.value = lower(edge.asset_match_key)` within the connector
(`policy.repository.ts:732-747,784-801`). Its integration fixture makes the seeded asset value equal
to the edge key (`controller.integration.spec.ts:4927-4947,4962-5016`), so it does not exercise the
actual CrowdStrike `aid` versus hostname/IP pairing.

Expected real-run symptom: policy rows and rules can ingest, while every policy edge increments
`edgesUnmatched` and no `cybi.asset_policy` relationship is created. This is source-proven; the W1/W2
runtime evidence should confirm aggregate match counts on the actual artifact.

### Major: nested rule values are stringified

The parser constructs `rule.value` with `F.to_json(value)` and then constructs the outer `rules`
JSON with another `F.to_json(rules)` (`crowdstrike_policy_projection.py:177-222,321-345`). The parser
test confirms the resulting inner field needs a second `json.loads(r['value'])`
(`tests/test_crowdstrike_policy_projection.py:221-233`). Therefore a rule lands as conceptually:

```json
{"value":"{...}"}
```

rather than:

```json
{"value":{}}
```

EA declares `value jsonb` in `jsonb_to_recordset` and derives fallback type/projections directly from
that JSONB value (`policy.repository.ts:635-669`). Its fixture instead seeds native string/object
values (`controller.integration.spec.ts:4979-4988`), so the actual parser representation is not
covered. Explicit parser `rule_type` prevents type rejection, but `value` is stored as a JSON string;
toggle `enabled_bool` and slider denormalization will not reflect the nested `{enabled,...}` /
`{detection,...}` object as the parser module documentation intends
(`crowdstrike_policy_projection.py:27-36`).

## Source readiness versus deployment readiness

### Source readiness

- **DB migrations/models: ready.** The exact staging and normalized schemas are on refreshed
  `cybi-db-models origin/master`.
- **EA ingestion source: present but not compatible with the current CrowdStrike edge/value
  semantics.** The generic consumer is implemented and tested, but its test fixture represents a
  different producer contract for asset keys and nested rule values.
- **Overall source contract: not staging-ready until the two semantic mismatches are resolved or
  disproved by runtime evidence.** The edge key mismatch is the blocking one.

### Deployment readiness

- The DB repository contains generated **live schema snapshots** showing the exact 26-column table
  and indexes on both STG and prod-eu. Snapshot timestamps are 2026-08-05 10:47:40Z for STG and
  2026-08-05 13:10:14Z for prod-eu
  (`database-schemas-up-to-date/stg/_overview.md:1-5,125-135` and
  `database-schemas-up-to-date/prod-eu/_overview.md:1-5,122-132`). This is affirmative historical
  deployment evidence for the DB schema, though not a live query performed in this task.
- Repository membership on `origin/master` does **not** prove that Exposure Analytics commit
  `32c6f8ed3c6f172ae1668d5b3a99ac970037fc7f` is deployed in STG or production. No deployment API,
  runtime version endpoint, or cluster workload was queried here. The user's colleague confirmation
  is useful external context but is not independent task evidence.

## Verdict for final validation

The databases can physically receive the parser rows, and EA can physically read their schema.
That is not sufficient for semantic end-to-end readiness: with the current sources, policies should
upsert, but real Falcon asset-policy edges are expected not to resolve, and rule JSON is expected to
land in a representation different from EA's tested native-value contract. Treat W3 as **FAIL** until
the runtime workstream either disproves these source-derived mismatches or the producer/consumer
contract is repaired.
