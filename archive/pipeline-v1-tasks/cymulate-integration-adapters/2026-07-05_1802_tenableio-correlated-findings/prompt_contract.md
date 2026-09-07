# Prompt Contract

Role:
You are a senior .NET integration engineer working in the Cymulate integration-adapters repo, expert in long-haul vendor-export collectors, checkpointed resumability, and bounded-memory streaming.

Goal:
Rewrite the TenableIo `CollectFindings` flow to emit correlated asset-driven NDJSON records `{uuid, chunk, isLastChunk, findingsInChunk, host, findings[]}` (host = full `/assets/export` record; findings = raw `/vulns/export` records; 2,000-findings cap; zero-vuln assets emit empty-findings envelopes), replacing the two passthrough lanes, with bounded memory (compressed in-RAM asset spool + budget + miss lane) and working checkpoint resume (claimed-first rebuild), on branch `feature/tenableio-correlated-findings`.

Context:
- Collector root: `src/Cymulate.Integration.Adapters/Collectors/TenableIoCollector` (entry `TenableIoCollector.cs`; flows under `Flows/Assets` and `Flows/Findings`; checkpoints under `Recovery/`; config under `Processing/Configuration/`).
- Today: CollectFindings runs assets lane then findings lane on one batch dir (`combinedRun`), both dumb passthrough; export lifecycle = POST export → poll status → progressive chunk download; checkpoint = export uuid + processed Tenable chunk ids + page counter; phase-transition checkpoint chains assets→findings.
- Reference implementation for the record grammar and correlated-flow idioms: `Collectors/FalconCollector/Flows/Findings/` (correlated rewrite, task 2026-07-02_1705).
- Mechanism, evidence, and pinned decisions: see `task.md`, `decisions.md`, `assumptions.md` in this directory. All vendor-behavior assumptions are VALIDATED — no external research needed.

Constraints:
- All items in `constraints.md` (repo boundaries, carried-over machinery, resilience wiring, export request shapes, spool/budget/miss-lane semantics, resume ordering, version bumps, test/docs/test-run protocol, adapters-repo-only scope).

Success Criteria:
- Adapters solution builds (`dotnet build Cymulate.Integration.Adapters.sln`, TargetFramework pinned by csproj).
- TenableIo unit tests green via `dotnet vstest` on the built test dll, covering: spool hit / miss / budget-overflow; per-chunk bucketing + 2,000-cap flush + isLastChunk; zero-vuln sweep; thin-envelope miss lane; claimed-first resume rebuild; checkpoint round-trip at the new format version; phase-transition and combined-run resume paths.
- LocalAdapterRunner runs `--collector "Tenable.io" --dry-run` (or equivalent alias) and a real CollectFindings end-to-end against the configured tenant emitting correlated `findings_*.json` (final large-tenant comparison run is performed by the user).
- Docs synced: `TenableIoCollector/Documentation/*` reflects the correlated model (no stale lane descriptions); relevant `ai/skills` collector content updated; `Collectors/README.md` updated if it references TenableIo output shape.
- Checkpoint format version bumped; MAJOR CollectorVersion bump in the csproj.
- No modifications under `Shared/`.

Execution Rules:
- Do not assume missing data; consult the referenced files.
- Respect constraints strictly; pinned decisions are not re-litigated; provisional decisions are implemented as pinned unless the user overrides.
- Mirror the Falcon correlated implementation idioms where they transfer; keep vendor-specific logic (export lifecycle) in the Tenable shapes that already work.
- Fix review-surfaced bugs directly during execution; reserve questions for genuine forks.

Output Format:
- Code changes on branch `feature/tenableio-correlated-findings` (no commit unless instructed).
- `execution_notes.md` appended with what changed per step, test results, and any accepted deviations.
- `state.json` step statuses updated.

Stop Conditions:
- Goal achieved (all success criteria met or explicitly handed to the user, e.g. the tenant comparison run).
- A pinned constraint cannot be satisfied without violating another — stop and surface.
- Required data missing (e.g. runner cannot resolve the collector alias) after reasonable inspection — stop and surface.
