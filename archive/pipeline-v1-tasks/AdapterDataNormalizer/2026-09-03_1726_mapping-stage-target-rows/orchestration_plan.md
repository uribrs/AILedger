# Orchestration Plan

## Problem Classification

- Contract sufficiency: sufficient
- Classes (maximum 3):
  - `exposure-identity` — the slice's hardest requirement is composing an exposure identity that
    reproduces EA's twelve-column content fingerprint rather than a `(host, CVE)` pair.
  - `entity-ids` — instance-scoped uuidv5 derivation with exactly one code path across both lanes.
  - `schema-currency` — the target column set and enum members come from checked-in artifacts whose
    currency is itself in question. Recon L5 confirmed the dumps are ~3 weeks stale.
- Additional classified prior art: A16 (`asset-key-uniqueness`), A17 (`canonical-array-order`) —
  appended to `assumptions.md` under `## Classified Prior Art`. The `cross-repo-contract` matches
  were dropped: they concern ISB wire-message package drift and this slice ships no package.
- Recon correction: classification **confirmed**, with one refinement. `schema-currency` turned out
  to be load-bearing rather than hygiene: it flipped the target-shape source from the pg_catalog
  dumps to Prisma (see `decisions.md`, drift section). Recon also refuted the premise that the
  mapping engine could be ported — the C# `AssetRowMapper` / `ExposureRowMapper` hold no vendor
  knowledge at all, because the vendor knowledge lives one stage upstream in the collector as
  hand-written C# (`ThinFalconCollector/Labels/FindingLabeler.cs:18-31`). Mapping-as-data is new
  design work, not a port. That raises Execution risk and is why a Stop Condition already covers it.
- New or changed artifacts:
  - `mapping spec (per-vendor data file)` → the mapping engine's load-time validator → verifies every
    declared field read against `LaneManifest.fields` and throws before any record is read
  - `enum member sets (generated, @map values)` → enum validation inside the mapping engine → turns
    what is a silent row drop downstream into a counted rejection before emit
  - `AssetRow` / `ExposureRow` → the new `map` CLI's NDJSON writer → serialized to disk; this slice
    has no Postgres consumer
  - `derived row id (uuid5)` → exposure content composition → `asset_id` sits positionally *before*
    the content inside the v5 name, so the exposure lane needs no asset-lane lookup and stays
    streaming
  - `additional_fields` carry rule (unclaimed vendor fields merged in) → the exposure content hash →
    **design-invalidating interaction.** `additional_fields` is one of EA's twelve fingerprint
    columns, so which fields a mapping leaves unclaimed changes the content, and therefore changes
    every derived id in the lane. Adding one mapping entry silently re-mints ids. See R3.

### Attention Items

| id | name | failure mode | causal path and impact | planned handling | source |
|---|---|---|---|---|---|
| R1 | enum-nonmember-silent-drop | A value outside an enum is accepted at map time and lost later | `parser_output_assets` columns are `text`, and the enrich step filters with `f.type = ANY(enum_range(...)::text[])` — a non-member row is dropped unremarked, not rejected. At `*_enrich` the columns are enum-typed so it becomes an INSERT error instead. Either way an unvalidated value is invisible at map time | `test:tests/enum-validation.test.ts::rejects a non-member and reports a counted rejection` | recon L9; `migrations/20260729120100_add_cloud_columns_to_parser_output_assets/migration.sql` Type-choices comment; `parsed-data.repository.ts:340-341` |
| R2 | created-at-in-content-hash | `created_at` reaches the content hash and ids change on every run | `created_at` falls back to the wall clock when a record labels none (`ExposureRowMapper.cs:186`), so including it re-derives every id per normalization and breaks reparse stability silently — nothing fails, the ids simply differ | `test:tests/identity.test.ts::two runs separated in wall-clock time derive identical ids` | `ExposureRowMapper.cs:93-98` ("the load-bearing exclusion"); contract criterion 5 |
| R3 | additional-fields-claims-shift-ids | Adding or removing one mapping entry re-mints every id in the lane | Unclaimed vendor fields are carried into `additional_fields`; `additional_fields` is one of EA's twelve fingerprint columns; the content hash feeds the v5 name. So a mapping edit that claims a previously-unclaimed field changes the content and therefore the id, with no error anywhere | `test:tests/identity.test.ts::claiming a previously-unclaimed field changes the derived id, and the change is visible` | `LaneBinding` carry rule at `LabelBinding.cs:63-84`; fingerprint at `parsed-data.repository.ts:70-76` |
| R4 | mapping-declares-absent-field | A mapping reads a vendor field the lane does not contain, and emits nulls instead of failing | `apps` is absent from `manifests/falcon-findings.json` **and** from the data — 0 of 246,212 findings carry it, verified this session. The C# labeler sourced `display_name` from `finding.apps[0].product_name_version`, so the obvious Falcon mapping declares a read that cannot resolve. Without load-time verification this lands as a column of nulls | `test:tests/mapping-validate.test.ts::rejects a mapping declaring a read of apps against the real Falcon manifest` | recon L1 as corrected in C2; `FindingLabeler.cs:18-31` |
| R5 | canonical-order-and-number-literals | Canonicalization is not deterministic across runs, or diverges from the ported rules | Object key order must be sorted ordinally or the same content hashes differently. Array order is significant and must be preserved. `JSON.stringify` normalizes `1.00` to `1`, which diverges from the C# rule that literals are written as they arrived. The Falcon data does not exercise any of this — 0 multi-element array fields in 52,000 sampled findings — so vendor data cannot catch it | `test:tests/canonical-json.test.ts::ordinal key sort, array order preserved, number-literal handling pinned` | A17 / `lessons.md#L-c9239f9c`; recon G4 CanonicalJson rules 1-4 and the `JSON.stringify` hazard |

### Research Questions

No research needed — every open question was about Cymulate's own repositories on this disk and was
resolved by internal recon plus the four orchestrator verifications recorded in
`research/internal-recon.md` under "Orchestrator corrections". No third-party vendor behavior is in
scope for this slice.

## Complexity Decision
- Path: decompose
- Axis scores: Complexity high | Separability high | Coupling low | Dependency order medium | Execution risk medium | Worker clarity high
- Rationale: Two hard triggers fire. Recon named three genuinely disjoint file sets downstream of a
  single blocking stage-0 surface, which is proof of low coupling rather than an estimate of it. And
  tests plus implementation are both in scope and separable, so tests go to an owner that may not
  write `src/` — the same isolation device as the blind code review. Worker clarity is high only
  because recon returned exact column sets, enum members, conventions with citations, and the file
  sets themselves; it would have been low had the path been chosen before recon.

## Research Decisions
- External research: none needed. Every question was internal to repositories on this disk.
- Internal recon: complete → `research/internal-recon.md`, plus an appended
  "Orchestrator corrections" section recording four recon errors (C1 the baseline does reproduce at
  409 MB/s; C2 `apps` is absent from the data too, which strengthens R4; C3 the Tenable manifest does
  match the scratchpad lane; C4 the provenance commit is `cybi-db-models` at `3d26e24e`, not the
  outer EA repo) and one confirmation that changed a decision (C5 the dumps are stale, so Prisma is
  the column-set source).

## File Ownership
- Disjoint sets found: 3, downstream of one blocking stage-0 surface.
- W0 (freeze-target-contract-and-mapping-schema) owns: `src/target/contract.generated.ts`,
  `src/target/contract.ts`, `src/target/generate-contract.ts`, `src/ids/rowId.ts`,
  `src/mapping/spec.ts`
- W1 (identity-canonical-content) owns: `src/rows/canonicalJson.ts`, `src/rows/exposureContent.ts`
- W2 (mapping-engine-and-enum-validation) owns: `src/mapping/engine.ts`, `src/mapping/validate.ts`
- W3 (vendor-mapping-data-and-map-cli) owns: `mappings/*.json`, `src/cli/map.ts`
- W4 (evidence-tests) owns: `tests/**` and nothing else. May not write under `src/` or `mappings/`.
- Main thread owns, last: `README.md`, `package.json`, `execution_notes.md`.
- Shared surface frozen in phase 0: the generated target column + enum contract with provenance;
  `AssetRow` / `ExposureRow` types derived from it; `NAMESPACE`
  (`7f6b1f26-9a3e-5d41-8c0b-2a5f4d9e1b73`) and `KEY_SEPARATOR` (`U+001F`); `uuid5` and
  `deriveRowId`; the mapping-spec type that W2, W3 and W4 all bind to.

## Worker Plan
- W0 (freeze-target-contract-and-mapping-schema) — scope: generate the target contract from Prisma
  plus the dumps, write the row types, port the id derivation, and design the mapping-spec type
  output: the frozen surface above  phase: 0  continuity: fresh
- W1 (identity-canonical-content) — scope: canonical JSON and exposure content composition
  owns: `src/rows/`  inputs: W0.output  phase: 1  continuity: fresh
- W2 (mapping-engine-and-enum-validation) — scope: the generic engine that applies a mapping spec,
  its load-time field verification, and enum validation  owns: `src/mapping/engine.ts`,
  `src/mapping/validate.ts`  inputs: W0.output  phase: 1  continuity: fresh
- W3 (vendor-mapping-data-and-map-cli) — scope: Falcon asset, Falcon findings and Tenable findings
  mapping data, plus the `map` CLI and its counters  owns: `mappings/`, `src/cli/map.ts`
  inputs: W0.output, W1.output, W2.output  phase: 2  continuity: fresh
- W4 (evidence-tests) — scope: deliver R1-R5 as tests, plus the demonstration evidence for contract
  criteria 1-7  owns: `tests/`  inputs: everything landed  phase: 3  continuity: fresh

## Synthesis Approach
Phase boundaries are file-set boundaries, so synthesis is integration checking rather than merging:
after each phase, confirm the phase's files exist, type-check under `tsc --noEmit`, and that no
worker wrote outside its set (`git status --short` against the ownership table). W3 is the first
point at which the whole chain runs end to end on real data; the measured numbers from that run are
what criteria 8 reports. The main thread writes `README.md` last, once the numbers exist.

## Verification Obligations
- Cross-check against every one of the ten Success Criteria in `prompt_contract.md`.
- Criteria 1-7 are demonstration-not-assertion. The verifier must confirm evidence was produced,
  not that code exists which would produce it.
- Criterion 8's reference is the re-verified 409 MB/s / 474 MB / 246,212 records baseline recorded in
  C1, not the README's original 405 MB/s figure.
- Confirm the zero-engine-edit claim for Tenable by file diff, not by assertion.
- Dispose of all 17 assumptions (A1-A17) and all five attention items (R1-R5).
- Record decision drift for the flips already logged in `decisions.md` (Prisma over dumps; prod-eu
  superseded by the Prisma superset; table name corrected to plural; mapping-as-data is new design).

## Amendments during execution

### Typecheck method — adopted from phase 0
The repo must stay dependency-free, and no TypeScript compiler is installed (`npx tsc` resolves to
an unrelated abandoned package, not the compiler). Phase 0 solved this out-of-tree, and it is now
the verification method for the whole task and for the verifier pass:

```
S=<scratchpad>/typecheck            # npm install typescript @types/node, once
$S/node_modules/.bin/tsc --noEmit --typeRoots $S/node_modules/@types --types node
```

Run from the repo root against the repo's own `tsconfig.json`. Nothing is installed into the repo;
`package.json` and the absence of `node_modules` were both re-confirmed after running it.

### Ownership extended — `src/cli/describe.ts`
Phase 0 found one pre-existing type error, `src/cli/describe.ts:21`: `Args` was
`Readonly<Record<string, string>> & { readonly _: readonly string[] }`, which is unsatisfiable
because `_` cannot be both a string and a string array. It compiled only because nothing had ever
type-checked the file — it dates from the reader stage, before this task.

The main thread took it rather than handing a pre-existing defect to a worker: `Args` is now two
fields (`named`, `positional`). Both CLIs were re-run afterwards and still work
(`describe` reproduces the 298-row asset manifest with 99 record fields; `probe` still reports
`$.aid=49 $.id=249`). Typecheck is now clean across every file not currently held by a live worker.

Main thread therefore owns: `README.md`, `package.json`, `execution_notes.md`, and
`src/cli/describe.ts`. W3 still owns `src/cli/map.ts` only.

### Operator constraints added mid-execution
Recorded in `constraints.md`. The material one for phase 2 and 3: **do not run the TenableIo
collector** — it is large and a fresh run is expensive. Tenable coverage rests on the 13 MB
`tenableio-two-phase/first-001` sample already on disk. The prohibited set is now TenableIoCollector,
FalconCollector, IsbLoadTestCollector, DummyCollector. Also: stability over completeness — record
gaps rather than widening scope to close them.

### R3 corrected — the hazard is exposure-lane only

`orchestration_plan.md`'s Attention Items row for R3 says claiming a previously-unclaimed field
"re-mints every id in the lane". That is right for the exposure lane and **wrong for the asset
lane**, and W2 measured the difference rather than reasoning about it.

An asset row id is `deriveRowId(instanceId, parentKey)` — there is no content term in the name at
all. So claiming a field changes that asset row's `additional_fields` value while leaving its `id`
byte-identical. Measured: all 298 Falcon asset ids unchanged across a claiming and a non-claiming
spec.

An exposure row id is `deriveRowId(instanceId, exposureKey(parentKey, cveId, content))`, and
`additional_fields` is one of the ten `CONTENT_COLUMNS`, so there the hazard is real and total.

R3's test must therefore assert the exposure behaviour AND the asset non-behaviour. The second half
is the more useful assertion, because it is the one a reader would get wrong from the plan's
original wording.

Related subtlety worth recording: `plan.claimedFields` is `collectFieldReads(spec).record`, which is
**top-level names only**. A mapping that reads `$.cve.description` therefore claims all of `cve`,
not just that leaf. Partial claims of a nested object are not expressible, so an unclaimed sibling
leaf cannot be carried into `additional_fields` while its parent is claimed.

And nothing warns about any of this. W2's answer to the direct question was "Yes, it is true. No,
nothing warns" — detecting it needs the previous run's claimed-field set to compare against, which
is state this stage does not hold and which the streaming constraint does not want. The code now
admits it in a comment; the R3 test is what pins it.
