# Decisions

## Wave 2 decisions (operator-directed 2026-08-10; orchestrator holds the comm from this point)

13. **Release sequence, operator-specified and binding.**
    All independent and cross verifications and reviews pass → both branches rebased on current
    `origin/dev` → Infra version bumped and built FIRST → verified via local feed seam → tested locally
    with `LocalAdapterRunner` → reverted to the per-package seam → ISB and adapters take the new version →
    adapters aligned studiously with tests updated, no breakage → adapters version bumped last.

14. **ISB is now in scope for a pin bump only.** Previously ruled out entirely. ISB pins
    `Cymulate.Integration.Client` at `1.1.0-preview.0` (`Directory.Packages.props:119`). Operator directed
    that ISB take the new version. One line, no code. Justified by the ledger entry recording that Client
    has drifted between the two repos in both directions.

15. **`AidBatchSize` default 50 → 10; this tenant configured to 6.** Operator decision, evidence-based
    (observed 502 volume and deferral counts), explicitly not a guess. A single global value tuned to the
    heaviest tenant observed would penalise lighter ones: ~8 MB/host is one tenant's measurement, not a
    fleet constant. 6 is a per-integration config value, not a code default. `BatchesPerPage` moves 20 → 100;
    anything assuming the old value must be re-derived. PA1 stands — batch bounds are vendor-risky, but
    shrinking is the safe direction.

16. **`CheckpointCreatedUtc` is deleted from the Falcon state blob; the staleness gate reads
    `AdapterCheckpoint.CreatedAtUtc`.** Operator asked for alignment with what ISB/Client wants the field to
    be. Answer from source: ISB already supplies it (`ProcessEventCommandHandler.cs:1346`,
    `CreatedAtUtc = entry.CreatedAtUtc`), and the durable column is insert-only — absent from the UPSERT
    `SET` list — so it measures how long the run has been checkpointing, which is precisely what a
    data-drift gate wants, and no collector can rewind it. This retires the third failure mode by removing
    the field rather than repairing it. Supersedes the earlier decision not to re-stamp: the question is moot
    once the collector stops owning the value.

17. **Fifth failure mode: fail fast, do not migrate.** Falcon findings throws at configuration time if
    `BatchScopedStorage` is enabled, naming the coordinate mismatch. Default stays false. A loud config
    error replaces a silent green-run-with-no-data. The correct fix — a dense checkpoint-derived output
    counter — changes the on-disk address scheme and needs a format-version bump and an in-flight-run
    decision. Ticketed, not done here.

18. **Object size: observability now, batch size does the work.** Add a per-object size warning in Infra
    (default 50 MiB, env-overridable, log-never-throw). Do NOT restore the fail-fast guards deleted in
    `b6a82f0` — they threw rather than split, so reinstating them converts oversized objects into failed
    runs. The operator's spec ("within 50 MiB unless a single asset overflows, then permit it") is satisfied
    structurally by decision 15: batch end is already host-atomic, so no host is ever split, and at ~8 MB/host
    a 6-host batch lands near 48 MiB.

19. **The 502 in-process retry is not re-added.** Operator: no benefit in retrying server errors, nor in
    dying to them. Current dev behaviour already matches — no in-process retry, and a 502 defers rather than
    kills. Deferral was only ruinous because of the rewind; with that fixed it costs one in-flight batch.

20. **The incident test is split, not skipped.** Flag-OFF case must pass and becomes the first honest
    regression witness for the 2026-08-09 incident. Flag-ON case is `Skip`ped with a ticket reference. The
    original never reproduced the incident: it set `batchScopedStorage: true`, a configuration production
    does not run, and asserted only `result.Success` while discarding the publish capture.

21. **The full adapters test suite is a release gate, not Falcon's alone.** The Infra seed changes resume
    behaviour for all fourteen collectors. Falcon-only coverage does not discharge the operator's "no
    breakage" requirement.

## Tickets raised by this task (not in scope, must be filed)

- **T1 — dense output counter for Falcon findings.** Split the ordinal's two jobs: position becomes
  `(staged page index, host index)`, output address becomes a dense checkpoint-derived counter like every
  other collector. Retires the fifth failure mode, the sparse address gaps, and the coordinate category
  error together. Needs a format-version bump and an in-flight-run decision.
- **T2 — the fifth failure mode itself** (retrograde guard vs sparse ordinal), pending T1.
- **T3 — re-spool ordinal skip.** `ResolveFrozenKeyListAsync` mints a new generation without resetting
  `lastCompletedOutputPage`, so the new generation's first N ordinals are silently skipped.
- **T4 — Qualys dead classifier.** The 409 → `QUALYS_INSTANCE_BUSY` retryable mapping has zero references;
  both bind points use a same-named class in `Processing.Resilience`. TenableIo has the identical split,
  already documented at `Documentation/03-current-concerns.md:57-59` and never closed.
- **T5 — `CommitPage` migration for the other thirteen collectors.**
- **T6 — ISB lease hardening.** Heartbeat fails open (catches only `OperationCanceledException`, launched
  fire-and-forget); 2-beat margin against documented 4; two-statement ScheduledWait→claim handoff; lock SQL
  missing the status and stop-request predicates; no fencing token.
- **T7 — default exception classifier / delete the ten stub classifiers** (area F). Changes classification
  behaviour per collector; needs per-collector checking.

1. Fix state authority generically in the substrate, not per collector.
   - Falcon is the only collector with a recovery hook; the other eleven are exposed to the same Infra gap
     with no mitigation at all.

2. Branch from freshly pulled `origin/dev` in both repos. (Operator, 2026-08-10.)
   - Adapters `6f8b58b3`, Infra `6dccb87`.

3. Seed persisted collector state keys into `AdapterState` at resume.
   - Makes live state populated and authoritative from the first instruction, which is what removes the
     two-sources-of-truth choice from every collector.

4. Delete the Falcon findings recovery hook rather than repair it.
   - With (3) in place it restores state that is no longer missing. Repairing it would keep the trap
     available to the next collector that copies the pattern.

5. Reduce the Falcon assets recovery hook to cursor re-anchoring only.
   - Dropping the dead `after` cursor and declining without a watermark cannot be done by the substrate;
     restoring position and totals must not be done by the hook.

6. `IntegrationServiceBus` is not touched in this task.
   - Its lease and heartbeat defects are real but did not cause this stall, and mixing them in would put
     three suspects behind one deploy.

7. The rolling ~50 MiB host-atomic publisher is not built here.
   - No host is provably complete before batch end, so it needs a host-aware publisher or per-host
     staging — a separate deliverable.

8. The authoritative traversal coordinate is not moved into `current_page` here.
   - It does not by itself reject a stale write, and it needs a set/advance-to primitive that
     increment-only `AdvancePage` does not provide.

9. Tests that specify the defect are rewritten, not deleted.
   - `ReturnsWorkItemUnchanged` becomes a divergence test. The fixture must carry live progress that
     differs from the leg-start object, or no assertion can detect the regression.

## Proceeding on unverified

- Proceeding on unverified: seeding collector keys at resume leaves the recovery budget and stuck gate
  unaffected. If wrong: the no-progress gate could read false progress and stop tripping, letting a stuck
  run defer until the 24 h / 50-deferral backstop instead of failing fast.
- Proceeding on unverified: the findings hook holds nothing load-bearing besides state restoration. If
  wrong: deleting it silently drops a continuation behaviour and the deferral path changes shape without a
  failing test.
- Proceeding on unverified: the eleven hookless collectors are currently losing state on a pre-publish
  deferral. If wrong: the generic fix is still correct but its urgency is overstated, and the blast radius
  of the Infra change is being justified by a defect that does not occur.
- Proceeding on unverified: `aidBatchSize` can be set per integration without a build. If wrong, B3
  becomes a code change and folds into this version instead of staying separate.

10. Collectors must conform to a shared checkpoint-handling contract, enforced in practice, not by
    convention. (Operator, 2026-08-10.)
    - A per-collector fix that leaves the next collector free to repeat the mistake is not a fix.

11. Assumptions are settled against the repos, known logs, and tests — never by judgement.
    (Operator, 2026-08-10.)

12. Standing instruction: if a critical mass of duplication appears across best-match collectors during
    the work, stop and propose moving those portions into Infra — only where justifiable.
    (Operator, 2026-08-10.)

## Resolved — was blocking (operator, 2026-08-10)

- **B1 — CORRECTED 2026-08-10 after W5b evidence. Applies to the FINDINGS flow only.**
  The original resolution implied the same removal in both flows, on the strength of the comment at
  `FalconFindingsFlow.cs:163-165` claiming the assets flow applies "the same construct". That comment is
  wrong in the one respect that matters. In assets, `counters.Page` is DENSE and IS the publish page number
  — passed as `pageNumber` in `ProcessPageAsync` and bumped once per published page
  (`FalconAssetsCheckpointWriter.cs:69`). Removing the `Math.Max` there would let the run publish under a
  number below the host watermark: object-key overwrite plus `instanceBatchId` collision, the exact hazard
  `BatchScopedStorage.cs:105-121` describes. **Assets keeps its call.**
  The one genuine assets defect is narrower: `FalconAssetsScrollRunner.cs` `RestoreProgress(counters.Page,
  counters.TotalHosts, 0)` — the literal `0` zeroes the host-restored `ProcessedFindings`, which feeds the
  budget progress coordinate. Minimal correct change: `0` → `progressContext.ProcessedFindings`. Verified
  verbatim on `origin/dev`.

- **B1 (findings) — remove the 6.1.1 clamp AND the `RestoreProgress` call it patches.**
  The clamp exists because the flow assigns a sparse output address into the dense page counter, which can
  land below the host's durable `current_page` and get every later write refused. That treats the symptom.
  The host already restored the counters (`CollectorResumeSetup.cs:59-63`); the collector must not touch
  them on resume. Removing the assignment removes the category error and ends the item-counter regression
  (`RestoreProgress(restoredPage, totalFindings, totalFindings)` overwriting host items downward).
  Folded into S2. Executor must verify nothing depends on the flow's own `RestoreProgress`.

- **B2 — do not re-apply the hand-rolled 502 retry.**
  Its stated justification (`FalconFindingsFlow.cs:241-258`) is "a deferral costs every batch published
  since that checkpoint" — the defect being fixed. Once a deferral costs one in-flight batch, the loop
  answers a question that no longer exists. Any 502 absorption belongs in Falcon's session policy
  (`DefensiveToolkit`/Polly), not hand-rolled in the Phase-2 walk. Consistent with PA2: not retained by
  inertia. **Known regression:** prod-eu runs 6.1.2 with the retry; dev does not. Deploy only after the
  fix is verified; if 502s resurface, add absorption at the session layer.

- **B3 — out of scope.**
  `AidBatchSize` is a config-only experiment after the fix is verified. PA1: Falcon batch bounds have
  already produced a vendor 400, and the AID batch is also the prevention-policy enrichment unit. One
  variable at a time.

## Superseded — operator decisions (kept for history)

- **B1** Disposition of the 6.1.1 clamp (`FalconFindingsFlow.cs:195-208`), which is present on dev.
  Blocks S2. Note PA2: do not retain by inertia.
- **B2** The 6.1.2 in-process 502 retry is live in prod-eu but absent from dev. A dev-based fix removes it
  on deploy unless deliberately re-applied. Blocks S5.
- **B3** `AidBatchSize` 50 → ~6: per-integration config (no repo change), code default, or not now.
  Independent of the fix; label it "reduce average object size and replay cost", not "implement 50 MiB
  output" — six ordinary hosts can collectively exceed 50 MiB, and it moves four variables at once so it
  cannot isolate the suspected URL-length cause.
