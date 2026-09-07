# Task

Align the Cortex XDR integration probe with the AgentService `CortexXdrCollector` so that running the probe produces the same per-record asset and finding JSON objects the collector emits in production.

## Scope

- Repository: `IntegrationProbes` (this repo).
- Collector reference: `/Users/user/Dev/AgentService/Source/CybiCollectors/CortexXdrCollector/CortexXdrCollector.cs`.
- Expected output shapes: `CortexXdrCollector/Examples/emitted_asset_sample.json`, `emitted_finding_sample.json`.

## Out of scope

- Replacing the existing discovery probes (kept alongside emit).
- S3 batch upload (`ICybiBatchUploader`) — probe stays local.
- Production-scale 50 000 record / 100-page-pagination runs — probe stays small.
- Touching the AgentService collector itself.
