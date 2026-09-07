# Verifier 1 — SDK Physical Absorption

## Verdict

**PASS.** Every Success Criterion in `prompt_contract.md` is satisfied, independently re-derived. The
verbatim gate holds byte-for-byte, both packages pack with correct identity and dependency wiring, the
full 247-test suite is green, and all constraints (no Infra bump, no namespace changes, ISB untouched,
IsAdapter cosplay dropped, feed pin removed without dangling) are met. No blocking findings.

## Criterion Table

| # | Success Criterion | Result | Evidence |
|---|---|---|---|
| 1 | SDK pack → 3.2.1 nupkg + snupkg; nuspec 3 deps + RepositoryUrl=cymulate-rnd/IntegrationInfra + NO IsAdapter assembly metadata | PASS | Both artifacts created; nuspec has exactly 3 deps + correct repository url; dll decompile shows only `AssemblyMetadata("RepositoryUrl",...)` — no IsAdapter/AdapterVersion pair |
| 2 | Infra pack → nuspec declares Cymulate.Integration.Sdk (>= 3.2.1) | PASS | Infra nuspec: `<dependency id="Cymulate.Integration.Sdk" version="3.2.1" .../>` (NuGet min-inclusive) |
| 3 | build 0 errors; full suite green 247 (238 + 9 SDK) | PASS | `dotnet build IntegrationInfra.slnx` → 0 errors; `dotnet test` → 247 passed / 0 failed across 8 projects |
| 4 | diff -r dest SDK .cs vs ISB source clean (only csproj/props/README differ) | PASS | `diff -r` reports only the 2 csproj URL lines + dest-only `Directory.Build.props`/`README.md`; every `.cs` identical |
| 5 | Directory.Packages.props: 3 pins added, SDK feed pin removed, no project package-references SDK | PASS | 3 pins present (M.E.DI.Abstractions 10.0.9, M.E.Logging.Abstractions 10.0.9, Polly 8.7.0); no SDK PackageVersion pin; both consumers use ProjectReference |
| 6 | Docs: README + ARCHITECTURE (two-package), DESIGN clarifier, CHANGELOG [Unreleased] | PASS | All four updated in commit 40f828a; DESIGN clarifier reaffirms "Infra's concerns remain one assembly" — no contradiction |
| 7 | execution_notes records Build.props/Polly/3.2.1 outcomes; state.json updated; archive mirrored | PASS | execution_notes S2/S4 cover all three; state.json status=executed, steps completed; mirror present at ~/codex-state |

## Findings by Severity

### Blocking
None.

### Non-blocking / Observations
- **NU1507 source-mapping warnings** now also emitted for the two new projects (nuget.org + cym-dom
  both defined without package source mapping). This is pre-existing repo-wide noise (all 8 existing
  projects emit it too) and is documented in execution_notes S-Residual. Cosmetic; not a
  task-introduced defect.
- **Infra nuspec dependency reads `version="3.2.1"`** — this is NuGet's minimum-inclusive form and
  correctly satisfies the ">= 3.2.1" criterion. Noted only to preempt a "should it say >=?" question.

## Evidence

**ISB untouched:** `git -C /Users/user/Dev/IntegrationServiceBus status --short` → clean (empty); the
Sdk source path is unmodified. ISB SDK csproj still points at the old
`github.com/cymulate-rnd/IntegrationMediatorService` URL (confirming the URL edit was made only on the
carried copy).

**Branch/commit:** `absorb/sdk-source` @ 40f828a, parent db6d722 (matches diff base). Working tree clean.

**Verbatim gate (re-run):**
- SDK: `diff -r -x bin -x obj <ISB-Sdk> <dest>` → only `Cymulate.Integration.Sdk.csproj` differs (2
  URL lines: PackageProjectUrl + RepositoryUrl), plus dest-only `Directory.Build.props` and `README.md`.
  Zero `.cs` differences.
- Test: `AdapterResultTests.cs` diff → exit 0 (byte-identical). Test csproj differs by the allowed set
  (net8.0 TFM + IsTestProject added, coverlet.collector dropped, ProjectReference re-pathed).
- Actual ISB test project lives at `.../Sdk/UnitTests/Cymulate.Integration.Sdk.UnitTests` — not the
  `.../Sdk/Cymulate.Integration.Sdk.UnitTests` path named in the contract. Immaterial: the carried
  `.cs` is byte-identical to the real source.

**Pack (re-run to temp dir, Release):**
- `Cymulate.Integration.Sdk.3.2.1.nupkg` + `.snupkg` created.
- `Cymulate.IntegrationInfra.1.0.0-preview.3.nupkg` + `.snupkg` created.
- SDK nuspec: id=Cymulate.Integration.Sdk, version=3.2.1, exactly 3 net8.0 deps
  (M.E.DI.Abstractions 10.0.9, M.E.Logging.Abstractions 10.0.9, Polly 8.7.0),
  `repository url="https://cymulate-corp.ghe.com/cymulate-rnd/IntegrationInfra" commit="40f828a..."`.
- Infra nuspec deps: Cymulate.Integration.Sdk 3.2.1, Cymulate.Http.Package.Session 2.0.2,
  Microsoft.Extensions.Http 10.0.9, OpenTelemetry 1.12.0.

**IsAdapter assembly-metadata check (ilspycmd decompile of the packed dll):** the only
`[assembly: AssemblyMetadata(...)]` is `("RepositoryUrl", "https://cymulate-corp.ghe.com/...")`. No
`IsAdapter` or `AdapterVersion` assembly metadata. The 1 `IsAdapter*` source hit is the legitimate
`bool IsAdapterRegistered(...)` interface member on IAdapterRegistry. Confirmed the origin: ISB's
`Sdk/Directory.Build.props` held the `IsAdapter=true`/`AdapterVersion` AssemblyMetadataAttribute pair;
it was deliberately not carried (dest standalone `Directory.Build.props` sets only versions).

**Version isolation (built dlls):**
- SDK: AssemblyVersion 3.2.1.0, FileVersion 3.2.1.0, InformationalVersion 3.2.1+40f828a.
- Infra: AssemblyVersion 1.0.0.0, FileVersion 1.0.0.0, InformationalVersion 1.0.0-preview.3+40f828a.
- SDK-dir `Directory.Build.props` is standalone (does NOT import parent) — decision recorded in-file
  with rationale (parent's only load-bearing content is Infra's 1.x version the SDK must not inherit).

**Wiring:** slnx lists both `src/Cymulate.Integration.Sdk` and
`tests/Cymulate.Integration.Sdk.UnitTests`. Directory.Packages.props has the 3 pins and NO
Cymulate.Integration.Sdk pin. IntegrationInfra.csproj line 38 ProjectReferences the SDK. No csproj
anywhere PackageReferences the SDK package.

**Suite arithmetic:** SDK 9 + FaultGovernance 27 + Reporting 15 + Job 50 + Kernel 69 + Conversation 11
+ Conducting 16 + Emission 50 = 247. 0 failed, 0 skipped.

**Namespaces:** grep of all carried `.cs` finds no `namespace` outside `Cymulate.Integration.Sdk.*`.

**Build warnings:** 23, all NU1507 (source mapping) + 3 CS1574 (unresolved XML cref) — pre-existing
noise, no NU1605 downgrade (confirms Polly 8.7.0 pin coexists cleanly).
