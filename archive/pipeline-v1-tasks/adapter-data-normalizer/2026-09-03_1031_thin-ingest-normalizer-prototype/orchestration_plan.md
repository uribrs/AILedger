# Orchestration Plan

## Problem Classification

- Contract sufficiency: sufficient
- Classes (maximum 3):
  - `entity-ids` — the prototype's whole claim rests on deriving the row identity from a vendor parent key instead of minting a random one.
  - `persisted-state` — typed enums, `timestamp(3)`, `jsonb` and `text[]` written to Postgres by new code with no in-house COPY precedent.
  - `vendor-field-presence` — the collector's guarantee that the parent key exists on both records is a claim about vendor fields, and recall already refuted the neighbouring claim once.
- Additional classified prior art: A9 (new-postgres-raw-sql-is-correct), from lessons.md#L-a561ce63.
- Recon correction: classification confirmed. Recon sharpened two of them — `vendor-field-presence` is now supported by a real S3 record (`findings[0].aid` present and equal to the envelope `aid`), and `persisted-state` is riskier than assumed because `BeginBinaryImport` has zero occurrences anywhere under `/Users/user/Dev`.
- New or changed artifacts:
  - derived asset row id → EA create-entities asset snapshot, keyed on `(value, type)` per flow → demotes the prior generation via `latest = false`, so a cross-run-stable id would collapse generations
  - derived exposure `asset_id` → EA exposure creation → `cybi.exposure.asset_id` FK to `cybi.asset(id)`; a value the asset lane never wrote is an unresolvable parent key
  - generated label contract (columns + enum members + source cluster) → the normalizer's row validation and the writer's COPY column list → determines which cluster's table shape the batch can legally land in
  - batch manifest (entity-per-file, parent key field, six scope ids, contract version) → the normalizer's entry point → an unknown version must reject the batch rather than parse it best-effort

### Attention Items

| id | name | failure mode | causal path and impact | planned handling | source |
|---|---|---|---|---|---|
| R1 | parent-key-disagreement-across-lanes | The assets lane and the findings lane derive the id from different parent keys for the same host | Assets lane takes the stated-or-derived AID while the findings lane takes the finding's `aid` (or vice versa for a host with no stated AID). The two ids differ, every exposure for that host becomes an unresolvable parent key, and the exit-bar SQL fails — or worse, passes on the 49 hosts that do state an AID and hides the other 254 | `test:tests/ParentKeyAgreementTests.cs::R1_both_lanes_derive_the_same_id_for_a_host_with_no_stated_aid` | A1 (lessons.md#L-29828585); `AidExtractor.cs:159` |
| R2 | tags-is-text-not-array | `tags` is written as an array | `tags` is `text` NOT NULL default `'{}'::text` while its three neighbours are real `text[]`. A writer that treats them alike throws at COPY time, or silently writes the literal `{}` for every row | `test:tests/CopyColumnTypeTests.cs::R2_tags_round_trips_as_text_and_arrays_round_trip_as_arrays` | recon: `parser_output_assets_enrich.md:12-15` |
| R3 | timestamp-kind-rejected | A UTC-kind `DateTime` is written to `timestamp(3) without time zone` | Npgsql 6+ throws on a UTC-kind `DateTime` or a `DateTimeOffset` against `timestamp`. Vendor timestamps arrive as ISO-8601 with `Z`, so the natural parse produces exactly the kind that throws, across six columns | `test:tests/CopyColumnTypeTests.cs::R3_vendor_utc_timestamp_round_trips_through_timestamp3` | recon: Npgsql behaviour note; `FalconHostFilters.cs:7` |
| R4 | enum-label-mapped-not-identifier | The label contract is generated from Prisma identifiers rather than `@map` values | `asset_host_os_type` spells two members `windows-server` and `active-directory` in the database but `windows_server` / `active_directory` in Prisma. Generating from the identifier makes every cast of a Windows Server or AD host fail, and those are the common cases | `test:tests/LabelContractGeneratorTests.cs::R4_hyphenated_enum_labels_come_from_the_map_value` | recon: `enum.prisma:78-96` vs `migrations/20250617115755_asset_host_os_type_add_enum/migration.sql:4-20` |
| R5 | contract-cluster-mismatch | A contract generated from one cluster is used against the other's table shape | prod-eu's enrich tables have no `batch_id`; stg's do. A contract that does not record its source cluster lets a batch validate against one shape and COPY into the other, failing on column count at the worst possible moment | `test:tests/LabelContractGeneratorTests.cs::R5_contract_records_source_cluster_and_rejects_a_mismatched_batch` | recon: `migrations/20260630120000_add_batch_id_columns/migration.sql:9-10` |

### Research Questions

No research needed — every open question resolved locally. Enum members are recoverable from
`prisma/schema/enum.prisma` plus migration DDL; Npgsql's version is already pinned at 10.0.3 in three
operator repos; Docker is up. The one question with no in-house precedent (binary COPY against
`jsonb` / `text[]` / `timestamp(3)`) is mechanically checkable here in seconds, and R2/R3 make that
check a required artifact — a local round-trip is stronger evidence than documentation.

## Complexity Decision
- Path: decompose
- Axis scores: Complexity medium | Separability high | Coupling low | Dependency order simple | Execution risk medium | Worker clarity high
- Rationale: two hard triggers fire. Recon named five disjoint file sets available once the shared surface is frozen, and tests are separable from the implementation they assert against. Coupling is read off the disjoint-set finding, not estimated. Execution risk sits at medium for the live vendor call and the precedent-free COPY path, which is why R2/R3 are tripwires rather than acceptances.

## Research Decisions
- External research: none needed. See Research Questions above for the rationale.
- Internal recon: complete → research/internal-recon.md

## File Ownership
- Disjoint sets found: 5
- W0 (freeze-contract-surface) owns: `adapter-data-normalizer.sln`, `Directory.Packages.props`, `Directory.Build.props`, `.gitignore`, `src/Contract/**`
- W1 (thin-falcon-collector) owns: `src/ThinFalconCollector/**`
- W2 (normalizer-id-derivation) owns: `src/Normalizer/**`
- W3 (postgres-copy-writer) owns: `src/Writer.Postgres/**`, `docker-compose.yml`, `db/**`
- W4 (host-and-exit-bar) owns: `src/Host/**`, `tests/**`, `README.md`
- Shared surface frozen in phase 0: the two target row types, the batch manifest model, the label-contract model (columns + types + nullability + enum members + source cluster), the id-derivation signature `(instanceId, parentKey) -> Guid`, and the validation result type.

## Worker Plan
- W0 (freeze-contract-surface) — scope: solution scaffold plus `src/Contract`, including the label-contract generator that parses `enum.prisma` and the two dump `.md` tables  output: the frozen surface above, plus a generated contract artifact checked into `src/Contract`  phase: 0  continuity: fresh
- W1 (thin-falcon-collector) — scope: live Discover + Spotlight collection, two flat lanes, parent key on both records, batch folder plus manifest  owns: `src/ThinFalconCollector/**`  inputs: W0.output  output: a batch on disk from the lab tenant  phase: 1  continuity: fresh
- W2 (normalizer-id-derivation) — scope: labeled JSON to row types, uuidv5 derivation, CVE explode, row validation against the contract  owns: `src/Normalizer/**`  inputs: W0.output  output: row sets ready for the writer  phase: 1  continuity: fresh
- W3 (postgres-copy-writer) — scope: docker-compose, DDL for both tables and the five enums, COPY-based writer  owns: `src/Writer.Postgres/**`, `docker-compose.yml`, `db/**`  inputs: W0.output  output: rows in local Postgres  phase: 1  continuity: fresh
- W4 (host-and-exit-bar) — scope: simulated work-available then data-ready with counts, the xUnit suite including R1–R5 tripwires and the exit-bar SQL proof, README  owns: `src/Host/**`, `tests/**`, `README.md`  inputs: W0.output, W1.output, W2.output, W3.output  output: the data-ready payload and a passing suite  phase: 2  continuity: resumed — it is the worker that reacts to integration failures in its own end-to-end wiring, so its transcript is the value

## Synthesis Approach
Phase 0 lands first and is read by every later brief, so no worker re-derives the surface. Phase 1
workers run in parallel over disjoint sets and each reports its own build result. The main thread then
builds the solution as a whole and hands W4 the integration, re-engaging it on any failure that crosses
a project boundary. A worker needing a change outside its set returns `BLOCKED:` rather than reaching
across.

## Verification Obligations
- Cross-check against the eight Success Criteria in `prompt_contract.md`.
- The load-bearing proof is SQL: zero exposures whose `(asset_id, instance_id)` matches no asset row.
- Reparse stability: normalizing the same batch twice yields byte-identical ids.
- R1–R5 each resolve to a named test that exists and passes.
- A1 and A14 must be disposed against the live collection, not against the S3 sample.
