# Orchestration Plan

## Complexity Decision
- Path: decompose
- Rationale: The work has two phases. Phase A (audit, S1–S5) is three independent read-only investigations over different sources (ISB code, adapters-repo identity, catalog data) with clean boundaries and low coupling — ideal to fan out and synthesize. Phase B (implementation, S6–S10) is tightly coupled and follows the audit. The audit must complete and its collision decisions be surfaced before implementation, because encoding the wrong collision policy is a "wrong implementation" stop condition.

## Research Decisions
- None needed (no external `technical-researcher`). All OPEN assumptions (A1–A8) are internal code/data facts, validated by the audit workers, not external-system behavior. `workflow.researchNeeded = false`.

## Worker Plan (Phase A — audit, parallel)
- W1 — ISB resolution inventory. inputs: /Users/user/Dev/IntegrationServiceBus. output: research/isb-resolution-sites.md — every site that maps inbound vendor/topic→PlatformType/adapter (ProductPlatformMapper, VendorPlatformResolver, RegisteredAdapterPlatformResolver+FallbackPlatforms, TriggerFlowMapper, AdapterMessageMapper, WrappedAdapterMessageMapper, MitigationActionMapper), the PlatformType enum [EnumMember] values, AdapterRegistry.Register key, and which sites the trigger-flow consume path actually uses. Flag every contradiction between maps. deps: none.
- W2 — Adapter identity inventory (adapters repo). inputs: /Users/user/Dev/cymulate-integration-adapters. output: research/adapter-identity.md — for every collector and indicator: AdapterId, Name, PlatformType, SupportedTopics (from *Identification.cs) + Glossary (CollectorNames/IndicatorNames/AdapterTopics). Table of adapter → (PlatformType, Category, declared topics). Flag same-PlatformType-different-category and same-brand-different-PlatformType cases. deps: none.
- W3 — Catalog permutation harvest. inputs: dbMigrations/.../integrationsData.js + Uri/ClientLogs/.../cybiIntegrationSetting.csv. output: research/catalog-permutations.md — distinct `name` values (NOT `vendor`) + their flows/topics + category, reconciled between seed and live; note discrepancies (e.g. seed missing "InsightVM Cloud"). deps: none.

## Synthesis Approach (main thread)
Merge W1+W2+W3 into research/identification-profile.md: the full `(name, topic) -> (PlatformType, Category)` table, plus three defect lists — collisions (one (name,topic) → multiple identities), orphan names (catalog name with no serving adapter), and orphan adapters (registered adapter with no catalog name). Derive the collision-policy proposal (Cisco duplicate EnumMember; Custom/Taegis fail-closed; missing Defender Endpoint/FortiGate aliases). Surface product-decision points to the user before Phase B.

## Phase B (implementation — after audit + collision decisions)
- Single resolver `(name, topic) -> (PlatformType, Category)`; ProductPlatformMapper + VendorPlatformResolver delegate; RegisteredAdapterPlatformResolver/FallbackPlatforms + other sites converge; retire duplicate maps. Contract test. Branch-first in each repo. (May be re-planned as direct execution once the profile is fixed.)

## Verification Obligations
- Cross-check against prompt_contract.md Success Criteria 1–5.
- Resolution total + idempotent over every harvested permutation; InsightVM Cloud + InsightVM resolve; unknown fails closed (no silent Custom/Taegis match); collision policy documented; contract test passes; both repos build on non-default branches.
