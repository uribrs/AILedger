# Assumptions

- VALIDATED — service-locator sites are exactly `{Throttling,Buffering,MemoryPressure}Options.Resolve(context.Services)` in ResultsBatchPublisher (lines 56-71, 176-191; grep 2026-07-08). MultipartUploadOptions resolution site to be confirmed by executor (likely in the batch sessions) and moved to construction the same way.
- VALIDATED — no src concern outside Emission references the static publishers; test blast radius = 24 call sites in 2 Emission test files.
- VALIDATED — zero package consumers exist (operator statement + preview status), so deleting public statics is non-breaking in practice.
- VALIDATED — sessions receive options via plain ctor params (NdjsonOptions + MemoryPressureOptions); extended with MultipartUploadOptions; internal flush/upload logic untouched.
- VALIDATED — telemetry tags are mode strings ("string"/"utf8"), not type names; GcMemorySnapshot untouched.
