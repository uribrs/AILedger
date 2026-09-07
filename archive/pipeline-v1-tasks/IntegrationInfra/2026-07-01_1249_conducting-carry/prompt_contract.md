Role:
You are a senior .NET library engineer executing a verbatim, behavior-preserving relocation of a battle-tested
in-production concern into a clean concern-organized NuGet library.

Goal:
Carry the CONDUCTING concern (the collector run pipeline — fresh-run + resume templates — and the shared
low-altitude orchestration primitives) from the source Shared library into `src/IntegrationInfra/Conducting/`,
namespace-only rewrite + cross-concern rewires, with ZERO change to logic or behavior. Reconstruct the one
deliberately-split Polly factory (`UnknownFlowRetryPolicy.CreatePipeline`) in FaultGovernance. Build clean, add
XML docs + README, add focused tests. This is the final concern carry.

Context:
- Source (REFERENCE ONLY — never mutate): `/Users/user/Dev/Uri/localprojects/IntegrationsInfra`
- Target: `/Users/user/Dev/Uri/localprojects/IntegrationInfra`, branch `carry/conducting`.
- Prior 6 concerns merged (Kernel+transport, FaultGovernance, Conversation, Emission, Job, Reporting).
- Design verdict (from real consumers FalconCollector / Falcon indicator / CollectorExecutor): consumers COMPOSE
  against the provided run-templates by injecting delegates; there is NO façade to build.
- The contract SDK `Cymulate.Integration.Sdk` (3.2.0) is an external package — sit behind it, never carry it.
- Full scope, rewires, decisions, and invariants are in `task.md`, `constraints.md`, `decisions.md`, `assumptions.md`.

Constraints:
(See constraints.md — the binding list. Summary of the load-bearing ones:)
* Verbatim logic; namespace-only rewrite + rewires; source never mutated.
* Mirror source sub-structure under `Cymulate.IntegrationInfra.Conducting.*` (not flattened).
* KEEP `Collector*` and `Adapter*` names — no D8 renaming this carry (D2).
* Conducting → Kernel + Conversation + FaultGovernance + Reporting + Envelopes.Common + SDK only. NOT Emission.
* No residual `Cymulate.Integration.Adapters.Shared.*` after rewire.
* Kernel/Conversation/Emission/Job/Reporting/Envelopes.Common + existing FaultGovernance files UNCHANGED except
  FaultGovernance gaining the new `UnknownFlowRetryPolicy.CreatePipeline` file.
* All hard behavioral invariants preserved verbatim (partial-success-wins; cancellation-NACK; success-completion
  ordering; flow-retry-OUTER / resilience-strategy-INNER nesting; deferred-wait externalization; checkpoint-before-
  advance; best-effort shutdown with CancellationToken.None; best-effort forwarding never affects publishing).
* net8.0; build clean (0 errors; NU1507/NU1900 OK) + tests pass; XML docs on public members.

Success Criteria:
1. The 38 source `Orchestration/` files relocated under `src/IntegrationInfra/Conducting/` (mirror sub-structure),
   namespace `Cymulate.IntegrationInfra.Conducting(.*)`, logic verbatim; boundary items (`CollectorCommonDependencies`
   in-scope; `BaseFlowHandler` per A2) placed with a recorded rationale in execution_notes.md.
2. D1 done: `UnknownFlowRetryPolicy.CreatePipeline` reconstructed in FaultGovernance (type name kept), delegating to
   Kernel `UnknownFlowRetryClassification`; Kernel git diff EMPTY.
3. D2 honored: `Collector*` and `Adapter*` names kept; no D8 renaming.
4. All rewires done; NO residual `Cymulate.Integration.Adapters.Shared.*` in Conducting; Kernel/Conversation/
   Emission/Job/Reporting/Envelopes.Common + existing FaultGovernance UNCHANGED except FaultGovernance +CreatePipeline
   (git diff otherwise clean on those trees).
5. `dotnet build` clean (0 errors); XML docs on public members; `src/IntegrationInfra/Conducting/README.md` written
   (charter; the two run-templates fresh+resume; composable-not-façade verdict grounded in the 3 consumers; D1-D3;
   the DAG note that Conducting does NOT depend on Emission; the flagged assets/findings reshape candidate).
6. `tests/IntegrationInfra.Conducting.Tests` (xUnit, ProjectReference, added to IntegrationInfra.slnx): pure units
   (CollectorTriggerParsing, ConfigurationValidation, CollectorGuards, CollectorResultPayload, resume checkpoint
   fingerprint/serialize if pure, `AdapterPlatformEventFactory.ResolveForFlow`, the `UnknownFlowRetryPolicy` predicate)
   PLUS two targeted invariant tests on `AdapterBusEntrypointRunner` with a stub `IAdapterExecutionContext`:
   (a) partial-success published BEFORE failure, (b) NO publish on cancellation. No brittle full-bus tests;
   add `InternalsVisibleTo` to the main csproj if a tested unit is internal.
7. `dotnet build` on IntegrationInfra.slnx clean + `dotnet test` pass; output recorded in execution_notes.md;
   task dir mirrored to `~/codex-state/tasks/IntegrationInfra/2026-07-01_1249_conducting-carry/`.

Execution Rules:
* Do not assume missing data — verify against source before relocating.
* Respect constraints strictly; do not reshape, rename (beyond namespaces), or "improve" battle-tested logic.
* Resolve OPEN assumptions (A1-A5) during execution and record the disposition; STOP conditions below are hard.
* Mirror the prior-carry mechanics: scripted `cp` + `find -exec perl -pi` namespace rewrite (specific-before-bare
  ordering), then build to surface missed rewires, then fix usings build-driven.

Output Format:
* Relocated source files under `src/IntegrationInfra/Conducting/**` + one new file in `src/IntegrationInfra/FaultGovernance/`.
* `src/IntegrationInfra/Conducting/README.md`.
* `tests/IntegrationInfra.Conducting.Tests/**` + slnx entry.
* Updated `execution_notes.md` (dated action log + assumption dispositions + build/test output + residual risks).
* Updated `state.json` (step statuses, blockers, lastUpdated).

Stop Conditions:
* A unit cannot preserve behavior without a logic change (A5).
* `UnknownFlowRetryPolicy.CreatePipeline` cannot be reconstructed verbatim against the Kernel residue (A3).
* A boundary item (`CollectorCommonDependencies` / `BaseFlowHandler`) drags an unexpected dependency (A1/A2).
* A required package/version cannot resolve (A4).
* An invariant cannot be preserved by a namespace-only move.
* Goal achieved and all success criteria met.
