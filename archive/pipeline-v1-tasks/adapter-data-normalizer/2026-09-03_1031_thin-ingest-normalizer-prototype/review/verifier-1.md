# Verifier 1 — thin-ingest normalizer prototype

**Verdict: PASS on all eight Success Criteria, independently re-run. The core request is demonstrated.
Two claims are weaker than the notes state, one decision was dropped without the recording it
required, and one identity-hash defect will silently collapse rows when EA's schema grows.**

Re-run on this machine, 2026-09-03:

- `dotnet build adapter-data-normalizer.sln` — 0 errors, 14 NU1507 (package-source mapping, cosmetic).
- `dotnet test adapter-data-normalizer.sln` — **40 passed, 0 failed, 0 skipped**, 35 s.
- The five tripwire tests re-run by name filter — all 5 passed.
- Exit-bar SQL run directly in the container — `0` unresolved, `294` assets, `122310` exposures,
  `0` null `asset_id`, 1 distinct `instance_id`.
- Both lane files re-scanned line by line in a separate process — 294 + 122,359 records,
  **0 missing parent key**, 0 orphan keys, 0 non-object `additional_fields`.
- `dotnet run --project src/Contract/Generator -- --check` — `CONTRACT UP TO DATE`, versions unchanged.

Git note: `state.json.baseRef` is the empty-tree hash and the repo has **zero commits and zero tracked
files** (`git log` → *does not have any commits yet*; `git ls-files` → 0). **No diff is available.**
Everything below is scoped to the artifacts named in `execution_notes.md` and to what I could re-run.

---

## 1. Success Criteria coverage

| # | Criterion | Verdict | Evidence I obtained myself |
|---|---|---|---|
| 1 | `dotnet build` succeeds and `dotnet test` passes | **met** | Build 0 errors; 40/40 tests pass, re-run here |
| 2 | Unknown label-contract version is rejected, not coerced, and a test proves it | **met** | `tests/ManifestVersionTests.cs:25,40,58,69,79` — 6 rejection cases (unknown hash, `v9` epoch, `v2` epoch, upper-case hash, bare cluster, empty) plus a same-batch control that is accepted. `ManifestVersionTests.cs:30` asserts `outcome.Batch` is **null**, so nothing is parsed on the way to the rejection |
| 3 | Live collection produces both lanes plus a manifest, parent key asserted on every record | **met** | `out/full-001/` holds `assets_000001.json` (686 KB), `findings_000001.json` (496 MB), `manifest.json` declaring both files. My independent scan: 294 + 122,359 records, 0 missing/blank `parent_key`. Asserted by `tests/CollectedBatchTests.cs:48` |
| 4 | Both lanes written to local Postgres | **met** | `count(*)` in the container: 294 assets, 122,310 exposures. `tests/ExitBarTests.cs:29` |
| 5 | SQL proves zero unresolved parent keys, output recorded | **met** — see Finding 3 for its evidential reach | I ran the contract's exact query against `adapter-data-normalizer-postgres-1`: `0`. Recorded at `execution_notes.md:528-543`. Executed in-suite via `tests/PipelineFixture.cs:25-30` |
| 6 | Re-normalizing the same batch produces byte-identical ids | **met** | `tests/ReparseStabilityTests.cs:18` over the real batch — asset ids compared element-wise, exposure ids and `asset_id`s by order-sensitive SHA-256. `:41` checks the derivation against the published RFC 4122 vector (`python.org` → `886313e1-…`), so correctness does not rest on the implementation agreeing with itself |
| 7 | Host prints `data ready` counts matching the Postgres row counts | **met** | Counts come from `count(*)` — `src/Host/NormalizeAndLoadStage.cs:90-92` → `EnrichWriter.CountRowsAsync` (`src/Writer.Postgres/EnrichWriter.cs:68-69`), not from normalizer tallies. `RequireAgreement` (`:109-116`) throws on disagreement. Asserted by `tests/ExitBarTests.cs:39` |
| 8 | README states docker up, collect, normalize, prove | **met** | `README.md:38-116` — four numbered steps, exact commands, port 15532, the `docker-compose`-not-`docker compose` note, the cluster switch, and the tripwire table |

Not re-run, and I say so rather than accepting the claim:

- The **live collection itself** (236.2 s, `execution_notes.md:186`). Re-running would consume live
  tenant calls and overwrite the batch the suite pins by name (`tests/RealBatch.cs:17`). I verified
  the *product* of that run byte by byte instead.
- The **3,342-orphan measurement at `--base-date 2026-08-30`** (`execution_notes.md:260-261`). Not
  re-runnable without another live collection. Its *mechanism* is verifiable in source — see A17.
- The **allocation-churn figures** (6.4 GB → 17.9 GB, `execution_notes.md:491`).

---

## 2. Coverage of the request beyond the formal criteria

**Yes — the deliverable demonstrates the thing the operator asked to be shown.** The load-bearing
piece of evidence is not a test, it is `src/ThinFalconCollector/Vendor/FalconUrls.cs:37-42`: the
findings request is a single tenant-wide Spotlight scroll with **no `aid:[…]` clause**. There is no
host list, no per-host hydration, and no join anywhere in the collector. Both lanes carry
`parent_key` and nothing else links them. One generic normalizer then produces rows that landed in
EA-shaped tables with `0` unresolved links. That is the claim, and it holds.

The target shape is genuinely EA's, verified live in the container rather than from the DDL source:
PK `(id, instance_id)`, `cve_id text NOT NULL`, six `timestamp(3) without time zone` columns,
`additional_fields jsonb` (`jsonb_typeof` = `object` on 294/294, `0` encoded strings),
`tags text` beside `group_names text[]`, and the five real `cybi.*` enum types.

### The vendor-agnosticism claim

**Structurally sound; evidentially thin on one axis.**

- I ran the grep myself over `src/Normalizer/**` for
  `falcon|crowdstrike|spotlight|tenable|qualys|sentinel|defender|cortex|\baid\b|hostname|platform_name|local_ip|discover`
  — **no hits**. Same grep over `src/Contract/**` — no hits. Every field name in the normalizer is
  either a generated target-column constant or `manifest.ParentKeyField`.
- The manifest carries no vendor name (`out/full-001/manifest.json`, four keys, verified).
- Vendor→label mapping lives entirely in the collector (`src/ThinFalconCollector/Labels/`), which is
  the correct side of the line.

**The `ExposureRowMapper` "wart" does not undermine the claim, and `execution_notes.md:429-431`
describes it inaccurately.** The check is at `src/Normalizer/Rows/ExposureRowMapper.cs:83-88` (the
note's line numbers predate Option C) and it reads `manifest.ParentKeyField` — the label name *is*
from the manifest. What is hardcoded is the *requirement that a parent key be present at all*, which
is target-shaped (an exposure's `asset_id` must derive from something), not vendor-shaped. The note's
own framing — "the check does not iterate a spec" — only mattered under Option B's declared
discriminator, which was reverted. There is no spec left to iterate. Not a wart.

**The real gap is different.** `decisions.md:13` committed to sanity-checking a second vendor shape
against the S3 Tenable samples, *or* recording the check as untested if dropped. I grepped `src/`,
`tests/`, `README.md` and `execution_notes.md` for `tenable|second vendor|other vendor`: the only hit
is the word inside W2's own grep string. The check was neither done nor recorded. So
vendor-agnosticism rests on (a) the structural absence of vendor tokens and (b) exactly one vendor
exercised. That is a reasonable prototype position — but it is one vendor, and the decision that
anticipated this was left undischarged. See Finding 2.

### Are the "data ready" counts read from Postgres?

**Yes, genuinely.** `NormalizeAndLoadStage.ReportAsync` (`src/Host/NormalizeAndLoadStage.cs:84-107`)
builds the payload from three `count(*)` queries and *then* cross-checks them against the normalizer's
tallies, failing the run on disagreement (`:109-116`). The tallies are the thing being checked, not
the thing being reported. `tests/ExitBarTests.cs:39-47` asserts payload == `count(*)`.

One caveat worth knowing: the host truncates the target first unless `--append` is passed
(`src/Host/Program.cs:55-58`), so the agreement check is easy to satisfy. It is still a real read
from the database, and the truncate is the right call for a prototype whose payload describes one
batch — but see Finding 5 for what `--append` does.

---

## 3. Assumption Disposition

Written back into `assumptions.md`. Reminder: **no diff is available** (zero commits, zero tracked
files), so every promotion below cites a re-run, a live SQL result, a file:line, or a named passing
test — never "the work completed".

| id | status | name | citation | actor |
|----|--------|------|----------|-------|
| A1 | VALIDATED | falcon-unmanaged-assets-have-no-aid | My own scan of `out/full-001/assets_000001.json`: 49 of 294 hosts state `aid`/`device_id`; the other 245 state none and **all 245** carry a 56-char non-hex combined-id suffix, which the 32-hex gate at `src/ThinFalconCollector/Keys/FalconParentKey.cs:78` rejects — so their key is the combined `id`, exactly as the lesson says. Matches `execution_notes.md:176-186` | verifier |
| A2 | NEVER-TESTED | widened-identity-untested-downstream | No identity was widened here and no EA code was executed. The downstream match key was *read* (`parsed-data.repository.ts:70-76`) and never run | verifier |
| A3 | NEVER-TESTED | policy-edge-keys-resolve | No policy edges are collected — the collector issues exactly two URLs (`FalconUrls.cs:18,37`) and neither returns policy edges. EA's match key was never exercised | verifier |
| A4 | VALIDATED | nested-json-lands-native | My SQL against the container: `jsonb_typeof(additional_fields)='object'` on 294/294 asset rows, `'string'` on 0. `tests/CopyColumnTypeTests.cs:155` (`additional_fields_lands_as_native_jsonb`) passes; `src/Normalizer/Labels/LabeledValueReader.cs:83-92` rejects a pre-serialized JSON string for a `jsonb` column by design. **Scope:** proven for `additional_fields`; the lesson's original `value` column is plain `text` here and was not exercised | verifier |
| A5 | NEVER-TESTED | readable-sql-parses | The task did not rely on it — every statement was executed live instead (schema DDL, staging + merge, exit bar, counts), which is the correct response to a REFUTED lesson but is not a test of the lesson | verifier |
| A6 | NEVER-TESTED | fql-last-seen-returns-every-host | The assets lane uses exactly that gate (`FalconUrls.cs:49`) but at the default 2000-01-01 floor (`Configuration/CollectorOptions.cs:37`), which cannot discriminate. Whether the gate loses hosts at a real floor is untested — and A17 shows the consequence when it does | verifier |
| A7 | NEVER-TESTED | devices-v2-accepts-1000-ids | `POST /devices/entities/devices/v2` is never called. The collector's only two URLs are `discover/combined/hosts/v1` and `spotlight/combined/vulnerabilities/v1` (`FalconUrls.cs:18,37`) — not calling it is the prototype's whole point | verifier |
| A8 | NEVER-TESTED | falcon-404-is-http-404 | No 404-specific handling exists anywhere; `FalconApiClient.cs:87-93` classifies only 408/429/5xx as transient and `:107` throws on anything else. No 404 was observed in the live run | verifier |
| A9 | VALIDATED | new-postgres-raw-sql-is-correct | Every raw statement in `src/Writer.Postgres` was executed against live Postgres, not reviewed: `SchemaDdl` applied and re-applied idempotently, the staging + `INSERT … ON CONFLICT DO UPDATE` merge ran over 122,310 rows, and I independently re-ran the exit-bar query and `\d integration.parser_output_exposures_enrich`. 21 per-type round-trips reported at `execution_notes.md:58-64`; `tests/CopyColumnTypeTests.cs` covers 6 of them and passes | verifier |
| A10 | VALIDATED | label-contract-generatable | `dotnet run --project src/Contract/Generator -- --check` re-derived both contracts in memory and reported `CONTRACT UP TO DATE` with versions `v1.prod-eu.5bfb104d97a3` / `v1.stg.d81d56c94a1c` unchanged. Inputs are only the four dump `.md` tables plus `enum.prisma` (`src/Contract/Generation/ContractSources.cs:28-35`). No column or enum list is hand-typed | verifier |
| A11 | NEVER-TESTED | dumps-current-enough | Currency was never checked against a live cluster. What *was* checked is internal consistency between the two dumps: I confirmed `batch_id` appears in both stg files and in neither prod-eu file (`grep -c` = 3 / 0). Whether either dump matches today's live schema is unknown | verifier |
| A12 | VALIDATED | enum-members-recoverable | Generated from `prisma/schema/enum.prisma` with no live cluster: 23 / 15 / 12 / 5 / 3 members, printed by `--check`. `PrismaEnumParser.ParseRequired` throws rather than yielding an empty set for a missing enum (`tests/LabelContractGeneratorTests.cs:68`, passes). I verified the hyphenated `@map` values at ground truth: `cybi-db-models/prisma/schema/enum.prisma:80,90` | verifier |
| A13 | NEVER-TESTED | uuidv5-satisfies-ea-model | Conjunctive claim; one conjunct has no evidence. **Reparse half — proven:** `tests/ReparseStabilityTests.cs:18` over the real 294 + 122,310-row batch, plus the RFC 4122 vector at `:41`. **PK half — proven:** 122,604 rows landed under PK `(id, instance_id)` with no violation, confirmed in my `\d` output. **EA-generation-model half — nothing moved it:** no EA code was executed, and `src/ThinFalconCollector/PrototypeScope.cs:24` pins `instance_id` to a constant, so the `latest = false` demotion keyed on a *changing* instance is untestable here by construction | verifier |
| A14 | VALIDATED | spotlight-finding-carries-aid | My own scan of the verbatim vendor records inside `additional_fields`: **122,359 of 122,359** findings carry a non-blank `aid`; 0 carry the key blank or absent. Covers the claim as written. **Scope:** one tenant, one run, at the 2000-01-01 floor. `FalconParentKey.ForSpotlightFinding` (`:42`) has no fallback, which is the right shape for this evidence — a finding without `aid` is counted, not invented | verifier |
| A15 | NEVER-TESTED | lab-collection-is-short | One 236.2 s run is recorded (`execution_notes.md:186`) and only one batch exists on disk (`out/full-001`). I did not re-run it: it needs live credentials and would overwrite the batch the suite pins by name. The "short enough to run **repeatedly**" half has no evidence, and each run writes a 496 MB findings file | verifier |
| A16 | VALIDATED | npgsql-copy-handles-types | The three named types go through one generic switch with **no per-column branch**: `src/Writer.Postgres/Schema/PostgresTypeMap.cs:35-49` maps `text[]`→`Array\|Text`, `jsonb`→`Jsonb`, timestamp→`Timestamp`, driven off `ColumnContract`. Round-tripped live — `tests/CopyColumnTypeTests.cs:25` (arrays with commas and embedded quotes), `:88` (timestamp(3) to `.456`), `:155` (native jsonb) — all pass. **Caveat A16 does not name:** enum columns cannot be COPYed at all and are staged as `text` then cast (`PostgresTypeMap.cs:23-28`, `EnrichCopyRunner.cs:88-107`), and `timestamp(3)` needs the `Timestamp3` Kind-coercion type upstream of COPY. Both are per-*type*, not per-column, so the claim as written stands | verifier |
| A17 | REJECTED | no-orphan-findings-on-falcon | **They do occur on this vendor.** Zero holds only at the default 2000-01-01 floor — I confirmed 0 of 122,359 finding keys fall outside the asset lane there. W1 measured **3,342 orphans across 8 keys** at `--base-date 2026-08-30` (`execution_notes.md:260-261`); I did not re-run that collection, but the mechanism is plain in source: the assets lane gates on `last_seen_timestamp` and the findings lane on `updated_timestamp` against the same floor (`FalconUrls.cs:49,54`) — two different vendor clocks. The claim as written is refuted; the exit bar simply ran under the one condition where it holds | verifier |

---

## 4. Attention Item Disposition

| id | final disposition | name | evidence |
|---|---|---|---|
| R1 | handled | parent-key-disagreement-across-lanes | `ParentKeyAgreementTests.R1_both_lanes_derive_the_same_id_for_a_host_with_no_stated_aid` **passes** (re-run by name filter, 54 ms). It drives the production path, which is the plan's actual rule: `LaneBuilder.cs:43-63` calls the shipped `FalconParentKey.ForDiscoverHost` / `ForSpotlightFinding` and the shipped `AssetLabeler` / `FindingLabeler`, then `:83` runs the real `BatchNormalizer` and asserts `Exposures[0].AssetId == Assets[0].Id`. Nothing is re-implemented. **See the probe below for exactly what it does and does not guard.** |
| R2 | handled | tags-is-text-not-array | `CopyColumnTypeTests.R2_tags_round_trips_as_text_and_arrays_round_trip_as_arrays` **passes** live against the container: `pg_typeof(tags)='text'`, `pg_typeof(group_names)='text[]'`, a scalar carrying `{production,"eu-west"},not-an-array` round-trips verbatim. I confirmed the same typing on the loaded pipeline rows. `:67` also proves the `'{}'` default reads back as 2 characters of text |
| R3 | handled | timestamp-kind-rejected | `CopyColumnTypeTests.R3_vendor_utc_timestamp_round_trips_through_timestamp3` **passes**, both halves: a `Kind=Utc` `DateTime` raises `ArgumentException … Kind=UTC` at write time (`:102-105`), and the same instant through `Timestamp3` reads back as `2026-06-02T13:52:22.456`, `Kind=Unspecified` (`:128-132`). The guard also exists one layer earlier — `Timestamp3.IsColumnSafe` (`src/Contract/Rows/Timestamp3.cs:57-58`), asserted at `:140` |
| R4 | handled | enum-label-mapped-not-identifier | `LabelContractGeneratorTests.R4_hyphenated_enum_labels_come_from_the_map_value` **passes** — the real `PrismaEnumParser` yields `windows-server` / `active-directory` and *not* `windows_server` / `active_directory`. `:53` asserts the same of the artifact the pipeline actually loads. I verified ground truth: `cybi-db-models/prisma/schema/enum.prisma:80,90` carry those `@map` values |
| R5 | handled | contract-cluster-mismatch | `LabelContractGeneratorTests.R5_contract_records_source_cluster_and_rejects_a_mismatched_batch` **passes**. Stronger than a check: the cluster token is inside the version (`v1.prod-eu.…` / `v1.stg.…`), so collision is impossible by construction, and `BatchManifestValidator` rejects a stg-pinned manifest against the prod-eu contract naming the version target. I confirmed the shape delta the recording exists for: `batch_id` present in both stg dumps, absent from both prod-eu dumps |

### R1 probed specifically

**I reproduced W4's declared limitation exactly.** My own scan of the 294 collected asset records:
49 state an `aid`/`device_id`; 245 state none and **every one of those 245 carries a 56-char
base64url combined-id suffix — 0 carry a 32-hex one**. So the tenant cannot supply the divergence
shape, and the R1 input record at `tests/ParentKeyAgreementTests.cs:141-151` is hand-built.

It is worse than "synthetic input", and worth stating plainly: **live data never exercises divergence
at all.** All 46 distinct finding keys land on hosts that *stated* an AID (I checked each). On those
46 hosts both rules read the same stated `aid`, so agreement is definitional. The 245 combined-id
hosts contribute zero findings, because Spotlight is sensor-only.

I judge R1 `handled`, and here is the precise scope:

- **It guards** a regression in `FalconParentKey.ForDiscoverHost` — specifically the ordering at
  `:49-50` and the 32-hex gate at `:78`. If someone made the assets lane return the combined `id`
  where an AID is derivable, or flipped stated-vs-derived precedence, this test fails. That is a real
  and likely regression, and it is caught end to end through the real normalizer.
- **It does not guard** that the vendor's two endpoints agree. No live record can currently disagree,
  so the test cannot notice if they start to.
- **It does not cover the shape that would actually hurt**: a *sensor* host whose Discover record
  omits `aid` and whose suffix is **not** 32 hex. The assets lane keys that host by its combined `id`
  while its findings key by `aid`, and every exposure on it orphans silently. Nothing in the suite
  constructs that case, and it is the shape that materialises the moment Discover changes what it
  states — or the moment Spotlight reports on any of the 245.

---

## 5. Decision drift

| decisions.md entry | outcome |
|---|---|
| Target the EA promotion boundary, not today's staging shape | **landed** — both tables generated from the enrich dumps; verified live in the container |
| Id derivation in the normalizer, not the collector | **landed** — `src/Normalizer/Ids/RowIdDerivation.cs`; the collector emits no `id`, `asset_id` or scope column (I confirmed no such label in either lane file) |
| Derived id scope is `(instance_id, vendor parent key)` | **drift, deliberate.** The derivation is correct, but the prototype pins `instance_id` to a constant (`src/ThinFalconCollector/PrototypeScope.cs:24`), so ids in *this repo* are stable across runs — the opposite of what `constraints.md:8` says. Documented honestly at `PrototypeScope.cs:6-12`; **not** flagged where the README states the production behaviour (Finding 8) |
| Falcon parent key = stated-or-derived AID, else combined `id` | **landed** — `FalconParentKey.cs:35-50`, mirroring `ExtractRecordKey` |
| Prototype input is the thin collector's own output; S3 is shape reference only | **landed** — `out/full-001` is the sole input; no S3 envelope shape appears anywhere |
| Unit of work is the batch folder | **landed** — `DirectoryBatchSource`; the page-file question stays open as decided |
| Manifest carries no vendor name | **landed** — verified: `out/full-001/manifest.json` has exactly `label_contract_version`, `parent_key_field`, `scope`, `files` |
| Unknown label-contract version is a hard rejection | **landed** — 6 rejection cases, `outcome.Batch` null |
| CVE explode in the normalizer | **landed** — `ExposureRowMapper.Explode`, read under the contract's own `cve_id` column name (`:154`), so the explode needs no vendor field name |
| Proceeding on unverified: enum members obtainable without a live cluster | **held.** No fallback needed; the hand-seeding contingency never fired |
| Proceeding on unverified: S3 Tenable samples suffice to sanity-check a second vendor shape | **ABANDONED, and the required recording was never made.** The decision said that if wrong the check is "dropped from the prototype and **recorded as untested**". It was dropped; `execution_notes.md` never records it. This is the one undischarged decision — Finding 2 |
| Failure policy deliberately not decided; the prototype fails the whole batch | **landed with a wrinkle.** The run's *verdict* fails whole, but the target's *state* does not: the asset lane commits in its own transaction before the exposure lane streams — Finding 6 |
| **Exposure identity (not in `decisions.md` at all)** | **material drift, recorded in the notes only.** The contract's decision list predates Option C. Three reversals, all at `execution_notes.md:349-431`: (1) a recommended freshest-wins collapse, **refuted by measurement** — open-member groups reported not-open would have gone 2,043 → 6,167 and 16,613 of 27,027 groups share one identical timestamp, so the rule cannot discriminate; (2) a shared false premise that the collector pages per host, **withdrawn** — the findings lane is one tenant-wide scroll with no `aid:[…]` clause, which I confirmed at `FalconUrls.cs:29-31,37-42`; (3) an authorized `BatchManifest` extension for Option B, **built and then reverted** because Option C needs no Contract change. I verified the revert: all three generated artifacts still hash to their phase-0 values per `--check`, and `grep` for `discriminator\|RequiredLabels\|MissingDeclaredLabel` under `src/Contract/` returns nothing. The EA evidence Option C rests on is real — I read `parsed-data.repository.ts:70-76` and it is exactly a 12-column `md5(ROW(...))` with `asset_id` and `cve_id` two columns among twelve, and `dedupMode = false` is the default at `:408`. **None of this reached `decisions.md`.** |

---

## 6. Findings, ranked

### 1. Contract drift is fail-loud in the writer and fail-silent in the normalizer — and silent omission from the identity hash collapses rows

`src/Normalizer/Rows/LabelBinding.cs:33` — `if (!setters.TryGetValue(column.Name, out var set)) continue;`
`src/Normalizer/Rows/ExposureContent.cs:75` — `if (!Readers.TryGetValue(column.Name, out var read)) continue;`
versus `src/Writer.Postgres/Copy/CopyPlan.cs:46-50`, which **throws** for a contract column with no cell.

The exposure identity hash iterates the contract's columns but silently skips any column that has no
entry in the hand-written `Readers` dictionary (`ExposureContent.cs:38-51`, ten entries). The doc
comment at `ExposureContent.cs:63-66` claims "adding a column to the target cannot make two
previously distinct rows collide". **That reasoning covers reinterpretation, not omission, and is
wrong for the omission case.**

Concrete failure: EA adds a content-bearing column to `parser_output_exposures_enrich` — say
`epss_score` or a product column. The contract is regenerated (new version, correctly). Someone adds
the `CopyColumn` cell so the writer starts, and forgets the `Setters` entry and the `Readers` entry.
The column then writes NULL for every row, and — the serious half — two findings that differ *only*
in that column hash identically, derive the same id, and one is dropped as
`DuplicateExposuresCollapsed`. On this tenant that class of difference is 37,750 rows (31% of
findings). It is silent: the counter increments, no validation fails, the exit bar still reads 0.

No test asserts setter or reader coverage — I grepped `tests/` for `ExposureContent|LabelBinding`:
**no hits**. `CopyPlan`'s exhaustiveness check is the model to copy.

### 2. The second-vendor check was dropped without the recording its own decision required

`decisions.md:13` — "if wrong: that check is dropped from the prototype and **recorded as untested**".
It was dropped and never recorded. `grep -rniE 'tenable|second vendor|other vendor'` over `src/`,
`tests/`, `README.md` and `execution_notes.md` returns only the word inside W2's grep string.

Why it matters: the vendor-agnosticism claim is the *generic* half of the prototype's thesis, and it
currently rests on one vendor plus the absence of vendor tokens. Absence of a token is not evidence
that a second vendor's records can be expressed as flat target-column labels — which is the
convention W2 had to invent mid-phase and which nothing outside Falcon has tested. A reader of
`execution_notes.md` would not know the check was skipped, because the notes do not say so.

### 3. The exit-bar SQL cannot report non-zero, so it proves less than the notes imply

`src/Normalizer/NormalizedBatch.cs:109-116` — `RequireResolvableAsset` throws
`BatchValidationException` the instant a derived `asset_id` is not in the asset lane's id set, and it
runs **inside** the streaming enumerator that feeds COPY (`:90`). An unresolvable row therefore never
reaches Postgres; the batch aborts and `src/Host/Program.cs:36-38` returns exit code 2, not the
`ExitNotReady` path at `:88-92`. `unresolved_parent_keys` in the payload can only ever be 0 on a run
that completes.

The query is still worth having — it proves the derived link survived label→row mapping, binary COPY,
the staging hop, the enum casts and the `ON CONFLICT` merge without an asset row being lost or a uuid
being altered. That is a real end-to-end property. But **it is not independent evidence that the two
lanes' derivations agree.** The independent evidence for that is the lane measurement (0 orphan keys
across the collected files), which I re-ran. Criterion 5 is met; the notes' framing at
`execution_notes.md:70` ("the load-bearing proof is SQL") overstates its reach.

### 4. The two lanes gate on different vendor clocks — orphans are structural, and the failure policy that would absorb them is undecided

`src/ThinFalconCollector/Vendor/FalconUrls.cs:49` (`last_seen_timestamp:>=`) versus `:54`
(`updated_timestamp:>=`), against the same floor value.

A host not *seen* since the floor leaves the assets lane while its findings still pass the findings
floor. W1 measured 3,342 orphans across 8 keys at `--base-date 2026-08-30`. Under the prototype's
policy (`decisions.md:14`, fail the whole batch), that is not 3,342 dropped rows — it is **the entire
122,359-record batch rejected**, at exit code 2, on the first orphan the stream reaches. The default
2000-01-01 floor is the only reason the exit bar ran green.

This is the design consequence the correlated envelope was structurally hiding, and it is correctly
identified as a design finding rather than a defect (`execution_notes.md:253-270`). Naming it here
because the operator will hit it the first time a real incremental floor is used, and the choice it
forces — reconcile the gates, or declare orphan tolerance — is still open.

### 5. `--append` throws for the case it exists for

`README.md:105` advertises `--append` "to keep the rows already in the target tables".
`src/Host/Program.cs:55-58` honours it by skipping the truncate. But
`src/Host/NormalizeAndLoadStage.cs:94-95` then compares total `count(*)` against *this batch's*
normalizer tally, and `RequireAgreement` (`:109-116`) throws when they differ.

So `--append` succeeds only when re-loading the same batch (the merge upserts the same ids, count
unchanged) and throws `InvalidOperationException` — surfacing as `RUN FAILED`, exit code 2 — the
moment the target holds rows from any other batch or instance. A documented flag that fails whenever
it does anything.

### 6. A rejected batch leaves the asset lane committed

`src/Host/NormalizeAndLoadStage.cs:69` commits the asset lane in its own transaction
(`EnrichCopyRunner.RunAsync` opens, COPYs, merges and commits per call) before `:72` begins streaming
exposures. An orphan or a validation failure at exposure row 100,001 rolls back the *exposure*
transaction only.

Result: 294 asset rows persist, 0 exposure rows, and the run reports failure. EA reading the tables
at that moment sees a complete asset generation with no exposures — which, given `latest = false`
demotion keyed on `(value, type)`, is not a harmless state. `decisions.md:14` says the prototype
"fails the whole batch"; that is true of the verdict, not of the target. Worth deciding before this
shape becomes real.

### 7. The test suite is bound to this machine

Three separate hard dependencies, none of them skippable:

- `tests/RealBatch.cs:23-31` throws if `out/full-001` is absent. `out/` is gitignored, the batch is
  496 MB, and reproducing it needs live Falcon lab credentials.
- `tests/PostgresFixture.cs` requires the container on port 15532.
- `src/Contract/Generation/ContractSources.cs:11-12` hardcodes
  `/Users/user/Dev/cymulate-exposure-analytics/cybi-db-models` as the models root.

`README.md:114-116` states the first two deliberately ("Nothing skips"), which is the right call for
a proof. Flagging it because with zero commits and zero tracked files, **nothing in the repo is yet
reproducible by anyone else**, and the 40-test pass is a property of this workstation. The generated
contract artifacts *are* checked in, so R4/R5 do not need the EA repo — only regeneration does.

### 8. The README states the production id model as if it were the prototype's

`README.md:154-158` — "The next collection carries a new `instance_id`, which is what lets Exposure
Analytics demote the previous generation." That is the production design, not this build:
`src/ThinFalconCollector/PrototypeScope.cs:24` pins `instance_id` to a constant, and the README's own
step-2 command re-collects under that same constant. `PrototypeScope.cs:6-12` and
`execution_notes.md:346-347` are honest about the trade; the README is not, at the one place a reader
would form the belief. It is also the reason A13's generation-model half is untestable here.

### 9. Stale comment: `Timestamp3` says seven timestamp columns; there are six

`src/Contract/Rows/Timestamp3.cs:4` — "the seven `timestamp(3) without time zone` columns".
`execution_notes.md:43-46` records W0's correction to **six** (`first_seen`, `last_seen`, `created_at`
on each table, which I confirmed in the live `\d` output) and states "both documents are corrected".
This one was not.

### 10. Lower-severity notes

- **In-band sentinels in the identity content.** `src/Normalizer/Rows/ExposureContent.cs:54,60,85-95`
  uses `U+001E` as the field separator, `U+001D` as the list separator and `U+0000` for absent, while
  `Encode` passes strings through verbatim. A label value containing those characters could shift the
  framing or alias absent-vs-present. Practically unreachable — Postgres `text` cannot hold NUL, so
  such a row would fail at COPY before it could collide — but it is a latent identity surface in the
  one function that defines identity.
- **The exposure lane is not fully streaming.** `src/Normalizer/NormalizedBatch.cs:65` holds a
  `HashSet<Guid> seen` over every exposure id produced — 122,310 Guids here, growing linearly with the
  lane. `execution_notes.md:452` reports 77 MB peak working set, so it is not a problem at this size,
  but "nothing materializes" is not literally true and the set is the part that scales with row count.
