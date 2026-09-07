Role:
You are a senior .NET library engineer relocating a battle-tested inbound-envelope concern verbatim.

Goal:
Carry the Job concern (inbound RUN envelope models + parsers + credential hydrator + trigger parsing +
routing/time defaults) from the in-production Shared library into IntegrationInfra — behavior verbatim,
namespaces flattened to Job, rewires resolved, Collector* names neutralized to Adapter* (wire-safe) — with
XML docs, README, and unit tests.

Context:
- Source (reference-only, DO NOT mutate): `/Users/user/Dev/Uri/localprojects/IntegrationsInfra`
  — Events/CollectorEnvelopes/Models/Run/* (4), Events/CollectorEnvelopes/Logic/{CollectorRunEnvelopeParser,
  CollectorRunActionParser, RunPayloadCredentialHydrator}, Orchestration/Collectors/Triggers/CollectorTriggerParsing,
  Glossary/AdapterTopics, Time/AdapterTimeDefaults, Events/CollectorEnvelopes/Models/Common/CollectorRunMetadata.
- Target: `/Users/user/Dev/Uri/localprojects/IntegrationInfra` — net8.0, branch carry/job off main.
  Kernel + Emission (incl. AdapterGlobalDefaults) already present/merged. Envelopes.Common holds AdapterError.
- Full scope + edges + decisions in task.md / decisions.md / constraints.md / assumptions.md.

Constraints:
- See constraints.md. Verbatim logic (esp. hydrator exception-swallowing); D8 neutralization (wire-safe,
  verify no $type); source not mutated; Kernel + Emission untouched; no parse→typed-job reshape; don't move
  DefaultLookbackDays; build clean + tests pass; XML docs on public members.

Success Criteria:
1. 10 Job files relocated under `src/IntegrationInfra/Job/` (flat ns `Cymulate.IntegrationInfra.Job`),
   logic verbatim; `CollectorRunMetadata`→`AdapterRunMetadata` in `src/IntegrationInfra/Envelopes/Common/`.
2. D8 neutralization applied across types/files/refs (the 8 names); JsonPropertyName values unchanged.
   Verify no `$type`/`[JsonDerivedType]` on the deserialized envelope types FIRST; if a type name is on
   the wire, leave it un-renamed and STOP/surface.
3. Rewires done: `...Models.Common`→`Envelopes.Common`; `AdapterTimeDefaults`→`Emission.AdapterGlobalDefaults.DefaultLookbackDays`
   (Job→Emission coupling, flagged). NO residual `Cymulate.Integration.Adapters.Shared.*`. Outbound/Reporting
   items NOT carried.
4. `dotnet build` clean (0 errors); Kernel + Emission unmodified (git diff empty on
   src/IntegrationInfra/Kernel and src/IntegrationInfra/Emission).
5. XML docs on every relocated/new public member; `src/IntegrationInfra/Job/README.md` extended (carried set,
   D8 renames, the Envelopes.Common addition, the flagged Job→Emission lookback coupling, the deferred
   hydrator parse→typed-job seam).
6. Unit tests for: `AdapterRunEnvelopeParser`/`AdapterRunActionParser` (parse + edge cases), `AdapterTriggerParsing`
   (trigger/flow-name/date parsing — pure, high-value), `AdapterTimeDefaults.ResolveBaseDateUtcOrDefault`,
   and characterization tests for `RunPayloadCredentialHydrator` (pin verbatim behavior incl.
   exception-swallowing + untyped-dict output).
7. `dotnet build` clean; all tests pass; build/test output recorded in execution_notes.md.

Execution Rules:
- Do not assume missing data; do not mutate the source repo; preserve behavior verbatim.
- Append a dated action log to execution_notes.md as you go (crash-resilience requirement).
- Mirror the task dir to the global archive at finish.
- Discretion only for: the test-project choice (A-test); naming follows D8.

Output Format:
- Relocated `.cs` under Job/ + AdapterRunMetadata in Envelopes/Common/; extended README; test project;
  execution_notes.md with action log + final build/test results.

Stop Conditions:
- A relocated unit cannot preserve behavior without a logic change → stop, surface.
- A deserialized envelope type has a type-name-on-the-wire dependency that D8 renaming would break → stop, surface.
- Carrying would require modifying Kernel/Emission or reshaping the hydrator → stop, surface.
- Required source information missing or contradictory → stop, surface.
