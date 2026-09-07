# Constraints

- Tests-only: no product-code changes. Sole exception: a test exposing a GENUINE product bug is fixed directly (standing operator rule), recorded in decisions.md.
- No duplicate tests: every addition must fill a verified gap in the existing suites; audit before writing.
- Branch off `dev`; never commit to `dev`/`master` directly.
- The public-statics reshape is a separate later task — do not start it, do not "prepare" for it in product code.
- Build: `dotnet build IntegrationInfra.slnx` (net8.0 pinned via csproj). Full suite must be green at the end; existing tests must not be modified except to DEEPEN assertions per (c)/(d) audit — never weakened or renamed without cause.
- No version bump (src/Directory.Build.props untouched).
- Test style mirrors existing projects: xUnit, plain Assert, sealed test classes, file-scoped namespaces, per-concern placement. No FluentAssertions/Moq additions unless already referenced by that test project.
- Reuse existing test doubles (Emission has EmissionTestDoubles.cs/RecordingPublisher.cs; Conducting invariant tests have their own context doubles) before inventing new ones.
- If driving the full runner is impractical for a gap, test at the closest composable seam and record the altitude choice in decisions.md.
- ai/ is gitignored in this repo — task records are disk-only; mirror to ~/codex-state/tasks/IntegrationInfra/2026-07-08_0155_invariant-pin-tests/.
- If `dotnet test` hangs in this harness, stop and rely on verifier agents; don't poll-loop.
