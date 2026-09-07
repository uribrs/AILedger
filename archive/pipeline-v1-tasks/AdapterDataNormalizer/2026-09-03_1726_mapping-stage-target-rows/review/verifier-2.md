# Verifier 2 — Mapping stage: manifest-described records to target-shaped rows

Second pass, 2026-09-03, after the repair and production-alignment cycles. Method: ran the
artifacts and re-derived the evidence. I did not read-and-agree, and I did not restate verifier-1.

## No diff was possible, again

`state.json.baseRef` is `null`. Re-confirmed: `git log --oneline -5` → `fatal: your current branch
'main' does not have any commits yet`; `git status --short` → nine `??` entries. Zero commits, zero
tracked files. **No diff against a base ref exists**, so nothing below rests on one. Scope: the
artifacts named in `execution_notes.md`, plus what I ran myself.

**Method for criterion 1's zero-engine-edit half.** Verifier-1 substituted mtime ordering plus a
vendor-knowledge audit. **The mtime substitute is now dead**: `src/mapping/{spec,engine,validate}.ts`
were edited at 22:12–22:14 for `caseFold`, so every engine file is now newer than
`mappings/tenable-findings.json` (18:31). I used the structural audit only, and ran it myself:

```
distinct manifest leaf field names across the three manifests: 192
minus target column names:                                    182
vendor field names in ENGINE CODE (comments stripped):          9
  all nine are MappingSpec vocabulary: source, sources, asset
vendor NAMES (falcon/crowdstrike/tenable) in engine code:        0
```

Plus a direct check the mtime argument could not make: **neither `mappings/tenable-findings.json`
nor `mappings/falcon-findings.json` declares `caseFold`** (grep count 0 each). The one engine
capability added since verifier-1 serves the Falcon asset lane alone, so it cannot be what made
Tenable work. That is stronger than mtime, and it survives the repair cycle.

## Verdict

The repair cycle materially improved the deliverable. The mapping now matches the production Spark
parser where the task says it should, and I verified each alignment on real data rather than
accepting the record. **128 tests pass, twice concurrently.**

Three findings that change what the operator should believe:

1. **The REVERSAL's central factual claim is not supported by its own evidence.** Both committed
   snapshots *predate* the cloud-columns migration by 17–19 days. They cannot say whether those six
   columns are deployed. "UNDEPLOYED — verified across every committed cluster" over-reads the same
   silence that C5 over-read in the opposite direction. The retarget's *outcome* is right; its
   *justification* is wrong. Detail in F-A below.
2. **`README.md` is stale in six ways, and one of them states the reversed position as fact.**
   Criterion 9 is not met for the retarget, `caseFold`, `display_name`, or `severity`.
3. **The production alignment left the largest divergence class unexamined — default values.**
   `os_type` is null on 248 of 298 assets where production emits `other`; `os_version` 248,
   `os_build` 248, `fqdn` 286. The cycle documented a 1-of-298 divergence in a 400-word mapping note
   and never looked at an 83% one.

## The test suite went from red to green during this pass

| Time | Suite | Failure |
|---|---|---|
| 22:33 (my first two concurrent runs) | 118 tests, **117 pass / 1 fail** | `criterion 10: the generated artifact records its provenance`, `tests/target-contract.test.ts:44:35` — `Cannot read properties of undefined (reading 'length')`, reading the removed `CONTRACT_PROVENANCE.columnSet` |
| 22:34:37 | file re-pinned | — |
| after | **128 tests, 128 pass / 0 fail**, twice concurrently (exit 0 both) | none |

So the "known re-pin in flight" was real and it landed. Recording it because for ~90 minutes the
repo's evidence for criterion 10 was a failing test that named the criterion, and the re-pin also
added 10 tests. The failure was a leftover field reference, not a substantive regression.

## Each of the eight changes since verifier-1

| # | Change | Verified | Evidence I produced |
|---|---|---|---|
| 1 | Three-repo audit; production authoritative | **yes** | `cymulate-integration-parsers` at `2638a0f` (2026-09-02). Every load-bearing citation re-read by me: `crowdstrikeAssetsFindings.py:72-73`, `crowdstrikeAssets.py:52-64`, `base_parser.py:231,234-237,239-253,281-284,545,791`. All accurate |
| 2 | `display_name` → `$.vulnerability_id` | **yes** | `display_name == name` on **25,565 of 25,565** prod rows and **246,129 of 246,129** lab rows, **zero nulls** (was 85% coverage). Production confirmed at `crowdstrikeAssetsFindings.py:73` |
| 3 | `severity` fallback `'info'` removed | **yes** | prod lane emits `high` 11,171 · `medium` 10,924 · `critical` 2,564 · `low` 906. No `info`, no null. Test `ok 56` |
| 4 | `caseFold?: 'lower'` | **yes** | `spec.ts:232` + check `:439-450`; applied `engine.ts:279` via `foldCase` `:509`; refused on jsonb `validate.ts:299-306`; declared on `value` and `type` in `mappings/falcon-assets.json:13,34`. Real data: **298/298 values lowercase, 298/298 `type = host`**. 6 engine tests (`ok 49-54`) + 3 validate tests |
| 5 | `assertValueShapes` | **yes** | `validate.ts:293-330`, called from `assertMappingUsable:146`. Both rules present. `ok 47` pins both directions failing at load; `ok 48` keeps the stream-time backstop reachable |
| 6 | Message shadowing fixed by guards | **yes, and the withdrawal is correct** | `assertFieldReadsPerColumn` returns early at `validate.ts:173` on vendor/entity mismatch and again on a flat lane (`laneHasParent`). Authority and wording stay in `spec.ts` — one authority, one message. Not reordered, not duplicated. The superseded `ACCEPTED RISK` heading is still in `execution_notes.md:266`; read it as void |
| 7 | Contract retarget to one cluster | **outcome yes, justification no** | Emitted asset row carries exactly **22 columns**, exposure row **20** — matching `prod-eu`'s snapshot exactly. `typeUnverifiedColumns` gone (`ok 118` asserts its absence). `isUndeployedColumn` table-qualified (`ok 119`). **But see F-A** |
| 8 | `$localIp` identity merge fixed | **yes, no merge remains** | 298 asset rows, **298 distinct ids, 292 distinct `(value, type)`** — the 5 residual groups are verifier-1's V2, not the defect. Divergence count confirmed **exactly 1 of 298**: `ip-172-31-20-25` is the only hostname containing lowercase `ip`. See F-D on the blast-radius claim |

### Item 8's residual divergence, counted myself

```
raw asset records: 298 | with a hostname: 222 | no hostname: 76
hostnames already lowercase:                          11   (matches the 11-of-298 rationale)
hostnames containing LOWERCASE 'ip' (Spark .contains):  1   ["ip-172-31-20-25"]
hostnames matching 'ip' only case-INSENSITIVELY:        0
```

Spark `.contains("ip")` is case-sensitive — confirmed at `crowdstrikeAssets.py:59`. Both counts the
mapping note claims are exact.

## REQUIRED SECTION 1 — Assumption Disposition

| id | status | name | citation | actor |
|----|--------|------|----------|-------|
| A1 | NEVER-TESTED | ea-generation-model | nothing in this slice reaches Exposure Analytics; no `latest = false` demotion was exercised. Adjacent evidence only: 11 of 298 asset rows share `(value, type)`, which I re-measured | verifier |
| A2 | VALIDATED | exposure-identity-is-content-md5 | my own `--pair-audit` runs: lab 153,878 distinct pairs, 92,251 rows beyond first, 246,129 distinct ids, 0 collisions; prod 17,486 / 8,079 / 25,565 / 0; Tenable 4,592 / 80 / 4,672 / 0. Fingerprint SQL at `parsed-data.repository.ts:70-76` re-read in verifier-1 | verifier |
| A3 | REJECTED | committed-dumps-current-enough | **rejected for a corrected reason.** Not "stale relative to Prisma" (verifier-1) but *silent on the question that mattered*: `prod-eu` snapshot Generated **2026-07-10 21:05:17+00**, `stg` **2026-07-12 09:36:07+00**, and `migrations/20260729120100_add_cloud_columns_to_parser_output_assets` is dated **2026-07-29** — 17-19 days later. A snapshot cannot report a migration that postdates it. **Do not re-assume a snapshot answers a deployment question about any migration newer than its Generated line** | verifier |
| A4 | NEVER-TESTED | jsonb-not-double-encoded | no Postgres write in scope, so no column was observed. Our NDJSON keeps nested vendor values native (`additional_fields.network_interfaces` is a real array of objects, checked by me). Adjacent counter-evidence I verified: production **double-encodes 6 of 54** `additional_fields` members (`test_files/tenable/assets_and_findings/findings.expected_findings.json`: `cvss_vector`, `vpr`, `port`, `scan`, `vpr_v2`, `cvss_temporal_vector` are JSON strings inside the JSON), mechanism `F.to_json` at `base_parser.py:545,620`. That points against the assumption, but at production's boundary, not ours | verifier |
| A5 | NEVER-TESTED | column-width-for-89-char-key | column width is outside this repo and no column was written. Defused for this batch, re-measured by me: longest parent key 89 chars, **longest emitted `value` 27 chars** over 298 non-null values — the `parentKey` last resort never wins | verifier |
| A6 | NEVER-TESTED | downstream-match-key | outside this repo and this slice; nothing downstream consumed a row | verifier |
| A7 | NEVER-TESTED | falcon-orphan-findings | orphan tolerance was not measured. `tests/end-to-end.test.ts` shows the two lanes agree on asset identity by construction, a different claim | verifier |
| A8 | VALIDATED | tenable-second-vendor-fits | my run: 1,521 records → 4,672 rows, fan-out 3.072, 629 without CVE, 4,672 distinct ids, 0 collisions, zero enum rejects, and **zero `caseFold` declarations** — so the one engine capability added since verifier-1 is not what made it work. Scope: one 13 MB batch; the TenableIo collector is prohibited | verifier |
| A9 | VALIDATED | ea-nested-enum-source-is-authoritative | `git rev-parse HEAD` in `cybi-db-models` = `3d26e24ee429e9d9a55b20912c7d8c525df6d34c`, 2026-08-02, "add cloud resource support (#456)" — exactly the pin in `CONTRACT_PROVENANCE`. Strengthened by the retarget: the snapshots carry **no** `CREATE TYPE` and no member lists, so Prisma is the only committed enum source, which the reversal correctly kept. Still never checked against a live cluster | verifier |
| A10 | VALIDATED | enum-member-counting-excludes-schema-directive | my own parse of `enum.prisma`, excluding `@@schema` and comments: **23 / 15 / 12 / 5 / 3**, matching `ENUM_MEMBERS` and the specified counts | verifier |
| A11 | VALIDATED | map-value-is-the-emitted-string | read by me: `windows_server   @map("windows-server")` at **`enum.prisma:80`** and `active_directory @map("active-directory")` at **`:90`**. **The `:79`/`:89` citation error verifier-1 flagged is STILL PRESENT** in `assumptions.md:58` and `constraints.md:55`. Never exercised against a live cast; `windows-server` appears on 6 of 298 real asset rows, `active-directory` on none | verifier |
| A12 | VALIDATED | declarative-mapping-expressive-enough | held, with a material qualification. `ValueSpec` is still a closed **ten**-member union with no `function` member and no expression string (`spec.ts:78-182`); all three real mappings load through the full gate; all four lanes run to completion; the escape-hatch Stop Condition did not fire. **Qualification: the format needed one new capability mid-slice** (`caseFold`, on `ColumnMapping` not `ValueSpec`) to express production's identity rule, and production's `contains('ip')` branch remains **inexpressible** — 1 of 298 assets diverges as a result, declared in the mapping's own `note` | verifier |
| A13 | VALIDATED | streaming-survives-content-hashing | my run: 474 MB lane under `--max-old-space-size=64` completes in **5.0s, 94 MB/s, peak RSS 184 MB**, 246,212 records → 246,129 rows. Hashing is inside that path | verifier |
| A14 | VALIDATED | twelve-columns-and-the-ordering-constraint | I inspected a real emitted prod exposure row: 20 columns, **all twelve fingerprint columns present with real values**. Ordering explicit and pinned: `ok 113` (v5 name is instanceId, parentKey, cveId, content in that order) and `ok 114` (swapping parent key and content changes the id) | verifier |
| A15 | VALIDATED | column-sets-readable-from-dumps | **verifier-1's REJECTED is now invalid — the retarget reversed it and the reversal is right on this point.** I counted the snapshots directly: `prod-eu` **22 asset / 20 exposure**, `stg` **23 / 21**, differing by `batch_id` on each. The generated contract declares exactly 22/20 and the emitted rows carry exactly 22/20. Pinned by `ok 120`. Readable for what the snapshot covers; silent beyond it, which is A3, now a separate claim | verifier |
| A16 | VALIDATED | asset-parent-key-uniqueness | measured by me through the real chain: **298 asset rows, 298 distinct row ids**, max parent-key length 89. No silent two-into-one collapse on this batch | verifier |
| A17 | REJECTED | canonical-content-insensitive-to-array-order | **stands rejected, by design rather than by accident.** `src/rows/canonicalJson.ts:118` preserves array order deliberately, citing `CanonicalJson.cs:63-74`, and `ok 5` / `ok 11` pin array order as significant on the production content path. So content IS order-sensitive; the assumption is false at the source. The plan's mitigating claim (0% of data exercises it) was already retracted to 99.3%. **Must not be re-assumed** | verifier |
| A18 | REJECTED | prototype-reflects-production-mapping | verified by me at both citations: `crowdstrikeAssetsFindings.py:72-73` maps **both** `name` and `display_name` to path `vulnerability_id`, deliberately duplicated; `_HEAVY_FINDING_FIELDS = frozenset({"host_info", "apps", "suppression_info"})` at `CrowdstrikeAssetsFindingsNotHydrated.py:51` prunes `apps` at line 169. So `apps[0].product_name_version` was never available to production. **Citation note: `execution_notes.md` cites `_HEAVY_FINDING_FIELDS` without its file; it is in `CrowdstrikeAssetsFindingsNotHydrated.py`, not `crowdstrikeAssetsFindings.py`** | verifier |
| A19 | REJECTED | production-authoritative-and-every-divergence-recorded | first half holds — I verified each aligned column on real data. **Second half is false**, and that is the rejection: `decisions.md`'s divergence table omits the default-value class entirely. Measured by me on 298 real asset rows: `os_type` **248 null** where production emits `other` (`default_value="Other"`, `crowdstrikeAssets.py:66`, lowercased at `base_parser.py:231ff`), `os_version` **248 null** vs `other`, `os_build` **248 null** vs `""`, `fqdn` **286 null** vs `""`. That is 83–96% of rows, against a documented divergence of 1 row. See F-C | verifier |
| A20 | REJECTED | production-status-open-is-not-an-enum-member | premise false; the normalization exists one repo upstream. `base_parser.py:281-284`: `open`→`opened`, `close`/`closed`→`resolved`, `re-opened`→`reopened`, read by me. Corroborated by committed real output — `findings.expected_findings.json` carries `status: 'opened'`. Our prod lane emits `opened` 12,630 · `resolved` 12,656 · `reopened` 279, zero rejects | verifier |
| A21 | VALIDATED | production-lowercases-type-and-os_type-somewhere | the step exists and I read it: `base_parser.py:231` `withColumn("type", F.lower(...))` under the comment `# Lowercase type to match asset_type enum (e.g. "Host" → "host")`; `:239-253` lowercases and enum-coerces `os_type`. Corroborated by `assets.expected_assets.json`: `type='host'`, `os_type='windows'`, `value='dc01'`. No production incident exists | verifier |
| A22 | VALIDATED | production-never-derives-windows-server | `base_parser.py:245` is `.when(_os.startswith("windows-server"), F.lit("windows-server"))` — it requires the input to already start with that string. Falcon's `platform_name` values, counted by me across all 298 records: `Windows` 37, `Linux` 12, `Mac` 1, **absent 248**. So production lands on `windows` (confirmed in `assets.expected_assets.json`) and never emits `windows-server` for Falcon. Ours emits it on **6 of 298** — deliberately more specific | verifier |

Totals: **VALIDATED 12 · REJECTED 5 · NEVER-TESTED 5.**

Changes against verifier-1: **A15 REJECTED → VALIDATED** (the retarget reversed the basis, correctly);
**A3 stays REJECTED but its reason is replaced** (silence about newer migrations, not staleness
against Prisma). A3 and A15 are no longer the same claim, and verifier-1 treated them as one.
The five NEVER-TESTED all concern behaviour outside this repo and are correctly still open.

### Decision drift — every entry in `decisions.md`

| entry | outcome |
|---|---|
| EA-nested `enum.prisma` as enum source | **landed.** Pinned at `3d26e24e`, confirmed HEAD. Reinforced by the retarget: snapshots carry no enum members |
| Vendor a generated copy, not a runtime Prisma read | **landed.** `src/target/contract.generated.ts`, no sibling-checkout dependency at runtime |
| Keep the generation step in-repo | **landed.** `src/target/generate-contract.ts`. Still no test that regenerating reproduces the artifact |
| Emit `@map` values, never Prisma identifiers | **landed.** `ok 21`-`ok 24`; `windows-server` on real rows |
| Target the prod-eu column set | **SUPERSEDED, then RESTORED.** Superseded in planning by the Prisma superset; restored by the REVERSAL. `cluster: 'prod-eu'`, 22/20 columns. Two flips on one decision, and the final position is the original one |
| Do not modify `src/reader.ts` / `src/manifest.ts` semantics | **landed.** Both at mtime 17:12:14, the oldest in `src/`, older than every task artifact |
| Unverified: enum copy reflects the live cluster | **still unverified.** A9 scope note |
| Unverified: dumps give the right column sets | **REVERSED TWICE.** Rejected in planning (C5), restored by the REVERSAL. Now A15 VALIDATED / A3 REJECTED as separate claims |
| Unverified: a declarative format expressive enough matters | **settled positively, with one extension.** A12 |
| **FLIPPED — column set from Prisma** (planning drift) | **REVERSED.** Superseded by the REVERSAL section. `research/internal-recon.md:675` (C5) still states the flipped position with no forward pointer to the reversal — a reader landing there gets the wrong answer |
| **Corollary — dumps for type precision only** | **REVERSED.** Snapshots are now the column-set authority; `intentSource` demotes Prisma to intent |
| **SUPERSEDED — prod-eu → Prisma superset** | **REVERSED.** `ok 117` now actively refuses a union: `'prisma-superset (stg shape; prod-eu lacks batch_id)'` would fail its regex |
| **CORRECTED — table names are plural** | **landed.** `parser_output_assets_enrich`. Still the clearest pipeline defect: the contract named a table that does not exist |
| **NEW — six cloud columns declared out of scope** | **landed and re-based.** They moved from `columns` (as excluded) to `prismaOnlyUndeployed`, plus `batch_id` on both tables — eight entries. `ok 118` asserts they are NOT declared columns |
| **NEW — mapping-as-data is new design** | **landed; still the slice's real achievement.** My independent 182-name audit: zero vendor field names and zero vendor names in engine code |
| **NEW — do not port the endianness swap** | **landed.** RFC 4122 vector pinned |
| **DECISION 1 — case-fold `value` and `type` only** | **landed exactly.** 298/298 values lowercase, `type = host` on all; `ok 49` asserts *exactly* those two columns fold and nothing else. Asset id digest unchanged by the fold, which is the correct result |
| **DECISION 2 — count rejections, do not silently drop** | **landed.** Rejections are counted with column, value and line number. Untested by real data: **zero rejections on all four lanes**, which I reproduced |
| **REVERSAL — Prisma-over-dumps was wrong** | **landed in code; its stated evidence does not support it.** See F-A. This is the drift entry the operator should read most carefully |
| **WITHDRAWN — accepted risk on message shadowing** | **withdrawal is correct.** Guard clauses at `validate.ts:173` and the flat-lane guard; authority and wording remain single, in `spec.ts`. The superseded `ACCEPTED RISK` section at `execution_notes.md:266` is void — correctly left in place as a record, but it reads as current |

## REQUIRED SECTION 2 — Attention Item Disposition

| id | final disposition | name | evidence |
|---|---|---|---|
| R1 | handled | enum-nonmember-silent-drop | named test exists and passes: `ok 17 - R1: rejects a non-member and reports a counted rejection`. **11 R1 tests** (`ok 17`–`ok 27`), including both `@map` spellings accepted (`ok 21`) and both Prisma identifiers rejected (`ok 22`), a Prisma identifier as a mapping *literal* refused at load (`ok 24`), a null enum value passed through and not counted (`ok 25`), every enum-typed column of both tables screened (`ok 26`), and rejection ordered before id derivation (`ok 27`). Residual, reproduced by me: **zero rejections on all four real lanes**, so the path is proven only by constructed tests |
| R2 | handled | created-at-in-content-hash | named test passes: `ok 28 - R2: two runs separated in wall-clock time derive identical ids`, plus `ok 29` (`created_at` absent from the content column list by construction). Confirmed end to end, not only in a test: two 474 MB runs with the same `--instance-id` and `--normalized-at` defaulting to now, minutes apart, both printed `idDigest=199f28d494d934052dfd5e8a7fd3f89113abff6e6126ae4e00bfc2129de3c293` |
| R3 | handled | additional-fields-claims-shift-ids | both halves of the mid-execution correction present, and the count grew from 4 tests to **7** (`ok 32`–`ok 38`): the exposure shift (`ok 32`), the asset non-behaviour (`ok 35`, byte-identical), why the asset id carries no content (`ok 36`), claiming is per top-level field (`ok 37`), `wholeRecord` immunity (`ok 33`), and all three shipped mappings carrying `wholeRecord` (`ok 34`). The plan's "re-mints every id in the lane" wording remains wrong for the asset lane; `orchestration_plan.md` § "R3 corrected" says so |
| R4 | handled | mapping-declares-absent-field | named test exists and passes: `ok 71 - R4: rejects a mapping declaring a read of apps against the real Falcon manifest`. **5 R4 tests** (`ok 71`–`ok 75`), including the flat-lane parent-read message (`ok 75`) and the honest limit that only the first path segment is checkable (`ok 74`). Stronger than the plan asked: the test injects a reader that throws if iterated and asserts the message does not contain that string |
| R5 | handled | canonical-order-and-number-literals | named test exists and passes: `ok 1 - R5: ordinal key sort, array order preserved, number-literal handling pinned`. **11 R5 tests** (`ok 1`–`ok 11`). **The premise stays REJECTED** — verifier-1 measured multi-element arrays inside the content hash on 99.8% of prod rows and 100% of Tenable rows, and the orchestrator re-measured 99.3% of 52,000 lab findings against the plan's stated 0%. The tests remain **correct despite it**: they pin array order as *significant*, which is the true behaviour (`ok 5`, `ok 11`), so nothing has to be reverted to fix the risk assessment. The artifact resolves; the assessment attached to it was wrong |

All five artifacts exist, are tagged with their R-id, and pass. I confirmed each by name in TAP
output, not by grep.

## REQUIRED SECTION 3 — Contract criteria

Criteria 1–5 and 7 are demonstration-not-assertion. For each I state whether evidence was
**produced**, not whether code exists that would produce it.

| # | criterion | met | evidence I produced |
|---|---|---|---|
| 1 | three lanes produce rows; Tenable needs only mapping data | **yes** | I ran all four lanes to completion. Zero-edit half shown by the structural audit above (182 vendor field names, 0 in engine code, 0 vendor names) plus the direct fact that no findings mapping declares `caseFold`. The mtime substitute verifier-1 used is no longer available and I did not rely on it |
| 2 | absent-field mapping rejected at load, before any record | **yes** | `ok 71`, with a reader that throws if iterated and an assertion it was not. Negative case is real: `apps` is absent from the manifest, from all 246,212 findings, and — newly established — pruned by production itself |
| 3 | ids byte-identical across runs with same instance, different across instances | **yes, both directions** | my three runs of the 474 MB lane: same `--instance-id` twice → `199f28d4…` both times; different `--instance-id` → `a91076e1…`. Plus `ok 30`, `ok 31`, `ok 108`, `ok 109` |
| 4 | exposure identity composes content; report repeated pairs on this data | **yes, on three lanes** | my `--pair-audit`: lab **153,878 pairs, 92,251 beyond first (37.5%), 246,129 distinct ids, 0 collisions**; prod **17,486 / 8,079 (31.6%) / 25,565 / 0**; Tenable **4,592 / 80 / 4,672 / 0** (new — verifier-1 did not report Tenable). All exact to the record where the record exists |
| 5 | `created_at` exclusion demonstrated, not just coded | **yes** | same experiment as criterion 3 — runs separated in wall clock, identical digest. Plus `ok 28`, `ok 29` |
| 6 | enum validation rejects a non-member, accepts `@map` spellings; per-lane reject counts | **yes** | 11 R1 tests, both spellings both directions. Per-lane counts reproduced by me: **zero on all four real lanes** — Falcon assets, Falcon lab, Falcon prod, Tenable. Reported as an unexercised path, which is honest |
| 7 | twelve fingerprint columns produced; `asset_id`-before-content ordering explicit | **yes** | I inspected a real prod exposure row: all twelve present with real values (`asset_id`, `cve_id`, `type`, `severity`, `status`, `name`, `display_name`, `description`, `mitigation`, `first_seen`, `last_seen`, `additional_fields`). Ordering pinned by `ok 113`, `ok 114` |
| 8 | measured numbers per lane; 474 MB under a stated heap cap; compare to baseline | **yes** | see the table below. Cap stated and reproduced: `--max-old-space-size=64` |
| 9 | README updated with new numbers and an honest limits extension | **NO — see F-B** | the four-lane table, the identity section and the 17-item limits section are all pre-repair. Six specific stalenesses, one of which states the reversed schema position as established fact |
| 10 | generated artifact records provenance: repo, commit, date, target cluster | **yes, and improved** | `CONTRACT_PROVENANCE` now names **one** cluster (`prod-eu`) instead of a union, plus four separately-attributed sources. `ok 116`, `ok 117`, `ok 118`, `ok 119`, `ok 120` — after the 22:34 re-pin. Before it, criterion 10's own test was the suite's one failure |

### Criterion 8, measured by me rather than read

| Measurement | `execution_notes.md` | My run | Verdict |
|---|---|---|---|
| Tests | 118 pass / 0 fail | **128 pass / 0 fail** (was 117/1 at 22:33) | superseded by the re-pin |
| Two concurrent `npm test` | both pass | **both 128/128, exit 0** | reproduced |
| Typecheck, `src/` | clean | **clean, exit 0** | reproduced |
| Typecheck **including `tests/`** | "clean with tests included" | **clean** — config in the scratchpad, repo unmodified | **now reproducible**; verifier-1 could not reproduce this |
| 474 MB lane under `--max-old-space-size=64` | completes, 93 MB/s | **completes, 5.0s, 94 MB/s, peak RSS 184 MB** | reproduced |
| 474 MB records / rows / withoutCve | 246,212 / 246,129 / 83 | **246,212 / 246,129 / 83** | exact |
| Same `--instance-id`, different wall clock | identical digest | **identical**, `199f28d4…` twice | reproduced |
| Different `--instance-id` | different digest | **different**, `a91076e1…` | reproduced |
| Lab pair audit | 153,878 / 92,251 / 0 collisions | **153,878 / 92,251 / 0** | exact |
| Prod pair audit | 17,486 / 8,079 / 0 | **17,486 / 8,079 / 0** | exact |
| Tenable fan-out / withoutCve | 3.072 / 629 | **3.072 / 629** | exact |
| Peak RSS, `--pair-audit` on 474 MB | 386 MB (README says 386–484) | **382 MB** | **verifier-1's 484 MB does not reproduce either.** Three runs now read 386 / 484 / 382. The README's range is the honest form; treat 484 as the outlier |
| Prod lane MB/s | 76 | **64** | 16% below; machine load. README should read this as a range too |
| Falcon assets MB/s | 45 | **37** | same |
| Enum rejects, all four lanes | none | **none on all four** | reproduced |

Both of verifier-1's number corrections **did land** in the README (385–409 MB/s at line 59;
386–484 MB at line 135). Its third — the `enum.prisma:79,89` citation — **did not**.

## Findings

### F-A — The REVERSAL is right in outcome and wrong in evidence

`decisions.md` states: *"The six 'missing' cloud columns are not a stale dump — they are UNDEPLOYED.
Verified across every committed cluster."*

That is not verified, and cannot be from these artifacts:

```
prod-eu snapshot   Generated 2026-07-10 21:05:17+00     22 asset / 20 exposure columns
stg     snapshot   Generated 2026-07-12 09:36:07+00     23 asset / 21 exposure columns
migration 20260729120100_add_cloud_columns_to_parser_output_assets   dated 2026-07-29
```

The migration is **17–19 days newer than both snapshots**. Their silence about the six columns is
exactly what a snapshot taken before a migration looks like — indistinguishable from undeployed. The
submodule's own skill says this in as many words: *"after writing a migration here, the matching
snapshot won't reflect it until (a) the migration is deployed to a cluster and (b) you regenerate
that cluster's snapshot… a mismatch means a migration is pending."* **Pending**, not undeployed.
Today is 2026-09-03, so both snapshots are ~8 weeks old and every one of the 129 files carries a
Generated line from 2026-07-10 or 07-12.

What the snapshots *do* establish, and what I confirmed:

- The 22/20 and 23/21 column sets, and that they differ by exactly `batch_id` — a genuine
  per-cluster difference that predates both snapshots and is therefore real drift.
- That the contract's 22 columns all existed on prod-eu as of 2026-07-10.
- That `rfqa` and `prod-us` — named by the skill — have **no committed snapshot at all**, so
  "every committed cluster" is two of four.

**Why the outcome is still right.** A contract naming 22 columns all known to exist beats one naming
29 of which 6 are unconfirmed, `assertColumnCoverage` now checks a set an INSERT could actually
meet, and nothing writes the six. Keep the retarget. Fix the claim: no committed artifact says
whether those six columns are deployed on prod-eu, and both C5 and the REVERSAL over-read the same
silence in opposite directions.

**`db-migration/SKILL.md` contradicts nothing here.** Its rule — every hand-written migration must
edit `prisma/schema/` in the same commit, because the schema is *"a quick reference for what the DB
looks like"* and not the source of truth — supports the reversal's hierarchy exactly. It adds one
nuance: Prisma carrying the cloud columns proves a migration was *written*, which is precisely why
it cannot prove one was *deployed*.

### F-B — `README.md` is behind the repair cycle; criterion 9 is not met

| Line | Says | Actually |
|---|---|---|
| 15 | "**Zero dependencies, no build step.**" | false — see F-E |
| 30, 201 | "107 tests" | **128** |
| ~121 | "**The committed pg_catalog dumps are stale.** … So the target column set comes from Prisma and only type *precision* from the dumps" | **the reversed position, stated as established fact.** The shipped contract takes its column set from the prod-eu snapshot and demotes Prisma to `intentSource` |
| ~181 | "**No TypeScript compiler is installed**, because the repo stays dependency-free. Typecheck runs out-of-tree" | false — I ran `./node_modules/.bin/tsc` in-tree, version 7.0.2 |
| `display_name` limit | comes from `$.remediation.entities[0].title`, "Present on 85%; the nulls are exactly the `closed` findings" | superseded — it is `$.vulnerability_id`, **0 nulls, 100% present, identical to `name`** |
| `(value, type)` limit | chain is `hostname -> fqdn -> current_local_ip -> parentKey`; "Whether the chain should reach `current_local_ip` at all is an open decision" | chain is `hostname -> current_local_ip -> parentKey` (`fqdn` removed); values are now lowercase (`dc01`, not `DC01`); and `execution_notes.md` closed the open decision — production does the same, documented at `crowdstrikeAssets.py:52-58` |

`caseFold`, `assertValueShapes`, the retarget, the dropped `severity` fallback and the `$localIp`
defect appear in the README **zero times** (grep count 0). Every one of them is the kind of thing
the "Deliberate limits" section exists to carry, and that section is otherwise the most honest
artifact in the repo.

### F-C — The production alignment never examined default values, the largest divergence class

Production sets a `default_value` on columns this slice leaves null. Measured by me on all 298 real
asset rows:

| Column | Null here | Production default | In EA's asset fingerprint |
|---|--:|---|---|
| `os_type` | **248 / 298 (83%)** | `"Other"` → `other` | yes |
| `os_version` | **248 / 298** | `"Other"` → `other` | yes |
| `os_build` | **248 / 298** | `""` | yes |
| `fqdn` | **286 / 298 (96%)** | `""` | yes |

Cause: 248 of 298 raw Falcon asset records carry no `platform_name` at all (`Windows` 37, `Linux`
12, `Mac` 1, absent 248), and the `os_type` mapping declares `require: ['platform']`.

The *behaviour* is deliberate and well-argued — `ok 43` pins it and its comment says why (*"Calling
an absent OS `other` would make an unmanaged entity indistinguishable from an odd one"*), and it
distinguishes absent (null) from present-but-unmatched (`other`, verified: `Solaris` → `other`). I
think the choice is correct.

The *divergence* is undocumented. `decisions.md`'s divergence table lists six items and none is a
default value. So a cycle that wrote 400 words about a 1-of-298 divergence left an 83% one out of
its own record. Asset row ids are unaffected (they carry no content term) and EA's demotion pair is
unaffected, so this is a documentation and product-parity gap, not a correctness defect.

### F-D — F1's mechanism is measurably overstated, and the alignment reduced fingerprint information

`execution_notes.md` F1 and the README both say Falcon's per-finding `$.id` inside
`additional_fields` *"is what actually guarantees zero collisions, not the semantic columns."* I
measured the counterfactual by hashing the other eleven fingerprint columns alone:

| Lane | Rows | Distinct on 11 columns (no `additional_fields`) | Distinct with it | Rows `additional_fields` rescues |
|---|--:|--:|--:|--:|
| Falcon lab | 246,129 | 239,039 | 246,129 | **7,090 (2.88%)** |
| Falcon prod | 25,565 | 25,492 | 25,565 | **73 (0.29%)** |

So 97.1% of lab distinctness and 99.7% of prod distinctness comes from the semantic columns —
mostly `first_seen`, `last_seen` and `mitigation`. `additional_fields` is genuinely *necessary* for
full distinctness, so F1 is a real finding; it is not the primary mechanism, and F1's **volatility**
concern, not its distinctness claim, is the part that survives.

A second measurement, which nothing records: **`name == cve_id` on 100% of rows on both lanes**
(246,129/246,129 and 25,565/25,565), because Falcon's `vulnerability_id` *is* the CVE id on this
lane. With `display_name` now also `$.vulnerability_id`, **three of the twelve fingerprint columns
carry the identical string.** Before the alignment `display_name` held the remediation title and was
distinct. The alignment therefore *reduced* the information in the fingerprint — correctly, because
it matches production, but that consequence is stated nowhere. **F2 is resolved in the sense that
matches production, and the column is now semantically empty.** F1 remains adequately documented as
a hazard; its magnitude claim needs the correction above.

### F-E — The repo is no longer dependency-free, and three artifacts still say it is

```
package.json      devDependencies: typescript ^7.0.2, @types/node ^26.4.1   (mtime 22:11)
package-lock.json 23 packages, present, NOT gitignored                       (mtime 22:11)
node_modules/     typescript, @typescript/*, @types, undici-types            (mtime 22:11)
./node_modules/.bin/tsc --version  ->  Version 7.0.2
```

`.gitignore` covers `node_modules/` but **not `package-lock.json`**, so the first commit will
include it. Contradicted by `constraints.md` ("No dependencies, no build step"), `README.md:15` and
`:181`, and `orchestration_plan.md` § Amendments ("Nothing is installed into the repo; `package.json`
and the absence of `node_modules` were both re-confirmed after running it"). **Unrecorded in
`execution_notes.md`** — line 90 still asserts "no `node_modules` in the repo, `package.json`
unchanged."

This is a defensible trade (an in-tree typecheck is worth more than the purity claim, and it made
the tests-inclusive typecheck reproducible for the first time), but it is a reversal of a stated
constraint that happened silently. `tsconfig.json` still has `include: ["src/**/*.ts"]`, so `npm`-level
typechecking still does not reach `tests/`.

### F-F — Smaller items

- **The `$localIp` blast-radius demonstration used synthetic hostnames.** `execution_notes.md` shows
  `IP-10-0-0-5`, `SHIPPING-01`, `EQUIP-7` collapsing to one `$localip` identity and calls it
  "reproduced through the real `mapLane` and the real mapping". The mapping and engine were real;
  **those three hostnames appear 0 times in the 298 real assets** (grep count 0). On real data
  exactly **1** asset matched `containsIgnoreCase('ip')`, so the emitted defect would have been one
  wrong value, not a three-way merge. The defect was real, the fix is right, and the severity is
  overstated by presenting a constructed case as a measurement.
- **`research/internal-recon.md` C5 still states the pre-reversal position** at line 675 with no
  forward pointer. Same for the `ACCEPTED RISK` heading at `execution_notes.md:266`. Leaving
  superseded records in place is right; neither carries a marker saying so, and verifier-1 built on
  the C5 chain.
- **`research/internal-recon.md` is detected as binary** (`file` says `data`; plain `grep` silently
  matches nothing without `-a`, from a stray non-printing byte near line 358). Low stakes, but a
  grep-checkable citation trail that needs `-a` is a trap for the next reader.
- The `enum.prisma:79,89` → `:80,90` correction from verifier-1 remains unapplied in
  `assumptions.md:58` and `constraints.md:55`.

## Fitness for the operator's stated purpose

**"I want it stable, it doesn't have to be perfect yet." Met.** 128 tests, green twice
concurrently, clean typecheck including `tests/`, four real lanes end to end, deterministic ids, and
the repair cycle closed two silent-wrong-value classes (`assertValueShapes`, the `$localIp`
literal). Stability improved measurably since verifier-1 — the two defects found then are the kind
that produce wrong data with no error, and both are now refused at load.

**"Prove it works and reason about how it scales." Met, and the reasoning is still the better half.**
474 MB through a 64 MB heap at 94 MB/s, peak RSS 184 MB, memory bounded by the largest line. The
extrapolation is in the terms that matter — 946.9 GB across 12,716 files, ~2.8 hours single-threaded,
fan-out available because records are independent. Scale is file count, not file size.

**"SCALES means 400-500 MiB of a file." Met directly.** 474 MB is inside the envelope; the largest
prod file is 165 MB.

**Not production grade, and does not claim to be.** `src/cli/map.ts` still has no test and produced
every number in this report. Nothing is committed. That is the right posture for the brief.

**No collector was run.** I ran none. All four datasets came from the on-disk scratchpad.

## Pipeline errors — is the recorded account accurate or self-flattering?

The six known ones are recorded, and I checked each against what I can see:

| Error | Recorded account | My read |
|---|---|---|
| Non-existent singular table in the contract | caught in planning, fixed in three files | **accurate** |
| Mis-scoped R3 | caught in execution, both halves in tests | **accurate**, and the 7 R3 tests exceed what was promised |
| Top-level-only array measurement (R5's 0%) | "it was my measurement error", re-measured at 99.3% | **accurate and unusually unflattering.** Names the cause precisely |
| Prisma-over-dumps inversion | REVERSAL section, "I had it exactly backwards" | **accurate about the inversion, self-flattering about the fix.** "Verified across every committed cluster" is not verified — F-A |
| `$localIp` identity merge | "the worst defect of the slice, and mine" | **accurate on cause and fix, overstated on blast radius** — F-F |
| Not reading the submodule's own skills | stated plainly | **accurate.** It is also the root cause of the previous two entries |

Four more that are still unrecorded, in descending order of consequence:

1. **The default-value divergence class** — 83–96% of asset rows, never examined (F-C).
2. **The repo stopped being dependency-free** — three artifacts still assert otherwise (F-E).
3. **F1's magnitude claim is wrong by ~34×** on the lab lane, and `name == cve_id` on 100% of rows
   means three fingerprint columns are one string (F-D).
4. **The `README.md` staleness itself** — criterion 9 was satisfied at 19:42 and then invalidated by
   the repair cycle, with no pass over it afterwards (F-B).

A pattern worth naming, because it recurred three times: **each of the three biggest errors came
from treating a derived artifact as the authority** — the C# prototype for the mapping, Prisma for
the deployed schema, a top-level field scan for nested content. The corrections all came from
reading the primary source. The one remaining instance of the same shape is F-A: a snapshot is
being read as an authority on a question it postdates.

## Action items, in the order I would take them

1. **Commit.** Zero commits, nine untracked paths, and an hour of this pass spent watching a file
   change under me. `package-lock.json` should be a deliberate include or an ignore, not an
   accident. Highest value per minute available, and it is verifier-1's item 1 still open.
2. **Correct the REVERSAL's claim** (F-A): the six columns are **pending, not proven undeployed**;
   the snapshots predate their migration by 17–19 days; "every committed cluster" is two of four.
   Keep the retarget. Add the Generated dates to `CONTRACT_PROVENANCE` so the artifact carries its
   own expiry.
3. **Do a README pass** (F-B). Six items, one of which currently tells a reader the opposite of what
   the code does. Include `caseFold`, `assertValueShapes`, the retarget, and 128 tests.
4. **Record the default-value divergence** (F-C) in `decisions.md`'s divergence table with the four
   row counts, and decide whether `os_type` should be `other` on 248 assets.
5. **Correct F1's magnitude** (F-D) in `execution_notes.md` and the README: 2.88% lab / 0.29% prod,
   and `name == cve_id` on 100% of rows.
6. **Get a test onto `src/cli/map.ts`.** Unchanged from verifier-1, and now more true: it produced
   every number in both reports.
7. **Apply the `enum.prisma:80,90` fix** in `assumptions.md:58` and `constraints.md:55`. Trivial,
   twice-flagged, and the whole discipline rests on citations being spot-checkable.
8. **Mark superseded sections as superseded** (F-F): `execution_notes.md:266` and
   `research/internal-recon.md:675`. One line each.
