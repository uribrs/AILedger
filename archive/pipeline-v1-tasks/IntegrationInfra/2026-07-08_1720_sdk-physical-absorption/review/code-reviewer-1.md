# Code Review — SDK physical absorption (packaging & wiring)

**Reviewer:** code-reviewer (isolated)
**Target:** `git diff db6d722...HEAD` on `absorb/sdk-source` (single commit `40f828a`)
**Stack:** .NET 8, MSBuild, NuGet Central Package Management, `dotnet build IntegrationInfra.slnx`

## Change classification

- **Change type:** infrastructure / packaging + public contract SDK (NuGet-packable).
- **Risk level:** Medium. The relocated `.cs` files are a verbatim library move (reviewed only for coherence in their new home, per scope). The authored surface — one new csproj, one standalone `Directory.Build.props`, central-package-management edits, a `PackageReference`→`ProjectReference` flip, the solution file, a test csproj, and doc edits — is release-affecting but small and reversible.
- **Depth applied:** deep on the authored packaging/wiring; shallow (coherence-only) on the relocated library.

## Verification performed

- Built the SDK project (`dotnet build … -f net8.0`) → **succeeds**, 0 errors.
- Built the SDK test project → **succeeds**, 0 errors.
- Packed `IntegrationInfra` (`dotnet pack -p:TargetFramework=net8.0`) → **succeeds**; inspected the emitted `.nuspec`.
- Packed the SDK → **succeeds**; inspected the emitted `.nuspec`.
- Grepped the whole repo (excluding `ai/`) for stale feed assumptions and residual version pins.

## Assessment

The authored wiring is **correct and internally consistent**, and the two behaviors that most easily go wrong in an absorption like this are both verified good:

1. **Standalone `Directory.Build.props` isolation works.** The pack produced `3.2.1` (not the parent's `1.0.0-preview.3`), confirming the SDK's own props stops the MSBuild walk-up and the parent is not imported.
2. **The `ProjectReference` flip packs correctly.** `Cymulate.IntegrationInfra.nuspec` declares `<dependency id="Cymulate.Integration.Sdk" version="3.2.1" />` and does **not** bundle `Cymulate.Integration.Sdk.dll` into its `lib/` — exactly the intended "reference by project, declare as package dependency" behavior.

No **Blocker** or **Major** findings. Items below are Observations / Minor.

---

## Findings

### 1. Standalone `Directory.Build.props` loses nothing load-bearing — verified (Observation, no action)

- **Problem/question raised:** does *not* importing `src/Directory.Build.props` drop repo-wide analyzer settings, deterministic-build flags, or SourceLink the parent carries?
- **Finding:** No. The repo has exactly two `Directory.Build.props` and no `Directory.Build.targets`; `src/Directory.Build.props` contains **only** `<Version>1.0.0-preview.3</Version>`. There are no analyzer, `Deterministic`, `ContinuousIntegrationBuild`, `TreatWarningsAsErrors`, `LangVersion`, or SourceLink properties anywhere in the tree for the SDK to miss. `IntegrationInfra.csproj` itself carries none of these either, so parity holds. Central Package Management is unaffected — `Directory.Packages.props` discovery is an independent walk-up that still resolves the single root file (confirmed: the SDK's `PackageReference`s with no inline version restored fine).
- **Impact:** None. The `DELIBERATELY STANDALONE` comment is accurate.
- **Fix:** None required.

### 2. License inconsistency between the two sibling packages (Minor)

- **Problem:** the new SDK csproj declares `<PackageLicenseExpression>MIT</PackageLicenseExpression>` (emitted into the nuspec as `license type="expression"` + `licenses.nuget.org/MIT`). The sibling `IntegrationInfra.csproj` declares **no** license at all. Both are internal Cymulate packages hosted on a private GHE (`cymulate-corp.ghe.com`).
- **Impact:** Low technical risk, but this is a public open-source license stamped onto a proprietary internal contract SDK. The divergence from the sibling package strongly suggests a template copy-paste rather than a deliberate licensing decision. If the SDK is ever mirrored to a public feed, MIT is a real (mis)grant.
- **Fix (local patch):** confirm intent. Either drop `PackageLicenseExpression` to match `IntegrationInfra`, or set both packages to the correct license deliberately. This is an authored line, not carried by the "verbatim" relocation.
- Requires: local patch, not refactor.

### 3. Release coupling introduced by the version bump + ProjectReference (Observation)

- **Problem:** `IntegrationInfra.nupkg` now declares a hard dependency on `Cymulate.Integration.Sdk` **3.2.1**. The feed previously pinned/served **3.2.0** (the removed `PackageVersion`). `3.2.1` currently exists only as a local build product.
- **Impact:** Any downstream consumer restoring `Cymulate.IntegrationInfra` from the feed will fail unless `Cymulate.Integration.Sdk 3.2.1` is published to the same feed. The two packages must now be built and published **together**; they are no longer independently releasable. This is a real operational consequence of the absorption, not a code bug.
- **Fix:** ensure the release pipeline publishes both packages from this repo, and confirm `3.2.1` (vs. matching whatever the ISB repo last published) is the intended new version for a verbatim-source move. Worth an explicit line in the release/runbook.
- Requires: process/release note; no code change.

### 4. `Microsoft.Extensions.*.Abstractions` pinned at `10.0.9` on a `net8.0` library (Minor)

- **Problem:** the SDK targets `net8.0` but pins `Microsoft.Extensions.DependencyInjection.Abstractions` and `...Logging.Abstractions` at `10.0.9` (a major ahead of the TFM). Verified the SDK's real dependency surface is exactly these two plus `Polly` (the `OpenTelemetry`/`IHttpClientFactory` hits are XML-doc comment text in `Contracts/IAdapterExecutionContext.cs`, not code) — so the pin set is complete and correct in *count*.
- **Impact:** low. The abstractions packages are netstandard-compatible so they load on net8; but pinning a `10.x` transitive floor on an `8.0` contract package raises the minimum for every consumer. It is consistent with the repo (`IntegrationInfra` already pins `Microsoft.Extensions.Http 10.0.9`), so this is a deliberate house version, not an accident.
- **Fix:** none required if the `10.x` floor is the repo standard. Flagging only so the major-ahead-of-TFM choice is conscious.
- Requires: none / awareness.

### 5. `NU1507` (CPM + 2 sources, no source mapping) now fires on the new projects (Observation)

- **Problem:** every restore in this environment warns `NU1507` because two feeds (`nuget.org`, `cym-dom/cym-repo-nuget`) are configured under Central Package Management with no source mapping. The `nuget.config` supplying these sources is not in this repo's diff (it is environmental/parent-level), so the condition is pre-existing — but the new SDK and test projects now surface it too.
- **Impact:** benign today. Note the theoretical hardening angle: `Cymulate.Integration.Sdk 3.2.0` still exists on `cym-dom`. The `ProjectReference` correctly shadows the feed (a project reference always wins over a package of the same id), so there is **no** restore ambiguity for the SDK itself — but source mapping would make that guarantee explicit rather than incidental.
- **Fix:** optional — add package source mapping at the repo level. Out of scope for this change; not introduced by it.
- Requires: none for this PR.

---

## Relocated library — coherence check (scope-limited)

Spot-checked that the moved sources sit coherently in their new home: namespaces are `Cymulate.Integration.Sdk.*` matching `RootNamespace`; the only external references are `Polly`, `Microsoft.Extensions.DependencyInjection` (`GetRequiredService` in `Query/BaseQueryAdapter.cs` — satisfied by the `.Abstractions` assembly), and `Microsoft.Extensions.Logging` — all three pinned. Compiles clean with `GenerateDocumentationFile` on and `CS1591/CS1570/CS1574` suppressed. Nothing looks broken or incoherent in the new location. No deeper design review performed, per scope.

## Docs accuracy

`README.md`, `ARCHITECTURE.md`, `DESIGN.md`, `CHANGELOG.md` edits are technically accurate: the "two packages / two version lines" framing matches the built artifacts, the dependency arrow `Everything ──► Cymulate.Integration.Sdk` still holds under the `ProjectReference`, and the `DESIGN.md` clarification that the SDK is a separate contract package (not a concern split) is correct. No stale "one NuGet package / SDK from the feed" language remains outside `ai/`. No CI/build scripts exist in the repo to update.

## Bottom line

Ship-able. The packaging mechanics are verified correct end-to-end (build + pack + nuspec inspection). Before merge, resolve **Finding 2** (confirm the MIT license is intended) and make sure **Finding 3** (both packages must publish together; `3.2.1` must reach the feed) is captured in the release process.
