# Constraints

## Behavior / surface
- No behavioral change; existing `FalconCollector.Test` suite must pass unchanged.
- No change to the public/SDK-facing surface: `FalconCollector` `IAssetsCollectorAdapter` /
  `IFindingsCollectorAdapter` methods, `ProcessAsync`, `ResumeAsync`, `CanResumeFrom`
  keep signatures and behavior.
- `FalconCheckpointHelper` public static methods (`SaveAssetsState`, `SaveFindingsState`,
  `TryLoadAssetsState`, `TryLoadFindingsState`, `CanResumeFrom`, `ApplyCursorTtlForResume`)
  keep their signatures — split work hides behind thin delegators.
- Internal call sites may change.

## Build / test
- Target `net8.0`; pin `TargetFramework` per CLAUDE.md. Do NOT pass `-f net9.0`.
- NuGet restore needs AWS CodeArtifact auth; a 401/403 on `Cymulate.*` is an auth
  issue, not a code issue — surface it, do not "fix" it in code.
- Test output redirection (`artifacts/bin|obj/ut/`) and in-project obj/bin excludes
  must not be "fixed".

## Style
- DTOs/records follow repo house style: positional `sealed record` with XML doc
  comments (ref `Dtos/FindingsDtos/FindingsFlowRunConfig.cs`) or init-only-prop
  `sealed record` for config/options; ref Shared `CollectorResumeExecutionContext`,
  `AdapterRecoveryContext`. No plain mutable data-bag classes.
- DTOs may be ported into a `Models` section/namespace if cleaner; update namespaces.
- C# style: small methods, helpers over long procedural methods, mirror existing
  neighbors; `Nullable` + `ImplicitUsings` on; `internal` members exposed to tests
  via existing `InternalsVisibleTo`.

## Execution (collision safety)
- Phased. Within a phase, no two workers touch overlapping files.
- Phase A is foundational and BLOCKS Phase B (B workers consume A's helpers).
- Phase B workers are file-disjoint (see decisions.md for the partition).

## Docs
- If documented flow/recovery architecture changes, update
  `ai/skills/collector-flow-patterns/SKILL.md` and `ai/skills/collector-recovery/SKILL.md`.
