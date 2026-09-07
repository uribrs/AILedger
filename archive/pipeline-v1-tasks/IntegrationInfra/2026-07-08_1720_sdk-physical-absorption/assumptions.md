# Assumptions

- VALIDATED — SDK is a clean leaf: no ISB ProjectReferences, no Cymulate.IntegrationServiceBus.* imports, no InternalsVisibleTo (recon 2026-07-08).
- VALIDATED — Infra's Directory.Packages.props pins neither Polly nor M.E.DependencyInjection/Logging.Abstractions → the SDK's three pins add without conflict at pin level (grounded at contract time).
- VALIDATED — SDK source version is 3.2.1 via ISB's Sdk/Directory.Build.props (`AdapterVersion` default), not the csproj.
- VALIDATED (restore/build clean, no downgrade warnings) — Polly 8.7.0 direct pin coexists with the transitive Polly resolved via Cymulate.Http.Package.* (DefensiveToolkit). Executor verifies at restore/build; a downgrade/conflict error is a surface-and-decide, not a silent pin change.
- VALIDATED (grep) — no Infra test csproj package-references Cymulate.Integration.Sdk directly (they ProjectReference IntegrationInfra). Executor greps before removing the feed pin.
- VALIDATED (build 0 errors + 247/247) — 3.2.0→3.2.1 surface is compatible with Infra's 67 consuming files + 238-test net. Executor's build/test run is the check; incompatibility is a STOP (would imply SDK code edits or Infra behavior changes, both out of contract).
- VALIDATED (aligned: explicit TFM, coverlet dropped, pins reconciled) — the SDK unit-test project's package references (test SDK/xunit versions in ISB) reconcile with Infra's central pins (17.11.1/2.9.2). Executor aligns the carried test csproj to Infra's pins — test csproj edits are allowed (it's packaging, not SDK code).
