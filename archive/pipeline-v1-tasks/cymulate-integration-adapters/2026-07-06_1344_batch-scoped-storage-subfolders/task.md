# Batch-Scoped Storage Subfolders (Collector Egress Mechanism)

## What

Add an opt-in mechanism so a collector's published pages land in deterministic
per-page subfolders under the run's StorageUrl, and each page's progress event
announces that subfolder — enabling upstream raw-data parsing to run per batch
while collection is still in progress.

Mechanism only. No collector is opted in during this pass; relevant collectors
are wired later.

## Behavior (when enabled)

- Page N uploads under `{StorageUrl}/batch_{N:D6}/` (deterministic, derived from
  page number — never random). Crash-resume re-publishes the same page to the
  same keys, so the layout is self-cleaning (bucket is never purged).
- Before the page's `AdvancePage`/checkpoint, `progressContext.Metadata["storageUrl"]`
  holds the subfolder path, so the host (ISB, pass-through dispatcher) echoes it
  in the page's progress event.
- The pristine base StorageUrl is preserved and every subfolder path derives from
  it (no stacking).
- Dud page (0 records): no upload, no subfolder — the event carries the bare base
  URL. Upstream triggers parsing only on subfolder paths.
- DONE / failure / partial-success events carry the bare base URL (restore base
  before completion/failure publication, including the
  `AdapterFailureDecisionExecutor` `AdvancePage(0,0)` snapshot path).
- Checkpoint format untouched: the folder name derives from the page number the
  checkpoint already restores.

## Key code

- `Shared/.../DataPipeline/Egress/` — `NdjsonBatchSession.ApplyStorageUrlPrefix`
  (~426) and `NdjsonUtf8BatchSession` twin read `Metadata["storageUrl"]` live per
  flush; `CollectorNdjsonPublisher` is the collector entry point.
- `Shared/.../Orchestration/AdapterBusEntrypointRunner` + `AdapterPlatformEventFactory`
  — storageUrl ingress from the RUN envelope.
- `Shared/.../Resilience/AdapterFailureDecisionExecutor` — checkpoint snapshot path.

## Out of scope

- YamlCollector (`context.PublishAsync` / bus-side prefixing path).
- Collector opt-in wiring (follow-up task).
- Upstream dedupe on `(correlationId, sequenceId)` — upstream's code.
- ISB/host changes — none required (pass-through confirmed by operator).
