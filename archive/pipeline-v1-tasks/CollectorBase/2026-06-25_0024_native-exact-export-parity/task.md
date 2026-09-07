# Task — Native-exact export parity + complete Defender profile

Make CollectorExecutor's emitted records match the **byte-shape each native collector produces**, and
finish the Defender profile to native collection scope. Two pieces:

## 1. Export parity (engine + Tenable + Defender)
Native collectors emit the **full raw vendor record** (passthrough), not a field subset:
- **Tenable** (`TenableIo{Assets,Findings}ChunkProcessor`): verbatim full record — no wrapper, no
  `sourceType`, no correlation keys ("dumb passthrough; mapping is the parser's job").
- **Defender** (`DefenderVmRecordFormatter`): machines/software → `{"type":"machine"|"software","data":{<record>}}`;
  vulns/changes/recommendations/recVulns → `{"sourceType":"inventory"|"delta"|"recommendationCatalog"|"recommendationScopedVulnerability", <record flattened>}`; recVulns also prepends `recommendationReference`.
- **Ours today** (`ResponseMapper.Map`): `{sourceType, <listed fields subset>, <correlationKeys>}` — a subset.

Add to the engine: a **verbatim full-record passthrough** emit mode + a per-emitting-step **envelope
selector** (`verbatim` | `typed_wrapper` | `source_type_prefix`) with optional templated metadata fields.
Keep the existing mapped-subset mode as the default (backward-compat). Apply native-exact shapes to the
Tenable and Defender profiles, each verified against native source.

## 2. Complete the Defender profile (no new pagination strategy)
`next_url` already replicates native OData `@odata.nextLink` + `$skip`/`$top` fallback. Complete
`integrations/defender-vm.yaml` to native scope: `$filter=lastSeen ge <base_date>` + base-date logic,
the vuln-changes stage (14-day lookback), and the correct native endpoints/stages.

## Scope
Engine mechanism (vendor-agnostic) + Tenable (verbatim) + Defender (wrapper/prefix). **Defer**
cortex-xdr, crowdstrike-falcon, qualys (keep mapped-subset; record as not-yet-native-exact).
