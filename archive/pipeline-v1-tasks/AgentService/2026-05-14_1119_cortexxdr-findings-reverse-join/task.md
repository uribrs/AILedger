# Task — Cortex XDR findings flow refactor (reverse join)

Refactor `CortexXdrCollector`'s findings flow to a Defender-style in-flight pipeline:

- Dump intermediates (`cves.jsonl`, `endpoints.jsonl`) to a per-collection temp directory.
- Enrich and emit hydrated `{asset, vulnerabilities[]}` rows during a second pass.
- Replace the existing forward join (`va_endpoints.cves[]`) with a reverse join from `va_cves.affected_hosts[]` → `endpoint.endpoint_name` (case-insensitive).
- Drop the `/xql/get_datasets` readiness gate and any helpers it rendered unreachable.

Scope is strictly the findings flow. `CollectAssetsAsync` is untouched.

Repo: `/Users/user/Dev/AgentService`
Branch: `cortex-assets-and-findings` (HEAD `68ef31c89`)
Single file under refactor: `Source/CybiCollectors/CortexXdrCollector/CortexXdrCollector.cs` (plus a new collector-local mapper helper class).
Worktree-modified file `Source/CybiCollectors/CortexXdrCollector/CortexXdrCollector.csproj` must be preserved as-is.
