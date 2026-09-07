# Assumptions

## VALIDATED at contract time (direct inspection, 2026-08-02)

- VALIDATED — ISB SDK on `origin/dev` is 73 `.cs` files under
  `src/Cymulate.IntegrationServiceBus/Sdk/Cymulate.Integration.Sdk`; csproj pins
  `AdapterVersion`/`Version` 3.3.0 plus Assembly/File/Informational versions, PackageId
  `Cymulate.Integration.Sdk`, RootNamespace `Cymulate.Integration.Sdk`. (`git ls-tree origin/dev`)
- VALIDATED — Infra Client is 105 `.cs`, PackageId + RootNamespace + namespaces all
  `Cymulate.Integration.Client`, and its csproj carries **no** version pin.
- VALIDATED — Client has **no** `src/Cymulate.Integration.Client/Directory.Build.props`; version comes
  from the repo-root `Directory.Build.props` at `1.0.0-preview.4`, shared with `Cymulate.IntegrationInfra`.
- VALIDATED — `origin/dev`'s SDK contains **no** `Query/Plan`, `Query/Pipeline`, `Query/Persistence`, or
  `BaseQueryAdapter`; the 2026-07-08 absorption copied 105 `.cs` from ISB. Client's 33 extra files are
  therefore stale copies of files ISB has since deleted, **not** new Client features. The naive
  "Client is a superset" reading is REJECTED.
- VALIDATED — Client is missing the whole 3.2.1→3.3.0 delta: `ISiemRulesCapability`
  (`Contracts/IAdapterCapability.cs:50`), `AdapterCategory.SiemRules = 5` (`Enums/AdapterCategory.cs:59`),
  and the `AdapterCategory.SiemRules => "siemrules"` mapping (`Contracts/IAdapterExecutionContext.cs:435`).
  `grep -rn SiemRules` over Client returns nothing. Client cannot replace the SDK as-is without
  regressing this.
- VALIDATED — the `.Sdk`→`.Client` rename rationale is commit `0607e90` "Retire the SDK concept"
  (PR #7 `rename/sdk-to-client`), with versioning centralised by `bce7b15`. CHANGELOG records it as
  BREAKING, 355 qualified references across 195 files + 77 prose occurrences, and states explicitly
  there is "no compatibility shim or type forward". Success-criterion (4) is therefore already met at
  contract time; the executor confirms rather than hunts.
- VALIDATED — `AdapterLoadContext.IsSharedDependency()` shares by assembly *simple name*, hardcoding
  `Cymulate.Integration.Sdk` (line 123) and prefix-matching
  `Cymulate.IntegrationServiceBus.Domain*` (127) and `Infrastructure.Core*` (131).
  `TryLoadFromDefaultContext` deliberately ignores version. So the break is name-based, not version-based.
- VALIDATED — ISB has its own `nuget.config` at `src/Cymulate.IntegrationServiceBus/nuget.config`.
- VALIDATED — 12 ISB build files reference the SDK: the `.sln`, `Docker/Dockerfile.WebApi`, and csprojs
  for Domain, Infrastructure.AWS, Infrastructure.KeyVault, Application.Query, Sdk, Sdk.Samples,
  Tests/API.UnitTests, Tests/KeyVault.IntegrationTests, UnitTests/Cymulate.Integration.Sdk.UnitTests,
  UnitTests/Application.UnitTests.
- VALIDATED — `Domain/TypeForwards.cs` forwards 24 legacy Domain-era types into
  `Cymulate.Integration.Sdk.*`.

## OPEN — Phase 1 must resolve

- OPEN — Does the exact namespace-normalized drift count reconcile to the operator's expected ~22? Which
  side is each change on? (S2/S3. Report the true number; do not force it to 22.)
- OPEN — Where do the 33 Client-only `Query/Plan|Pipeline|Persistence|BaseQueryAdapter` types live in ISB
  today (likely `Applications/Cymulate.IntegrationServiceBus.Application.Query`)? Are Client's copies
  dead weight, or would they duplicate/conflict with ISB types once ISB references Client? (S4. This
  decides whether Client must *shed* files, not just gain them.)
- OPEN — Does "Client as the entrypoint into ISB" hold against ISB's actual loader/activator/registry
  and Infra's `Conducting/*`? Specifically: does `Conducting/*` assume it drives adapters in-process,
  while ISB drives them through an isolated collectible ALC? (S5. Prime suspect for a concept-level gap.)
- OPEN — Are `IIndicatorCapability` and `ICollectorCapability` (forwarded by `TypeForwards.cs`) present in
  Client? If absent, `TypeForwards.cs` cannot be retargeted as-is.
- OPEN — How many collector DLLs exist in S3, and can every one be rebuilt? Any that cannot forces
  candidate (a)(iii). Is a mixed-version window unavoidable during rollout? (S7, evidence for D-A.)
- OPEN — Does ISB's `nuget.config` carry the CodeArtifact source `cym-dom/cym-repo-nuget` and a
  `packageSourceMapping` that would admit `Cymulate.Integration.Client`? (S7, evidence for D-B.)
- OPEN — Exactly what does `Docker/Dockerfile.WebApi` do with the Sdk path, and does the build context
  admit a sibling-repo path at all? (S7, evidence for D-B. Expected to rule out ProjectReference.)
- OPEN — The Infra root props references a "local-install test plan" for proving the pre-release line in
  an end-to-end composed run. `grep -rl local-install` over Infra `.md` files found nothing. Locate it
  or record its absence — it is the natural seed for Phase 2's pre-deployment verification.
- OPEN — Is `Cymulate.Integration.Sdk.Samples` (which the absorption deliberately left in ISB, consumed
  only by ISB's API.UnitTests) affected by the swap?

## REJECTED

- REJECTED — "Client is a superset of the SDK and only adds features." Its extra 33 files are stale
  copies of ISB deletions, and it is missing ISB's 3.3.0 SiemRules delta. Drift runs both ways.
- REJECTED — "A zero-code `TypeForwardedTo` shim named `Cymulate.Integration.Sdk` can bridge old
  collectors to Client." Type forwarding cannot rename a namespace (see constraints.md).
- REJECTED — "Client inherits the SDK's 3.2.1/3.3.0 version lineage." It joined `1.0.0-preview.4`; the
  3.x line was deliberately abandoned with the PackageId.
