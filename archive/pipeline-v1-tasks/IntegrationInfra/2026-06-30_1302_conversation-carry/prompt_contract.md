Role:
You are a senior .NET library engineer relocating a battle-tested HTTP-session concern verbatim.

Goal:
Carry the Conversation concern (the Session layer + reclaimed session lifecycle + 2 helpers) from the
in-production Shared library into IntegrationInfra — behavior verbatim, namespaces rewritten, Kernel
rewires resolved, http.package/SDK dependencies satisfied — with XML docs, README, and unit tests for the
testable units.

Context:
- Source (reference-only, DO NOT mutate): `/Users/user/Dev/Uri/localprojects/IntegrationsInfra`
  — `Session/*` (18 to carry + 3 in HttpCertValidationHandler/), `Orchestration/AdapterSessionLifecycle.cs`
  (reclaim), `Helpers/{RateLimiterHelper,HttpStatusExtractor}.cs`.
- Target: `/Users/user/Dev/Uri/localprojects/IntegrationInfra` — net8.0, branch carry/faultgovernance.
  Kernel (Transport/Redaction/Telemetry/Exceptions) already in place.
- Full scope + edges + operator decisions in task.md / decisions.md / constraints.md / assumptions.md.

Constraints:
- See constraints.md. Verbatim logic; SessionHandle dispose ordering verbatim; source not mutated;
  Kernel untouched; do not re-carry TransportErrorHandling/LogRedaction; leak is expected (no facade);
  build clean + tests pass; XML docs on public members.

Success Criteria:
1. `Session/*` (18) + `HttpCertValidationHandler/*` (3) relocated under
   `src/IntegrationInfra/Conversation/` (mirror sub-structure), namespaces →
   `Cymulate.IntegrationInfra.Conversation` (+ `.HttpCertValidationHandler`), logic verbatim.
2. `AdapterSessionLifecycle` reclaimed into Conversation; `RateLimiterHelper` + `HttpStatusExtractor` carried.
3. Kernel rewires done: `...Session.TransportErrorHandling` → `Kernel.Transport` (+ `Kernel.Exceptions`
   for AdapterHttpRequestFailedException); `...Session.LogRedaction` → `Kernel.Redaction`;
   `...DataPipeline.Telemetry` → `Kernel.Telemetry`. NO residual `Cymulate.Integration.Adapters.Shared.*`
   reference remains; `TransportErrorHandling`/`LogRedaction` are NOT re-carried.
4. Build resolves: if `Http.Package.Authentication` / `Sdk.Query.*` are not transitive, add a
   `PackageReference` + central `PackageVersion` (at the floor-resolved version). If a package/version
   cannot be resolved → STOP and surface.
5. XML docs on every relocated/new public member; `src/IntegrationInfra/Conversation/README.md` extended
   (the reclaim, the 2 helpers, the Kernel rewires, the http.package dependency, the leak-is-expected note).
6. Unit tests for the genuinely-testable pure units (`RateLimiterHelper`, `HttpStatusExtractor`,
   `SessionSpec` defaults / any pure logic). Realistically narrower than FaultGovernance; do NOT force
   brittle integration tests around http.package-coupled session creation/auth.
7. `dotnet build` clean (0 errors); all tests pass; build/test output recorded in execution_notes.md.

Execution Rules:
- Do not assume missing data; do not mutate the source repo; preserve behavior verbatim.
- Append a dated action log to execution_notes.md as you go (crash-resilience requirement).
- Mirror the task dir to the global archive at finish.
- Naming discretion only for identity-neutralization (D3), the package-ref resolution (A-pkg), and the
  test-project choice (A-test).

Output Format:
- Relocated `.cs` under Conversation/ + reclaimed lifecycle + 2 helpers; csproj/props updates if needed;
  extended README; test project; execution_notes.md with action log + final build/test results.

Stop Conditions:
- A relocated unit cannot preserve behavior without a logic change → stop, surface.
- A needed package or its floor-resolved version cannot be obtained → stop, surface.
- Carrying would require modifying Kernel or re-authoring SessionHandle dispose ordering → stop, surface.
- Required source information missing or contradictory → stop, surface.
