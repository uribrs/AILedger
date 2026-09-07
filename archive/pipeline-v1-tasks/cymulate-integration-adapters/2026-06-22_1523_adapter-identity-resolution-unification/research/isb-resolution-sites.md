# ISB Adapter Identity Resolution — Site Inventory

**Date:** 2026-06-22  
**Repo root:** `/Users/user/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus`

---

## 1. `PlatformType` Enum

**File:** `Sdk/Cymulate.Integration.Sdk/Enums/PlatformType.cs`

| Value | EnumMember string |
|-------|-------------------|
| `Unknown = 0` | `"unknown"` |
| `MicrosoftDefender = 1` | `"microsoftAtp"` |
| `CrowdStrikeFalcon = 2` | `"crowdStrikeFalcon"` |
| `SentinelOne = 3` | `"sentinelone"` |
| `PaloAltoNetworks = 4` | `"paloAltoNetworks"` |
| `FortiGate = 5` | `"FortinetFortiEdr"` |
| `CiscoSecureX = 6` | **`"CiscoSecureEndpoint"`** ← duplicate |
| `TrendMicro = 7` | `"TrendMicroVisionOne"` |
| `Zscaler = 8` | `"Zscaler"` |
| `CarbonBlack = 9` | `"carbonBlack"` |
| `CortexXDR = 10` | `"paloAltoCortexXdr"` |
| `Cybereason = 11` | `"CybereasonEdr"` |
| `CheckPointHarmony = 12` | `"CheckPointHarmonyEndpoint"` |
| `CiscoUmbrella = 13` | **`"CiscoSecureEndpoint"`** ← duplicate (same as CiscoSecureX) |
| `PaloAltoFirewall = 14` | `"PaloAltoNGFW"` |
| `Sophos = 15` | `"Sophos"` |
| `TenableIo = 16` | `"Tenable.io"` |
| `Rapid7InsightVM = 17` | `"InsightVM"` |
| `Rapid7InsightVMCloud = 18` | `"InsightVM Cloud"` |
| `TenableSc = 19` | `"Tenable.sc"` |
| `MicrosoftEntraId = 20` | `"Microsoft Entra ID"` |
| `Qualys = 21` | `"Qualys"` |
| `SentinelOneSingularityXDR = 22` | `"SentinelOne Singularity XDR"` |
| `Guardicore = 23` | `"Guardicore"` |
| `CloudGuard = 24` | `"CloudGuard"` |
| `ServiceNowCmdb = 25` | `"Service Now Cmdb"` |
| `DefenderVm = 26` | `"Defender VM"` |
| `YamlEngine = 98` | `"yaml_engine"` |
| `Custom = 99` | `"custom"` |

**Duplicate EnumMember flag:** Both `CiscoSecureX` (line 29) and `CiscoUmbrella` (line 49) carry `[EnumMember(Value = "CiscoSecureEndpoint")]`. Any serializer that round-trips through EnumMember strings cannot distinguish them.

---

## 2. `ProductPlatformMapper`

**File:** `Domain/Cymulate.IntegrationServiceBus.Domain/Services/ProductPlatformMapper.cs`

### 2a. `ProductToPlatformMap` (string → PlatformType, case-insensitive)

All canonical EnumMember keys are listed first, then display-name aliases.

| Input string | PlatformType |
|---|---|
| `"unknown"` | `Unknown` |
| `"microsoftAtp"` | `MicrosoftDefender` |
| `"crowdStrikeFalcon"` | `CrowdStrikeFalcon` |
| `"sentinelone"` | `SentinelOne` |
| `"paloAltoNetworks"` | `PaloAltoNetworks` |
| `"FortinetFortiEdr"` | `FortiGate` |
| `"CiscoSecureEndpoint"` | `CiscoSecureX` (note: maps to SecureX, not Umbrella) |
| `"TrendMicroVisionOne"` | `TrendMicro` |
| `"Zscaler"` | `Zscaler` |
| `"carbonBlack"` | `CarbonBlack` |
| `"paloAltoCortexXdr"` | `CortexXDR` |
| `"CybereasonEdr"` | `Cybereason` |
| `"CheckPointHarmonyEndpoint"` | `CheckPointHarmony` |
| `"PaloAltoNGFW"` | `PaloAltoFirewall` |
| `"Sophos"` | `Sophos` |
| `"Tenable.io"` | `TenableIo` |
| `"Tenable.sc"` | `TenableSc` |
| `"InsightVM"` | `Rapid7InsightVM` |
| `"InsightVM Cloud"` | `Rapid7InsightVMCloud` |
| `"custom"` | `Custom` |
| `"Crowdstrike Falcon"` | `CrowdStrikeFalcon` |
| `"CrowdStrike Falcon"` | `CrowdStrikeFalcon` |
| `"Microsoft Defender"` | `MicrosoftDefender` |
| `"Microsoft Defender ATP"` | `MicrosoftDefender` |
| `"Microsoft Defender for Endpoint"` | `MicrosoftDefender` |
| `"SentinelOne"` | `SentinelOne` |
| `"Sentinel One"` | `SentinelOne` |
| `"Palo Alto Networks"` | `PaloAltoNetworks` |
| `"Palo Alto Cortex XDR"` | `CortexXDR` |
| `"Cortex XDR"` | `CortexXDR` |
| `"Palo Alto NGFW"` | `PaloAltoFirewall` |
| `"Palo Alto Firewall"` | `PaloAltoFirewall` |
| `"Fortinet FortiEDR"` | `FortiGate` |
| `"FortiEDR"` | `FortiGate` |
| `"Cisco Secure Endpoint"` | `CiscoSecureX` |
| `"Cisco SecureX"` | `CiscoSecureX` |
| `"Cisco Umbrella"` | `CiscoUmbrella` |
| `"Trend Micro Vision One"` | `TrendMicro` |
| `"Trend Micro"` | `TrendMicro` |
| `"Carbon Black"` | `CarbonBlack` |
| `"VMware Carbon Black"` | `CarbonBlack` |
| `"Cybereason EDR"` | `Cybereason` |
| `"Cybereason"` | `Cybereason` |
| `"CheckPoint Harmony Endpoint"` | `CheckPointHarmony` |
| `"Check Point Harmony"` | `CheckPointHarmony` |
| `"Sophos Endpoint"` | `Sophos` |
| `"Tenable io"` | `TenableIo` |
| `"Tenable sc"` | `TenableSc` |
| `"TenableIo"` | `TenableIo` |
| `"Tenable"` | `TenableIo` |
| `"Rapid7 InsightVM"` | `Rapid7InsightVM` |
| `"Rapid7InsightVM"` | `Rapid7InsightVM` |
| `"Insight VM"` | `Rapid7InsightVM` |
| `"Rapid7"` | `Rapid7InsightVM` |
| `"Rapid7 InsightVM Cloud"` | `Rapid7InsightVMCloud` |
| `"Rapid7InsightVMCloud"` | `Rapid7InsightVMCloud` |
| `"InsightVMCloud"` | `Rapid7InsightVMCloud` |
| `"Insight VM Cloud"` | `Rapid7InsightVMCloud` |
| `"Microsoft Entra ID"` | `MicrosoftEntraId` |
| `"Qualys"` | `Qualys` |
| `"QualysCollector"` | `Qualys` |
| `"SentinelOne Singularity XDR"` | `SentinelOneSingularityXDR` |
| `"Guardicore"` | `Guardicore` |
| `"CloudGuard"` | `CloudGuard` |
| `"Service Now Cmdb"` | `ServiceNowCmdb` |
| `"Defender VM"` | `DefenderVm` |

**Notes:**
- `YamlEngine` is **absent** from `ProductToPlatformMap`. No string resolves to it via this map.
- Missing null/empty → `Unknown` (line 129); unknown string → `Custom` (line 130).

### 2b. `PlatformToProductMap` (PlatformType → canonical product ID)

| PlatformType | Canonical product ID |
|---|---|
| `Unknown` | `"unknown"` |
| `MicrosoftDefender` | `"microsoftAtp"` |
| `CrowdStrikeFalcon` | `"crowdStrikeFalcon"` |
| `SentinelOne` | `"sentinelone"` |
| `PaloAltoNetworks` | `"paloAltoNetworks"` |
| `FortiGate` | `"FortinetFortiEdr"` |
| `CiscoSecureX` | `"CiscoSecureEndpoint"` |
| `TrendMicro` | `"TrendMicroVisionOne"` |
| `Zscaler` | `"Zscaler"` |
| `CarbonBlack` | `"carbonBlack"` |
| `CortexXDR` | `"paloAltoCortexXdr"` |
| `Cybereason` | `"CybereasonEdr"` |
| `CheckPointHarmony` | `"CheckPointHarmonyEndpoint"` |
| `CiscoUmbrella` | `"CiscoSecureEndpoint"` ← same string as CiscoSecureX |
| `PaloAltoFirewall` | `"PaloAltoNGFW"` |
| `Sophos` | `"Sophos"` |
| `TenableIo` | `"Tenable.io"` |
| `TenableSc` | `"Tenable.sc"` |
| `Rapid7InsightVM` | `"InsightVM"` |
| `Rapid7InsightVMCloud` | `"InsightVM Cloud"` |
| `MicrosoftEntraId` | `"Microsoft Entra ID"` |
| `Qualys` | `"Qualys"` (line 111); **then immediately overwritten by `"QualysCollector"` (line 112)** |
| `SentinelOneSingularityXDR` | `"SentinelOne Singularity XDR"` |
| `Guardicore` | `"Guardicore"` |
| `CloudGuard` | `"CloudGuard"` |
| `ServiceNowCmdb` | `"Service Now Cmdb"` |
| `DefenderVm` | `"Defender VM"` |
| `Custom` | `"custom"` |

**Bug — Qualys overwrite (lines 111–112):** Two entries share `PlatformType.Qualys` as the key. C# dictionary initializers allow this; the second write (`"QualysCollector"`) silently wins. `ToProductId(PlatformType.Qualys)` returns `"QualysCollector"`, not `"Qualys"`.

**Missing:** `YamlEngine` has no entry in `PlatformToProductMap`. `ToProductId(PlatformType.YamlEngine)` returns `null`.

### 2c. Public methods

| Method | Behaviour |
|---|---|
| `ToPlatformType(string?)` | Null/empty → `Unknown`; not found → `Custom` |
| `ToProductId(PlatformType)` | Not found → `null` |
| `IsSupported(string?)` | True iff `ToPlatformType(...)` != `Unknown` |
| `GetSupportedProductIds()` | Returns all keys of `ProductToPlatformMap` |
| `GetAliases(PlatformType)` | Returns all keys in `ProductToPlatformMap` that map to the given value |

---

## 3. `VendorPlatformResolver`

**File:** `Domain/Cymulate.IntegrationServiceBus.Domain/Services/VendorPlatformResolver.cs`

### 3a. `PlatformToVendorMap` (PlatformType → display string)

| PlatformType | Display string |
|---|---|
| `Unknown` | `"Unknown"` |
| `MicrosoftDefender` | `"Microsoft Defender"` |
| `CrowdStrikeFalcon` | `"Crowdstrike Falcon"` |
| `SentinelOne` | `"SentinelOne"` |
| `PaloAltoNetworks` | `"Palo Alto Networks"` |
| `FortiGate` | `"Fortinet FortiEDR"` |
| `CiscoSecureX` | `"Cisco Secure Endpoint"` |
| `TrendMicro` | `"Trend Micro Vision One"` |
| `Zscaler` | `"Zscaler"` |
| `CarbonBlack` | `"Carbon Black"` |
| `CortexXDR` | `"Palo Alto Cortex XDR"` |
| `Cybereason` | `"Cybereason"` |
| `CheckPointHarmony` | `"CheckPoint Harmony Endpoint"` |
| `CiscoUmbrella` | `"Cisco Umbrella"` |
| `PaloAltoFirewall` | `"Palo Alto NGFW"` |
| `Sophos` | `"Sophos"` |
| `TenableIo` | `"Tenable.io"` |
| `TenableSc` | `"Tenable.sc"` |
| `Rapid7InsightVM` | `"Rapid7 InsightVM"` |
| `Rapid7InsightVMCloud` | `"Rapid7 InsightVM Cloud"` |
| `MicrosoftEntraId` | `"Microsoft Entra ID"` |
| `Qualys` | `"Qualys"` |
| `SentinelOneSingularityXDR` | `"SentinelOne Singularity XDR"` |
| `Guardicore` | `"Guardicore"` |
| `CloudGuard` | `"CloudGuard"` |
| `ServiceNowCmdb` | `"Service Now Cmdb"` |
| `DefenderVm` | `"Defender VM"` |
| `YamlEngine` | `"YamlEngine"` |
| `Custom` | `"Custom"` |

**Notes:**
- `YamlEngine` IS present here (line 42), with display string `"YamlEngine"`.
- `Rapid7InsightVM` → `"Rapid7 InsightVM"` (with space after "Rapid7")
- `Rapid7InsightVMCloud` → `"Rapid7 InsightVM Cloud"` (with spaces)

### 3b. Public methods

| Method | Behaviour |
|---|---|
| `ResolvePlatform(string?)` | Delegates entirely to `ProductPlatformMapper.ToPlatformType(vendor)` (line 50) |
| `ResolveVendor(PlatformType)` | Looks up `PlatformToVendorMap`; returns `"Unknown"` if not found (line 55–58) |

---

## 4. `RegisteredAdapterPlatformResolver`

**File:** `Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Core/Services/RegisteredAdapterPlatformResolver.cs`

### 4a. `FallbackPlatforms` (string → PlatformType, case-insensitive)

| Fallback string | PlatformType |
|---|---|
| `"Crowdstrike Falcon"` | `CrowdStrikeFalcon` |
| `"CrowdStrike Falcon"` | `CrowdStrikeFalcon` |
| `"Defender VM"` | **`MicrosoftDefender`** ← mismatch (see Contradictions) |
| `"Defender Endpoint"` | `MicrosoftDefender` |
| `"Microsoft Defender"` | `MicrosoftDefender` |
| `"SentinelOne Singularity XDR"` | `SentinelOne` ← maps to base SentinelOne, not SentinelOneSingularityXDR |
| `"SentinelOne"` | `SentinelOne` |
| `"Cortex XDR"` | `CortexXDR` |
| `"Palo Alto Cortex XDR"` | `CortexXDR` |
| `"Palo Alto Networks"` | `PaloAltoNetworks` |
| `"Carbon Black"` | `CarbonBlack` |
| `"Trend Micro"` | `TrendMicro` |
| `"Trend Micro Vision One"` | `TrendMicro` |
| `"Check Point Harmony"` | `CheckPointHarmony` |
| `"CheckPoint Harmony Endpoint"` | `CheckPointHarmony` |
| `"Cisco Secure Endpoint"` | `CiscoSecureX` |
| `"Cisco Umbrella"` | `CiscoUmbrella` |
| `"Fortinet FortiEDR"` | `FortiGate` |
| `"FortiGate"` | `FortiGate` |
| `"Cybereason"` | `Cybereason` |
| `"Cybereason EDR"` | `Cybereason` |
| `"Sophos"` | `Sophos` |
| `"Zscaler"` | `Zscaler` |

**Missing from FallbackPlatforms:** Tenable, Qualys, InsightVM, InsightVM Cloud, Defender VM (mapped to wrong type), MicrosoftEntraId, Guardicore, CloudGuard, ServiceNowCmdb, YamlEngine, Custom.

### 4b. `TryResolvePlatformType` algorithm (lines 38–100)

Input: `AdapterPlatformResolutionRequest { Vendor, Category, DefinitionSource }`

Resolution order:

1. **Guard:** If `request.Vendor` is null/whitespace → error `"Vendor is required"`, return false.
2. **Candidate set:** Call `adapterRegistryManager.GetAllRegistrations()`, filter by `Category`, collect distinct `PlatformType` values (line 103–108).
3. **Empty guard:** If no candidates → error `"No registered adapters available for category: {category}"`, return false.
4. **NamedYaml shortcut:** If `DefinitionSource == NamedYaml && Category == Collectors && YamlEngine is a candidate` → return `YamlEngine` immediately (lines 63–66, 128–133).
5. **Primary match — ResolveVendor compare:** For each candidate PlatformType, call `vendorPlatformResolver.ResolveVendor(candidate)` and compare to `normalizedVendor` (OrdinalIgnoreCase). Collect all matches (lines 68–73).
   - Exactly 1 match → return it (lines 75–79).
   - >1 matches → error `"Vendor '{vendor}' is ambiguous"`, return false (lines 81–85).
   - 0 matches → continue to step 6.
6. **Fallback match:** Call `TryResolveFallbackPlatformType(normalizedVendor, candidatePlatforms, ...)` — looks up `FallbackPlatforms` dict by vendor string, then checks the mapped platform is in the candidate list (lines 87–89, 111–126).
7. **InlineYaml fallback:** If `DefinitionSource == InlineYaml && Category == Collectors && YamlEngine is a candidate` → return `YamlEngine` (lines 92–95, 135–140).
8. **Fail:** error `"No registered adapter matches vendor '{vendor}' in category: {category}"`, return false.

**The DLQ path:** A message whose `vendor` string is not in `VendorPlatformResolver.PlatformToVendorMap` values AND not in `FallbackPlatforms` falls through to step 8 → `TryResolvePlatformType` returns false → message is DLQ'd. The primary match in step 5 depends entirely on `ResolveVendor`, which returns the display strings from `VendorPlatformResolver.PlatformToVendorMap`, not the EnumMember strings and not the alias strings from `ProductPlatformMapper.ProductToPlatformMap`.

---

## 5. `AdapterCategoryResolver`

**File:** `Domain/Cymulate.IntegrationServiceBus.Domain/Services/AdapterCategoryResolver.cs`

### `ResolveFromTopic` mapping (line 45–54)

| Topic (case-insensitive) | AdapterCategory |
|---|---|
| `"collector"` / `"collectors"` / `"collect"` / `"assets"` / `"findings"` | `Collectors` |
| `"indicator"` / `"indicators"` / `"auto-remediation"` / `"ioc"` | `Indicators` |
| `"exclusion"` / `"exclusions"` | `Exclusions` |
| Anything else (including null/empty) | `Indicators` |

---

## 6. `TriggerFlowMapper`

**File:** `Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Core/Messaging/TriggerFlowMapper.cs`

- Injects `IAdapterCategoryResolver` and `IRegisteredAdapterPlatformResolver`.
- Calls `ResolveCategory(message.Topic)` → `categoryResolver.ResolveFromTopic(topic)` (line 185, 312).
- Calls `platformResolver.TryResolvePlatformType(new AdapterPlatformResolutionRequest(message.Vendor, category, definitionSource), ...)` (lines 189–194, 316–321).
- `definitionSource` determined by `ResolveDefinitionSource(additionalProperties)` (lines 424–431):
  - Has non-empty `"integrationName"` key → `NamedYaml`
  - Has non-empty `"yaml"` key → `InlineYaml`
  - Otherwise → `None`
- On resolution failure: returns `false` / `null` (message is dropped / DLQ'd).
- **Uses `IRegisteredAdapterPlatformResolver.TryResolvePlatformType`** — the registered-adapter-aware resolver, not `IVendorPlatformResolver.ResolvePlatform`.

---

## 7. `AdapterMessageMapper`

**File:** `Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Core/Messaging/AdapterMessageMapper.cs`

- Injects `IVendorPlatformResolver`.
- `MapFromRunMessage` calls `_vendorResolver.ResolvePlatform(message.Vendor)` (line 24), which delegates to `ProductPlatformMapper.ToPlatformType(vendor)`.
- **Uses `IVendorPlatformResolver.ResolvePlatform`** → `ProductPlatformMapper.ToPlatformType` (wide alias table).
- This mapper is used for `AdapterRunContext` assembly — a different downstream path from `TriggerFlowMapper`.
- `ResolveCategoryFromTopic` in this class (lines 106–113) duplicates `AdapterCategoryResolver.ResolveFromTopic` logic but with a subtly different set:
  - `"collector" or "collectors"` → `Collectors`
  - `"indicator" or "indicators" or "auto-remediation"` → `Indicators`
  - `"exclusion" or "exclusions"` → `Exclusions`
  - `_` → `Unknown` (not `Indicators`) — diverges from `AdapterCategoryResolver`

---

## 8. `WrappedAdapterMessageMapper`

**File:** `Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Core/Messaging/WrappedAdapterMessageMapper.cs`

- Static class; no injected resolver.
- `TryParseToPlatformEvent` calls **`ProductPlatformMapper.ToPlatformType(vendor)`** directly (line 138).
- **Bypasses both `VendorPlatformResolver` and `RegisteredAdapterPlatformResolver`.**
- Does NOT check registered adapters; does NOT use the `ResolveVendor` display-name comparison.
- Topics handled: excludes `"ioc"`, `"delete-ioc"`, `"ioa"`, `"delete-ioa"` (those route to `MitigationActionMapper`). All other topics pass through.

---

## 9. `MitigationActionMapper`

**File:** `Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Core/Messaging/MitigationActionMapper.cs`

- Static class; no injected resolver.
- Three call sites for platform resolution, all via **`ProductPlatformMapper.ToPlatformType(...)`**:
  - `MapToDomain` (flat format): `ProductPlatformMapper.ToPlatformType(dto.ProductType)` (line 205).
  - `MapWrappedToDomain` (wrapped format): `ProductPlatformMapper.ToPlatformType(productType)` (line 288), where `productType` = `payload.ProductType ?? resolvedIntegrationType ?? dto.Vendor`.
  - `MapExposure` (legacy exposures): `ProductPlatformMapper.ToPlatformType(productId)` (line 572).
- **Bypasses both `VendorPlatformResolver` and `RegisteredAdapterPlatformResolver`.**

---

## 10. `AdapterRegistry`

**File:** `Applications/Cymulate.IntegrationServiceBus.Application/Services/AdapterRegistry.cs`

- Registration key: `(PlatformType, AdapterCategory, string? ClientId)` — tuple on `ConcurrentDictionary` (line 17).
- `Register(AdapterRegistration)`: key = `(registration.PlatformType, registration.Category, registration.ClientId)` (line 27–28). Upserts; newer registration for same key overwrites.
- Lookup (`GetRegistration`): first tries `(platformType, category, clientId)`, then falls back to `(platformType, category, null)` if `clientId` is non-null and specific key is absent (lines 110–125).
- `IsAdapterRegistered(PlatformType)`: matches any registration with that PlatformType (line 214–217).
- `IsAdapterRegistered(PlatformType, AdapterCategory)`: matches PlatformType+Category pair (line 219–222).
- Adapter instances are NOT stored here — only `AdapterRegistration` metadata (type info, version, etc.). Instances are created per-execution.

---

## Contradictions

### C1. `"Defender VM"` maps to different PlatformTypes depending on path

| Source | Input string | Maps to |
|---|---|---|
| `ProductPlatformMapper.ProductToPlatformMap` (line 82) | `"Defender VM"` | `PlatformType.DefenderVm` |
| `VendorPlatformResolver.PlatformToVendorMap` (line 41) | `PlatformType.DefenderVm` → `"Defender VM"` | `DefenderVm` (consistent) |
| `RegisteredAdapterPlatformResolver.FallbackPlatforms` (line 16) | `"Defender VM"` | **`PlatformType.MicrosoftDefender`** |

A message with `vendor = "Defender VM"` that fails the primary `ResolveVendor` match (because no registered adapter has `PlatformType.DefenderVm`) will fall to `FallbackPlatforms`, which routes it to `MicrosoftDefender`. This is almost certainly wrong: `DefenderVm` is a distinct platform type with its own adapter registration.

### C2. InsightVM / InsightVM Cloud: VendorPlatformResolver disagrees with EnumMember

| Source | Rapid7InsightVM display string | Rapid7InsightVMCloud display string |
|---|---|---|
| `PlatformType` EnumMember | `"InsightVM"` | `"InsightVM Cloud"` |
| `VendorPlatformResolver.PlatformToVendorMap` (lines 33–34) | **`"Rapid7 InsightVM"`** | **`"Rapid7 InsightVM Cloud"`** |
| `ProductPlatformMapper.PlatformToProductMap` (lines 108–109) | `"InsightVM"` | `"InsightVM Cloud"` |

The trigger-flow consume path uses `VendorPlatformResolver.ResolveVendor` for the primary match (step 5 in `RegisteredAdapterPlatformResolver`). So an inbound message with `vendor = "InsightVM"` (the EnumMember canonical string) will NOT match in step 5 — `ResolveVendor(Rapid7InsightVM)` returns `"Rapid7 InsightVM"`, not `"InsightVM"`. It then falls to `FallbackPlatforms`, which has neither `"InsightVM"` nor `"InsightVM Cloud"`. Result: DLQ.

Conversely, `vendor = "Rapid7 InsightVM"` WILL match via step 5 if the adapter is registered.

### C3. `"SentinelOne Singularity XDR"` fallback maps to wrong PlatformType

| Source | Input | Maps to |
|---|---|---|
| `ProductPlatformMapper` (line 78) | `"SentinelOne Singularity XDR"` | `SentinelOneSingularityXDR` |
| `VendorPlatformResolver` (line 37) | `PlatformType.SentinelOneSingularityXDR` → `"SentinelOne Singularity XDR"` | `SentinelOneSingularityXDR` |
| `RegisteredAdapterPlatformResolver.FallbackPlatforms` (line 19) | `"SentinelOne Singularity XDR"` | **`SentinelOne`** (base type) |

If the primary match fails (adapter not registered as `SentinelOneSingularityXDR`), the fallback silently redirects to the base `SentinelOne` adapter. May be intentional routing, but inconsistent with the dedicated enum value and `ProductPlatformMapper` mapping.

### C4. Cisco dual-enum / shared EnumMember

Both `CiscoSecureX = 6` and `CiscoUmbrella = 13` carry `[EnumMember(Value = "CiscoSecureEndpoint")]`. Any JSON deserializer using EnumMember values cannot distinguish them. `ProductToPlatformMap` key `"CiscoSecureEndpoint"` resolves to `CiscoSecureX` (the first one alphabetically/positionally in the map), but `PlatformToProductMap` also maps `CiscoUmbrella → "CiscoSecureEndpoint"`. Round-trip via EnumMember is broken for `CiscoUmbrella`.

### C5. `Qualys` overwrite in `PlatformToProductMap`

Lines 111–112 in `ProductPlatformMapper`:
```csharp
[PlatformType.Qualys] = "Qualys",
[PlatformType.Qualys] = "QualysCollector",  // overwrites
```
`ToProductId(PlatformType.Qualys)` returns `"QualysCollector"`, not `"Qualys"`. This is almost certainly a copy-paste error; the intent was to add `QualysCollector` as a `ProductToPlatformMap` alias entry, not a second reverse-map entry.

### C6. `AdapterMessageMapper.ResolveCategoryFromTopic` diverges from `AdapterCategoryResolver`

`AdapterMessageMapper` (line 106–113): unknown topic → `AdapterCategory.Unknown`.  
`AdapterCategoryResolver.ResolveFromTopic` (line 45–54): unknown/null topic → `AdapterCategory.Indicators`.

This is a silent behavioural divergence between the two category-resolution code paths.

### C7. `YamlEngine` missing from `ProductPlatformMapper`

`VendorPlatformResolver.PlatformToVendorMap` maps `YamlEngine → "YamlEngine"`. However:
- `ProductPlatformMapper.ProductToPlatformMap` has no `"YamlEngine"` key.
- `ProductPlatformMapper.PlatformToProductMap` has no `YamlEngine` entry.

`ProductPlatformMapper.ToPlatformType("YamlEngine")` returns `PlatformType.Custom`, not `PlatformType.YamlEngine`. This affects `WrappedAdapterMessageMapper` and `MitigationActionMapper` — both call `ProductPlatformMapper.ToPlatformType` directly. A wrapped message with `vendor = "YamlEngine"` would produce `PlatformType.Custom`, not `PlatformType.YamlEngine`.

---

## Resolution Paths

### Path 1: TriggerFlowMapper (collector/indicator trigger)

```
Inbound JSON: { topic, vendor, payload: { action: { flows, ... } } }
            or { topic, vendor, payload: { flows, ... } }  [flat format]

1. TriggerFlowMapper.TryParseToPlatformEvent
2.   → AdapterCategoryResolver.ResolveFromTopic(topic)          [topic → AdapterCategory]
3.   → ResolveDefinitionSource(additionalProperties)             [yaml/integrationName → DefinitionSource]
4.   → RegisteredAdapterPlatformResolver.TryResolvePlatformType(vendor, category, definitionSource)
        a. GetAllRegistrations() filtered by category → candidate PlatformType list
        b. NamedYaml shortcut → YamlEngine (if registered)
        c. For each candidate: VendorPlatformResolver.ResolveVendor(candidate) == vendor?
        d. Fallback: FallbackPlatforms[vendor] if in candidate list
        e. InlineYaml fallback → YamlEngine (if registered)
        f. Fail → null / error (DLQ)
5.   → PlatformEvent { ProductType = resolved PlatformType }
6. ProcessEventCommandHandler
7.   → AdapterRegistry.GetRegistration(platformType, category, clientId)
8.   → AdapterActivator creates adapter instance, executes
```

**String consumed:** `message.Vendor` (from wire message).  
**Primary comparison:** `VendorPlatformResolver.ResolveVendor(registeredCandidate)` vs `message.Vendor`.  
**ProductPlatformMapper / EnumMember strings: NOT used on this path for primary match.**

---

### Path 2: WrappedAdapterMessageMapper (query operations: get-ioc, get-ioa, handshake, etc.)

```
Inbound JSON: { topic, vendor, payload: { actionId, clientId, ... } }
              topic NOT in [ioc, delete-ioc, ioa, delete-ioa]

1. WrappedAdapterMessageMapper.TryParseToPlatformEvent
2.   → ProductPlatformMapper.ToPlatformType(vendor)   [direct, no registration check]
3.   → PlatformEvent { ProductType = resolved PlatformType }
4. ProcessEventCommandHandler
5.   → AdapterRegistry.GetRegistration(platformType, category, clientId)
```

**String consumed:** `vendor` field from JSON.  
**Uses ProductPlatformMapper alias table directly. No registered-adapter check. YamlEngine NOT reachable (no alias for it).**

---

### Path 3: MitigationActionMapper (IOC/IOA upload/delete)

```
Inbound JSON: { topic: "ioc"|"delete-ioc"|"ioa"|"delete-ioa", vendor, payload: { ... } }
         or flat: { actionId, clientId, productType, indicators }

1. MitigationActionMapper.TryParseMessage / ParseMessage
   a. Wrapped format: productType = payload.ProductType ?? resolvedIntegrationType ?? vendor
      → ProductPlatformMapper.ToPlatformType(productType)
   b. Flat format:
      → ProductPlatformMapper.ToPlatformType(dto.ProductType)
   c. Legacy exposures:
      → ProductPlatformMapper.ToPlatformType(exposure.MitigationProduct or dto.ProductType)
2.   → MitigationAction { PlatformType = resolved PlatformType }
3. MitigationActionHandler
4.   → AdapterRegistry.GetRegistration(platformType, Indicators, clientId)
```

**String consumed:** `payload.ProductType` → `payload.integrationType` → `dto.Vendor` (cascade).  
**Uses ProductPlatformMapper directly. No registered-adapter check.**

---

### Path 4: AdapterMessageMapper (AdapterRunContext, older/secondary path)

```
Inbound: AdapterRunMessage (already deserialized)

1. AdapterMessageMapper.MapFromRunMessage
2.   → VendorPlatformResolver.ResolvePlatform(message.Vendor)
         → ProductPlatformMapper.ToPlatformType(vendor)   [same as ProductPlatformMapper directly]
3.   → AdapterRunContext { Platform = resolved PlatformType }
```

**String consumed:** `message.Vendor`.  
**Uses `VendorPlatformResolver.ResolvePlatform`, which is a thin wrapper over `ProductPlatformMapper.ToPlatformType`. Does NOT check registered adapters.**

---

## Which Maps Are Active vs Dead on the Trigger-Flow Path

| Map / method | Used by TriggerFlowMapper path? | How |
|---|---|---|
| `VendorPlatformResolver.PlatformToVendorMap` | **YES — primary match** | `ResolveVendor(candidate)` vs inbound vendor string |
| `RegisteredAdapterPlatformResolver.FallbackPlatforms` | **YES — fallback** | After primary match fails |
| `ProductPlatformMapper.ProductToPlatformMap` | **Indirectly** (only via `VendorPlatformResolver.ResolvePlatform` which is the same as `ToPlatformType`) | Not called on the forward resolution path |
| `ProductPlatformMapper.PlatformToProductMap` | **Dead on trigger-flow path** | Not called anywhere in TriggerFlowMapper or RegisteredAdapterPlatformResolver |
| `ProductPlatformMapper.ToPlatformType` (aliases) | **Dead on primary match** | Primary match uses `ResolveVendor` (display names), not `ToPlatformType` aliases |
| `AdapterCategoryResolver.ResolveFromTopic` | **YES** | topic → AdapterCategory |

**Key insight:** The trigger-flow consume path's primary resolution pivots on `VendorPlatformResolver.PlatformToVendorMap` display strings (e.g. `"Crowdstrike Falcon"`, `"Rapid7 InsightVM"`), not on `ProductToPlatformMap`'s many aliases. A vendor string that matches a `ProductToPlatformMap` alias but not the `PlatformToVendorMap` display string will fail primary match and must be caught by `FallbackPlatforms`; if not in `FallbackPlatforms` either, the message DLQs.
