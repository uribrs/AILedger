# Cortex XDR — Hydrated Assets + Findings Parser

Add a new parser class under `libs/packages/parsers/cortex/` that consumes the hydrated emit format produced by `CortexXdrCollector` and yields both `assets_df` and `findings_df`.

## Inputs

- Emit shape (one JSON object per emitted record):
  ```json
  {
    "asset": { "client_id": "...", "instance_id": "...", "type": "endpoint", "aid": "...", "value": "...", "os_type": null, "os_version": null, "first_seen": null, "last_seen": null, "finding_ids": [...], "ip_address": null, "mac_address": null, "tags": [], "os_build": null, "fqdn": "..." },
    "vulnerabilities": [
      { "id": "...", "client_id": "...", "instance_id": "...", "name": "...", "display_name": "...", "type": "vulnerability", "severity": "high", "mitigation": null, "status": "open", "first_seen": null, "last_seen": null, "cve_ids": ["CVE-..."], "description": "..." }
    ]
  }
  ```
- Reference sample: `AgentService/Source/CybiCollectors/CortexXdrCollector/Examples/emitted_finding_sample.json`.

## Outputs

- `assets_df` matching `integration.parser_output_assets` DB schema.
- `findings_df` matching `integration.parser_output_exposures` DB schema.
- Registered under a new key in `parsers/__init__.py` `PARSERS` map.

## Scope

- New class is additive — `CortexXdrAssets` stays unchanged in the same package.
- Parser is a pass-through normalizer: collector owns field mapping; parser validates required fields, builds dataframes, runs the standard `BaseParser.post_process`.
- No collector-side changes.

## Out of Scope

- Modifying or deprecating `CortexXdrAssets`.
- Adding a façade / `input_mode` selector (Cortex has only the hydrated emit shape).
- Collector / schema changes.
- DB migrations.
