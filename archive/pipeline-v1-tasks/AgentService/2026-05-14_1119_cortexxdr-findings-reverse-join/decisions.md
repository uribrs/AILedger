# Decisions

- **D1** — Refactor scope is `CollectFindingsAsync` plus dead-helper cleanup. `CollectAssetsAsync` untouched.
- **D2** — Defender-style intermediates: dump `cves.jsonl` and `endpoints.jsonl` to a per-collection temp dir; enrich and emit in Phase 2.
- **D3** — `cvesByHost` is an in-memory `Dictionary<string, List<JObject>>` keyed on hostname (case-insensitive).
- **D4** — Drop the `/xql/get_datasets` readiness gate and all helpers that become unreachable as a result.
- **D5** — `baseDate` gates the endpoints pagination only; va_cves XQL stays unbounded.
- **D6** — Port mapper logic byte-faithfully from `CortexXdrEmitMapper.cs` (probe) into a collector-local helper class (e.g. `CortexXdrFindingsMapper`) to keep `CortexXdrCollector.cs` lean — same spirit as DefenderVm's `Services/` split.
- **D7** — Direct execution path (single contributor, focused refactor, no decomposition).
