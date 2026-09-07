# Assumptions

## Prior Art

Tags: `cymulate-integration-adapters`, `falcon`, `policy-edge`, `batching`,
`memory-bound`, `resilience-routing`, `cursor-expiry`.

Matched 14 rows; triaged the newest 9 as materially in-class, dropped 5 as
off-class (streaming-transport x3, rate-limits, deploy-verification). No
`supersedes:` or `retracts:` fields exist anywhere in the ledger, so no
head-filtering was required. Followed 3 task directories:
`2026-07-26_1655_falcon-prevention-policy-enrichment`,
`2026-08-20_1101_falcon-policy-s3-parser-e2e-validation`,
`2026-08-26_1001_falcon-spotlight-transport-retry`.

- A1 NEVER-TESTED — `POST /devices/entities/devices/v2` accepts ~1000 ids per request. A live run previously got HTTP 400 at 250 AIDs while a single-AID probe on the same session returned 200, and the 400 body still carried populated `resources`, favouring a rejected id over a hard cap. source: lessons.md#L-16fed2de (executor, 2026-07-26). Counter-evidence in favour: prod-eu correlation 6a8f00dd66c86fb72e4e45a8 made 9 clean HTTP 200 POSTs at page size on 2026-08-26.
- A2 NEVER-TESTED — A Falcon 404 arrives as an HTTP 404 status. `spotlight.pdf` p19 states a 404 "displays with a 200 OK header and the 404 code under errors in the response body" for at least one endpoint; if that also holds for the policy endpoints, a status-only defense never fires and rejections pass silently. source: lessons.md#L-882455c1 (researcher, 2026-08-18). verify: `grep -n 'IsServerError\|StatusCode ==' src/Cymulate.Integration.Adapters/Collectors/FalconCollector/Flows/SharedFlows/FalconHttpFailureClassifier.cs`
- A3 NEVER-TESTED — Policy edge keys resolve against Exposure Analytics' asset match key, so policy-to-asset edges land. Previously REFUTED. Restructuring the edge emission risks reintroducing it. source: lessons.md#L-f3058a37 (verifier, 2026-08-20). verify: `select count(*) from cybi.asset_policy ap join cybi.asset a on a.value = ap.asset_match_key` — zero reproduces the defect.
- A4 NEVER-TESTED — `rules[].value` reaches Postgres as native JSON. Previously REFUTED via double-encoding. Moving definitions into a plane object changes the serialization path that broke it. source: lessons.md#L-5de41efb (verifier, 2026-08-20). verify: `select distinct jsonb_typeof(value) from cybi.security_policy_rule` — `string` reproduces the defect, `object` is correct.
- A5 NEVER-TESTED — Per-batch and per-stage memory cost is known well enough to trade against. Previously UNTESTED twice; the observed figure at AidBatchSize 10 was 90-125 MB against a 35-50 MB target. A ~1000-AID assignment batch plus an in-memory AID->policy map is a new allocation this task introduces. source: lessons.md#L-9ad5a354, lessons.md#L-85a32cda (verifier, 2026-08-26 / 2026-08-18).
- A6 NEVER-TESTED — Clamping a retry ladder's configuration inputs bounds how long the ladder can stall the pipeline. Previously DRIFTED. Relevant because this task adds a classifier arm rather than a clamp. source: lessons.md#L-40bca6e4 (verifier, 2026-08-26).
- A7 REJECTED — The findings flow shares the assets flow's checkpoint-writer paths. Previously REFUTED. The staged ledger touches resume coordinates for both flows. source: lessons.md#L-dae004f6 (verifier, 2026-08-26). verify: `grep -rn 'OnTerminalSnapshotWithoutPublishedPage' src/Cymulate.Integration.Adapters/Collectors/FalconCollector/` — call sites must all be under `Flows/Assets/`.
- A8 NEVER-TESTED — Emission order does not affect identity. Previously the parser had `entities[0]` nondeterminism from array ordering. Edge emission order in the new plane must be deterministic. source: lessons.md#L-c9239f9c (executor, 2026-07-02).

## Task assumptions

- A9 NEVER-TESTED — `device_policies.prevention.settings_hash` exists and is populated. NOT vendor-documented; searched official CrowdStrike docs and FalconPy source, NOT FOUND. Evidence it exists: `crowdstrike_policy_projection.py` reads `assignment.settings_hash` in production. verify: `grep -n "settings_hash" /Users/user/Dev/cymulate-integration-parsers/libs/packages/parsers/crowdstrike/crowdstrike_policy_projection.py`
- A10 NEVER-TESTED (MOVED in cycle 2 — the endpoint is now actually called; see the verifier-2 table) — `GET /policy/combined/prevention/v1` returns `precedence` and an `is_default` / platform-default indicator per policy. Endpoint identity and its `limit`/`offset` bounds are confirmed from falconpy `_endpoint/_prevention_policies.py`; the per-policy field list is not.
- A11 NEVER-TESTED (MOVED in cycle 2 — load-bearing again once the sweep landed; see the verifier-2 table) — ~12 distinct prevention policies is representative for this tenant. Source is a POC run against 23 devices (`prevention_policies_distinct: 12`), which is not scale evidence for a 97,332-host tenant.
- A12 VALIDATED — Writing the manifest before policy work does not create a resumable state where spotlight reads host pages that a later policy stage would have altered. Depends on host-page immutability holding in fact, not just in intent.
- A13 VALIDATED — The mid-scroll 500 re-anchor is lossless. It reuses the watermark plus boundary-AID dedup that the existing `FalconCursorExpiredException` arm relies on, whose own losslessness is asserted in comments but not proven by a test.

## Classified Prior Art

- A14 VALIDATED (stated null-return mechanism REJECTED) — The staged ledger fits inside the manifest read ceiling. `TryReadManifestAsync` caps at `MaxControlArtifactBytes` (default 1 MB) and an over-ceiling manifest returns null, which is deliberately indistinguishable from absent, so the run re-spools silently rather than failing. source: research/internal-recon.md (d); FalconStagingArea.cs:250-253; IngestionOptions.cs:38,92 (recon, 2026-08-27). verify: `grep -n "MaxControlArtifactBytes" /Users/user/Dev/IntegrationInfra/src/IntegrationInfra/Ingestion/IngestionOptions.cs`
- A15 REJECTED — Classifying `DataPipelineException` as non-retryable is safe. The type is overloaded across an over-ceiling refusal, a read-slot timeout and a null stream; the read-slot timeout is transient, so a flat non-retryable arm would make a transient fault terminal. source: ai/skills/collector-flow-patterns/SKILL.md; GuardedObjectStore.cs:274-289,361-379 (recon, 2026-08-27). verify: `grep -n "DataPipelineException" /Users/user/Dev/IntegrationInfra/src/IntegrationInfra/Ingestion/GuardedObjectStore.cs`

## Disposition (verifier-2, 2026-08-27 — TERMINAL, supersedes verifier-1's table)

Rebuilt from the code by the verifier pass recorded in `review/verifier-2.md` §2, after the three repair
rounds. Never-tested was the default: a status moved only on a specific diff hunk, test result, grep output
or source line. The fact that the work completed and that 357 tests pass was NOT treated as evidence for any
assumption. Verifier-1's table is retained below for history; where the two differ, THIS table is terminal.

Two rows MOVED in substance without moving in status, because the repairs changed what they bear on: A10's
endpoint is now actually called, and A11 became a live sizing input again. Two rows grew their exposure
(A4, A8) because the definitions sweep added a second serialization path and made vendor array order an input.

| id | status | one-line citation |
|---|---|---|
| A1 | NEVER-TESTED | no live vendor call; fabricated `SessionStub` in every rejection/bisection test; code encodes no bound |
| A2 | NEVER-TESTED | no vendor evidence; both shapes coded, each pinned only by a fixture; status alone never decides |
| A3 | NEVER-TESTED | `cybi.asset_policy` verify query not run; no database touched |
| A4 | NEVER-TESTED | `cybi.security_policy_rule` verify query not run; exposure GREW — definitions now also cross the staged NDJSON codec |
| A5 | NEVER-TESTED | no memory figure in any artifact; three unmeasured new allocations incl. the limit=5000 sweep list |
| A6 | NEVER-TESTED | no clamp added or exercised; this task added classifier arms instead |
| A7 | REJECTED | verify grep re-run: all `OnTerminalSnapshotWithoutPublishedPage` call sites under `Flows/Assets/` |
| A8 | NEVER-TESTED | no ordering test; exposure GREW — the sweep pins no `sort`, so vendor order is now an input; risk structurally avoided, not measured |
| A9 | NEVER-TESTED | verify grep re-run: the parser READS `settings_hash`, which is expectation not vendor confirmation |
| A10 | NEVER-TESTED — MOVED (now reachable) | `/policy/combined/prevention/v1` IS called (`FalconPreventionPolicyClient.cs:105`, proven end to end by R5's `sweepCalls == 1`); the per-policy field claim is still unasserted anywhere, and the sweep does not run under the shipped `EnablePreventionPolicyEnrichment = false` |
| A11 | NEVER-TESTED — MOVED (load-bearing again) | the sweep landed, so single-page sizing at `SweepPageSize = 5000` is a live input again; no tenant measurement |
| A12 | VALIDATED | one host-page writer, one caller above the manifest write; staged line shape carries no policy property; Phase 2 entry guarded |
| A13 | VALIDATED (fixture scope) | mid-scroll 500 test passes; dedup sets provably not reset across the arm |
| A14 | VALIDATED (stated mechanism REJECTED) | `R1_...At20kPages` passes on shipped 1 MiB defaults; over-ceiling read THROWS and is converted to a named failure, so null now only means absent |
| A15 | REJECTED | `GuardedObjectStore.cs:374-379` read-slot timeout is transient and is 1 of 5 throw sites; flat non-retryable arm unsafe; landed arm discriminates and `R4_...` passes |

Summary: VALIDATED 3 (A12, A13, A14) · REJECTED 2 (A7, A15) · NEVER-TESTED 10 (A1–A6, A8–A11).

---

## Disposition (verifier-1, 2026-08-27 — superseded by verifier-2 above, retained for history)
Statuses above were set by the verifier pass recorded in `review/verifier-1.md` §2, which carries the
citation and actor for every row. Never-tested was the default: a status moved only on a specific diff hunk,
test result, grep output or source line. The fact that the work completed and the tests passed was NOT
treated as evidence for any assumption.

| id | status | one-line citation |
|---|---|---|
| A1 | NEVER-TESTED | no live vendor call; every rejection test uses a fabricated response |
| A2 | NEVER-TESTED | no vendor evidence; defense coded both ways and pinned only by fixtures |
| A3 | NEVER-TESTED | `cybi.asset_policy` verify query not run; no database touched |
| A4 | NEVER-TESTED | `cybi.security_policy_rule` verify query not run |
| A5 | NEVER-TESTED | no memory figure exists in any artifact; wall-clock is not memory |
| A6 | NEVER-TESTED | no clamp added or exercised; this task added a classifier arm instead |
| A7 | REJECTED | verify grep: all `OnTerminalSnapshotWithoutPublishedPage` call sites under `Flows/Assets/` |
| A8 | NEVER-TESTED | no ordering-determinism test; risk structurally avoided, not measured |
| A9 | NEVER-TESTED | edge carries the vendor assignment verbatim; field never inspected |
| A10 | NEVER-TESTED | `/policy/combined/prevention/v1` is not called — `BuildPreventionPolicySweepUrl` has no caller |
| A11 | NEVER-TESTED | no tenant measurement; claim no longer load-bearing after D7 drift |
| A12 | VALIDATED | one host-page writer above the manifest write; staged page carries no policy property; `FalconTwoPhaseFindingsTests.cs:574,3378` |
| A13 | VALIDATED (fixture scope) | `FalconTwoPhaseFindingsTests.cs:3696` asserts one generation, dedup, each host emitted once |
| A14 | VALIDATED (mechanism REJECTED) | `R1_...At20kPages` passes on shipped defaults; over-ceiling read THROWS, it does not return null |
| A15 | REJECTED | read-slot timeout (`GuardedObjectStore.cs:374-376`) is transient; flat non-retryable arm unsafe; `R4_...` pins the discrimination |
