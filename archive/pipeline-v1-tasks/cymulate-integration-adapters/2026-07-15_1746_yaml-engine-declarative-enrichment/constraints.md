# Constraints

- No vendor names or vendor-conditional logic anywhere in engine code.
- All new constructs expressed in the existing workflow stage grammar; validated at load time.
- Stream-through stages (not a merge target) must be behaviorally unchanged — byte-identical output path.
- No changes to: adapter public surface, ISB host contract, Shared/* subsystems, S3 layout (`{kind}_NNNNNN.json`), done-event payload shape.
- No yaml files created or modified in this task.
- No version bump (operator decides separately; never in csproj regardless).
- Merge is per-page: extract keys → fetch uncached keys (batched) → embed → publish that page. No whole-run barrier, no deferred-until-end publish.
- Enrichment cache: run-scoped, bounded (default 10,000 entries), evict-oldest; never persisted to checkpoints.
- Duplicate source join keys: last wins, logged.
- Join key comparison: canonical case-insensitive strings.
- merge_into validation: target must be an EARLIER stage with a topic; key paths must parse; reject unknown targets/cycles at load.
- Tests live in Cymulate.Integration.Adapters.Collectors.YamlCollector's engine test project (Workflow/, Mapping/, Pagination/ suites); use the real captured XML fixtures for converter regressions.
- Build with the solution's pinned net8.0 target; central package management (no versions in csproj).
- C# style: small methods, mirror neighboring engine patterns; no speculative abstractions.
