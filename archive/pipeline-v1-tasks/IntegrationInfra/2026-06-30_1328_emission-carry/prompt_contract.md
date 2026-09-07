Role:
You are a senior .NET library engineer relocating a battle-tested egress/publish concern verbatim.

Goal:
Carry the Emission concern (DataPipeline/Egress + egress invariants) from the in-production Shared library
into IntegrationInfra — behavior verbatim, namespaces rewritten, Kernel rewires resolved, the 3 generic
Collector* names neutralized to Adapter* — with XML docs, README, and unit tests for the testable units.

Context:
- Source (reference-only, DO NOT mutate): `/Users/user/Dev/Uri/localprojects/IntegrationsInfra`
  — `DataPipeline/Egress/*` (18, incl. Multipart/, Ndjson/, Telemetry/) + `Glossary/CollectorGlobalDefaults.cs`.
- Target: `/Users/user/Dev/Uri/localprojects/IntegrationInfra` — net8.0, branch carry/emission off main.
  Kernel (Exceptions, Telemetry, Json) already present.
- Full scope + edges + operator decisions in task.md / decisions.md / constraints.md / assumptions.md.

Constraints:
- See constraints.md. Verbatim logic; D8 neutralization; source not mutated; Kernel untouched; single
  authoritative JSON validator preserved; NO ISink reshape; build clean + tests pass; XML docs on public members.

Success Criteria:
1. The 18 Egress files + the defaults type relocated under `src/IntegrationInfra/Emission/` (mirror
   sub-structure: Multipart/, Ndjson/, Telemetry/), namespaces → `Cymulate.IntegrationInfra.Emission(.*)`,
   logic verbatim.
2. Kernel rewires done: `...Shared.Exceptions` → `Kernel.Exceptions`; `...Shared.DataPipeline.Telemetry`
   → `Kernel.Telemetry`; `...Shared.Glossary` (the defaults type) → `Emission`. NO residual
   `Cymulate.Integration.Adapters.Shared.*`. (If Egress references `DataPipeline/Json`, rewire to `Kernel.Json`.)
3. D8 neutralization applied consistently across ALL references: `CollectorGlobalDefaults`→`AdapterGlobalDefaults`,
   `CollectorOutputDefaults`→`AdapterOutputDefaults`, `CollectorNdjsonPublisher`→`AdapterNdjsonPublisher`.
   The held string/numeric values are unchanged.
4. Build resolves: if `Microsoft.Extensions.Configuration(.Abstractions)` (or another) is not transitive, add
   a `PackageReference` + central `PackageVersion` at the floor-resolved version. If a package/version cannot
   be resolved → STOP and surface.
5. XML docs on every relocated/new public member; `src/IntegrationInfra/Emission/README.md` extended (carried
   set, Kernel rewires, the D8 renames, the deferred ISink/static-entry reshape, the JSON-validator invariant).
6. Unit tests for the genuinely-testable pure units (formatters, options records, `MultipartPartPlanner`
   partitioning, the defaults, pure byte-budget/NDJSON logic). Do NOT force brittle tests on the
   DI/streaming-coupled publisher entry; note untestable seams rather than forcing.
7. `dotnet build` clean (0 errors); all tests pass; build/test output recorded in execution_notes.md.

Execution Rules:
- Do not assume missing data; do not mutate the source repo; preserve behavior verbatim.
- Append a dated action log to execution_notes.md as you go (crash-resilience requirement).
- Mirror the task dir to the global archive at finish.
- Discretion only for: the package-ref resolution (A-pkg), the Json rewire if present (A-json), and the
  test-project choice (A-test). Naming follows D8.

Output Format:
- Relocated `.cs` under Emission/ + the defaults type; csproj/props updates if needed; extended README;
  test project; execution_notes.md with action log + final build/test results.

Stop Conditions:
- A relocated unit cannot preserve behavior without a logic change → stop, surface.
- A needed package or its floor-resolved version cannot be obtained → stop, surface.
- Carrying would require modifying Kernel or introducing the ISink reshape → stop, surface.
- Required source information missing or contradictory → stop, surface.
