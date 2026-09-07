# Code Review — Cymulate.Integration.Yaml.Engine

**Classification:** public contract / SDK-level code (shipping NuGet package) · **Risk: High** (auth, retries, pagination state, network IO, temp-file persistence) · **Stack:** C# / .NET 8

**Baseline verified:** `dotnet build` clean (0 warnings), `dotnet test` 699/699 passing, `dotnet pack` produces a valid `.nupkg`. The move and reshape landed cleanly; the findings below are about what ships, not about whether it compiles.

---

## Major

### 1. `Microsoft.Extensions.*` pinned at 10.0.9 on a `net8.0` package
`Directory.Packages.props:18-20`. The nuspec emits `>= 10.0.9` for DependencyInjection.Abstractions, Http.Polly and Logging.Abstractions. Every consumer of this package on net8.0 is dragged onto the 10.x assemblies — a library sets the **lowest** compatible floor, not the version its author happened to build with. The in-file comment already identifies this; it is listed here because it is the single most consequential fact about the published artifact and it is still live. **Fix:** move the Engine group to `8.0.x` floors. Local patch. Do it before the first publish — raising a floor later is fine, lowering it is not.

### 2. `Microsoft.Extensions.DependencyInjection.Abstractions` is unreferenced
`Cymulate.Integration.Yaml.Engine.csproj:22`. Zero occurrences of `IServiceCollection`, `IServiceProvider` or any DI abstraction type in `src/` — verified by grep across the whole tree. It is nonetheless in the nuspec dependency group, forcing a version constraint on consumers for nothing. **Fix:** delete the `PackageReference` (keep the `PackageVersion` entry only if a future DI extension is imminent). Local patch.

### 3. Temp NDJSON spill files are orphaned on every failure path
`IntegrationEngine.cs:1259-1265` and `1349-1359`. Both spill paths create a file under `%TEMP%/cymulate-results` and never delete it. On the success path the host learns the path via `OperationResult.ResultFilePath` and can clean up. On **every** failure path — HTTP error (`:452`), status-code fail (`:616`), completeness shortfall (`:806`), the catch-all (`:882`) — the result carries no `ResultFilePath`, so the file is unreachable and permanent. A long-running collector pod accumulates these until the disk fills. Two compounding problems in the same code:
- The name is `{integration}_{operation}_{yyyyMMdd-HHmmss-fff}.ndjson` with only spaces stripped. Two concurrent runs of the same operation in the same millisecond collide, and `new StreamWriter(path, append: false)` then either throws `IOException` or silently truncates the other run's spill.
- `integrationName` is a definition key flowing unsanitized into `Path.Combine`. A key containing `../` escapes the results directory.

**Fix:** append a `Guid.NewGuid().ToString("N")[..8]` to the filename, sanitize with `Path.GetInvalidFileNameChars()`, use `DateTime.UtcNow`, and delete the spill in the `finally` unless it is being returned in a successful `OperationResult`. Local patch, ~10 lines.

### 4. `_definitions` is an unsynchronized `Dictionary` with a public mutator
`IntegrationEngine.cs:50, 84, 87, 93-96, 177`. `InjectDefinition` writes to a plain `Dictionary` — its own doc comment says the caller is a UI mutating definitions in-memory — while `ExecuteOperationCoreAsync` reads it (`:177`) and `GetLoadedIntegrations` enumerates it (`:84`). The engine's shape (definitions loaded once in the constructor, `ILogger<T>` + `IHttpClientFactory` injected) makes a DI singleton the expected registration. A concurrent write during a read on `Dictionary` is not merely a lost update: it can produce a torn bucket chain and an infinite loop inside `TryGetValue`. **Fix:** `ConcurrentDictionary<string, IntegrationDefinition>` — `TryGetValue`/indexer/`Keys` all keep working unchanged. Local patch.

### 5. `HmacAuthenticator` does not compute an HMAC, and has zero tests
`Authentication/Logic/HmacAuthenticator.cs:119`. `Compute` is `SHA256.HashData(secret ‖ nonce ‖ timestamp)` — a plain digest of a concatenation, not `HMACSHA256`. For Cortex XDR advanced keys that is the correct vendor scheme, so today's behavior is right. The hazard is the naming: the class, the config type (`HmacConfig`), the YAML discriminator (`hmac`) and the comment *"Only sha256 is implemented; the field is reserved for future algorithms"* (`:118`) all read as "the algorithm knob picks the MAC". The next vendor that needs a real keyed MAC will be added under this type and silently mis-signed. This is also the **only authenticator of the eight with no test coverage at all** — grep for `hmac` across `tests/` returns only AWS SigV4 string literals. A signature primitive shipping in a public package with no known-answer vector is the wrong thing to be untested. **Fix:** rename the algorithm concept in the doc comment to state explicitly that this is a digest-of-concatenation scheme (not a MAC), and add a fixed-input test locking the hex output for each of the three encodings and both timestamp formats. Local patch plus tests; no refactor needed.

### 6. `1.0.0` conflicts with the planned public-surface narrowing
`Directory.Build.props:13` sets `VersionPrefix 1.0.0` and the library is packable now. `CLAUDE.md` ("Default to `internal`") and `ARCHITECTURE.md` both plan to trim the surface in phase 3, and `ARCHITECTURE.md:269` says that phase "can land as patch releases afterward". It cannot: `public` → `internal` is a compile-break for consumers and a major-version event under SemVer. Publishing `1.0.0` today freezes ~130 public types as a support commitment. **Fix:** ship the first releases as `1.0.0-preview.N` (or `0.x`) and cut `1.0.0` after phase 3 trims the surface. One-line change now, unrecoverable later.

---

## Minor

### 7. Package metadata is incomplete for first publish
`dotnet pack` warns *"missing a readme"*, and the produced nuspec has no `<license>`. The package contains exactly one file: `lib/net8.0/*.dll`. Specifically missing:
- `GenerateDocumentationFile` — the codebase has genuinely good `///` docs on the public surface (`IIntegrationEngine`, `TemplateEngine`, `IExecutionSink`, every config record) and all of it is discarded. Consumers get no IntelliSense.
- `PackageReadmeFile` — `README.md` exists at the root and is not packed.
- `PackageLicenseExpression` — no license element, no `LICENSE` file in the repo.
- `IncludeSymbols` / `SymbolPackageFormat=snupkg` — no symbols published.
- SourceLink is **not active** despite `PublishRepositoryUrl` and `EmbedUntrackedSources` being set (`Directory.Build.props:20, 28`): no `.sourcelink.json` is generated in `obj/Release/net8.0/`. The repository is GitHub Enterprise (`cymulate-corp.ghe.com`); the SDK's bundled SourceLink only recognizes `github.com` unless the host is declared via a `SourceLinkGitHubHost` item. As configured those two properties do nothing.

Also worth noting: `nuget.config` describes CodeArtifact as "a push target for CI", but there is no CI configuration in the repo at all, and `ContinuousIntegrationBuild` (`:27`) is gated on `$(CI) == 'true'`, which will need verifying against whatever runner is chosen.

### 8. `.editorconfig` style severities are inert
`Directory.Build.props:8` sets `EnforceCodeStyleInBuild=false`, so `csharp_style_namespace_declarations = file_scoped:warning` (`.editorconfig:19`) never fires at build time. The convention holds today by discipline only. Either turn enforcement on for the rules you care about, or drop the `:warning` suffix so the file stops implying a gate that does not exist.

### 9. The stated internals-testing strategy is not wired up
`CLAUDE.md` says "Tests reach internals via `InternalsVisibleTo`". There is no `InternalsVisibleTo` anywhere in the repo — `YamlToJsonNodeEdgeTests.cs:11` even documents working around its absence. Seven `internal` types (`BoundedKeyCache`, `BoundedKeyGroupCache`, `MergeEnrichment`, `MergeEnrichmentSink`, `MergeShapeProjector`, `WorkflowWaitRequested`, `YamlToJsonNode`) are reachable only through public entry points. That is a defensible choice, but it is the opposite of the documented one, and it becomes a real constraint once phase 3 makes most of the codebase internal. Decide now: add `<InternalsVisibleTo Include="$(AssemblyName).Tests" />`, or amend `CLAUDE.md`.

### 10. Conspicuous unit-test gaps
`EngineFailureClassifier` (311 lines — the retry / defer / skip / fail decision brain) and `DelayResolver` have no direct test file; both are exercised only end-to-end through `EngineErrorRuleTests` with a fake handler. Both are pure functions of their inputs and are the cheapest things in the repo to test directly. Today, covering one more rule-matching combination costs a whole engine fixture. Add `Resilience/EngineFailureClassifierTests.cs` driven by `(status, body, headers, config)` tuples.

### 11. Test layout does not mirror the source concepts
`Execution/WorkflowRunnerTests.cs` and `Execution/MergeIntoTests.cs` (2,098 lines) test the `Workflow` concept; there is no `tests/…/Workflow/`. `ModelRecordTests.cs` and `ModelRecordEdgeTests.cs` sit at the test root — residue of the deleted `Models/` bucket, now testing types that live in `Definition`, `Execution`, `Pagination` and `Diagnostics`. Given that the whole point of the reshape was "read a concept by opening one directory", the test tree should follow. Mechanical, zero risk.

### 12. Sync-over-async on the request path
`IntegrationEngine.cs:1957` (`CloneRequest`) and `AwsSigV4Authenticator.cs:194` both do `ReadAsByteArrayAsync().GetAwaiter().GetResult()`. `CloneRequest` runs on every page and every Polly retry attempt. The content is always locally-built `StringContent`, so the task is already completed and there is no deadlock or thread-pool blocking in practice — the comment at `:1955` says as much. It is still a pattern that becomes a hang the day someone passes a stream-backed body. `ReadAsByteArrayAsync()` on a completed task can be replaced by making the two call sites async, or by buffering once into a `byte[]` at `BuildJsonContent` time and cloning from that.

---

## Observations (no action required)

- **`HttpResponseMessage` used after `Dispose()`.** `IntegrationEngine.cs:520` disposes `response`, then `:710` and `:765` read `response.Headers`, and `:858` reads `lastResponse.StatusCode`. Neither property checks the disposed flag in the current runtime, so this works and the tests confirm it — but it is undocumented behavior. `SnapshotHeaders` is already called at `:519`; passing that snapshot to `AdvancePagination` and `TryPrefetchNextPageAsync` would remove the dependence on an implementation detail. `LinkHeaderPaginator` is the only consumer that needs the real header collection.
- **`AuthManager` is constructed per operation call** (`IntegrationEngine.cs:227`) and disposed in the `finally`, so its class-level doc describing a shared cross-caller cache with concurrent-refresh coalescing (`AuthManager.cs:6-18`) never applies as written. The practical consequence: a workflow with N stages issues N OAuth2 token requests, one per `ExecuteStageAsync`. That may well be intentional isolation; the doc should say so.
- **`DateTime.Now` in `HttpTraceEntry`** (six authenticators plus `IntegrationEngine.cs:1021`). Local time with no offset, from pods in arbitrary time zones, in a diagnostic record that is presumably correlated across systems. `DateTimeOffset.UtcNow` costs nothing.
- **`WorkflowRunner` depends on the concrete `IntegrationEngine`** (`:33`) rather than `IIntegrationEngine`, because `ExecuteStageAsync` is not on the interface. Already identified in `ARCHITECTURE.md:243` as phase-3 work. Correctly diagnosed; no comment beyond agreement.
- **The dependency rule holds.** I spot-checked the direction claim: no concept's `Logic` references `Execution/Logic` or `Workflow/Logic` except `Workflow → Execution`, and `Definition/Logic`'s import of `…Workflow` touches only `StageConfig`/`MergeIntoConfig`. The embedded-resource `LogicalName` (`csproj:36`) matches `SchemaResourceName` (`IntegrationSchemaValidator.cs:19`) exactly, and the load path throws with the available resource names on mismatch rather than failing silently — that is the right defensive choice for a resource whose name is not build-checked.
- **`BoundedKeyCache` FIFO logic is correct** under overwrite-then-evict, including the re-add-after-eviction case. No action.

## Nit

- `IntegrationEngine.cs:407-408`: `else` followed by an unbraced `try` block. Legal, but the indentation makes the prefetch branch look like it falls through into the try. Add braces.
