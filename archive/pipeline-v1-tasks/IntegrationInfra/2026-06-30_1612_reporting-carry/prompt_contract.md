Role:
You are a senior .NET library engineer relocating a battle-tested outbound-reporting concern verbatim.

Goal:
Carry the Reporting concern (outbound Done/Progress envelopes + envelope builder + event-args/sink + the
in-proc event hub + run diagnostics) from the in-production Shared library into IntegrationInfra — behavior
verbatim, namespaces flattened to Reporting, Common refs rewired, Collector*→Adapter* neutralized — and
COMPLETE Envelopes.Common with the last 2 shared shapes + the status converter. With XML docs, README, tests.

Context:
- Source (reference-only, DO NOT mutate): `/Users/user/Dev/Uri/localprojects/IntegrationsInfra`
  — Events/CollectorEnvelopes/Models/{Done,Progress}/*, Events/CollectorEnvelopes/Logic/{CollectorEnvelopeBuilder,
  CollectorStatusJsonConverter}, Events/{BatchProducedEventArgs,CheckpointAdvancedEventArgs,ICollectorEventSink},
  Orchestration/Collectors/Telemetry/CollectorInProcEventHub, Orchestration/Diagnostics/*,
  Events/CollectorEnvelopes/Models/Common/{CollectorStatus,CollectorEventMetadata}.
- Target: `/Users/user/Dev/Uri/localprojects/IntegrationInfra` — net8.0, branch carry/reporting off main.
  Kernel + Emission + Job merged; Envelopes.Common holds AdapterError/AdapterPartialCompletionMetadata/AdapterRunMetadata.
- Full scope + edges + decisions in task.md / decisions.md / constraints.md / assumptions.md.

Constraints:
- See constraints.md. Verbatim logic; best-effort-forwarding invariant preserved; D8 (wire-safe — re-verify
  no $type); source not mutated; Kernel/Emission/Job/existing-Envelopes.Common untouched; build clean + tests
  pass; XML docs on public members; co-locate AdapterStatus + its converter.

Success Criteria:
1. 14 files relocated under `src/IntegrationInfra/Reporting/` (flat ns `Cymulate.IntegrationInfra.Reporting`),
   logic verbatim; + `AdapterStatus`, `AdapterEventMetadata`, `AdapterStatusJsonConverter` added to
   `src/IntegrationInfra/Envelopes/Common/`.
2. D8 renames applied across types/files/refs (the listed names); `JsonPropertyName` values AND the
   `CollectorStatus` string values ("success"/"failed"/"partial") unchanged; wire-safety re-confirmed (no
   `$type`/`JsonDerivedType`).
3. Rewires: Common refs → `Envelopes.Common` (Adapter* names); NO residual `Cymulate.Integration.Adapters.Shared.*`;
   Diagnostics/Telemetry reclaimed (no Orchestration-internal drag).
4. `dotnet build` clean (0 errors); Kernel + Emission + Job + the 3 existing Envelopes.Common shapes
   unmodified (git diff clean apart from the 2 new shapes + converter).
5. XML docs on every relocated/new public member; `src/IntegrationInfra/Reporting/README.md` extended
   (carried set, D8 renames, the Envelopes.Common COMPLETION + the resolved Events-shapes reckoning, the
   best-effort-forwarding invariant).
6. Unit tests for the genuinely-testable pure units: `AdapterEnvelopeBuilder` (BuildProgress/BuildDone output),
   `AdapterStatusJsonConverter` (lowercase round-trip success/failed/partial + invalid throws), the Done/Progress
   payload shapes, and `AdapterRunDiagnostics`/snapshot if pure. Don't force brittle tests on the event-driven hub.
7. `dotnet build` clean; all tests pass; build/test output recorded in execution_notes.md.

Execution Rules:
- Do not assume missing data; do not mutate the source repo; preserve behavior verbatim.
- Append a dated action log to execution_notes.md as you go (crash-resilience requirement).
- Mirror the task dir to the global archive at finish.
- Discretion only for the test-project choice; naming follows D8.

Output Format:
- Relocated `.cs` under Reporting/ + 3 in Envelopes/Common/; extended README; test project; execution_notes.md
  with action log + final build/test results.

Stop Conditions:
- A relocated unit cannot preserve behavior without a logic change → stop, surface.
- A deserialized type has a type-name-on-the-wire dependency that D8 would break → stop, surface.
- Carrying would require modifying Kernel/Emission/Job or changing forwarding semantics → stop, surface.
- Required source information missing or contradictory → stop, surface.
