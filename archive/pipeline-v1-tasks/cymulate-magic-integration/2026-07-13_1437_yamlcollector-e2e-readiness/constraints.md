# Constraints

- Read-only analysis: no changes to product code in any of the three repos.
- All claims about behavior must be grounded in actual source code read from the repos
  (evidence hierarchy: source code > docs > speculation; label speculation explicitly).
- Trace the specific tenable.io use case, not a generic abstract flow.
- Assess against the locked direction: ADR-0001 (Magic orchestrates, two routes),
  ADR-0002 (ISB route = in-process YamlCollector adapter, peer bridge retired),
  ADR-0003 (engine dual-home drift watch, Proposed).
- Repo paths on this machine: magic=/Users/user/Dev/cymulate-magic-integration,
  ISB=/Users/user/Dev/IntegrationServiceBus, adapters=/Users/user/Dev/cymulate-integration-adapters
  (CLAUDE.md's /Users/slava/work/* paths are stale).
- Diagram must cover A→B→C→D including error/completion/checkpoint paths.
- Findings must distinguish: confirmed bug / gap (missing piece) / drift / working-as-intended.
