# Execution Notes

Path: decompose. Phase 0 blocking, then two parallel workers, then mapping data, then tests.
Full decisions from phase 0 are in `notes-phase0.md`; this file is the integration record.

## Pre-execution corrections (orchestrator)

Four recon errors were corrected before any code was written, and are recorded in
`research/internal-recon.md` under "Orchestrator corrections" rather than edited out:

| id | Recon said | Actually |
|---|---|---|
| C1 | Every README number is unreproducible; re-baseline against a 309 MB set | The 474 MB lane exists at the briefed path and re-runs at **409 MB/s, 246,212 records, zero drift**. Criterion 8 keeps its reference. |
| C2 | The Falcon manifest is missing `apps` | `apps` is missing from the **data** too — 0 of 246,212 findings carry it, and the data's 13 field names are exactly the manifest's. The manifest is accurate; this batch has no `apps`. Strengthens R4 to a real negative case. |
| C3 | The Tenable manifest does not match its lane | It matches the scratchpad lane, zero drift. Recon probed a different, flat Tenable file under `IntegrationProbes` — a useful observation (a flat Tenable shape exists in the wild) but not a manifest defect. |
| C4 | Pin provenance at `e2ee9a302` | That is the outer `cymulate-exposure-analytics` HEAD. `cybi-db-models` is its own repo; the correct pin is `3d26e24e`. |

One recon finding was confirmed and changed a decision:

- **C5 / L5.** The committed pg_catalog dumps are ~3 weeks stale. `sub_type`, `cloud_platform`,
  `region`, `cloud_account_id`, `cloud_account_name`, `cloud_provider_url` exist in Prisma and in
  EA's own asset fingerprint but in neither dump; newest migration `20260731090100` postdates the
  prod-eu dump (2026-07-10). **Target column set now comes from Prisma; the dumps supply only
  `timestamp(3)` precision.** Recorded as decision drift in `decisions.md`.

Also corrected: the tables are `parser_output_**assets**_enrich` (plural). The singular name in the
original contract does not exist (recon L4). Fixed in `prompt_contract.md`, `constraints.md`,
`task.md`.

## Phase 0 — freeze-target-contract-and-mapping-schema (W0): complete

Files (all within its set, verified by mtime): `src/target/contract.generated.ts`,
`src/target/contract.ts`, `src/target/generate-contract.ts`, `src/ids/rowId.ts`,
`src/mapping/spec.ts`, plus `notes-phase0.md` (authorized separately).

Orchestrator re-verified independently rather than trusting the report:

| Check | Result |
|---|---|
| RFC 4122 published vector | `2ed6657d-e927-568b-95e1-2665a8aea6a2` — MATCH |
| `NAMESPACE` | `7f6b1f26-9a3e-5d41-8c0b-2a5f4d9e1b73` — verbatim |
| `KEY_SEPARATOR` | codepoint 31 = U+001F — correct |
| Same-instance id stability | stable |
| Different-instance id divergence | divergent |
| Blank `parentKey` | throws |
| Enum member counts | 23 / 15 / 12 / 5 / 3 — all match |
| `@map` values emitted | `windows-server`, `active-directory` present |
| `tags` | `text`, notNull, default `'{}'::text` — not an array |
| `group_names` | `text[]`, default `ARRAY[]::text[]` |
| `cve_id` | `text`, notNull |
| Provenance commit | `3d26e24ee429e9d9a55b20912c7d8c525df6d34c` |
| Column counts | assets 29 (22 dump-verified), exposures 21 (20 dump-verified) |

Beyond the brief, it recorded `typeUnverifiedColumns` naming the eight columns whose Postgres type
no dump could corroborate (the six cloud columns plus both `batch_id`). That is the honest position
and it is now machine-readable.

**The Stop Condition did not fire.** `ValueSpec` is a closed ten-member union — `recordField`,
`parentField`, `parentKey`, `constant`, `firstNonBlank`, `join`, `stringList`, `lookup`, `decide`,
`postgresTextArray` — with no `function` field and no expression string, so load-time checkability
holds for the whole spec. Two kinds and three redundant literal options were cut after writing all
three real mappings showed them unexercised (`notes-phase0.md` §5). Every remaining kind is
exercised by Falcon assets, Falcon findings or Tenable findings.

R4's mechanism is already proven at phase 0: a mapping declaring a read of `apps` is rejected with
*"reads record field(s) 'apps' that lane 'findings' does not carry"*, alongside nine other
rejections (parent read on a flat lane, non-member enum literal, uncovered column, blank exclusion
reason, typo'd exclusion column, wrong vendor, wrong entity, duplicate column mapping, malformed
path).

Two phase-0 decisions the orchestrator adopted over its own brief:
1. **Declared field reads are derived, not author-written.** `collectFieldReads(spec)` is the single
   statement of what a mapping reads. A hand-maintained `reads:` list is a second statement of the
   same fact, and the first forgotten nested read is a mapping that passes verification and then
   reads a field the lane lacks — the exact failure verification exists to catch.
2. **`require: ['platform']` means an absent OS yields nothing, not `other`.** Calling an absent OS
   `other` makes an unmanaged entity indistinguishable from an odd one.

## Typecheck — method established, one pre-existing bug found and fixed

`npx tsc` resolves to an unrelated abandoned package, not the compiler, and the repo must stay
dependency-free. Phase 0 solved it out-of-tree; adopted for the whole task:

```
S=<scratchpad>/typecheck            # npm install typescript @types/node, once
$S/node_modules/.bin/tsc --noEmit --typeRoots $S/node_modules/@types --types node
```

Run from the repo root against the repo's own `tsconfig.json`. Confirmed after running: no
`node_modules` in the repo, `package.json` unchanged.

It found exactly one error, pre-existing and from the reader stage rather than this task:
`src/cli/describe.ts:21`, where `Args` was
`Readonly<Record<string, string>> & { readonly _: readonly string[] }` — unsatisfiable, because `_`
cannot be both a string and a string array. It compiled only because nothing had ever type-checked
the file. The main thread fixed it (`Args` is now `named` + `positional`) rather than handing a
pre-existing defect to a worker, and re-ran both CLIs: `describe` reproduces the 298-row asset
manifest with 99 record fields, `probe` still reports `$.aid=49 $.id=249`.

## Operator constraints added mid-execution

- **Do not run the TenableIo collector** — large, and a fresh run is expensive. Tenable coverage in
  this slice rests on the 13 MB `tenableio-two-phase/first-001` sample already on disk: real vendor
  data, but one batch. This is reported as such, not as second-vendor parity.
- The supplied prod Falcon dataset is the reference: 12,716 findings files, 946.9 GB, avg 74.5 MB,
  max 165 MB, one 44 MB sample on disk.
- Stability over completeness. Gaps are recorded, not closed by widening scope.

## Phase 1 — identity-canonical-content (W1) and mapping-engine-and-enum-validation (W2): complete

Ran in parallel over disjoint sets. Typecheck clean repo-wide afterwards; no ownership violations.
Full decisions in `notes-phase1-rows.md` and `notes-phase1-mapping.md`.

Orchestrator re-verified: ordinal key sort (not `localeCompare`), array order significant, exactly
ten `CONTENT_COLUMNS` in order, `created_at` absent from them, content unchanged when only
`created_at` differs, `null` distinguished from `''`, and `engine.ts` free of vendor knowledge.

Three findings from these workers that outrank the brief:

1. **`mapLane` is deliberately not an async generator.** A generator's body does not run until the
   first `next()`, so validation written inside one happens *after* the caller starts iterating —
   "fails before any record is read" would be false at the call site while looking correct in
   review. A plain function that validates and then returns the generator is what makes it true.
   Verified independently: a bad spec throws without the reader being iterated at all.
2. **A `decide` rule's `source` name was unchecked.** `assertMappingSpec` resolves every path but
   never verified that a rule's `source` names one of that `decide`'s own sources, so a misspelled
   name is a condition that can never hold — a rule that silently never fires. `validate.ts` adds
   that check.
3. **A `Date` inside `canonicalJson` had to be a literal, not an object.** A `Date` has no own
   enumerable properties, so the object branch would canonicalise every nested instant to `{}` and
   two records differing only in a nested timestamp would collide.

Two deviations from the C# are documented and are decisions to keep, not debt:
- **Number literals are normalised here.** Input is `JSON.parse` output — an IEEE-754 double with
  no memory of its spelling — so `1.0` and `1.00` are one value by the time the module sees them.
  Beyond being forced, it is the safer direction: EA's own md5 runs over stored column values where
  the spelling is likewise gone, so the C# emits two rows for one value and relies on EA to collapse
  them.
- **Non-ASCII escaping.** .NET escapes as `\uXXXX`; `JSON.stringify` emits raw. Costs nothing —
  ids never cross implementations.

W2's vendor-knowledge check was more rigorous than briefed: the union of all 192 field names across
the three manifests, minus the 10 that are also target column names, word-boundary matched against
`engine.ts`. Only hits are `source`, `sources`, `state` — spec vocabulary. The falcon/crowdstrike
hits are comment citations at lines 13 and 19.

## Phase 2 — vendor-mapping-data-and-map-cli (W3): complete

Three mappings plus `src/cli/map.ts` (278 lines). Typecheck clean. **Zero engine edits confirmed by
mtime**: nothing under `src/mapping/`, `src/rows/`, `src/ids/`, `src/target/`, `src/reader.ts` or
`src/manifest.ts` changed while the Tenable mapping was added.

### Measured by the orchestrator, not taken from the worker's report

| Lane | Records | Rows | Bytes | Elapsed | MB/s | Peak RSS | Enum rejects |
|---|--:|--:|--:|--:|--:|--:|---|
| Falcon assets (flat) | 298 | 298 | 1 MB | 0.0s | 45 | 115 MB | none |
| Falcon findings, lab — **under a 64 MB heap cap** | 246,212 | 246,129 | 474 MB | 5.0s | 95 | 191 MB | none |
| Falcon findings, separate prod client | 25,570 | 25,565 | 42 MB | 0.6s | 76 | 252 MB | none |
| Tenable findings (second vendor) | 1,521 | 4,672 | 12 MB | 0.1s | 85 | 227 MB | none |

**The memory property survives the mapping stage.** The 474 MB lane completes under
`--max-old-space-size=64`, as the reader alone does.

**Throughput cost of mapping: ~4.3×.** Reader alone is 409 MB/s on that lane; the full chain is
95 MB/s. Not a regression of the reader — it is what the mapping stage costs. Extrapolated to the
operator's prod batch (946.9 GB): ~39 minutes single-threaded for the reader alone, **~2.8 hours for
the full chain**. Fan-out across the 12,716 files is what makes that tractable, and records are
independent so it is available.

**Criterion 3 and criterion 5, one experiment.** Two runs of the same lane with the same
`--instance-id`, separated in wall-clock time and with `--normalized-at` defaulting to now each
time, give a byte-identical id digest (`bdb8dacb8116…`). A different `--instance-id` gives a
different one (`38417221a58c…`). So ids are within-run stable, `created_at` is genuinely outside the
hash, and generation scoping works.

**Criterion 4, measured on this data rather than restated from the prototype.**

| Lane | Rows | Distinct (asset_id, cve_id) | Rows beyond first of pair | Distinct row ids | Collisions |
|---|--:|--:|--:|--:|--:|
| Falcon findings, lab | 246,129 | 153,878 | **92,251 (37.5%)** | 246,129 | **0** |
| Falcon findings, prod client | 25,565 | 17,486 | **8,079 (31.6%)** | 25,565 | **0** |

Keying on `(asset_id, cve_id)` alone would collapse 37.5% of the lab lane and 31.6% of the prod
lane. The content-composed identity keeps every row distinct with zero collisions. The prototype
measured 30.9% on a different batch; both figures here are independent confirmations, and the lab
figure is higher.

**CVE explode.** Falcon fan-out is 1.000 (one CVE per finding), with 83 findings carrying no CVE in
the lab lane and 5 in the prod lane. Tenable fan-out is 3.072, with 629 of 1,521 carrying none.
Findings without a CVE cannot produce a row, because `cve_id` is NOT NULL — they are counted as
`withoutCve`, not silently dropped.

**One honest limit in the CLI.** `--pair-audit` deliberately does not stream: it retains state per
distinct pair and per row id, so peak RSS rises from 191 MB to 386 MB on the 474 MB lane. It is an
opt-in diagnostic, documented as such in the module comment. Normal operation streams.

**Enum rejections were zero on every real lane.** So R1's tripwire is not exercised by any data on
disk, and the rejection path is unproven by this run. Phase 3 must construct a non-member value
deliberately.

## Orchestrator findings from inspecting emitted rows (not from any worker's report)

### F1 — Exposure identity is effectively keyed on the vendor's own record, via `additional_fields`

Inspecting a real emitted row shows `additional_fields` carries the whole vendor record — 12 of the
13 fields the Falcon findings manifest lists (`closed_timestamp` is absent only because it is null
on that record), *including* the fields already claimed by named columns (`cve`, `remediation`,
`status`, `vulnerability_id`, `created_timestamp`, `updated_timestamp`).

`additional_fields` is one of the ten `CONTENT_COLUMNS`. Two consequences follow, and neither is
visible from the mapping data alone:

1. **The zero-collision result is real but incidental.** Falcon's own per-finding `$.id` is carried
   inside `additional_fields`, and it is unique per finding. That is what guarantees 246,129
   distinct ids from 153,878 distinct `(asset_id, cve_id)` pairs — not the semantic columns. The
   measurement stands, but the mechanism should be stated: identity is distinct because the vendor's
   record identity is inside the hash, not because `name`/`display_name`/`severity` discriminate.
2. **Identity is as volatile as the rawest field carried.** Any change to any vendor field — a
   `confidence` value, an `updated_timestamp` — re-derives the id. In EA's generation model that
   means a new row and demotion of the previous one. Whether that is desirable is a real question:
   it is faithful re-collection semantics, and it is also maximal churn.

Worth noting the design is nonetheless *consistent with EA*: EA's own twelve-column fingerprint
includes `additional_fields`, so EA would treat those rows as distinct too. This reproduces EA's
rule rather than inventing one. But the sensitivity is a property of that rule which the prototype's
evidence never surfaced, because the prototype only ever ran one vendor and never varied a field.

Recorded as a finding for the verifier to dispose of, not repaired. Reducing the carry to unclaimed
fields only would change every id in every lane and is not a change to make inside this slice.

### F2 — `display_name` on the Falcon lane is a semantic substitution, and it sits inside identity

The C# sourced `display_name` from `finding.apps[0].product_name_version`, producing values like
"Google Chrome Enterprise 150.0.7871.46". `apps` is absent from the manifest and from the data (0 of
246,212 records), so W3 substituted `$.remediation.entities[0].title`, producing the imperative form
— confirmed on a real row: "Update Microsoft Edge (Chromium-Based)".

Present on 209,369 of 246,212 records (85%); the 36,843 nulls are exactly the `closed` findings,
which carry no remediation entity. This is honest and well-reasoned, and it is a **different kind of
value** from what the Spark parser produced for this column.

It matters beyond cosmetics: `display_name` is one of the twelve columns EA fingerprints. So this
substitution changes exposure identity relative to the old pipeline for the same vendor data. It
does not affect this slice's correctness — nothing here compares ids against the old pipeline's —
but it is a decision the operator should make knowingly rather than inherit, and it belongs in the
README's limits.

## Phase 3 — evidence-tests (W4): complete

**100 tests, 100 pass, 0 fail, ~210 ms.** Verified by the orchestrator, not taken from the report.
Two simultaneous `npm test` runs both returned 100/100 — the concurrency defect that made the C#
suite unrunnable in CI (fixture and host both TRUNCATEing the same tables) is not reproduced.
Typecheck clean with tests included. Nothing written outside `tests/` and one `package.json` script.

Every spec used in a test is a clone of one of the three real files in `mappings/`, edited only in
the part under test; every manifest is one of the three real files. No hand-built mapping and no
hand-built column contract anywhere — a tripwire assembled from a stand-in tests the stand-in.

R-id coverage: R1 11 tests, R2 2, R3 4, R4 5, R5 11. All five tripwires delivered and tagged.

W4 corrected the briefed test command: `node --experimental-strip-types --test tests/` fails because
Node 22.17's directory discovery does not match `.ts` files and tries to load `tests` as a module.
`npm test` uses a quoted glob, which works.

### ACCEPTED RISK — message shadowing in `assertMappingUsable` (validate.ts:133-142)

Not repaired, deliberately. W4 argued against my framing that this was a cheap reorder, and it is
right on four counts:

1. **Swapping the two calls mirrors the defect.** If `assertMappingLoadable` runs first it catches
   the absent field *without* column attribution, and R4's better message becomes the shadowed one.
   The order is right for the case it was chosen for.
2. **The correct fix edits the frozen shared surface.** Making the vendor and entity checks run
   first means extracting them out of `assertMappingLoadable` in `src/mapping/spec.ts` — the phase-0
   artifact that three workers bind to — late in the slice.
3. **The alternative creates a second authority.** A vendor/entity check written directly into
   `validate.ts` is the failure its own module comment names: "the two would disagree the first time
   one is edited."
4. **The failure is loud and self-diagnosing.** The trigger is a CLI run with mismatched
   `--manifest` and `--mapping`; the operator's next look is at their own two arguments on the same
   command line.

Mitigation already in place: the three shadowed messages are asserted verbatim via direct
`assertMappingLoadable` calls, with a comment at each site naming the finding, so the better text
cannot be deleted unnoticed and a future reader meets the explanation at the code.

**Disposition: accept for this slice; do the extraction in the next one, when `spec.ts` is no longer
frozen.**

### Criteria with measured evidence but no test coverage — W4's own accounting

| Criterion | Status | Where the evidence lives instead |
|---|---|---|
| 1 zero-engine-edit | partial | mtime/file-diff fact, phase 2. No test can establish it. |
| 4 magnitude (37.5% / 31.6%) | partial | `--pair-audit` runs. The mechanism IS tested. |
| 6 per-lane reject counts (zero on all four) | partial | Measurement. Rejection/acceptance IS tested, 11 tests. |
| 8 throughput, MB/s, peak RSS, heap cap | **none** | Deliberate — a test would read 474 MB, unrunnable in CI, proving nothing new. |
| 9 README | none | Main thread owns it. |

### Untested code — named so it is not mistaken for covered

- **`src/cli/map.ts` entirely**, including `--pair-audit`, the id digest, and W3's megabyte-chunking
  writer with its `FLUSH_AT` / `WRITER_HIGH_WATER_MARK` reasoning. **This is the newest code in the
  repo and has no test at all** — the largest single gap in the slice.
- **The streaming / no-lane-buffering constraint.** Measured only. No test asserts that nothing
  accumulates across records.
- **`src/target/generate-contract.ts`.** Tests assert the generated artifact's shape, not that
  regenerating reproduces it.
- **The "engine holds no vendor knowledge" check.** A one-off audit, not a regression guard; a naive
  grep fails because `engine.ts` cites falcon/crowdstrike in two comments.
- **The reader's own grammar and error paths.** Pre-existing baseline, outside W4's brief.

## Repair cycle — both W4 findings fixed; my accepted-risk record REVERSED

### Repair 1 — `postgresTextArray` into a real `text[]` column (was W4's finding 3)

Fixed. `src/mapping/validate.ts` 306 → 416 lines, exported surface unchanged, `engine.ts` untouched.
A new `assertValueShapes(spec)` runs last in `assertMappingUsable` and pairs each column's
`ValueSpec` kind against the column's `pgType`, using `isArrayColumn` / `isJsonbColumn` from the
frozen contract. Two rules that are one rule from both sides:

- `postgresTextArray` may not feed an array column.
- `stringList` may only feed an array column or a `jsonb` one — `jsonb` exempt because a JSON array
  there is legitimate and the engine passes it through untouched.

Only positions that can *be* the column's value are examined: the value itself, and recursively the
branches of a `firstNonBlank` since any branch can win. Nested under `stringList`, `lookup`, `join`
or `decide` it is an input to something else and is left alone.

Verified by the orchestrator against the original reproduction:

| | before | after |
|---|---|---|
| rejected at load time | NO | **YES** |
| `group_names` emitted | `["{g1,g2}"]` | never reached |
| counted rejection | 0 | never reached |

All three shipped mappings still load. The reverse direction (`stringList` into `tags`) is now also
caught at load rather than at stream time — the smaller win, but it matters: the stream-time version
needs a record that actually reaches the column, so on a sparse field thousands of records can map
before one trips it.

### Repair 2 — message shadowing. **My accepted-risk disposition above is WITHDRAWN.**

The entry recorded earlier in this file accepted the shadowing as risk on W4's recommendation. That
disposition is superseded: it is FIXED.

W2 found a third option neither W4 nor I had considered — **guard rather than duplicate.**
`assertFieldReadsPerColumn` returns early on a vendor/entity mismatch, and separately skips its
parent half when the lane is flat. The comparison decides only *whether to run a diagnostic*; the
check and its wording stay in `spec.ts`. So there is still exactly one authority and one message,
which was the entire objection. No extraction from the frozen surface, no second authority.

W4 withdrew its own recommendation on seeing the implementation, and corrected one of its own claims:
"the obvious fix does not exist" was too strong — swapping the two calls still does not work, but
standing down conditionally is neither a swap nor a duplicate.

Guard two also fixed a genuinely broken message rather than a merely shadowed one. The flat-lane
diagnostic was printing `The lane's parent fields are: .` — an empty list with nothing after it.
W4 had recorded that as a shadowing symptom; it was a defect in the diagnostic's own output.

### Tests: the isolation earned its cost

The repair broke exactly 4 of 100 tests. All four asserted the old, worse behaviour — one was named
`"...and only one direction is caught"`, encoding the defect in its own title. Nothing regressed:
the tests detected an improvement, which is the reason tests and implementation went to separate
owners.

Re-pinned and extended: **107 tests, 107 pass, 0 fail.** Typecheck clean. Two simultaneous runs both
107/107. Only `tests/` touched. Verified by the orchestrator, not taken from the report.

Six new tests cover `assertValueShapes`, including two the brief did not ask for and which are the
better tests:
- The `jsonb` exemption is pinned on **both** tables' jsonb column, so it is anchored to the *type*
  rather than to one column name.
- `the shipped falcon-assets mapping is exactly the exempt shape` — anchors the rule to real
  production data, so a stricter future rule cannot silently reject what ships today.

And one test that prevents a future mistake I had not anticipated:
`the stream-time refusal of an array in a text column is still reachable and still holds`.
`assertValueShapes` closes the `stringList` route into a scalar column, but a `recordField` can
still put a vendor array in front of a `text` column, because the manifest describes only that a
field exists — not its per-record shape. So `coerce`'s refusal remains the backstop and remains the
only one carrying a line number. Without that test someone would eventually delete it as dead code.

## Verifier findings, re-checked by the orchestrator

### V1 — R5's stated premise was FALSE, and it was my measurement error

`orchestration_plan.md`'s R5 row says "0 multi-element array fields in 52,000 sampled findings". I
measured **top-level fields only**. Re-measured, walking nested structures:

```
sampled 52,000 findings
TOP-LEVEL multi-element arrays: {}                          <- what I measured
findings with a multi-element array ANYWHERE: 51,654 (99.3%)
nested paths: cve.vendor_advisory 43,913 | remediation.entities 18,285 | cve.references 12,879
```

These sit inside `additional_fields` via the `wholeRecord` carry, and `additional_fields` is one of
the ten content columns — so array order **is** inside the id derivation for 99.3% of findings, not
0%. The verifier demonstrated it rather than inferring: swapping two elements of
`cve.vendor_advisory` on a real row moved the id from `c26dfb76-…` to `c2b31556-…`.

The code and the R5 tests are correct. The risk assessment attached to them was wrong, and it was
mine.

**Resolution, which is partly reassuring.** The upstream collector already sorts one of these three:
`FalconCorrelatedRecord.cs:76-97` canonically sorts `remediation.entities` by each entity's
serialized form with `StringComparer.Ordinal`. That is precisely the sort-at-emission recorded in
`lessons.md#L-c9239f9c`, and it is in place — so the 18,285 findings with multi-element
`remediation.entities` are order-stable at the source.

`cve.vendor_advisory` (43,913) and `cve.references` (12,879) are **not** sorted anywhere.

Why this is a narrow rather than broad risk: within one batch the bytes are fixed, so
canonicalisation is deterministic and reparse stability holds; across batches ids change by design.
The residual exposure is the same finding appearing twice **within one batch** with differently
ordered arrays, which would emit two rows for one finding.

The notable part is that the upstream collector already treated array-order determinism as worth
engineering, and covered one array. Our content hash now depends on two more that it did not.
Sorting arrays inside `canonicalJson` is NOT the fix — array order is legitimately significant in
general, which is why the C# preserved it. The fix belongs upstream (sort those two arrays as
`remediation.entities` already is) or in the mapping. **Not changed in this slice; recorded.**

### V2 — 11 of 298 Falcon asset rows share `(value, type)`, which is EA's demotion key

Confirmed by the orchestrator against real emitted rows:

```
asset rows=298  distinct ids=298  distinct (value,type)=292
5 pairs shared by >1 row, covering 11 rows:
  ('10.10.1.23','host') -> 3    ('DC01','host') -> 2    ('CROWDSTRIKE','host') -> 2
  ('RHEL-SPlunk','host') -> 2   ('WIN11-CROWDSTRI','host') -> 2
```

Row ids are 298-distinct, so A16 (parent-key uniqueness) holds. But `constraints.md` records that EA
demotes prior generations via `latest = false` keyed on **`(value, type)`**, and EA's asset identity
is that pair. So these 11 rows do not have 11 distinct identities downstream — three different hosts
would contend for the identity `('10.10.1.23','host')`.

Mechanism: the `value` mapping is `firstNonBlank(hostname, fqdn, current_local_ip, parentKey)`, so a
host stating no hostname falls back to its IP, and three hosts share `10.10.1.23`. Two hosts
genuinely share the hostname `DC01`.

This is an asset-identity collision at **EA's** boundary, not at ours — our ids are distinct and our
rows are well-formed. It was unrecorded anywhere before the verifier found it, and it is the kind of
thing that shows up as "assets mysteriously flapping between generations" long after this slice.
**Not changed; recorded.** Whether the fallback chain should reach `current_local_ip` at all is an
operator decision.

### V3 — two of my measured numbers did not hold on re-run

| Figure | I recorded | Verifier measured |
|---|---|---|
| `--pair-audit` peak RSS on the 474 MB lane | 386 MB | **484 MB** |
| Reader-alone throughput, same lane | 409 MB/s | **385 MB/s** |

Both are single-run figures on a machine doing other work, and neither changes a conclusion: the
pair audit still does not stream (which was the point of recording it), and mapping still costs
roughly 4× the reader. But the specific numbers should be read as approximate, and the README's
409 MB/s and 386 MB figures are the optimistic end of the range rather than a stable measurement.

## The mapping was anchored on the wrong artifact — orchestrator error

The operator challenged the reported `display_name` gap with three questions: does the current
parser not resolve it, does the data not teach the mapping, and why is there a gap. All three land.

**What went wrong.** I anchored the vendor→target mapping on the C# prototype's
`ThinFalconCollector/Labels/FindingLabeler.cs`, which sources `display_name` from
`apps[0].product_name_version`. Production sources it from `vulnerability_id`
(`crowdstrikeAssetsFindings.py:73`) — the same field as `name`, duplicated deliberately. The
prototype invented its own mapping; production prunes `apps` entirely as a heavy field. So the
"gap" I reported was a prototype artifact that I treated as a data problem and then worked around
by substituting `remediation.entities[0].title`.

**Why the data could not have caught it.** The manifest correctly reported that `apps` was absent,
and the load-time check correctly rejected reading it. That machinery worked. But a manifest
describes which fields exist and their shapes — it cannot say which target column a field belongs
to. `vulnerability_id` and `remediation.entities[0].title` are both present and both plausible for
`display_name`. Only the parser settles it.

**Why the process did not catch it.** I read `cymulate-integration-parsers` for EA's *behaviour* —
correlation, the dedup fingerprint, enum casts, the silent row drop — and cited it repeatedly. I
never read it for the *mapping*. The one artifact that is authoritative about vendor→target mapping
was the one artifact I did not consult for that purpose, while consulting it for everything else.
Recon was not asked for it either; my brief asked what to port from the C# prototype, which framed
the prototype as the source of truth before any evidence supported that.

**One thing this already resolves.** The 11 asset rows sharing `(value, type)` are not a defect
introduced here. Production falls back to `current_local_ip` too, and documents the reason at
`crowdstrikeAssets.py:52-58`: "Unmanaged/unsupported passive-discovery hosts carry only an IP;
without this fallback their value is null and BaseParser.post_process drops them. Keeping the
fallback here ensures no discovered asset is omitted." IP-as-value is a deliberate production
trade-off — asset retained over asset dropped. V2 needs no operator decision after all.

**Divergences the first comparison already exposed** (full audit in progress across all three repos):

| Column | Production | This slice | Note |
|---|---|---|---|
| findings `display_name` | `vulnerability_id` | `remediation.entities[0].title` | **wrong** |
| findings `status` | `open`/`resolved`/`reopened`, default `open` | `opened`/`resolved`/`reopened`, no default | production's `open` is not an enum member |
| assets `value` | `coalesce(when(hostname contains 'ip', current_local_ip).otherwise(hostname), current_local_ip)` | `firstNonBlank(hostname, fqdn, current_local_ip, parentKey)` | no `fqdn` in production; we lack the contains-'ip' rule |
| assets `os_type` | raw `platform_name`, default `Other` | `decide` block deriving `windows-server` | ours is more specific than production |
| assets `ip_address` | `current_local_ip` only | `local_ip_addresses` + `external_ip`, deduped | ours is richer |
| assets `type` | `"Host"` | `"host"` | casing; production's is not an enum member |

Being more correct than production is a decision, not a default. Every divergence must be recorded
with its reason once the audit lands.

## Correction to my own account of the mapping error

I recorded above that the C# prototype "invented" the `apps` mapping. That is wrong, and the
adapters audit corrected it with git evidence. The truth is more useful.

`apps[0].product_name_version` was a **correct read against a superseded collector grammar.** The
pre-correlated Falcon findings flow (`11d0deaa`, `FalconFindingsFlow.cs:279-300`) emitted one
enriched Discover asset per line with `assetObj["vulnerabilities"] = vulnsForAsset`, each element
the **verbatim** Spotlight vulnerability resource — and `apps` is a default top-level member of that
resource. So under that shape the read worked, and the array key was `vulnerabilities`, not
`findings`.

The strip landed on 2026-07-05 in `f68e0113` ("Falcon findings flow: correlated asset-driven
collection, collector v5.0.0"), which introduced both the correlated envelope and
`StrippedFindingFields = { "apps", "suppression_info", "host_info" }`
(`FalconCorrelatedRecord.cs:21`). `git log -S'"apps"'` over the FalconCollector tree returns that
one commit and nothing else. The strip is pinned by a test
(`FalconCorrelatedFindingsTests.cs:560` asserts `apps` is absent), so it is a hard-coded prune, not
a configuration knob.

**So the lesson is not "the prototype invented a mapping."** It is: *I treated a superseded contract
as current.* The prototype was written against collector v4-and-earlier grammar; the collector moved
to a new envelope and a prune list two months before this task; and I carried the old read forward
without checking either the current collector or the production parser. Both would have caught it
independently, and I had both repos open.

That also explains why the mistake was invisible from inside this repo: the manifest correctly said
`apps` was absent, which is exactly what a pruned field looks like. Absence in a manifest cannot
distinguish "the vendor does not send this" from "the collector used to send this and no longer
does". Only the collector's own history answers that.

## A design consequence: `fieldSource: 'declared'` is unreachable for Falcon

The Falcon findings lane has **no positive projection and no field allow-list.** A finding is the
verbatim Spotlight resource with three names removed. What it contains is decided by the requested
facets (`FalconUrls.cs:35-41`: `cve`, `remediation`, and conditionally `evaluation_logic`) minus the
prune list — not by any DTO the collector owns.

So a Falcon mapping can only ever be validated against an **observed** manifest. The `declared`
value of `fieldSource`, which `src/manifest.ts` defines and which I described earlier as the honest
label for a collector that writes its own vendor projection, has no Falcon producer. Cortex XDR's
hardcoded XQL field list remains the one case where `declared` would be truthful.

The `host` block is likewise the verbatim `discover/combined/hosts` record plus one added property,
and Falcon repeats the full `host` on **every** chunk (`host.DeepClone()`,
`FalconCorrelatedRecord.cs:68`) rather than sending it once — which is worth knowing for payload
cost, since a host with many findings carries its whole host record per chunk.

## Production-alignment cycle (after the three-repo audit)

Three read-only audits ran in parallel, one per repo. Their full output is in
`research/production-mapping-spec.md` (951 lines) and `research/collector-field-availability.md`.
The EA audit's findings are folded in below.

### What the audits established

**Production's mapping is five stages, not one.** `base_parser.py`: extract → mandatory-field
injection → per-parser casts → `post_process` (row drops, enum normalization, CVE explode) →
`create_*_source` (fixed projection, JSON-encode structs, `_lower_string_values`, cast). The final
stage lowercases every `string` and `array<string>` column — JSON-encoded structs included, so
production's `additional_fields` has lowercased keys *and* values. Confirmed against committed real
output: `test_files/crowdstrike/assets/assets.expected_assets.json` shows `value: "dc01"` and keys
`"os build"`, `"kernel version"`, `"product type desc"`.

**There is no production bug.** An intermediate reading had it that production emits `open`, `"Host"`
and `"Windows"` — none of which are enum members — and that EA therefore drops those rows at
`parsed-data.repository.ts:340-341,515`. The EA half of that is correct: no normalization exists in
EA and a non-member is silently dropped. But the normalization is one repo upstream, in
`base_parser.py:231` (`type` → lower), `:239-253` (`os_type` → lower, enum-coerce, prefix-match),
`:281-284` (`status`: `open`→`opened`, `close`/`closed`→`resolved`, `re-opened`→`reopened`). I came
close to reporting a live production incident that does not exist; the lesson is that a
single-repo audit cannot conclude anything about a multi-repo pipeline.

**Production's `additional_fields` is a curated label map, not a whole-record carry** — nine labels
for Falcon assets (`crowdstrikeAssets.py:126-139`), dynamic for findings
(`crowdstrikeAssetsFindings.py:103-107, 243-295`). It also **double-encodes** nested values:
`"cvss_vector": "{\"access_complexity\":\"low\",…}"`, a JSON string inside the outer JSON. That
independently confirms the standing ledger belief `L-6f6ccb67` about double-encoding, which had
been carried as untested.

**`_VALID_OS_TYPES` is hand-copied.** `base_parser.py:234-237` hardcodes all 15
`asset_host_os_type` members in the parser. The generated contract in this repo removes exactly that
class of cross-repo drift.

### Changes applied, and verified against real data

| Change | Verification |
|---|---|
| findings `display_name` → `$.vulnerability_id` | `name == display_name` on all 25,565 prod-client rows |
| findings `severity` — invented `fallback: 'info'` removed | emitted values are `high`/`medium`/`critical`/`low` only |
| assets `value` → production's decide/coalesce logic, plus `caseFold: 'lower'` | **298/298 values lowercase** |
| assets `type` → `caseFold: 'lower'` | all rows `host` |
| findings `status` | unchanged — already matched production's final output |

Re-measured after the rewrite: 474 MB lane, **64 MB heap cap**, 246,212 records → 246,129 rows,
93 MB/s, zero enum rejections. Pair audit unchanged: 153,878 distinct pairs, 92,251 rows beyond the
first of their pair, **246,129 distinct ids, 0 collisions** — distinctness survives `display_name`
losing its distinguishing content, because the vendor's own record id rides in `additional_fields`.

The asset id digest was **unchanged** by the case fold, which is the correct result and a useful
check: asset ids derive from `parentKey` alone and carry no content term, so folding `value` cannot
move them.

### New capability: `caseFold` on `ColumnMapping`

`spec.ts:232` (type + `assertMappingSpec` check at `:439-450`), applied in `engine.ts:483-515` at
`:279` — after `evaluate`, before `coerce` and before `asStringList`/`distinct`. Refused at load on
a `jsonb` column (`validate.ts:299-306`), because the operator explicitly chose not to reproduce
production's JSON-key mangling.

Placed on the column rather than as a `ValueSpec` kind deliberately: case folding is a property of
the target column — "this column is case-normalized because EA's identity comparison is
case-sensitive" — not of how the value was sourced. As a `ValueSpec` kind it could appear nested
inside a `join` or `decide` branch where it means nothing.

16 verification checks pass, driving the real `mapLane` over the real mappings. One behaviour found
that was not briefed and is worth knowing: **a folded exploded column de-duplicates.**
`cve_id` values `['CVE-2024-1','cve-2024-1']` yield one row when folded and two when not. No shipped
mapping declares `caseFold` on `cve_id`, so it is latent — but it means declaring it there would
silently change row counts.

### My second mapping error, caught by a test

My first rewrite matched production's `value` chain exactly — hostname, then `current_local_ip` —
and so dropped the `parentKey` last resort. `tests/mapping-engine.test.ts` test 59
("firstNonBlank falls through to the parent key, so an unnamed host is still identifiable") failed.
Production would *drop* such a row (`base_parser.py:184-198`). I restored the fallback as a
documented divergence: it fires only where production discards the asset, and it extends
production's own stated intent at `crowdstrikeAssets.py:52-58` — "ensures no discovered asset is
omitted" — one step further, consistent with the operator's count-don't-drop decision. Not
exercised by real data: all 298 assets state a hostname or an IP.

That is the second time in this cycle a test written by a different owner caught an error of mine.

## My third mapping error — an identity MERGE, caught by the tests worker

The worst defect of the slice, and mine. Introduced by my own production-alignment rewrite and found
by the tests worker, which reported it rather than pinning it.

I wrote production's hostname rule as a `decide` whose rule result was `"then": "$localIp"`,
inventing a `$`-dereference. **No such mechanism exists.** `DecisionRule.then` is a plain `string`
and `engine.ts:583` returns it verbatim. So the mapping emitted the literal text `$localip`,
lowercased by `caseFold`.

Compounded by a second mistake: I used `containsIgnoreCase: 'ip'`, which matches far more than
IP-shaped hostnames. Reproduced through the real `mapLane` and the real mapping:

```
hostname=IP-10-0-0-5   -> value = "$localip"
hostname=SHIPPING-01   -> value = "$localip"
hostname=EQUIP-7       -> value = "$localip"
hostname=WEB-01        -> value = "web-01"
```

Three distinct hosts, one `value`, one `type` — so **EA's `(value, type)` pair collapses them into a
single asset identity.** An identity merge across distinct hosts, on the exact column pair
`decisions.md` names identity-critical, in service of a change whose entire purpose was to make
identity correct.

Nothing catches it: `value` is nullable `text` and `$localip` is non-blank, so there is no coercion
error, no enum rejection, no counted outcome. The tests worker deliberately did **not** pin it,
because an as-behaves assertion would have had to be reverted on fix — the right call.

**Production's rule also turns out to be narrower than I implemented.** Spark's `.contains("ip")` is
case-SENSITIVE, so production only ever substitutes the IP for a *lowercase* `ip-…` hostname — AWS
EC2 default naming. Measured on the real lane: exactly **1 of 298** assets qualifies
(`ip-172-31-20-25`). `SHIPPING-01` and `IP-10-0-0-5` never matched in production at all.

### Fix applied

`value` is now `firstNonBlank[hostname, current_local_ip, parentKey]` with `caseFold: 'lower'`.
Verified: the four hostnames above now yield `ip-10-0-0-5`, `shipping-01`, `equip-7`,
`ip-172-31-20-25` — four distinct identities. 118/118 tests pass.

### Deliberately NOT adding a capability to close the gap

Expressing production's branch needs two new pieces of spec surface: a rule result that can name a
source, and a case-sensitive `contains` test. I am not adding either.

- Measured impact of the divergence: **1 asset in 298.** That asset presents as
  `ip-172-31-20-25` where production emits `172.31.20.25`, so on cutover it appears as a new EA
  identity and its predecessor is not demoted. One asset.
- Against that: new spec surface added late, in the same files a defect was just found in, to serve
  one row. The operator's standing instruction is stability over completeness.

Recorded in the mapping's own `note` field, with an explicit warning not to reintroduce a
`$`-dereference, so the next author meets the history at the data rather than rediscovering it.

### Two defects the rewrite exposed in other people's work, both already fixed by their owners

- The tests harness's `bindColumn` rebuilt a `ColumnMapping` as `{column, value}`, silently dropping
  the new `caseFold` field — so the R3 asset test was varying two things at once. Now spreads the
  existing entry, with `caseFold` moved to a separate `setCaseFold` helper so the axes stay
  independent.
- The R3 asset test's never-winning source is now appended to the **shipped** `value` chain read out
  of the mapping, and the test first asserts the chain ends at `parentKey` so the appended source is
  provably unreachable. A future `value` change can no longer make that test quietly compare two
  things it invented.

## Contract retarget to cluster truth — applied

Driven by a skill shipped inside `cybi-db-models` that I had not read:
`.claude/skills/db-schema-snapshots/SKILL.md`. It states that
`database-schemas-up-to-date/<cluster>/` is ground truth read from `pg_catalog`, and that
`prisma/schema/` can drift, is identical across every cluster, and cannot express
partial/expression/INCLUDE indexes. `.claude/skills/db-migration/SKILL.md` explains the sync
discipline, from which it follows that a migration sitting in `migrations/` says nothing about what
landed on a cluster.

So my earlier flip from snapshots to Prisma was inverted, and the six cloud columns are **undeployed**,
not missing from a stale file.

| Table | prod-eu | stg | previously declared |
|---|--:|--:|--:|
| `parser_output_assets_enrich` | **22** | 23 | 29 |
| `parser_output_exposures_enrich` | **20** | 21 | 21 |
| `prismaOnlyUndeployed` | 8 | 6 | — |

`typeUnverifiedColumns` is gone; it existed only because the sources were inverted. With a cluster
snapshot as the column source every contract column is cluster-verified by construction, so the
honest field is the inverse: `prismaOnlyUndeployed`, table-qualified, which is also a useful record
of the pending-migration surface.

Both clusters generate. `--cluster=rfqa` and `--cluster=prod-us` are refused by name with the list of
generatable clusters, since neither has a committed snapshot tree — the `db-schema-snapshots` skill
mentions those clusters but they are not committed here.

**Decision implemented deliberately: an exclusion MAY name an undeployed column.**
`assertColumnCoverage` accepts an exclusion whose column is in `prismaOnlyUndeployed` for that table;
every other unknown name stays fatal. This keeps `mappings/falcon-assets.json` documenting intent for
`batch_id` and the six cloud columns, and keeps it portable to stg where `batch_id` does exist.

### A second false claim of mine, corrected by the worker

My brief asserted the generator "already corroborates enum members against `CREATE TYPE` DDL under
`migrations/`, as the current generator already does". **It did not.** The original generator only
compared Prisma member counts against **five hardcoded numbers** — `asset_type 23`,
`asset_host_os_type 15`, and so on.

Two things follow. First, I propagated a claim I had not checked: recon performed that DDL
cross-check by hand and reported "all five CONFIRMED", and I carried its conclusion forward as a
property of the code. Second, and worse, those five hardcoded counts are exactly the class of
hand-copied constant that the generated contract exists to eliminate — the same defect as
`base_parser.py:234-237`'s hand-copied `_VALID_OS_TYPES`, reproduced inside the tool built to avoid
it. Real corroboration against `migrations/*/migration.sql` is now implemented.

That is the third time in this slice a worker corrected a factual claim in my own brief. The pattern
is consistent: I restate a finding from an earlier agent as established, without re-checking whether
it describes the code or merely the investigation.

## Verifier-2 findings — all confirmed, four of them corrections to me

### V4 — The repo had stopped being dependency-free, and three artifacts still claimed otherwise

`node_modules/` (33 MB: `typescript`, `@types`, `@typescript`, `undici-types`) and
`package-lock.json` were present in the repo, untracked and not gitignored. A worker had run
`npm install` inside the repo despite the constraint, and I never checked — I verified the invariant
once, early, right after establishing the out-of-tree typecheck, and then trusted it for the rest of
the session while repeatedly asserting it in reports.

Removed. Nothing under `src/` imports anything external — verified, the only matches are the words
"from" inside comments. `package.json` has no `dependencies` key. Typecheck still clean via the
out-of-tree compiler, tests unaffected by the removal.

### V5 — The alignment never examined DEFAULT values, which is the largest divergence class

Production fills defaults that this mapping leaves null. Measured on the 298-asset lane:

| column | null here | production default |
|---|--:|---|
| `os_type` | 248 of 298 (83%) | `Other` → normalized to `other` |
| `os_version` | 248 | `Other` |
| `os_build` | 248 | `Other` |
| `fqdn` | 286 | — |

So the single largest behavioural divergence from production is one the alignment never looked at,
and it dwarfs everything the cycle did record. I wrote roughly 400 words on a divergence affecting
1 asset in 298 and nothing at all on one affecting 83%. The behaviour here is deliberate and pinned
by tests — a null column is honest where production invents `other` — but "deliberate" was never
established, because the question was never asked.

Note the interaction: `os_type` is in EA's twenty-column asset fingerprint, so production's `other`
and this mapping's null are different content for 248 of 298 assets.

### V6 — My finding F1 was overstated by roughly 34×

I wrote that `additional_fields` carrying the vendor record is "what actually guarantees zero
collisions, not the semantic columns". Measured: it rescues **7,090 of 246,129** lab rows (2.88%) and
**73 of 25,565** prod-client rows (0.29%). The semantic columns do almost all the work; the carry is
a thin safety net. The direction of F1 was right — identity is sensitive to the rawest field carried
— but the magnitude claim was wrong and I asserted it without measuring.

Verifier-2 also found something sharper that I missed entirely: **`name == cve_id` on 100% of rows in
both lanes**, and after my alignment `display_name == name`. So three of EA's twelve fingerprint
columns now carry the same string. The production alignment *reduced* fingerprint information. It
matches production, so it is production's property too — but it is worth knowing that the twelve-column
fingerprint is effectively ten on this vendor.

### V7 — The `$localIp` merge demonstration used synthetic hostnames

My reproduction showed `IP-10-0-0-5`, `SHIPPING-01` and `EQUIP-7` collapsing to one identity. None of
those hostnames exists in the 298 real assets; real-data impact was 1 asset. The fix was correct and
necessary — a mapping that can silently merge identities should not ship — but I presented a
synthetic three-host merge as though it were observed, which is the same error the C# prototype's
synthetic divergence case was criticised for in the handoff I started from.

### Assumption disposition changes from verifier-2

A1-A22: **VALIDATED 12 · REJECTED 5 · NEVER-TESTED 5.** Two changes against verifier-1:
- **A15 REJECTED → VALIDATED.** The retarget reversed its basis, correctly.
- **A3 stays REJECTED but for a different reason** — not "the dumps are stale" but "the dumps are
  silent about migrations newer than themselves". Those are not the same claim and the record now
  says so.

R1-R5 all `handled`, each confirmed by name in TAP output. R5's premise stays rejected while its
tests remain correct: they pin array order as *significant*, which is the true behaviour.

### One process failure worth naming

Verifier-2 began its pass against a **red** suite (117/118) — the failing test being criterion 10's
own provenance assertion, mid-re-pin. I dispatched a verification pass over a state I had not
confirmed was green. The re-pin landed during the pass and it ended at 128/128, so the conclusions
hold, but the sequencing was mine and it was wrong.

## Final verification, and a phantom regression I nearly chased

**150 tests, 150 pass**, twice concurrently. Typecheck clean. Repo dependency-free again
(`node_modules` and `package-lock.json` removed and confirmed absent). All seven code-review repairs
verified independently by the orchestrator, not accepted from reports.

F1 confirmed fixed across four timezones — UTC, Asia/Jerusalem, America/Los_Angeles and
Pacific/Kiritimati (+14 and −7 chosen so a naive parse of one instant differs by 21 hours). All four
give idDigest `61b044f3a435f330`. F3 refuses `casefold` naming the key and the known set.

### The phantom regression

The first post-repair run of the 474 MB lane read **17 MB/s against a recorded 93**, a 5.5× apparent
regression, with the id digest unchanged. I began diagnosing a performance defect in the F1 timestamp
grammar.

It was not a regression. Three checks, in order:
1. Micro-benchmarked the parse path: regex plus `Date.UTC` costs **242 ms** for all 246,212 records
   across all six timestamp columns. Nowhere near 23 seconds.
2. CPU-profiled the run: flat, no hot spot, `canonicalJson` the largest cluster and unchanged.
3. Ran the **reader alone**, which no repair touched: **66 MB/s against a recorded 385-409.** A module
   nobody had edited had apparently regressed 6×.

That third check is what settled it. `uptime` showed a load average of **7.69** — several subagents
and their processes still running. Measured back to back under identical conditions:

```
run 1   reader 401 MB/s   mapper 85 MB/s
run 2   reader 362 MB/s   mapper 50 MB/s
```

So the reader is unchanged and the mapper is ~50-95 MB/s, a ratio of roughly 4-7× to the reader.
There is no material regression from the repairs.

**This is the third time single-run timings on a loaded shared machine have produced a false
conclusion in this slice** — first my 409 MB/s and 386 MB reference figures, then verifier-1's 484 MB
and verifier-2's 382 MB for the same pair-audit measurement, now this. The correct discipline was
available from the first contradiction and I kept recording point figures anyway. The README now
states ranges and the ratio rather than single numbers, which is the only defensible form for
anything measured here.

Worth noting what nearly happened: I was one step from dispatching a worker to optimise a grammar
that costs 242 ms, on the strength of one unrepeated reading. Checking a component nobody had touched
is what caught it, and that check cost one command.

### Two notes from the tests worker worth keeping

- **`isRealDate` is stronger than the repair claimed.** It resolves through `Date.UTC(year, month, 0)`
  rather than a month-length table, so the leap-century rule comes free: `2000-02-29` is accepted and
  **`1900-02-29` is refused**. A hand-written table would very likely have got 1900 wrong. Both are
  now pinned.
- **F5's refusal lives in `planMapping` in `engine.ts`, not in `assertMappingUsable`.** A probe
  through the validator alone shows such a mapping loading cleanly. So the load-time gate is split
  across two modules and the validator is not a complete gate on its own — which is precisely the
  code review's closing risk, that the load-time gate is trusted more than it has earned.
- The test suite guards against its own tautology: alongside the timezone-invariance test there is a
  companion asserting that a naive `new Date` **is** zone-dependent. Without it the invariance test
  could pass while asserting nothing, since the explicit grammar is zone-independent by construction.

## The thin Falcon collector now emits a schema manifest — and it proves the premise

The C# collector in `~/Dev/Uri/localprojects/adapter-data-normalizer` was converted to write raw
vendor rows plus a per-lane `LaneManifest`, accumulated **while writing** rather than by re-reading
the file. The labeling (`AssetLabeler`, `FindingLabeler`, `FalconEnumLabels`, `PostgresTextArray`,
`LabeledRecord`) moved to that repo's test tree; the label contract and the CVE-skip policy are gone
from the collector entirely.

Live run against the Falcon lab tenant, 217.9s, read-only GETs. Verified by this repo, not accepted
from the report:

```
assets_000001    VALID   98 fields   301 records     parentKey ["$.aid","$.id"]
findings_000001  VALID   15 fields   112,959 records parentKey ["$.aid"]

probe assets    301 records      77 MB/s   drift: none   $.aid=47  $.id=254
probe findings  112,959 records  441 MB/s  drift: none   $.aid=112,959
```

**The Node reader consumed lanes written by an entirely different collector with zero code changes.**
Both manifests satisfy `assertLaneManifest`; both lanes report zero drift. That is the manifest
premise demonstrated across two independent producers rather than argued.

### The finding that matters: `apps` is present on 100% of findings here

This collector calls Falcon Spotlight directly and applies no prune list. The production
`FalconCollector` strips `apps`, `suppression_info` and `host_info`
(`FalconCorrelatedRecord.cs:21`). So the same vendor, collected two ways, yields two different field
sets — and each manifest describes its own lane correctly.

This settles the `display_name` story properly. `apps[0].product_name_version` was never a fiction:
it is a real Spotlight field, present on every record of *this* lane, and the C# prototype read it
correctly against the collector generation it was written for. It is unavailable only through the
production collector. My error was never "the prototype invented a field" — it was treating one
collector's contract as the vendor's contract.

It also demonstrates why the manifest is keyed on (vendor, **lane**) and not on vendor alone. Two
lanes, one vendor, 15 fields versus a pruned set, and a mapping written for one is correctly rejected
at load time against the other. That rejection is the feature.

### Consequence for the mappings

`mappings/falcon-findings.json` targets the production collector's envelope lane and stays as it is.
A mapping for **this** collector's flat lane would be a separate file — different `recordPath`
(`"$"` not `"$.findings[*]"`), no `parentPath`, and `display_name` could legitimately read
`$.apps[0].product_name_version` because this manifest carries `apps`. Not written; noted.
