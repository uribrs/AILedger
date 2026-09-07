# Adapter Identification Profile (synthesis of W1 + W2 + W3)

Sources: `isb-resolution-sites.md` (ISB), `adapter-identity.md` (adapters repo), `catalog-permutations.md` (catalog). This is the consolidated picture + the decisions that must be made before implementation.

## 1. Confirmed identity model
- `(PlatformType, Category)` is the identity for **all vendors except `Custom`** (see Decision 1).
- Resolution direction confirmed: **name → PlatformType**, **topic → Category**.
  - InsightVM vs InsightVM Cloud: both are `Collectors`, both flow `CollectFindings`, identical flow name. The ONLY discriminator is the catalog `name` ("InsightVM" vs "InsightVM Cloud") → PlatformType (`Rapid7InsightVM` vs `Rapid7InsightVMCloud`). Topic cannot tell them apart. This validates name-as-primary-key.
- Topic/category is derived from the live CSV `flows[N].type`: `CollectAssets` / `CollectFindings` → Collectors; `Connection` is a validation ping (not dispatched). Indicator flows route to Indicators. (Seed has no flows.)

## 2. Confirmed root cause + sibling defects (all fixed by converging on one resolver)
- DLQ cause: trigger-flow path (`RegisteredAdapterPlatformResolver.TryResolvePlatformType`) matches inbound vendor against `VendorPlatformResolver.PlatformToVendorMap` display strings + `FallbackPlatforms` — NOT the EnumMember or the `ProductPlatformMapper` alias table. `"InsightVM Cloud"` ≠ `"Rapid7 InsightVM Cloud"`, no fallback → DLQ.
- `FallbackPlatforms["Defender VM"] = MicrosoftDefender` — **wrong**, should be `DefenderVm`. Silent misroute.
- `FallbackPlatforms["SentinelOne Singularity XDR"] = SentinelOne` — **wrong**, should be `SentinelOneSingularityXDR`. Silent misroute.
- `PlatformToProductMap[Qualys]` assigned twice; `"QualysCollector"` silently wins over `"Qualys"`.
- `YamlEngine` absent from `ProductPlatformMapper` → resolves to `Custom`.
- `AdapterMessageMapper.ResolveCategoryFromTopic` returns `Unknown` for unknown topics; `AdapterCategoryResolver` returns `Indicators`. Divergent category logic across the 4 paths.
- Four resolution paths, three different resolvers (TriggerFlow→Registered; Wrapped+Mitigation→ProductPlatformMapper; AdapterMessage→VendorPlatformResolver). Convergence target confirmed.

## 3. Four resolution entry points to converge
| Entry point | Resolver used today | Registration-aware? |
|---|---|---|
| TriggerFlowMapper (collector/indicator runs) | RegisteredAdapterPlatformResolver (display-string + fallback) | yes |
| WrappedAdapterMessageMapper | ProductPlatformMapper.ToPlatformType | no |
| MitigationActionMapper | ProductPlatformMapper.ToPlatformType | no |
| AdapterMessageMapper | VendorPlatformResolver.ResolvePlatform (→ ProductPlatformMapper) | no |

## 4. Catalog reality (contract-test input)
- **Seed (`integrationsData.js`) is NOT a usable contract-test source**: no `flows`, names differ heavily from live, missing entries (no "InsightVM Cloud"). Only 3 names match live exactly (InsightVM, Tenable.io, Tenable.sc).
- **Live `cybiIntegrationSetting.csv` is authoritative** for `(name, flow.type)` → validates A1. Contract test should be built from the live catalog shape (curated fixture), not the seed.
- Case differences (e.g. "Crowdstrike Falcon" vs "CrowdStrike Falcon") are harmless — matching is `OrdinalIgnoreCase` — but token differences ("Qualys VM" vs "Qualys") are real and must be aliased.

## DECISIONS REQUIRED BEFORE IMPLEMENTATION

### Decision 1 — `PlatformType.Custom` is shared by 4 collectors (BREAKS the (PlatformType,Category) identity)
`DefenderForCloud`, `Taegis`, `IsbLoadTest`, `Dummy` collectors ALL declare `PlatformType.Custom`. So `(Custom, Collectors)` maps to 4 adapters — the tuple is NOT unique for Custom. The locked model holds for every real vendor but not for `Custom`.
- Options: (a) give each its own `PlatformType` enum value (cleanest, touches SDK + those adapters); (b) treat `Custom` as a special dispatch keyed additionally by name/AdapterId; (c) accept that `Custom` is non-routable via the catalog and out of scope (these may be test/internal adapters not driven by catalog `name`).
- Need: how does ISB pick the right Custom collector today? (Likely they aren't catalog-driven — Dummy/IsbLoadTest are test harnesses — but DefenderForCloud/Taegis may be real.)

### Decision 2 — unknown vendor must fail closed
`ToPlatformType` defaults unmapped → `Custom`. With Custom collectors registered, an unmapped name could silently match one. The unified resolver must return "no match" for unmapped names, never `Custom`. (Confirms A4.)

### Decision 3 — Cisco duplicate EnumMember
`CiscoSecureX` and `CiscoUmbrella` share `[EnumMember "CiscoSecureEndpoint"]`. Fixing means giving Umbrella its own EnumMember — an SDK enum change that ripples to both repos and to any serialized/persisted value. Decide: fix now, or document + special-case in the resolver.

### Decision 4 — contract-test source
Build the alignment test from the live catalog shape (recommended) — confirm whether a checked-in fixture derived from `cybiIntegrationSetting.csv` is acceptable, since the live DB isn't reachable from a unit test.
