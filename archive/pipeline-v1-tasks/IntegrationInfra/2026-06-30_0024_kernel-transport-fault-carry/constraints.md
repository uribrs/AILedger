# Constraints

- Behavior preserved verbatim — only namespace rewrites + the dependency-seam split. No logic changes.
- Kernel stays Polly-free; no third-party dependency may enter Kernel from this work (charter rule:
  "nothing beyond `Microsoft.Extensions.*` abstractions").
- Source repo `/Users/user/Dev/Uri/localprojects/IntegrationsInfra` is reference-only — do not mutate it.
- `UnknownFlowRetryPolicy.CreatePipeline` is NOT carried in this task.
- Target framework net8.0; `RootNamespace` `Cymulate.IntegrationInfra`.
- Relocated namespaces: `Cymulate.IntegrationInfra.Kernel.Transport` and
  `Cymulate.IntegrationInfra.Kernel.Exceptions`.
- Project must build clean; tests must pass.
- Every public member carries XML doc comments (relocated members keep/extend existing docs).
- No vendor identity introduced (generic infra only).
