# Internal Recon

Recon for the mapping stage. Everything below is cited. Where I could not find something, it says so.

## Durable sources read

| Source | Path | What it settles |
|---|---|---|
| Node normalizer repo | `/Users/user/Dev/Uri/localprojects/AdapterDataNormalizer` | Q1 conventions, existing signatures |
| Live pg_catalog dumps, prod-eu | `/Users/user/Dev/cymulate-exposure-analytics/cybi-db-models/database-schemas-up-to-date/prod-eu/integration/` | Q2 column sets, generated 2026-07-10 21:05:17+00 |
| Live pg_catalog dumps, stg | `.../database-schemas-up-to-date/stg/integration/` | Q2 column sets, generated 2026-07-12 09:36:07+00 |
| Prisma enum source | `.../cybi-db-models/prisma/schema/enum.prisma` | Q3 enum members incl. `@map` |
| Migration DDL | `.../cybi-db-models/migrations/` (1,599 dirs) | Q3 confirmation |
| Prisma models for both tables | `.../prisma/schema/integration.parser-output-{assets,exposures}-enrich.prisma` | Prisma-vs-live drift |
| C# prototype | `/Users/user/Dev/Uri/localprojects/adapter-data-normalizer/src/` | Q4 constants and rules |
| Promotion boundary consumer | `/Users/user/Dev/cymulate-exposure-analytics/apps/create-entities-tp/src/app/create-entities-third-party/repositories/parsed-data.repository.ts` | The twelve fingerprint columns; enum-cast row-drop behaviour |
| Live probe runs (this session) | see Landmines L1, L6, L7 | Current measured baselines |

**Repo rules file: none.** No `CLAUDE.md`, no `RULES.md`, no `.claude/` in `/Users/user/Dev/Uri/localprojects/AdapterDataNormalizer` (verified by `ls`). The only governing docs are `README.md` and the four task files under `ai/active/2026-09-03_1726_mapping-stage-target-rows/`.

## Files in scope

### Exists today (Node repo)

| Path | Lines | Role |
|---|--:|---|
| `src/manifest.ts` | 190 | Manifest types + path grammar + `assertLaneManifest` |
| `src/reader.ts` | 224 | `readLane` streaming generator. **Contract says do not modify.** |
| `src/describe.ts` | 175 | Derives a manifest by reading a lane (stands in for the collector) |
| `src/cli/describe.ts` | 66 | CLI wrapper; hand-rolled `parseArgs` |
| `src/cli/probe.ts` | 88 | CLI wrapper; measurement harness |
| `manifests/falcon-assets.json` | — | asset lane, `recordPath '$'`, `parentKey ['$.aid','$.id']`, 99 record fields |
| `manifests/falcon-findings.json` | — | exposure lane, `recordPath '$.findings[*]'`, `parentPath '$.host'`, `parentKey ['$.aid','$.host.id']`, 13 record fields, 77 parent fields |
| `manifests/tenable-findings.json` | — | exposure lane, `recordPath '$.findings[*]'`, `parentPath '$.host'`, `parentKey ['$.uuid','$.host.id']`, 20 record fields, 69 parent fields |
| `package.json` | 12 | **Zero dependencies**, `"type": "module"`, `engines.node >=22.6`, scripts run `node --experimental-strip-types` |
| `tsconfig.json` | 14 | `strict`, `noEmit`, `verbatimModuleSyntax`, `allowImportingTsExtensions`, `types: []`, `lib: ["ES2023"]` |
| `.gitignore` | 3 | `node_modules/`, `data/`, `*.log` |

### Exact exported surface, `src/manifest.ts`

```ts
export type Framing = 'ndjson';                                          // :14
export type Entity = 'asset' | 'exposure';                               // :17
export type FieldSource = 'declared' | 'observed';                       // :24
export type JsonKind = 'string'|'number'|'boolean'|'object'|'array';     // :27
export type FieldDescriptor = {                                          // :29
  readonly name: string;
  readonly kinds: readonly JsonKind[];
  readonly presence: number;              // 0..1
  readonly elementKinds?: readonly JsonKind[];
};
export type Layout = {                                                   // :59
  readonly framing: Framing;
  readonly recordPath: string;
  readonly parentPath?: string;
  readonly parentKey: readonly string[];  // ordered candidates
};
export type LaneManifest = {                                             // :80
  readonly manifestVersion: 1;
  readonly lane: string;
  readonly entity: Entity;
  readonly vendor: string;
  readonly flow?: string;
  readonly batch: { readonly generationId: string; readonly createdUtc: string };
  readonly layout: Layout;
  readonly counts: { readonly lines: number; readonly records: number };
  readonly fieldSource: FieldSource;
  readonly fields: {
    readonly record: readonly FieldDescriptor[];
    readonly parent?: readonly FieldDescriptor[];
  };
};
export type ResolvedPath =                                               // :106
  | { readonly kind: 'line' }
  | { readonly kind: 'array'; readonly property: string };

export function resolveRecordPath(recordPath: string): ResolvedPath;                  // :118
export function resolveKeyPath(path: string): readonly string[];                      // :134
export function resolveParentPath(parentPath: string): string;                        // :145
export function assertLaneManifest(value: unknown): asserts value is LaneManifest;    // :159
```

Path grammars are three module-private regexes, `src/manifest.ts:110-112`:
`ARRAY_PATH = /^\$\.([A-Za-z_][A-Za-z0-9_]*)\[\*\]$/`,
`OBJECT_PATH = /^\$\.([A-Za-z_][A-Za-z0-9_]*)$/`,
`KEY_PATH = /^\$\.([A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)?)$/`.

### Exact exported surface, `src/reader.ts`

```ts
export type LaneRecord = {                                               // :21
  readonly lineNumber: number;      // 1-based
  readonly recordIndex: number;     // 0 when the record is the line
  readonly parentKey: string;
  readonly parent: Readonly<Record<string, unknown>> | undefined;
  readonly record: Readonly<Record<string, unknown>>;
};
export type ReadStats = {                                                // :41
  lines: number;
  records: number;
  readonly unknownFields: Map<string, number>;
  readonly absentFields: Set<string>;
  readonly keySources: Map<string, number>;
  bytes: number;
};
export function newReadStats(): ReadStats;                               // :58
export class LaneContractError extends Error {                           // :70
  readonly lineNumber: number;
  constructor(message: string, lineNumber: number);   // message becomes `${message} (line ${n})`
}
export async function* readLane(                                         // :167
  filePath: string,
  manifest: LaneManifest,
  stats: ReadStats,
): AsyncGenerator<LaneRecord, void, undefined>;
```

Not exported (module-private, would need re-implementation or export if the mapper needs them):
`fieldNames`, `asObject`, `valueAt`, `asKey`, `readParentKey`, `noteFields` (`src/reader.ts:80-160`).

## Patterns to mirror

Observed conventions, with the evidence. Copy these; they are consistent across all five source files.

1. **Module doc comment first, always a `/** … *&#47;` block, and it states *why the module exists*, not what it does.** `src/manifest.ts:1-11`, `src/reader.ts:1-8`, `src/describe.ts:1-9`, `src/cli/probe.ts:1-7`. `src/cli/describe.ts:1` is a one-line variant.
2. **Comments carry the measured evidence.** They cite record counts, file sizes and named upstream components: "the real Falcon lane is 497 MB in 151 lines" (`src/reader.ts:164`); "49 of 298 hosts state a sensor `aid` and all 298 state the Discover `id`" (`src/manifest.ts:66-70`); "IntegrationInfra `NdjsonBatchEmitter`" (`src/manifest.ts:43-44`). Comment density is high on *decisions* and zero on mechanics.
3. **Functional, no classes** — except `LaneContractError extends Error` (`src/reader.ts:70`). Everything else is exported `function` / `async function*` plus `type` aliases.
4. **`type` aliases, never `interface`.** Every field is `readonly` on read-only shapes; `ReadStats` deliberately leaves counters mutable (`lines`, `records`, `bytes`) and marks the containers `readonly`.
5. **Options are a single `readonly` options object**, one per entry point: `DescribeOptions` (`src/describe.ts:79-92`). Multi-arg is used only for the hot path (`readLane(filePath, manifest, stats)`).
6. **Caller-owned mutable state, zero module state.** `ReadStats` is created by the caller via `newReadStats()` and passed in, explicitly "so the reader keeps no state of its own and two concurrent reads cannot interfere" (`src/reader.ts:37-40`). Mirror this for any mapping counters.
7. **Errors: throw eagerly with the offending value in the message.** `resolveRecordPath` throws `` `Unsupported recordPath '${recordPath}'. Expected '$' or '$.<property>[*]'.` `` (`src/manifest.ts:124-126`). Load-time validation is a separate `assert*` function that throws (`assertLaneManifest`), not a result type. Data-level failure at stream time is `LaneContractError` with a line number.
8. **Fatal over skip, stated as a rule.** "A record with no usable candidate is fatal rather than skippable" (`src/reader.ts:122-124`); README:83 "A missing parent key is fatal. Silently skipping is how orphans reach production."
9. **Naming:** camelCase functions/values, PascalCase types, SCREAMING_SNAKE module consts (`ARRAY_PATH`, `USAGE`). Full words, no abbreviations (`elementKinds`, `parentProperty`, `descriptors`). Loop counters are `index`, not `i` (`src/reader.ts:132`, `:213`).
10. **Imports:** `node:` prefix always; local imports carry the `.ts` extension (`from './manifest.ts'`); `import { type X, fn }` inline type modifier form (`src/reader.ts:12-18`).
11. **Optional fields are spread conditionally, never set to `undefined`:** `...(options.flow ? { flow: options.flow } : {})` (`src/describe.ts:158`, `:163`, `:171`).
12. **Non-null assertion `!` used freely after a regex/length guard** (`src/manifest.ts:122`, `src/reader.ts:133-134`). `strict` is on; this is the house style for "already proven present".
13. **CLI shape:** hand-rolled `parseArgs` returning `Readonly<Record<string,string>> & { _: readonly string[] }`, a `USAGE` const, `process.exitCode = 1` (never `process.exit`), `await main()` at top level, human output on stdout via `console.log` and diagnostics on stderr via `console.error` (`src/cli/describe.ts:7-64`). Measurements print as one space-joined `key=value` line (`src/cli/probe.ts:52-63`).
14. **README tone:** measured tables, an explicit "Deliberate limits" section, and a "Not yet resolved" section that names what was *not* reachable. Criterion 9 asks for an honest extension of exactly this.

## Shared surface to freeze

Anything a worker touches here collides with another worker. Freeze the signatures before splitting work.

| Surface | Where it must live | Frozen shape |
|---|---|---|
| Target column contract (column list, pg type, nullability, enum type name, PK) | one generated data file | Must carry provenance: source repo, commit, date, cluster (criterion 10) |
| The five enum member sets | same generated file | `@map` values only — see Ground truth G3 |
| Row types (`AssetRow`, `ExposureRow`) | one module | Derived from the column contract, not hand-typed twice |
| `uuid5(namespace, name) -> string` | exactly one module | Constraint: "Exactly ONE id-derivation code path serves both lanes" |
| `NAMESPACE` GUID | that module | `7f6b1f26-9a3e-5d41-8c0b-2a5f4d9e1b73` — verbatim |
| `KEY_SEPARATOR` | that module | `U+001F` |
| `FIELD_SEPARATOR`, `ABSENT`, list separator | content module | `U+001E`, `U+0000`, `U+001D` |
| `canonicalJson(node) -> string` | one module | Ordinal key sort, array order preserved, numeric literals as-arrived |
| Mapping-data schema (the per-vendor DSL) | one module | Every worker writing mapping data depends on it |
| `declaredFields` verification against `LaneManifest.fields` | one module | Load-time, before any record read |
| Mapping counters (the `ReadStats` analogue) | one module | Caller-owned, per pattern 6 |
| `src/reader.ts` | — | **Do not modify.** Contract constraint. |
| `src/manifest.ts` | — | Do not change `recordPath` / `parentKey` semantics. Additive-only if at all. |

## Disjoint sets available

The work **can** be split, but only after the shared surface above is written and frozen by one owner. Two of the sets are strictly downstream of it.

**Stage 0 — one owner, blocking (cannot be parallelised).** Produces the frozen surface.
- `src/target/contract.generated.ts` (or `.json` + a loader) — generated column + enum artifact with provenance
- `src/target/contract.ts` — types over it
- the generator script that reads the two dump `.md` files and `enum.prisma`
- `src/ids/uuid5.ts` — namespace, separators, `uuid5`, `deriveRowId`

**Then three genuinely disjoint sets:**

| Set | Files owned | Depends on |
|---|---|---|
| **A — identity and canonicalisation** | `src/rows/canonicalJson.ts`, `src/rows/exposureContent.ts` | Stage 0 |
| **B — mapping engine and enum validation** | `src/mapping/schema.ts`, `src/mapping/engine.ts`, `src/mapping/validate.ts` | Stage 0 |
| **C — mapping data** | `mappings/falcon-assets.json`, `mappings/falcon-findings.json`, `mappings/tenable-findings.json` | Stage 0 + B's schema |

**Overlapping files that must have exactly one owner** (list them explicitly in the plan):
- `src/cli/*` — a new `map` CLI plus the measurement harness. Both A and B want to print counters. Give the CLI to whoever owns B, or make it a fourth set after A and B land.
- `README.md` — criterion 9. Single owner, last.
- `package.json` — only if a script is added. One owner.
- `ai/active/.../execution_notes.md` — single owner, last.

**Recommendation:** Stage 0 by one worker, then A and B in parallel, then C, then CLI + README + notes. C is small enough to fold into B's owner if the mapping schema turns out to need iteration (it likely will — see Landmine L1).

## Ground truth

### G2 — Target column sets

Table names are **`parser_output_assets_enrich`** and **`parser_output_exposures_enrich`** — *assets* plural. The task brief and `prompt_contract.md` both say `parser_output_asset_enrich` (singular). That table does not exist. See Landmine L4.

Sources: `prod-eu` dump generated **2026-07-10 21:05:17+00**; `stg` dump generated **2026-07-12 09:36:07+00** (dates from `_overview.md:3` of each cluster; the prod-eu table file repeats it at line 3, the stg table file does not and points at `_overview.md`).

#### `integration.parser_output_assets_enrich`

`#` is the pg `attnum`. **It is not contiguous** — attnum 3 is a dropped column (`20260420130000_parser_output_drop_hash_id`). Never address a column by position.

| # | Column | Postgres type | Nullable | Default | Enum |
|--:|---|---|---|---|---|
| 1 | `id` | `uuid` | NOT NULL | — | |
| 2 | `instance_id` | `uuid` | NOT NULL | — | |
| 4 | `client_id` | `text` | null | — | |
| 5 | `client_integration_id` | `uuid` | null | — | |
| 6 | `type` | `cybi.asset_type` | null | — | **`cybi.asset_type`** |
| 7 | `value` | `text` | null | — | |
| 8 | `os_type` | `cybi.asset_host_os_type` | null | — | **`cybi.asset_host_os_type`** |
| 9 | `os_version` | `text` | null | — | |
| 10 | `os_build` | `text` | null | — | |
| 11 | `fqdn` | `text` | null | — | |
| 12 | `group_names` | **`text[]`** | null | `ARRAY[]::text[]` | |
| 13 | `site_names` | **`text[]`** | null | `ARRAY[]::text[]` | |
| 14 | `tags` | **`text`** | **NOT NULL** | **`'{}'::text`** | |
| 15 | `ip_address` | **`text[]`** | null | `ARRAY[]::text[]` | |
| 16 | `first_seen` | `timestamp(3) without time zone` | null | — | |
| 17 | `last_seen` | `timestamp(3) without time zone` | null | — | |
| 18 | `integration_setting_id` | `uuid` | null | — | |
| 19 | `integration_setting_flow_id` | `uuid` | null | — | |
| 20 | `connector_flow_name` | `text` | null | — | |
| 21 | `additional_fields` | `jsonb` | null | — | |
| 22 | `created_at` | `timestamp(3) without time zone` | null | — | |
| 23 | `risk_score` | `double precision` | null | — | |
| 24 | `batch_id` | `uuid` | null | — | **stg only** |

- prod-eu: 22 columns. stg: 23 columns.
- PK on both: `parser_output_assets_enrich_pkey` — `PRIMARY KEY (id, instance_id)`.
- text[] columns: `group_names`, `site_names`, `ip_address`. `tags` is **plain text**, not an array.
- timestamp(3) without time zone: `first_seen`, `last_seen`, `created_at`.
- Citations: prod-eu `.../prod-eu/integration/parser_output_assets_enrich.md:11-32`, PK `:36`; stg `.../stg/integration/parser_output_assets_enrich.md:11-33`, PK `:37`.

#### `integration.parser_output_exposures_enrich`

attnum 4 is dropped (same `drop_hash_id` migration).

| # | Column | Postgres type | Nullable | Default | Enum |
|--:|---|---|---|---|---|
| 1 | `id` | `uuid` | NOT NULL | — | |
| 2 | `instance_id` | `uuid` | NOT NULL | — | |
| 3 | `cve_id` | `text` | **NOT NULL** | — | |
| 5 | `client_id` | `text` | null | — | |
| 6 | `client_integration_id` | `uuid` | null | — | |
| 7 | `name` | `text` | null | — | |
| 8 | `display_name` | `text` | null | — | |
| 9 | `type` | `cybi.exposure_type` | null | — | **`cybi.exposure_type`** |
| 10 | `severity` | `cybi.exposure_source_severity` | null | — | **`cybi.exposure_source_severity`** |
| 11 | `asset_id` | `uuid` | null | — | |
| 12 | `status` | `cybi.exposure_status` | null | — | **`cybi.exposure_status`** |
| 13 | `first_seen` | `timestamp(3) without time zone` | null | — | |
| 14 | `last_seen` | `timestamp(3) without time zone` | null | — | |
| 15 | `integration_setting_id` | `uuid` | null | — | |
| 16 | `integration_setting_flow_id` | `uuid` | null | — | |
| 17 | `connector_flow_name` | `text` | null | — | |
| 18 | `additional_fields` | `jsonb` | null | — | |
| 19 | `description` | `text` | null | — | |
| 20 | `mitigation` | `text` | null | — | |
| 21 | `created_at` | `timestamp(3) without time zone` | null | — | |
| 22 | `batch_id` | `uuid` | null | — | **stg only** |

- prod-eu: 20 columns. stg: 21 columns.
- PK on both: `parser_output_exposures_enrich_pkey` — `PRIMARY KEY (id, instance_id)`.
- **No text[] columns on this table.** No varchar(n) anywhere on either table.
- timestamp(3) without time zone: `first_seen`, `last_seen`, `created_at`.
- Citations: prod-eu `.../prod-eu/integration/parser_output_exposures_enrich.md:11-30`, PK `:34`; stg `.../stg/integration/parser_output_exposures_enrich.md:11-31`, PK `:35`.

#### Every prod-eu ↔ stg difference

| Difference | prod-eu | stg |
|---|---|---|
| `batch_id uuid` null on **both** tables | absent | present (assets attnum 24, exposures attnum 22) |
| assets index `..._instance_id_batch_id_idx` | absent | present |
| assets index `..._client_id_os_type_idx` | present | present |
| assets index `..._instance_id_idx` | present | present |
| exposures index `..._client_id_asset_id_idx` | present | **absent** |
| exposures index `..._client_id_cve_id_idx` | present | **absent** |
| exposures index `..._instance_id_idx` | present | **absent** |
| exposures index `..._instance_client_idx` (instance_id, client_id) | absent | **present** |
| exposures index `..._instance_id_batch_id_idx` | absent | **present** |
| Row estimate / size | assets 10,000 / 1.1 GB; exposures 3,945,296 / 18.8 GB | both 0 rows; 42.6 MB / 20.6 MB |

Column types, nullability, defaults and the PK are **identical** between clusters for every shared column. The only column-set difference is `batch_id`.

**Independent confirmation:** the C# prototype already generates this exact artifact from these exact files. `/Users/user/Dev/Uri/localprojects/adapter-data-normalizer/src/Contract/Generated/label-contract.prod-eu.json` has 22 asset + 20 exposure columns; `label-contract.stg.json` has 23 + 21, `batch_id` last on both. Its generator resolves paths in `src/Contract/Generation/ContractSources.cs:30-49` — same dump directory, table names spelled `parser_output_assets_enrich` / `parser_output_exposures_enrich`.

### G3 — The five enums

From `/Users/user/Dev/cymulate-exposure-analytics/cybi-db-models/prisma/schema/enum.prisma`. **Emitted database string** = `@map` value where present, else the identifier. `@@schema("cybi")` lines excluded from counts. All five live in schema `cybi`.

**`asset_type` — 23 members** (`enum.prisma:50-76`). No `@map` on any member.
```
host, ip, user, domain, application, edr, waf, seg, swg, siem, ips_ids,
dlp, krs, cwpp, cdr, antivirus, soar, asm, phishing, vrt, hopper, avm,
cloud_resource
```

**`asset_host_os_type` — 15 members** (`enum.prisma:78-96`). **Two members carry `@map`.**

| Prisma identifier | Emitted database string |
|---|---|
| `windows` | `windows` |
| `windows_server` | **`windows-server`** (`@map`, `:80`) |
| `linux` | `linux` |
| `mac` | `mac` |
| `macos` | `macos` |
| `android` | `android` |
| `ios` | `ios` |
| `gcp` | `gcp` |
| `aws` | `aws` |
| `azure` | `azure` |
| `kubernetes` | `kubernetes` |
| `active_directory` | **`active-directory`** (`@map`, `:90`) |
| `ubuntu` | `ubuntu` |
| `debian` | `debian` |
| `other` | `other` |

**`exposure_type` — 12 members** (`enum.prisma:212-227`). No `@map`.
```
vulnerability, bas, cart, asm, asm_vulnerability, asm_misconfiguration,
cloud_misconfiguration, active_directory_misconfiguration, phishing, vrt,
hopper, avm
```

**`exposure_source_severity` — 5 members** (`enum.prisma:191-199`). No `@map`. Comment at `:190` records the ordering: `critical < high < medium < low < info` ascending.
```
critical, high, medium, low, info
```

**`exposure_status` — 3 members** (`enum.prisma:98-104`). No `@map`.
```
opened, resolved, reopened
```

Do **not** confuse `exposure_status` with `exposure_status_phase2` (`enum.prisma:106-113`: `exposed`, `not_exposed`, `fixed`, `exposed_again`). Different type; the `_enrich` column is typed `cybi.exposure_status`.

#### Migration DDL cross-check — all five CONFIRMED, no disagreement

| Enum | DDL trail | Final members | Agrees with Prisma |
|---|---|---:|---|
| `asset_type` | `20250306140804_init/migration.sql:14` (13 members) → recreated as `asset_type_new` `20250424182444_.../migration.sql:29-47` (17) → `ADD VALUE` asm, phishing, vrt, hopper, avm `20250626120555_.../migration.sql:12-16` (22) → `ADD VALUE IF NOT EXISTS 'cloud_resource'` `20260729120000_add_cloud_resource_to_asset_type/migration.sql:14` (23) | 23 | ✅ |
| `asset_host_os_type` | `20250617115755_asset_host_os_type_add_enum/migration.sql:4-19` — created once, never altered. DDL literals include `'windows-server'` and `'active-directory'` | 15 | ✅ **and the hyphenated spellings are confirmed at the DDL level** |
| `exposure_type` | `init:35` (3) → `exposure_type_new` `20250424182444:102-109` (6) → `ADD VALUE` phishing, vrt, hopper, avm `20250702085724_add_exposures_types/migration.sql:9-12` (10) → `ADD VALUE` asm_vulnerability, asm_misconfiguration `20250925114908_update_exposure_types_enum/migration.sql:1-2` (12) | 12 | ✅ |
| `exposure_source_severity` | `init:29` — created once, never altered | 5 | ✅ |
| `exposure_status` | `init:17` (opened/resolved/reopened) → replaced by exposed/not_exposed/fixed/exposed_again `20250325082603_change_enums/migration.sql:201-234` → **reverted** back to opened/resolved/reopened `20250401085913_revent_exposure_status/migration.sql:18,46`, with `exposure_status_phase2` created alongside to hold the four-value set | 3 | ✅ |

I grepped every `CREATE TYPE` and `ALTER TYPE` across all 1,599 migration directories for these five names; the lists above are complete. The generated C# contract (`label-contract.prod-eu.json`, `enums` array) reproduces all five member lists **byte-identically** to what I derived — a third independent agreement.

`PrismaEnumParser.cs:6-15` states the `@map` rule and why it is load-bearing: "Emitting the identifier would make every Windows Server and Active Directory host fail its cast."

### G4 — C# code to port

#### `Ids/RowIdDerivation.cs` (112 lines)

RFC 4122 §4.3 UUID v5 (name-based, SHA-1) over a frozen namespace. Implements `IRowIdDerivation`.

**Constants that must be preserved verbatim:**

| Constant | Value | Line |
|---|---|--:|
| `Namespace` | `7f6b1f26-9a3e-5d41-8c0b-2a5f4d9e1b73` | 29 |
| `KeySeparator` | `''` (ASCII unit separator) | 36 |

**Derivation rules:**
- Asset / any row id: `Uuid5(Namespace, $"{instanceId:D}{KeySeparator}{parentKey}")` (`:45`). `instanceId:D` is the lowercase hyphenated 8-4-4-4-12 form.
- Exposure name: `ExposureKey(parentKey, cveId, content) => $"{parentKey}{KeySeparator}{cveId}{KeySeparator}{content}"` (`:58-59`). Note this name does **not** include `instanceId` — it is passed as the first argument to `Derive`, which prefixes it. So the full v5 name for an exposure is
  `{instanceId:D}{parentKey}{cveId}{content}`.
- Blank `parentKey` throws (`ArgumentException.ThrowIfNullOrWhiteSpace`, `:43`).
- Byte mechanics (`:68-111`): SHA-1 over `namespaceBytes(16, network order) || utf8(name)`; take first 16 bytes; `bytes[6] = (bytes[6] & 0x0F) | 0x50`; `bytes[8] = (bytes[8] & 0x3F) | 0x80`. The `SwapEndianFields` dance (swap 0↔3, 1↔2, 4↔5, 6↔7) exists **only** because .NET `Guid` stores the first three fields in host order. **A Node port must not do it** — a hex string built straight from the big-endian digest is already correct. Verified: see G5.
- Design note worth porting (`:14-21`): no salt, because run scope comes from `instance_id`; a salt would break re-normalization, and namespace-and-key-only would collapse the generations Exposure Analytics demotes via `latest = false`.
- Design note (`:52-55`): content is appended to the v5 name rather than digested first — uuid5 already SHA-1s the whole name, so a pre-digest adds a second collision surface for nothing.

#### `Rows/CanonicalJson.cs` (75 lines)

Canonicalisation rules, exactly four:

1. **Object members sorted by key, ordinal comparison** — `members.OrderBy(m => m.Key, StringComparer.Ordinal)` (`:51`). Ordinal, not culture-aware.
2. **No insignificant whitespace** — `{`, `,`, `:`, `}` emitted bare (`:48-60`).
3. **Array order preserved**, because it is significant (`:63-74`).
4. **Numeric and other scalar literals written as they arrived** — `node.ToJsonString()` (`:41`). Explicitly *not* normalised: `1.0` and `1.00` stay distinct. Rationale at `:13-16`: "normalizing … is beyond what identity requires here, and no vendor varies the spelling of a value between records of one batch."
5. `null` → the four characters `null` (`:31-33`).
6. Keys are re-serialised through the JSON string serialiser (`JsonSerializer.Serialize(member.Key)`, `:56`), so key escaping is JSON-standard.

**Node port hazard:** `JSON.stringify` on a number does normalise (`1.00` → `1`). To preserve rule 4 the port must keep the raw literal text from the source line, or accept a documented deviation. This is a real decision the plan should name.

#### `Rows/ExposureContent.cs` (76 lines)

**Constants verbatim:**

| Constant | Value | Line |
|---|---|--:|
| `FieldSeparator` | `''` (record separator) | 28 |
| `Absent` | `" "` | 34 |
| string-list join separator | `''` (group separator) | 73 |

**Composition** (`:49-63`): for each planned column, append `column.Name`, `FieldSeparator`, `Encode(value)`, `FieldSeparator`. So the content is `namevalue` repeated — a trailing separator after the last value. Column names are written **next to** their values so adding a column cannot make two previously distinct rows collide (`:38-39`).

**`Encode` value rules** (`:65-75`):

| Value | Encoding |
|---|---|
| `null` | `" "` (distinct from `""`) |
| `string` | as-is |
| `DateTime` | `ToString("O", InvariantCulture)` — ISO 8601 round-trip |
| jsonb | `CanonicalJson.Write(...)` |
| `double` | `ToString("R", InvariantCulture)` — round-trippable |
| `Guid` | `ToString("D")` — lowercase hyphenated |
| `IEnumerable<string>` | `string.Join('', items)` |
| anything else | `Convert.ToString(value, InvariantCulture) ?? Absent` |

**The exact column list hashed, and `created_at`:**

The list is not literal in `ExposureContent.cs`; it is `ColumnCoverage.Plan(table, Accessors, OutsideIdentity)` (`ExposureRowMapper.cs:109-110`), which walks `table.Columns` **in declared order** and drops anything in the exclusion map (`ColumnCoverage.cs:71-73`). Resolving that against the prod-eu column order gives **10 columns, in this order**:

```
name, display_name, type, severity, status, first_seen, last_seen,
additional_fields, description, mitigation
```

**`created_at` IS excluded.** Explicitly, and it is called "the load-bearing exclusion" — `ExposureRowMapper.cs:93-98`: "falls back to the clock, so including it would break reparse stability." The fallback is `draft.CreatedAt ??= normalizedAt` (`ExposureRowMapper.cs:186`).

Also excluded, each with a stated reason (`ExposureRowMapper.cs:70-83`): `id`, `instance_id`, `cve_id`, `asset_id`, `client_id`, `client_integration_id`, `integration_setting_id`, `integration_setting_flow_id`, `connector_flow_name`, `batch_id`.

**How this reaches the twelve fingerprint columns.** The promotion boundary's exposure dedup fingerprint is 12 columns (`parsed-data.repository.ts:70-77`):
```sql
md5(ROW(asset_id, cve_id, type, severity, status,
        name, display_name, description, mitigation,
        first_seen, last_seen, additional_fields)::text)
```
The C# identity covers those 12 as **10 in the content hash + `cve_id` + `parentKey`** — and `parentKey` is what `asset_id` is derived from (`ExposureRowMapper.cs:180`: `draft.AssetId = derivation.Derive(scope.InstanceId, parentKey)`). This is exactly criterion 7's "ordering constraint that `asset_id` precedes the exposure content hash": the derivation name is `instanceId ⟂ parentKey ⟂ cveId ⟂ content`, so the asset identity is *positionally before* the content. Same 12 columns, split across the name rather than all inside the digest.

For reference, the **asset** fingerprint at the boundary is 20 columns (`parsed-data.repository.ts:50-58`): `value, type, os_type, os_version, os_build, fqdn, group_names, site_names, tags, ip_address, first_seen, last_seen, sub_type, cloud_platform, region, cloud_account_id, cloud_account_name, cloud_provider_url, risk_score, additional_fields`. Six of those columns are not in the committed dumps — see Landmine L5.

#### `Rows/AssetRowMapper.cs` (113) and `Rows/ExposureRowMapper.cs` (243) — **were they vendor-specific? No.**

Answering the question honestly, because it changes the design:

**Neither RowMapper contains any vendor knowledge.** They are already generic engines. Evidence:

- Both take `(LabelContract contract, BatchManifest manifest, IRowIdDerivation derivation, DateTime normalizedAt)` — no vendor parameter, no vendor switch, no vendor name anywhere in either file.
- Their `Accessors` / `Setters` dictionaries are keyed on **target column names** (`ExposureColumns.Name`, `AssetColumns.OsType`, …), not vendor field names — `AssetRowMapper.cs:20-38`, `ExposureRowMapper.cs:50-64`.
- Binding is `if (!record.TryGet(column.Name, out var labeled)) continue;` (`LabelBinding.cs:35`) — it looks up the **column name** in the record.
- `ExposureRowMapper.cs:35-38` states it outright: "The CVE list is read under the target table's own `cve_id` column name. That name is the contract's, pinned by the manifest's `label_contract_version`, so the explode needs no vendor field name and no per-batch declaration."

**The per-vendor knowledge lives one stage upstream, in the collector, as code.** `src/ThinFalconCollector/Labels/FindingLabeler.cs:18-31` is a hand-written C# labeler:
```csharp
.Set(ExposureColumns.CveId,       VendorJson.String(finding, "cve", "id"))
.Set(ExposureColumns.Name,        VendorJson.String(finding, "vulnerability_id"))
.Set(ExposureColumns.DisplayName, DisplayName(finding))          // finding.apps[0].product_name_version
.Set(ExposureColumns.Severity,    enums.Severity(finding))
.Set(ExposureColumns.Mitigation,  Mitigation(finding))           // finding.remediation.entities[0].action
```
So the C# prototype's "no per-vendor code in the normalizer" holds **only because the collector emits records already keyed by target column name**. It moved the vendor code, it did not eliminate it.

**Consequence for this task.** The Node design's requirement — per-vendor mapping as *data* consumed by a generic engine — is a **genuinely new capability, not a port**. What ports cleanly is: the id derivation, the canonical JSON, the content composition, the column-coverage discipline (`ColumnCoverage.cs`: a column that is neither accessed nor *declared excluded with a reason* throws at construction), and the `additional_fields` carry rule (`LabelBinding.cs:63-84`: unclaimed labels merged in, declared value wins a name collision, nothing dropped). What does **not** port is any notion of a mapping DSL — there isn't one to port. Expect the mapping-data schema to be the highest-risk design work in this task, and note that `prompt_contract.md`'s own Stop Condition anticipates exactly this ("cannot express a real Falcon or Tenable mapping without an escape hatch to arbitrary code").

### G5 — uuid5 in Node with zero dependencies: CONFIRMED

`node:crypto` is sufficient. Exact API: **`createHash('sha1')`** (`require('node:crypto').createHash`). `crypto.getHashes()` includes `'sha1'` on the installed Node **v22.17.0**; `crypto.hash` (the one-shot form, Node ≥ 21.7) is also present.

Verified against the published RFC 4122 vector — namespace DNS `6ba7b810-9dad-11d1-80b4-00c04fd430c8`, name `www.example.com`:

```
computed  2ed6657d-e927-568b-95e1-2665a8aea6a2
expected  2ed6657d-e927-568b-95e1-2665a8aea6a2   ✅
```

Working implementation, ~10 lines, no dependency:
```js
import { createHash } from 'node:crypto';
const nsBytes = Buffer.from(namespaceUuid.replace(/-/g, ''), 'hex');
const digest  = createHash('sha1')
  .update(Buffer.concat([nsBytes, Buffer.from(name, 'utf8')]))
  .digest();
const b = Buffer.from(digest.subarray(0, 16));
b[6] = (b[6] & 0x0f) | 0x50;   // version 5
b[8] = (b[8] & 0x3f) | 0x80;   // RFC 4122 variant
const h = b.toString('hex');
// 8-4-4-4-12
```
No endian swapping — that step exists in the C# only to satisfy .NET's `Guid` layout. Buffer's hex output is already big-endian, which is what RFC 4122 specifies. `Buffer` is a global; no import needed, and `src/reader.ts:188` already uses `Buffer.byteLength` with `types: []` in tsconfig, so it type-checks today.

## Landmines

Ordered by how much damage each one does if missed.

### L1 — The committed Falcon findings manifest is missing `apps`, and `apps` is exactly what `display_name` needs. This blocks a stated constraint against real data.

I ran the existing probe over the real Falcon correlated-findings lane:

```
$ npm run probe -- manifests/falcon-findings.json \
    /Users/user/Dev/Uri/localprojects/IntegrationProbes/ProbeResults/FalconCorrelatedFindings_20260702_132204_279Z/correlated_*.json

files=21 lines=352 records=59,040 bytes=309 MB elapsed=0.9s records/s=65,640 MB/s=344 peakRSS=273 MB
parent key resolved via: $.aid=352
drift: fields not in manifest = 2: apps(59,040), suppression_info(59,040)
```

`apps` is present in **all 59,040 records** and absent from `manifests/falcon-findings.json` (13 record fields, verified: `aid, cid, closed_timestamp, confidence, created_timestamp, cve, data_providers, id, remediation, status, updated_timestamp, vulnerability_id, vulnerability_metadata_id`).

`prompt_contract.md` Constraints: *"A mapping declares the manifest fields it reads; the engine verifies them against the lane manifest at load time and fails before reading any record when one is absent."*

`FindingLabeler.cs:44-47` derives `display_name` from `finding.apps[0].product_name_version`. So a faithful Falcon exposure mapping **will be rejected at load time by its own engine** against the committed manifest. Three ways out, all of which the plan should name rather than discover:
1. Re-derive `manifests/falcon-findings.json` with `npm run describe` over the full lane (the manifest today is a 20-line / 40,000-record sample, `counts: {lines: 20, records: 40000}`).
2. Drop `display_name` from the Falcon mapping and lose a fingerprint column.
3. Make the load-time check a warning for `fieldSource: 'observed'` manifests — which weakens the constraint the manifest exists to provide.

Option 1 is correct and cheap. Do it in Stage 0.

### L2 — `parser_output_assets_enrich` really does have `tags text NOT NULL DEFAULT '{}'::text`. Confirmed, and it is a trap.

`tags` is **plain `text`**, NOT NULL, defaulting to the two-character string `{}` — while `group_names`, `site_names` and `ip_address` beside it are genuine `text[]` defaulting to `ARRAY[]::text[]`. Cited: prod-eu `parser_output_assets_enrich.md:23` vs `:21,:22,:24`; identical in stg (`:23` vs `:21,:22,:24`); Prisma agrees (`integration.parser-output-assets-enrich.prisma:15` `tags String @default("{}")` vs `:13,:14,:16` `String[] @default([])`).

Two consequences:
- A mapping that emits an array for `tags` is wrong. It is a text blob whose default happens to *look* like an empty pg array literal.
- Because it is NOT NULL with a default, the mapper must emit a value. The C# does: `draft.Tags ??= AssetRow.EmptyTags` (`AssetRowMapper.cs:111`).

### L3 — `cve_id` really is `text NOT NULL` on the exposures enrich table. Confirmed.

Cited: prod-eu `parser_output_exposures_enrich.md:13`; stg `:13`; Prisma `integration.parser-output-exposures-enrich.prisma:5` (`cve_id String`, no `?`). It is the only non-key NOT NULL column on the table.

The C# handles it by **exploding**: one record naming three CVEs becomes three rows; one naming none becomes **zero rows and a counted outcome**, never a row with a null key (`ExposureRowMapper.cs:24-27`, `:140-143`, `ExposureMapping.WithoutCve()`). Also worth porting: the CVE list is de-duplicated within one record because "a repeat inside one record would derive the same id twice and break the target's primary key" (`ExposureRowMapper.cs:189-192`, `:232`).

### L4 — The table name in the task brief and in `prompt_contract.md` is wrong.

Both say `integration.parser_output_asset_enrich` (singular *asset*). The real table is `integration.parser_output_assets_enrich`. Verified by `find` over the whole dump tree — only the plural spelling exists, in both clusters. The C# generator also spells it plural (`ContractSources.cs:46`). Fix the spelling in the code and the artifact; it is not a schema question.

### L5 — The committed dumps are stale by ~3 weeks against the migrations. Six asset columns exist in Prisma and in the promotion boundary's own fingerprint but not in either dump.

`integration.parser-output-assets-enrich.prisma:25-31` declares six more columns, marked "Cloud-inventory columns, mirrored from parser_output_assets":
```
sub_type, cloud_platform, region, cloud_account_id, cloud_account_name, cloud_provider_url
```
They were added by `migrations/20260729120100_add_cloud_columns_to_parser_output_assets/migration.sql`, which ALTERs **both** `parser_output_assets` and `parser_output_assets_enrich`. That migration is dated **2026-07-29** — *after* the prod-eu dump (2026-07-10) and the stg dump (2026-07-12). Neither dump shows them. The boundary's asset fingerprint already hashes all six (`parsed-data.repository.ts:50-58`), and its enrich INSERT already projects them (`:330-334`, `:352-375`).

The migration's own comment explains why the enrich table needs them: *"a REAL table (not a view) that create-entities-tp uses as the `LIKE ... INCLUDING DEFAULTS` source for its per-worker TEMP table. If the column is missing here, the temp table lacks it and the enrich INSERT fails."*

Decision the plan must make explicitly: generate the column contract from the **dumps** (criterion 10 wants a cluster recorded, and the dumps are the only per-cluster artifact) and accept a known-stale 22/23-column set, or from **Prisma** (28 asset columns, no per-cluster distinction) and lose the cluster provenance. Either is defensible; silently picking one is not. Note that `ColumnCoverage.Verify`'s posture — a column neither accessed nor declared-excluded **throws** — means adding six columns later is a hard failure, not a silent gap. That is the behaviour you want; just know it will fire.

### L6 — Every measured number in `README.md` is unreproducible on the data I can find. Criterion 8 asks you to compare against those numbers.

| README claim (README.md:37-40, :66-67) | What I measured today |
|---|---|
| Falcon findings, lab tenant: 474 MB, 246,212 records, 151 lines, 405 MB/s, drift **none** | 309 MB, **59,040** records, **352** lines, 344 MB/s, drift **2 fields** — `FalconCorrelatedFindings_20260702_132204_279Z/correlated_*.json`, 21 files, 324,434,722 bytes |
| Falcon findings, prod client: 42 MB, 25,570 records | no file of that size found; `/Users/user/Dev/Uri/ClientLogs/findings_000040.ndjson` is 217 MB |
| Falcon assets: 298 records; "298 rows carry the Discover `id` and only 49 carry a sensor `aid`" | 298 records ✅, but `$.aid=25 $.id=273`, **not** 49/249 — `/Users/user/Dev/Uri/falcon-assets.ndjson` (432,194 bytes) |
| Tenable findings: 12 MB, 1,521 records | 54 MB, **6,098** records, 33 lines, 429 MB/s, drift none — `/Users/user/Dev/Uri/ClientLogs/2026-08-31-tenable/findings_000001.ndjson` (56,993,066 bytes) |

The repo has **no `data/` directory** (`.gitignore:2` excludes it; `ls` confirms absent). The lane files live outside the repo, and the specific batches behind the README's table are not identifiable with confidence. Additional consequences:
- `manifests/falcon-findings-big.json`, named in success criterion 1, **does not exist**. Only `falcon-assets.json`, `falcon-findings.json`, `tenable-findings.json` are in `manifests/`.
- Criterion 8's "the 474 MB Falcon lane completing under a heap cap" has no 474 MB lane to run against. The largest coherent Falcon findings set I found is 309 MB.
- The 405 MB/s baseline cannot be reproduced, so "note any regression" has no fixed reference. **Recommendation:** re-baseline the reader alone on the datasets below, record those as the new reference in `execution_notes.md`, and report mapping throughput against *that* — not against the README's numbers.

**Usable lane files, confirmed by probe run:**

| Lane | Path | Measured |
|---|---|---|
| Falcon findings | `/Users/user/Dev/Uri/localprojects/IntegrationProbes/ProbeResults/FalconCorrelatedFindings_20260702_132204_279Z/correlated_*.json` (21 files) | 352 lines, 59,040 records, 309 MB |
| Falcon assets | `/Users/user/Dev/Uri/falcon-assets.ndjson` | 298 lines, 298 records, 432 KB |
| Tenable findings | `/Users/user/Dev/Uri/ClientLogs/2026-08-31-tenable/findings_000001.ndjson` | 33 lines, 6,098 records, 54 MB |

### L7 — `manifests/tenable-findings.json` does not match the Tenable file under `IntegrationProbes`. Two different Tenable shapes exist on disk.

```
$ npm run probe -- manifests/tenable-findings.json \
    .../ProbeResults/TenableIo_20260611_104324_803Z/findings.ndjson
LaneContractError: No usable parent key at any of '$.uuid', '$.host.id' (line 1)
```
That file is a **flat** lane — top-level keys `asset, output, plugin, port, scan, severity, severity_id, severity_default_id, severity_modification_type, first_found, last_found, state, indexed, source, finding_id`. The manifest describes the **envelope** shape.

The manifest's real match is `/Users/user/Dev/Uri/ClientLogs/2026-08-31-tenable/findings_000001.ndjson`, whose line-1 keys are `uuid, chunk, isLastChunk, findingsInChunk, host, findings` with 219 findings in the first chunk — probes clean, drift none. Use that path. Do not use the `IntegrationProbes` Tenable file for criterion 1.

Note also that the manifest's `fields.record` list happens to match the *flat* file's keys, because a finding inside the envelope has the same shape as a flat-lane line. That coincidence makes the mismatch easy to miss.

### L8 — Two `cybi-db-models` checkouts exist and one is stale. Reading the wrong one gives a wrong enum list.

| Path | Migrations | `asset_type` members |
|---|--:|--:|
| `/Users/user/Dev/cymulate-exposure-analytics/cybi-db-models` (HEAD `e2ee9a302`) | 1,599 | 23 |
| `/Users/user/Dev/cybi-db-models` (HEAD `98b95949`) | 970 | **22 — missing `cloud_resource`** |

`diff` of the two migration listings is `968a969,1597`: the standalone checkout is a strict prefix, missing 629 migrations. `diff` of the two `enum.prisma` files is exactly one line — `72a73 > cloud_resource`. The standalone checkout also has **no** `database-schemas-up-to-date/` directory at all, so it cannot answer Q2.

**Use `/Users/user/Dev/cymulate-exposure-analytics/cybi-db-models` only.** Pin its commit in the generated artifact's provenance (criterion 10) — `e2ee9a302` at the time of this recon.

### L9 — A non-member enum value causes a **silent row drop**, not an error, one stage downstream. This is what makes enum validation load-bearing rather than hygiene.

`parsed-data.repository.ts:340-341`, the enrich step's `WHERE`:
```sql
AND f.type    = ANY(enum_range(NULL::cybi.asset_type)::text[])
AND (f.os_type IS NULL OR f.os_type = ANY(enum_range(NULL::cybi.asset_host_os_type)::text[]))
```
A row whose `type` is not an enum member is filtered out. Not rejected — **dropped, unremarked**. The cloud-columns migration says so in as many words: *"The enrich step casts `type::cybi.asset_type` and `os_type::cybi.asset_host_os_type`, and an unrecognized value there silently DROPS the whole row"* (`migrations/20260729120100_add_cloud_columns_to_parser_output_assets/migration.sql`, Type-choices comment).

One precision that matters for this task: that filter runs on `integration.parser_output_assets`, whose columns are `text`. The Node normalizer writes to `*_enrich`, whose columns are **already enum-typed** — so there a non-member is an INSERT error, not a silent drop. Either way, validating before emit is the only way to get a *counted* rejection instead of a mystery. Note the asymmetry the SQL encodes: `os_type` tolerates NULL, `type` does not tolerate anything outside the enum.

### L10 — Column length limits: none. Nothing can overflow.

Checked every column on both tables in both clusters. Types present: `uuid`, `text`, `text[]`, `jsonb`, `timestamp(3) without time zone`, `double precision`. **Zero `varchar(n)`, zero `char(n)`.** A long identity value cannot exceed a column limit — and in any case the composed exposure content never reaches Postgres: it is consumed by SHA-1 and only the resulting `uuid` is stored (`ExposureRowMapper.cs:164-167`). `cve_id` is unbounded `text`.

The one truncation risk is `timestamp(3)`: **millisecond precision**. A mapper emitting microseconds will have them rounded by Postgres, while the content hash would have been composed over the unrounded value (`ExposureContent.Encode` uses `"O"`, which emits 7 fractional digits). That does not break identity — the hash is computed before the write and ids are not recomputed from the stored row — but if anything downstream ever re-derives an id from a persisted row, it will not match. Truncate to milliseconds at compose time and the question never arises.

### L11 — Minor, but will cost time if unnoticed

- **`attnum` gaps.** `#` 3 is missing on the assets table and `#` 4 on the exposures table (dropped by `20260420130000_parser_output_drop_hash_id`). The dump's `#` column is the pg attnum, not an ordinal position. Address columns by name.
- **`batch_id` exists in stg but not prod-eu.** The C# declares it excluded with a reason rather than ignoring it: *"the manifest carries no batch identity; left at the column default"* (`AssetRowMapper.cs:54`, `ExposureRowMapper.cs:82`). Pick one cluster for the contract, record which (criterion 10), and declare `batch_id` excluded so `ColumnCoverage`-style verification passes on both.
- **`JSON.stringify` normalises numbers.** See G4 CanonicalJson rule 4. `1.00` becomes `1`, which diverges from the C#. Decide and document.
- **`Buffer` under `types: []`.** `tsconfig.json:11` sets `types: []`, so `@types/node` is not loaded — yet `src/reader.ts:188` already calls `Buffer.byteLength` and type-checks. Whatever makes that work also covers `createHash`; if it breaks, the existing reader breaks too, so it is not a new risk.
- **`falcon-findings.json` was derived from a sample.** `counts: {lines: 20, records: 40000}` — 40,000 of the ~59,040 records available. README:45-46 treats this as a proof that a sample suffices; L1 shows it did not, on this batch.

---

# Orchestrator corrections to this recon

Recon's factual ground truth (G2, G3, G4, G5) and landmines L2, L3, L4, L5, L9, L10, L11 were
re-checked and stand. Four items are corrected. They are recorded rather than edited out, because
recon's reasoning is legitimate and the corrections are about which files it looked at.

## C1 — L6 is wrong. The measured baseline reproduces exactly.

Recon reported every README number unreproducible and recommended re-baselining against a 309 MB
set. The data it was given does exist, at the path in its brief:

```
/private/tmp/claude-501/.../scratchpad/s3/falcon-findings-big.json   497,195,139 bytes
```

Re-run this session, after recon returned:

```
files=1 lines=151 records=246,212 bytes=474 MB elapsed=1.2s records/s=212,215 MB/s=409 peakRSS=224 MB
parent key resolved via: $.aid=151
drift: none - lane matches its manifest exactly
```

409 MB/s against the recorded 405 MB/s. Criterion 8 keeps its reference. Do NOT re-baseline against
the `IntegrationProbes` data.

## C2 — L1 is half right, and the correct reading is stronger.

`apps` is absent from `manifests/falcon-findings.json` — recon is right. But it is also absent from
the **data**: 0 of 246,212 findings carry it, and the data's distinct field names are exactly the
manifest's 13. The manifest is accurate for this batch.

Recon inferred the gap from the C# labeler reading `finding.apps[0].product_name_version`
(`FindingLabeler.cs`), which ran against a different Falcon batch — the lab tenant's 122,359
findings, not this batch's 246,212.

The real finding: `display_name` cannot be sourced from `apps` on this batch. A Falcon mapping that
declares a read of `apps` must therefore fail load-time verification against this manifest — which
is the checkability requirement working as designed, and gives criterion 2 a negative case from
**real data** instead of a contrived one. Use it.

## C3 — L7's recommendation does not apply to this task.

`manifests/tenable-findings.json` matches the Tenable lane in the scratchpad. Re-run this session:

```
files=1 lines=13 records=1,521 bytes=12 MB records/s=43,030 MB/s=351 peakRSS=130 MB
parent key resolved via: $.uuid=13
drift: none - lane matches its manifest exactly
```

Recon probed a different Tenable file under `IntegrationProbes` and correctly found it is a flat
lane that the envelope manifest cannot read. That is a genuinely useful observation — it is evidence
that a flat Tenable shape exists in the wild — but it is not a defect in the manifest, and the
scratchpad path is the one this task uses.

## C4 — L8's commit pin names the wrong repository.

`cybi-db-models` nested inside `cymulate-exposure-analytics` is its own git repository. Recon
reported `e2ee9a302`, which is the **outer** `cymulate-exposure-analytics` HEAD.

For criterion 10 provenance, pin `cybi-db-models` at `3d26e24ee429e9d9a55b20912c7d8c525df6d34c`.
L8's substance — that the standalone `/Users/user/Dev/cybi-db-models` checkout is stale by 629
migrations and lacks `cloud_resource` and any `database-schemas-up-to-date/` tree — stands.

## C5 — L5 confirmed, and it changes the target-shape source.

Verified: `sub_type`, `cloud_platform`, `region`, `cloud_account_id`, `cloud_account_name` and
`cloud_provider_url` are in Prisma and absent from the prod-eu dump. Newest migration is
`20260731090100`; the prod-eu dump is dated 2026-07-10. The dumps are ~3 weeks stale.

`prisma/schema/integration.parser-output-assets-enrich.prisma` carries 30 columns with enum types,
nullability, defaults, `String[]` vs `String` (so `tags String @default("{}")` — plain, correct) and
`@@id([id, instance_id])`. It includes a nullable `batch_id`, i.e. it is the stg superset.

**Decision:** take the column SET from Prisma, and the exact Postgres type precision
(`timestamp(3) without time zone`) from the dumps, citing both. This flips the decision recorded in
`decisions.md`; the drift is recorded there.
