# Constraints

- Strict conformance to the collector contract SHAPE used by native collectors; no divergence.
- Bound to `Cymulate.Integration.Sdk`; reuse `Shared.*` — never reinvent transport, egress, resilience, or resume.
- CollectorExecutor has NO vendor identity. No hardcoded vendor behavior; vendor specifics live only in YAML profiles + named strategies (registry-resolved).
- Adapter must implement: `IAssetsCollectorAdapter`, `IFindingsCollectorAdapter`, `ICollectorAdapter` (`BatchProduced`/`CheckpointAdvanced`), `ICollectorEventSink`, `IResumableAdapter`, `IAsyncDisposable`; declare all 5 SDK events.
- `ProcessAsync` MUST go through `CollectorBusEntrypointDefinitionBuilder.Build(new DelegateCollectorBusEntrypointSource<TRequest>{...})` -> `AdapterBusEntrypointRunner.RunAsync(...)` with all 13 required delegates.
- `ResumeAsync`/`CanResumeFrom` MUST go through `CollectorResumeRunner` + `CollectorResumeDefinition<CheckpointState>`.
- Progress context is supplied BY the bus runner to the collect delegates; stop calling `_context.CreateProgressContext` inside the runner.
- Output page number MUST be a monotonic, per-emit-target counter (`AssetsPage`/`FindingsPage`) persisted in `CheckpointState`, decoupled from the pagination cursor, restored on resume, contiguous + gap-free per type.
- Egress limit applied AT the caller: buffer to `ThrottlingOptions.Resolve(context.Services).MaxBytesPerBatch`, one `PublishUtf8PageAsync` call per slice = one file. Persist the counter base at chunk/step start so a mid-item re-run re-publishes the same page numbers (idempotent overwrite).
- Target `net8.0`; solution `CollectorBase.slnx`.
- Do NOT modify: production RUN-envelope ingress; the local Runner cred/input passthrough.
