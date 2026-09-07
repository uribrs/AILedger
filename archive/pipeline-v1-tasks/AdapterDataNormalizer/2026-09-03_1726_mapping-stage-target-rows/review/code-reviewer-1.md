# Code review: AdapterDataNormalizer

Reviewer: independent code-reviewer pass. Subject: everything under `src/`, plus `mappings/*.json`,
`package.json`, `tsconfig.json`. Tests read for intent and reviewed for quality.

## What I ran

- `npm test` — 128 tests, all pass.
- Out-of-tree `tsc --noEmit` — clean, no errors.
- The 474 MB Falcon findings lane through `src/cli/map.ts` under `--max-old-space-size=96`:
  151 lines, 246,212 records, 246,129 rows, peak RSS 183 MB, 49k records/s, 94 MB/s.
- The real Tenable lane with `--pair-audit`: 1,521 records, 4,672 rows, 4,592 distinct
  `(asset_id, cve_id)` pairs, **80 rows share a pair and 0 ids collide**.
- Four `mapLane` runs interleaved on one event loop, two specs, two instance ids — ids
  byte-identical to the serial baselines.
- Targeted probes for each finding below. Every finding was reproduced; none is theoretical.

---

## F1. A timestamp with no UTC offset is parsed in the host's local timezone, and the row id follows

`src/mapping/engine.ts:672` — `const parsed = text === undefined ? undefined : new Date(text);`

`first_seen` and `last_seen` are both content columns (`src/rows/exposureContent.ts:124-135`), and
`encodeInstant` writes `value.toISOString()` — UTC. So the emitted instant, the content string and
the derived id all depend on `process.env.TZ`.

**Reproduced.** One line of `tests/fixtures/falcon-findings.ndjson`, with `created_timestamp` changed
from `2026-06-01T08:00:00Z` to `2026-06-01 08:00:00`, same lane file, same `--instance-id`:

| TZ | `first_seen` | `id` |
| --- | --- | --- |
| `UTC` | `2026-06-01T08:00:00.000Z` | `6f79d911-4969-5b42-b312-0dada90bce87` |
| `Asia/Jerusalem` | `2026-06-01T05:00:00.000Z` | `9979cf8c-de21-580e-8e4b-0a16a7bb2a2d` |

Nothing throws and nothing is counted. Two machines normalizing one batch write two generations of
exposures side by side, which is exactly the outcome `src/ids/rowId.ts` exists to prevent.

This is the same class of defect the repo already guards twice, by name: `canonicalJson.ts:83-87`
refuses `localeCompare` and `engine.ts:501-503` refuses `toLocaleLowerCase`, both because "the same
lane would normalize differently on two hosts". `new Date` is the third instance of that rule and it
is unguarded. `Date.parse` on anything that is not ISO 8601 is also implementation-defined per
ECMA-262, so `Jan 15 2024 10:00:00` parses today and is not required to.

All three shipped lanes carry `Z`, so no current data triggers it. The manifest doc names eighteen
collectors; an offset-less `YYYY-MM-DD HH:MM:SS` is a common vendor spelling.

**Direction.** Refuse a timestamp string that carries no offset, the way a number is already refused
at `engine.ts:665` and for the same stated reason — the value alone cannot say what it means. If a
lane needs local instants later, that is a value kind carrying the zone, not a default.

---

## F2. `stringList` over a field the manifest declares as an array of objects silently yields `[]`

`src/mapping/engine.ts:453-464` (`asStringList`) drops any element with no text form.
`src/mapping/validate.ts:293` (`assertValueShapes`) checks the value KIND against the column type but
never checks the SOURCE FIELD's declared element kinds.

The manifest already carries the answer. `manifests/falcon-assets.json` declares
`network_interfaces`, `disk_sizes`, `mount_storage_info` and `bios_hashes_data` with
`elementKinds: ["object"]`, beside `groups` and `local_ip_addresses` with `elementKinds: ["string"]`.

**Reproduced**, both directions of the mistake:

```
ip_address := stringList($.network_interfaces)          -> ip_address = []      rejectedRecords 0
tags       := postgresTextArray(stringList($.disk_sizes)) -> tags = "{}"
```

Loads clean, emits for every record, no error, no counter. An asset lane can lose every IP address
this way and the run reports `enum rejections: none`.

This is the highest-value miss in the design's own terms: the load-time gate had the information and
did not look. Compare `assertValueShapes`'s own docstring, which was written after exactly this shape
of defect ("A name is not a check") — the check it added covers the column side and not the field
side.

**Direction.** In `assertValueShapes`, where a `stringList` or `postgresTextArray` source is a
`recordField`/`parentField` whose path is a single segment, look up that field's `elementKinds` and
refuse `["object"]` / `["array"]`. Single-segment only, since that is the boundary the manifest can
speak to — the same boundary `spec.ts:28-30` already states.

---

## F3. A misspelled or misplaced `caseFold` key is accepted, and the column is silently unfolded

`src/mapping/spec.ts:444-450` reads `mapping.caseFold` as `unknown` and refuses any value but
`'lower'`. That guard exists for a stated reason: "`caseFold: 'lowercase'` … would otherwise be an
unfolded column, which looks exactly like a column that was never meant to be folded." It checks the
VALUE. Nothing checks the KEY, and no unknown property anywhere in a spec is rejected.

**Reproduced** on the shipped `mappings/falcon-assets.json`, `value` column:

```
"casefold": "lower"                    -> LOADED, value = WEB-01, WEB-02
caseFold moved inside the value object -> LOADED, value = WEB-01, WEB-02
```

The mapping's own note (`mappings/falcon-assets.json:35`) says what that costs: production emits
`dc01`, Exposure Analytics identifies an asset by `(value, type)`, only 11 of 298 hostnames are
already lowercase, so "without the fold ~211 assets would never demote".

The `note` properties the shipped mappings carry are themselves unknown properties that load fine, so
the specs already demonstrate that a stray key is invisible.

**Direction.** Reject unknown keys on `ColumnMapping` and on each `ValueSpec` kind in
`assertMappingSpec`. A closed key set costs one loop and it is what makes the `note` convention safe
rather than lucky.

---

## F4. A `join` with no `separator` silently joins with a comma

`src/mapping/engine.ts:551` — `return parts.join(value.separator);`. When `separator` is absent,
`Array.prototype.join(undefined)` uses `,`. `assertMappingSpec` does not require the field.

**Reproduced** by deleting `separator` from the one real join in the shipped mappings, the `fqdn`
join in `mappings/falcon-assets.json:188`:

```
LOADED, no error. fqdn = ["WEB-01,corp.example.com","WEB-02,corp.example.com"]
```

A comma-joined hostname is a wrong value, not a missing one — the distinction the `join` docstring
itself draws to justify its all-or-nothing rule.

**Direction.** Require `separator` structurally. Related probes on the same gap:
`decide` with no `rules` and `lookup` with no `table` both load and then throw a bare `TypeError`
from inside `evaluate` on the first record — see F8.

---

## F5. A mapping that binds a derived column is silently overwritten

`src/mapping/engine.ts:278-285` writes `instance_id`, `created_at`, `asset_id` and `id` after the
column loop, unconditionally. `DERIVED_COLUMNS` (`engine.ts:60`) is used only to relax the NOT NULL
check at `engine.ts:171-182`; nothing refuses a mapping that binds one of those four.
`assertColumnCoverage` counts such a column as written, so coverage passes.

**Reproduced:**

```
created_at := $.first_seen_timestamp  -> LOADED, created_at = 2026-01-01T00:00:00Z (= normalizedAt)
id        := constant <uuid>          -> LOADED, id = 74680adb-... (derived, not the constant)
asset_id  := constant <uuid>          -> LOADED, asset_id = 4fa7aee6-... (derived, not the constant)
```

Every shipped mapping declares these excluded with a reason, so this is latent. It matters because the
exclusion reason ("the mapping may not mint identity") is stated as a rule and enforced nowhere: an
author who writes the column instead of excluding it gets no error and no value.

**Direction.** In `planMapping`, throw when `spec.columns` binds any name in `DERIVED_COLUMNS`,
naming the column and pointing at the exclusion.

---

## F6. Excluding a nullable array column writes an explicit NULL, defeating its `ARRAY[]::text[]` default

`src/mapping/engine.ts:246-253` writes `null` for every unmapped column, with the comment: *"for
every excluded column the default IS null, so an explicit null and the default agree."*

That is false on the asset table. `group_names`, `site_names` and `ip_address` are nullable with
`defaultExpression: 'ARRAY[]::text[]'` (`contract.generated.ts:67-70`), and `tags` is NOT NULL with
`'{}'::text`.

**Reproduced** — `group_names` unmapped and excluded with a reason:

```
LOADED. group_names = [null, null]
```

`coerce` states why this is not cosmetic (`engine.ts:643-646`): *"NULL and `{}` are different strings
inside the boundary's `md5(ROW(...))` fingerprint — so two lanes that both state nothing must not
fingerprint differently over which one said it with a null."* The mapped path honours that; the
excluded path does the opposite.

**Direction.** Either refuse to exclude a column whose `defaultExpression` is not null (the honest
option, since the engine states it will not synthesize Postgres defaults), or omit the property from
the row so the default applies. Do not leave the comment asserting a property the contract
contradicts.

---

## F7. The enum-rejection tally grows with the number of distinct offending values

`src/mapping/validate.ts:405-418`. `rejectionSamples` is capped at 25. `enumRejections` is
`Map<column, Map<value, count>>` and is not capped in any dimension.

**Reproduced** — 20,000 asset records, `os_type` bound straight to `$.platform_name`, a distinct
non-member per record:

```
records= 20000  rows= 0  rejectedRecords= 20000
distinct values retained in enumRejections = 20000
rejectionSamples retained = 25 (capped at 25)
```

That is the realistic shape of the mistake, not a contrived one: `os_type` and `os_version` are
adjacent fields, `os_version` is high-cardinality free text, and binding the wrong one is a one-word
error. On the 246,212-record lane it retains 246k map entries and 246k strings — the constraint at
`validate.ts:70-76` says memory "must not be unbounded", and it is.

The test that covers this, `tests/enum-validation.test.ts:147` — *"the tally stays exact while
retained examples are capped, so memory stays bounded"* — sends the same value 30 times, so it
exercises the sample cap and cannot see the distinct-value growth. The name overclaims what the
assertions prove.

**Direction.** Cap distinct values per column (say 100) and keep a single `otherValues` counter beyond
it. The report at `map.ts:170-180` already shows only the top 10.

---

## F8. `assertMappingSpec` does not validate value-kind payloads; malformed specs surface as bare `TypeError`

`src/mapping/spec.ts:405-454` checks the spec's top level and resolves paths, then stops. The module
docstring says a malformed spec "fails here rather than mid-stream". Probes:

| spec | result |
| --- | --- |
| `decide` with no `sources` | `TypeError: Cannot convert undefined or null to object` |
| `stringList` with no `sources` | `TypeError: value.sources is not iterable` |
| `decide` with no `rules` | accepted; `TypeError` on the first record |
| `lookup` with no `table` | accepted; `TypeError` on the first record |
| `join` with no `separator` | accepted; wrong value forever (F4) |

The two `TypeError`s do fail at load, but with no mapping name, no column, and no mention of the
value kind — the opposite of the attribution discipline every other error message here shows.

**Direction.** One `switch` over the ten kinds in `assertMappingSpec`, asserting each kind's own
required fields with the mapping and column in the message.

---

## F9. `absentFields` over-reports when a run is given more than one lane file

`src/reader.ts:221-223` computes unseen fields at the end of each `readLane` call and adds them to the
caller's shared `stats.absentFields`. A second file cannot remove what the first added.

**Reproduced** — the same two asset records, one file versus two:

```
one file:  manifest fields never seen = 85
two files: manifest fields never seen = 86
```

Reporting only, in `probe` and `map`. Low severity, but drift reporting is the thing those CLIs exist
for.

**Direction.** Track `seen` in the caller-owned `ReadStats` and derive `absentFields` when the caller
is done, not when one file is.

---

## F10. `describe` writes SAMPLE counts into `counts`, which is documented as what the batch wrote

`src/describe.ts:105,148,166` — `sampleRecords` stops the read, and `counts` is whatever the sample
reached. `src/manifest.ts:93` documents `counts` as *"What was written. Lets a consumer verify it read
everything the collector produced."*

The committed `manifests/falcon-findings.json` was clearly sampled. Running the real lane prints:

```
counts: manifest declares lines=20 records=40,000, this lane file has lines=151 records=246,212
        - the manifest describes a different batch
```

The message is wrong — it is the right file — so the one consumer of the field cries wolf on a correct
run, which is how a real mismatch later gets ignored.

**Direction.** Record the sample separately (`counts.sampled: true`, or a `sample` block) and leave
`counts` for a full pass, so the check at `map.ts:305-315` keeps meaning something.

---

## F11. Two divergent argument parsers, each with its own swallowing bug — low

`src/cli/describe.ts:17-30` does `named[name] = inline ?? argv[++index] ?? ''`, so
`--entity --vendor foo` sets `entity` to `--vendor`. `src/cli/map.ts:49-71` fixed that, and its
valueless-flag branch instead eats a following positional: `map.ts --pair-audit lane.json …`
consumes `lane.json` as the flag's value and prints usage. **Reproduced.**

Both fail visibly rather than silently. Worth one shared parser rather than two, mostly so the fixed
one does not get un-fixed.

---

## F12. Cosmetic, listed for completeness

- `src/describe.ts:75` sorts manifest field descriptors with `localeCompare` — the exact call
  `canonicalJson.ts:83-87` forbids for machine-independence. Field order is not load-bearing here, so
  the effect is only that a committed manifest is not byte-reproducible across ICU data. Use the
  ordinal comparison the repo already argues for.
- `src/mapping/engine.ts:568` — comment reads "`??` rather than `||`: an empty-string result in the
  table is a stated answer." The code below it uses `Object.hasOwn`, which is a better fix for the
  same problem. The comment describes a version that no longer exists.
- `src/mapping/engine.ts:505-507` — `foldCase`'s stated reason for running before the explode is that
  `CVE-2024-1` and `cve-2024-1` fold to one row. No shipped mapping declares `caseFold` on `cve_id`,
  so the behaviour the comment claims is inert; `distinct` at `engine.ts:466` is case-sensitive.
- `src/mapping/engine.ts:220` — `contentVariesPerExplodedValue` is `CONTENT_COLUMNS.includes('cve_id')`,
  and `cve_id` is in `OUTSIDE_IDENTITY`, so it is always `false` and the recompose branch at
  `engine.ts:304` is unreachable under the current contract. Defensive, and cheap; just not exercised.
- `src/reader.ts:188` — `stats.bytes += Buffer.byteLength(line) + 1` assumes LF; a CRLF lane
  undercounts one byte per line. Affects only the MB/s figure.
- `collectLiterals` (`spec.ts:374-398`) treats a `join`/`stringList` of constants as producing those
  constants. For an enum column that is wrong in both directions — `join(constant 'high', field)`
  passes load time and then rejects every record. I confirmed it: every record was counted as a
  rejection, so it is loud, not silent. Not worth fixing before the kinds above.

---

## What I checked and found sound

Stated plainly rather than padded into findings.

- **`uuid5` (`src/ids/rowId.ts:83`).** Correct RFC 4122 §4.3: namespace bytes in network order, UTF-8
  name, version and variant set in place, no endian swap. The published DNS vector is in the tests and
  is the right way to prove the un-swapped port. `assertUuid` rejecting non-canonical spellings closes
  the real hazard, which is `Buffer.from(hex,'hex')` truncating silently.
- **`canonicalJson`.** Ordinal key sort at every depth, array order preserved, `Date` excluded from the
  object branch (otherwise every instant renders `{}`), and every value `JSON.stringify` would collapse
  onto `null` is fatal instead. Both deviations from the C# are named with their consequence. I found
  nothing wrong here.
- **The content separator scheme.** I tried to forge a column boundary by putting U+001E inside a
  `name` value. It cannot be done: `composeExposureContent` emits the column NAME plus a trailing
  separator for every column, so the injected string produces a longer sequence, not a colliding one.
  The docstring's claim that no JSON encoder produces U+001E is untrue (`""` parses fine), but
  the design does not rest on it.
- **Streaming.** Verified, not trusted. Nothing accumulates per record: `plan.pathSteps` is bounded by
  distinct paths in the spec, `row` is rebuilt per record, `sharedContent` is per record,
  `readLane`'s `seen` is bounded by the manifest, `keySources` by the candidate list. The 474 MB lane
  completes under a 96 MB old-space cap at 183 MB RSS. F7 is the one real exception, and
  `--pair-audit` is honestly labelled.
- **Concurrency.** No module-level mutable state anywhere in `src/` (I grepped, then tested). Four
  interleaved runs across two specs and two instance ids produced ids byte-identical to the serial
  baselines. `ReadStats`/`MappingStats` being caller-owned is the right call and it holds.
- **Hot-path cost.** Per record: one `JSON.parse`, one map lookup per column, one path walk per read,
  one canonical serialization of `additional_fields`, one SHA-1. Nothing superlinear. 49k records/s
  and 94 MB/s on the real lane, which is `JSON.parse` bound.
- **The coverage discipline** (`assertColumnCoverage`, `planColumns`) and the identity split into
  `IDENTITY_COLUMNS` / `OUTSIDE_IDENTITY` that must partition the table. This is the strongest part of
  the design: a migration that adds a column throws at import until someone decides which side it is
  on. The undeployed-column tolerance is argued correctly and does not weaken typo detection.
- **The content-in-identity claim is borne out by real data.** The Tenable lane has 80 rows sharing an
  `(asset_id, cve_id)` pair and zero id collisions. That is the measurement that justifies the whole
  `exposureKey` shape.

---

## On the `ValueSpec` design

Asked directly, so answered directly.

**The union is coherent and the overlaps are argued, not accidental.** `lookup` could be expressed as
`decide`; keeping it separate because four key-value pairs should read as four key-value pairs is the
right trade. `caseFold` as a property of the target column rather than an eleventh kind is the best
single decision in the file — the docstring's reasoning about a fold nested inside a `join` separator
meaning nothing is exactly right.

**The one real expressiveness gap is already documented, with evidence of the hack it produced.**
`mappings/falcon-assets.json:35` records that production substitutes `current_local_ip` when the
hostname contains a case-sensitive `ip`, that `DecisionRule.then` is a literal and cannot name a
source, and that an attempt to write `{then: '$localIp'}` made three distinct hosts emit the literal
`'$localip'` and collapse into one identity. That is the design under pressure, caught. If a second
vendor needs the same shape, the answer is a `then` that can name one of the `decide`'s own sources —
which stays inspectable and stays checkable — not a string that gets dereferenced.

**Two kinds carry checks the spec cannot make, and neither is reported.** An unmatched `lookup` key is
a dead table entry, and a `decide` rule that can never fire for any reason other than a misspelled
source name is invisible. Both silently yield null or the fallback. A per-run counter of
"records that matched no lookup key, by column" would cost nothing on the hot path and would make the
second-worst class of mapping mistake visible; today only enum violations are counted.

**F2 is the boundary of load-time checkability drawn one step short of where the data allows.** The
manifest states `elementKinds`; the gate reads `fields[].name` and nothing else. Closing that is the
single highest-value change on this list.

---

## Overall assessment

**Fit for its stated purpose: yes, clearly.** As a prototype meant to prove a direction and measure
its scaling, this does both. The generic reader and generic engine hold — I could not find vendor
knowledge in either — the mapping-as-data claim survives three real lanes across two vendors, the
streaming constraint is real and measured rather than asserted, and the identity derivation is
correct against an external vector. The commentary is unusually good: most of it states a measured
fact and its consequence, and several comments record a defect that was actually hit, which is worth
more than any of them being tidy.

The test suite is the second strongest part. No hand-built mapping spec, no stand-in contract, every
variant an edit of a clone of a shipped file, and `readerThatRefusesToBeIterated` is a genuinely good
tripwire for the "validates before the first record" claim. It targets the risky spots deliberately —
rule ordering, the `tags`-versus-`group_names` trap, both directions of the Prisma-identifier
spelling.

**The biggest risk if this is taken further** is that the load-time gate is trusted more than it has
earned. Its coverage is excellent on the two questions it asks — does this column exist, is this
literal a member — and it currently asks nothing about a value spec's own payload (F4, F8), the
element kinds of the field it reads (F2), the spelling of its own optional keys (F3), or a column the
engine will overwrite anyway (F5). Every one of those produces a plausible-looking wrong value with no
error and no counter, which is the failure mode the design was built to eliminate. Each is a small,
local fix; the risk is not the size of the work but that "it fails at load time" gets treated as
settled.

**Second risk: machine-dependent identity.** The repo reasons about this well twice and misses it once
(F1). With eighteen collectors to onboard, the third vendor with an offset-less timestamp derives
different ids on different hosts and nothing anywhere fails. Refuse the ambiguous input.

**Third risk, smaller: the manifest as a committed artifact.** F10 means the shipped Falcon findings
manifest disagrees with its own lane and the tool says so on every correct run. Sampled descriptions
and full ones should not occupy the same field.

## Action items, most valuable first

1. Check `elementKinds` for `stringList` / `postgresTextArray` single-segment sources in
   `assertValueShapes` (F2).
2. Refuse a timestamp string with no UTC offset in `coerce` (F1).
3. Reject unknown keys on `ColumnMapping` and each `ValueSpec` kind (F3), and assert each kind's
   required fields with mapping and column attribution (F4, F8).
4. Throw when a mapping binds a `DERIVED_COLUMNS` name (F5).
5. Decide what an excluded column with a non-null Postgres default means, then make the code and the
   comment at `engine.ts:249` agree (F6).
6. Cap distinct values in `enumRejections` and rename or strengthen the test at
   `tests/enum-validation.test.ts:147` (F7).
7. Move `seen` into `ReadStats` (F9); separate sampled counts from batch counts (F10); share one
   argument parser (F11); clear the stale comments in F12.
