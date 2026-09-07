# Cross-reference: adapter identities (adapters repo) vs PlatformType contract (ISB)

## Contract sync — CLEAN
- ISB vendored SDK source (`Sdk/Cymulate.Integration.Sdk`) is at **3.1.8**; adapters compile against `Cymulate.Integration.Sdk` **3.1.8**. Same package, same version.
- All 28 `PlatformType` values present in both, identical names/order. No enum drift between repos.
- Every registered adapter declares a `PlatformType` that exists in the shared enum. No adapter references a value ISB lacks.

## (PlatformType, Category) identity — unique for all REAL adapters except one collision
Walking the adapter inventory against the enum, `(PlatformType, Category)` is a unique key for every real adapter EXCEPT:

### Collision (breaks uniqueness) — the only real defect
`(Custom, Collector)` is shared by 4 collectors:
| Adapter | Real? | Catalog name |
|---|---|---|
| DefenderForCloudCollector | YES | "Defender for Cloud" / seed "Microsoft Defender for Cloud" |
| TaegisCollector | YES | "Secureworks Taegis" |
| IsbLoadTestCollector | test | — |
| DummyCollector | test | — |
→ The two REAL ones (DefenderForCloud, Taegis) need dedicated `PlatformType` values so `(PlatformType, Category)` is unique. Test ones stay `Custom`.

### Cross-category shared PlatformType (expected — category disambiguates, NOT a defect)
- `CrowdStrikeFalcon`: FalconCollector + Falcon indicator.
- `CortexXDR`: CortexXdrCollector + CortexXDR indicator.
These are fine: `(PlatformType, Category)` still unique because category differs.

### Same brand, different PlatformType (expected — distinct enum values)
- SentinelOne: collector `SentinelOneSingularityXDR` (22), indicator `SentinelOne` (3).
- Defender family: `DefenderVm` (26) collector, `MicrosoftDefender` (1) indicator, `Custom`→(to be `MicrosoftDefenderForCloud`) collector.

## Enum cleanliness notes (non-blocking under forward name→PlatformType resolution)
- **Cisco duplicate EnumMember**: `CiscoSecureX` (6) and `CiscoUmbrella` (13) both `[EnumMember "CiscoSecureEndpoint"]`. Distinct enum values; only the reverse (PlatformType→string) serialization is ambiguous. Forward resolution by name is unaffected.
- **Orphan enum value**: `CheckPointHarmony` (12) exists in the shared enum but NO adapter in the repo declares it (seed has "Check Point Harmony Endpoint"). Either an unbuilt/external indicator or dead. Harmless — an enum value with no adapter is simply never registered.
- Glossary `CollectorNames` has 3 constants with no implementation: `DefenderEndpointCollector`, `ActiveDirectoryCollector`, `CymulateCollector`. Not adapters; ignore.

## Starting point (what this implies for the build)
1. **Shared enum: add 2 values** — `SecureworksTaegis`, `MicrosoftDefenderForCloud` (next free ids 27, 28) in the SDK enum (ISB source) — and the adapters repo must consume an SDK with them (version bump).
2. **Adapters repo (on a branch): repoint 2 collectors** — `TaegisIdentification` → `SecureworksTaegis`, `DefenderForCloudIdentification` → `MicrosoftDefenderForCloud`.
3. After (1)+(2), `(PlatformType, Category)` is a total unique key for every real adapter; `Custom` is reserved for test/non-routed → resolver fails closed on it.
4. Cisco enum cleanup is optional/deferred (forward resolution unaffected).

## Open coupling to resolve before coding
- The shared enum lives in ISB's `Sdk/` and is published as the `Cymulate.Integration.Sdk` NuGet the adapters consume. Adding enum values means: edit ISB SDK source → publish a new SDK version → bump the adapters' package reference → repoint the 2 collectors. Both repos branch first. This is the cross-repo sequence the implementation must follow.
