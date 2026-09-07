# Execution notes

Appended during execution. Empty at contract time.

## freeze-contract-surface (W0, phase 0) — complete

`dotnet build adapter-data-normalizer.sln` passes: 0 errors, 4 NU1507 warnings (multiple package
sources with no source mapping — cosmetic here). Also verified under `/p:LangVersion=12` so an SDK
8.0.4xx build compiles.

**Generated label contract.** `dotnet run --project src/Contract/Generator/...` emits, and `--check`
re-derives in memory and fails on drift (confirmed `CONTRACT UP TO DATE`, byte-identical on re-run).
Inputs read-only: the four dump `.md` tables plus `enum.prisma`.

- Versions: `v1.prod-eu.5bfb104d97a3`, `v1.stg.d81d56c94a1c` — format `v1.<cluster>.<sha256-12>` over a
  canonical rendering of tables plus enums. The cluster token is inside the version, so the two clusters
  cannot collide. **R5 is satisfied by construction**, not by a check.
- Artifacts: `src/Contract/Generated/LabelContractData.g.cs`, `label-contract.prod-eu.json`,
  `label-contract.stg.json`.
- Enum member counts: asset_type 23, asset_host_os_type 15, exposure_type 12,
  exposure_source_severity 5, exposure_status 3.
- **R4 verified in the artifact**: `asset_host_os_type` emits `windows-server` and `active-directory`;
  `windows_server` is not a member.
- Generation internals live in the library under `Generation/` as `internal`, so W4's R4/R5 tests bind
  to them directly; the console project is argument parsing and file IO only.

**Frozen surface.**
- `AdapterDataNormalizer.Contract.Labels` — `TargetCluster`, `TargetEntity`, `ColumnNullability`,
  `ColumnContract`, `TableContract`, `EnumContract`, `LabelContract`, `LabelContracts`
  (`.ProdEu`, `.Stg`, `.All`, `.For(cluster)`, `.TryGet(version, out)`), `AssetColumns`,
  `ExposureColumns`, `CybiEnumNames`.
- `AdapterDataNormalizer.Contract.Rows` — `AssetRow`, `ExposureRow` (`required` on the PK halves plus
  `Tags` and `CveId`), `JsonbValue`, `Timestamp3`, `RowValidator`, `IRowIdDerivation`
  (`Guid Derive(Guid instanceId, string parentKey)`).
- `AdapterDataNormalizer.Contract.Batches` — `BatchScope`, `BatchFile`, `BatchManifest`,
  `BatchManifestJson`, `BatchManifestValidator`.
- `AdapterDataNormalizer.Contract.Validation` — `ValidationFailure(Target, Message)`,
  `ValidationResult`, `ValidationResultBuilder`.
- Test assembly for `InternalsVisibleTo`: `AdapterDataNormalizer.Tests`, proven reachable.

**Cluster difference:** exactly one, on both tables — `batch_id uuid` null, stg only. prod-eu 22 asset /
20 exposure columns; stg 23 / 21. Everything else identical including PK.

**Correction to recon and to R3:** there are **six** `timestamp(3) without time zone` columns
(`first_seen`, `last_seen`, `created_at` on each table), not seven. Verified by direct grep of all four
dumps. R3's substance is unaffected; both documents are corrected.

**Scope decisions flagged by W0:**
- Contract types are `public sealed` while generation internals stay `internal` — four sibling projects
  consume the surface, so `internal` plus four `InternalsVisibleTo` entries would be worse. The
  internal-by-default convention is kept where it applies.
- `BatchId` is `Guid?` on both row types so one row type spans both cluster shapes.

## postgres-copy-writer (W3, phase 1) — complete

`dotnet build` succeeded, 0 errors, 0 code warnings (8 pre-existing NU1507).

**21/21 per-type round-trips pass, live against the container.** Written via `EnrichWriter`, read back
through a separate `NpgsqlDataSource`. Covered: `uuid`; `text[]` including commas and embedded quotes;
`jsonb` (`jsonb_typeof=object`, native JSON not an encoded string — this is the L-85e358c4 failure mode,
now proven absent here); `timestamp(3)` (`kind=Unspecified`); `double precision`; a hyphenated enum
label (`windows-server`, `pg_typeof=cybi.asset_host_os_type`); NULL in all 19 nullable asset columns and
all 17 nullable exposure columns; a 2 000-row streamed `IAsyncEnumerable` with nothing materialized; and
the stg `batch_id` shape in a separate database.

- **R2 proven:** scalar `tags` round-trips as `text` (`pg_typeof=text`), and the `'{}'` default reads
  back as a 2-character string, not an empty array.
- **R3 proven by demonstration:** writing a UTC-kind `DateTime` raises
  `ArgumentException: Cannot write DateTime with Kind=UTC to PostgreSQL type 'timestamp without time zone'`,
  and the same instant through `Timestamp3` writes cleanly.

**Environment.**
- Host port **15532** → container 5432, deliberately not the ISB harness's 15432.
  `Host=localhost;Port=15532;Database=cybi_local;Username=postgres;Password=postgres;` — the exemplar's
  local-only default.
- Container `adapter-data-normalizer-postgres-1`, `postgres:15-alpine`, healthy via
  `pg_isready -U postgres -d cybi_local`. Compose project `adapter-data-normalizer`, volume
  `normalizer-postgres-data`; no collision with the ISB harness.
- **This machine has the standalone `docker-compose` binary, not the `docker compose` plugin** —
  `docker compose` returns `unknown command`. Use `docker-compose up -d`.

**Cluster shape is explicit, not implicit.** `docker-compose.yml` mounts
`./db/schema.${NORMALIZER_CLUSTER:-prod-eu}.sql` as the init script and `PostgresTarget.Local` defaults
to `TargetCluster.ProdEu`; `NORMALIZER_CLUSTER=stg` builds the `batch_id` shape. Both SQL files are
rendered by `SchemaDdl.Render(contract)` — nothing hand-typed. Verified against the live container:
two tables, enum member counts 23/15/12/5/3, `tags text NOT NULL DEFAULT '{}'::text`, `group_names`
ARRAY, `first_seen` `datetime_precision=3`, `additional_fields` jsonb, `risk_score` double precision.

Per the L-a8ddf024 lesson every statement was executed against live Postgres, not reviewed: init apply,
a second idempotent apply, the stg file applied twice to a throwaway database, and `EnsureSchemaAsync`
over an already-initialised container. Both throwaway databases dropped.

**Conflict policy:** COPY into a transaction-scoped staging table, then one
`INSERT … SELECT DISTINCT ON (id, instance_id) … ON CONFLICT (id, instance_id) DO UPDATE SET <every non-key column> = EXCLUDED.<col>`.
Re-running identical rows merges without duplication; a changed field updates in place (last write
wins); the same key twice inside one batch collapses to one row and reports `RowsCollapsed`.

**Caveat carried forward:** which of two same-key rows in a single batch survives is unspecified — the
`ORDER BY` carries only the key columns, no tiebreaker. Callers must not emit duplicate primary keys,
so the test suite should assert the collector emits no duplicate parent key per lane.

The staging hop also means enum columns stage as `text` and are cast on insert.

## normalizer-id-derivation (W2, phase 1) — complete

`dotnet build` passes, zero C# warnings under `-warnaserror` apart from the repo-wide pre-existing
NU1507. 27/27 behaviour checks pass in a throwaway harness.

**Id derivation.** Frozen namespace `7f6b1f26-9a3e-5d41-8c0b-2a5f4d9e1b73`
(`src/Normalizer/Ids/RowIdDerivation.cs:29`). Derivation name is `{instanceId:D}` + U+001F + parentKey.
Verified against the published RFC 4122 vector (DNS namespace, `python.org` →
`886313e1-3b8a-5372-9b90-0c9aee199e5d`), so the implementation is checked against an external oracle
rather than only against itself. `Uuid5` is `internal` so the test assembly can assert that vector.

Run scope comes from `instance_id`, not a salt — which is what reconciles the two requirements that
look contradictory. Re-normalizing the same batch reuses the same `instance_id` and yields
byte-identical ids; the next collection carries a new `instance_id`, so EA's `latest = false` demotion
is not collapsed. A frozen namespace alone would have been cross-run stable; a random salt would have
broken reparse.

**The FK matches by construction, verified in source:**
- `AssetRowMapper.cs:87` — `draft.Id = derivation.Derive(scope.InstanceId, parentKey)`
- `ExposureRowMapper.cs:126` — `draft.AssetId = derivation.Derive(scope.InstanceId, parentKey)`
- `ExposureRowMapper.cs:112` — `id = derivation.Derive(..., RowIdDerivation.ExposureKey(parentKey, cveId))`

Same call, same inputs on both lanes, so the exposure's `asset_id` cannot diverge from the asset's `id`
in the normalizer. The exposure's own identity includes the CVE, so a host's many findings do not
collapse onto one row.

**Entry point.**
```csharp
BatchNormalizer.Create()                                  // LabelContracts.All + frozen derivation
Task<NormalizationOutcome> OpenAsync(IBatchSource, CancellationToken)
```
`NormalizationOutcome` → `IsAccepted`, `Failures` (every reason at once), `Batch`, `Require(name)`.
`NormalizedBatch` → `Manifest`, `Contract`, `IReadOnlyList<AssetRow> Assets`,
`IAsyncEnumerable<ExposureRow> ExposuresAsync(ct)`, `Counts`. `IBatchSource` with
`DirectoryBatchSource(path)` for the collector's folder.

Lane shapes differ deliberately: assets are held (bounded by host count, and the exposure lane's derived
asset ids are checked against them); exposures stream and are never collected.

**Counts.** `NormalizationCounts` — `AssetRecords`, `Assets`, `FindingRecords`, `Exposures`,
`FindingsWithoutCve`, `DuplicateExposuresCollapsed`, `ValidationFailures`. `Describe()` leads with
`OK -` or `REJECTED -`. Exposure counters are complete only after the stream is enumerated to its end.

**Vendor knowledge: none.** A grep for
`falcon|crowdstrike|spotlight|tenable|qualys|sentinel|defender|cortex|aid|hostname|platform_name|local_ip`
over `src/Normalizer/**` returns no hits in source. Every field name is either a generated
target-column constant or `manifest.ParentKeyField`.

**Two design points owned by W2, recorded here because they are not in decisions.md:**

1. **The input convention.** The manifest carries no field map, so the only reading that keeps the
   normalizer vendor-free is: a record's property names are the pinned contract's column names for its
   entity, plus the manifest's parent key field. Vendor→label mapping therefore belongs to the
   collector. This was not a BLOCKED because everything that varies *per batch* is expressible in the
   manifest. W2 sent the exact label list and rules directly to `thin-falcon-collector`, which was
   building in parallel against raw Discover shapes.
2. **Duplicate exposures collapse; duplicate assets reject.** One host can carry two findings for the
   same CVE, deriving the same exposure id twice, which the target PK cannot hold. The first is kept and
   counted in `DuplicateExposuresCollapsed`. A repeated *host* is a collector defect instead, so it
   fails the batch — which also removes W3's unspecified-survivor caveat for the asset lane.

**Orchestrator note.** The phase-0 freeze did not specify the input convention, so W2 had to establish
it and broadcast it to W1 mid-phase. That is a gap in the shared surface, not in either worker: the
convention belongs in the frozen contract next time.

## thin-falcon-collector (W1, phase 1) — complete

`dotnet build` passes on the project and on the whole solution.

**Live run against the lab tenant — R1 holds empirically.**

| | |
|---|---|
| batch path | `out/full-001` |
| assets | 294 |
| findings | 122,359 |
| hosts stating a sensor AID | 49 |
| hosts keyed by the combined `id` | 245 |
| hosts with no key at all | 0 |
| **findings whose `aid` matches no asset in the batch** | **0** (0 distinct keys) |
| findings with no `aid` | 0 |
| findings naming no CVE | 49 |
| wall clock | 236.2 s |

Counters verified independently by re-reading both files: 294 asset lines, 294 distinct keys, 0
duplicates; 122,359 finding lines, 0 orphans, 0 envelope-shaped lines. Findings landed on 46 of the 49
sensor hosts (3 sensor hosts carry none).

49 stated AIDs of 294 matches ledger lesson L-29828585 (49 of 303) — the tenant lost 9 hosts to churn
and the sensor count is unchanged. **A1 is therefore supported by fresh live evidence, not only recall.**

Consequence worth stating plainly: all 122,359 findings belong to 46 hosts, because Spotlight is
sensor-only and the 245 combined-id hosts cannot have findings. The zero-orphan result is real, and it
disproves the direction of R1 that mattered — a sensor host keyed by combined `id` on one lane and by
AID on the other.

**Bind values for the sibling workers and the test suite:**
- parent key field `parent_key`, one field on both lanes, derived only by
  `FalconParentKey.ForDiscoverHost` / `ForSpotlightFinding` (`src/ThinFalconCollector/Keys/FalconParentKey.cs`).
- label contract version `v1.prod-eu.5bfb104d97a3`, read from `LabelContracts.ProdEuVersion`, not invented.
- files `assets_000001.json`, `findings_000001.json`, `manifest.json`.
- scope ids are fixed constants in `src/ThinFalconCollector/PrototypeScope.cs`, deliberately not per-run:
  a fresh `instance_id` would re-identify every row and make reparse stability untestable.
  `client_id=prototype-lab`, `client_integration_id=1111…`, `instance_id=2222…`,
  `integration_setting_id=3333…`, `integration_setting_flow_id=4444…`,
  `connector_flow_name=CollectFindings`.
- entry point `ThinFalconCollectorRunner.RunAsync(CollectorOptions, CancellationToken)` → `BatchRunReport`.
  `InternalsVisibleTo` grants `AdapterDataNormalizer.Tests`.

**Label protocol** — each line is a labeled projection: keys are target column names, plus `parent_key`,
plus `additional_fields` carrying the verbatim vendor record as native JSON. Scope columns and
`id`/`asset_id`/`created_at` are deliberately unlabeled: the manifest declares the scope and the
normalizer derives the ids. This is the convention W2 established and broadcast mid-phase, honoured here.

- Timestamps emitted as ISO-8601 UTC with milliseconds; vendor format parsing is vendor knowledge and so
  lives here. `Timestamp3.Parse` accepts the format (verified).
- `tags` carries a Postgres array literal (`{}` when empty) because the column is `text` NOT NULL; its
  `text[]` neighbours carry real JSON arrays. R2's distinction is honoured on the producing side too.
- Asset mapping: `value`←hostname (falling back fqdn → current_local_ip → parent key),
  `os_type`←platform_name+os_version (`windows-server` when the version says Server; ubuntu/debian split
  out of linux), `os_build`←kernel_version, `fqdn`←fqdn else hostname.machine_domain,
  `group_names`←groups, `site_names`←[site_name], `tags`←discoverer_tags,
  `ip_address`←local_ip_addresses+external_ip, `type`=host. `risk_score` left null — Discover states
  `criticality` as a label, not a number.
- Exposure mapping: `cve_id`←cve.id, `name`←vulnerability_id,
  `display_name`←apps[0].product_name_version, `severity`←cve.severity (NONE/UNKNOWN→info),
  `status`: open→opened, reopen→reopened, closed→resolved, `first_seen`←created_timestamp,
  `last_seen`←updated_timestamp, `description`←cve.description,
  `mitigation`←remediation.entities[0].action, `type`=vulnerability.
- Every enum label is checked against `LabelContract.IsEnumMember` at label time, so a wrong mapping
  fails on the record that caused it rather than at COPY time.

## Cross-worker audit (W1 ↔ W2) and three flags

W1 audited the live batch against all nine of W2's input-convention rules, line by line across 294
assets and 122,359 findings: **zero violations.** Labels are target column names on both lanes;
`parent_key` on every line; `cve_id` a string on all 122,310 lines that have one (Falcon states one CVE
per finding); every enum label an exact member (`windows-server` hyphenated on 6 hosts, `windows` 31,
`ubuntu` 7, `linux` 5, `macos` 1); ISO-8601 timestamps with 0 non-conforming; `tags` scalar on all 294
lines; `additional_fields` a JSON object on every line with 0 encoded strings; and no stamped column
(`id`, `instance_id`, `asset_id`, the scope ids, `batch_id`) labeled anywhere.

So the mid-phase convention broadcast cost nothing to reconcile — the producing side already conformed.

### Flag 1 — expect 122,310 exposure rows, not 122,359

49 findings name no CVE. They are counted and reported, not dropped. `cve_id` is NOT NULL at the target,
so they cannot become exposure rows.

### Flag 2 — the two lanes gate on different vendor axes (DESIGN FINDING, not a prototype defect)

The assets lane gates on `last_seen_timestamp`; the findings lane gates on `updated_timestamp`. They are
different vendor clocks, so a host that has not been seen since the floor leaves the assets lane while
its findings still pass the findings floor — and those findings become orphans with no parent row.

Measured on the live tenant: `--base-date 2026-08-30` produced **3,342 orphans across 8 keys**. The
default floor (2000-01-01) produces **0**.

This is a consequence of removing correlation, and the correlated envelope hid it structurally: the
asset travelled with the finding, so a finding could not outlive its parent inside one batch. Once the
lanes are independent, **their time gates have to be reconciled or the batch is internally
inconsistent** — either by gating both lanes on the same axis, by having the findings lane's floor
follow the assets lane's, or by declaring orphan-tolerance as the batch failure policy.

It also scopes A17: zero orphans holds at the default floor, and does not hold under a date gate. The
exit bar runs at the default floor.

### Flag 3 — `findings_000001.json` is 496 MB (assets 686 KB)

`additional_fields` carries the verbatim finding including the `apps` array. W1 offered a one-line trim
in `FindingLabeler` and deliberately did not apply it, since the prototype's claim is about the parent
key rather than payload size. Left as-is; it is a knob, not a defect.

## Normalization of the live batch, and the exposure-identity decision (OPERATOR DECISION REQUIRED)

W2 ran the normalizer over the real batch `out/full-001`:

| | |
|---|---|
| asset records / rows | 294 / 294 (294 distinct ids) |
| finding records | 122,359 |
| **exposure rows** | **84,560** |
| findings without CVE | 49 |
| **duplicates collapsed** | **37,750** |
| validation failures | 0 |
| orphan parent keys | 0 |

Reconciles exactly: 84,560 + 49 + 37,750 = 122,359. Contract resolved to `v1.prod-eu.5bfb104d97a3`.
5.1 s, 75 MB peak working set over a 496 MB findings file — the lane streams, nothing materializes.

### The cause

The exposure id derives from `(instance_id, parent_key + cve_id)`. `cve_id` is the **only** vulnerability
identity the target table carries — there is no product or app column — so two Falcon findings for the
same host and same CVE derive the same id, and PK `(id, instance_id)` cannot hold both.

Measured over the real file: 27,027 `(host, CVE)` groups carry more than one finding, largest group 13.
**7,037 of those groups disagree on `status`** (opened vs resolved); 336 disagree on `severity`; none
disagree on `name`.

The collector sorts `updated_timestamp.asc`, so the first line in a group is the **oldest** observation.
First-wins therefore keeps the stale status: a host+CVE that was resolved and then reopened lands as
`resolved`.

### This is a change from current behaviour, verified

There is **no** unique constraint on `(asset_id, cve_id)` anywhere in the target: `parser_output_exposures_enrich`
is unique on `(id, instance_id)` only (`prod-eu/integration/parser_output_exposures_enrich.md:52`),
`cybi.exposure` on `id` only (`prod-eu/cybi/exposure.md:89`), `cybi.exposure_risk_v2` on `exposure_id`
only (`:205`).

Today the Spark parser mints a **random** uuid per finding row, so two findings for one host+CVE receive
two different ids and **both rows survive** into `cybi.exposure`. A derived id keyed on `(host, cve)`
collapses them to one. So the collapse is introduced by this design, not merely exposed by it, and on
this tenant it affects 31% of findings.

### The three options

1. **Collector collapses per `(parent_key, cve_id)` before writing the lane**, keeping the freshest by
   `updated_timestamp`. Cheapest and internally correct; the collector already pages per host so the
   group is bounded. Lane becomes 84,560 lines and the normalizer's duplicate counter reads 0. W2's
   recommendation.
2. **Declare an exposure-identity field in the manifest** so all 122,310 rows survive. This is the option
   that **preserves today's row semantics**. Requires a phase-0 change to the frozen `BatchManifest`;
   W2 correctly did not make one.
3. **Accept first-wins** — 7,037 exposures carrying a possibly stale status.

Independent of the choice: if any collapse happens, the survivor must be the **freshest**, not the
oldest. First-wins over an ascending sort is wrong on its own terms.

W2 cannot resolve it inside the normalizer: last-wins means holding a row until the file ends, which
defeats the streaming the 122k lane requires, and picking a winner by status precedence is an Exposure
Analytics semantic policy it is not entitled to invent.

**Not blocking the exit bar.** Orphan parent keys are 0 either way and reparse stability holds because
file order is stable. The choice changes the row count and those 7,037 statuses.

### Other answers from W2

- No `apps` trim needed for the normalizer: 496 MB streams in 5.1 s at 75 MB working set. Whether the
  verbatim payload is worth trimming is a COPY-cost question — roughly 340 MB of jsonb across 84,560 rows.
- W1's time-gate warning confirmed and belongs in the README.
- The fixed `PrototypeScope` instance id is what makes ids stable across runs here; that is a prototype
  convenience. In production `instance_id` is per-run, which is what keeps EA's generations separate.

## Exposure identity — resolved with evidence, Option C

The fork turned on one question W2 correctly labeled as speculation: does Exposure Analytics tolerate
multiple exposure rows for the same `(asset_id, cve_id)`? It does, and its identity rule is **content**.

From `apps/create-entities-tp/src/app/create-entities-third-party/repositories/parsed-data.repository.ts`:

- `:70-76` — the exposure dedup fingerprint is
  `md5(ROW(asset_id, cve_id, type, severity, status, name, display_name, description, mitigation, first_seen, last_seen, additional_fields)::text)`.
  `asset_id` and `cve_id` are two columns among twelve, not the key.
- `:408` — `dedupMode = false` is the default.
- `:18`, `:453-460` — the content dedup is a safety net gated on a pre-pass finding byte-identical
  duplicates (`maxGroupSize > 1`). On a clean parse the enrich keeps the cheap `DISTINCT ON (id)` and
  the heavy dedup never runs.

So two rows differing by affected product — hence by `display_name`, `mitigation`, `additional_fields` —
survive both the default path and the safety net. **B and C do not degenerate to A.**

### Options as they finally stood

| | rows | hides an open exposure | loses product facts | needs |
|---|---|---|---|---|
| first-wins (as built) | 84,560 | 2,043 | 37,750 | nothing |
| A — open-precedence in collector | 84,560 | 0 | 37,750 | collector: two status-partitioned scrolls |
| B — manifest-declared discriminator | 122,310 | 0 | 0 | unfreeze BatchManifest + collector + normalizer |
| **C — content-discriminated id** | **122,310** | **0** | **0** | **normalizer only** |

### Decision: C

Composed exposure key is parent key + CVE + a stable hash of the record's own normalized content.
122,310 distinct contents on the live batch, zero byte-identical repeats.

Rationale, in order of weight:
1. **Behaviour-preserving.** A content hash reproduces EA's own rule — a byte-identical repeat collapses,
   which EA also does; anything differing survives, which EA also does. B would have made the prototype
   more opinionated about exposure identity than EA itself is.
2. **Lossless.** 37,750 real observations on distinct packages are kept, along with their per-package
   remediation, which is the information a fixer needs.
3. **Cheapest and least invasive.** One file, no manifest change, no collector change, phase 0 stays frozen.

Stated property, not an accident: identity is tied to payload composition, because the hash covers the
record including `additional_fields`. EA's own fingerprint has the same property.

### Orchestrator course corrections, recorded plainly

- I recommended freshest-wins collapse. **Wrong.** W1 measured it: groups with an open member reported
  not-open go 2,043 → 6,167, and 16,613 of 27,027 groups share one identical timestamp so the rule cannot
  discriminate at all. Both workers reproduced this independently.
- W2 and I both asserted the collector "pages per host, so the group is bounded". **False** — the findings
  lane is one tenant-wide scroll with no `aid:[…]` clause, which is the entire design. W2 traced its own
  claim to the reference collector's recon note rather than to `src/ThinFalconCollector` and withdrew it.
- I then authorized unfreezing `BatchManifest` for Option B. **Reversed** once the EA evidence above showed
  C is both cheaper and more faithful. W0 stood down; the surface never changed.

Both workers held their changes rather than implementing a recommendation they had disproven. That is why
none of the three wrong turns reached the code.

## The manifest extension: built, then reverted

W0's stand-down crossed with its delivery, so the extension was applied before the reversal arrived. It
was correct and provably additive — contract versions unchanged (`v1.prod-eu.5bfb104d97a3`,
`v1.stg.d81d56c94a1c`, `--check` still `CONTRACT UP TO DATE`), an undeclared manifest serialized
byte-identically to before via `[JsonIgnore(WhenWritingNull)]`, the existing four-argument construction
still compiled, and 25/25 additive checks passed.

Reverted anyway, because Option C supersedes it and superseded code does not survive the diff. Two
mechanisms for exposure identity in one prototype is the exact confusion this decision existed to
remove.

**Design judgments worth reusing if a declared discriminator is ever wanted for real:**
- It belongs on `BatchManifest`, not `BatchFile` — per-file would let the two lanes disagree, which is
  the R1 failure mode by another route.
- An init-only property rather than a fifth positional record parameter, because a positional parameter
  would be source-breaking for every worker already constructing the record with named arguments.
- `[JsonIgnore(WhenWritingNull)]` to override the serializer's global `Never`, so manifests already on
  disk are unaffected in both directions.
- Enforcement expressed as a `RequiredLabels(entity)` spec rather than a hardcoded label name per lane,
  so a declaration cannot be honoured in one lane and forgotten in the other. Dropped here only because
  with no discriminator it degenerates to a one-element list.

**Correction (verifier-1).** An earlier version of this note called W2's parent-key presence check at
`src/Normalizer/Rows/ExposureRowMapper.cs:76-82` a hardcoded wart against the vendor-agnosticism claim.
That description was inaccurate: the check reads `manifest.ParentKeyField`. There is no wart, and the
orchestrator's characterisation was wrong.

## Build state at this point

53 `CS0246` errors, all in `tests/` on `Fact` / `Collection`: the test csproj carries no
`<Using Include="Xunit" />` and the files do not import it per-file. That file set is W4's and W4 is
mid-flight, so it was left alone rather than fixed across an ownership boundary. `src/**` compiles.

## Option C implemented and proven on the live batch

`dotnet build` passes. Counts over `out/full-001`:

| | before (collapse) | now (Option C) |
|---|---|---|
| asset records / rows | 294 / 294 | 294 / 294 |
| finding records | 122,359 | 122,359 |
| **exposure rows** | 84,560 | **122,310** |
| findings without CVE | 49 | 49 — counted, batch not failed |
| **duplicates collapsed** | 37,750 | **0** |
| validation failures | 0 | 0 |

Reconciles exactly: 122,310 + 49 + 0 = 122,359. 7.6 s, 77 MB peak working set over the 496 MB file —
streaming intact, no group buffering.

**Reparse stability proven on the live batch, not a fixture.** Normalized `out/full-001` twice in one
process and compared SHA-256 digests over the full id sequences of both lanes: assets
`A40CA954E904C08A…` identical, exposures `DE7D2309047A1472…` identical, across 294 + 122,310 rows.
All 30 fixture checks pass, including the RFC 4122 uuid5 vector.

**What is hashed.** Derivation name is `parent_key` + U+001F + `cve_id` + U+001F + canonical
target-column content. Included: every exposure column whose value came from a label — `name`,
`display_name`, `type`, `severity`, `status`, `first_seen`, `last_seen`, `description`, `mitigation`,
`additional_fields` — read off the contract in column order with each value written next to its column
name, so a future column cannot make two previously distinct rows collide. Excluded: `id` (circular),
the manifest-stamped scope columns, `batch_id`, and `created_at`.

**`created_at` is the load-bearing exclusion.** It falls back to the clock when a record labels none, so
including it would derive a different id on every normalization and silently break reparse stability.
That exclusion also lands the included set on exactly the twelve columns EA fingerprints — `asset_id`
and `cve_id` arriving via the key, the other ten via the content — so the two identity rules converge
by derivation rather than by coincidence.

Normalization: object members in ordinal name order, no insignificant whitespace, array order preserved
(significant), absent encoded distinctly from empty. A passing check proves key order in
`additional_fields` does not change identity. Numeric literals written as they arrived.

**Stated property:** `additional_fields` is included, so identity is tied to payload composition — a
collector that changes what it carries there derives different ids for the same logical finding. That
costs nothing because ids are `instance_id`-scoped and not stable across runs by design.

**Two deliberate deviations from the brief, both sound:**
1. No intermediate digest — the canonical content is appended to the derivation name and uuid5's own
   SHA-1 hashes it. A separate hash would add a second collision surface for nothing.
2. Content read from the mapped row's columns rather than the raw JSON line — more faithful to EA's
   `md5(ROW(...))` rule, and it makes key-order and whitespace normalization structural instead of
   something a text canonicalizer must get right.

One derivation path preserved: `IRowIdDerivation.Derive` via
`RowIdDerivation.ExposureKey(parentKey, cveId, content)`.

**Known cost:** allocation churn rose from 6.4 GB to 17.9 GB (a canonical content string per record).
Peak working set did not move, so this is GC pressure rather than retention. Acceptable for the
prototype; the obvious fix if it ever matters is hashing into a pooled buffer instead of a string.

A genuinely repeated identical line still collapses, which is correct — the rows would be
indistinguishable in every target column and the PK cannot hold both. EA's safety net does the same.

### Revert verified

Nothing was committed (the repo still has zero commits). Both Contract files are back to their phase-0
shape, and the evidence is stronger than a diff: all three generated artifacts hash exactly as they did
at phase 0 — `LabelContractData.g.cs` `031cf3b9478f`, `label-contract.prod-eu.json` `3396af825a70`,
`label-contract.stg.json` `0b955a553320` — with `--check` reporting `CONTRACT UP TO DATE` and versions
unchanged.

W0 grepped every `*.cs` and `*.json` outside `src/Contract/` before removing: no sibling had bound to
any removed member, so the revert cost W1–W4 nothing. Residue check for `discriminator`,
`RequiredLabels`, `MissingDeclaredLabel` across `src/Contract/`: zero matches. 11/11 revert checks pass,
including reflection assertions that the members are gone from the public surface and that manifest JSON
carries exactly the four phase-0 keys and still hard-rejects an unknown `label_contract_version` and a
cross-cluster contract.

All six production projects compile.

**Worth carrying into the design write-up:** because the content hash composes inside
`RowIdDerivation.ExposureKey`, the lossless outcome needs **no Contract change at all** — the frozen
surface as the four workers consumed it already supports it. That is a stronger position than the
manifest field would have left, and it is the substantive reason C beat B rather than merely being
cheaper.

## host-and-exit-bar (W4, phase 2) — complete. EXIT BAR MET

`dotnet build`: 0 errors, 14 NU1507 warnings (pre-existing). `dotnet test`: **40 passed, 0 failed,
0 skipped**, 27 s.

### Exit-bar SQL, run directly against the container

```
SELECT count(*) FROM integration.parser_output_exposures_enrich e
WHERE NOT EXISTS (
    SELECT 1 FROM integration.parser_output_assets_enrich a
    WHERE a.id = e.asset_id AND a.instance_id = e.instance_id);

 count
-------
     0
(1 row)

     t     | count
-----------+--------
 assets    |    294
 exposures | 122310
```

### `data ready` payload, as printed

```json
{
  "event": "data ready",
  "batch": "/Users/user/Dev/Uri/localprojects/adapter-data-normalizer/out/full-001",
  "label_contract_version": "v1.prod-eu.5bfb104d97a3",
  "instance_id": "22222222-2222-4222-8222-222222222222",
  "connector_flow_name": "CollectFindings",
  "asset_count": 294,
  "exposure_count": 122310,
  "unresolved_parent_keys": 0
}
```

Counts come from `count(*)` against Postgres, not from the normalizer's tallies; the stage throws if the
two disagree.

### Success criteria

| # | Criterion | Verdict |
|---|---|---|
| 1 | build + test pass | met — 40 tests |
| 2 | unknown contract version rejected, not coerced | met — `tests/ManifestVersionTests.cs`, 6 cases incl. future epoch and case-variant hash |
| 3 | both lanes on disk with a manifest, parent key on every record | met — `tests/CollectedBatchTests.cs`, all 294 + 122,359 lines checked, 0 missing |
| 4 | both lanes written to Postgres | met — 294 / 122,310 rows |
| 5 | SQL proves zero unresolved parent keys | met — output above; `tests/ExitBarTests.cs` runs the query |
| 6 | reparse produces byte-identical ids | met — `tests/ReparseStabilityTests.cs`, asset ids compared one by one, exposure ids by order-sensitive SHA-256 over raw id bytes |
| 7 | host prints counts matching Postgres rows | met |
| 8 | README states how to run it | met |

### Tripwires

| Test | Result |
|---|---|
| `ParentKeyAgreementTests.cs::R1_both_lanes_derive_the_same_id_for_a_host_with_no_stated_aid` | pass (+4 more in file) |
| `CopyColumnTypeTests.cs::R2_tags_round_trips_as_text_and_arrays_round_trip_as_arrays` | pass |
| `CopyColumnTypeTests.cs::R3_vendor_utc_timestamp_round_trips_through_timestamp3` | pass — both halves: UTC-kind write raises `ArgumentException … Kind=UTC`; same instant via `Timestamp3` reads back `2026-06-02T13:52:22.456`, `Kind=Unspecified` |
| `LabelContractGeneratorTests.cs::R4_hyphenated_enum_labels_come_from_the_map_value` | pass |
| `LabelContractGeneratorTests.cs::R5_contract_records_source_cluster_and_rejects_a_mismatched_batch` | pass |

R1 drives `FalconParentKey.ForDiscoverHost` / `ForSpotlightFinding` through the collector's own labelers
and then the real `BatchNormalizer`; nothing is re-implemented. Real vendor records are lifted back out
of the batch's `additional_fields` for the stated-AID and combined-id cases.

**Limitation W4 declared, and it matters for R1's strength:** the no-stated-AID-with-32-hex-suffix case
is **synthetic**, because no host in this tenant has that shape — 49 state an AID, 245 carry a 56-char
base64url suffix, 0 carry a hex one. That shape is the only one in which the two derivation rules can
diverge, so the divergence R1 exists to catch is guarded by a constructed record rather than by live
data.

W4 also noted the mid-flight `src/Normalizer` change (Option C: `ExposureKey` gained a `content`
argument, new `Rows/ExposureContent.cs` and `Rows/CanonicalJson.cs`), correctly identifying it as
well-reasoned rather than a defect, and that it dissolves W3's unspecified-survivor caveat.

## Repair cycle 1 — W4 (host, tests, README)

`dotnet build` 0 errors; `dotnet test` **47 passed, 0 failed** (was 40).

**`--append` removed, not repaired.** W4 judged the flag its own invention, absent from its brief, and
buying nothing: under append the payload's `count(*)` describes other batches' rows too, so the mode
changes what the payload *means* rather than how it is counted, and criterion 7's check could never be
satisfied honestly. Flag and `Append` member gone from `HostOptions.cs`; `ClearTargetAsync` is
unconditional in `Program.cs`; `RequireAgreement` kept comparing `count(*)` to the batch tally, which is
now exactly right because the truncate is unconditional. The precondition is documented at the check
itself: anything that lets rows survive into the load must make the comparison batch-scoped via
`WriteOutcome.RowsMerged` or drop it, rather than leaving a total compared against a tally — the trap
that produced the bug.

**Coverage assertion — `tests/ExposureContentCoverageTests.cs`, 6 tests.** W4 asked W2 for the shape
rather than guessing; W2 had already exposed `internal static TableContract VerifyCoverage(TableContract)`
on both mappers, with exclusions as declared maps carrying a reason per column (`NotFromLabels`, and
`OutsideIdentity` adding `created_at`), so "excluded on purpose" is distinguishable from "nobody wrote an
accessor". W2 also collapsed getter and setter into one `ColumnAccessor` entry so the two halves cannot
drift.

The two tests that matter most are the pair pinning the `created_at` exclusion from both sides:
- two records differing only in `created_at` yield 1 row and 1 collapse (the exclusion, as behaviour);
- two records differing in `display_name` yield 2 distinct ids and 0 collapses (the counterpart —
  without it, an identity that ignored every column would pass the first).

Someone tidying the exclusion list would otherwise break reparse stability silently. The `[Theory]`
covers both clusters and both mappers, so stg's `batch_id` is included and not just the shape the
prototype writes.

**README rescoped.** Criterion 5 now separates two claims and names the evidence for each: that the two
lanes agree is proven by the lane measurement (0 orphan keys, 0 distinct orphan keys across 122,359
finding lines) plus `ParentKeyAgreementTests`; that the link survives COPY, the staging hop, the enum
casts and the merge is what the SQL proves. An explicit "What the SQL does not prove" paragraph states
that it cannot return non-zero because `NormalizedBatch.ExposuresAsync` throws first.

## Repair cycle 1 — W2 (normalizer identity hash)

The defect was real at both sites. Repaired structurally rather than by a check.

1. **Accessors paired.** The identity readers and the label setters were two dictionaries over the same
   properties; they are now one entry per column — `ColumnAccessor<TDraft>(Get, Set)` in
   `src/Normalizer/Rows/ColumnCoverage.cs`. Adding a column without its reader is no longer possible, so
   that drift vector is closed by construction instead of guarded.
2. **Every exclusion declared with a reason.** `NotFromLabels` is now a `column → reason` map rather
   than a bare `HashSet`; the exposure mapper adds `OutsideIdentity` = `NotFromLabels` + `created_at`. A
   missing accessor is therefore distinguishable from an intentional omission. Two previously implicit
   exclusions are now stated: `batch_id` ("the manifest carries no batch identity") and `created_at`
   ("falls back to the clock, so including it would break reparse stability").
3. **Both skip paths fail loudly.** `ColumnCoverage.Plan(...)` throws `InvalidOperationException` naming
   the column when it is neither excluded nor accessed — matching `CopyPlan`'s posture, and as an
   exception rather than a `ValidationResult` because the batch data is not at fault and no retry fixes
   it. `ColumnCoverage.Verify` runs as a field initializer, so a mapper cannot be constructed against a
   table it does not cover.
4. **Doc comment corrected** to state what is actually guaranteed, with the consequence spelled out.

**Verification:** exposure rows 122,310, collapses 0, no-CVE 49, assets 294, validation failures 0 — all
unchanged. Reparse digest `DE7D2309047A1472` is **the same as before the repair**, which proves no id
moved. 36 fixture checks pass, including four new ones: both live tables fully covered; an uncovered
column throws by name in each mapper; `mitigation` is inside the identity (two findings differing only
in it produce two rows — the positive half, without which coverage could pass while contributing
nothing); and `created_at` is outside it, proven by normalizing the same batch under two different fixed
clocks and getting identical ids.

Residual `continue` statements audited: six remain, all legitimate. No silent column skip left.

**W2 retracted a performance claim of its own.** It had reported 39 s versus 7.6 s and attributed the
regression to per-record filtering it introduced. Unsupported: load average on this machine is 21 with
the sibling workers running, and the same binary measured 12.0 s and 45.0 s on consecutive runs.
Allocation volume is the load-independent signal and is identical across variants (17.9 GB before and
after), so the filtering was not a material cost. The column plan was still hoisted to construction, on
design grounds rather than measured ones.

### Scope correction on finding 1, from W2

The routed finding — and the orchestrator's relay of it — framed the defect as "add a contract column
with no reader and two findings differing only in it derive the same id". W2 established the honest
scope, and it is narrower: `GateAsync` runs the Contract's own `RowValidator.ValidateColumnCoverage()`
before the mapper is constructed, and a column absent from `RowValidator`'s bindings is rejected there.
The real drift window is a column added to the generated contract **and** to `RowValidator`'s bindings
**and** to the row type, but not to W2's accessors.

Still real drift, and worth the structural fix, but it was overstated in the relay and is corrected here.

### W2 caught a test that would have passed for the wrong reason

W4 planned to assert the rejection end-to-end through `OpenAsync`, calling it the stronger test. It
would have passed for the wrong reason: the Contract's `RowValidator` gate rejects a synthetic column
first, so the assertion would have exercised the Contract's check while W2's repair regressed
undetected. W2 steered it to direct `VerifyCoverage` calls against a synthetic `TableContract`.

W2 also declined W4's preferred `ValidationResult` return in favour of the throw: a `ValidationResult`
routes into batch rejection, which asserts the batch's data is at fault and that a different batch would
succeed, whereas an uncovered column is an assembly defect no batch fixes. One mechanism for one
invariant.

Recommended and adopted: two behavioural assertions in preference to the declarative coverage one, since
they test the consequence rather than the declaration — two findings differing only in `mitigation` must
yield 2 rows, and the same batch under two fixed clocks must yield identical ids. `BatchNormalizer`'s
optional `TimeProvider` parameter is what makes the second writable.

One visibility change to serve the test: the three exclusion maps are `internal` rather than `private`.
Behaviour re-verified after it — 294 / 122,310 / 0 / 49, reparse digests identical, 36/36 fixture checks,
zero compiler and analyzer warnings, vendor scan clean.

**First independent cross-check of the numbers:** W4 reports the same four counts and an exit-bar SQL of
0 from its own code path, not from W2's runner.

## Post-repair clean pass — ORCHESTRATOR measurements, not a verifier pass

All four subagents died to API 529s (W2, W4, the code reviewer, and the verifier as it began its clean
pass), so `review/verifier-2.md` was never written. The repo was quiet, so the orchestrator took the
measurements directly. These are objective counts, not a disposition pass, and they are labeled as such.

Repo state confirmed quiet: no `dotnet test`/`dotnet run` against this repo (the two live `dotnet run`
processes are the operator's own LocalAdapterRunner and AdapterDeploymentTool in the adapters repo, which
touch neither this tree nor this container). Container `adapter-data-normalizer-postgres-1` up 2 hours,
healthy, on 15532.

- `dotnet build adapter-data-normalizer.sln` → **0 errors**, 14 NU1507 warnings.
- `dotnet test --no-build` → **Failed: 0, Passed: 54, Skipped: 0, Total: 54**, 28 s. Confirms the suite
  total is 54, not the 47 recorded earlier — `ExposureContentCoverageTests.cs` grew from 6 to 13 tests
  after W4's report.
- Exit-bar and shape query, run directly through `psql` in the container:

```
 unresolved_parent_keys: 0
 assets: 294
 exposures: 122310
 null_asset_id: 0
 distinct_host_cve_pairs: 84560
```

**The last row is the strongest single piece of evidence for the exposure-identity decision, and it comes
from the target table rather than from any worker's tally.** 122,310 rows span 84,560 distinct
`(asset_id, cve_id)` pairs. 84,560 is exactly what a collapse on `(host, CVE)` would have produced, so
37,750 rows — 31% — are ones that Option A would have destroyed. `null_asset_id: 0` also confirms no
exposure reached the target without a parent.

## Re-verification gap, stated plainly

`review/verifier-1.md` (326 lines) is complete and carries both disposition tables: 7 VALIDATED, 1
REJECTED (A17), 9 NEVER-TESTED, plus attention-item dispositions and decision drift.
`review/code-reviewer-1.md` (324 lines) is complete in structure — 0 blockers, 3 major, 10 minor, 6 nits,
6 observations — though the agent died while starting its collector section, so collector coverage may be
thinner than the rest.

**What is missing:** a second verifier cycle re-disposing the assumptions and attention items against the
repairs, and disposing the code reviewer's three Major findings. The repairs themselves are verified at
the code level — by the dead verifier's own account before it stopped, and independently by the
orchestrator reading `ColumnCoverage.cs`, both mappers, `HostOptions.cs` and the new test file — and the
numbers above show they moved nothing. But the formal re-disposition did not happen.

## Code reviewer's three Major findings — open, not repaired

1. **M1** — `src/Host/Program.cs:22-28` and `src/Contract/Generator/Program.cs:21-26` exit `0` on
   rejected input. A typo'd `--chek` makes the contract drift gate pass without checking anything, which
   is the gate enforcing generated-not-hand-typed. `src/ThinFalconCollector/Program.cs:10-14` does it
   correctly and returns 2.
2. **M2** — `SchemaDdl.cs` contains no `ALTER`, so `EnsureSchemaAsync` cannot repair a diverged database,
   while `README.md:69-70` and `docker-compose.yml:14-17` both claim it does. Separately `db/*.sql` are
   declared generated but nothing regenerates them and `--check` does not cover them.
3. **M3** — `FalconApiClient.GetPageAsync` retries on status codes only, so `HttpRequestException` and
   the 5-minute `TaskCanceledException` propagate out of the loop; 429 burns three blind attempts in 22 s
   while ignoring `Retry-After`. The reviewer scoped cursor checkpointing explicitly out.

All three are small local fixes. Left open deliberately: the C# normalizer and writer are slated for
replacement by a Node implementation in the EA repo, so repairing them now would be work on code the
operator has already decided to discard. M3 is the one that would matter if this collector were ever run
unattended.
