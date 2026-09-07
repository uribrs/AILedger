# Assumptions

- A1 [OPEN] — The live `cybiIntegrationSetting.csv` is the authoritative source of inbound `name` permutations; `integrationsData.js` (seed) may be stale/incomplete (e.g. missing "InsightVM Cloud"). Reconcile both; treat live as truth where they differ. (Validate in S3.)
- A2 [OPEN] — The bus message `vendor` is populated solely from the integration `name` field end-to-end; nothing else can set it. (Confirmed for the InsightVM Cloud case; validate no other producer path in S1.)
- A3 [OPEN] — Cisco duplicate EnumMember (`CiscoSecureX` and `CiscoUmbrella` both `"CiscoSecureEndpoint"`) is a genuine defect; correct disambiguation = give CiscoUmbrella its own EnumMember and confirm what the catalog `name`/topic sends for each. (Decide in S5; may require adapters-repo/SDK alignment.)
- A4 [OPEN] — Unknown/unmapped vendor should resolve to a non-match (fail closed), not to `Custom`. The Taegis collector legitimately uses `Custom`, so the resolver must distinguish "explicitly Taegis" from "unmapped". (Decide in S5.)
- A5 [OPEN] — `Defender Endpoint` and `FortiGate` fallback aliases correspond to real inbound names; reconcile into the unified map or confirm they are dead. (Validate in S3/S4.)
- A6 [OPEN] — The same `PlatformType` enum definition is shared by both repos (ISB has a vendored copy under `Sdk/`; adapters consume the `Cymulate.Integration.Sdk` NuGet). Confirm they are in sync for the values in scope. (Validate in S1/S2.)
- A7 [OPEN] — ISB has a test project using xUnit (RegisteredAdapterPlatformResolverTests / TriggerFlowMapperTests already exist) where the contract test can live. (Validate in S1.)
- A8 [OPEN] — A machine-readable catalog of `name`+topic is reachable at test time for the contract test (embedded fixture from the CSV/seed, or a checked-in data file). Source/shape to be decided in S8.
