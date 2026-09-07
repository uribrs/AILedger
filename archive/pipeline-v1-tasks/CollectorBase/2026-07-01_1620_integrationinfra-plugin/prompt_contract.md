Role:
You are a senior .NET engineer performing a dependency swap: replacing a deleted in-solution project with a
NuGet package, via mechanical namespace + type-name rewiring, to a clean build.

Goal:
Plug `Cymulate.IntegrationInfra` (1.0.0-preview.2, local feed) into CollectorBase in place of the deleted
`Shared` project — solution/csproj plumbing + namespace remaps + type renames across 22 consumer files — and
reach a clean `dotnet build CollectorBase.slnx` with passing CollectorExecutor tests.

Context:
- Repo: /Users/user/Dev/Uri/localprojects/CollectorBase (NOT git — no commit; deliverable is green build + tests).
- Package: /Users/user/Dev/Uri/localprojects/IntegrationInfra/artifacts/Cymulate.IntegrationInfra.1.0.0-preview.2.nupkg.
- Shared/ folder is deleted; CollectorBase.slnx + 4 csprojs (Runner, CollectorExecutor, Tests x2) have dangling refs.
- Full namespace map, type renames, keep-unchanged list, scope steps (S1–S7), decisions, and assumptions live in
  `task.md`, `constraints.md`, `decisions.md`, `assumptions.md`.

Constraints:
(Binding list in constraints.md. Load-bearing:)
* Reference/namespace rewire only — no consumer logic changes.
* Do NOT edit IntegrationInfra; a genuine gap → STOP + surface (do not patch around in CollectorBase).
* nuget.config adds the local feed WITHOUT `<clear/>` (keep the org feed for transitive Cymulate deps).
* Central versions in Directory.Packages.props (add IntegrationInfra 1.0.0-preview.2; bump Sdk → 3.2.0).
* Specific-before-bare namespace substitution; SDK types + Conducting `Collector*` machinery unchanged.
* No residual `Cymulate.Integration.Adapters.Shared.*`.

Success Criteria:
1. Shared removed from CollectorBase.slnx + all 4 dangling ProjectReferences removed.
2. `Cymulate.IntegrationInfra` referenced by the needed consumers; central `PackageVersion` 1.0.0-preview.2 added;
   `Cymulate.Integration.Sdk` bumped to 3.2.0; `nuget.config` local feed added (no `<clear/>`).
3. All 22 consumer files rewired (namespace remaps + type renames); zero residual `...Adapters.Shared.*`.
4. `dotnet build CollectorBase.slnx` clean (0 errors; pre-existing NU warnings OK).
5. `dotnet test` — Tests/CollectorExecutor.Test + Collectors.Tests.Infrastructure pass (end-to-end proof).
6. Any gap/blocker surfaced explicitly (not worked around).
7. Verifier + code-reviewer passes run (code-bearing).

Execution Rules:
* Read a target file before editing; drive the rewrite by build feedback (restore → build → fix stragglers).
* Do not assume a type's target namespace/name — verify against the installed IntegrationInfra package/build errors.
* Respect constraints; if a fix would require editing IntegrationInfra or re-declaring a missing type locally, STOP + surface.

Output Format:
* Edited CollectorBase.slnx, 4 csprojs, Directory.Packages.props, new nuget.config, 22 rewired .cs files.
* Updated `execution_notes.md` (dated action log + assumption dispositions + restore/build/test output + any gap surfaced).
* Updated `state.json` (step statuses, blockers, lastUpdated).

Stop Conditions:
* A Shared type used by consumers is missing from IntegrationInfra (new gap — surface, don't work around).
* Sdk 3.2.0 unavailable on the feed or breaks CollectorBase compilation.
* Restore can't resolve the package (local feed / org feed).
* A consumer relied on a Shared API whose shape (not just name) changed.
* Goal achieved and all success criteria met.
