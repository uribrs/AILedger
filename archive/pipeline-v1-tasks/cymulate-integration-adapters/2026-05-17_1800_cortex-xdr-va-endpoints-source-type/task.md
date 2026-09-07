# Task: Cortex XDR va_endpoints stage + sourceType discriminator

Add `va_endpoints` as a third XQL stage in the Cortex XDR findings flow and stamp a
`sourceType` discriminator on every published row across all three streams so
upstream can route by source.

## Current state (post `cortex-xdr-ingress-streaming`)

The Cortex XDR findings flow runs two upstream-hydration streams:

- `va_cves` rows over XQL → `findings_*.json`
- endpoint rows over `/endpoints/get_endpoint` REST → `assets_*.json`

The on-prem reference additionally uses XQL's `va_endpoints` dataset to provide
an authoritative `endpoint_id`-keyed CVE→endpoint mapping. The cloud adapter
currently discards that source, forcing upstream hydrators to reverse-derive the
mapping from `va_cves.affected_hosts` → `endpoint_name`. That join is fuzzy and
loses:

- endpoints not present in any `va_cves.affected_hosts` list
- `va_endpoints` CVEs not present in `va_cves`

## Target state

1. `CortexXdrFindingsFlow` runs three XQL/REST stages in order:
   `FindingsCves` → `FindingsEndpoints` → `Assets`. Each stage has an independent
   per-stage cursor.
2. `va_endpoints` rows are published to `findings_*.json` (CVE-domain rows live
   with findings; endpoint REST rows continue to live with `assets_*.json`).
3. Every published record carries a `sourceType` field as its first JSON
   property:
   - `"va_cves"` — rows from the va_cves XQL stage
   - `"va_endpoints"` — rows from the new va_endpoints XQL stage
   - `"endpoint"` — rows from the endpoint REST walk
4. `va_endpoints` dataset availability is probed before the stage runs.
   Missing/empty (Host Insights add-on absent) → log info + skip the stage. The
   va_cves and Assets stages continue normally.
5. Checkpoint shape bumps to `checkpointVersion = 3` with a new
   `NextVaEndpointIndex` cursor. Resume from a v2 checkpoint is rejected as
   legacy with a clear log message.

## Out of scope

- Any change to `DataPipeline/Ingress/`, `DataPipeline/Egress/`, `DataPipeline/Json/`.
- `Session/`, `Recovery/`, `Orchestration/`.
- SDK contract (`IAdapterDataPublisher`).
- Upstream consumer code — this task ships the producer side only.
- On-prem `expandCveIds` normalization — the adapter passes `cves` bytes
  through as-is; upstream owns parsing.
- 50k cap warning log (separate hygiene item).
