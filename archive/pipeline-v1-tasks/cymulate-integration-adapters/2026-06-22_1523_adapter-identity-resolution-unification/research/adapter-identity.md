# Adapter Identity Inventory

Generated: 2026-06-22  
Repo root: `src/Cymulate.Integration.Adapters`

---

## 1. Glossary Constants

### CollectorNames.cs
`Shared/Cymulate.Integration.Adapters.Shared/Glossary/CollectorNames.cs`

| Constant | Value |
|---|---|
| `FalconCollector` | `"Crowdstrike Falcon"` |
| `TaegisCollector` | `"Secureworks Taegis"` |
| `DefenderVmCollector` | `"Defender VM"` |
| `SentineloneCollector` | `"SentinelOne Singularity XDR"` |
| `GuardicoreCollector` | `"Guardicore"` |
| `QualysCollector` | `"Qualys"` |
| `CymulateCollector` | `"Cymulate"` |
| `TenableIoCollector` | `"Tenable.io"` |
| `DefenderEndpointCollector` | `"Defender Endpoint"` |
| `ActiveDirectoryCollector` | `"Active Directory"` |
| `InsightVMCollector` | `"InsightVM"` |
| `InsightVMCloudCollector` | `"InsightVM Cloud"` |
| `CortexXDRCollector` | `"Cortex XDR"` |
| `CloudGuardCollector` | `"CloudGuard"` |
| `DefenderForCloudCollector` | `"Defender for Cloud"` |
| `TenableScCollector` | `"Tenable.sc"` |
| `MicrosoftEntraIdCollector` | `"Microsoft Entra ID"` |
| `ServiceNowCmdbCollector` | `"Service Now Cmdb"` |
| `IsbLoadTestCollector` | `"ISB Load Test"` |

Note: `DefenderEndpointCollector` and `ActiveDirectoryCollector` appear in `CollectorNames` but have no implementation under `Collectors/` with an Identification file. They are either deprecated stubs or external adapters not in this repo.

### IndicatorNames.cs
`Shared/Cymulate.Integration.Adapters.Shared/Glossary/IndicatorNames.cs`

| Constant | Value |
|---|---|
| `CarbonBlack` | `"CarbonBlack"` |
| `CiscoSecure` | `"CiscoSecure"` |
| `CiscoUmbrella` | `"CiscoUmbrella"` |
| `CortexXDR` | `"CortexXDR"` |
| `Cybereason` | `"Cybereason"` |
| `Defender` | `"Defender"` |
| `Falcon` | `"Falcon"` |
| `FortiGate` | `"FortiGate"` |
| `PaloAlto` | `"PaloAlto"` |
| `PaloAltoFirewall` | `"PaloAltoFirewall"` |
| `SentinelOne` | `"SentinelOne"` |
| `Sophos` | `"Sophos"` |
| `TrendMicro` | `"TrendMicro"` |
| `Zscaler` | `"Zscaler"` |

### AdapterTopics.cs
`Shared/Cymulate.Integration.Adapters.Shared/Glossary/AdapterTopics.cs`

**Indicator topics:**
| Constant | Value |
|---|---|
| `Indicator.CollectorHandshake` | `"collector.handshake"` |
| `Indicator.Ioc` | `"ioc"` |
| `Indicator.DeleteIoc` | `"delete-ioc"` |
| `Indicator.GetIoc` | `"get-ioc"` |
| `Indicator.Ioa` | `"ioa"` |
| `Indicator.DeleteIoa` | `"delete-ioa"` |
| `Indicator.GetIoa` | `"get-ioa"` |

**Collector topics:**
| Constant | Value |
|---|---|
| `Collector.AssetsFlow` | `"CollectAssets"` |
| `Collector.FindingsFlow` | `"CollectFindings"` |

### CollectorZipNames.cs
`Shared/Cymulate.Integration.Adapters.Shared/Glossary/CollectorZipNames.cs`

| Constant | Value |
|---|---|
| `CloudGuardCollector` | `"CloudGuardCollector"` |
| `CortexXdrCollector` | `"CortexXdrCollector"` |
| `DefenderVmCollector` | `"DefenderVmCollector"` |
| `DefenderForCloudCollector` | `"DefenderForCloudCollector"` |
| `DummyCollector` | `"DummyCollector"` |
| `FalconCollector` | `"FalconCollector"` |
| `GuardicoreCollector` | `"GuardicoreCollector"` |
| `InsightVmCollector` | `"InsightVmCollector"` |
| `InsightVmCloudCollector` | `"InsightVmCloudCollector"` |
| `QualysCollector` | `"QualysCollector"` |
| `SentinelOneCollector` | `"SentinelOneCollector"` |
| `MicrosoftEntraIdCollector` | `"MicrosoftEntraIdCollector"` |
| `TaegisCollector` | `"TaegisCollector"` |
| `TenableIoCollector` | `"TenableIoCollector"` |
| `TenableScCollector` | `"TenableScCollector"` |
| `IsbLoadTestCollector` | `"IsbLoadTestCollector"` |
| `YamlCollector` | `"YamlCollector"` |

---

## 2. Collector Identity Files

### FalconCollector
File: `Collectors/FalconCollector/Processing/Configuration/FalconIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"crowdstrike-falcon-collector"` |
| Name | `"CrowdStrike Falcon Collector"` |
| PlatformType | `PlatformType.CrowdStrikeFalcon` |
| SupportedTopics | `CollectAssets`, `CollectFindings` |
| Version | `1.0.0` |

### CortexXdrCollector
File: `Collectors/CortexXdrCollector/Processing/Configuration/CortexXdrIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"cortex-xdr-collector"` |
| Name | `"Cortex XDR Collector"` |
| PlatformType | `PlatformType.CortexXDR` |
| SupportedTopics | `CollectAssets`, `CollectFindings` |
| Version | `1.0.0` |

### DefenderForCloudCollector
File: `Collectors/DefenderForCloudCollector/Processing/Configuration/DefenderForCloudIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"defender-for-cloud-collector"` |
| Name | `"Defender for Cloud Collector"` |
| PlatformType | `PlatformType.Custom` |
| SupportedTopics | `CollectAssets`, `CollectFindings`, `Recommendations` (custom), `AttackPaths` (custom) |
| Version | `1.0.0` |

Note: Uses `PlatformType.Custom` despite having a dedicated `DefenderForCloudCollector` name in `CollectorNames`. The two custom flow names (`RecommendationsFlow`, `AttackPathsFlow`) are defined in `DefenderForCloudFlowContract`, not in `AdapterTopics`.

### DefenderVmCollector
File: `Collectors/DefenderVmCollector/Processing/Configuration/DefenderVmIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"defender-vm-collector"` |
| Name | `"Defender VM Collector"` |
| PlatformType | `PlatformType.DefenderVm` |
| SupportedTopics | `CollectAssets`, `CollectFindings` |
| Version | `1.0.0` |

### CloudGuardCollector
File: `Collectors/CloudGuardCollector/Processing/Configuration/CloudGuardIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"cloudguard-collector"` |
| Name | `"CloudGuard Collector"` |
| PlatformType | `PlatformType.CloudGuard` |
| SupportedTopics | `CollectAssets`, `CollectFindings` |
| Version | `1.0.0` |

### GuardicoreCollector
File: `Collectors/GuardicoreCollector/Processing/Configuration/GuardicoreIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"guardicore-collector"` |
| Name | `"Guardicore Collector"` |
| PlatformType | `PlatformType.Guardicore` |
| SupportedTopics | `CollectAssets` only |
| Version | `1.0.0` |

### InsightVmCollector
File: `Collectors/InsightVmCollector/Processing/Configuration/InsightVmIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"rapid7-insightvm-collector"` |
| Name | `"Rapid7 InsightVM Collector"` |
| PlatformType | `PlatformType.Rapid7InsightVM` |
| SupportedTopics | `CollectAssets`, `CollectFindings` |
| Version | `1.1.0` |

### InsightVmCloudCollector
File: `Collectors/InsightVmCloudCollector/Processing/Configuration/InsightVmCloudIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"rapid7-insightvm-cloud-collector"` |
| Name | `"Rapid7 InsightVM Cloud Collector"` |
| PlatformType | `PlatformType.Rapid7InsightVMCloud` |
| SupportedTopics | `CollectAssets`, `CollectFindings` |
| Version | `1.1.0` |

### MicrosoftEntraIdCollector
File: `Collectors/MicrosoftEntraIdCollector/Processing/Configuration/MicrosoftEntraIdIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"microsoft-entra-id-collector"` |
| Name | `"Microsoft Entra ID Collector"` |
| PlatformType | `PlatformType.MicrosoftEntraId` |
| SupportedTopics | `CollectAssets` only |
| Version | `1.0.0` |

### QualysCollector
File: `Collectors/QualysCollector/Processing/Configuration/QualysIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"qualys-collector"` |
| Name | `"Qualys Collector"` |
| PlatformType | `PlatformType.Qualys` |
| SupportedTopics | `CollectFindings` only |
| Version | `1.0.0` |

### SentinelOneCollector
File: `Collectors/SentinelOneCollector/Processing/Configuration/SentinelOneIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"sentinelone-collector"` |
| Name | `"SentinelOne Collector"` |
| PlatformType | `PlatformType.SentinelOneSingularityXDR` |
| SupportedTopics | `CollectAssets` only |
| Version | `1.0.0` |

### ServiceNowCmdbCollector
File: `Collectors/ServiceNowCmdbCollector/Processing/Configuration/ServiceNowCmdbIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"servicenow-cmdb-collector"` |
| Name | `"ServiceNow CMDB Collector"` |
| PlatformType | `PlatformType.ServiceNowCmdb` |
| SupportedTopics | `CollectAssets` only |
| Version | `1.0.0` |

### TaegisCollector
File: `Collectors/TaegisCollector/Processing/Configuration/TaegisIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"taegis-collector"` |
| Name | `"Taegis Collector"` |
| PlatformType | `PlatformType.Custom` |
| SupportedTopics | `CollectAssets` only |
| Version | `1.0.0` |

Note: `PlatformType.Custom` despite `CollectorNames.TaegisCollector = "Secureworks Taegis"` and a dedicated `CollectorZipNames.TaegisCollector`. No dedicated `PlatformType` enum value exists for Taegis.

### TenableIoCollector
File: `Collectors/TenableIoCollector/Processing/Configuration/TenableIoIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"tenable-io-collector"` |
| Name | `"Tenable.io Collector"` |
| PlatformType | `PlatformType.TenableIo` |
| SupportedTopics | `CollectFindings` only |
| Version | `1.0.0` |

### TenableScCollector
File: `Collectors/TenableScCollector/Processing/Configuration/TenableScIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"tenable-sc-collector"` |
| Name | `"Tenable.sc Collector"` |
| PlatformType | `PlatformType.TenableSc` |
| SupportedTopics | `CollectFindings` only |
| Version | `1.0.0` |

### IsbLoadTestCollector
File: `Collectors/IsbLoadTestCollector/Cymulate.Integration.Adapters.Collectors.IsbLoadTestCollector/Processing/Configuration/IsbLoadTestIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"isb-load-test-collector"` |
| Name | `"ISB Load Test Collector"` |
| PlatformType | `PlatformType.Custom` |
| SupportedTopics | `CollectFindings` only |
| Version | `1.0.0` |

### DummyCollector
File: `Collectors/DummyCollector/DummyCollector.cs` (metadata inline, no separate Identification file)

| Field | Value |
|---|---|
| AdapterId | `"dummy-collector"` |
| Name | `"Dummy Collector"` |
| PlatformType | `PlatformType.Custom` |
| SupportedTopics | `CollectAssets`, `CollectFindings` |
| Version | `1.0.0` |

### YamlCollector
File: `Collectors/YamlCollector/YamlCollectorAdapter.cs` (metadata inline, no separate Identification file)

| Field | Value |
|---|---|
| AdapterId | `"yaml-collector"` |
| Name | `"YAML Execution Engine Collector"` |
| PlatformType | `PlatformType.YamlEngine` |
| SupportedTopics | `"assets"`, `"findings"` (raw strings, not `AdapterTopics` constants) |
| Version | `1.0.0` |

Note: YamlCollector uses raw strings `"assets"` and `"findings"` instead of `AdapterTopics.Collector.AssetsFlow` (`"CollectAssets"`) and `AdapterTopics.Collector.FindingsFlow` (`"CollectFindings"`). This is the only collector that deviates from the canonical topic constants.

---

## 3. Indicator Identity Files

### CarbonBlack
File: `Indicators/Cymulate.Integration.Adapters.Indicators.CarbonBlack/Configuration/CarbonBlackIndicatorIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"carbon-black-adapter"` |
| Name | `"Carbon Black Cloud Threat Feed Adapter"` |
| PlatformType | `PlatformType.CarbonBlack` |
| SupportedTopics | `ioc`, `delete-ioc`, `get-ioc` |
| Version | `1.0.0` |

### CiscoSecure
File: `Indicators/Cymulate.Integration.Adapters.Indicators.CiscoSecure/Configuration/CiscoSecureIndicatorIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"cisco-secure-adapter"` |
| Name | `"Cisco Secure Endpoint (AMP) Hash IOC Adapter"` |
| PlatformType | `PlatformType.CiscoSecureX` |
| SupportedTopics | `ioc`, `delete-ioc`, `get-ioc` (via `TopicHandlerFactory.GetSupportedTopics()`) |
| Version | `1.0.0` |

Note: `IndicatorNames.CiscoSecure = "CiscoSecure"` but `PlatformType` is `CiscoSecureX` (includes the `X` suffix).

### CiscoUmbrella
File: `Indicators/Cymulate.Integration.Adapters.Indicators.CiscoUmbrella/Configuration/CiscoUmbrellaIndicatorIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"cisco-umbrella-adapter"` |
| Name | `"Cisco Umbrella Domain/URL IOC Adapter"` |
| PlatformType | `PlatformType.CiscoUmbrella` |
| SupportedTopics | `ioc`, `delete-ioc`, `get-ioc` (via `TopicHandlerFactory.GetSupportedTopics()`) |
| Version | `2.0.0` |

### CortexXDR (Indicator)
File: `Indicators/Cymulate.Integration.Adapters.Indicators.CortexXDR/Configuration/CortexXDRIndicatorIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"cortex-xdr-adapter"` |
| Name | `"Cortex XDR Hash IOC Adapter"` |
| PlatformType | `PlatformType.CortexXDR` |
| SupportedTopics | `ioc`, `delete-ioc`, `get-ioc` |
| Version | `2.0.0` |

### Cybereason
File: `Indicators/Cymulate.Integration.Adapters.Indicators.Cybereason/Configuration/CybereasonIndicatorIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"cybereason-adapter"` |
| Name | `"Cybereason IOC Adapter"` |
| PlatformType | `PlatformType.Cybereason` |
| SupportedTopics | `ioc`, `delete-ioc`, `get-ioc` |
| Version | `2.0.0` |

### Defender (Indicator)
File: `Indicators/Cymulate.Integration.Adapters.Indicators.Defender/Configuration/DefenderIndicatorIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"microsoft-defender-adapter"` |
| Name | `"Microsoft Defender for Endpoint IOC Adapter"` |
| PlatformType | `PlatformType.MicrosoftDefender` |
| SupportedTopics | `ioc`, `delete-ioc`, `get-ioc` |
| Version | `2.0.0` |

Note: `IndicatorNames.Defender = "Defender"` but `PlatformType` is `MicrosoftDefender`. The DefenderVm collector uses `PlatformType.DefenderVm` — these are distinct enum values for distinct products (Defender for Endpoint vs. Defender Vulnerability Management).

### Falcon (Indicator)
File: `Indicators/Cymulate.Integration.Adapters.Indicators.Falcon/Configuration/FalconIndicatorIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"crowdstrike-falcon-adapter"` |
| Name | `"CrowdStrike Falcon Adapter"` |
| PlatformType | `PlatformType.CrowdStrikeFalcon` |
| SupportedTopics | `collector.handshake`, `ioc`, `delete-ioc`, `get-ioc`, `ioa`, `delete-ioa`, `get-ioa` |
| Version | `2.0.0` |

### FortiGate
File: `Indicators/Cymulate.Integration.Adapters.Indicators.FortiGate/Configuration/FortiGateIndicatorIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"fortigate-adapter"` |
| Name | `"FortiGate IOC Adapter"` |
| PlatformType | `PlatformType.FortiGate` |
| SupportedTopics | `collector.handshake`, `ioc`, `delete-ioc`, `get-ioc` |
| Version | `2.0.0` |

### PaloAlto
File: `Indicators/Cymulate.Integration.Adapters.Indicators.PaloAlto/Configuration/PaloAltoIndicatorIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"paloalto-adapter"` |
| Name | `"Palo Alto Firewall IOC Adapter"` |
| PlatformType | `PlatformType.PaloAltoNetworks` |
| SupportedTopics | `ioc`, `delete-ioc`, `get-ioc` |
| Version | `2.0.0` |

Note: `IndicatorNames.PaloAlto = "PaloAlto"` but `PlatformType` is `PaloAltoNetworks`.

### PaloAltoFirewall
File: `Indicators/Cymulate.Integration.Adapters.Indicators.PaloAltoFirewall/Configuration/PaloAltoFirewallIndicatorIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"paloalto-firewall-adapter"` |
| Name | `"Palo Alto Firewall URL IOC Adapter (REST API)"` |
| PlatformType | `PlatformType.PaloAltoFirewall` |
| SupportedTopics | `ioc`, `delete-ioc`, `get-ioc` |
| Version | `2.0.0` |

### SentinelOne (Indicator)
File: `Indicators/Cymulate.Integration.Adapters.Indicators.SentinelOne/Configuration/SentinelOneIndicatorIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"sentinelone-adapter"` |
| Name | `"SentinelOne Adapter (IOC Blocklist + IOA STAR Rules)"` |
| PlatformType | `PlatformType.SentinelOne` |
| SupportedTopics | `ioc`, `delete-ioc`, `get-ioc`, `ioa`, `delete-ioa`, `get-ioa` |
| Version | `4.0.0` |

### Sophos
File: `Indicators/Cymulate.Integration.Adapters.Indicators.Sophos/Configuration/SophosIndicatorIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"sophos-adapter"` |
| Name | `"Sophos Endpoint Adapter"` |
| PlatformType | `PlatformType.Sophos` |
| SupportedTopics | `ioc`, `delete-ioc`, `get-ioc` |
| Version | `2.0.0` |

### TrendMicro
File: `Indicators/Cymulate.Integration.Adapters.Indicators.TrendMicro/Configuration/TrendMicroIndicatorIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"trend-micro-adapter"` |
| Name | `"Trend Micro Vision One IOC Adapter"` |
| PlatformType | `PlatformType.TrendMicro` |
| SupportedTopics | `ioc`, `delete-ioc`, `get-ioc` |
| Version | `3.0.0` |

### Zscaler
File: `Indicators/Cymulate.Integration.Adapters.Indicators.Zscaler/Configuration/ZscalerIndicatorIdentification.cs`

| Field | Value |
|---|---|
| AdapterId | `"zscaler-adapter"` |
| Name | `"Zscaler ZIA URL IOC Adapter"` |
| PlatformType | `PlatformType.Zscaler` |
| SupportedTopics | `ioc`, `delete-ioc`, `get-ioc` |
| Version | `2.0.0` |

---

## 4. Master Table

| Adapter | Category | AdapterId | Name | PlatformType | SupportedTopics |
|---|---|---|---|---|---|
| FalconCollector | Collector | `crowdstrike-falcon-collector` | CrowdStrike Falcon Collector | `CrowdStrikeFalcon` | CollectAssets, CollectFindings |
| CortexXdrCollector | Collector | `cortex-xdr-collector` | Cortex XDR Collector | `CortexXDR` | CollectAssets, CollectFindings |
| DefenderForCloudCollector | Collector | `defender-for-cloud-collector` | Defender for Cloud Collector | `Custom` | CollectAssets, CollectFindings, Recommendations, AttackPaths |
| DefenderVmCollector | Collector | `defender-vm-collector` | Defender VM Collector | `DefenderVm` | CollectAssets, CollectFindings |
| CloudGuardCollector | Collector | `cloudguard-collector` | CloudGuard Collector | `CloudGuard` | CollectAssets, CollectFindings |
| GuardicoreCollector | Collector | `guardicore-collector` | Guardicore Collector | `Guardicore` | CollectAssets |
| InsightVmCollector | Collector | `rapid7-insightvm-collector` | Rapid7 InsightVM Collector | `Rapid7InsightVM` | CollectAssets, CollectFindings |
| InsightVmCloudCollector | Collector | `rapid7-insightvm-cloud-collector` | Rapid7 InsightVM Cloud Collector | `Rapid7InsightVMCloud` | CollectAssets, CollectFindings |
| MicrosoftEntraIdCollector | Collector | `microsoft-entra-id-collector` | Microsoft Entra ID Collector | `MicrosoftEntraId` | CollectAssets |
| QualysCollector | Collector | `qualys-collector` | Qualys Collector | `Qualys` | CollectFindings |
| SentinelOneCollector | Collector | `sentinelone-collector` | SentinelOne Collector | `SentinelOneSingularityXDR` | CollectAssets |
| ServiceNowCmdbCollector | Collector | `servicenow-cmdb-collector` | ServiceNow CMDB Collector | `ServiceNowCmdb` | CollectAssets |
| TaegisCollector | Collector | `taegis-collector` | Taegis Collector | `Custom` | CollectAssets |
| TenableIoCollector | Collector | `tenable-io-collector` | Tenable.io Collector | `TenableIo` | CollectFindings |
| TenableScCollector | Collector | `tenable-sc-collector` | Tenable.sc Collector | `TenableSc` | CollectFindings |
| IsbLoadTestCollector | Collector | `isb-load-test-collector` | ISB Load Test Collector | `Custom` | CollectFindings |
| DummyCollector | Collector | `dummy-collector` | Dummy Collector | `Custom` | CollectAssets, CollectFindings |
| YamlCollector | Collector | `yaml-collector` | YAML Execution Engine Collector | `YamlEngine` | "assets", "findings" |
| CarbonBlack | Indicator | `carbon-black-adapter` | Carbon Black Cloud Threat Feed Adapter | `CarbonBlack` | ioc, delete-ioc, get-ioc |
| CiscoSecure | Indicator | `cisco-secure-adapter` | Cisco Secure Endpoint (AMP) Hash IOC Adapter | `CiscoSecureX` | ioc, delete-ioc, get-ioc |
| CiscoUmbrella | Indicator | `cisco-umbrella-adapter` | Cisco Umbrella Domain/URL IOC Adapter | `CiscoUmbrella` | ioc, delete-ioc, get-ioc |
| CortexXDR | Indicator | `cortex-xdr-adapter` | Cortex XDR Hash IOC Adapter | `CortexXDR` | ioc, delete-ioc, get-ioc |
| Cybereason | Indicator | `cybereason-adapter` | Cybereason IOC Adapter | `Cybereason` | ioc, delete-ioc, get-ioc |
| Defender | Indicator | `microsoft-defender-adapter` | Microsoft Defender for Endpoint IOC Adapter | `MicrosoftDefender` | ioc, delete-ioc, get-ioc |
| Falcon | Indicator | `crowdstrike-falcon-adapter` | CrowdStrike Falcon Adapter | `CrowdStrikeFalcon` | collector.handshake, ioc, delete-ioc, get-ioc, ioa, delete-ioa, get-ioa |
| FortiGate | Indicator | `fortigate-adapter` | FortiGate IOC Adapter | `FortiGate` | collector.handshake, ioc, delete-ioc, get-ioc |
| PaloAlto | Indicator | `paloalto-adapter` | Palo Alto Firewall IOC Adapter | `PaloAltoNetworks` | ioc, delete-ioc, get-ioc |
| PaloAltoFirewall | Indicator | `paloalto-firewall-adapter` | Palo Alto Firewall URL IOC Adapter (REST API) | `PaloAltoFirewall` | ioc, delete-ioc, get-ioc |
| SentinelOne | Indicator | `sentinelone-adapter` | SentinelOne Adapter (IOC Blocklist + IOA STAR Rules) | `SentinelOne` | ioc, delete-ioc, get-ioc, ioa, delete-ioa, get-ioa |
| Sophos | Indicator | `sophos-adapter` | Sophos Endpoint Adapter | `Sophos` | ioc, delete-ioc, get-ioc |
| TrendMicro | Indicator | `trend-micro-adapter` | Trend Micro Vision One IOC Adapter | `TrendMicro` | ioc, delete-ioc, get-ioc |
| Zscaler | Indicator | `zscaler-adapter` | Zscaler ZIA URL IOC Adapter | `Zscaler` | ioc, delete-ioc, get-ioc |

---

## 5. CRITICAL: Same PlatformType, Both Categories

These PlatformType values are shared by a Collector and an Indicator. Disambiguation requires (PlatformType, Category) as the composite identity key.

| PlatformType | Collector | Indicator |
|---|---|---|
| `CrowdStrikeFalcon` | FalconCollector (`crowdstrike-falcon-collector`) | Falcon indicator (`crowdstrike-falcon-adapter`) |
| `CortexXDR` | CortexXdrCollector (`cortex-xdr-collector`) | CortexXDR indicator (`cortex-xdr-adapter`) |

Both of these are real cross-category collisions where the same enum value is used by adapters of different categories doing completely different jobs (collection vs. IOC push).

---

## 6. CRITICAL: Same Brand, Different PlatformType by Category

These adapters target the same vendor but use **different** PlatformType enum values in the collector vs. the indicator.

| Brand | Collector PlatformType | Indicator PlatformType | Notes |
|---|---|---|---|
| SentinelOne | `SentinelOneSingularityXDR` (SentinelOneCollector) | `SentinelOne` (SentinelOne indicator) | Collector uses the branded XDR product name; indicator uses the bare brand name |
| Palo Alto | N/A (no collector) | `PaloAltoNetworks` (PaloAlto indicator), `PaloAltoFirewall` (PaloAltoFirewall indicator) | Two distinct indicator adapters for the same vendor; one uses XML API against Firewall/Panorama, the other uses REST API for URL categories |
| Microsoft Defender | `DefenderVm` (DefenderVmCollector), `Custom` (DefenderForCloudCollector) | `MicrosoftDefender` (Defender indicator) | Three distinct Microsoft security products: DefenderVm = Vulnerability Management, DefenderForCloud = cloud security posture, MicrosoftDefender = Defender for Endpoint IOCs |
| Cisco | N/A (no collector) | `CiscoSecureX` (CiscoSecure indicator), `CiscoUmbrella` (CiscoUmbrella indicator) | Two Cisco products with distinct PlatformTypes; `CiscoSecureX` uses the older product naming (SecureX/AMP) |

---

## 7. PlatformType Usage Map

| PlatformType | Adapter(s) | Category |
|---|---|---|
| `CrowdStrikeFalcon` | FalconCollector, Falcon indicator | **Both** |
| `CortexXDR` | CortexXdrCollector, CortexXDR indicator | **Both** |
| `DefenderVm` | DefenderVmCollector | Collector only |
| `CloudGuard` | CloudGuardCollector | Collector only |
| `Guardicore` | GuardicoreCollector | Collector only |
| `Rapid7InsightVM` | InsightVmCollector | Collector only |
| `Rapid7InsightVMCloud` | InsightVmCloudCollector | Collector only |
| `MicrosoftEntraId` | MicrosoftEntraIdCollector | Collector only |
| `Qualys` | QualysCollector | Collector only |
| `SentinelOneSingularityXDR` | SentinelOneCollector | Collector only |
| `ServiceNowCmdb` | ServiceNowCmdbCollector | Collector only |
| `TenableIo` | TenableIoCollector | Collector only |
| `TenableSc` | TenableScCollector | Collector only |
| `YamlEngine` | YamlCollector | Collector only |
| `Custom` | DefenderForCloudCollector, TaegisCollector, IsbLoadTestCollector, DummyCollector | Collector only (4 adapters share this value) |
| `CarbonBlack` | CarbonBlack indicator | Indicator only |
| `CiscoSecureX` | CiscoSecure indicator | Indicator only |
| `CiscoUmbrella` | CiscoUmbrella indicator | Indicator only |
| `Cybereason` | Cybereason indicator | Indicator only |
| `MicrosoftDefender` | Defender indicator | Indicator only |
| `FortiGate` | FortiGate indicator | Indicator only |
| `PaloAltoNetworks` | PaloAlto indicator | Indicator only |
| `PaloAltoFirewall` | PaloAltoFirewall indicator | Indicator only |
| `SentinelOne` | SentinelOne indicator | Indicator only |
| `Sophos` | Sophos indicator | Indicator only |
| `TrendMicro` | TrendMicro indicator | Indicator only |
| `Zscaler` | Zscaler indicator | Indicator only |

---

## 8. Notable Mismatches (Name/AdapterId vs. PlatformType)

| Adapter | AdapterId / Name | PlatformType | Mismatch |
|---|---|---|---|
| CiscoSecure indicator | `cisco-secure-adapter` / "Cisco Secure Endpoint (AMP)" | `CiscoSecureX` | Name says "Cisco Secure" / "AMP"; PlatformType has the older `SecureX` suffix |
| PaloAlto indicator | `paloalto-adapter` / "Palo Alto Firewall IOC Adapter" | `PaloAltoNetworks` | Name says "PaloAlto"; PlatformType says `PaloAltoNetworks` (longer form) |
| DefenderForCloud collector | `defender-for-cloud-collector` / "Defender for Cloud Collector" | `Custom` | Specific product but falls back to `Custom`; no `DefenderForCloud` enum value exists |
| TaegisCollector | `taegis-collector` / "Taegis Collector" | `Custom` | Specific product (Secureworks Taegis) but no dedicated enum value; falls back to `Custom` |
| YamlCollector | `yaml-collector` / "YAML Execution Engine Collector" | `YamlEngine` | Uses raw strings `"assets"`/`"findings"` instead of `AdapterTopics.Collector.AssetsFlow`/`FindingsFlow` constants |
| IsbLoadTestCollector | `isb-load-test-collector` / "ISB Load Test Collector" | `Custom` | Synthetic/test adapter sharing `Custom` with real adapters |
| DummyCollector | `dummy-collector` / "Dummy Collector" | `Custom` | Synthetic adapter sharing `Custom` with real adapters |

---

## 9. Adapters in CollectorNames with No Implementation Found

The following constants exist in `CollectorNames.cs` but have no corresponding `*Identification.cs` or collector folder:

| Constant | Value | Notes |
|---|---|---|
| `DefenderEndpointCollector` | `"Defender Endpoint"` | No collector folder under `Collectors/` |
| `ActiveDirectoryCollector` | `"Active Directory"` | No collector folder under `Collectors/` |
| `CymulateCollector` | `"Cymulate"` | No collector folder under `Collectors/` |

These may be external adapters hosted elsewhere or deprecated/planned entries.
