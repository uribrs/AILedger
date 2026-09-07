# Constraints

- Behavior preserved verbatim — namespace-only rewrite + the resolved dependency rewires. No logic change.
- Do NOT make it event-based; do NOT alter functionality. It is a synchronous decision engine.
- Source repo `/Users/user/Dev/Uri/localprojects/IntegrationsInfra` is reference-only — do not mutate it.
- Kernel is not modified by this carry. FaultGovernance depends on `Kernel.Transport`, never the reverse.
- Envelope DTOs (`CollectorError`, `CollectorPartialCompletionMetadata`) do NOT go to Kernel — they are
  envelope shapes, not primitives. They go to a shared envelope-shapes area.
- Fixed policy-chain order is an invariant — values-only parameterization; never expose chain reordering.
- Checkpoint-before-advance / resume semantics preserved exactly.
- `ErrorSeverity` is an SDK enum (`Cymulate.Integration.Sdk.Events`) already in the floor — do not carry it.
- net8.0; `RootNamespace` `Cymulate.IntegrationInfra`; build clean + tests pass.
- XML docs on every public member (relocated members keep/extend existing docs).
- No vendor identity introduced.
