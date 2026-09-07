# Independent verifier — Falcon policy S3/parser end to end

Verification date: 2026-08-20

## Overall verdict

**Validation result: PASS. Staging rollout verdict: FAIL / NO-GO.**

The task produced sufficient, independently reconcilable evidence to answer the
contract. The collector, final S3 artifact, merged parser replay, and isolated
PostgreSQL persistence all work. The complete producer-to-consumer feature is not
staging-ready because the real payload proves two semantic incompatibilities with
the current Exposure Analytics consumer:

1. all 47 policy edge keys are Falcon AIDs while the consumer joins against
   hostname/IP-valued `cybi.asset.value`; the actual replay matched 0 of 47 keys;
2. all 939 `rules[].value` values persist as JSON strings containing encoded
   objects, so the consumer's native JSON field extraction yields no toggle or
   slider values.

No production write or product-source modification was used in this verifier
pass. Aggregate-only read queries were run against the task-created PostgreSQL
container on `127.0.0.1:55432`; the S3 check was read-only.

## Criterion results

| Contract criterion | Result | Independent finding |
|---|---|---|
| Collector terminal status | PASS | Log lines 846, 860, and 870 show successful completion at 14:03:00 +03:00 for correlation `a2d4279b-38dd-4293-a66f-7496d402f012`, with 47 hosts and 124,983 findings. |
| Exact S3 inventory and readable final object | PASS | Read-only listing returned the enriched staged page (969,218 bytes), manifest (368 bytes), and `findings_000001.json` (324,046,289 bytes) under `s3://cybi-data/Uri-Tests/falcon-with-policies/001/`. The downloaded final object's SHA-256 independently equals the log hash `98dc0daf5a515e8a8d7c340c61d95bfd1e4efb029a86dc658371da55daecc48e`. |
| Policy envelope in actual artifacts | PASS | Direct aggregate inspection found 91 final records, 47 distinct AIDs, 91 `collection_status=complete`, 91 `definition_status=resolved`, and no finding-count mismatch. The staged page also contains 47 version-1, complete/resolved `host.device_policies` envelopes. |
| Merged parser revision and actual-artifact execution | PASS | Replay used parser `83626813d1b90caf0b814908c05adf1703e97062`; it is an ancestor of refreshed parser `origin/master` `743293d2cd41a8d971fab5026f26ee1b95db44ec`. The production option/preparation/concrete-parser path consumed the exact downloaded bytes and produced 47 assets, 124,918 findings, and 19 policies. |
| Finding-count reconciliation | PASS | Raw count 124,983 minus exactly 65 vulnerability records lacking `cve.id` equals parser output 124,918. `BaseParser` explicitly filters vulnerability rows whose `cve_ids` array is empty (`base_parser.py:262-318`), so the delta is intentional CVE-only shaping rather than replay loss. |
| Policy projection/schema/stable IDs | PASS | The sanitized result records the exact 15-column vendor projection, 19 distinct non-null `external_id` values, and the expected `crowdstrike`/`prevention`/`untested` contract. |
| Safe PostgreSQL persistence | PASS | The task-created PostgreSQL 15 container is bound to `127.0.0.1:55432`. Independent readback returned 19 rows, 19 distinct external IDs, zero required ID/scope nulls, one batch, and 47 edge objects. All 19 rows have `settings=array`, `rules=array`, `policy_edges=array`, `additional_fields=object`. |
| Rule JSON semantic compatibility | FAIL | Independent PostgreSQL expansion returned 939 rule values, all JSONB `string`. Native extraction returned `toggle 822/0 enabled`, `slider 8/0 detection`, and `ml_slider_pair 109/0 detection`, confirming the double-encoding defect. |
| DB-model source contract | PASS | Refreshed `cybi-db-models origin/master` is `6ba627b08f2fb087e686886ca32fd4c77280d740`; its migrations/models contain the 26-column staging table, required indexes, normalized policy/rule/edge tables, and accepted enums described in W3. |
| Exposure Analytics source presence | PASS | Refreshed `cymulate-exposure-analytics origin/master` is `32c6f8ed3c6f172ae1668d5b3a99ac970037fc7f`; the policy ingestion, rule expansion, edge resolution, paging, and sweep paths are present. |
| Edge semantic compatibility | FAIL | Actual replay found 47 distinct AID edge keys, 43 distinct asset values, and zero matches. Source confirms EA joins `a.value = de.asset_match_key` (`policy.repository.ts:784-800`) while the CrowdStrike parser emits hostname/current IP as asset `value`. |
| Deployed-version boundary | PASS (bounded) | W3 correctly distinguishes source proof and historical DB snapshots from live deployment proof. The exact EA deployed revision was not independently queried and remains NEVER-TESTED. |

## Cross-stage reconciliation

- Collector receipt: 47 hosts, 124,983 findings.
- Final NDJSON: 91 chunks, 47 AIDs, 124,983 summed findings, zero
  `findingsInChunk` mismatches.
- Parser: 47 assets, 124,918 CVE-complete findings, 19 distinct policies.
- PostgreSQL staging: 19 attributable policy rows, 47 edge objects, 939 rule
  objects.
- EA edge join projection: 0 of 47 observed edge keys can resolve against the
  corresponding parser asset values.
- EA rule projection: 0 of 939 encoded rule objects exposes the expected native
  fields through the consumer's current JSON operations.

## Evidence-report corrections — cleared

The closeout recheck confirms all verifier-requested evidence corrections were
applied:

1. W0 now correctly records 47 enriched staged `host.device_policies`
   envelopes.
2. `execution_notes.md` now records the completed S3, parser, PostgreSQL, and
   downstream verification work.
3. The rejected assumption is now stated as the original positive compatibility
   assumption being false.
4. `decisions.md` now resolves AWS access, localhost PostgreSQL, parser
   publication, and the rollout decision.
5. Internal recon now consistently describes the staging table as 26 columns.

No evidence-documentation correction remains open. These corrections do not
change the product-level NO-GO verdict.

## Decision drift and assumption audit

There was no unauthorized scope drift: the execution remained product-source
read-only, used the requested real S3 artifact, confined writes to a synthetic
localhost PostgreSQL scope, and separated source readiness from deployment
readiness. Runtime-only Docker/helper artifacts stayed outside product source.

Assumption dispositions are substantively supported and consistently worded. The
parser-publication assumption is supported by the refreshed ancestry check. The
exact EA deployed binary remains correctly NEVER-TESTED.

### Assumption disposition table

| id | assumption | status | citation | actor |
|---|---|---|---|---|
| A1 | Local Falcon run terminated successfully and exposed its exact S3 prefix/object set in logs | VALIDATED | Collector log lines 846, 860, 870 — completion 14:03:00 +03:00, correlation `a2d4279b-38dd-4293-a66f-7496d402f012`, 47 hosts / 124,983 findings | verifier-1 |
| A2 | Operator AWS identity listed and read every object needed from the run prefix | VALIDATED | Read-only listing of `s3://cybi-data/Uri-Tests/falcon-with-policies/001/`; downloaded object SHA-256 `98dc0daf5a515e8a8d7c340c61d95bfd1e4efb029a86dc658371da55daecc48e` equals the log hash | verifier-1 |
| A3 | All 91 emitted correlated chunks carried the version-1 `device_policies` envelope with stable per-host policy content | VALIDATED | Aggregate inspection of the final NDJSON: 91 records, 47 distinct AIDs, 91 `collection_status=complete`, 91 `definition_status=resolved`, zero `findingsInChunk` mismatch | verifier-1 |
| A4 | Replayed parser commit `8362681` is an ancestor of refreshed parser `origin/master` `743293d` | VALIDATED | Ancestry check against refreshed `origin/master` `743293d2cd41a8d971fab5026f26ee1b95db44ec` | verifier-1 |
| A5 | Isolated Python 3.10 / OpenJDK 11 / Spark runtime consumed the actual 324 MB artifact without production writes | VALIDATED | Production option/preparation/concrete-parser path over the exact 324,046,289-byte object; output 47 assets, 124,918 findings, 19 policies; no production write in this pass | verifier-1 |
| A6 | Task-local PostgreSQL on `127.0.0.1:55432` accepted 19 policy rows through production preparation/JDBC code | VALIDATED | Independent readback on `127.0.0.1:55432`: 19 rows, 19 distinct `external_id`, zero required ID/scope nulls, one batch, 47 edge objects. Engine restated as PostgreSQL 15; the `15.18` patch level was not re-checked | verifier-1 |
| A7 | The positive assumption that master source contracts are mutually compatible — edge identity and nested rule-value representation are claimed compatible | REJECTED | Edge join resolves 0 of 47 AID keys against `a.value = de.asset_match_key` (`policy.repository.ts:784-800`); all 939 `rules[].value` persist as JSONB strings, native extraction yields toggle 822/0, slider 8/0, ml_slider_pair 109/0 | verifier-1 |
| A8 | The exact Exposure Analytics production deployment revision was queried | NEVER-TESTED | Source presence proven at `cymulate-exposure-analytics` `origin/master` `32c6f8ed3c6f172ae1668d5b3a99ac970037fc7f`; live deployed revision never queried, historical DB snapshots prove schema only | verifier-1 |
| P1 | No Falcon device-batch error occurred in this run | NEVER-TESTED | lessons.md#L-16fed2de — no device-batch error path was triggered in this pass | executor, 2026-07-26 |
| P2 | No Spotlight cursor-expiry body occurred in this run | NEVER-TESTED | lessons.md#L-882455c1 — no cursor expiry occurred; the failure mode remains unexercised | researcher, 2026-08-18 |
| P3 | The successful local JDBC write did not exercise a dropped PostgreSQL connection | NEVER-TESTED | lessons.md#L-9a2a1c4d — connection-loss handling unexercised by the localhost replay | researcher, 2026-08-19 |
| P4 | No deployed parser wheel was read; the exact merged source commit was run locally | NEVER-TESTED | lessons.md#L-727045fe — source-commit replay only; no packaged wheel resolved or executed | executor, 2026-08-19 |

Net: 6 VALIDATED confirmed, 1 REJECTED confirmed as rejected, 1 NEVER-TESTED confirmed still
untested, 4 prior-art NEVER-TESTED carried forward. No recorded disposition was overturned by
this pass, and no assumption was found overstated relative to its evidence. A7 is stated as the
original positive compatibility assumption so that REJECTED is the lesson-bearing verdict.

## Exact rollout gate

Do not promote this feature to STG as an end-to-end-ready policy integration yet.
The gate opens only when both conditions are demonstrated on a fresh real-artifact
replay:

1. policy edge keys use the same identity domain that EA resolves (or EA adds a
   proven AID mapping), with all expected real edges resolving; and
2. `rules[].value` reaches PostgreSQL as native JSON consumed correctly by EA,
   with non-zero/expected toggle and slider projections.

After repair, repeat the isolated parser/JDBC replay and then run the EA ingestion
against a non-production database to prove `cybi.security_policy`,
`cybi.security_policy_rule`, and `cybi.asset_policy` outcomes. The current
evidence is strong enough to stop the rollout before STG becomes the integration
test suite wearing a trench coat.
