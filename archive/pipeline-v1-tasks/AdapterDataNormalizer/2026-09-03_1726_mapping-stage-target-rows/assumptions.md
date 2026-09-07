# Assumptions

Every entry here is OPEN. This skill holds no evidence and may not write any other status.

## Prior Art

Tags: `adapter-data-normalizer`, `cymulate-exposure-analytics`, `falcon`, `tenable`, `entity-ids`,
`enum`, `exposure-identity`.

Matched 57 of 83 ledger rows; triaged the newest 10. Dropped `L-57acd253` as superseded. Followed
one pointer: `tasks/adapter-data-normalizer/2026-09-03_1031_thin-ingest-normalizer-prototype/lesson.md`.

Three recalled `verify:` commands were run rather than taken on faith. Two re-confirmed and one
surfaced a new ambiguity; all three are recorded below as OPEN, because a re-confirmed lesson is
still stale evidence about a system that may have changed.

- A1 OPEN — A derived row id scoped per `(instance_id, vendor parent key)` will exercise Exposure
  Analytics' generation model. The C# prototype pinned `instance_id` to a constant, so the
  `latest = false` demotion was untestable by construction.
  source: lessons.md#L-ca4fa812 (verifier, 2026-09-03)
- A2 OPEN — Keying an exposure row on `(host, CVE)` is insufficient, and EA's real identity is a
  twelve-column content md5. The fingerprint SQL re-confirmed verbatim this session at
  `parsed-data.repository.ts:70-76`, but the 31%-collapse measurement behind it comes from one
  tenant and one vendor.
  source: lessons.md#L-b98fca67 (verifier, 2026-09-03)
- A3 OPEN — The committed pg_catalog schema dumps are current enough to generate a target contract
  against. Re-confirmed only for internal consistency: `batch_id` appears 3 times in the stg dump
  of `parser_output_assets_enrich` and 0 times in prod-eu. Currency against a live cluster was
  never checked, and the prod-eu dump is dated 2026-07-10.
  source: lessons.md#L-aa0427b1 (verifier, 2026-09-03)
- A4 OPEN — A vendor nested value reaches Postgres as native JSON rather than a double-encoded
  string in every column that carries one. Bears directly on `additional_fields` (`jsonb`).
  source: lessons.md#L-6f6ccb67 (verifier, 2026-09-03)
- A5 OPEN — An 89-character combined id is safe in parser-output columns that have only ever
  carried 32-character AIDs. Column width for the Falcon Discover key is unverified.
  source: lessons.md#L-2186c9a2 (verifier, 2026-08-30)
- A6 OPEN — A widened identity value reaching parser-output columns lands correctly in the
  downstream match key that consumes it.
  source: lessons.md#L-88abb13f (verifier, 2026-09-03)
- A7 OPEN — Falcon findings can arrive without a matching asset in the same collection batch, so
  orphan tolerance is a policy choice rather than an impossibility.
  source: lessons.md#L-cc8f607b (verifier, 2026-09-03)
- A8 OPEN — The S3 Tenable samples are enough to show a second vendor shape fits the same mapping
  engine with no code change. The reader half was shown this session; the mapping half was not.
  source: lessons.md#L-1e7de7bb (verifier, 2026-09-03)

## Task assumptions

- A9 OPEN — `~/Dev/cymulate-exposure-analytics/cybi-db-models` is the authoritative enum source,
  not the top-level `~/Dev/cybi-db-models`. The two are at different commits (`3d26e24e`,
  2026-08-02 vs `98b95949`, 2026-05-06) and their `enum.prisma` differs by one `asset_type`
  member, `cloud_resource`. Only the EA-nested copy yields the member counts this task was
  specified against (asset_type 23). Neither has been checked against a live cluster.
- A10 OPEN — Excluding the `@@schema("cybi")` directive line is the correct way to count enum
  members. Including it inflates every count by one, which is how the specified counts appeared to
  disagree with the file on first read.
- A11 OPEN — The `@map` value is what a Postgres cast requires, so `windows-server` and
  `active-directory` are the strings to emit. Read from `enum.prisma:79,89`; not exercised against
  a live cast in this repo.
- A12 OPEN — A mapping's declared field reads can be verified against the manifest at load time
  without the mapping format becoming so restrictive that a real vendor mapping cannot be
  expressed in it. This is the central design bet of the slice.
- A13 OPEN — Mapping and content hashing can be applied per record without buffering, so the
  measured streaming property survives the new stage. Unproven for the hashing step specifically.
- A14 OPEN — The twelve fingerprint columns can all be produced at the mapping stage. `asset_id`
  is one of them, so the exposure row's content hash depends on the asset id already being
  derived — an ordering constraint that has not been walked through.
- A15 OPEN — The `parser_output_*_enrich` column sets can be read from the committed dumps without
  a live cluster. Depends on A3.

## Classified Prior Art

Delta lookup on the chosen classes `exposure-identity`, `entity-ids`, `schema-currency`. Two rows
newly relevant beyond A1-A8. The `cross-repo-contract` matches (L-9d08e415, L-b1b66b36, L-645966cc)
were dropped: they concern ISB wire-message package drift, and this task ships no cross-repo
package.

- A16 OPEN — A vendor parent key derived for an asset that states no sensor AID is unique within a
  lane. The prior belief that `AidExtractor.ExtractAid` could derive one was REFUTED. This task's
  ids are `uuidv5(namespace, instanceId + parentKey)`, so a non-unique parent key silently collapses
  two assets into one row. Uniqueness of the 249 `$.id`-resolved Falcon asset keys has not been
  measured.
  source: lessons.md#L-29828585 (verifier, 2026-08-30)
- A17 OPEN — Canonical content hashing is insensitive to vendor array ordering. A prior task found
  ids identical across runs while only array order differed, and adopted sort-at-emission to remove
  the nondeterminism; the lesson recorded is not to conflate unstable ordering with unstable
  identity. Here the risk runs the other way: if the canonicalizer does not order array elements,
  the same content yields different ids across runs and reparse stability breaks.
  source: lessons.md#L-c9239f9c (executor, 2026-07-02)

## Assumptions added after the verifier pass (orchestrator)

- A18 OPEN — The C# prototype's `FindingLabeler.cs` reflects the production vendor→target mapping.
  **This was assumed, never stated, and is false.** Production maps
  `display_name` to `vulnerability_id`
  (`cymulate-integration-parsers/libs/packages/parsers/deprecated/crowdstrike/crowdstrikeAssetsFindings.py:73`),
  the same field as `name`, deliberately duplicated. The prototype's
  `apps[0].product_name_version` was its own invention, and production explicitly PRUNES `apps` as a
  heavy field (`_HEAVY_FINDING_FIELDS`, ~69% of each document), so it was never available to the
  real pipeline either. Recorded as OPEN pending the three-repo audit now running; it will be
  REJECTED with that citation.
- A19 OPEN — The production parser is the authoritative specification for the vendor→target mapping,
  and every divergence from it must be deliberate and recorded. The mapping in `mappings/` was built
  without consulting it.
- A20 OPEN — Production's `status` mapping emits `open`, which is not a member of
  `cybi.exposure_status` (`opened`, `resolved`, `reopened`). Either a normalization exists somewhere
  on the enrich path, or production has been writing a value that gets filtered. Under audit.
- A21 OPEN — Production emits `type` as `"Host"` and `os_type` as raw `platform_name` ("Windows"),
  while the enums are lowercase. A lowercasing step must exist somewhere or those rows are being
  dropped today. Under audit.
- A22 OPEN — Production never derives `windows-server` or `active-directory`; it passes
  `platform_name` through. If true, those two enum members exist for a producer other than this
  parser, and this slice's `decide` block is more specific than production rather than matching it.
