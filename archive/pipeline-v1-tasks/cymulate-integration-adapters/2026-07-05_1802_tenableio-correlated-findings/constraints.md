# Constraints

- Repo CLAUDE.md boundaries: Shared owns orchestration/session/egress; collector owns vendor logic; do not re-implement the AdapterBusEntrypointRunner pipeline.
- No changes under `Shared/` (egress hash logging etc. already exists from the Falcon task).
- Weigh behaviour over shape: existing machinery carries over unless the redesign obsoletes it — progressive chunk loop, 409 `active_job_id` reuse, per-chunk retry/exclusion, `MaxSkippedChunkRatio` guard, byte-budget egress re-pagination, dry-run probe.
- Existing resilience wiring stays: `MappedFailurePolicy` + `TenMinuteSingleShot` transient backoff, in-process server-delay threshold 60s, session retry/rate-limiter/circuit-breaker config.
- Vulns export request: `since=baseDate`, `state=[OPEN,REOPENED]`, `num_assets` (default lowered 500→50). Assets export request: `chunk_size=1000`, `filters.last_assessed=baseDate`. Always send time filters (30-day default trap).
- Spool: compressed raw bytes only, never parsed DOM; hard budget with defined overflow (chunk-0 publish + marker set, host-less findings for marked assets); spool behind an abstraction so a windowed-join mode is a future config knob — do NOT implement windowed mode now.
- Miss lane: thin host synthesized from the finding's embedded `asset` sub-object; counted + logged; never dropped, never buffered.
- Resume: claimed-set rebuilt FIRST (uuid-only re-scan of processed vuln chunk ids), then spool rebuild skipping claimed. Checkpoint staleness stays at the existing ~24h rule.
- Checkpoint format version bump; MAJOR CollectorVersion bump.
- Tests: xUnit + Moq + FluentAssertions; `InternalsVisibleTo` for internals; central package management (versions in Directory.Packages.props).
- Test-run protocol: `dotnet test` hangs in this harness — build, then `dotnet vstest` on `artifacts/bin/ut/...` dlls; phase-boundary cadence only.
- Docs sync: `TenableIoCollector/Documentation/*`, `ai/skills` collector content, `Collectors/README.md` if it references the TenableIo output shape.
- Scope: adapters repo only; no parser changes in this task.
- LocalAdapterRunner must run the new CollectFindings end-to-end (user performs the tenant comparison).
