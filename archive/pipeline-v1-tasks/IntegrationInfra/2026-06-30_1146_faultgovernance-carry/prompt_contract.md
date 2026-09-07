Role:
You are a senior .NET library engineer relocating a battle-tested resilience concern verbatim.

Goal:
Carry the FaultGovernance concern (Resilience renamed + Recovery folded in) from the in-production Shared
library into IntegrationInfra — behavior verbatim, namespaces rewritten, dependency edges resolved — with
XML docs, README, and unit tests. No functional change; not event-based.

Context:
- Source (reference-only, DO NOT mutate): `/Users/user/Dev/Uri/localprojects/IntegrationsInfra`
  — `Resilience/*` (25 files), `Recovery/*` (3 files), plus 2 Orchestration types to reclaim and 2
  Events/Common DTOs to provide.
- Target: `/Users/user/Dev/Uri/localprojects/IntegrationInfra` — net8.0, `RootNamespace`
  `Cymulate.IntegrationInfra`. Kernel (incl. `Kernel.Transport`, commit 72292d5) already in place.
- Full edge analysis + operator decisions in decisions.md / assumptions.md / constraints.md.

Constraints:
- See constraints.md. Verbatim behavior; namespace-only rewrite; source not mutated; Kernel untouched;
  envelope DTOs NOT in Kernel; fixed chain order invariant; checkpoint-before-advance preserved;
  build clean + tests pass; XML docs on every public member.

Success Criteria:
1. `Resilience/*` + `Recovery/*` relocated under `src/IntegrationInfra/FaultGovernance/` mirroring the
   source sub-structure (Logic / Models / Policies / Recovery), namespaces →
   `Cymulate.IntegrationInfra.FaultGovernance.*`, behavior identical.
2. Transport references in `UnknownFlowFailurePolicy` + `RetryableTransportFailurePolicy` rewired to
   `Cymulate.IntegrationInfra.Kernel.Transport`, including `UnknownFlowRetryPolicy.IsUnknownRetryCandidate`
   → `UnknownFlowRetryClassification.IsUnknownRetryCandidate`. No reference to the source `Shared.*`
   namespaces remains.
3. `FlowExceptionHandling` + `AdapterFlowFailureHandling` reclaimed into FaultGovernance (verbatim logic;
   namespace rewritten; their transport ref also points to `Kernel.Transport`).
4. `CollectorError` + `CollectorPartialCompletionMetadata` relocated into a shared envelope-shapes area
   (proposed `Cymulate.IntegrationInfra.Envelopes.Common`; refine if needed) — NOT Kernel, NOT inside
   FaultGovernance. Executor first verifies these DTOs drag no further Shared-internal deps; if they do,
   STOP and surface.
5. XML docs on every relocated/new public member; `src/IntegrationInfra/FaultGovernance/README.md` extended
   to note the Recovery fold, the reclaim, and the shared-shapes dependency.
6. Unit tests: `CreateDefault` produces the fixed chain in order; `DecideAsync` returns the first
   non-null policy decision and falls back when all decline; reclaim conversions
   (`FromFlowExceptionHandling` / `ToFlowExceptionHandling`) round-trip; `AdapterFailureHandling.ToCollectorError`
   maps fields correctly.
7. `dotnet build` clean (0 errors); all tests pass; build/test output recorded in execution_notes.md.

Execution Rules:
- Do not assume missing data; do not mutate the source repo; preserve behavior verbatim.
- Append a dated action log to execution_notes.md as you go (crash-resilience requirement).
- Mirror the task dir to the global archive at finish.
- Naming discretion only on the shared-shapes namespace (A5) and the test-project choice (A6).

Output Format:
- Relocated `.cs` under FaultGovernance/ + the shared envelope area; reclaimed types; extended README;
  test project; execution_notes.md with action log + final build/test results.

Stop Conditions:
- A relocated unit cannot preserve behavior without a logic change → stop, surface.
- An envelope DTO drags further Shared-internal dependencies → stop, surface (scope decision).
- Carrying would require modifying Kernel or making the engine event-based → stop, surface.
- Required source information missing or contradictory → stop, surface.
