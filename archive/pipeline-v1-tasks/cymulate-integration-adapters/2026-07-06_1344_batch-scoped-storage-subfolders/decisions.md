# Decisions

- Deterministic per-page subfolder (`batch_{page:D6}`), not a random UUID — restores today's overwrite-on-retry self-cleaning; uniqueness comes from page number within a run and per-run StorageUrl. (Operator signed off.)
- Batch granularity = one published page. (Operator confirmed.)
- Vehicle is `Metadata["storageUrl"]` mutation — ISB is a pass-through dispatcher and echoes live metadata into progress events; no host change needed. (Operator confirmed.)
- Dud pages: option (c)/(b) hybrid — no upload, no subfolder; the event still fires (host-driven) but carries the bare base URL; upstream parses only subfolder paths. (Operator accepted.)
- Duplicate announcements after crash-resume are deduped upstream on `(correlationId, sequenceId)` — sequence restore makes the re-published page carry the same id. SHA256 content hash rejected as dedupe key (vendor re-fetch is not byte-stable). (Operator accepted.)
- DONE/failure events carry the run-root StorageUrl; base restored before publication. (Operator accepted.)
- Checkpoint does not persist any storage pointer — folder name is derivable from restored page number. (Operator stated; consistent with existing design.)
- Opt-in flag lives on per-collector *Configuration; this pass ships the shared mechanism + surface only. (Operator directed.)
- YamlCollector out of scope. (Operator directed.)
- Caller-owned scope helper in Egress (not a bool threaded through `CollectorNdjsonPublisher` signatures) — supports future multi-publish batches and keeps opt-in beside collector config.
