# Phase 1 notes — mapping engine and enum validation

Worker: W2 (mapping-engine-and-enum-validation).
Files owned and written: `src/mapping/engine.ts` (656 lines), `src/mapping/validate.ts` (306 lines).
Nothing else in `src/`, `mappings/`, `tests/`, `manifests/`, `package.json` or `README.md` was touched.
Nothing committed.

Verification method: no TypeScript compiler is installed and the repo stays dependency-free, so
every claim below was produced by RUNNING the modules under
`node --experimental-strip-types`. Harnesses live in the session scratchpad, not in the repo:
`check-engine.ts`, `check-memory.ts`, `check-r3.ts`, plus `ASSET_SPEC.json`,
`EXPOSURE_SPEC.json`, `TENABLE_SPEC.json` under
`/private/tmp/claude-501/-Users-user-Dev-Uri-localprojects-AdapterDataNormalizer/75719977-6fbd-4678-ab6d-c694c4efa12e/scratchpad/`.
Those three spec files are a reasonable starting point for the `mappings/` worker.

---

## Exported surface

### `src/mapping/engine.ts`

```ts
export type MapLaneOptions = {
  readonly spec: MappingSpec;
  readonly manifest: LaneManifest;
  readonly instanceId: string;
  readonly normalizedAt: Date;
};

export type MappedRow = {
  readonly lineNumber: number;
  readonly recordIndex: number;
  readonly row: TargetRow;
};

export class MappingError extends Error {
  readonly lineNumber: number;
}

export function mapLane(
  records: AsyncIterable<LaneRecord>,
  options: MapLaneOptions,
  stats: MappingStats,
): AsyncGenerator<MappedRow, void, undefined>;
```

### `src/mapping/validate.ts`

```ts
export type EnumRejection = {
  readonly column: string;
  readonly enumType: EnumTypeName;
  readonly value: string;
  readonly lineNumber: number;
  readonly recordIndex: number;
};

export type MappingStats = {
  records: number;
  rows: number;
  rejectedRecords: number;
  withoutCve: number;
  readonly enumRejections: Map<string, Map<string, number>>;  // column -> value -> count
  readonly rejectionSamples: EnumRejection[];                 // capped at 25
};

export function newMappingStats(): MappingStats;
export function assertMappingUsable(value: unknown, manifest: LaneManifest): asserts value is MappingSpec;
export function enumColumnsOf(table: TargetTable): readonly TargetColumn[];
export function firstEnumViolation(
  columns: readonly TargetColumn[],
  row: Readonly<Record<string, unknown>>,
  lineNumber: number,
  recordIndex: number,
): EnumRejection | undefined;
export function noteEnumRejection(stats: MappingStats, rejection: EnumRejection): void;
export function describeEnumRejection(rejection: EnumRejection): string;
```

`MappingStats` lives in `validate.ts` and is imported by `engine.ts`. One stats object for the whole
stage, one import direction, no cycle.

---

## The `additional_fields` question, answered plainly

**Yes, it is true. No, nothing warns.**

The carry rule is implemented so that which fields a mapping leaves unclaimed decides every exposure
id in the lane. The chain, each link verified:

1. `carryFields` builds the carried object from the record's fields whose top-level name is not in
   `plan.claimedFields`.
2. `plan.claimedFields` is `collectFieldReads(spec).record` — the frozen helper, top-level names
   only. So a mapping that reads `$.cve.description` claims **all** of `cve`.
3. `additional_fields` is in `CONTENT_COLUMNS` — confirmed by running it:
   `name, display_name, type, severity, status, first_seen, last_seen, additional_fields,
   description, mitigation`.
4. `composeExposureContent(row)` therefore canonicalises the carried object into the content string.
5. The exposure row id is `deriveRowId(instanceId, exposureKey(parentKey, cveId, content))`.

Measured on the real lane rather than reasoned about. Adding one mapping entry that reads the
previously-unclaimed top-level field `confidence`:

```
carried keys BEFORE: aid,cid,confidence,data_providers,id,vulnerability_metadata_id
carried keys AFTER : aid,cid,data_providers,id,vulnerability_metadata_id
exposure ids changed: 2000 of 2000
anything thrown or counted about it: no
```

**What warns about it: nothing.** No throw, no `MappingError`, no counter, no stat, no log line. The
run succeeds, the row count is unchanged, and every id in the lane is new. The only artifacts that
admit the hazard are prose:

- the comment block on `carryFields` in `src/mapping/engine.ts`, which states it in capitals and says
  outright that there is no error path;
- the doc on `AdditionalFieldsRule` in the frozen `src/mapping/spec.ts`.

There is no runtime signal, and I did not invent one. A warning would need a stored fingerprint of
the previous run's claimed-field set to compare against, which is state this stage does not have and
which the streaming constraint does not want. The right place to pin it is the R3 test, and the code
now at least admits it.

**One correction to how R3 is described in `orchestration_plan.md`.** The hazard is **exposure-lane
only**. An asset row id is `deriveRowId(instanceId, parentKey)` and has no content term at all, so
claiming a field changes an asset row's `additional_fields` and leaves its `id` byte-identical.
Measured: all 298 asset ids unchanged across the claiming and non-claiming specs. The plan's
wording ("re-mints every id in the lane") is right for exposures and wrong for assets.

---

## Three design points the team lead adopted

### 1. `mapLane` is not an async generator, and must not become one

An async generator's body does not execute until the first `next()`. Validation written inside one
would run *after* the caller had already begun iterating, which makes the contract sentence — "the
engine verifies them against the lane manifest at load time and fails before reading any record" —
false at the call site while looking entirely correct in review.

So `mapLane` is a plain function. It calls `planMapping` (which calls `assertMappingUsable`) eagerly,
then returns the generator produced by the private `emitRows`:

```ts
export function mapLane(records, options, stats) {
  const plan = planMapping(options);          // throws HERE, at the call
  return emitRows(records, plan, options, stats);
}
```

This is the kind of thing a later reader "simplifies" into `export async function* mapLane(...)`,
because the two look equivalent and the tests still pass — the throw just moves from the call to the
first `for await`. If the caller never iterates, nothing is validated at all. Do not merge that
change.

### 2. `validate.ts` is delegation plus exactly three additions

`src/mapping/spec.ts` was frozen in phase 0 and already performs the mapping-vs-manifest field
verification, the load-time enum literal check, and the column-coverage discipline (via
`assertColumnCoverage` in `src/target/contract.ts`). None of that is reimplemented.
`assertMappingUsable` calls `assertMappingSpec` then `assertMappingLoadable`. A second
implementation would be a second authority on the same question, and the two would disagree the
first time either was edited.

The three additions:

**(a) Column attribution on an absent field.** `assertMappingLoadable` names the offending field and
the lane but not the target column that wanted it. On a thirty-column mapping that leaves the author
searching. The new pass runs the frozen `collectFieldReads` against a one-column view of the spec
(`{ ...spec, columns: [mapping] }`) so the message names the column, then hands over to the frozen
check, which remains the authority. Implemented by reusing the frozen helper deliberately: a second
path walker would be a second answer to "what does this mapping read", and the whole checkability
property rests on there being one.

**(b) `decide` source names — a real bug class, invisible in review.** `assertMappingSpec` resolves
every value path, so a malformed path fails at load. It never checks that a `decide` rule's
`source`, or an entry in its `require` list, names one of that same `decide`'s own declared sources.
A misspelled source name is not a type error and not a path error. The condition simply can never
hold, so the rule **silently never fires** and the column takes the fallback or nothing.

This matters because `os_type` is the only column that needs `decide` today, and it is one of the two
enum columns whose value decides whether a row survives downstream. A rule that never fires there is
expensive and looks like correct mapping data. Verified:

```
Mapping 'harness-assets' column 'os_type' has a decision referring to source 'platfrom',
which it does not declare. Declared sources: platform, version.
```

**(c) Per-record enum membership.** Load time can only see literals, which `spec.ts` says itself. A
`recordField` routed straight into an enum column is legitimate — Tenable already states `high` and
`critical` — and whether record 200,000 states something else is knowable only per record.

### 3. Vendor-knowledge grep: method and result

The brief asked for a grep. The method used is stricter than a hand-picked keyword list, because a
hand-picked list can only catch what the author thought to include.

**Method.** Take the union of every field name in all three manifests (`fields.record` plus
`fields.parent` across `falcon-assets.json`, `falcon-findings.json`, `tenable-findings.json`) —
**192 names**. Subtract the names that are also target column names, since those are contract
vocabulary rather than vendor vocabulary — **10 names**: `cloud_account_id`, `created_at`,
`first_seen`, `fqdn`, `id`, `last_seen`, `os_version`, `severity`, `status`, `tags`. Word-boundary
match the remaining **182** against the file.

**Result.**

```
$ grep -niE 'falcon|crowdstrike|tenable|nessus|spotlight|qualys|rapid7' src/mapping/engine.ts
13: * (`ThinFalconCollector/Labels/FindingLabeler.cs:18-31` sets each target column from a hard-coded
19: * the largest single line, as measured - the 474 MB Falcon lane completes under

  vendor field names across the 3 manifests: 192
  also target column names (excluded from the grep): 10
  purely-vendor names searched: 182
  HITS in src/mapping/engine.ts:   source, sources, state
  HITS in src/mapping/validate.ts: source, sources, state

$ grep -niE 'cve' src/mapping/engine.ts
306:      stats.withoutCve += 1;

$ grep -nE 'spec\.vendor|spec\.entity|manifest\.vendor|manifest\.entity' src/mapping/engine.ts
178:  const table = tableFor(spec.entity);
```

Every hit accounted for, none of them vendor knowledge:

- **`source`, `sources`** are `ValueSpec`'s own frozen property names (`value.sources`,
  `condition.source`). **`state`** is the English word, in "no module state". All three collide
  coincidentally with Tenable field names. Spec vocabulary, not vendor vocabulary.
- **`Falcon` / `ThinFalconCollector`: two occurrences, both comment citations, at lines 13 and 19.**
  Line 13 cites the C# prototype file this engine replaces; line 19 cites the measured 474 MB
  baseline. Neither is code. The precedent is `src/reader.ts:164` — the file whose vendor-neutrality
  is the established baseline claim — which names the same lane in a comment for the same reason, and
  the repo convention (recon "Patterns to mirror" item 2) requires comments to carry measured
  evidence with citations. Flagged as a judgement call: if "no vendor name anywhere" is to be read
  literally, it is a two-line edit at the cost of two citations.
- **`withoutCve`: one occurrence, a counter name.** `cve_id` is a column of
  `parser_output_exposures_enrich`, so this is target vocabulary. Neither vendor's spelling appears
  anywhere in the file — Falcon states `cve.id`, Tenable states `plugin.cve`, and no path literal
  exists in `engine.ts` at all.
- **`tableFor(spec.entity)`** is a target-schema lookup, not a vendor branch. Adding a vendor does
  not add an entity. The only `switch` in the file is `switch (value.kind)`, the `ValueSpec` dispatch.

**Proof by construction, which is stronger than the grep.** `engine.ts` was written and finished
before any Tenable mapping existed. The Tenable lane then ran with new mapping data only and **zero
engine edits**: 1,521 records to 4,672 rows.

---

## Zero-CVE finding: no row, counted in `stats.withoutCve`

`cve_id` is `text NOT NULL` on the exposures table, so a finding naming no CVE cannot become a row.

**Decision: it produces no row and increments `stats.withoutCve`.** Not fatal — the record is
well-formed and the lane is not at fault, so stopping the run would be wrong. Not silent — a lane
that loses findings this way must be able to say so. This follows the C#
(`ExposureRowMapper.cs:24-27`, `:140-143`, `ExposureMapping.WithoutCve()`).

The CVE list is also de-duplicated within a record, case-sensitively, because a repeat inside one
record derives the same id twice and collides on the target's primary key
(`ExposureRowMapper.cs:189-192`).

Measured:

| Lane | Records | `withoutCve` | Share |
|---|--:|--:|--:|
| `falcon-findings-big.json` | 246,212 | 83 | 0.03% |
| `falcon-prod-findings.json` | 25,570 | 5 | 0.02% |
| `tenable-findings.json` | 1,521 | **629** | **41%** |

**Tenable's 41% needs a decision that is not this worker's to make.** 629 of 1,521 Tenable findings
carry no `plugin.cve` — plugin findings that are not CVE-backed. They leave the exposure lane by
construction. The loss is counted, but a 41% loss is a product question, not a mapping detail. Raise
it before the slice is called done.

---

## Three-lane run results

All runs under `--max-old-space-size=64`. `--experimental-strip-types` throughout.

| Run | Result |
|---|---|
| reader alone (reference, re-measured this session) | 246,212 records, 474 MB, 1.2 s, **409 MB/s**, 212,219 rec/s, peakRSS 156 MB |
| `falcon-findings-big.json`, reader + mapping | 246,212 records → **246,129 rows**, 83 withoutCve, 0 rejected, 3.0 s, 81,212 rec/s, **156 MB/s**, peakRSS 183 MB |
| same, carry removed (cost isolation) | 2.7 s, 92,820 rec/s, 179 MB/s |
| `falcon-assets.json` | 298 records → 298 rows, 0 rejected, peakRSS 120 MB |
| `tenable-findings.json` | 1,521 records → **4,672 rows** (CVE explode), 629 withoutCve, 0 rejected, peakRSS 186 MB |
| `falcon-prod-findings.json` | 25,570 records → 25,565 rows, 5 withoutCve, 0 rejected, peakRSS 176 MB |

**Throughput, reported as a finding rather than as noise.** The reader is unmodified and still
measures 409 MB/s, matching the C1 baseline. Adding the mapping stage costs **2.6x: 409 → 156 MB/s**.
That is the stage's own work — value evaluation, coercion to Postgres types, canonical JSON over the
carried fields, and one SHA-1 per row — not a regression in the reader. Isolating the
`additional_fields` carry and its canonicalisation puts it at roughly 13% of mapping time
(156 → 179 MB/s with the carry removed), so the remaining cost is spread across evaluation, coercion
and id derivation rather than concentrated in one place.

**Bounded memory holds.** All four lanes complete under a 64 MB heap cap. Peak RSS of 176-186 MB is
process RSS — the capped heap plus the V8 baseline plus readline's buffers — against the reader's own
156 MB on the same lane, so mapping adds roughly 27 MB and does not grow with the lane. Nothing
accumulates across records and no lookup table is built.

### Tripwire evidence produced

These are production-behaviour runs, not tests. The tests belong to W4 (evidence-tests).

- **R4 (mapping-declares-absent-field).** A mapping binding `display_name` to
  `$.apps[0].product_name_version` is rejected at load, before any record is read:
  `Mapping 'harness-findings' binds column 'display_name' to record field 'apps', which lane
  manifest 'findings' does not carry.` The R4 test should assert the **column-attributed** message,
  since that is the addition this worker made.
- **R1 (enum-nonmember-silent-drop).** A mapping routing raw `platform_name` into `os_type` rejects
  50 of 298 records, tallied by column and value as `os_type: Windows=37 Linux=12 Mac=1`,
  non-fatal, and `rows + rejectedRecords == records` exactly (248 + 50 = 298). `windows-server` and
  `active-directory` are both accepted in their `@map` spelling; `windows_server` is rejected at
  load time by the frozen literal check.
- **R3 (additional-fields-claims-shift-ids).** Measured above. Exposure lane only.
- **R5 (created-at-in-content-hash).** Two runs stamped at different wall-clock times derive
  identical ids. `planMapping` throws unconditionally if `CONTENT_COLUMNS` ever contains
  `created_at`, so the exclusion is enforced in code and not only in the content module's data.
- **Criterion 4.** 92,251 of 246,129 Falcon exposure rows repeat an `(asset_id, cve_id)` pair, and
  **0** ids collide across all 246,129 rows. The prototype measured 37,750 of 122,310 on a different
  batch; this data gives 92,251 of 246,129.
- **Criterion 7.** All twelve fingerprint columns present on every exposure row. `asset_id` is
  derived from `parentKey` before the content is composed, and sits positionally ahead of the content
  inside the version-5 name via `exposureKey`, so no asset-lane lookup is needed and the stage stays
  streaming.

### The load-time check caught a real error in this worker's own harness

Writing the Tenable spec, I bound `display_name` to `$.fqdn`. It failed at load:

```
Mapping 'harness-tenable' binds column 'display_name' to parent field 'fqdn', which lane
manifest 'findings' does not carry. The lane's parent fields are: ... fqdns ...
```

The lane states `fqdns`, plural. This is the central design bet of the task paying off against an
unforced, ordinary error rather than a contrived negative case — worth recording alongside the
deliberate `apps` case, because the deliberate case only proves the check runs.

---

## Decisions the brief did not settle

Each is reversible in one place. Each is stated in a comment at that place.

1. **An epoch number into a `timestamp(3)` column is fatal, not coerced.** Epoch seconds and epoch
   milliseconds are indistinguishable from the value alone, and guessing wrong puts the row in 1970
   or in the year 55000. Grounded in the manifests: all 25 timestamp-shaped fields across the three
   lanes declare `kinds: ['string']`, so nothing needs it today. A lane that does need it should get
   a `ValueSpec` kind, not a guess in the coercion.
2. **A numeric string into `double precision` is fatal, not parsed.** Blanket "strings that look
   numeric are coerced" is the kind of silent leniency this repo avoids.
3. **`text[]` columns always emit a list, never null.** The column default is the empty array, and
   NULL and `{}` are different strings inside the boundary's `md5(ROW(...))` fingerprint — so two
   lanes that both state nothing must not fingerprint differently over which one said it with a
   null.
4. **Strings are trimmed on the way out.** Matches `asKey` in `src/reader.ts`, so a value and the
   parent key derived from it stay identical. Note that this does affect the content hash.
5. **The carry is over the record only, never the parent.** On a correlated lane the parent is one
   object shared by every record on the line, so carrying it would repeat ~77 host fields into every
   finding and put host facts inside the exposure's own content hash.
6. **A `NOT NULL` column left to the column default throws at plan time.** The engine will not parse
   a Postgres default expression such as `'{}'::text` into a value; that would mean interpreting DDL,
   and `tags` is exactly what the `postgresTextArray` value kind exists to state. Verified:
   `Mapping 'harness-assets' leaves integration.parser_output_assets_enrich.tags to the column
   default, but the column is NOT NULL (default '{}'::text).`
7. **Content is composed once per record, not once per exploded value**, because `CONTENT_COLUMNS`
   does not include `cve_id`. `planMapping` checks that at load and recomposes per value
   automatically if it ever changes. This is a throughput decision on a 246,212-record lane, not a
   correctness one.
8. **Rejection samples are capped at 25; the tally stays exact.** Unbounded retention would make
   memory grow with the reject count, contradicting the bounded-memory constraint.
9. **`text` columns reject objects and arrays.** `String({})` is `'[object Object]'`, which is a
   wrong value rather than a missing one.
10. **Postgres array literal elements are quoted when they contain `{`, `}`, `,`, `"`, `\` or
    whitespace, are empty, or spell `null`.** A single tag `a,b` written bare would read back as two
    tags. Verified: `{plain,"has,comma","has \"quote\"","NULL"}`.
11. **The instance id's shape is not re-checked in the engine.** `deriveRowId` enforces it on the
    first row, and it is the single id-derivation path. A regex here would be a second statement of
    the same rule, which is the thing that constraint exists to prevent. Cost: the shape failure
    surfaces on the first row rather than at load.
12. **Enum nulls pass.** The enrich SQL carries an explicit `os_type IS NULL OR` and no such clause
    for `type`, but that filter runs on `integration.parser_output_assets`, one table earlier than
    this stage's target, and on `*_enrich` both columns are declared nullable. So a null is passed
    through and it is the mapping's job to supply a value; only a non-null non-member is rejected.
    The asymmetry is written out in the `validate.ts` module comment.

---

## Two findings for the `mappings/` worker

1. **`display_name` for Falcon does not need `apps`.** `parentField $.hostname` resolves off the
   correlated envelope's host object and works on all 246,212 findings. Recon L1's option 2 — drop
   `display_name` and lose a fingerprint column — is not necessary.
2. **`platform_name` is absent on 248 of 298 Falcon Discover hosts.** An `os_type` `decide` with
   `require: ['platform']` therefore yields null for 83% of the asset lane. That is correct per the
   spec's own reasoning — an absent OS is not the `other` member, and calling it `other` would make
   an unmanaged entity indistinguishable from an odd one — but the resulting distribution is worth
   knowing before writing the mapping data. Measured with the harness spec:
   `windows=31 windows-server=6 ubuntu=7 linux=5 macos=1 null=248`.
