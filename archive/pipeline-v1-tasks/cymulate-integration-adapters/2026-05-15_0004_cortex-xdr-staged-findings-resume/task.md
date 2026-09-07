# Cortex XDR Staged Findings And Resume

Fix `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector` so the findings flow collects and publishes Cortex CVE rows and endpoint asset rows as separate upstream-hydration datasets.

The legacy implementation and current port must be compared, but the target contract is not adapter-side hydration. The adapter must publish CVEs to chunked `findings_*.json` files and endpoints to chunked `assets_*.json` files, with checkpoint/resume support that reflects both stages.
