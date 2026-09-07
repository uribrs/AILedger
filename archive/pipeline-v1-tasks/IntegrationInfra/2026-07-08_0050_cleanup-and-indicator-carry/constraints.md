# Constraints

- Work on a new branch off `main`; never commit to `main` directly.
- Zero behavior change: fixes 1–2 are delete/rename-only; fix 3 is additive verbatim carry (logic byte-identical modulo namespace/using rewires).
- The word "Legacy" must not appear in any identifier (type/method/field/file name) in src/ after fix 2. Comments describing external legacy wire shapes are permitted.
- Carry discipline (established by prior `*-carry` tasks): relocate verbatim; only namespaces + already-established renames; no re-authoring, no "improvements".
- `IndicatorNames` must NOT be carried.
- Do not set/bump the package version in the csproj — suggest a bump in the final report only (operator decision).
- Do not relocate `AdapterGlobalDefaults` constants (AssetsFileName/FindingsFileName/DefaultLookbackDays) in this task — deferred (see decisions.md).
- C# style mirrors neighboring files (doc comments on public surface, sealed where neighbors seal, file-scoped namespaces if that's the local idiom).
- Build with `dotnet build IntegrationInfra.slnx` (net8.0 pinned via csproj; do not pass -f net9.0).
- NuGet restore uses the cym-dom CodeArtifact feed; a 401/403 on Cymulate.* is an auth issue, not a code issue.
- If `dotnet test` hangs in this harness, stop and rely on verifier subagents instead of poll-looping.
