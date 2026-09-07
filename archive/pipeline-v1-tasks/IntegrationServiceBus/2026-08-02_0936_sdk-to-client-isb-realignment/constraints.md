# Constraints

## Scope gate

- Phase 1 makes **ZERO edits** to either repo. Read-only. No exceptions.
- Stop and report for operator review before producing the Phase 2 plan.
- Stop **again** before any edit, even after Phase 2 is approved.
- Do not decide either deferred decision (D-A, D-B). Supply evidence only.

## ISB working agreement (`/Users/user/Dev/IntegrationServiceBus/CLAUDE.md`)

- Never commit, push, open a PR, or merge. The developer reviews first.
- Branch off `dev`; PR into `dev` with an explicit `--base dev`. Diff against `origin/dev`, **not**
  `master`. The GitHub default branch is `master`, so anything inferring a base infers wrong.
- Do not build or test after every edit. Build/test only before a review and before a push.
- Read `docs/clean-architecture.md` and `docs/coding-standards.md` before touching `src/`.
- Do not add a new reference from an ISB application project to an Infrastructure project.
- XML doc generation is off — a broken `<see cref="..."/>` compiles silently. Grep for old names after
  any rename.

## Expected non-regressions — do NOT chase

- `NU1900` on every project (private CodeArtifact feed unreachable offline).
- `DockerUnavailableException` across `Tests/API.UnitTests` and all `*.IntegrationTests` when Docker is
  down.
- Exactly one pre-existing failure in `Application.UnitTests`. Stash and re-run that single test before
  attributing any failure to a change.

## Hard technical constraints (validated, not assumptions)

- **Type forwarding cannot rename a namespace.** An ECMA-335 `ExportedType` row carries namespace +
  name and must match the target type's fully-qualified name exactly. A zero-code shim assembly named
  `Cymulate.Integration.Sdk` full of `[assembly: TypeForwardedTo(...)]` → Client therefore cannot bridge
  `Cymulate.Integration.Sdk.Contracts.IIntegrationAdapter` to
  `Cymulate.Integration.Client.Contracts.IIntegrationAdapter`. Any plan assuming a shim works must first
  restore the `Sdk` namespaces inside the Client assembly.
- **`AdapterCategory.SiemRules = 5` is a frozen cross-repo contract.** ISB's SDK csproj states the
  Adapters side hardcodes `(AdapterCategory)5` until it retargets. The value must not change.
- **Infra's `Cymulate.Integration.Client` has never been published under that name** and sits at
  `1.0.0-preview.4`, sharing one `Version` with `Cymulate.IntegrationInfra` from the repo-root
  `Directory.Build.props`. The client's standalone props file was deleted; the SDK's 3.x lineage is
  abandoned. Root props marks the line pre-release: "the libraries build and unit-pass but are not yet
  proven in an end-to-end composed run".
- **Infra's rename was deliberate and documented as BREAKING**, with "no compatibility shim or type
  forward" as an explicit choice (CHANGELOG, commit `0607e90`, PR #7 `rename/sdk-to-client`). Deferred
  candidate (a)(i) is a *reversal* of a signed-off decision, not a free option, and must be presented
  to the operator as such.
- Internal packages resolve **only** from CodeArtifact `cym-dom/cym-repo-nuget` via
  `packageSourceMapping` pattern `Cymulate.*` (Infra `nuget.config`). ISB has its own at
  `src/Cymulate.IntegrationServiceBus/nuget.config` — verify it matches before assuming a feed swap works.

## Process

- Do not use the Workflow tool or deep-research. Multi-agent orchestration was not requested.
- Reuse the normalized-diff parity methodology established by
  `IntegrationInfra/ai/active/2026-07-30_1108_shared-parity-carry` (type-level + normalized file-pair
  comparison), not an ad-hoc eyeball diff.
