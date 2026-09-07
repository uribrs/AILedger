# Orchestration Plan

## Complexity Decision
- Path: direct
- Rationale: One coherent, tightly-coupled dependency swap (solution/csproj plumbing + a mechanical namespace/
  type rewrite across 22 files) driven by a single restore→build→fix loop. Decomposition would only add
  coordination overhead; the whole thing must converge on one build.

## Research Decisions
- None needed. The seam map is fully enumerated (11 namespaces → carried concerns; type renames listed) and every
  edge is build-verifiable in-repo. OPEN assumptions A1–A5 are restore/build-driven STOP conditions, not
  external-system research.

## Worker Plan
Not applicable — direct path.

## Synthesis Approach
Not applicable — direct path.

## Verification Obligations
- Cross-check all 7 Success Criteria in prompt_contract.md.
- Shared fully removed (slnx + 4 ProjectReferences); zero residual `Cymulate.Integration.Adapters.Shared.*`.
- Package wiring: IntegrationInfra 1.0.0-preview.2 referenced + central version; Sdk bumped 3.2.0; nuget.config
  local feed added WITHOUT `<clear/>`.
- Namespace remaps + type renames applied faithfully; Conducting `Collector*` machinery + SDK types NOT over-renamed.
- No consumer logic changed (rewire only).
- `dotnet build CollectorBase.slnx` clean; CollectorExecutor tests + Collectors.Tests.Infrastructure pass.
- Any gap (a used Shared type missing from IntegrationInfra) surfaced explicitly, not worked around.
