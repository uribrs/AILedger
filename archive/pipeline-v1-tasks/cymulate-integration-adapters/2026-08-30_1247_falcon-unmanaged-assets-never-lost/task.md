# Falcon correlated findings: restore "no assets are ever lost"

## What is wrong

`CollectFindings` silently discards every Discover asset from which no AID can be derived.
`FalconDiscoverHostScroller.cs:82-86` `continue`s past such a host, so it is fetched from the vendor,
parsed, and dropped in the client.

Measured live on the lab tenant (`api.us-2.crowdstrike.com`, CID `8884df8d8f704f43b23dad2e90572984`,
2026-08-30): Discover reports 298 assets with no filter, 49 with `entity_type:'managed'`. The run
staged exactly 49. The other 249 were lost. There is no `entity_type` filter in the findings lane —
the narrowing is purely the AID requirement.

The standalone `CollectAssets` lane keeps all of them. Only the findings lane drops them, and for
`CollectFindings` the findings record IS the asset spine: the July 2026 redesign deliberately dropped
the separate `assets_*.json` lane from that flow, so a host absent from the findings output is an
asset absent from the platform.

## Second defect, independent of the first

`FalconDiscoverHostScroller.cs:100` returns `After = null` when the POST-FILTER host list is empty.
The spooler reads that as end-of-scroll and then writes a manifest marked as a completed freeze
(`FalconHostSpooler.cs:216,220-223`). A tenant whose unmanaged assets cluster onto whole Discover
pages therefore gets a truncated inventory reported as a successful, complete run. The lab tenant
did not hit this only because all 298 assets fit one page of 1000.

## What "fixed" means

Every asset Falcon returns for an unfiltered Discover scroll appears in the emitted output, carrying
an empty findings array when it has no Spotlight correlation — which is what the pre-July flow did,
and what its own comment stated as the rule.

## Scope

Collector change in `cymulate-integration-adapters`. The parser in `cymulate-integration-parsers`
must be proven, by execution against a real artifact, not to regress; a defensive non-silent guard
there is allowed, a change to its output contract is not.
