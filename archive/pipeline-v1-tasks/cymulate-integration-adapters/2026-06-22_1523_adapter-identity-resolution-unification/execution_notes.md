# Execution Notes

## Phase A — Audit (complete)
Three parallel read-only workers (W1 ISB, W2 adapters, W3 catalog) ran and wrote:
- research/isb-resolution-sites.md
- research/adapter-identity.md
- research/catalog-permutations.md
- research/identification-profile.md (synthesis)

### Outcome
Audit confirmed the root cause and surfaced 6 sibling defects (all resolved by converging on one resolver) — see identification-profile.md §2. It also surfaced one finding that qualifies the locked identity model: **`PlatformType.Custom` is shared by 4 collectors** (DefenderForCloud, Taegis, IsbLoadTest, Dummy), so `(PlatformType, Category)` is not unique for `Custom`. The model holds for every real catalog vendor.

### Assumptions updated
- A1 VALIDATED — live `cybiIntegrationSetting.csv` is authoritative; seed `integrationsData.js` is unusable as contract-test source (no flows, divergent names).
- A4 reinforced — unknown vendor must fail closed; never default to `Custom` (4 Custom collectors registered).
- A2 VALIDATED — topic/category derives from live `flows[N].type` (CollectAssets/CollectFindings → Collectors).
- A3 still OPEN — Cisco duplicate EnumMember confirmed; fix requires SDK enum change (product/architecture decision).
- A5 RESOLVED — "Defender VM" and "SentinelOne Singularity XDR" fallback entries are actively WRONG (misroute), not just missing; deletion + correct unified entries fixes them.

## Phase B — Implementation (BLOCKED pending decisions)
Held at the audit/implementation boundary. 4 decisions in identification-profile.md must be resolved before coding the resolver — chiefly Decision 1 (Custom collisions) and Decision 3 (Cisco enum), which are product/architecture calls, not implementation details. Surfaced to the user via the coordinator.

## Cross-reference (adapters ↔ ISB identities) — per user re-scope (integrationsData parked)
- research/identity-crossref.md written.
- Shared PlatformType enum is IN SYNC: ISB SDK source 3.1.8 == adapters' Cymulate.Integration.Sdk 3.1.8; all 28 values identical. No drift.
- Every registered adapter's PlatformType exists in the shared enum.
- (PlatformType, Category) is a unique key for every REAL adapter EXCEPT the (Custom, Collector) collision: DefenderForCloud + Taegis (real) share it with IsbLoadTest + Dummy (test).
- Cisco duplicate EnumMember and CheckPointHarmony orphan are non-blocking under forward name→PlatformType resolution.
- Cross-repo coupling identified: enum lives in ISB Sdk/, published as the NuGet the adapters consume → adding values is an ISB-SDK edit → publish → adapters bump → repoint 2 collectors. Both repos branch first.

## EXECUTION HALTED at start — target branch diverged from audit basis
Branch `harden-adapter-resolving` (clean tree) = dev + merged `Hudini-yaml-collector` PRs (#624–#630). Re-baselined the resolver against current code:
- `RegisteredAdapterPlatformResolver.TryResolvePlatformType` now takes `AdapterPlatformResolutionRequest(Vendor, Category, DefinitionSource)` + `allowYamlEngineFallback`, NOT the audited `(vendor, category)` signature.
- It adds YAML-engine routing that MUST be preserved: `NamedYaml` collector → `PlatformType.YamlEngine`; `InlineYaml` collector → `YamlEngine` fallback. (`ShouldRouteNamedYamlToEngine` / `ShouldFallBackInlineYamlToEngine`.)
- The DLQ bug is UNCHANGED: still matches via `vendorPlatformResolver.ResolveVendor(candidate)` + `FallbackPlatforms` (with the wrong "Defender VM"→MicrosoftDefender, "SentinelOne Singularity XDR"→SentinelOne entries).
- `ProductPlatformMapper` and `VendorPlatformResolver` are UNCHANGED from audit (ProductToPlatformMap is actually mostly correct; the bug is that the registered resolver never uses it).
- Callers updated: `TriggerFlowMapper` (×2) and `TestAdapterConnectionCommandHandler` build the request object; TriggerFlowMapper computes `definitionSource`.

### Impact on plan
Design holds, with one scope addition: the unified resolver path MUST preserve YAML-engine routing. Minimal convergence is actually: make `RegisteredAdapterPlatformResolver` resolve via the single table (ProductPlatformMapper/AdapterIdentityResolver) instead of ResolveVendor+FallbackPlatforms, keep the YAML branches, delete FallbackPlatforms. Fail-closed (Unknown, not Custom) for unmapped.

### Why halted
Preserving YAML-engine routing was not in the contract; the branch is actively receiving related merges. Surfacing before a 6-file cross-cutting refactor rather than adapting silently.

## Implementation (ISB) — DONE on harden-adapter-resolving (design revised per user)
Design pivot (user correction): NO alias table in the core resolver. Canonical name = the PlatformType [EnumMember] value (single source). Legacy backend spellings quarantined in a separate, temporary normalizer.

Files:
- Sdk/.../Enums/PlatformType.cs: added SecureworksTaegis=27 ("Secureworks Taegis"), MicrosoftDefenderForCloud=28 ("Microsoft Defender for Cloud"); fixed CiscoUmbrella EnumMember "CiscoSecureEndpoint" -> "Cisco Umbrella" (uniqueness now REQUIRED — duplicate canonical names can't build a 1:1 map).
- Domain/.../Services/AdapterIdentityResolver.cs (NEW): single source. Builds canonical name<->PlatformType from the enum's [EnumMember] via reflection; FAILS FAST on duplicate EnumMember (startup guard). ResolvePlatform = normalize(legacy) then canonical lookup; fail-closed (Unknown, never Custom). ResolveVendor = the canonical EnumMember value.
- Domain/.../Services/LegacyVendorNameNormalizer.cs (NEW): quarantined, explicitly TEMPORARY map of known backend spellings -> canonical name. Death condition: catalog emits canonical names.
- Domain/.../Services/ProductPlatformMapper.cs: collapsed to a thin facade over AdapterIdentityResolver (deleted ProductToPlatformMap + PlatformToProductMap + dead ToProductId/GetAliases/GetSupportedProductIds; this also removed the Qualys double-assignment bug).
- Domain/.../Services/VendorPlatformResolver.cs: ResolvePlatform/ResolveVendor delegate to AdapterIdentityResolver (deleted PlatformToVendorMap).
- Infrastructure/.../Services/RegisteredAdapterPlatformResolver.cs: rewritten to resolve via AdapterIdentityResolver + category-filtered registry; DELETED FallbackPlatforms (and the wrong Defender VM / SentinelOne Singularity XDR entries); preserved YAML-engine routing (NamedYaml/InlineYaml); dropped the unused IVendorPlatformResolver ctor dep; fail-closed.

Convergence: all four entry points now resolve through one place — TriggerFlowMapper/TestAdapterConnection via RegisteredAdapterPlatformResolver; WrappedAdapterMessageMapper/MitigationActionMapper/AdapterMessageMapper via ProductPlatformMapper/VendorPlatformResolver which delegate to AdapterIdentityResolver. No parallel tables remain.

Tests (Tests/.../API.UnitTests/Services):
- AdapterIdentityResolverTests.cs (NEW): canonical resolution, legacy normalization, fail-closed (never Custom), per-PlatformType round-trip (ResolvePlatform(ResolveVendor(p))==p), idempotency.
- RegisteredAdapterPlatformResolverTests.cs: dropped removed ctor arg; rewrote the test that encoded the Defender VM->MicrosoftDefender misroute to assert fail-closed; added InsightVM/InsightVM Cloud/Taegis/DefenderForCloud regression + InsightVM-vs-Cloud distinction.

Build/verify:
- ISB production projects (Sdk, Domain, Infrastructure.Core) BUILD CLEAN.
- Test project could NOT restore (EmptyFiles.8.17.2 transitive Verify dep fails to download from nuget.org — transient feed issue, not code). Tests written + hand-traced; execution pending a working restore.

RESIDUAL RISKS / FOLLOW-UPS:
- ResolveVendor now returns the canonical EnumMember (e.g. "crowdStrikeFalcon") instead of the prior display name ("Crowdstrike Falcon"). Consumers ProcessEventCommandHandler:1355/1426 and EventsController:1290 surface this to done-queue consumers. VERIFY done-queue consumers tolerate canonical names, or add an explicit display map (separate concern).
- CiscoUmbrella EnumMember changed "CiscoSecureEndpoint" -> "Cisco Umbrella": contract change; check for any persisted/serialized PlatformType values.
- Adapters repo NOT yet edited (see below) — pending user confirm + SDK publish.

## Adapters repo — DONE (branch adapter-identity-platformtypes, off freshly pulled dev d54ac4c)
- TaegisIdentification.cs: PlatformType.Custom -> PlatformType.SecureworksTaegis
- DefenderForCloudIdentification.cs: PlatformType.Custom -> PlatformType.MicrosoftDefenderForCloud
- No other adapter code changes needed: Cisco Umbrella uses PlatformType.CiscoUmbrella (enum value unchanged; only its serialized name changed) -> rebuild only; all other adapters rebuild against new SDK, no code change.
- BUILD-BLOCKED (expected, per D13): these reference enum values not present in the consumed SDK 3.1.8. Will not compile until the user publishes an SDK containing SecureworksTaegis + MicrosoftDefenderForCloud + the Cisco Umbrella EnumMember fix, then bumps Directory.Packages.props `Cymulate.Integration.Sdk` (currently 3.1.8) to it.
