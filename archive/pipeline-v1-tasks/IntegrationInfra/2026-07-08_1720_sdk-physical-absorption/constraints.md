# Constraints

- SDK `.cs` files VERBATIM — byte-identical to ISB source; the only edited/authored files are csproj, Directory.Build.props, and a provenance README. Any .cs edit is out of contract (stop and surface, incl. 3.2.0→3.2.1 surface deltas that break Infra).
- No namespace changes anywhere; PackageId stays `Cymulate.Integration.Sdk`; version stays 3.2.1 (no bump).
- No `Cymulate.IntegrationInfra` version bump.
- ISB repo is READ-ONLY for this task — nothing there changes (host swap is a later coordinated step).
- Samples project stays in ISB (not carried).
- Versions live in Directory.Build.props files only, never in csproj.
- Do NOT carry `AdapterVersion`/`IsAdapter=true` metadata into the carried packaging.
- Full suite green: 238 existing + 9 SDK = 247; existing tests unmodified.
- Feed pin `Cymulate.Integration.Sdk 3.2.0` must not dangle — removed after verifying no project package-references the SDK.
- Build via `dotnet build IntegrationInfra.slnx`; pack checks via `dotnet pack` per project. If `dotnet test` hangs, stop and use verifier agents.
- ai/ gitignored; mirror task dir to ~/codex-state/tasks/IntegrationInfra/2026-07-08_1720_sdk-physical-absorption/.
