# Task: Defender VM Split Collection

Align the AgentService DefenderVmCollector output shape with the integration-adapters
Defender VM collector, and wire the Defender Endpoint parser to consume the resulting
split raw output.

## Problem

The current AgentService Defender VM collector hydrates each machine record with all
related vulnerabilities under `data.Vulnerabilities[]` and batches uploads by row count
only (~1000 rows). A single asset with many vulnerabilities produces a huge NDJSON row;
the uploader wraps NDJSON in a JSON `fileContent` string, inflating wire size further.
Large batches fail with `413 Payload Too Large`, but some Defender VM upload calls ignore
the `SaveUploadAndDeleteBatchAsync` boolean result, so the collector can silently lose
most findings while still reporting success.

## Scope

Two repos:

1. **AgentService** (`/Users/user/Dev/AgentService`) — change DefenderVmCollector to emit
   split `assets_*.json` / `findings_*.json` output mirroring the adapter, add a 50MB
   NDJSON byte-cap batch guard, and propagate upload failures.
2. **cymulate-integration-parsers** (`/Users/user/Dev/cymulate-integration-parsers`) —
   wire `defender-endpoint-assets-and-findings` into split (DUAL_MODE) mode reusing the
   Defender VM split pre-process.

## Authoritative Reference

`/Users/user/Dev/Uri/defender-vm-split-collection-plan.md` — full specification, target
output shapes, implementation tasks, parser context, acceptance criteria, and review risks.
Treat it as authoritative. Adapter source of truth lives under
`/Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Collectors/DefenderVmCollector`.

## Out of Scope

- Cortex collector (untouched — used only as the local batch-guard reference pattern).
- Shared uploader (`CybiBatchUploader`) behavior.
- Defender VM parser logic (already prepared for split mode).
