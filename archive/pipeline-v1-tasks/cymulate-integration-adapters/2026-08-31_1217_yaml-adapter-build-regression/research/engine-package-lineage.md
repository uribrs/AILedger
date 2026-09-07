# Engine/package lineage evidence

## Conclusion

`Cymulate.Integration.Yaml.Engine` `1.0.0-preview.19` was published from immutable engine commit `88be469f9af93754b356fda7e7c39da3dfac4aef` on the `master` lineage. That commit contains the preview.19 work merged to `dev` at `9a05fd04c411d6cbd2a353abf9af3c88f7e0a678`, including version-intent commit `cf5e6c1999bf2704df9de14b63b4a967511a1ca7`. It does **not** contain `OperationConfig.HandshakeFor` or the YAML key `handshake_for`.

The contract was introduced five days later by `ef407cf8030fa2cbf838e5f2d113a29ac30eb32f` and merged to engine `dev` in PR #51 by `1b765b9b4bde785aa1368bb8e79d1ea41df82dc5`. A clean restore from CodeArtifact on 2026-08-31 produced byte-for-byte the same preview.19 package and DLL as the existing cache, and neither artifact contains the member. Therefore engine source has the required contract, but no evidence places it in published preview.19; in fact, the package provenance and binary prove it is absent.

## Immutable chronology

All times below are the commits' ISO-8601 author/committer times. Author and committer times are identical except that the merge commits name GitHub Enterprise as committer.

| Event | Immutable commit | Time (`+03:00`) | Evidence and role |
|---|---|---:|---|
| Preview.19 version intent authored | `cf5e6c1999bf2704df9de14b63b4a967511a1ca7` | 2026-08-24 17:05:32 | Changes the default suffix to `preview.19`; parent `7b32f0fc8e2c37235c7151f6aefbc49909dbe422`. This is intent, not publication proof. |
| Preview.19 work merged to `dev` | `9a05fd04c411d6cbd2a353abf9af3c88f7e0a678` | 2026-08-25 15:04:07 | Merge parents `8c861602106a43328918a2ae03af0a98d4c028f3` and `06fa38d08cfe0cdbf7d27329f0311618f2f2b660`; `cf5e6c1` is an ancestor through the second parent. |
| Published package source / PR #49 merge to `master` | `88be469f9af93754b356fda7e7c39da3dfac4aef` | 2026-08-25 15:13:06 | Merge parents `4b1ba27a7afdfd40e2712b8080efacf45a1a07ac` and `9a05fd04c411d6cbd2a353abf9af3c88f7e0a678`. The restored `.nuspec` embeds this exact repository commit. The archive entries were packed at 2026-08-25 12:16 UTC (15:16 local), three minutes after the merge. |
| `HandshakeFor` authored | `ef407cf8030fa2cbf838e5f2d113a29ac30eb32f` | 2026-08-30 13:53:47 | Parent `14d4a0ff5def594106b4ee83e5fbf0f10d9ad49b`; this is the first commit found by both `git log -SHandshakeFor` and `git log -Shandshake_for`. |
| Engine PR #51 merged to `dev` | `1b765b9b4bde785aa1368bb8e79d1ea41df82dc5` | 2026-08-30 14:01:33 | Merge parents `9a05fd04c411d6cbd2a353abf9af3c88f7e0a678` and `ef407cf8030fa2cbf838e5f2d113a29ac30eb32f`; merge subject is `Merge pull request #51 from cymulate-rnd/fix/empty-mapping-passthrough`. |

Ancestry checks all returned exit code `0` for:

```text
cf5e6c1 -> 9a05fd0 -> 88be469 -> ef407cf -> 1b765b9
```

The reverse check `1b765b9 -> 88be469` returned `1`, as expected. `git merge-base 88be469 ef407cf` is exactly `88be469`, proving the `HandshakeFor` authoring commit is based on, and later than, the published preview.19 source.

## Complete contract propagation introduced by `ef407cf`

`git grep -n -E 'HandshakeFor|handshake_for' ef407cf -- .` finds exactly these production locations:

- `src/Cymulate.Integration.Yaml.Engine/Definition/Contracts/Models/OperationConfig.cs:70-71` — `[YamlMember(Alias = "handshake_for")]` and `public List<string>? HandshakeFor { get; set; }`.
- `src/Cymulate.Integration.Yaml.Engine/Definition/Schemas/integration.schema.json:532` — schema property `handshake_for`, an array with at least one unique value restricted to `collectors`, `queries`, `indicators`, or `siemRules`.
- `src/Cymulate.Integration.Yaml.Engine/Compile/Logic/PlanBinder.cs:80` — copies `HandshakeFor = op.HandshakeFor` into the bound plan.
- `src/Cymulate.Integration.Yaml.Engine/Compile/Logic/TaskInstantiation.cs:128` — copies `HandshakeFor = op.HandshakeFor` when instantiating a task.

The same grep against package-source commit `88be469` returns no matches. A path-limited diff confirms `88be469` and `9a05fd0` have identical versions of all four contract-propagation files, while `88be469` and `ef407cf` differ in `OperationConfig.cs`.

## Published package proof and fresh-restore verification

The adapter centrally selects `Cymulate.Integration.Yaml.Engine/1.0.0-preview.19`. To avoid treating a mutable local branch or a version string as publication proof, the package was freshly restored into an isolated package root:

```sh
NUGET_PACKAGES=/private/tmp/yaml-engine-lineage-fresh-20260831 \
  dotnet restore \
  src/Cymulate.Integration.Adapters/YamlAdapter/Cymulate.Integration.Adapters.YamlAdapter/Cymulate.Integration.Adapters.YamlAdapter.csproj \
  --force --no-cache --verbosity minimal
```

The command completed successfully and reported the YamlAdapter project restored. Its fresh `.nupkg.metadata` names the CodeArtifact source and content hash:

```text
source: https://cym-dom-118330362824.d.codeartifact.us-east-1.amazonaws.com/nuget/cym-repo-nuget/v3/index.json
contentHash: rO0ak8OKcr1FT59RmMb3D/6yZjdzzJM9Ryp1WpVmRLTTbo5FddihPFma/aCJPdiQT7WmMDjvIUkEhWwwzdvKow==
```

The freshly restored `.nuspec` states:

```xml
<version>1.0.0-preview.19</version>
<repository type="git"
  url="https://cymulate-corp.ghe.com/cymulate-rnd/yaml-adapter-engine"
  commit="88be469f9af93754b356fda7e7c39da3dfac4aef" />
```

Fresh and pre-existing cache SHA-256 hashes are identical:

| Artifact | Fresh isolated restore | Existing global cache |
|---|---|---|
| `.nupkg` | `52daaba1b4b49c6201054775aa0947c9fdeca14409a05134bb7e54a351017cd7` | `52daaba1b4b49c6201054775aa0947c9fdeca14409a05134bb7e54a351017cd7` |
| `lib/net8.0/Cymulate.Integration.Yaml.Engine.dll` | `58aee1b88381257899396d6116136429727768d950e9b0e62ae5c8b8dd763779` | `58aee1b88381257899396d6116136429727768d950e9b0e62ae5c8b8dd763779` |

Both `strings <fresh DLL> | rg 'HandshakeFor|handshake_for'` and `rg 'HandshakeFor|handshake_for' <fresh XML docs>` returned no matches. By contrast, the same DLL contains preview.19-specific symbols including `StreamIdleTimeoutMs` and `ICircuitBreakerStore`, which is consistent with its embedded `88be469` provenance rather than a stale or unrelated package. Since a CLR property name and its accessor names are stored in assembly metadata/string data, absence from the DLL plus absence from its generated public XML documentation is direct evidence that the freshly restored binary has no `OperationConfig.HandshakeFor` member.

## Attention item R2 — resolved

R2 warned against using `Directory.Build.props` as publication proof because CI may override `VersionSuffix`. That warning is borne out by the file's own comment and history, so the conclusion does not rely on it.

R2 is resolved using three independent artifact facts:

1. The CodeArtifact-restored package's `.nuspec` identifies its source as exact commit `88be469`.
2. Git proves `88be469` precedes `ef407cf`, and the package-source tree has no `HandshakeFor`/`handshake_for` occurrence.
3. The fresh package and DLL hashes equal the prior cache, and direct inspection of the fresh DLL/XML documentation finds no member.

Thus preview.19 is conclusively the pre-`HandshakeFor` publication even though `Directory.Build.props` still says `preview.19` at `ef407cf` and `1b765b9`. The unchanged suffix after the source change is not evidence of a republish; NuGet package versions are immutable and the feed returned the earlier `88be469` artifact.

## Commands used

Commands were run read-only against `/Users/user/Dev/yaml-adapter-engine`, except for the isolated NuGet restore under `/private/tmp` and this evidence file.

```sh
git -C /Users/user/Dev/yaml-adapter-engine show --no-patch --format=fuller \
  cf5e6c1 9a05fd0 88be469 ef407cf 1b765b9
git -C /Users/user/Dev/yaml-adapter-engine rev-list --parents -n 1 <commit>
git -C /Users/user/Dev/yaml-adapter-engine merge-base --is-ancestor <older> <newer>
git -C /Users/user/Dev/yaml-adapter-engine merge-base 88be469 ef407cf
git -C /Users/user/Dev/yaml-adapter-engine log --all --reverse \
  --format='%H %aI %cI %s' -S'HandshakeFor' -- .
git -C /Users/user/Dev/yaml-adapter-engine log --all --reverse \
  --format='%H %aI %cI %s' -S'handshake_for' -- .
git -C /Users/user/Dev/yaml-adapter-engine grep -n \
  -E 'HandshakeFor|handshake_for' 88be469 -- .
git -C /Users/user/Dev/yaml-adapter-engine grep -n \
  -E 'HandshakeFor|handshake_for' ef407cf -- .
git -C /Users/user/Dev/yaml-adapter-engine diff --quiet 88be469 9a05fd0 -- \
  src/Cymulate.Integration.Yaml.Engine/Definition/Contracts/Models/OperationConfig.cs \
  src/Cymulate.Integration.Yaml.Engine/Compile/Logic/PlanBinder.cs \
  src/Cymulate.Integration.Yaml.Engine/Compile/Logic/TaskInstantiation.cs \
  src/Cymulate.Integration.Yaml.Engine/Definition/Schemas/integration.schema.json
unzip -p <fresh-preview.19.nupkg> Cymulate.Integration.Yaml.Engine.nuspec
unzip -l <fresh-preview.19.nupkg>
sha256sum <fresh-and-cache-nupkgs-and-dlls>
strings <fresh-preview.19-dll> | rg -n 'HandshakeFor|handshake_for'
rg -n 'HandshakeFor|handshake_for' <fresh-preview.19-xml-docs>
```
