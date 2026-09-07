# Constraints

- Behavior preserved verbatim — namespace-only rewrite + Kernel rewires + the D8 naming neutralization. No logic change.
- Single authoritative JSON validator invariant preserved (the engine validates; don't add a second path).
- Source repo `/Users/user/Dev/Uri/localprojects/IntegrationsInfra` is reference-only — do not mutate.
- Kernel is not modified. Emission depends on Kernel (Exceptions, Telemetry), never the reverse.
- D8 naming: neutralize the 3 Collector* type names → Adapter* (values unchanged; wire-safe).
- NO ISink reshape / no killing the static entry / no assets-findings rewire in this carry — verbatim only; flag as reshape candidate.
- net8.0; RootNamespace Cymulate.IntegrationInfra; build clean + tests pass.
- XML docs on every public member.
- Central package management: any added PackageReference gets a PackageVersion at the floor-resolved version.
- No vendor identity introduced.
