# Assumptions

Ids are stable. Statuses written by the verifier are terminal.

## Prior Art

Recall tags: `falcon`, `cymulate-integration-parsers`, `cymulate-exposure-analytics`, `entity-ids`.
Matched 69 rows, triaged the newest 10, followed 1 task pointer
(`2026-08-30_1247_falcon-unmanaged-assets-never-lost`). No superseded or retracted row entered the set.

- **A1** VALIDATED — falcon-unmanaged-assets-have-no-aid — A Falcon Discover asset without a stated sensor AID has no derivable AID, so the parent key for those assets must come from the combined `id`. source: lessons.md#L-29828585 (verifier, 2026-08-30). Verify command re-run this session: the 32-hex gate is still at `AidExtractor.cs:159` and `ExtractRecordKey` already falls back to the combined `id`.
- **A2** NEVER-TESTED — widened-identity-untested-downstream — A widened identity value reaching parser output columns was never probed against the downstream match key that consumes it. source: lessons.md#L-2186c9a2 (verifier, 2026-08-30).
- **A3** NEVER-TESTED — policy-edge-keys-resolve — Policy edge keys resolve against Exposure Analytics' asset match key. source: lessons.md#L-cc7eaeb4 (verifier, 2026-08-27).
- **A4** VALIDATED — nested-json-lands-native — A nested `value` reaches Postgres as native JSON rather than a double-encoded string. source: lessons.md#L-85e358c4 (verifier, 2026-08-27).
- **A5** NEVER-TESTED — readable-sql-parses — Raw SQL that reads correctly to a reviewer parses correctly in Postgres. source: lessons.md#L-a8ddf024 (verifier, 2026-08-30) — recorded REFUTED there; treat SQL as unproven until it executes.
- **A6** NEVER-TESTED — fql-last-seen-returns-every-host — Falcon FQL `last_seen_timestamp:>=` returns every host, so a date-gated Discover scroll loses nothing. source: lessons.md#L-fd80d22b (verifier, 2026-08-30) — recorded REFUTED there.
- **A7** NEVER-TESTED — devices-v2-accepts-1000-ids — `POST /devices/entities/devices/v2` accepts about 1000 ids per request. source: lessons.md#L-49fe697c (verifier, 2026-08-27).
- **A8** NEVER-TESTED — falcon-404-is-http-404 — A Falcon 404 always arrives as an HTTP 404 status line. source: lessons.md#L-33ce251b (verifier, 2026-08-27).

## Classified Prior Art

Delta lookup on the plan's classes (`entity-ids`, `persisted-state`, `vendor-field-presence`). One row
newly relevant, not covered by the designer's recall:

- **A9** VALIDATED — new-postgres-raw-sql-is-correct — Postgres raw SQL newly added for a feature is correct. source: lessons.md#L-a561ce63 (verifier, 2026-08-27) — recorded UNTESTED there, in the same class as this prototype's writer.

## This task

- **A10** VALIDATED — label-contract-generatable — The label contract can be generated mechanically from the EA schema dumps plus Prisma, without hand-authored column or enum lists.
- **A11** NEVER-TESTED — dumps-current-enough — The pg_catalog dumps under `database-schemas-up-to-date/` are current enough to generate against. Known to lag: `batch_id` was added by migration `20260630120000` after the 2026-07-10 prod-eu dump, and is present in the stg dump.
- **A12** VALIDATED — enum-members-recoverable — Enum member values for the five `cybi.*` enums are recoverable without a live cluster.
- **A13** NEVER-TESTED — uuidv5-satisfies-ea-model — A uuidv5 over `(instance_id, vendor parent key)` satisfies EA's `(id, instance_id)` PK and its generation model, and reproduces identically on a reparse of the same batch.
- **A14** VALIDATED — spotlight-finding-carries-aid — Every Falcon Spotlight finding carries `aid`, so the thin collector needs no stamping for the findings lane on this vendor.
- **A15** NEVER-TESTED — lab-collection-is-short — ~300 hosts and ~100k findings from the lab tenant is a short enough collection to run repeatedly during development.
- **A16** VALIDATED — npgsql-copy-handles-types — Npgsql binary COPY can write `text[]`, `jsonb`, and `timestamp(3)` columns for these two row shapes without a per-column special case.
- **A17** REJECTED — no-orphan-findings-on-falcon — Findings whose parent key matches no asset in the same batch do not occur on this vendor. If they do, the exit bar's zero-unresolved-key proof fails and the failure policy question becomes blocking.

## Verifier disposition (verifier-1, 2026-09-03)

Statuses above are terminal. Citations for every promotion, and the precise scope of each, are in
`review/verifier-1.md` §3. Summary of what moved and what did not:

- **VALIDATED (7):** A1, A4, A9, A10, A12, A14, A16 — each on a re-run command, a live SQL result, or
  a named passing test, not on the work having completed.
- **REJECTED (1):** A17 — orphan findings do occur on this vendor. Zero holds only at the default
  2000-01-01 floor; the two lanes gate on different vendor clocks
  (`src/ThinFalconCollector/Vendor/FalconUrls.cs:49,54`).
- **NEVER-TESTED (9):** A2, A3, A5, A6, A7, A8, A11, A13, A15. A2/A3/A7/A8 are external-behaviour
  claims this task never exercised — the collector never calls `devices/v2`, never handles a 404, and
  no Exposure Analytics code was executed. A13 is a conjunction whose reparse and PK halves are
  proven but whose EA-generation-model half has nothing behind it, because `instance_id` is pinned
  (`src/ThinFalconCollector/PrototypeScope.cs:24`).

No git diff was available for this disposition: the repo has zero commits and zero tracked files, and
`state.json.baseRef` is the empty-tree hash. Evidence is scoped to re-runnable commands and the
artifacts named in `execution_notes.md`.
