# Adapter history: YamlAdapter regression boundary

## Conclusion

The adapter-side regression begins in authored commit `54aef287745c83018fb3a9d65245a3401fe53145`, whose direct parent is `59ba33517f15b3283dbf82545c6b23927f883a64`. The parent builds; the child fails with three `CS1061` errors because the child first reads `OperationConfig.HandshakeFor` while retaining the central `Cymulate.Integration.Yaml.Engine` pin at `1.0.0-preview.19`.

Pull request #350 is not the breaking PR. Its merge commit, `a5b2e60bebb610f716d547eafe4b97210b4c6c17`, has `59ba3351` as its second parent, contains no `HandshakeFor` read in YamlAdapter, resolves engine package `1.0.0-preview.19`, and builds successfully.

Pull request #352 is the breaking PR on `dev`. Its merge commit, `80fad2bd521701096aa164ced508b913be7683c0`, has `a5b2e60b` as first parent and `09e6d36e` as second parent. `09e6d36e` contains authored commit `54aef287`; the relevant YamlAdapter source is unchanged between `54aef287`, `09e6d36e`, and `80fad2bd`. Thus:

- first broken authored tree: `54aef287`;
- last passing direct parent of that authored change: `59ba3351`;
- last passing `dev` state immediately before the PR merge: `a5b2e60b`;
- first broken `dev` merge: `80fad2bd` (PR #352).

This explicitly resolves attention item R1: `54aef287` is the authored consumer delta, not the PR merge; `80fad2bd` is the merge that introduced that already-broken delta to `dev`.

## Immutable commit and PR evidence

Commit metadata was read with:

```text
git show -s --format='commit=%H%nparents=%P%nauthor=%an <%ae>%nauthor_date=%aI%ncommitter=%cn <%ce>%ncommit_date=%cI%nsubject=%s%n' <ref>
```

| role | commit and subject | author / committer | timestamps (author = commit) | parents |
|---|---|---|---|---|
| PR #350 merge | `a5b2e60bebb610f716d547eafe4b97210b4c6c17`; `Merge pull request #350 from cymulate-rnd/fix/handshakeyaml` | Anatoli Pakastorvsky `<anatolip@cymulate.com>` / GitHub Enterprise `<noreply@ghe.com>` | `2026-08-27T12:21:01+03:00` | first `b14d6d64e1f317bd6c5fae71d04f8ccca6c9e481`; second `59ba33517f15b3283dbf82545c6b23927f883a64` |
| PR #350 authored tip | `59ba33517f15b3283dbf82545c6b23927f883a64`; `fix` | theavengers `<anatolip@cymulate.com>` / same | `2026-08-27T12:19:52+03:00` | `b14d6d64e1f317bd6c5fae71d04f8ccca6c9e481` |
| breaking authored delta | `54aef287745c83018fb3a9d65245a3401fe53145`; `fix(yaml-adapter): run only the handshake probes for the requesting capability` | theavengers `<anatolip@cymulate.com>` / same | `2026-08-30T13:53:57+03:00` | direct parent `59ba33517f15b3283dbf82545c6b23927f883a64` |
| PR branch reconciliation | `09e6d36eae5d196c05ca746eb804ad83a2c46219`; `Merge branch 'dev' into fix/handshakeyaml` | theavengers `<anatolip@cymulate.com>` / same | `2026-08-30T13:57:10+03:00` | first `54aef287745c83018fb3a9d65245a3401fe53145`; second `a5b2e60bebb610f716d547eafe4b97210b4c6c17` |
| PR #352 merge | `80fad2bd521701096aa164ced508b913be7683c0`; `Merge pull request #352 from cymulate-rnd/fix/handshakeyaml` | Anatoli Pakastorvsky `<anatolip@cymulate.com>` / GitHub Enterprise `<noreply@ghe.com>` | `2026-08-30T14:00:02+03:00` | first `a5b2e60bebb610f716d547eafe4b97210b4c6c17`; second `09e6d36eae5d196c05ca746eb804ad83a2c46219` |

Ancestry checks all exited `0` (true):

```text
git merge-base --is-ancestor 59ba3351 54aef287
git merge-base --is-ancestor 54aef287 80fad2bd
git merge-base --is-ancestor a5b2e60b 80fad2bd
```

The source boundary is direct rather than inferred: `git rev-parse 54aef287^` returned `59ba33517f15b3283dbf82545c6b23927f883a64`. `git diff 59ba3351 54aef287` adds two production reads of `OperationConfig.HandshakeFor` in `Definitions/ConnectionValidator.cs` (the compiler reports them as three member-access errors at lines 184 and 194). The same diff also forwards `adapterCategory` and adds scoped-handshake tests/fixtures.

The following comparisons exited `0`, proving there is no later relevant source rewrite hiding the boundary:

```text
git diff --exit-code 54aef287 09e6d36e -- \
  src/Cymulate.Integration.Adapters/YamlAdapter \
  src/Cymulate.Integration.Adapters/UnitTests/YamlAdapter

git diff --exit-code 09e6d36e 80fad2bd -- \
  src/Cymulate.Integration.Adapters/YamlAdapter \
  src/Cymulate.Integration.Adapters/UnitTests/YamlAdapter
```

`git diff --name-only 59ba3351 a5b2e60b` over those same YamlAdapter paths produced zero files, so PR #350's merge tree and its authored tip are equivalent for this build boundary.

## Clean export and package-resolution method

No branch or product working tree was changed. Each immutable commit was exported without Git metadata or pre-existing `bin`/`obj` state:

```text
mkdir -p /private/tmp/yaml-adapter-history-20260831/<ref>
git archive <ref> | tar -x -C /private/tmp/yaml-adapter-history-20260831/<ref>
```

The initial build in every export performed restore. Its generated production `obj/project.assets.json` was queried with:

```text
jq -r '.libraries | keys[] | select(startswith("Cymulate.Integration.Yaml.Engine/"))' \
  src/Cymulate.Integration.Adapters/YamlAdapter/Cymulate.Integration.Adapters.YamlAdapter/obj/project.assets.json
```

For all four refs (`59ba3351`, `a5b2e60b`, `54aef287`, `80fad2bd`) the resolved identity was exactly:

```text
Cymulate.Integration.Yaml.Engine/1.0.0-preview.19
```

Each ref also pins `PackageVersion Include="Cymulate.Integration.Yaml.Engine" Version="1.0.0-preview.19"` in `src/Cymulate.Integration.Adapters/Directory.Packages.props`. The assets files identify `/Users/user/.nuget/packages/` as the package folder. Builds used .NET SDK `10.0.301` and target the repository's .NET 8 projects.

## Reproduced build boundary

The restore/build entry point was deliberately limited to the YamlAdapter test project, which necessarily builds the production adapter project:

```text
dotnet build \
  src/Cymulate.Integration.Adapters/UnitTests/YamlAdapter/Cymulate.Integration.Adapters.YamlAdapter.Test/Cymulate.Integration.Adapters.YamlAdapter.Test.csproj \
  --disable-build-servers --nologo --verbosity minimal
```

The production-only result was then confirmed from the restored export with:

```text
dotnet build \
  src/Cymulate.Integration.Adapters/YamlAdapter/Cymulate.Integration.Adapters.YamlAdapter/Cymulate.Integration.Adapters.YamlAdapter.csproj \
  --no-restore --disable-build-servers --nologo --verbosity minimal
```

| ref | role | test-project build | production-only confirmation | resolved engine |
|---|---|---:|---:|---|
| `59ba3351` | direct parent / PR #350 authored tip | exit `0`; 0 warnings, 0 errors | exit `0`; 0 warnings, 0 errors | `1.0.0-preview.19` |
| `a5b2e60b` | PR #350 merge / last passing `dev` state | exit `0`; 0 warnings, 0 errors | exit `0`; 0 warnings, 0 errors | `1.0.0-preview.19` |
| `54aef287` | first authored consumer tree | exit `1`; 0 warnings, 3 errors | exit `1`; 0 warnings, 3 errors | `1.0.0-preview.19` |
| `80fad2bd` | PR #352 merge / first broken `dev` state | exit `1`; 0 warnings, 3 errors | exit `1`; 0 warnings, 3 errors | `1.0.0-preview.19` |

Both failing refs report the same errors:

```text
ConnectionValidator.cs(184,37): error CS1061: 'OperationConfig' does not contain a definition for 'HandshakeFor'
ConnectionValidator.cs(194,33): error CS1061: 'OperationConfig' does not contain a definition for 'HandshakeFor'
ConnectionValidator.cs(194,65): error CS1061: 'OperationConfig' does not contain a definition for 'HandshakeFor'
```

This is a compile-time package-contract failure, so the connection test itself cannot start at the broken refs.

## Adapter-side causal statement

PR #350 successfully introduced the earlier handshake behavior while compiling against engine `preview.19`. Three days later, authored commit `54aef287` extended the adapter to consume `HandshakeFor` but did not change the engine package pin. PR #352 merged that tree onto `dev` as `80fad2bd`; the merge preserved both the new member reads and the old package pin. Therefore PR #352 caused the adapter repository's build regression. Whether/when engine source implemented the member and whether a compatible package was published are separate producer-side facts covered by the engine-package-lineage evidence.
