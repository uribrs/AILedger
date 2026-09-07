# Lessons — 2026-09-03_1031_thin-ingest-normalizer-prototype (2026-09-03)

## L-cc8f607b — A17
belief:  Falcon findings never arrive without a matching asset in the same collection batch.
counter: Zero orphans holds only at the default 2000-01-01 floor. The assets lane gates on last_seen_timestamp and the findings lane on updated_timestamp against the same floor, so a host not seen recently leaves the asset lane while its findings still pass: 3,342 orphans across 8 keys measured at --base-date 2026-08-30.
source:  review/verifier-1.md A17; src/ThinFalconCollector/Vendor/FalconUrls.cs:49,54 (verifier, 2026-09-03) (verifier, 2026-09-03)
verify:  grep -n 'last_seen_timestamp\|updated_timestamp' /Users/user/Dev/Uri/localprojects/adapter-data-normalizer/src/ThinFalconCollector/Vendor/FalconUrls.cs
do not:  do not assume two independently-gated vendor lanes are mutually consistent without checking which vendor clock each one filters on

## L-ca4fa812 — A13
belief:  A derived row id scoped per (instance_id, vendor parent key) will exercise Exposure Analytics' generation model.
counter: The prototype pins instance_id to a constant in PrototypeScope.cs:24, so ids are stable across runs here — the opposite of the constraint — and the latest=false demotion keyed on a changing instance is untestable by construction.
source:  review/verifier-1.md section 5; src/ThinFalconCollector/PrototypeScope.cs:24 (verifier, 2026-09-03) (verifier, 2026-09-03)
verify:  grep -n 'instance_id\|InstanceId' /Users/user/Dev/Uri/localprojects/adapter-data-normalizer/src/ThinFalconCollector/PrototypeScope.cs
do not:  do not claim a generation model is satisfied while the scoping field that drives it is pinned to a constant for test convenience

## L-b98fca67 — A13 A17
belief:  Keying an exposure row on (host, CVE) is sufficient because that is what the target table can hold.
counter: 37,750 of 122,310 findings repeat a (host, CVE) pair across 27,027 groups because the vendor's identity is (host, CVE, product) and the target has no product column. Collapsing loses 31% of rows; 7,037 groups disagree on status and 16,613 share one identical timestamp so no timestamp rule discriminates. EA's own dedup is a 12-column content md5, not the pair.
source:  review/verifier-1.md section 5; cymulate-exposure-analytics/apps/create-entities-tp/src/app/create-entities-third-party/repositories/parsed-data.repository.ts:70-76 (verifier, 2026-09-03) (verifier, 2026-09-03)
verify:  grep -n -A8 'exposureContentFingerprintSql' /Users/user/Dev/cymulate-exposure-analytics/apps/create-entities-tp/src/app/create-entities-third-party/repositories/parsed-data.repository.ts
do not:  do not treat (asset_id, cve_id) as an exposure's identity — no unique constraint exists on the pair and EA identifies an exposure by row content

## L-1e7de7bb — A10
belief:  The S3 Tenable samples are enough to sanity-check that a second vendor shape fits the same normalizer with no code change.
counter: The check was dropped and the recording the decision required was never made: no tenable reference exists anywhere in src/, tests/ or execution_notes.md. Vendor-agnosticism rests on token absence plus exactly one vendor actually run.
source:  review/verifier-1.md section 5 and finding 2; decisions.md:13 (verifier, 2026-09-03) (verifier, 2026-09-03)
verify:  grep -ril tenable /Users/user/Dev/Uri/localprojects/adapter-data-normalizer/src /Users/user/Dev/Uri/localprojects/adapter-data-normalizer/tests || echo 'no tenable reference — claim still unexercised'
do not:  do not accept a vendor-agnosticism claim proven against one vendor and a grep for vendor tokens

## L-45043e2e — A3
belief:  Policy edge keys resolve against Exposure Analytics' asset match key.
counter: No policy edges are collected — the collector issues exactly two URLs and neither returns them — and no EA code was executed.
source:  review/verifier-1.md A3; src/ThinFalconCollector/Vendor/FalconUrls.cs:18,37 (verifier, 2026-09-03) (verifier, 2026-09-03)
verify:  grep -c 'policy' /Users/user/Dev/Uri/localprojects/adapter-data-normalizer/src/ThinFalconCollector/Vendor/FalconUrls.cs
do not:  do not re-assume policy edges land downstream without running EA's match key against real edge rows

## L-6f6ccb67 — A4
belief:  A vendor nested value reaches Postgres as native JSON rather than a double-encoded string in every column that carries one.
counter: Proven only for additional_fields (jsonb_typeof='object' on 294/294 asset rows). The column the original lesson concerned is plain text here and was never exercised.
source:  review/verifier-1.md A4 scope note; tests/CopyColumnTypeTests.cs:155 (verifier, 2026-09-03) (verifier, 2026-09-03)
verify:  grep -n 'additional_fields_lands_as_native_jsonb' /Users/user/Dev/Uri/localprojects/adapter-data-normalizer/tests/CopyColumnTypeTests.cs
do not:  do not generalise a jsonb round-trip proof to a text column carrying JSON

## L-aa0427b1 — A11
belief:  The committed pg_catalog schema dumps are current enough to generate a target contract against.
counter: Currency was never checked against a live cluster. Only internal consistency was confirmed: batch_id appears in both stg dump files and neither prod-eu file. The prod-eu dump is dated 2026-07-10 and migration 20260630120000 postdates it.
source:  review/verifier-1.md A11 (verifier, 2026-09-03) (verifier, 2026-09-03)
verify:  grep -rc batch_id /Users/user/Dev/cymulate-exposure-analytics/cybi-db-models/database-schemas-up-to-date/prod-eu/integration/parser_output_assets_enrich.md
do not:  do not treat a committed schema dump as live truth without checking its generation date against the migration list

## L-f88b5876 — A15
belief:  A ~300 host and ~100k finding lab collection is short enough to re-run repeatedly during development.
counter: Exactly one 236.2 s run exists and it was not repeated: it needs live credentials and would overwrite the batch the suite pins by name, and each run writes a 496 MB findings file.
source:  review/verifier-1.md A15 (verifier, 2026-09-03) (verifier, 2026-09-03)
verify:  ls -la /Users/user/Dev/Uri/localprojects/adapter-data-normalizer/out/
do not:  do not plan an iteration loop around re-collecting from a live tenant when the suite pins a single batch by name

## L-05f182bf — A5
belief:  Raw SQL that reads correctly to a reviewer parses correctly in Postgres.
counter: Not tested — the task executed every statement live instead of relying on review, which is the correct response to the refuted lesson but is not evidence about the lesson.
source:  review/verifier-1.md A5 (verifier, 2026-09-03) (verifier, 2026-09-03)
verify:  none — citation is not mechanically checkable
do not:  do not ship raw SQL on review alone; execute it

## L-88abb13f — A2
belief:  A widened identity value reaching parser output columns lands correctly in the downstream match key that consumes it.
counter: No identity was widened in this task and no EA code was executed. The downstream match key was read, never run.
source:  review/verifier-1.md A2 (verifier, 2026-09-03) (verifier, 2026-09-03)
verify:  none — citation is not mechanically checkable
do not:  do not assume a widened identity column lands downstream without running the match key that consumes it
