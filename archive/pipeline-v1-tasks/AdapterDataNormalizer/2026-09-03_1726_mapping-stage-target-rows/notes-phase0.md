# Phase 0 notes — freeze-target-contract-and-mapping-schema

Decisions made while writing the five frozen files, and the places the brief was wrong or
underspecified. Written because the report truncated twice in transit.

Files frozen by this phase:

- `src/target/generate-contract.ts`
- `src/target/contract.generated.ts`
- `src/target/contract.ts`
- `src/ids/rowId.ts`
- `src/mapping/spec.ts`

## Verification command — use this exact one

`npx tsc --noEmit` does not work. TypeScript is not installed on this machine, and `npx tsc`
resolves to an unrelated abandoned package named `tsc@2.0.4`, not the compiler. Separately,
`tsconfig.json` sets `types: []` with no `@types/node` present, which makes every `node:` import
plus `console`, `process` and `Buffer` a hard error across the whole repo — including
`src/reader.ts`, which nobody may modify.

Install the compiler out of tree, once:

```
cd <scratchpad>
npm init -y
npm install typescript @types/node
```

Then run, from the repo root, against the repo's own `tsconfig.json`:

```
<scratchpad>/node_modules/.bin/tsc --noEmit \
  --typeRoots <scratchpad>/node_modules/@types \
  --types node
```

Nothing is installed into the repo, `package.json` is untouched, and the zero-dependency constraint
holds. The scratchpad used in phase 0 was
`/private/tmp/claude-501/-Users-user-Dev-Uri-localprojects-AdapterDataNormalizer/75719977-6fbd-4678-ab6d-c694c4efa12e/scratchpad/tscheck`.

Expected output as of the end of phase 0: exactly one error, pre-existing, in a file phase 0 does
not own —

```
src/cli/describe.ts(21,3): error TS2322: Type '{ _: string[]; }' is not assignable to type 'Args'.
```

`return { ...named, _: positional }` is not assignable to
`Readonly<Record<string, string>> & { _: readonly string[] }`, because `string[]` violates the
index signature. Owned by whoever owns `src/cli/*`. Zero errors in the five phase-0 files.

Per-module load check, also worth running:

```
node --experimental-strip-types -e "import('./src/target/contract.ts').then(()=>console.log('ok'))"
```

Generator reproducibility check:

```
node --experimental-strip-types src/target/generate-contract.ts --check
```

`--check` regenerates in memory and diffs against the committed artifact without writing. It
ignores only the `generatedUtc` line, which changes on every run and means nothing to the contract.

## 3. `decide` for `os_type`, `lookup` for the other four enum columns

The concrete shape, verbatim, for whoever writes the mapping data. This is the single most
misusable part of the spec.

`os_type` needs `decide` because it reads **two** fields with **two different tests**.
`platform_name` alone cannot separate "Windows Server 2022" from "Windows 11" — both carry
`platform_name: "Windows"` — so the version string is read as a second source and tested with
`containsIgnoreCase`. Rule order carries the logic; the first rule whose every condition holds wins.

```json
{
  "kind": "decide",
  "sources": {
    "platform": { "kind": "recordField", "path": "$.platform_name" },
    "version":  { "kind": "recordField", "path": "$.os_version" }
  },
  "require": ["platform"],
  "rules": [
    { "when": [{ "source": "platform", "test": "equalsIgnoreCase", "value": "Windows" },
               { "source": "version",  "test": "containsIgnoreCase", "value": "Server" }],
      "then": "windows-server" },
    { "when": [{ "source": "platform", "test": "equalsIgnoreCase", "value": "Windows" }],
      "then": "windows" },
    { "when": [{ "source": "platform", "test": "equalsIgnoreCase", "value": "Linux" },
               { "source": "version",  "test": "containsIgnoreCase", "value": "Ubuntu" }],
      "then": "ubuntu" },
    { "when": [{ "source": "platform", "test": "equalsIgnoreCase", "value": "Linux" },
               { "source": "version",  "test": "containsIgnoreCase", "value": "Debian" }],
      "then": "debian" },
    { "when": [{ "source": "platform", "test": "equalsIgnoreCase", "value": "Linux" }],
      "then": "linux" },
    { "when": [{ "source": "platform", "test": "equalsIgnoreCase", "value": "Mac" }],
      "then": "macos" },
    { "when": [{ "source": "platform", "test": "equalsIgnoreCase", "value": "macOS" }],
      "then": "macos" },
    { "when": [{ "source": "platform", "test": "equalsIgnoreCase", "value": "Android" }],
      "then": "android" },
    { "when": [{ "source": "platform", "test": "equalsIgnoreCase", "value": "iOS" }],
      "then": "ios" }
  ],
  "fallback": "other"
}
```

Three traps in that block.

- The `windows-server` rule **must precede** the bare `windows` rule. Otherwise every server
  matches `windows` first and the `windows-server` member is never emitted at all.
- `require: ["platform"]` is load-bearing, not decoration. When `platform_name` is absent the whole
  `decide` yields nothing, because an absent OS is **not** the `other` member — calling it `other`
  makes an unmanaged entity indistinguishable from an odd one.
- `fallback` applies only when the required sources are present and no rule matched. It is not a
  default for the absent case; `require` handles that, and the two must not be conflated.

Rules are flat, not nested. No real case needs nesting, verified against both vendors.

`severity` and `status` are `lookup`, not `decide` — a four-or-five entry table on one field with
`normalize: "lowercase"`.

```json
{ "kind": "lookup",
  "source": { "kind": "recordField", "path": "$.cve.severity" },
  "normalize": "lowercase",
  "table": { "critical": "critical", "high": "high", "medium": "medium", "low": "low" },
  "fallback": "info" }
```

```json
{ "kind": "lookup",
  "source": { "kind": "recordField", "path": "$.status" },
  "normalize": "lowercase",
  "table": { "open": "opened", "reopen": "reopened", "closed": "resolved" } }
```

Falcon severity carries `fallback: "info"` because Falcon also answers `NONE` and `UNKNOWN`, which
have no enum counterpart and belong on the member that means "no severity to act on". Falcon status
carries **no** fallback, deliberately: the three states map one-to-one and anything else is left
null rather than invented into a lifecycle state.

Both kinds are load-time enum-checkable — every `then`, every table value, and both `fallback`s are
literals — so `lookup` costs nothing in safety and reads as a table instead of a rule list. That
readability is the whole reason mapping-as-data beats code, so the redundancy is deliberate.

`asset_type` and `exposure_type` are neither: they are `constant`. Falcon Discover returns host
inventory, so `type` is always `host`; Falcon Spotlight returns vulnerabilities, so `type` is always
`vulnerability`.

## 4. `tags` gets `postgresTextArray`; the other three array-ish columns get `stringList`

Not interchangeable, and getting it wrong fails the COPY rather than producing a wrong value.

`tags` is plain `text NOT NULL DEFAULT '{}'::text`. `group_names`, `site_names` and `ip_address`
are real `text[]` defaulting to `ARRAY[]::text[]`.

- `group_names`, `site_names`, `ip_address` → `stringList`. The engine hands Postgres a real array.
- `tags` → `postgresTextArray` wrapping a `stringList`. The list renders into a `{a,b,c}` literal
  *string*, elements quoted when a bare element would move the delimiters. Emitting a real array
  here is wrong.

Two further consequences for `tags` specifically:

- Because it is NOT NULL **with a default**, a mapping must emit something. The empty literal `{}`
  is what an absent tag list becomes — which is also the column's own default, so that is the
  convention its values follow.
- The C# did the same thing for the same reason (`AssetRowMapper.cs:111`,
  `draft.Tags ??= AssetRow.EmptyTags`).

And the mirror-image trap on its neighbour: `site_names` is a real `text[]`, but Falcon Discover
states **one** site per host as a scalar (`$.site_name`). So it is `stringList` over a single scalar
source, not a passthrough:

```json
{ "kind": "stringList", "sources": [{ "kind": "recordField", "path": "$.site_name" }] }
```

`ip_address` is the only one needing `distinct: "caseInsensitive"` — the external address is
frequently also in the local set, and two spellings of one address are one address:

```json
{ "kind": "stringList",
  "sources": [{ "kind": "recordField", "path": "$.local_ip_addresses" },
              { "kind": "recordField", "path": "$.external_ip" }],
  "distinct": "caseInsensitive" }
```

I considered folding `postgresTextArray` into a generic named-transform list and did not. It exists
for exactly one column, and naming it after that column's problem is what stops someone applying it
to a real `text[]`.

## 5. Two `ValueSpec` kinds cut after they proved unexercised

I first wrote eleven kinds plus `join.require: 'all' | 'any'`. Writing all three real mappings
showed:

- `{ kind: 'record' }` was dead. `additionalFields.carry: 'wholeRecord'` covers the
  `additional_fields` case, and a nested object into a `jsonb` column is already reachable via
  `recordField` (`$.cve` yields an object). Removed, along with `FieldReads.usesWholeRecord`.
- `join.require: 'any'` had no case at all. Removed; `join` now always requires every source,
  because the one case that needs a join needs it that way round — a hostname with no domain is not
  a fully-qualified name, so emitting one is a **wrong** value rather than a missing one.
- `lookup.normalize: 'none'` and `stringList.distinct: 'none'` were redundant literals: absent
  already means none. Removed.

Final surface: ten kinds — `recordField`, `parentField`, `parentKey`, `constant`, `firstNonBlank`,
`join`, `stringList`, `lookup`, `decide`, `postgresTextArray`. Every one is exercised by Falcon
assets, Falcon findings or Tenable findings. No `function` field, no expression string, so the load
-time checkability property holds for the whole spec.

## 6. `KEY_SEPARATOR` spelled `'\u001F'`, not the raw control character

The brief showed it as a literal U+001F, which renders invisibly. Same byte at runtime — verified
`U+001F` — but a raw control character in source is invisible to a reviewer and does not survive
copying reliably. The value is verbatim; the spelling is not. Flagged because "verbatim" was the
instruction.

## 7. `deriveRowId` validates the instance id shape; the C# did not need to

C# `Guid` with the `:D` format specifier can only produce the lowercase hyphenated 8-4-4-4-12 form.
In Node an instance id is a string, and `Buffer.from(hex, 'hex')` truncates silently at the first
non-hex character rather than throwing — so an uppercase or braced instance id would hash a
different byte string and mint a different, plausible-looking id for the same run. It now throws
with the offending value. An addition to the port, not a deviation from it.

## 8. Column set from Prisma, type precision from the dumps — reconciled, not merged

The generator derives each Postgres type from Prisma and **asserts it equals the dump's** for every
column the dump carries (22 asset, 20 exposure). That cross-check is what makes the derived types
for the eight dump-absent columns trustworthy — the derivation is proven on 42 columns before being
trusted on eight.

Nullability and defaults come from the dump where present, because Prisma forbids `?` on a list
type and would therefore call all three `text[]` columns NOT NULL when the database calls them
nullable.

`CONTRACT_PROVENANCE.typeUnverifiedColumns` names the eight: `batch_id` on both tables plus the six
cloud columns (`sub_type`, `cloud_platform`, `region`, `cloud_account_id`, `cloud_account_name`,
`cloud_provider_url`).

## 9. Prisma's column order preserves the C# exposure content order

Prisma puts `batch_id` third where the dumps put it last, and appends the six cloud columns after
`created_at`. Both are excluded from the content hash, so the ten hashed columns land in the same
relative order under either source:

```
name, display_name, type, severity, status, first_seen, last_seen,
additional_fields, description, mitigation
```

Recorded in a comment in the generator, because a reorder re-mints every exposure id and nothing
else would flag it.

## 10. Timestamps typed `Date`, not `string`

`Date` is millisecond-precision natively and so is `timestamp(3)`, which makes recon's L10
truncation hazard structural rather than a rule someone has to remember.

## 11. Notes the engine and mapping-data workers need

- `assertMappingLoadable` checks **literals only** against enums. A `recordField` routed straight
  into an enum column is legitimate — Tenable already states `high` and `critical` — so per-record
  enum validation remains the engine's job. The function's doc comment says so; the load-time check
  claims no more than it delivers.
- `EXPLODED_COLUMN = 'cve_id'` is the one `text` column whose mapping may yield a list. Tenable's
  `$.plugin.cve` is an array; Falcon's `$.cve.id` is a scalar. The engine must accept both,
  de-duplicate within a record (a repeat derives the same id twice and collides on the target's
  primary key), and count records naming no CVE rather than emitting a null key.
- `additionalFields.carry: 'unclaimed'` claims a field when **any** mapping reads it, top level
  only. Adding a mapping for `$.cve.description` claims all of `cve` and removes it from
  `additional_fields`, re-minting every exposure id in the lane, because `additional_fields` is one
  of EA's twelve fingerprint columns and feeds this repo's content hash. `'wholeRecord'` cannot do
  that, at the cost of storing values twice. Both Falcon mappings I exercised use `'wholeRecord'`.
- `planColumns(table, written, exclusions)` is the shared source of the content-hash column order.
  Do not filter `table.columns` independently anywhere.
- The mapping path grammar is deliberately wider than the manifest's `parentKey` grammar: deep
  paths and `[0]` indices, so `$.remediation.entities[0].action` parses. It lives in
  `src/mapping/spec.ts`; `src/manifest.ts` is unchanged.
- A repro of all three real mappings plus every negative case is at
  `<scratchpad>/verify-spec.mjs`. Useful as a starting point for whoever owns `tests/` and
  `mappings/`.

## Where the brief was wrong or underspecified

- **"`npx tsc --noEmit` passes (tsconfig is already configured)"** — it does not, on two independent
  counts. See the verification section above.
- **"`types: []` is deliberate"** — it may be, but it is also why nothing in the repo typechecks
  without out-of-tree node types. Anyone verifying needs the same workaround or they will report the
  phase-0 files as broken.
- **`KEY_SEPARATOR`** was rendered as a bare invisible character in the brief. Recoverable only
  because `constraints.md` and the recon both say U+001F in words. A future brief should write
  `'\u001F'`.
- **"A mapping DECLARES every manifest field it reads"** is ambiguous between an author-written list
  and a derivable property. Read as the latter, which the lead has since adopted — but it needed a
  decision rather than a reading.
- **`assertColumnCoverage`'s scope was underspecified.** The brief asked only for "neither mapped
  nor explicitly excluded-with-reason is a construction-time error". Three more failures of the same
  class were added, because each is a way an author believes a column is handled when it is not:
  - an exclusion naming a nonexistent column — a typo, which silently leaves a real column uncovered
  - an exclusion with a blank reason — an oversight wearing a decision's clothes
  - a column both written and excluded — the write happens while the content hash omits it, so two
    rows differing only in that column derive the same id

  If the narrow version is wanted, the extra three are separable.
- **The brief's `apps` framing is slightly off.** It says the C# labeler sourced `display_name` from
  `finding.apps[0].product_name_version` and that `apps` is absent from the manifest **and** the
  data. Correct — but that leaves the *passing* Falcon findings mapping with no `display_name`
  source at all, which the brief does not address. Phase 0 used
  `$.remediation.entities[0].title` ("Update Google Chrome Enterprise") in its verification
  mapping: present in the data and a reasonable display name. Mapping data is worker C's to own, so
  treat that as a suggestion with a working precedent, not a phase-0 decision.
- **Table names.** Confirmed plural, as the brief said: `parser_output_assets_enrich`,
  `parser_output_exposures_enrich`. The singular form does not exist in either cluster.
