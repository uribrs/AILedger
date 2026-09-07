# Assumptions

- A1 — VALIDATED (operator): ISB forwards progress-context metadata into progress events unmodified; adapter-side mutation of `Metadata["storageUrl"]` is sufficient for the event to carry the subfolder path.
- A2 — VALIDATED (operator): raw-data bucket is never purged; layout must therefore be self-cleaning (deterministic overwrite), not lifecycle-dependent.
- A3 — VALIDATED (operator): upstream parser triggers only on subfolder-bearing storageUrl values and dedupes on `(correlationId, sequenceId)`.
- A4 — OPEN: collector flows are strictly sequential per run (publish → advance → next page); no concurrent page publishes share one progress context. Holds for all current native collectors; the ordering-invariant test plus helper doc comment guard it. Rejecting this would require a redesign (per-call paths instead of shared metadata).
- A5 — VALIDATED (repo search, 2026-07-06): the only writers of `Metadata["storageUrl"]` are ingress-time (`AdapterPlatformEventFactory.SetIfMissing` on `PlatformEvent.Metadata`, pre-run); the NDJSON sessions only read it. No mid-run writer existed before this change.
- A6 — VALIDATED (code, 2026-07-06): file naming is `{name}_{page:D6}` via `CollectorOutputDefaults.BuildPageTargetPath` with `page >= 1` enforced; `BatchScopedStorage.BuildBatchSegment` mirrors D6 padding and the same `>= 1` guard.
