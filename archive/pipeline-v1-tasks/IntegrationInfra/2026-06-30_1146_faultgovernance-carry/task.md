# Task: FaultGovernance carry (Resilience + Recovery)

Carry the FaultGovernance concern into IntegrationInfra, behavior verbatim (namespace-only rewrite).
FaultGovernance = `Resilience/*` renamed + the `Recovery/*` fragment folded in. It is a synchronous
failure-policy decision engine (`CreateDefault` → `DecideAsync` → `AdapterFailureDecision`) plus
checkpoint persist/restore — NOT event-based. Functionality is unchanged.

## In scope
- Relocate `Resilience/*` (25 files) + `Recovery/*` (3 files) → `FaultGovernance/` (mirror
  Logic/Models/Policies + Recovery), namespaces → `Cymulate.IntegrationInfra.FaultGovernance.*`.
- Rewire transport refs → `Kernel.Transport` (incl. `UnknownFlowRetryPolicy` → `UnknownFlowRetryClassification`).
- Reclaim 2 governance types from Orchestration → FaultGovernance: `FlowExceptionHandling`,
  `AdapterFlowFailureHandling`.
- Provide 2 envelope DTOs the surface maps to/from (`CollectorError`, `CollectorPartialCompletionMetadata`)
  in a shared envelope-shapes area (NOT Kernel, NOT inside FaultGovernance).

## Out of scope
- Any behavior change / making it event-based.
- The rest of the Events/Reporting/Orchestration carve.
- Mutating the source repo (reference only).
- Re-homing the envelope DTOs to their final location (deferred to the Events carve).

## Deliverables
1. Verbatim relocation under `FaultGovernance/`.
2. Transport rewire + reclaim + shared DTOs in place.
3. XML docs on every public member; `FaultGovernance/README.md` extended.
4. Unit tests (chain order, DecideAsync short-circuit, reclaim conversions, ToCollectorError).
5. `dotnet build` clean; tests pass.
