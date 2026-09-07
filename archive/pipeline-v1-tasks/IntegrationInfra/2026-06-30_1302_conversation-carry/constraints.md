# Constraints

- Behavior preserved verbatim — namespace-only rewrite + the resolved dependency rewires. No logic change.
- `SessionHandle` dispose ordering is load-bearing — relocate VERBATIM, never re-author.
- Source repo `/Users/user/Dev/Uri/localprojects/IntegrationsInfra` is reference-only — do not mutate.
- Kernel is not modified by this carry. Conversation depends on Kernel, never the reverse.
- Do NOT re-carry already-Kernel items: `TransportErrorHandling/*` (→ Kernel.Transport),
  `LogRedaction.cs` (→ Kernel.Redaction). Rewire references instead.
- LEAK IS EXPECTED — http.package / DefensiveToolkit / Authentication types in the Session surface are
  by-charter dependencies. Do NOT introduce a facade over the substrate here.
- Naming: neutralize any identity-bearing (Collector*/vendor-family) names if found; otherwise preserve.
- net8.0; `RootNamespace` Cymulate.IntegrationInfra; build clean + tests pass.
- XML docs on every public member.
- Central package management: any added PackageReference gets a PackageVersion in Directory.Packages.props
  at the version the floor resolves.
- No vendor identity introduced.
