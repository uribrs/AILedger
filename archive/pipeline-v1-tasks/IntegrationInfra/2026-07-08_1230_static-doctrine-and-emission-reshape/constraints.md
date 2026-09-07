# Constraints

- Branch off `dev`; never commit to `dev`/`master` directly.
- Sequencing: Part 1 doctrine text lands BEFORE Part 2 code (separate commits; doctrine is the review basis).
- Behavior-identical reshape: the 237-test invariant net must pass with ONLY mechanical arrange/act call-site updates in tests. Changing any assertion is a STOP condition (surface, don't adapt the assertion).
- No version bump — operator rule: none until a real consumer exists.
- Concern DAG unchanged: Emission depends on Kernel + Envelopes.Common only; no new using directions.
- Scope fence: only the Emission publisher stack reshapes. ThrottlingAdapterExecutionContext, AdapterOutputDefaults/AdapterGlobalDefaults, batch sessions' internal logic, and all other concerns untouched (sessions may receive already-resolved options instead of resolving — that IS in scope; their flush/upload logic is NOT).
- Option resolution semantics preserved exactly: DI instance → IConfiguration section → env {Section}__{Key} → default, resolved once at emitter construction.
- Startup guards preserved (MaxBytesPerBatch ≥ 5 MiB; effective maxBufferedBytes ≥ 5 MiB) — same exception type/wording at equivalent point (construction or first publish; record choice).
- Static entrypoints deleted, not wrapped (operator-approved ruling).
- Style mirrors neighbors; doc comments on public surface; sealed where neighbors seal.
- Build `dotnet build IntegrationInfra.slnx`; if `dotnet test` hangs, stop and rely on verifier agents.
- ai/ gitignored; mirror task dir to ~/codex-state/tasks/IntegrationInfra/2026-07-08_1230_static-doctrine-and-emission-reshape/.
