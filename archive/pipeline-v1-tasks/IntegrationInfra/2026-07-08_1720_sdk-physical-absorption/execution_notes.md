# Execution Notes

Branch: `absorb/sdk-source` (off dev @ db6d722). Single commit: 40f828a (118 files, +7035).

## S1 — Copy
- rsync (excluding bin/obj): 105 .cs files (recon's "120" counted obj/ generated files). Test project
  (AdapterResultTests.cs, 9 tests) → tests/Cymulate.Integration.Sdk.UnitTests/.
- Verbatim gate run twice: dir-level `diff -r` shows ONLY the csproj URL lines differ; md5 set-compare of
  the .cs trees (obj/bin excluded both sides — first attempt forgot dest-side exclusions and flagged
  build-generated files, corrected) = byte-identical.

## S2 — Packaging
- `src/Cymulate.Integration.Sdk/Directory.Build.props`: STANDALONE (decision recorded in-file) — parent
  src/Directory.Build.props' only load-bearing content is Infra's 1.x Version, which the SDK must not
  inherit. Sets Version 3.2.1 + Assembly/File/InformationalVersion to mirror ISB output.
- Adapter cosplay NOT carried (it lived in ISB's Sdk-dir props, not the csproj — the carried csproj was
  already clean). RepositoryUrl + PackageProjectUrl → cymulate-rnd/IntegrationInfra.
- Provenance README added at the project root.
- Test csproj aligned to Infra conventions: explicit net8.0 TFM + IsTestProject, coverlet.collector
  dropped (no Infra test project uses it; no central pin), ProjectReference re-pathed.

## S3 — Wiring
- slnx: both projects added. Directory.Packages.props: +M.E.DependencyInjection.Abstractions 10.0.9,
  +M.E.Logging.Abstractions 10.0.9, +Polly 8.7.0; Cymulate.Integration.Sdk 3.2.0 feed pin REMOVED
  (grep-verified: no csproj package-references the SDK).
- IntegrationInfra.csproj: PackageReference → ProjectReference.

## S4 — Build/tests (assumptions resolved)
- Build 0 errors → 3.2.0→3.2.1 source compatibility VALIDATED (no surface delta hit Infra's 67 files).
- No NuGet downgrade/conflict warnings → Polly 8.7.0 direct pin coexists with transitive resolution: VALIDATED.
- Full suite 247/247 (238 existing untouched + 9 SDK).

## S5 — Pack verification (nuspec inspected, not assumed)
- Cymulate.Integration.Sdk.3.2.1.nupkg + snupkg: repository url=cymulate-rnd/IntegrationInfra (+commit
  hash), 3 dependencies exactly. IsAdapter deep-check: 3 grep hits inside the package traced to
  `IsAdapterRegistered` (legitimate SDK member) + its XML doc lines — NO IsAdapter assembly metadata.
- Cymulate.IntegrationInfra.1.0.0-preview.3.nupkg: declares dependency Cymulate.Integration.Sdk >= 3.2.1. 
- Pack note: `--no-build` fails (Release vs Debug artifacts); packed with build. CI should pack Release.

## S6 — Docs
- README + ARCHITECTURE: one-repo/two-packages/two-version-lines. DESIGN "Concerns, not layers" clarifier
  (SDK = contract package, not a concern split). CHANGELOG [Unreleased] entry.

## Residual
- ISB repo untouched (host still builds its own in-tree SDK copy until the coordinated S4-swap of the
  absorption sequence). Publishing 3.2.1 from the new home = pipeline task.
- NU1507 source-mapping warnings now also on the two new projects (pre-existing repo-wide noise).

## Post-review disposition (code-reviewer-1)
- MIT PackageLicenseExpression: reviewer flagged as authored — CORRECTION: it is verbatim from the ISB csproj (SDK has shipped MIT-stamped through 3.2.0). Inherited, surfaced to operator as a licensing decision; not changed here.
- Release coupling (both packages publish together; 3.2.1 must reach the feed before any downstream restores Infra): routed to the publish-pipeline task's requirements.
- MS.Extensions 10.x pins on net8.0 + NU1507 noise: pre-existing house conventions, accepted.
