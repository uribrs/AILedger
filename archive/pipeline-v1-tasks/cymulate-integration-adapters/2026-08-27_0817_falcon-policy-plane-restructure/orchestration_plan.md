# Orchestration Plan

## Problem Classification

- Contract sufficiency: sufficient
- Classes (maximum 3):
  - `checkpoint-resume` — the task moves the completion proof that resume reads. Reused tag; ledger already carries it (L-dae004f6).
  - `vendor-partial-rejection` — new lowercase slug. No existing tag covers "vendor rejects some ids in a batch and we must keep the survivors".
  - `memory-bound` — reused tag (L-85a32cda, L-9ad5a354). Every ceiling in this design is a size ceiling: 8 MB buffered write, 1 MB manifest read, 4 read slots.
- Additional classified prior art: A14, A15 appended to `assumptions.md` under `## Classified Prior Art`.
- Recon correction: classification confirmed. Recon refined `vendor-partial-rejection` from "parse the error body" to "read the whole body via the session, because AdapterHttpClient discards it" — the class holds, the mechanism changed.
- New or changed artifacts:
  - staged-ledger fields on `FalconPhase1Manifest` → `FalconStagingArea.TryReadManifestAsync` (`:250-253`, capped by `MaxControlArtifactBytes` = 1 MB) → **an oversized manifest returns null, which is deliberately indistinguishable from absent, so the run silently re-spools instead of failing.** Design-invalidating. R1.
  - `_staging/policies/pgen_<id>/` prefix → `FalconStagingArea.DeleteAbandonedGenerationsAsync` (`:327-331`) → `continue`s when `TryGetGenerationId` returns null, which it does for a `pgen_` first segment → policy artifacts are never pruned. R2.
  - Falcon-local `IHttpSession.StreamResponseAsync` call in `FalconDevicePolicyClient` → bypasses `AdapterHttpClient` → **loses `LogRedaction.Scrub` on the body, loses the Polly pipeline, loses `FalconHttpFailureClassifier` dispatch.** Design-invalidating: an unscrubbed vendor body could reach logs. R3.
  - `DataPipelineException` arm in `FalconFlowExceptionClassifier` → `UnknownFlowRetryPolicy` is what it displaces → but the type is **overloaded** (over-ceiling refusal, read-slot timeout, null stream — per `ai/skills/collector-flow-patterns/SKILL.md`), and a read-slot timeout IS transient. A flat `IsRetryable: false` makes a transient fault terminal. R4.
  - edge-only staged host page → `FalconStagedHostPage.DecodeAsync` then Phase 2 composition at publish → the emitted `device_policies` must stay canonically identical to the assets flow's, per `FalconDocs/CollectorDocs/06-prevention-policy-contract.md` §6.1. R5.

### Attention Items

| id | failure mode | causal path and impact | planned handling | source |
|---|---|---|---|---|
| R1 | Staged ledger grows past the 1 MB manifest read ceiling | `PageKeys` already dominates the manifest; ~48 chars/key means ~20k pages ≈ 1 MB. `TryReadManifestAsync` returns null on over-ceiling, `ResolveFrozenKeyListAsync` treats null as absent, and the run re-spools silently — the exact failure this task exists to remove, reintroduced by its own fix. Per-stage state must be O(1), never O(pages). | `test:FalconTwoPhaseFindingsTests.cs::R1_LedgerStaysUnderControlArtifactCeilingAt20kPages` | recon (d); `FalconStagingArea.cs:250-253`; `IngestionOptions.cs:38,92` |
| R2 | Policy artifacts accumulate forever under every run prefix | `DeleteAbandonedGenerationsAsync` skips any key whose first segment is not `gen_`. A `pgen_` sibling is therefore never collected, so every run leaves its definitions sweep behind until the run-scoped prefix is reclaimed wholesale. | `test:FalconTwoPhaseFindingsTests.cs::R2_AbandonedPolicyGenerationsArePruned` | recon (b); `FalconStagingArea.cs:327-331`; `FalconStagingPaths.cs:157-158` |
| R3 | Unscrubbed vendor body reaches logs, or the call loses its retry pipeline | Reading the body via `IHttpSession.StreamResponseAsync` steps outside `AdapterHttpClient`, which is where `LogRedaction.Scrub` (`AdapterHttpClient.cs:176,196`) and the Polly dispatch live. Nothing else applies them. Credentials or host data in a vendor error body would be logged raw. | `test:FalconPolicyEnrichmentTests.cs::R3_RejectionPathScrubsBodyAndAppliesPolicies` | recon (c); `AdapterHttpClient.cs:137-149,166,176` |
| R4 | A transient read-slot timeout is classified terminal | `DataPipelineException` is thrown for over-ceiling refusal, read-slot timeout AND null stream. A flat non-retryable arm makes a 4-slot read-contention timeout (`GuardedObjectStore.cs:361-379`) permanently fatal instead of retried. The arm must discriminate on the exception's cause, not its type. | `test:FalconFlowExceptionClassifierTests.cs::R4_DataPipelineExceptionArmDiscriminatesCeilingFromReadSlot` | `ai/skills/collector-flow-patterns/SKILL.md` ("bound it by volume, do not treat the type as benign"); `GuardedObjectStore.cs:274-289,361-379` |
| R5 | The two flows stop emitting a canonically identical host envelope | Findings composes `device_policies` at publish from edge + cached definition; assets composes it inline during its page scroll. Two composition sites, one documented contract (§6.1 "canonically identical"). Any drift is invisible until a parser reads one and not the other. | `test:FalconCollectorTests.cs::R5_BothFlowsEmitCanonicallyIdenticalDevicePolicies` | `FalconDocs/CollectorDocs/06-prevention-policy-contract.md` §6.1; `ai/skills/collector-tests/SKILL.md` (prescribes exactly this comparison) |

### Research Questions

No research needed — the two vendor facts that could change the solution are already resolved and cited in the contract (prevention-members offset ceiling; endpoint identity and `limit`/`offset` bounds from falconpy `_endpoint/_prevention_policies.py`). The remaining vendor unknown (`settings_hash` presence) is unchanged by this work because the same call is retained, and no answer to it would change a solution decision. The one question that did block planning — whether a non-2xx body is readable — was internal and recon answered it.

## Complexity Decision
- Path: decompose
- Axis scores: Complexity `high` | Separability `high` | Coupling `low` | Dependency order `medium` | Execution risk `high` | Worker clarity `high`
- Rationale: Hard trigger met twice — recon named four disjoint file sets, and tests are separable from implementation (R5's cross-flow comparison must be written by a worker that may not touch `src`). Coupling is read off recon's disjoint-set finding, not estimated. Worker clarity is high because recon cites conventions and durable sources per file, so no worker re-derives them.

## Research Decisions
- External topic: none needed. Rationale above.
- Internal recon: complete → `research/internal-recon.md`

## File Ownership
- Disjoint sets found: 4 (recon sets A, B, C, D), plus a shared spine that must be frozen first.
- Shared surface frozen in phase 0 (main thread): the rejection-outcome type returned by the assignment call; `FalconPolicyEnricher.EnrichAsync` signature and the composition seam it exposes to Phase 2; `FalconDevicePoliciesEnvelope` edge-vs-composed split. Files: `Flows/Policies/FalconPolicyEnricher.cs`, `Flows/Policies/FalconDevicePoliciesEnvelope.cs`, `Flows/Policies/FalconPreventionPolicyCache.cs`.
- W1 owns: `Flows/Policies/FalconDevicePolicyClient.cs`, `Flows/Policies/FalconPreventionPolicyClient.cs`, `Processing/Urls/FalconUrls.cs`, `Flows/SharedFlows/FalconHttpFailureClassifier.cs`
- W2 owns: `Processing/FalconFlowExceptionClassifier.cs`, `Processing/Validation/FalconAccessProber.cs`, `Processing/Validation/FalconConfigurationValidationService.cs`, `UnitTests/.../FalconFlowExceptionClassifierTests.cs`, `UnitTests/.../FalconAccessProberTests.cs`
- W3 owns: `Flows/Findings/TwoPhase/*` (all six), `Flows/Findings/FalconFindingsFlow.cs`, `Flows/Findings/FalconFindingsCheckpointWriter.cs`, and the phase-0-frozen spine files
- W4 owns: `UnitTests/.../FalconCollectorTests.cs`, `UnitTests/.../FalconTwoPhaseFindingsTests.cs`, `UnitTests/.../FalconPolicyEnrichmentTests.cs` — tests only, may not write under `src/`
- Not in scope for any worker: `Flows/Assets/*` (must keep working unchanged; W4 proves it)

## Worker Plan
- W0 — scope: freeze shared spine  output: rejection-outcome type, enricher signature, envelope edge/composed split  phase: 0  (main thread)
- W1 — scope: full-body read via `IHttpSession`, requested-vs-returned rejection semantics, definitions sweep URL  owns: set-A paths  inputs: W0.output  output: clients returning per-id outcomes  phase: 1  continuity: fresh  attention items: R3
- W2 — scope: classifier arm discriminating `DataPipelineException` causes; prober must not fail init on 404  owns: set-B paths  inputs: none  output: classification + validation changes with tests  phase: 1  continuity: fresh  attention items: R4
- W3 — scope: manifest written after scroll; staged ledger with O(1) per-stage state; policies as post-freeze stage; edge-only staged page; Phase 2 composition; `pgen_` paths + pruner arm; streaming staged-page write; mid-scroll 500 re-anchor  owns: set-C paths + spine  inputs: W0.output, W1.output  output: restructured phase 1/2  phase: 2  continuity: fresh  attention items: R1, R2
- W4 — scope: tripwire tests for R1, R2, R5 and the assets-unchanged proof  owns: test files only  inputs: W0.output, W1.output, W3.output  output: tests  phase: 3  continuity: resumed — it must react to review findings on its own tests  attention items: R5

## Synthesis Approach
Main thread integrates in phase order. W1 and W2 are independent and merge without mediation. W3 consumes W1's outcome type. W4 runs last because its tripwires must drive the real production path, not a stand-in — the flow-patterns skill's rule. After synthesis, run the change-relevant test classes only (never the full FalconCollector suite, per the prior task's still-binding constraint).

## Verification Obligations
- Cross-check against `prompt_contract.md` Success Criteria.
- The manifest-first invariant is non-negotiable and operator-reiterated: no worker may reorder it, and the verifier must confirm `WriteManifestAsync` precedes any policy work in `SpoolAsync`.
- Confirm the two pre-existing branch changes survived (config default `false`, `CollectorVersion` 6.3.4).
- Confirm `Flows/Assets/*` is unmodified.
- Confirm every R-id's named test exists and passes.
