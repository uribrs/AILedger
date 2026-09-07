# Phase 3 — evidence tests (W4)

Owner: W4 (evidence-tests). Wrote `tests/**` and one `package.json` script entry. Wrote nothing
under `src/` or `mappings/`; `src/cli/map.ts` has a newer mtime than this work because W3's writer
amendment (`FLUSH_AT`, `WRITER_HIGH_WATER_MARK`) landed during phase 3.

## State at hand-off

| Check | Result |
|---|---|
| Suite | **150 tests, 150 pass, 0 fail** across 9 files, ~300 ms |
| Two simultaneous `npm test` runs | both green — no shared mutable state, no destructive fixture |
| Repo typecheck (`tsconfig.json`, `src/**`) | clean |
| Tests typecheck (same options, tests included) | clean |
| Dependencies added | none; no `node_modules` in the repo |

The count moved five times after the orchestrator's 97-test verification: +3 for the R3 asymmetry
correction (finding 5), +4 net for the approved repair of findings 2 and 3 (see the **Repair**
subsections), +11 for the production-alignment mapping rewrite (finding 6), +10 for the contract
retarget from the Prisma superset to the prod-eu cluster snapshot, and +22 for the seven
code-review repairs F1-F8. Each time the failing tests were re-pinned to the new behaviour, never
weakened.

### Command

```
npm test
```

which is the single script entry added to `package.json`:

```
node --experimental-strip-types --test 'tests/**/*.test.ts'
```

### Files

| file | tests | R-id |
|---|--:|---|
| `tests/enum-validation.test.ts` | 13 | **R1** — all 13 tagged, two of them for F7's distinct-value bound |
| `tests/identity.test.ts` | 12 | **R2** (2), **R3** (7), criteria 3 and 7 (3) |
| `tests/mapping-validate.test.ts` | 47 | **R4** (5), load-time discipline (15), `assertValueShapes` (6), `caseFold` refusals (3), undeployed-column tolerance (5), F2 element kinds (5), F3/F4 closed key sets (5), F5/F6 (2) |
| `tests/canonical-json.test.ts` | 11 | **R5** — all 11 tagged |
| `tests/mapping-engine.test.ts` | 31 | ordering trap, tags/group_names, CVE explode, coercion, `caseFold` (6), production alignment (2) |
| `tests/row-id.test.ts` | 11 | RFC 4122 vector, run scope, criterion 7 name layout |
| `tests/target-contract.test.ts` | 13 | criterion 10 provenance and cluster identity, `isUndeployedColumn`, column counts |
| `tests/end-to-end.test.ts` | 5 | criterion 1, real `readLane` over checked-in fixtures |
| `tests/timestamp-parsing.test.ts` | 7 | **F1** — the timestamp grammar and host-timezone invariance |

Support: `tests/helpers/harness.ts`, and two read-only fixtures `tests/fixtures/falcon-assets.ndjson`
(806 B) and `tests/fixtures/falcon-findings.ndjson` (2,390 B).

Every spec is a clone of one of the three real files in `mappings/`, edited only in the part under
test. Every manifest is one of the three real files in `manifests/`. No hand-built mapping spec and
no hand-built column contract anywhere in the suite.

---

## Finding 1 — R1's path had no prior coverage, and it holds

**Severity: positive result, not a defect.** This is the highest-value output of phase 3.

Enum rejections were zero on all four real lanes (474 MB Falcon findings, the prod-client findings
lane, Falcon assets, Tenable findings), so nothing on disk ever drove the rejection path. The R1
test is the first thing that has executed it. Driven through the real `mapLane`, the real
`firstEnumViolation` and the real `noteEnumRejection`, with the non-member constructed the way one
would actually arrive — a `recordField` routed straight into an enum column, which is legitimate at
load time because Tenable really does state `high` and `critical`.

What holds:

- **Counted.** The record produces no row; `stats.rejectedRecords` increments; `withoutCve` does not.
- **Attributable.** Column, offending value, enum type, `lineNumber` and `recordIndex` are all
  retained, and `describeEnumRejection` appends the enum's member list.
- **Non-fatal.** A rejection between two good records leaves both good records emitted.
- **Bounded in memory, exact in count.** 30 non-member records give `rejectedRecords = 30` and a
  per-value tally of 30, while `rejectionSamples.length` caps at 25.
- **`@map` spellings both ways.** `windows-server` and `active-directory` are accepted at the
  per-record gate and at the load-time literal gate; `windows_server` and `active_directory` are
  rejected at both, and the load-time message names the members because the cause is a near miss.
- **The documented null asymmetry.** A null enum value is passed through and is not counted, which
  matches the `os_type IS NULL OR` clause in `parsed-data.repository.ts:340-341`.
- **Both tables screened.** `enumColumnsOf` returns `type`/`os_type` on assets and
  `type`/`severity`/`status` on exposures; the set is asserted, so a column losing its enum type in
  a migration is visible.

No defect found on this path.

---

## Finding 2 — message shadowing in `assertMappingUsable` — FIXED

**Location:** `src/mapping/validate.ts:133-142` (the call order inside `assertMappingUsable`).

**Your reading is correct, and I confirm it.** This is a diagnostics-quality defect, not a
correctness one. Every one of the cases below is still rejected at load time, before any record is
read. Nothing gets through. What is wrong is only the reason reported: the check one layer down
already knows a shorter and more accurate answer, and never gets to say it.

`assertMappingUsable` runs `assertMappingSpec` → `assertDecisionSources` →
`assertFieldReadsPerColumn` → `assertMappingLoadable`. Putting `assertFieldReadsPerColumn` before
`assertMappingLoadable` is deliberate and correct for the absent-field case — it is what adds column
attribution to R4's message, and the module comment says so. It also shadows three messages in
`src/mapping/spec.ts`:

| shadowed message | location | what the author sees instead |
|---|---|---|
| vendor mismatch | `spec.ts:428-433` | `binds column 'cve_id' to record field 'cve', which lane manifest 'findings' does not carry`, followed by the whole 20-name Tenable field list |
| entity mismatch | `spec.ts:434-439` | `binds column 'value' to record field 'hostname', which lane manifest 'findings' does not carry` |
| flat-lane branch | `spec.ts:466-472` | `binds column 'fqdn' to parent field 'hostname' … The lane's parent fields are: .` — an empty list where `is flat - its layout declares no parentPath` exists |

The flat-lane branch is unreachable through `assertMappingUsable` for **every** parent read, not
just some: a flat lane's declared parent set is empty, so the per-column check always throws first.

### Is it worth fixing inside this slice? No. Record it as an accepted risk.

Direct answer, and I disagree with the framing that this is a cheap reorder:

1. **The obvious fix does not exist.** You cannot simply swap the two calls. If
   `assertMappingLoadable` runs first, it catches the absent field *without* column attribution, and
   finding 2 reappears mirrored — R4's better message becomes the shadowed one. The order is not
   wrong; it is right for the case it was chosen for.
2. **The correct fix edits a frozen shared surface.** Making the vendor and entity checks run first
   means extracting them out of `assertMappingLoadable` in `src/mapping/spec.ts` — W0's file, part of
   the phase-0 frozen surface that W2, W3 and W4 all bind to — and calling the extract first from
   `validate.ts`. That is a late change to the one thing three workers agreed on.
3. **The alternative fix is the thing `validate.ts` exists to avoid.** Adding a vendor/entity check
   directly in `validate.ts` creates a second authority on the same question, which its own module
   comment names as the failure mode: "the two would disagree the first time one is edited."
4. **The failure is loud, always, and self-diagnosing in seconds.** The likely trigger is a CLI run
   with mismatched `--manifest` and `--mapping`. The operator's next action is to look at their own
   two arguments, which are on the same command line.

So: your instinct to ship the known-worse message rather than churn a frozen module late is the
right call, and I would make the same one.

### REPAIR — FIXED, and my recommendation above was wrong

The orchestrator authorised a repair in `src/mapping/validate.ts` after this was written, and it
dissolves my objection rather than overruling it. W2 found a third option neither of us had:
**guard rather than duplicate.**

`assertFieldReadsPerColumn` now returns early on a vendor or entity mismatch, and separately skips
the parent half when the lane declares no `parentPath`:

```
if (spec.vendor !== manifest.vendor || spec.entity !== manifest.entity) return;   // guard one
if (!laneHasParent || declaredParent.has(name)) continue;                          // guard two
```

The comparison decides only whether to run a DIAGNOSTIC. The check and its wording stay in
`spec.ts`, so there is still exactly one authority on vendor identity and exactly one message. My
objection was to duplicating the comparison, and no comparison is duplicated — the diagnostic
stands down and the frozen check speaks. `assertValueShapes` was added last in the gate at the same
time, which is the repair to finding 3.

Two things I got wrong and one I had not seen:

1. **"The obvious fix does not exist" was too strong.** It is true that swapping the two calls does
   not work, and that is still worth knowing. But standing down conditionally is neither a swap nor
   a duplicate, and I did not consider it.
2. **My accept-as-risk recommendation is withdrawn.** The repair costs two guard clauses in a
   diagnostic pass, not an extraction from a frozen module, so the cost argument that drove the
   recommendation does not apply to what was actually done.
3. **Guard two fixed a genuinely broken message, not only a shadowed one.** The flat-lane path was
   printing `The lane's parent fields are: .` — an empty list with nothing after it. I recorded that
   as a shadowing symptom; it was a defect in the diagnostic's own output, and it is now gone.

Verified: `assertMappingUsable` reports `is for vendor 'crowdstrike-falcon' but lane 'findings' was
collected from 'tenable-io'` on a wrong-vendor mapping, the entity equivalent on a wrong-entity one,
and `reads parent field(s) 'hostname' but lane 'assets' is flat - its layout declares no
\`parentPath\`` on a flat lane. The absent-field path with column attribution — R4's common case —
is unchanged and still passes.

Re-pinned tests (all three renamed, since the old names encoded the shadowing):

- `R4: a parent read against a FLAT lane is rejected, and the message says the lane is flat`
- `a mapping applied to the wrong vendor lane is rejected, and the message names the vendor` — also
  asserts the per-column diagnostic does **not** fire, which is the guard itself under test
- `a mapping applied to the wrong entity lane is rejected, and the message names the entity`

Each still calls `assertMappingLoadable` directly afterwards, but the comment now states the real
reason: both entry points produce the same text, which is what distinguishes standing down from
re-implementing the comparison. The anti-shadowing rationale is gone from the comments.

---

## Finding 3 — `postgresTextArray` into a real `text[]` column is a silent wrong value — FIXED

**Location:** the `isArrayColumn` branch of `coerce` in `src/mapping/engine.ts`.

**Severity: was real, bounded by requiring an authoring mistake; never triggered by a shipped
mapping. NOW FIXED — see the Repair subsection.**

The `postgresTextArray` kind renders a string — `{g1,g2}`. Coercion for a `text[]` column runs
`asStringList`, which wraps a scalar. So binding `group_names` to `postgresTextArray` produces
`['{g1,g2}']`: a one-element array whose single element is the literal text. Wrong value, no error,
no count. The engine cannot distinguish it from a legitimate one-element list, because encoding is
chosen by the column's declared Postgres type and the value really is one string.

The reverse direction **is** caught: a real `stringList` into the `text` `tags` column raises
`MappingError` — `is text and the mapping produced a array, which has no text form`.

The asymmetry is what makes this worth recording. The plausible authoring mistake is to reach for
`postgresTextArray` for every list-shaped column, having learned it from `tags` — and that is the
direction that fails silently. The correct direction fails loudly.

All three shipped mappings were and remain correct: `tags` uses `postgresTextArray`, and
`group_names`, `site_names` and `ip_address` use `stringList`.

### REPAIR — FIXED

The orchestrator authorised `assertValueShapes(spec)`, which runs last in `assertMappingUsable` and
rejects **both** directions at load time:

- `postgresTextArray` may not feed an array column — the silent double-encoding above.
- `stringList` may only feed an array column or a `jsonb` one. `jsonb` is exempt because storing a
  JSON array there is legitimate and the engine passes it through untouched.

Only positions that can BE the column's value are examined: the value itself, and recursively the
branches of a `firstNonBlank`, since any branch can win outright. A `postgresTextArray` nested under
`stringList`, `lookup`, `join` or `decide` is that kind's input rather than the column's value and
is left alone — which is exactly the shape the shipped `tags` mapping uses,
`postgresTextArray(stringList(...))`, so a stricter rule would have rejected real production data.

My earlier recommendation to leave the code stands corrected: I judged the fix as a `spec.ts` change
and it landed in `validate.ts` as a self-contained pass, which is materially cheaper than what I
priced. The direction-1 message also carries the worked example (`['g1','g2']` emitted as
`["{g1,g2}"]`), so the author sees what would have happened rather than a rule citation.

New coverage in `tests/mapping-validate.test.ts` — six tests, since this was new production code
with none:

- `assertValueShapes: postgresTextArray may not feed ANY of the real array columns` — all three
  `text[]` columns, not only the one this finding used
- `assertValueShapes: stringList may not feed a plain text column` — `tags`, `value`, `os_version`,
  plus that the `tags` message names the column's own `'{}'::text` default
- `assertValueShapes: stringList into a JSONB column IS allowed - the exemption is deliberate` — on
  both tables' jsonb column, so the exemption is on the TYPE and not on one column name
- `assertValueShapes: only positions that can BE the column value are examined` — nesting under
  `stringList`, `lookup` and `join` allowed; the same nesting under `firstNonBlank` rejected
- `assertValueShapes: the shipped falcon-assets mapping is exactly the exempt shape`
- `all three shipped mappings pass the FULL load-time gate, value shapes included`

Re-pinned in `tests/mapping-engine.test.ts`:

- `tags and group_names are NOT interchangeable, and BOTH directions fail at load time` — renamed,
  because the old name encoded the defect
- `the stream-time refusal of an array in a text column is still reachable and still holds` — new.
  `coerce`'s refusal is still the backstop and still the one carrying a line number: a `recordField`
  can put a vendor array in front of a `text` column, since the manifest describes only that the
  field exists and not its per-record shape. So the stream-time guard is not dead code.

---

## Finding 4 — the briefed test command does not work

**Severity: tooling. Corrected; no code involved.**

`node --experimental-strip-types --test tests/` fails with
`Error: Cannot find module '/Users/user/Dev/Uri/localprojects/AdapterDataNormalizer/tests'` on Node
22.17.0. The test runner's directory discovery does not match `.ts` files, so the path is handed to
the module loader instead. The quoted glob form, which Node expands itself, works:

```
node --experimental-strip-types --test 'tests/**/*.test.ts'
```

That is what the `test` script contains. Adopted by the orchestrator.

---

## Finding 5 — R3 is asymmetric across the two lanes

**Severity: brief correction, not a code defect. Both halves now tested.**

The original brief stated R3 as "claiming a previously-unclaimed field changes every derived id in
the lane". That is true for the **exposure** lane and false for the **asset** lane, because the two
lanes derive their ids from different names:

```
asset row id    = deriveRowId(instanceId, parentKey)
exposure row id = deriveRowId(instanceId, exposureKey(parentKey, cveId, content))
```

There is no content term in the asset name at all — `mapLane` takes the no-explode branch for a
table that does not declare `cve_id` — so claiming a field changes that asset row's
`additional_fields` value and leaves its id byte-identical. The row still changes, so a
re-normalization is an update to the same row rather than a new generation of rows. On the exposure
lane `additional_fields` is one of the ten `CONTENT_COLUMNS`, so the same edit re-mints every id.

Confirmed by test at small scale, matching the orchestrator's measurement of 298 unchanged Falcon
asset ids. Tests in `tests/identity.test.ts`:

- `R3: claiming a previously-unclaimed field changes the derived id, and the change is visible` —
  the exposure half, under the plan's own name. Two specs that write byte-identical values into
  every column; the second merely *reads* one more field (`confidence`, as a last-resort source for
  `name` that never wins). Under `carry: 'unclaimed'` that read alone moves the id.
- `R3: an ASSET row id has no content term, so claiming a field leaves it BYTE-IDENTICAL` — the
  asset half. Also asserts the id equals `deriveRowId(INSTANCE_A, 'aid-1')` positively.
- `R3: the asset table declares no cve_id, which is why its ids carry no content` — the structural
  reason, so the asymmetry does not have to be inferred from two ids.
- `R3: claiming is per TOP-LEVEL field, so a nested object is never partly carried` —
  `claimedFields` is `collectFieldReads(spec).record`, top-level names only, so `$.cve.id` claims
  all of `cve`. Asserted with a `cve` carrying an unread leaf, so the assertion is not vacuous. No
  test assumes a partial claim of a nested object is expressible.
- `R3: the 'wholeRecord' carry rule cannot suffer the claim shift` and `R3: the three shipped
  mappings all carry wholeRecord, so none is exposed to the shift` — the protection, and a tripwire
  on flipping it.

The absence of a production warning for this is **not** reported as a finding: detecting it would
require the previous run's claimed-field set, which this stage does not hold and the streaming
constraint does not want.

---

## Success criteria NOT covered by tests

Named explicitly for the verifier pass. An honest gap is more useful than a claim of full coverage.

| criterion | test coverage | what is missing, and where the evidence actually lives |
|---|---|---|
| 1 — both Falcon lanes and Tenable produce rows, Tenable with zero engine edits | **partial** | Row production is covered end to end (`tests/end-to-end.test.ts`) and the Tenable mapping loads and maps through the same engine. **The zero-engine-edit claim itself is not tested** — it is a file-diff/mtime fact, verified in phase 2 and recorded in `execution_notes.md`. No test can establish it. |
| 2 — a mapping reading an absent field is rejected before any record is read | **covered** | R4, five tests, including the reader that throws if iterated at all. |
| 3 — ids byte-identical across runs with the same `instance_id`, different across different ones | **covered** | Both directions, at the derivation (`tests/row-id.test.ts`), through `mapLane` (`tests/identity.test.ts`), and through the whole chain (`tests/end-to-end.test.ts`). |
| 4 — exposure identity composes canonical content; report repeated `(parentKey, cve_id)` pairs | **partial** | The **mechanism** is covered: two findings on one host and one CVE stay two rows with different ids. The **magnitude** is not re-measured — 37.5% (lab) and 31.6% (prod client) come from the phase-2 `--pair-audit` runs in `execution_notes.md`, not from a test. |
| 5 — `created_at` exclusion demonstrated, not just coded | **covered** | R2, plus the chain-level version in `tests/end-to-end.test.ts`. |
| 6 — enum validation rejects a non-member, accepts the `@map` spellings; report per-lane reject count | **partial** | Rejection and acceptance are covered exhaustively (R1, 11 tests). The **per-lane reject count against real data** — zero on all four lanes — is a measurement in `execution_notes.md`, not a test. |
| 7 — the twelve fingerprint columns produced; `asset_id` precedes the content hash | **covered** | `CONTENT_COLUMNS` asserted as ten names in table order, plus `asset_id` and `cve_id` present and non-null on emitted rows; the version-5 name layout asserted directly, including that swapping parent key and content changes the id. |
| 8 — measured records/s, MB/s, peak RSS, 474 MB lane under a heap cap | **NOT COVERED AT ALL** | Deliberate. A test would have to read 474 MB, which would make the suite unrunnable in CI and prove nothing new. Every number is in `execution_notes.md`. |
| 9 — `README.md` updated with the new numbers and an honest limits section | **NOT COVERED** | No test reads `README.md`. The main thread owns that file. |
| 10 — the generated enum artifact records its provenance | **covered** | `tests/target-contract.test.ts` asserts source repository, a full 40-character commit hash, a parseable generation date, and each of the four named sources. Plus, after the retarget: that `cluster` is one of the two committed clusters and is a SINGLE value not a union, that `columnSetSource` names that same cluster, that every `prismaOnlyUndeployed` entry is table-qualified and names a column the contract does **not** declare, that `isUndeployedColumn` is table-qualified, and that the per-table column counts match the snapshot they came from. |

### Other properties with no test

- **The streaming / no-lane-buffering constraint.** Measured only (474 MB lane under
  `--max-old-space-size=64`). No test asserts that nothing accumulates across records.
- **`src/cli/map.ts` entirely** — argument parsing, `--pair-audit`, the id digest, and W3's new
  megabyte-chunking writer with its `FLUSH_AT` / `WRITER_HIGH_WATER_MARK` reasoning. The writer
  change is the newest code in the repo and has no test.
- **The "engine holds no vendor knowledge" check.** Not reproduced as a test: `engine.ts` cites
  `falcon` and `crowdstrike` in two comments, so a naive grep fails and a real one needs a comment
  stripper. W2's version (192 field names across three manifests, word-boundary matched) was more
  rigorous than a test would be; it is a one-off, not a regression guard.
- **`src/target/generate-contract.ts`** — the generator itself. The tests assert the generated
  artifact's shape, not that regenerating reproduces it.
- **The reader's own grammar and error paths** — `resolveRecordPath`, `resolveKeyPath`,
  `LaneContractError`, the unknown/absent field tallies. Exercised only incidentally through
  `tests/end-to-end.test.ts`. Outside W4's brief; the reader is the pre-existing measured baseline.

---

## Finding 6 — BLOCKING: `decide.then` is a literal, so the rewritten `value` mapping emits `$localip`

**Location:** `mappings/falcon-assets.json`, the `value` column. **Severity: wrong data reaching the
target, silently, on the column that IS half of EA's asset identity. Not pinned by any test — this
is reported for repair, not documented as behaviour.**

The production-alignment rewrite encodes "prefer hostname, but take `current_local_ip` when the
hostname is itself an IP" as a `decide` rule whose `then` is `"$localIp"`:

```json
{ "when": [{ "source": "hostname", "test": "containsIgnoreCase", "value": "ip" }],
  "then": "$localIp" }
```

`ValueSpec` has no mechanism for a rule result that references a source. `DecisionRule.then` is
`string`, and `evaluate`'s `decide` branch in `src/mapping/engine.ts:583` is
`return rule.then` — the literal, verbatim. There is no `$`-dereference anywhere in the engine.

Reproduced against the real mapping and the real manifest through `mapLane`:

| record | emitted `value` | intended |
|---|---|---|
| `hostname: 'IP-10-0-0-5', current_local_ip: '10.0.0.5'` | **`$localip`** | `10.0.0.5` |
| `hostname: 'SHIPPING-01', current_local_ip: '10.0.0.6'` | **`$localip`** | `shipping-01` |
| `hostname: 'WEB-01', current_local_ip: '10.0.0.7'` | `web-01` | `web-01` |
| `current_local_ip: '10.0.0.8'` (no hostname) | `10.0.0.8` | `10.0.0.8` |
| neither | `aid-1` (parent key) | `aid-1` |

Two independent problems, and the second is what makes the first serious:

1. **The rule result is a literal.** `$localIp` is emitted as the text `$localip` after the fold.
2. **`containsIgnoreCase: 'ip'` matches far more than IP-shaped hostnames.** `SHIPPING-01` matches,
   as would `EQUIP-*`, `PHILIPS-*`, `CLIPPER-*`, `MULTIPATH-*`. Every matching host emits the SAME
   `value` (`$localip`) and the same `type` (`host`), so EA's `(value, type)` identity pair
   **collapses all of them into one asset identity**. That is an identity merge across distinct
   hosts, not merely a wrong string.

Nothing catches it: `value` is plain `text`, nullable, and `$localip` is non-blank, so there is no
coercion error, no enum rejection, and no counted outcome. `decisions.md` records the branch as
"low-impact — only 1 of 298 assets has 'ip' in its hostname", which is a measurement of this batch;
the substring test is broad enough that other batches will hit it more often.

**Not re-pinned deliberately.** The three failures I was asked to fix are all elsewhere, and my
fixture hostnames (`WEB-01`, `WEB-02`) do not contain `ip`, so the suite is green without touching
this. Writing an as-behaves pin asserting `value === '$localip'` would have to be reverted the
moment the mapping is fixed, so the finding is reported instead.

**Options, for the operator:** either add a source-reference capability to `DecisionRule.then`
(new spec surface, load-time checkable), or drop the branch and accept
`firstNonBlank[hostname, current_local_ip, parentKey]` — which diverges from production for the one
measured asset whose hostname contains `ip`. If the branch is kept in any form, the `'ip'` substring
test needs narrowing regardless; an IP-shaped hostname is not "contains the letters i and p".

---

---

## Phase 3 addendum — the seven code-review repairs F1-F8

Each was verified against the running code before its tests were written, because a test written
from a description pins the description. Findings, not re-reported as defects: all seven repairs
hold, and no eighth error was found.

**F1 — host timezone in the row id.** The one with the widest blast radius. `new Date(text)` parses
an offset-less timestamp in the HOST zone, and `first_seen` / `last_seen` are two of the ten
exposure content columns, so `TZ` reached the id. Covered by `tests/timestamp-parsing.test.ts`,
which owns the process's `TZ` and nothing else — its own file, because the mutation is global.
Seven tests: invariance across UTC, Asia/Jerusalem, America/Los_Angeles and Pacific/Kiritimati
(spanning the date line, 21 hours apart); a companion test proving a naive `new Date` really is
zone-dependent, so the invariance test cannot become vacuous; whole-row id invariance rather than
one column; thirteen accepted vendor spellings; nine refused ones.

**F1 bonus — February 31st.** Confirmed on raw V8: `new Date('2026-02-31T08:00:00.000Z')` returns
`2026-03-03T08:00:00.000Z`. ECMA-262 requires NaN; V8 rolls surplus days forward, so an impossible
date became a plausible wrong one three days out, inside a content hash. Pinned both directions,
and the leap rules with it: `2024-02-29` and `2000-02-29` accepted, `2026-02-29` and `1900-02-29`
refused. The last is worth noting — `isRealDate` resolves through `Date.UTC(year, month, 0)`, so the
divisible-by-100-not-400 century rule comes free, where a hand-written month-length table would
have got 1900 wrong.

**F2 — `assertElementKinds`.** Refuses a list built from an object array, which the manifest already
knew. Five tests: the object-element refusal through both `stringList` and `postgresTextArray`; the
string-element acceptance across four real fields, since a stricter rule would reject the shipped
mappings; the absent-`elementKinds`-is-unknown exemption on the four Tenable fields the sample only
saw empty; the single-segment boundary, where a path INTO the objects is the fix the message
suggests and therefore has to load; and that a bare scalar into a text column is not this check's
business.

**F3 / F4 / F8 — closed key sets and per-kind payloads.** Five tests. `casefold` for `caseFold` is
refused naming the key and the known set; `caseFold` misplaced one level down inside the value
object is refused; unknown keys are refused at the top level and inside an exclusion; `note` is
confirmed a known column key, which is why the shipped mappings load at all — they carry notes that
previously passed only because nothing looked. On payloads: `join` without `separator` (which used
to comma-join silently, `undefined` reaching `Array.prototype.join`), `decide` without `sources` or
`rules`, and `lookup` without `table` — all of which were bare TypeErrors thrown mid-record. Plus
the two boundary decisions: `separator: ''` loads because the check is for PRESENCE not truthiness,
`stringList` over no sources loads because the empty list is a real value for an array column, and
`firstNonBlank` / `join` over no sources are refused because they can only ever yield nothing.

**F5 — a mapping binding a derived column.** Refused for `id`, `instance_id` and `created_at` on the
asset table and `asset_id` on the exposure table. Note for the record: the refusal lives in
`planMapping` in `engine.ts`, not in `assertMappingUsable` — a probe through the validator alone
shows the mapping loading, which is why the test drives `mapLane`.

**F6 — an excluded nullable column with a default.** Stronger than "no longer writes explicit NULL":
the three `text[]` columns may no longer be left to their `ARRAY[]::text[]` default at all, because
NULL and `{}` are different strings inside the boundary's `md5(ROW(...))`. The test also pins the
boundary — a nullable column with NO default (`os_build`) is still legitimately excludable, so the
rule is about a default being overwritten and not about nullability.

**F7 — unbounded distinct rejection values.** The gap was real and it was in the old test, which sent
one value thirty times and so grew the map to a single entry however many records arrived. Replaced
by two tests: 150 distinct values, asserting the map stays at or under the cap plus one overflow
entry while `rejectedRecords` is exactly 150 AND the per-value tallies sum to 150 — so the bound
costs spellings, not counts; and that a value already retained keeps being tallied on its own entry
once the map is full, so a lane whose first hundred spellings recur never reaches the overflow label
at all.

**The one test that failed** was the jsonb exemption case, and it failed correctly: it built the
allowed case from `$.data_providers`, which the Falcon findings manifest declares as an array of
objects. Rebuilt on `parentField $.groups` — the Falcon findings RECORD declares no string-element
array at all, so the elements come from the correlated envelope's parent, which is a legitimate read
on a lane that declares `$.host`.

---

## Note on the repair cycle

Findings 2 and 3 were both written as pinned-as-behaves tests, and both were repaired afterwards by
the worker that owns `src/mapping/`. Four tests then failed — they were asserting the old text and
the old defect — and were re-pinned to the new behaviour, with the old rationale removed from their
comments rather than left citing a finding that no longer holds. No assertion was weakened to get
green, and the suite grew from 100 to 107.

A fifth cycle followed the seven code-review repairs; see the addendum above. One test failed and
was rebuilt on a valid source, and 22 tests were added. Two of my own test-authoring bugs surfaced
in the process, both caught by the new checks rather than by review: a `separator` passed to a
`firstNonBlank` payload (refused by the closed key set, correctly), and the earlier
`$.data_providers` source. Neither reached the suite.

A fourth cycle followed the contract retarget. The orchestrator had taken the Prisma models as the
column-set authority, reasoning that the pg_catalog snapshots were stale because six asset columns
appear in Prisma and in neither snapshot. The authority is the opposite way round, and it is stated
by a skill shipped inside the submodule
(`cybi-db-models/.claude/skills/db-schema-snapshots/SKILL.md`): the snapshots are ground truth read
straight from `pg_catalog`, and `prisma/schema/` can drift and is identical across clusters. So the
six columns are UNDEPLOYED, not missing from a stale file.

Independently verified before re-pinning, since the whole value of the retarget is that the
contract describes a real database: prod-eu has 22 asset and 20 exposure columns, stg has 23 and 21,
and the contract now declares 22 and 20 against `cluster: 'prod-eu'`. The difference between the two
clusters is exactly `batch_id` on each table, which is what makes the exclusion tolerance in
`assertColumnCoverage` necessary rather than convenient. `dumpVerified` is gone from `TargetColumn`
and from the generated file, which is correct: with every column read from pg_catalog the flag could
only ever be true, and a field that is always true is not a check.

Only one test failed - criterion 10's provenance assertions - and it was replaced by six, because
the honest disclosure inverted: the old `typeUnverifiedColumns` listed emitted columns no snapshot
corroborated, and `prismaOnlyUndeployed` lists columns the cluster does not have. Five more tests
cover the tolerance itself, including that it does not weaken typo detection and that MAPPING an
undeployed column still fails while EXCLUDING one is tolerated.

A third cycle followed the production-alignment mapping rewrite: three tests failed on the new
mapping behaviour and were re-pinned, and one HARNESS defect surfaced in the process —
`bindColumn` rebuilt a `ColumnMapping` as `{ column, value }`, so re-binding a value silently
dropped the new `caseFold: 'lower'` and made a test vary two things at once. It now spreads the
existing entry, and `setCaseFold` is a separate helper, so the two axes stay independent as
`ColumnMapping` grows.

That sequence is the test/implementation ownership split doing its job: behaviour was pinned, a
sibling changed it, and the tests reported the change instead of passing silently through it. It
also caught one thing the sibling did not intend — finding 6.

## Concurrency posture

Explicitly not the C# prototype's failure. No test writes a file, holds module state, or shares a
temp path; both fixtures are read-only and checked in; Node's runner isolates each file. Two
simultaneous `npm test` runs were executed and both returned 100/100. The suite is safe in CI and
safe for two people at once.
