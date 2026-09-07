# Decisions

- The sink-less `ExecuteOperationAsync` overload **survives**, implemented as sugar that supplies an `InMemorySink` and returns its records. Keeps one internal path without breaking the adapter's connection test.
- Two sink kinds, because there are genuinely two situations: `CountingSink` wrapping the host sink for sink-routed stages, `InMemorySink` for stages with no downstream consumer.
- `InMemorySink` is public; `CountingSink` is internal until a consumer outside the assembly needs it.
- `Sinks` gains a `Logic/` layer. Flat — two types, no nameable sub-concept between them.
- Records reaching the wrapped sink must be untouched by `CountingSink`. Observation only.
- Sink lifecycle ordering (`InitializeAsync` before the stage) is pinned by a test rather than documented.
- The host-side gap — `IsbExecutionSink` not overriding `PublishStreamAsync`, so it buffers via the default fallback — is recorded in the defect register, not fixed. The monorepo is read-only.
- Determinism is verified by repeated runs with trx capture, never by a single green run.
- Defects found along the way go in `defect_register.md` as they are found, not into commit messages.
