
---

## Phase 2 — implementation + review (2026-08-03)

Branches (both UNCOMMITTED): ISB `refactor/adopt-integration-client` off dev@769cbaa1 ·
IntegrationInfra `carry/client-siemrules-checksum` off dev@340be07.

5 implementation workers, then 2 verifiers + 3 code-reviewers (reviewers given minimal context:
no plan, no contract, no verifier output, not told of each other).

**Verdicts:** V-infra READY TO PUBLISH (13/13) · CR-infra no blockers, 4 Important ·
V-isb READY WITH GAPS · CR-isb-1 1 blocker + 5 Important · CR-isb-2 1 blocker + 6 Important.

### The review's most valuable catch — the freeze named the wrong value
`[EnumMember]` is **decorative** for `AdapterCategory`. What travels is the **C# member name**, via
`category.ToString()` into `Metadata["AdapterCategory"]`, read back by `Enum.TryParse`. Six reader sites;
**four pass `ignoreCase: true`, two do not** — `Infrastructure.Kafka/KafkaProducer.cs:274` and
`Infrastructure.RabbitMQ/RabbitMqPublisher.cs:655`. So **exact casing is contract**, and recasing would
keep four readers working while silently breaking two — a partial failure, harder to diagnose than a total
one. Every ISB publisher uses `JsonStringEnumConverter(CamelCase)`, which on net8.0 ignores the attribute
and would emit `"siemRules"`. Now backed by a passing test, not by argument.

### Findings resolved
- ISB detection Strategy 1 is **dead code**: `AdapterManager.cs:643` reassigns
  `adapter = adapter.ApplyDecorators(...)`, and both decorators declare only `: IIntegrationAdapter`, so
  `GetInterfaces()` never yields `IAdapter`/`ICollectorAdapter`. Pre-existing; comment made honest, name
  widening kept, deadness ticketed.
- **Three** instances of the DLL-selection defect class in `S3AdapterLoader.cs`, all closed:
  `FindMainAdapterDll` last-resort, `ExtractDllFromZipAsync` fallback, and its primary filter (which
  lacked the `Sdk` exclusion, making the legacy `Cymulate.Integration.Adapters.Sdk.dll` selectable).
- Stale docs fixed: `CLAUDE.md` layout + ports table + the false offline-restore promise;
  `.claude/skills/create-mediator-handler/SKILL.md` (was generating non-compiling global usings);
  ISB `Directory.Build.props` per-adapter versioning strategy.
- `Domain 1.0.90` republish-collision fear **refuted** — highest published is `1.0.57`.
- CR-isb-1's `SiemRules` blocker **resolved**: it reflected preview.3/.4; preview.5 adds it.

### Open — operator decision
`Domain` is `IsPackable` at stable `1.0.90` and now depends on prerelease `preview.5` → `NU5104`
(warning here, error under `-warnaserror`). Accept until Client is stable / bump `CentralVersion` to a
prerelease / `IsPackable=false`.

### Handoff (blocking, in order)
1. `git add` the 3 untracked Infra files — `git commit -a` skips them; one replaces coverage ISB deletes.
2. Publish Client `preview.5` — gates ISB compiling at all.
3. Pipeline CodeArtifact token into the Docker build, keyed **exactly** `cym-dom/cym-repo-nuget`
   (NuGet matches credentials by source key name); prefer `--mount=type=secret` over `ARG`.
4. One `docker build --no-cache` against `dev` — 3 csprojs in the API closure are not COPYed, so the
   Dockerfile is likely coasting on BuildKit cache, and the new `nuget.config` COPY invalidates it.

### Follow-up tickets (deliberately excluded from these branches)
- **Union-only** single-homing of the two share policies. `PreloadAdapterDependencies` calls
  `LoadFromAssemblyPath` directly, which never consults the `Load` override — so every rule in
  `IsSharedDependency` is unenforceable there and `IsSharedAssembly` is the only guard on the path where
  force-loading happens. The `AWSSDK.*` divergence recreates the split-context IRSA failure that
  `AdapterLoadContext.cs:154-162` exists to prevent. Collapsing onto the narrower predicate is
  **actively harmful**.
- Full-path-vs-filename matching in both DLL selectors (`f.Contains(...)` over the whole path).
- Decorator masking Strategy-1 collector detection.
- `AdapterFilePattern` divergence degrading the ETag downgrade guard.
- Infra's 3 pre-existing `CS1574` warnings (clean-build only).

### Verification state
Infra: build clean, **353 tests passing** (from 330), zero new warnings.
ISB: **nothing compiler-verified** — `preview.5` unpublished, restore fails only on that package (87 ×
NU1102, no other diagnostic class). `AdapterManager.cs:3049-3051` is the likeliest syntax slip. D's
contract pin was validated by compiling the real test file against a surrogate project and
mutation-testing it 5 ways (11 pass; each mutation caught).
