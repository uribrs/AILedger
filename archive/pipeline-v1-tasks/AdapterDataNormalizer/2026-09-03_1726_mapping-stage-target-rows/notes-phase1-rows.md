# Phase 1 - `src/rows/` - decisions the brief did not settle

Author: the identity-canonical-content worker. Files delivered:
`src/rows/canonicalJson.ts`, `src/rows/exposureContent.ts`. Both frozen as of this note.

This file records reasoning that the module comments state in shorter form. Where the two differ,
the module comments are authoritative, because they are the ones a future reader will find.

---

## 1. `planColumns` needs the ten identity columns as `written`, not all twenty-one

### What happened

The C# resolves the identity through `ColumnCoverage.Plan(table, Accessors, OutsideIdentity)`
(`ExposureRowMapper.cs:109-110`). In that call `Accessors` covers **every** column of the table -
all twenty-one, `created_at` and the stamped scope ids included - and `OutsideIdentity` excludes
eleven of them. The C# runs `Plan` twice over the same accessor map with two different exclusion
maps: once with `NotFromLabels` to get the label binding, once with `OutsideIdentity` to get the
identity.

I ported that shape first: `written` = every column name, `exclusions` = the eleven. It throws at
module load:

```
integration.parser_output_exposures_enrich column 'id' is both written and declared excluded
('derived from the parent key, the CVE and the row's content'). The value would be written and left
out of the row's identity, so two rows differing only in it would derive the same id.
```

That is `assertColumnCoverage` in `src/target/contract.ts` doing its job. Phase 0 made
written-and-excluded one of its four named failure modes, which is **stricter than the C#'s
`Plan`**: the C# only checks that a non-excluded column has an accessor, and says nothing about a
column that has both. Phase 0's rule is the better one - it is what catches a column that gets
written to the row and silently left out of the row's identity - but it means one accessor map
cannot serve two different exclusion sets.

### What the module does instead

Two lists, which together must partition the table exactly:

```ts
const IDENTITY_COLUMNS: readonly string[] = [ ... ten names ... ];
const OUTSIDE_IDENTITY: readonly ColumnExclusion[] = [ ... eleven, each with a reason ... ];

const CONTENT_PLAN: readonly TargetColumn[] = planColumns(
  EXPOSURE_TABLE, IDENTITY_COLUMNS, OUTSIDE_IDENTITY,
);
```

Ten plus eleven is twenty-one, every column is in exactly one list, and `planColumns` asserts all
of that at module load.

### Why it is that way - do not "fix" the assertion

The partition is the whole reason the identity is expressed as two lists rather than one list of
ten names. `assertColumnCoverage` fails in four ways, and all four are load-time:

- a column in neither list - the case that matters, described below
- an exclusion naming a column that does not exist - a typo, which leaves a real column uncovered
- an exclusion with a blank reason - an oversight wearing a decision's clothes
- a column in both lists - the case that forced this design

**The case that matters.** When a migration adds a column to
`integration.parser_output_exposures_enrich` and `contract.generated.ts` is regenerated, the new
column is in neither `IDENTITY_COLUMNS` nor `OUTSIDE_IDENTITY`. `exposureContent.ts` then throws on
import, and nothing in the normalizer runs until a person decides which side the column belongs on.

Compare the alternative - a hand-written array of the ten names, with no assertion. It keeps
loading. The new column is simply absent from the content, so two rows differing only in that
column compose identical content, derive the same uuid5, and one of them is dropped as a duplicate
collapse. Nothing errors. Nothing is logged. The defect surfaces as missing exposures in Exposure
Analytics, weeks later, with no trace back to the migration that caused it.

So the failure this assertion prevents is not a crash - it is silent data loss. That asymmetry is
the argument. A load-time throw costs one person ten minutes and is impossible to miss; the silent
path costs an investigation and is very easy to miss.

**The predictable way this gets undone.** Someone hits the load-time throw, reads it as the
assertion being too strict rather than as a decision they need to make, and either deletes the
`planColumns` call in favour of the plain ten-name array, or relaxes
`assertColumnCoverage`/`planColumns` in `src/target/contract.ts` to warn instead of throw. Either
edit removes the guarantee, and the code keeps working perfectly on the day it is made. If you are
looking at that throw now: the fix is to add the new column to one of the two lists - with a reason
if it goes in `OUTSIDE_IDENTITY` - not to weaken the check.

**Two smaller points inside the same decision.**

- The order of names inside `IDENTITY_COLUMNS` is not significant and deliberately so.
  `planColumns` returns columns in `EXPOSURE_TABLE.columns` order, which is Prisma's declared order,
  and that is the order the content is composed in. Deriving the composition order anywhere but from
  the contract is a way for identity to change without an identity function being edited. Reordering
  the contract's columns re-mints every exposure id; reordering the names in this file changes
  nothing.
- `written` in `planColumns`' signature means "columns a mapping writes". Here it is being read as
  "columns inside the identity", which is a stretch of the parameter's name but not of its
  behaviour: `planColumns`' own doc calls its result "the columns a per-row pass must visit", and a
  content composition is exactly such a pass. If phase 0's semantics are ever tightened so the two
  readings can no longer share one function, this module needs its own assertion rather than losing
  one.

---

## 2. The number-literal deviation is a decision to keep, not debt to repay

**Your reading is correct.** State it as you wrote it. The claim is not merely "Node forced our
hand"; it is that the Node behaviour is the safer of the two directions, and would be the better
choice even if the raw literal text were available at this layer.

The two halves of the argument, kept separate because they are independent:

**Half one - it is forced.** `canonicalJson`'s input is the output of `JSON.parse`, which yields a
`number`: an IEEE-754 double with no memory of how it was spelled. `1.0` and `1.00` are the same
double by the time this module sees them. The C# preserved the spelling because `JsonNode` still
held the source text and `node.ToJsonString()` replayed it (`CanonicalJson.cs:41`, rationale
`:13-16`). Recovering that here would mean plumbing raw source text through the reader, the mapper
and the row draft. That is out of scope, and the brief said not to invent it.

**Half two - it is also the better behaviour.** This is the part that stops the deviation being
read as debt.

Exposure Analytics dedups an exposure on
`md5(ROW(asset_id, cve_id, type, severity, status, name, display_name, description, mitigation,
first_seen, last_seen, additional_fields)::text)`
(`cymulate-exposure-analytics/apps/create-entities-tp/src/app/create-entities-third-party/repositories/parsed-data.repository.ts:70-76`).
That digest runs over **stored column values** - a `jsonb` column that Postgres has already parsed
and re-rendered. The source spelling is gone there too. So `1.0` and `1.00` are one fingerprint at
the promotion boundary no matter which normalizer produced them.

Follow both implementations through:

- **This port.** One value, one content string, one id, one row. The row's identity agrees with the
  boundary's fingerprint.
- **The C#.** One value, two content strings, two ids, two rows - which Exposure Analytics then
  collapses into one, because its own fingerprint cannot tell them apart. Two rows emitted, one
  survives, and which one survives depends on the `latest` demotion rather than on anything the
  normalizer decided.

So the C# spends a row and an id to preserve a distinction the next stage immediately discards. This
port does not create the distinction in the first place. Same outcome downstream, one less
generation of ids, and the normalizer's notion of "same finding" now matches the boundary's.

**Consequence, stated exactly, for anyone comparing the two implementations.** Two records differing
only in how a number is written - `1.0` against `1.00`, `1e3` against `1000` - compose the same
content here and different content in the C#, so they hash to one id here and two there. Verified:
`canonicalJson(JSON.parse('{"n":1.0}'))` and `canonicalJson(JSON.parse('{"n":1.00}'))` both give
`{"n":1}`.

**What is not a deviation.** The rendering of a number is identical between the two. `JSON.stringify`
writes the ECMA-262 shortest round-trippable form, which is the same text .NET's `"R"` format
produces for the same double. Only the memory of the source literal differs.

**A downstream worker owns the test that pins this.** The behaviour is deterministic and stated: two
number spellings that parse to one double compose one content string. Pin that, not the C#'s
behaviour.

### The second, smaller deviation in the same module

.NET's default `JavaScriptEncoder` escapes non-ASCII and HTML-sensitive characters as `\uXXXX`;
`JSON.stringify` escapes only what JSON requires and emits the rest raw. This affects both object
keys and string values. Both encodings are valid JSON and both are deterministic, so each
implementation is self-consistent - the byte strings simply differ for a record carrying non-ASCII
text. That costs nothing, because ids are scoped to `instance_id` and are never compared across the
two implementations (see the design note in `src/ids/rowId.ts`). Also a decision to keep, for the
same reason: there is nothing to repay.

---

## 3. `Date` is written as a literal inside `canonicalJson`, not through the object branch

A `Date` has no own enumerable properties. Left to the object branch, every instant nested inside a
`jsonb` value would canonicalise to `{}` - so two records whose only difference was a timestamp
inside `additional_fields` would compose identical content and derive one id. That is precisely the
silent collapse this module exists to prevent, so `Date` is excluded from the object branch
explicitly.

Written as a literal it becomes `"2026-07-01T10:20:30.123Z"`, which is the same quoted
millisecond-ISO string a Postgres driver stores into a `jsonb` column. So a re-parse of the stored
row canonicalises to the same text as the original composition - the re-derivation property holds
through the round trip.

---

## 4. `encode` dispatches on the column's declared `pgType`, not on the value's shape

The C# could switch on the runtime type (`ExposureContent.cs:65-75`) because a jsonb value arrived
wrapped in `JsonbValue` and a string list as `IEnumerable<string>`. In Node both are plain arrays.
A `jsonb` column holding a top-level JSON array and a `text[]` column holding two strings are
indistinguishable by inspection, and encoding one as the other - `["x","y"]` against
`x<U+001D>y` - is a silent identity change.

The declared type resolves it, so `encode` takes the `TargetColumn` and asks it. A value that does
not match its column's declared type is fatal, with the column name, the `pgType` and the offending
value in the message. Verified: `additional_fields: ['x','y']` encodes as `["x","y"]`, not as a
U+001D join.

---

## 5. `undefined` and non-JSON values are fatal, not coerced

`JSON.stringify` mangles several inputs quietly: it drops an `undefined` member, a function and a
symbol from an object and writes `null` for each of them inside an array; it writes `null` for `NaN`
and the infinities; it throws a bare `TypeError` from inside the serialiser for a `bigint`, naming
no value. Every silent case collapses a distinct value onto `null`, which is how two different
records end up with one id.

None of them can arrive from `JSON.parse`, so any of them is a defect in the code that built the
value - and the offending value is what the author needs to see. `canonicalJson` therefore throws,
naming the member key for an `undefined` member and the value otherwise. This is the same
absent-versus-empty distinction `ABSENT` (U+0000) keeps in `exposureContent.ts`, one layer down.

---

## 6. `encodeInstant` needs no truncation step

The brief asked for ISO 8601 truncated to milliseconds. `Date` holds integer milliseconds and
`toISOString` always writes exactly three fractional digits with a `Z`, so `toISOString()` **is**
the truncated form - there is nothing to round. No truncation code was written, and its absence is
not an omission.

The deliberate improvement over the C# stands and is documented on the function: the C# wrote `"O"`,
seven fractional digits, while the target columns are `timestamp(3) without time zone`. Postgres
rounds anything finer, so composing the content over an unrounded value yields an id that cannot be
re-derived from the stored row. Phase 0 typed timestamp columns as `Date` rather than as strings for
the same reason - a string could carry microseconds - and `encodeInstant` rejects a string outright.

---

## 7. `encodeList` is currently unreachable, and was kept anyway

`integration.parser_output_exposures_enrich` declares no `text[]` column, so no route exists from
`composeExposureContent` to `encodeList` today. The U+001D path is unexercised by construction, and
a test cannot reach it through the exposure lane - flagging that for whoever writes the tests, so it
is not mistaken for a coverage gap.

Kept for two reasons: the brief specifies the rule and the `LIST_SEPARATOR` constant verbatim, and
without it a future `text[]` exposure column would fall through to `encodeText` and throw rather
than encode. The shape it exists for is the asset table's `group_names`, `site_names` and
`ip_address`. The comment on `encode` says all of this in the file.

---

## 8. The `uuid` encode case is absent

None of the ten content columns is `uuid`: `asset_id`, `cve_id` and the four scope ids are all
outside the identity. So the C#'s `Guid -> ToString("D")` case has nowhere to apply and was not
ported.

Where a uuid does enter an identity - the version-5 derivation name - `src/ids/rowId.ts` asserts the
lowercase hyphenated 8-4-4-4-12 form rather than coercing it, which is the stronger posture:
`Buffer.from(hex, 'hex')` truncates silently at the first non-hex character, so a silently
lowercased or braced id would mint a different, plausible-looking id for the same run. Documented in
the `encode` comment rather than left as an unexplained gap against the C#.
