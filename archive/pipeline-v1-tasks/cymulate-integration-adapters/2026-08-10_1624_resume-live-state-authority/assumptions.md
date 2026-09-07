# Assumptions

Every entry is OPEN. This skill holds no evidence; only the researcher, executor, or verifier may move
a status, and only with an actor and a citation.

## Prior Art

Recall tags: `cymulate-integration-adapters`, `falcon`, `checkpoint-resume`, `recovery`.
Ledger: `~/codex-state/lessons.md` (75 lines). Two relevant hits, both in-repo Falcon. One task
directory read (cap 3).

- **PA1 — OPEN** — Falcon batch bounds cannot be assumed from documentation. A prior task assumed
  `POST /devices/entities/devices/v2` accepts 5,000 IDs; a live run returned HTTP 400 at 250 AIDs while a
  single-AID probe on the same session returned 200. Bears on the `AidBatchSize` 50→6 decision (B3) and on
  the suspected nginx URL-length cause. That task's decision 3 also pins prevention-policy enrichment to
  "one existing AID batch", so `AidBatchSize` is not an isolated knob — changing it multiplies assignment
  lookups. source: lessons.md#2026-07-26-falcon-batching (executor, 2026-07-26)
- **PA2 — OPEN** — A Falcon fix has previously outlived the reason for it: sorting at emission was kept
  after the belief that motivated it (unstable entity IDs) was disproved. Bears on the disposition of the
  6.1.1 clamp (B1) and the 6.1.2 502 retry (B2) — neither should be retained by inertia.
  source: lessons.md#2026-07-02-falcon-entity-ids (executor, 2026-07-02)

Considered and not seeded: `2026-04-29 | AgentService | tenable | checkpoint-resume` — its lesson ("do not
assume a resume path exists without pointing at the checkpoint write") is already satisfied here; the
checkpoint write is cited at `FalconFindingsCheckpointWriter.cs:56-80`.

## Task assumptions

- **A1 — OPEN** — Seeding persisted collector keys into `AdapterState` at resume does not disturb the
  recovery-budget stuck gate or the whole-collection backstop. Reasoned from `AdapterRecoveryBudget.cs:179-184`
  (the coordinate derives from host-restored counters, not `AdapterState`) but not exercised.
- **A2 — OPEN** — The Falcon *findings* recovery hook has no load-bearing behaviour beyond restoring
  state, so it can be deleted once seeding lands. `FalconRecoveryContinuationBuilder.cs:50-55` is a bare
  `return context.WorkItem;`, which supports this, but the deletion has not been exercised against the
  deferral path.
- **A3 — OPEN** — The Falcon *assets* recovery hook does have load-bearing behaviour — dropping the dead
  `after` cursor and re-anchoring `MonthSegmentFloorUtc`, plus declining when no watermark exists — so it
  must be reduced rather than deleted.
- **A4 — OPEN** — The eleven hookless collectors currently wipe their collector state on a pre-publish
  deferral of a resumed leg. Deduced from `CollectorResumeSetup.cs:65-69` plus
  `AdapterFailureDecisionExecutor.cs:186-189` (a null hook honours the wait and persists live state);
  never observed in a run or a test.
- **A5 — OPEN** — `aidBatchSize` is settable end to end from the platform integration configuration, not
  only in code. `FalconCollectorConfigurationBuilder.cs:162-165` reads and clamps it (1–5000); the
  platform-side plumbing that supplies that key has not been confirmed.
- **A6 — OPEN** — The 6.1.2 in-process 502 retry is load-bearing in prod-eu today. It is absent from
  `origin/dev` (0 matches for `BadGatewayMaxAttempts`) and present on the deployed 6.1.2 DLL, so a
  dev-based fix removes it on deploy. Whether its removal regresses anything once the position stops
  rewinding is untested — its own comment justifies it by the very defect being fixed here.
- **A7 — OPEN** — The 6.1.1 clamp is inert for traversal and harmful for reporting. It is present on dev.
  `FalconFindingsFlow.cs:195-208` clamps only the host page counter while `:236` still passes the
  unclamped `lastCompletedOutputPage` to the walk, and `RestoreProgress(restoredPage, totalFindings,
  totalFindings)` overwrites host item counters downward. Consistent with the operator-supplied row where
  page rose 86→89 while items fell 2.66M→1.11M, but that row is external evidence, not a test.
- **A8 — OPEN** — Bumping the pinned Infra version in adapters does not pull unrelated Infra changes that
  break other collectors. Infra `origin/dev` is at the same commit as local `dev` (`6dccb87`), which
  supports a narrow delta, but the full adapters suite has not been run against it.

## Operator-supplied evidence (outside the repo evidence set)

Not assumptions; recorded so the verifier does not treat them as code-derived.

- Live checkpoint row, correlation `6a798425002cdef0d1b947bf`, prod-eu 2026-08-10 15:01:45:
  `current_page` 89, items 1,111,330, `checkpoint_kind` PageAdvance, `adapter_state_json`
  `lastCompletedOutputPage` = 5, `totalDeferralCount` 15, `lastProgressCoordinate` "86:2659187:2659187".
- S3 for that correlation holds addresses through 25 with 17–20 absent. Structural, not loss:
  `FalconPhase1Manifest.cs:68,90` give `BatchesPerPage` 20; staged page 0 = 761 hosts (16 batches),
  page 1 = 234 hosts (5 batches).
- 44 object writes onto 12 distinct addresses on the prior correlation; an earlier run froze at output
  page 72.
- Deployed prod-eu collector is 6.1.2 (zip pushed 2026-08-10 15:10:22; confirmed by UTF-16 literal probe
  of the deployed DLL).
