# Orchestration Plan

## Complexity Decision

- Path: **decompose**
- Axis scores: Complexity **high** | Separability **high** | Coupling **low** (read off recon: four
  non-overlapping evidence surfaces, each with its own output file) | Dependency order **medium**
  (one hard barrier: run identity must be frozen before any surface can be queried) | Execution risk
  **medium** (live-environment reads, credential expiry, easy to measure the wrong run) | Worker
  clarity **high** once identity is frozen
- Rationale: the deliverable is a reconciliation of four independent sources (repo code, S3 raw data,
  Glue+RDS telemetry, ISB collector). Each requires a different toolchain and a large volume of raw
  output that must not enter the synthesizing context. Hard trigger met: four disjoint output sets.

## Research Decisions

- External research: **none needed.** No OPEN assumption depends on third-party vendor behavior — the
  Tenable API is not in question; every uncertainty is about Cymulate's own code, data, and
  infrastructure, which is measurable directly. A1/A2 concern the internal ISB collector and are
  assigned to W3 as measurement, not research.
- Internal recon: **complete** → `research/internal-recon.md` (write path + row fan-out map, run as a
  bounded subagent; it doubles as the code evidence for contract questions 2 and 3).

## File Ownership

- Disjoint sets found: **4**
- Phase 0 (main thread) owns: `research/run-identity.md` — the frozen run identity.
- RECON owns: `research/internal-recon.md` — repo write path, row fan-out, log-line map.
- W1 owns: `research/evidence-s3-collector.md` — S3 inventory, record/finding counts, duplication,
  CVE fan-out, row width.
- W2 owns: `research/evidence-glue-db.md` — Glue driver/executor logs, Glue CloudWatch, RDS instance
  facts, Performance Insights API, RDS CloudWatch, baseline run history.
- W3 owns: `research/evidence-isb-collector.md` — Mongo run document, adapter version, collector
  publish counts, full-vs-incremental cursor, requeue/double-publish, volume trend.
- Shared surface frozen in phase 0: client id, instance id, integration setting id, S3 prefix, Glue
  workflow + job run ids, both UTC windows, capacity (G.1X x10), the failure message, and the
  IDT→UTC conversion. No worker re-derives any of these; all four briefs quote them verbatim.
- Main thread owns: `execution_notes.md`, `report.md`, `review/`.

## Worker Plan

- **W0 — phase 0, main thread** — scope: resolve run identity from Glue workflow run properties and
  anchor the clock. output: `research/run-identity.md`. Completed before any worker was spawned.
- **RECON — phase 1, fresh** — scope: repo code ground truth (write path, batching, writer
  concurrency, CVE explode, join fan-out, input-file resolution, table width, count-reporting log
  strings). owns: `research/internal-recon.md`. inputs: W0. May not touch live environments.
- **W1 — phase 1, fresh** — scope: measure S3 published volume and duplication; predict exposure rows
  from CVE fan-out. owns: `research/evidence-s3-collector.md`. inputs: W0's S3 prefix.
- **W2 — phase 1, fresh** — scope: Glue logs + Glue/RDS CloudWatch + Performance Insights API; the
  full INSERT statement text (single-row vs multi-row VALUES); write-stage task count; retries;
  baseline run history. owns: `research/evidence-glue-db.md`. inputs: W0's run ids and windows.
- **W3 — phase 1, fresh** — scope: ISB collector run identity and behavior via the `isb-run-triage`
  skill; full-vs-incremental; requeue. owns: `research/evidence-isb-collector.md`. inputs: W0 +
  the operator's Mongo `_id`.
- Continuity: all four **fresh**. No worker reacts to feedback on its own output; each returns one
  evidence packet the main thread reconciles. Resuming would replay large log/S3 transcripts for no
  gain. Exception reserved: if a worker's packet contradicts another's, that worker is **resumed**,
  because the contradiction is feedback on its own measurement.

## Synthesis Approach

Main thread reconciles in this order, because each step gates the next:

1. **Volume chain**: S3 records (W1) → collector's own belief (W3) → parser-logged counts (W2) →
   predicted exposure rows via the fan-out map (RECON x W1). Any disagreement between two links is a
   finding, not a rounding error.
2. **Shape arithmetic**: RECON's batch size and writer-partition count x W2's write-stage task count,
   checked against W2's real PI statement text and AAS. The single-row-vs-multi-row VALUES question
   decides whether the cause is shape.
3. **Attribution**: split the measured peak into exposures INSERT, other statements, and downstream
   consumers, from W2's PI grouping.
4. **Anomaly verdict**: W2's baseline table x W3's volume trend.
5. Only then write `report.md`. A conclusion resting on one surface is reported as unconfirmed.

## Verification Obligations

- Cross-check against all eight Success Criteria in `prompt_contract.md`.
- Dispose of A1-A15 with actor + citation; NEVER-TESTED is the default.
- Task-specific: (a) every number in the report traces to a command, log line, or `file:line`;
  (b) the IDT→UTC conversion is stated and consistent everywhere; (c) volume and shape are attributed
  separately, not merged into "too much load"; (d) no duplication claim rests on a contiguous sample;
  (e) the report does not present the operator's screenshot figures as measurements; (f) the read-only
  constraint was not violated by any worker.
