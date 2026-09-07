# Lessons — 2026-09-03_1726_mapping-stage-target-rows (2026-09-06)

## L-d48fc80f — A18
belief:  A working prototype's vendor-to-target field mapping reflects how the current collector emits that vendor's data.
counter: The prototype read apps[0].product_name_version, valid against the pre-correlated Falcon grammar (11d0deaa) and dead after f68e0113 (2026-07-05) introduced the correlated envelope plus StrippedFindingFields = {apps, suppression_info, host_info}. Production maps display_name to vulnerability_id instead, duplicating name deliberately. A thin collector calling Spotlight directly still sees apps on 100% of findings, so the field is real and only the production collector prunes it.
source:  FalconCorrelatedRecord.cs:21; crowdstrikeAssetsFindings.py:72-73; CrowdstrikeAssetsFindingsNotHydrated.py:51 (verifier, 2026-09-06) (verifier, 2026-09-06)
verify:  grep -n 'StrippedFindingFields' /Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Collectors/FalconCollector/Flows/Findings/Correlated/FalconCorrelatedRecord.cs
do not:  do not treat a prototype's mapping as current without checking the collector generation it was written against and the production parser for the same lane

## L-c22b391c — A3
belief:  A committed pg_catalog schema snapshot can settle whether a column exists on that cluster.
counter: It can only settle it for migrations older than the snapshot. prod-eu was generated 2026-07-10 and stg 2026-07-12, while migrations/20260729120100_add_cloud_columns_to_parser_output_assets is dated 2026-07-29 - 17-19 days later. A snapshot cannot report a migration that postdates it, so silence about the six cloud columns means pending, not absent.
source:  database-schemas-up-to-date/{prod-eu,stg}/_overview.md Generated lines vs migrations/20260729120100 (verifier, 2026-09-06) (verifier, 2026-09-06)
verify:  head -3 /Users/user/Dev/cymulate-exposure-analytics/cybi-db-models/database-schemas-up-to-date/prod-eu/_overview.md; ls -d /Users/user/Dev/cymulate-exposure-analytics/cybi-db-models/migrations/*add_cloud_columns*
do not:  do not read a snapshot's silence as absence without comparing its Generated date against the migration in question

## L-1e5f81a2 — A20
belief:  A parser writing a value that is not a member of the target Postgres enum means those rows are being dropped in production.
counter: The normalization lives one repo upstream, in the parsers' shared base class, not in Exposure Analytics. base_parser.py:281-284 maps open to opened, close/closed to resolved, re-opened to reopened; :231 lowercases type; :239-253 coerces os_type. Exposure Analytics does filter non-members silently at parsed-data.repository.ts:340-341,515, but never sees one. Committed real output carries status 'opened'.
source:  base_parser.py:231,239-253,281-284; parsed-data.repository.ts:340-341,515 (verifier, 2026-09-06) (verifier, 2026-09-06)
verify:  sed -n '277,290p' /Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/common/base_parser.py
do not:  do not conclude a multi-repo pipeline is broken from evidence gathered in one of its repos

## L-42014f05 — A19
belief:  Aligning a new normalizer to the production parser column by column captures every material divergence from it.
counter: Column-by-column alignment omits the default-value class entirely, which was the largest divergence by far. Measured on 298 real Falcon asset rows: os_type 248 null where production emits other, os_version 248 null vs other, os_build 248 null vs empty string, fqdn 286 null vs empty string - 83 to 96 percent of rows, against a divergence of 1 row that was documented at length.
source:  crowdstrikeAssets.py:66 default_value=Other, lowercased at base_parser.py:231ff; measured over out rows (verifier, 2026-09-06) (verifier, 2026-09-06)
verify:  grep -n 'default_value' /Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/deprecated/crowdstrike/crowdstrikeAssets.py | head
do not:  do not treat a source-path comparison as a mapping comparison - compare defaults and post-processing too

## L-b12bf36f — A17
belief:  Vendor array ordering does not reach a content hash composed from mapped target columns.
counter: It reaches it whenever the hash includes a column carrying raw vendor structure. additional_fields carries the whole record and is one of the ten content columns, so array order is inside the derived id for 99.3 percent of Falcon findings - cve.vendor_advisory 43,913 of 52,000 sampled, remediation.entities 18,285, cve.references 12,879. A top-level-only measurement reported 0 percent because the arrays sit one level down. Only remediation.entities is canonically sorted at the source.
source:  review/verifier-1.md; FalconCorrelatedRecord.cs sorts remediation.entities only (verifier, 2026-09-06) (verifier, 2026-09-06)
verify:  grep -rn 'SortRemediationEntities' /Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Collectors/FalconCollector/Flows/Findings/Correlated/FalconCorrelatedRecord.cs
do not:  do not measure structural properties of vendor records at the top level only when the hash walks the whole object

## L-fed731cb — A19 A17
belief:  Deriving a row id from mapped content is enough to make the id reproducible for the same input bytes.
counter: It is not enough if any content column is parsed with new Date on a timestamp carrying no UTC offset - that parses in the host timezone, so the same lane file and instance id produced three different id digests under TZ=UTC, Asia/Jerusalem and America/Los_Angeles. Falcon's created_timestamp and Cortex's trailing-UTC shape both arrive without an offset. Fixed by an explicit grammar; note new Date('2026-02-31T...') returns March 3rd in V8 rather than NaN.
source:  review/code-reviewer-1.md F1; tests/timestamp-parsing.test.ts (verifier, 2026-09-06) (verifier, 2026-09-06)
verify:  npm test 2>&1 | grep -c 'timestamp'
do not:  do not use new Date on vendor timestamp text that may carry no UTC offset when the parsed value feeds an identity hash

## L-7d67fe96 — A18
belief:  A field absent from an observed lane manifest means the vendor does not supply that field.
counter: An observed manifest cannot distinguish a field the vendor never sends from one the collector stopped sending. apps is absent from the production Falcon findings manifest and present on 100 percent of findings from a thin collector calling the same API without a prune list. Only a declared projection, or the collector's own history, separates the two - and Falcon has no projection to declare.
source:  out/manifest-001/findings_000001.manifest.json apps presence=1 vs manifests/falcon-findings.json (executor, 2026-09-06) (executor, 2026-09-06)
verify:  python3 -c "import json;m=json.load(open('/Users/user/Dev/Uri/localprojects/adapter-data-normalizer/out/manifest-001/findings_000001.manifest.json'));print([f for f in m['fields']['record'] if f['name']=='apps'])"
do not:  do not read absence in an observed manifest as a statement about the vendor rather than about that collector and that batch

## L-ae844bb4 — A1
belief:  An instance-scoped derived row id will exercise Exposure Analytics' latest=false generation model.
counter: Still unexercised. Nothing in this slice reaches Exposure Analytics and no demotion was observed. Adjacent measurement only: 11 of 298 asset rows share a (value, type) pair - 3 on one IP, 2 on a hostname - which is the pair EA demotes on, so those rows do not have distinct identities downstream.
source:  review/verifier-2.md A1 (verifier, 2026-09-06) (verifier, 2026-09-06)
verify:  none - citation is not mechanically checkable from this repo
do not:  do not claim the generation model is satisfied until a row has actually been promoted and a prior generation demoted

## L-8780d7a5 — A4
belief:  A vendor nested value reaches Postgres as native JSON rather than a double-encoded string.
counter: Unobserved here - no Postgres write is in scope - but production double-encodes 6 of 54 additional_fields members via F.to_json: cvss_vector, vpr, port, scan, vpr_v2 and cvss_temporal_vector are JSON strings inside the JSON. This normalizer keeps them native, so the two differ at that boundary.
source:  base_parser.py:545,620; findings.expected_findings.json (verifier, 2026-09-06) (verifier, 2026-09-06)
verify:  grep -n 'to_json' /Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/common/base_parser.py
do not:  do not assume the two pipelines agree on nested-value encoding without comparing a written column

## L-192cd3c9 — A5 A6 A7
belief:  A normalizer that emits target-shaped rows has thereby shown those rows survive the columns, match keys and joins waiting downstream.
counter: It has shown nothing about any of them, because no row was written. Three separate claims stayed untested for the same reason: that an 89-character parent key fits columns that have only carried 32-character AIDs, that a widened identity value lands correctly in the downstream match key, and that Falcon findings never arrive without a matching asset. Partial defusal only: the longest emitted value was 27 characters over 298 non-null values, because the parentKey last resort never won.
source:  review/verifier-2.md A5, A6, A7 (verifier, 2026-09-06) (verifier, 2026-09-06)
verify:  none - citation is not mechanically checkable from this repo
do not:  do not treat emitting a correctly shaped row as evidence about the columns, match keys or joins it has not yet reached
