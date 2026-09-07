# Verifier 1 — Mapping stage: manifest-described records to target-shaped rows

Verifier: independent pass, 2026-09-03. Method: ran the artifacts, did not read-and-agree.

## No diff was possible, and this is not a caveat I can work around

`state.json.baseRef` is `null`. Confirmed directly:

```
$ git log --oneline -5
fatal: your current branch 'main' does not have any commits yet
$ git status --short | wc -l
9          # all nine entries are '??' — every path is untracked
```

The repository has **zero commits**. There is no base ref, no tree object, and no prior version of
any file. I therefore **cannot** diff the delivered state against anything, and I make no claim
that rests on one.

Two consequences the operator should hold on to:

- **Criterion 1's "zero engine edits" cannot be verified by file diff**, which is what
  `orchestration_plan.md` § Verification Obligations asked for ("by file diff, not by assertion").
  The available substitutes are mtime ordering and a structural audit of the engine. I ran both and
  report them below, but they are weaker instruments than the plan assumed.
- **Nothing is protected.** A single `rm` loses the whole slice. This is the most immediately
  actionable gap in the deliverable and it is not on any list in the task directory.

Scope of comparison: the artifacts named in `execution_notes.md`, and what I could run myself.

## Verdict

The work satisfies the original request. Nine of ten Success Criteria are demonstrated with
evidence I reproduced myself; the tenth (criterion 9, README) is met — the main thread did land it,
at 19:42, after every other file. The deliverable is immediately usable.

Two substantive findings the pipeline did not produce, both about identity stability:

1. **A17 is REJECTED, and the plan's stated reason for parking it is factually wrong.**
   99–100% of exposure rows on all three lanes carry a multi-element array inside the content hash.
   Reordering two elements re-mints the id. The plan's R5 row says "0 multi-element array fields in
   52,000 sampled findings"; that count missed the nested arrays that `wholeRecord` carry puts into
   `additional_fields`.
2. **11 of 298 Falcon asset rows collide on `(value, type)`** — the key `constraints.md` names as
   EA's demotion key. Row ids are distinct; the downstream identity key is not. Unrecorded anywhere.

## What I re-ran, and whether the reported numbers held

| Measurement | `execution_notes.md` | My run | Verdict |
|---|---|---|---|
| Tests | 107 pass / 0 fail | **107 pass / 0 fail** | reproduced |
| Two simultaneous `npm test` | both 107/107 | **both 107/107** | reproduced |
| Typecheck (out-of-tree) | clean | **clean, exit 0** | reproduced |
| 474 MB lane under `--max-old-space-size=64` | completes | **completes, 5.4s** | reproduced |
| 474 MB lane records / rows | 246,212 / 246,129 | **246,212 / 246,129** | exact |
| 474 MB lane `withoutCve` | 83 | **83** | exact |
| Lab pair audit | 153,878 pairs, 92,251 beyond first, 0 collisions | **153,878 / 92,251 / 0** | exact |
| Prod pair audit | 17,486 pairs, 8,079 beyond first, 0 collisions | **17,486 / 8,079 / 0** | exact |
| Tenable fan-out | 3.072, 629 without CVE | **3.072, 629** | exact |
| Same `--instance-id`, different wall clock | identical digest | **identical** (`1ab5be0d…` twice) | reproduced |
| Different `--instance-id` | different digest | **different** (`88d8d63a…`) | reproduced |
| Enum rejects, all four real lanes | none | **none on all four** | reproduced |
| Peak RSS, 474 MB under cap | 191 MB | 183 MB | within noise |
| Peak RSS, `--pair-audit` on 474 MB | 386 MB | **484 MB** | **25% higher than reported** |
| Reader-alone baseline | 409 MB/s (C1) | **385 MB/s** | 6% below; see below |
| Full-chain MB/s, 474 MB lane | 95 | 88–92 | within noise |

Two figures did not reproduce and should be corrected in `README.md`:

- **`--pair-audit` peak RSS is 484 MB, not 386 MB**, on the 474 MB lane. The claim it supports —
  that `--pair-audit` does not stream — holds and is if anything stronger.
- **The reader-alone baseline measured 385 MB/s for me**, against C1's re-verified 409 MB/s and the
  README's original 405 MB/s. Record counts and drift matched exactly (246,212, zero drift), so this
  is run-to-run variance, not a regression. But C1 stated 409 as settled fact and criterion 8's
  comparison rests on it. The honest statement is "roughly 385–409 MB/s across runs".

I did not reproduce a clean count-only run of the 42 MB prod lane; both of my prod-lane runs carried
`--pair-audit` or `--out`. Its reported 76 MB/s is therefore unchecked by me.

## REQUIRED SECTION 1 — Assumption Disposition

| id | status | name | citation | actor |
|----|--------|------|----------|-------|
| A1 | NEVER-TESTED | ea-generation-model | — nothing in this slice reaches Exposure Analytics; no `latest = false` demotion was exercised. See Finding V2: I measured 11 asset rows sharing `(value, type)`, which is adjacent evidence that the model will misbehave, but A1 itself is untested | verifier |
| A2 | VALIDATED | exposure-identity-is-content-md5 | both halves. SQL read by me at `parsed-data.repository.ts:70-76` — `md5(ROW(asset_id, cve_id, type, severity, status, name, display_name, description, mitigation, first_seen, last_seen, additional_fields)::text)`, verbatim. Magnitude re-measured by me on two independent batches: `--pair-audit` gave 92,251/246,129 (37.5%) lab and 8,079/25,565 (31.6%) prod. The prototype's single-tenant weakness is now retired | verifier |
| A3 | REJECTED | committed-dumps-current-enough | contradicted. I checked `database-schemas-up-to-date/prod-eu/integration/parser_output_assets_enrich.md`: `sub_type`, `cloud_platform`, `region`, `cloud_account_id`, `cloud_account_name`, `cloud_provider_url`, `batch_id` all return grep count 0 in the dump and ≥1 in `prisma/schema/integration.parser-output-assets-enrich.prisma`. **Do not re-assume the dumps carry a current column set.** They remain usable for `timestamp(3)` precision only | verifier |
| A4 | NEVER-TESTED | jsonb-not-double-encoded | no Postgres write is in scope, so nothing observed a column. The emitted NDJSON carries `additional_fields` as a JSON object, not a string, which is necessary but not sufficient | verifier |
| A5 | NEVER-TESTED | column-width-for-89-char-key | outside this repo; no column was written. Partially defused for this data: I measured the longest asset parent key at **89 chars** (`8884df8d…qt3F`), but the longest emitted `value` at **27 chars**, because `value` is `firstNonBlank(hostname, fqdn, current_local_ip, parentKey)` and the fallback never won. So no 89-char string reaches a column in this batch. The width itself is still unverified | verifier |
| A6 | NEVER-TESTED | downstream-match-key | outside this repo and outside this slice; nothing downstream consumed a row | verifier |
| A7 | NEVER-TESTED | falcon-orphan-findings | orphan tolerance was not measured. `tests/end-to-end.test.ts:132` shows the two lanes agree on asset identity by construction, which is a different claim | verifier |
| A8 | VALIDATED | tenable-second-vendor-fits | I ran the Tenable lane end to end: 1,521 records → 4,672 rows, fan-out 3.072, zero enum rejects, 4,672 distinct ids, 0 collisions. Scope note: one 13 MB batch, and the TenableIo collector is prohibited, so this is not parity at scale — as `execution_notes.md` already says | verifier |
| A9 | VALIDATED | ea-nested-enum-source-is-authoritative | I ran `git rev-parse HEAD` in `/Users/user/Dev/cymulate-exposure-analytics/cybi-db-models`: `3d26e24ee429e9d9a55b20912c7d8c525df6d34c`, Sun Aug 2 2026, "add cloud resource support (#456)" — exactly the pin in `CONTRACT_PROVENANCE`. C4's correction of recon was right. Scope note: still never checked against a live cluster, so "authoritative file" is validated, "matches the database" is not | verifier |
| A10 | VALIDATED | enum-member-counting-excludes-schema-directive | I counted per-enum members with `awk`/`grep -c` on `enum.prisma`: 23 / 15 / 12 / 5 / 3, matching the specified counts and `ENUM_MEMBERS` in `src/target/contract.generated.ts` | verifier |
| A11 | VALIDATED | map-value-is-the-emitted-string | `windows_server   @map("windows-server")` and `active_directory @map("active-directory")` read by me. **Citation correction: they are at `enum.prisma:80` and `:90`, not `:79` and `:89`** as `constraints.md` and A11 both state. Scope note: never exercised against a live Postgres cast; `windows-server` did appear in real emitted asset rows, `active-directory` did not | verifier |
| A12 | VALIDATED | declarative-mapping-expressive-enough | the central design bet, and it held. `ValueSpec` is a closed ten-member union with no `function` member and no expression string (`src/mapping/spec.ts`); all three real mappings load through the full gate (`tests/mapping-validate.test.ts:467`, passing) and I ran all four lanes to completion. The Stop Condition for an escape hatch did not fire | verifier |
| A13 | VALIDATED | streaming-survives-content-hashing | my own run: the 474 MB lane completed under `--max-old-space-size=64` in 5.4s at 88 MB/s, peak RSS 183 MB, 246,129 rows. The hashing step specifically — which A13 called unproven — is inside that path | verifier |
| A14 | VALIDATED | twelve-columns-and-the-ordering-constraint | I inspected a real emitted exposure row: 21 columns, and all twelve fingerprint columns present with real values. Ordering is handled explicitly — `CONTENT_COLUMNS` is exactly ten (`asset_id` and `cve_id` excluded), and `exposureKey(parentKey, cveId, content)` places the parent key positionally before the content, pinned by `tests/row-id.test.ts:91` and `:104`, both passing. Note the substitution: the name carries `parentKey`, not `asset_id`; they are equivalent given `instanceId`, since `asset_id = deriveRowId(instanceId, parentKey)` | verifier |
| A15 | REJECTED | column-sets-readable-from-dumps | superseded by the same evidence as A3. The column set now comes from Prisma. `CONTRACT_PROVENANCE.typeUnverifiedColumns` names the eight columns no dump could corroborate, which is the honest residue. **Do not re-assume a dump-sourced column set** | verifier |
| A16 | VALIDATED | asset-parent-key-uniqueness | measured by me through the real reader on `falcon-assets.json`: **298 parent keys, 298 distinct, max length 89**. The silent two-assets-into-one-row collapse A16 feared does not occur on this batch. Scope note: one batch, 298 assets | verifier |
| A17 | REJECTED | canonical-content-insensitive-to-array-order | **contradicted, twice over.** (a) The canonicalizer deliberately preserves array order (`src/rows/canonicalJson.ts:118`, citing `CanonicalJson.cs:63-74`), so content IS order-sensitive by design. (b) The plan's mitigating claim that no data exercises it is false: I measured multi-element arrays inside `additional_fields` on **99.8% of prod rows (25,514/25,565)**, **~99.2% of lab findings (1,984/2,000 sampled)** and **100% of Tenable rows (4,672/4,672)**. (c) I demonstrated the consequence: swapping two elements of `additional_fields.cve.vendor_advisory` on a real row changed the id from `c26dfb76-56fd-5e6f-94e4-5202bacd05da` to `c2b31556-442a-5f1c-8096-c5ac273e5065`. **Must not be re-assumed without new evidence: that vendor array ordering is stable across collections, or that Falcon/Tenable data does not exercise array order** | verifier |

Disposition totals: **VALIDATED 9, REJECTED 3, NEVER-TESTED 5.**

The five NEVER-TESTED (A1, A4, A5, A6, A7) all concern behaviour outside this repo — Exposure
Analytics' generation model, Postgres column types and widths, the downstream match key, and orphan
policy. None was in scope, and none is a defect in the slice. They are correctly still open.

## REQUIRED SECTION 2 — Attention Item Disposition

| id | final disposition | name | evidence |
|---|---|---|---|
| R1 | handled | enum-nonmember-silent-drop | the plan named `tests/enum-validation.test.ts::rejects a non-member and reports a counted rejection`. It exists at line 97 and passes — I confirmed it as `ok 17` in a full TAP run. Eleven R1 tests in total, including `windows-server`/`active-directory` accepted in `@map` spelling (line 182) and `windows_server`/`active_directory` rejected as Prisma identifiers (line 193). I separately re-read the downstream mechanism the item cites and it is real: `parsed-data.repository.ts:340-341` filters with `f.type = ANY(enum_range(NULL::cybi.asset_type)::text[])`. Residual, correctly recorded: real data produced **zero** rejections on all four lanes, which I reproduced, so the path is proven only by constructed tests |
| R2 | handled | created-at-in-content-hash | the plan named `tests/identity.test.ts::two runs separated in wall-clock time derive identical ids`. It exists at line 113 and passes (`ok 28`). I also verified it end to end rather than only in a test: one 474 MB run with `--normalized-at 2026-09-03T00:00:00.000Z` and a later run with `--normalized-at` defaulting to now both printed `idDigest=1ab5be0d4ebe4470d277a218e6aa82f1a10a612b1ad10db0a60c98a7705aa43a`. `created_at` is genuinely outside the hash. `CONTENT_COLUMNS` is ten names and `created_at` is not among them |
| R3 | handled | additional-fields-claims-shift-ids | **both halves of the mid-execution correction landed.** The exposure half is `tests/identity.test.ts:159` (`ok 32`); the asset non-behaviour is `tests/identity.test.ts:240` — `R3: an ASSET row id has no content term, so claiming a field leaves it BYTE-IDENTICAL` (`ok 35`). Fourteen R3 references in `tests/identity.test.ts`, including the top-level-claim subtlety at line 293 and `wholeRecord` immunity at line 208. The `orchestration_plan.md` § "R3 corrected" analysis is correct: the asset id has no content term, so the hazard is exposure-lane only |
| R4 | handled | mapping-declares-absent-field | the plan named `tests/mapping-validate.test.ts::rejects a mapping declaring a read of apps against the real Falcon manifest`. It exists at line 63 and passes (`ok 63`). It is stronger than the plan asked: the test injects a reader that throws `READER WAS ITERATED` if touched, and asserts the error message does **not** contain that string — so "fails before any record is read" is pinned at the call site, not merely at the throw. Five R4 tests, including the flat-lane parent-read case |
| R5 | handled | canonical-order-and-number-literals | the plan named `tests/canonical-json.test.ts::ordinal key sort, array order preserved, number-literal handling pinned`. It exists at line 40 and passes (`ok 1`). Twelve R5 tests, covering ordinal sort against `localeCompare`, sorting at every depth, array order preserved, the number-literal deviation pinned deliberately, the nested-`Date` trap, and `null` distinct from absent and from `''`. **The artifact resolves, so the disposition is `handled` — but the item's stated premise does not.** R5's causal column asserts "0 multi-element array fields in 52,000 sampled findings", and that is false for the content that enters the hash. See Finding V1. The tests are right; the risk assessment attached to them understated the exposure |

All five artifacts named in the plan exist, are tagged with their R-id, and pass. I confirmed each
by name in TAP output rather than by grep alone.

## REQUIRED SECTION 3 — Decision Drift

`decisions.md` holds nine original decisions plus a six-entry planning-drift section. Below, every
entry, with what actually happened.

### Original decisions

| decision | outcome |
|---|---|
| Use the EA-nested `cybi-db-models` `enum.prisma` as the enum source | **landed as decided.** Pinned at `3d26e24e…`, which I confirmed is that repo's HEAD |
| Vendor a generated copy rather than reading Prisma at runtime | **landed as decided.** `src/target/contract.generated.ts`, no runtime sibling-checkout dependency, repo still dependency-free (no `node_modules`) |
| Keep the generation step in-repo for reproducibility and provenance | **landed as decided.** `src/target/generate-contract.ts` exists. Residual gap, honestly recorded in the README: no test asserts regenerating reproduces the artifact |
| Emit `@map` values, never Prisma identifiers | **landed as decided.** Verified in the artifact and in real output (`os_type` values included `windows-server`) |
| **Target the prod-eu column set** | **SUPERSEDED during planning.** Prisma is the stg superset; because `batch_id` is nullable, emitting nothing satisfies both clusters. `CONTRACT_PROVENANCE.columnSet` records `prisma-superset (stg shape; prod-eu lacks batch_id)` and says plainly it is "NEITHER dump exactly" |
| Do not modify `src/reader.ts` or `src/manifest.ts` semantics | **landed as decided.** Both files are untouched at mtime 17:12:14, the oldest in `src/`, predating every phase-0 artifact |
| Proceeding on unverified: the enum copy reflects the live cluster | **still unverified.** Correctly carried as A9's scope note |
| Proceeding on unverified: the dumps give the right column sets | **REVERSED by evidence.** See the drift entry below; this is A3/A15 |
| Proceeding on unverified: a declarative format expressive enough for Falcon and Tenable matters | **settled positively.** A12; three real mappings, no escape hatch, Stop Condition did not fire |

### Planning-drift section

| entry | outcome |
|---|---|
| **FLIPPED — column set from Prisma, not the pg_catalog dumps** | **landed, and I confirmed the underlying fact myself.** All six cloud columns plus `batch_id` are absent from the prod-eu dump and present in Prisma. The flip was correct and load-bearing |
| **Corollary — dumps retained for type precision only** | **landed as decided.** `CONTRACT_PROVENANCE` separates `columnSetSource` (Prisma) from `typePrecisionSource` (the prod-eu dump), and `typeUnverifiedColumns` names the eight columns where no dump could corroborate the type |
| **SUPERSEDED — prod-eu column set → Prisma superset** | **landed as decided.** `batch_id` is declared excluded with the reason "the manifest carries no batch identity; left at the column default", which I read in `mappings/falcon-assets.json` |
| **CORRECTED — table names are plural** | **landed as decided.** `ASSET_TABLE.name` is `parser_output_assets_enrich`. This was a **defect in the contract itself** — it named a table that does not exist. Caught and fixed in place in three files |
| **NEW — six cloud columns declared out of scope, not silently skipped** | **landed as decided.** They are in the exclusions list with reasons, and `assertColumnCoverage` makes an unmapped-and-unexcluded column a construction-time error (`tests/mapping-validate.test.ts:225`, passing) |
| **NEW — mapping-as-data is new design, not a port** | **landed as decided, and it is the slice's real achievement.** I re-ran the vendor-knowledge audit independently: 192 distinct manifest leaf field names across the three manifests, minus 39 target column names, word-boundary matched against `src/mapping/engine.ts`. Hits in code: `source`, `sources` only — both `MappingSpec` vocabulary for `decide` rules. Falcon/CrowdStrike/Tenable appear only in comment citations. `src/rows/exposureContent.ts` and `src/rows/canonicalJson.ts` have zero code hits |
| **NEW — do not port the endianness swap** | **landed as decided.** `tests/row-id.test.ts:30` pins the published RFC 4122 vector and passes (`ok 88`-range) |

### The reversal that must be recorded as drift

**Message shadowing in `assertMappingUsable` was recorded as ACCEPTED RISK and then REVERSED to
FIXED.** This is drift and I record it as such.

- `execution_notes.md` first carries a section headed `ACCEPTED RISK — message shadowing in
  assertMappingUsable (validate.ts:133-142)`, with a four-point argument and the disposition
  "accept for this slice; do the extraction in the next one".
- A later section, `Repair 2`, withdraws it: "**My accepted-risk disposition above is WITHDRAWN.**"
  The resolution was a third option — guard rather than duplicate — so `assertFieldReadsPerColumn`
  returns early on a vendor/entity mismatch and skips its parent half on a flat lane, keeping one
  authority and one message in `src/mapping/spec.ts`.
- I confirmed the fix landed: `src/mapping/validate.ts` is 19,311 bytes at mtime 19:31:31, the
  newest file under `src/`, and the vendor/entity/flat-lane message tests pass
  (`tests/mapping-validate.test.ts:118`, `:148`, `:178`).
- The repair broke 4 of 100 tests, all of which asserted the older, worse behaviour — one titled
  `"...and only one direction is caught"`. The suite was re-pinned at **107 tests, which I
  reproduced at 107/107, twice concurrently.**
- A genuinely broken diagnostic was fixed in passing: the flat-lane message had been printing
  `The lane's parent fields are: .`

Both artifacts — the withdrawn acceptance and the repair — are left in the file rather than edited
out. That is the right record. The operator should read the `ACCEPTED RISK` heading as superseded;
its four arguments no longer describe the code.

## Success Criteria coverage

Criteria 1–7 are demonstration-not-assertion. For each I state whether evidence was **produced**,
not whether code exists that would produce it.

| # | criterion | met | evidence I produced or reproduced |
|---|---|---|---|
| 1 | three lanes produce rows; Tenable needs only mapping data | **yes, with a method caveat** | I ran all four lanes to completion. The zero-edit half cannot be shown by diff (no commits). Substitutes: mtime — `mappings/tenable-findings.json` at 18:31:29, every `src/mapping/`, `src/rows/`, `src/ids/`, `src/target/` file older except `validate.ts` (19:31, the unrelated Repair 1); and the 192-name vendor-knowledge audit above, which is the stronger of the two. Tests `criterion 1:` ×2 pass |
| 2 | absent-field mapping rejected at load time, before any record | **yes** | `tests/mapping-validate.test.ts:63`, passing, with a reader that throws if iterated and an assertion that it was not. The negative case is real data: `apps` is absent from the manifest and from all 246,212 findings |
| 3 | ids byte-identical across runs with same instance, different across instances | **yes, both directions** | my own runs on the 474 MB lane: same `--instance-id` twice → `1ab5be0d…` both times; different `--instance-id` → `88d8d63a…`. Plus `criterion 3:` ×4 tests passing |
| 4 | exposure identity composes content; report repeated pairs on this data | **yes** | my `--pair-audit`: lab 92,251/246,129 = **37.5%** repeated, 246,129 distinct ids, **0** collisions; prod 8,079/25,565 = **31.6%**, **0** collisions. Both exact to the report. Contract asked what this data gives rather than the prototype's 30.9%, and it was answered on two independent batches |
| 5 | `created_at` exclusion demonstrated, not just coded | **yes** | the same experiment as criterion 3: runs separated in wall clock, one with `created_at` pinned and one defaulted to now, identical digest. Plus `tests/identity.test.ts:113` and `:136` |
| 6 | enum validation rejects non-member, accepts `@map` spellings; per-lane reject counts | **yes** | 11 R1 tests including both spellings in both directions. Per-lane counts I reproduced: **zero on all four real lanes** — Falcon assets, Falcon lab findings, Falcon prod findings, Tenable. Reported as an unexercised path, which is the honest reading |
| 7 | twelve fingerprint columns produced; `asset_id`-before-content ordering handled explicitly | **yes** | I inspected a real emitted row: 21 columns, all twelve fingerprint columns present with real values. Ordering is explicit — ten `CONTENT_COLUMNS`, `asset_id`/`cve_id` positioned earlier in the uuid5 name, pinned at `tests/row-id.test.ts:91` and `:104` |
| 8 | measured numbers per lane; 474 MB under a stated heap cap; compare to baseline | **yes, with two number corrections** | all four lanes have records/s, MB/s and peak RSS in the README. Cap stated and reproduced: `--max-old-space-size=64`. Corrections: `--pair-audit` peak RSS is 484 MB not 386 MB; the reader baseline measured 385 MB/s for me against the stated 409 |
| 9 | README updated with new numbers and an honest limits extension | **yes** | mtime 19:42:25, the newest file in the repo. Carries the four-lane table, the identity section, the collapse table, and a 17-item "Deliberate limits" section that names the untested `map.ts`, the zero real enum rejects, the F1 volatility, the F2 substitution, the single Tenable batch, and the absent TypeScript compiler. This is a genuinely honest limits section, not a formality |
| 10 | generated artifact records provenance: repo, commit, date, target cluster | **yes** | `CONTRACT_PROVENANCE` carries all four. I independently confirmed the commit is that repo's HEAD. Deviation worth noting and better than the letter of the criterion: rather than naming one cluster it says "NEITHER dump exactly" and explains why one artifact serves both. Pinned by `tests/target-contract.test.ts:30` |

## The operator's request beyond the formal criteria

- **"stable, doesn't have to be perfect"** — met. 107 tests, concurrency-safe under two simultaneous
  runs, clean typecheck, four real lanes running end to end, deterministic ids. The one place
  stability is genuinely at risk is A17/Finding V1, and that is a property of the design bet rather
  than a defect in the build.
- **"prove it works and reason about how it scales"** — met, and the reasoning is the better half.
  The memory property is the load-bearing scaling claim and it survived: 474 MB through a 64 MB
  heap. The extrapolation to the operator's real batch is stated in the terms that matter — 946.9 GB
  across 12,716 files, ~2.8 hours single-threaded, fan-out available because records are
  independent. That is the correct shape of answer: scale is file count, not file size.
- **"SCALES means 400-500 MiB of a file"** — met directly. 474 MB is inside the stated envelope, and
  the largest prod file is 165 MB, well under it.
- **The generic-manifest framing** ("receive the manifest, from either collector, either endpoint,
  generically"; per (vendor, lane); "a very functional direction") — **met, and this is the strongest
  part of the slice.** Two structurally different lane shapes (flat `$`, correlated
  `$.findings[*]` + `$.host`) and two vendors run through one engine that provably holds no vendor
  field name in code. Adding Tenable was three files under `mappings/`. The functional direction is
  real: `ValueSpec` is a closed union of ten data kinds with no escape hatch to code, which is what
  buys the load-time checkability.
- **No collector was run.** I ran none. All four datasets came from the on-disk scratchpad; nothing
  was re-downloaded from S3.

## Is it immediately usable

Yes. `npm test` works, all three CLIs work, the mapping data is real, and `README.md` § "Run it" is
accurate — I ran the `map` invocations as written. Two frictions, both documented in the README:
typecheck requires an out-of-tree TypeScript install, and `src/cli/map.ts` has no test.

## F1 and F2 — is accepting them defensible

**F1 — identity is effectively keyed on the vendor record via `additional_fields`. Recorded, not
repaired. Accepting it is defensible; the documentation is adequate.**

I confirmed the mechanism independently. A real emitted exposure row's `additional_fields` carries
12 vendor fields — `id, cid, aid, vulnerability_id, vulnerability_metadata_id, data_providers,
created_timestamp, updated_timestamp, status, confidence, remediation, cve` — including fields
already claimed by named columns. Falcon's per-finding `$.id` is in there and is unique per finding,
so it, not `name`/`severity`/`display_name`, is what actually delivers 246,129 distinct ids from
153,878 distinct pairs.

Defensible because it **reproduces EA's own rule** rather than inventing one: EA's twelve-column
fingerprint includes `additional_fields`, so EA would treat those rows as distinct too. And the
alternative — carrying only unclaimed fields — would change every id in every lane, which is not a
change to make late in a slice whose brief was stability. The README limits section states the
consequence plainly, including the churn implication in EA's generation model.

**F2 — `display_name` is a semantic substitution inside identity. Recorded, not repaired. Accepting
it is defensible; the documentation is adequate but the operator does need to make the call.**

Confirmed on a real row: `display_name` = `"Update Microsoft Edge (Chromium-Based)"`, the imperative
remediation title, where the C# produced product-name-version form like
`"Google Chrome Enterprise 150.0.7871.46"`. On that row `display_name` is also the leading clause of
`mitigation`, which makes the substitution visible: the column now holds a remediation action, not
an application name.

Defensible because the intended source does not exist — `apps` is absent from the manifest and from
all 246,212 records — so there is no faithful option, only a choice of substitute, and 85% coverage
with nulls falling exactly on `closed` findings is a well-reasoned one. But `display_name` is one of
the twelve fingerprint columns, so this **changes exposure identity relative to the old pipeline for
the same vendor data**, and that is a product decision rather than an engineering one. The README
says exactly this. It is adequately documented; it is not yet decided.

## Findings the pipeline itself got wrong

The two known cases are real: the contract named a non-existent singular table (caught in planning,
fixed in three files), and R3 was mis-scoped (caught in execution, corrected, and both halves
landed in tests). Three more:

### V1 — R5's premise is false, and it is the one that matters

`orchestration_plan.md` R5: *"The Falcon data does not exercise any of this — 0 multi-element array
fields in 52,000 sampled findings — so vendor data cannot catch it."*

Measured by me, on emitted rows and raw findings:

| lane | rows/records scanned | with a multi-element array inside `additional_fields` |
|---|--:|--:|
| Falcon prod client | 25,565 | **25,514 (99.8%)** |
| Falcon lab (first 2,000 findings of line 1) | 2,000 | **1,984 (99.2%)** |
| Tenable | 4,672 | **4,672 (100%)** |

Top offenders: `cve.vendor_advisory` (22,999 prod rows), `remediation.entities` (5,832),
`cve.references` (3,407); Tenable's `plugin.xrefs` (4,665), `plugin.cve` (4,226),
`plugin.see_also` (3,940).

The likely cause of the error: the 52,000-record sample counted **top-level manifest fields**, of
which none is an array. But `additionalFields: {carry: "wholeRecord"}` puts the entire nested vendor
record into `additional_fields`, and that is one of the ten `CONTENT_COLUMNS`. The arrays are one
level down from where the count looked.

Consequence, demonstrated rather than inferred — same real row, two elements of
`cve.vendor_advisory` swapped, nothing semantic changed:

```
id with vendor order        : c26dfb76-56fd-5e6f-94e4-5202bacd05da
id with two elements swapped: c2b31556-442a-5f1c-8096-c5ac273e5065
```

So if Falcon or Tenable returns any of these lists in a different order on a later collection — a
routine thing for API-returned lists — the exposure id changes, a new row is written, and EA demotes
the previous one. This is precisely the hazard A17 was recorded to guard against, on 99–100% of
rows, and the slice concluded it was unexercised.

Note what is *not* wrong: the code and the R5 tests are correct. `canonicalJson` preserves array
order deliberately, citing `CanonicalJson.cs:63-74`, and `tests/canonical-json.test.ts:78` pins it.
The defect is in the risk assessment, not the implementation. Whether to sort arrays inside
`additional_fields` is a real design decision that this evidence now forces, and it interacts with
F1: both point at the same place, the `wholeRecord` carry rule.

### V2 — 11 of 298 Falcon asset rows collide on `(value, type)`, EA's demotion key

`constraints.md` states ids are deliberately not stable across runs "because EA demotes prior
generations via `latest = false` keyed on `(value, type)` per flow". Nothing measured whether
`(value, type)` is unique *within* a run. I did:

```
asset rows: 298 | distinct row ids: 298 | distinct value: 292
duplicate (value, type) groups: 5 | rows involved: 11
  x3 ["10.10.1.23","host"]      x2 ["DC01","host"]        x2 ["CROWDSTRIKE","host"]
  x2 ["RHEL-SPlunk","host"]     x2 ["WIN11-CROWDSTRI","host"]
```

`value` is `firstNonBlank(hostname, fqdn, current_local_ip, parentKey)` and `type` is the constant
`host`, so two hosts sharing a hostname — or three sharing an IP — produce the same demotion key.
This does not break the slice: row ids are 298-distinct and A16 holds. But it means the generation
model A1 describes will treat 11 distinct assets as generations of each other. It sits squarely in
A1's territory and belongs in the README limits.

### V3 — two citation and number errors

- `constraints.md` and A11 both cite `enum.prisma:79` and `:89` for the two hyphenated members. The
  actual lines are **80** and **90**. Trivial in itself, but the whole discipline rests on citations
  being spot-checkable, so it is worth fixing.
- `README.md` states `--pair-audit` raises peak RSS "from 191 MB to 386 MB". I measured **484 MB**.
- C1's re-verified reader baseline of 409 MB/s did not reproduce for me (385 MB/s), with identical
  record counts and zero drift. Variance, not regression — but stated as settled fact.

### Not a finding, but worth knowing

`tsconfig.json` has `"include": ["src/**/*.ts"]`, so the typecheck command in
`orchestration_plan.md` § Amendments covers `src/` only. `execution_notes.md` says "Typecheck clean
with tests included"; that must have used a different invocation, because the documented one does
not reach `tests/`. I could not reproduce a tests-inclusive typecheck without writing a config into
the repo, which I declined to do. Low stakes — the tests run, so type errors that execute would
surface — but the claim as written is not reproducible from the documented command.

## Action items, in the order I would take them

1. **`git init` is done but there is no commit. Make one.** Nine untracked paths, zero commits, and
   an unrepeatable slice. This is the highest-value five minutes available.
2. **Decide the array-order question (V1/A17).** The evidence now exists and it says 99–100% of rows
   are exposed. Either sort arrays inside `additional_fields` and accept a one-time re-mint of every
   exposure id, or record explicitly that ids are expected to churn on vendor list reordering. Do
   not leave it as "unexercised by the data" — that is no longer true.
3. **Add V1 and V2 to the README's "Deliberate limits".** The section is otherwise the most honest
   artifact in the repo; these two are the gaps in it.
4. **Correct three numbers**: `--pair-audit` peak RSS 386 → 484 MB; the reader baseline as a range
   (385–409 MB/s) rather than 409; `enum.prisma:79,89` → `:80,90`.
5. **Get a test onto `src/cli/map.ts`.** The slice's own accounting names it the largest gap and it
   is right: it is the newest code, it produced every reported number, and nothing guards it.
6. **Put F2 (`display_name`) in front of whoever owns parity with the old pipeline.** It is
   documented and defensible; it is not decided.
