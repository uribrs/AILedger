# Prompt Contract

## Role
You are a senior .NET engineer working across two repositories: Cymulate IntegrationServiceBus (host/router) and cymulate-integration-adapters (adapters), plus supporting data in dbMigrations.

## Goal
Replace ISB's scattered, contradictory vendor→platform resolution with a single total, idempotent resolver `(inbound name, topic) -> (PlatformType, Category)`, so any inbound vendor permutation routes to the correct registered adapter. Prove it with a contract test that ties every live-catalog integration `name` to a `(PlatformType, Category)` a registered adapter serves.

## Context
- Inbound trigger messages carry a `vendor` string (== integration catalog `name`) and a `topic` (collector/indicator). ISB must resolve these to an adapter registered under `(PlatformType, Category, ClientId)`.
- The failing path: `TriggerFlowMapper` -> `RegisteredAdapterPlatformResolver.TryResolvePlatformType` matches the inbound vendor against `VendorPlatformResolver.ResolveVendor(candidate)` (a hand-maintained `PlatformToVendorMap`) plus a `FallbackPlatforms` map. These disagree with `ProductPlatformMapper` and with the `PlatformType` `[EnumMember]` values.
- Concrete bug: `vendor "InsightVM Cloud"` DLQ'd because `ResolveVendor(Rapid7InsightVMCloud)` returns `"Rapid7 InsightVM Cloud"` and no fallback exists. Same for on-prem `InsightVM`.
- Resolution sites to converge: `ProductPlatformMapper`, `VendorPlatformResolver`, `RegisteredAdapterPlatformResolver` (+`FallbackPlatforms`), `TriggerFlowMapper`, `AdapterMessageMapper`, `WrappedAdapterMessageMapper`, `MitigationActionMapper`.
- Adapter identity declared in adapters repo: `Shared/.../Glossary` (CollectorNames, IndicatorNames, AdapterTopics) and `<vendor>Identification.cs` (`AdapterMetadata.PlatformType/Name/AdapterId/SupportedTopics`) for both collectors and indicators.
- Permutation sources (harvest `name`, not `vendor`): `dbMigrations/Application/data/integrations/integrationsData.js` (seed) and `Uri/ClientLogs/2026-06-22-dlqued-collector/cymulate.cybiIntegrationSetting.csv` (live).
- See `decisions.md` (D1–D8), `constraints.md`, `assumptions.md` (A1–A8) in this task dir.

## Constraints
(see constraints.md — all apply) Key ones:
- Branch first in BOTH repos; never commit on default/working branches.
- Keep `PlatformType`; do not replace the key type. Do not touch `ClientId` axis.
- Resolution total + idempotent; unknown vendor fails closed (must NOT silently match `Custom`/Taegis).
- Collision policy explicit + documented (Cisco duplicate EnumMember; missing `Defender Endpoint`/`FortiGate`).
- Simplest change that satisfies the design; justify any added complexity.
- net8.0 pinning in adapters csproj; CodeArtifact auth for NuGet; xUnit + Moq + FluentAssertions.

## Success Criteria
1. A single resolver owns `(name, topic) -> (PlatformType, Category)`. `ProductPlatformMapper` and `VendorPlatformResolver` delegate to it with their duplicate maps removed; `RegisteredAdapterPlatformResolver`/`FallbackPlatforms` and the other sites converge on it.
2. The `InsightVM Cloud` and `InsightVM` DLQ scenarios resolve correctly; resolution is total + idempotent across all harvested permutations.
3. Collision policy for Cisco, Custom/Taegis, and the missing aliases is encoded and documented.
4. Contract test passes: every live-catalog `name` resolves to a `(PlatformType, Category)` served by a registered adapter; unmapped names fail closed.
5. Both repos build and relevant tests pass; all changes on non-default branches in each repo.

## Execution Rules
- Do not assume missing data — validate the OPEN assumptions (A1–A8) against the actual code/data before encoding them.
- Audit before implementing: produce the full `(name, topic) -> (PlatformType, Category)` identification profile first (S1–S4); surface every collision, orphan name, and unregistered adapter.
- Respect constraints strictly; surface collision decisions, do not silently resolve them.
- Branch in each repo before editing it.

## Output Format
- Updated source on non-default branches in ISB (and adapters repo if required).
- The identification profile + collision policy captured in task-dir artifacts (e.g. `execution_notes.md` / research).
- The contract test + passing build/test evidence.

## Stop Conditions
- Goal achieved and success criteria met.
- A collision/ambiguity cannot be resolved without a product decision (e.g. Cisco) — surface it.
- Required data (catalog/source/registration) is missing or contradictory in a way that blocks correct mapping.
