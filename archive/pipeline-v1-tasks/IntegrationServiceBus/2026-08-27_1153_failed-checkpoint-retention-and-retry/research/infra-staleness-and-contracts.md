# Infra Staleness and Contracts (W3)

Read-only recon. Every claim below carries a `path:line`. Anything not read is labelled `SPECULATION:`.

No `CLAUDE.md` or `RULES.md` exists in `/Users/user/Dev/IntegrationInfra` (checked; absent). The repo's
own docs are `ARCHITECTURE.md`, `DESIGN.md`, `CHANGELOG.md`, `docs/`.

---

## 1. Staleness threshold definition

**Definition site:** `/Users/user/Dev/IntegrationInfra/src/IntegrationInfra/FaultGovernance/Recovery/RecoveryParsingHelper.cs:13`

```csharp
public static readonly TimeSpan DefaultStaleThreshold = TimeSpan.FromHours(23);
```

- **Value:** 23 hours. Confirms the stated belief.
- **Accessibility:** `public static readonly` field on `public static class RecoveryParsingHelper`
  (`RecoveryParsingHelper.cs:7`), namespace `Cymulate.IntegrationInfra.FaultGovernance.Recovery`
  (`RecoveryParsingHelper.cs:1`). Because it is `static readonly`, not `const`, it is NOT baked into
  consumer assemblies — a value change in the package takes effect on the next package upgrade with no
  consumer recompile required beyond the restore.
- **Stated rationale** (`RecoveryParsingHelper.cs:9-12`):
  > Default age after which a checkpoint is considered stale (e.g. export/cursor expired).
  > Many provider APIs expire after approximately 24 hours; 23h is a safe threshold.

  That is the whole rationale. It is a proxy for *vendor-side* export/cursor expiry, not for anything
  ISB owns. This matters for section 4: lengthening it does not make an expired vendor cursor work
  again.

**Important scoping fact.** This constant lives in `IntegrationInfra`, **not** in
`Cymulate.Integration.Client`. It ships in the `Cymulate.IntegrationInfra` package
(`src/IntegrationInfra/IntegrationInfra.csproj:20` — `<PackageId>Cymulate.IntegrationInfra</PackageId>`).
ISB does not reference that package at all (see section 5), so `DefaultStaleThreshold` is invisible to
ISB code.

---

## 2. The staleness test function signature

`/Users/user/Dev/IntegrationInfra/src/IntegrationInfra/FaultGovernance/Recovery/RecoveryParsingHelper.cs:37-41`

```csharp
public static bool IsCheckpointStale(DateTime checkpointCreatedUtc, TimeSpan? threshold = null)
{
    var age = DateTime.UtcNow - checkpointCreatedUtc;
    return age > (threshold ?? DefaultStaleThreshold);
}
```

**It already accepts an optional threshold override.** The parameter is documented at
`RecoveryParsingHelper.cs:35` (`<param name="threshold">Age threshold; defaults to DefaultStaleThreshold</param>`).

Two consequences worth stating plainly:

- The *function* needs no change to support a longer bound. The problem is entirely upstream of it —
  getting a value into the 24 call sites (section 4).
- It reads `DateTime.UtcNow` directly rather than a `TimeProvider`, so it is not fake-clock testable.
  The adapters' tests work around this by building timestamps relative to `DefaultStaleThreshold`
  (e.g. `.../FalconResumeRunnerTests.cs:561`).

---

## 3. Every call site (exhaustive, with override status and counts)

### 3a. `IntegrationInfra` — production

**Zero call sites** outside the definition itself. Verified by
`grep -rn "IsCheckpointStale\|DefaultStaleThreshold" --include="*.cs" .` over the whole repo: the only
hits are `RecoveryParsingHelper.cs:13,35,37,40`. There is also **no test** of `IsCheckpointStale` in
`/Users/user/Dev/IntegrationInfra/tests/` — the same grep covers `tests/` and returns nothing.

### 3b. `cymulate-integration-adapters` — production call sites

All are `RecoveryParsingHelper.IsCheckpointStale(<timestamp>)` with **one argument**. Paths are relative
to `/Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/`.

| # | path:line | override? |
| --- | --- | --- |
| 1 | `Collectors/CloudGuardCollector/Recovery/CloudGuardCheckpointHelper.cs:63` | no (default) |
| 2 | `Collectors/CloudGuardCollector/Recovery/CloudGuardCheckpointHelper.cs:80` | no (default) |
| 3 | `Collectors/CortexXdrCollector/Recovery/CortexXdrCheckpointHelper.cs:96` | no (default) |
| 4 | `Collectors/CortexXdrCollector/Recovery/CortexXdrCheckpointHelper.cs:121` | no (default) |
| 5 | `Collectors/DefenderForCloudCollector/Recovery/DefenderForCloudCheckpointHelper.cs:57` | no (default) |
| 6 | `Collectors/DefenderForCloudCollector/Recovery/DefenderForCloudCheckpointHelper.cs:74` | no (default) |
| 7 | `Collectors/DefenderVmCollector/Recovery/DefenderVmCheckpointHelper.cs:71` | no (default) |
| 8 | `Collectors/DefenderVmCollector/Recovery/DefenderVmCheckpointHelper.cs:94` | no (default) |
| 9 | `Collectors/FalconCollector/Recovery/FalconCheckpointResumePolicy.cs:81` | no (default) |
| 10 | `Collectors/FalconCollector/Recovery/FalconCheckpointResumePolicy.cs:107` | no (default) |
| 11 | `Collectors/GuardicoreCollector/Recovery/GuardicoreCheckpointHelper.cs:16` | no (default) |
| 12 | `Collectors/InsightVmCloudCollector/Recovery/InsightVmCloudCheckpointHelper.cs:33` | no (default) |
| 13 | `Collectors/InsightVmCloudCollector/Recovery/InsightVmCloudCheckpointHelper.cs:56` | no (default) |
| 14 | `Collectors/InsightVmCollector/InsightVmCollector.cs:428` | no (default) |
| 15 | `Collectors/InsightVmCollector/InsightVmCollector.cs:452` | no (default) |
| 16 | `Collectors/IsbLoadTestCollector/Cymulate.Integration.Adapters.Collectors.IsbLoadTestCollector/Recovery/IsbLoadTestCheckpointHelper.cs:28` | no (default) |
| 17 | `Collectors/MicrosoftEntraIdCollector/Recovery/MicrosoftEntraIdCheckpointHelper.cs:16` | no (default) |
| 18 | `Collectors/QualysCollector/Recovery/QualysCheckpointHelper.cs:16` | no (default) |
| 19 | `Collectors/SentinelOneCollector/Recovery/SentinelOneCheckpointHelper.cs:16` | no (default) |
| 20 | `Collectors/ServiceNowCmdbCollector/Recovery/ServiceNowCmdbCheckpointHelper.cs:16` | no (default) |
| 21 | `Collectors/TaegisCollector/Recovery/TaegisCheckpointHelper.cs:16` | no (default) |
| 22 | `Collectors/TenableIoCollector/Recovery/TenableIoCheckpointHelper.cs:33` | no (default) |
| 23 | `Collectors/TenableIoCollector/Recovery/TenableIoCheckpointHelper.cs:56` | no (default) |
| 24 | `Collectors/TenableScCollector/TenableScCollector.cs:337` | no (default) |

### 3c. `cymulate-integration-adapters` — test references to the constant

These read `DefaultStaleThreshold` to *construct* a deliberately-stale timestamp. None calls
`IsCheckpointStale` with an override.

- `UnitTests/Collectors/...DefenderForCloudCollector.Test/DefenderForCloudCheckpointAndResumeTests.cs:149`
- `UnitTests/Collectors/...DefenderVmCollector.Test/DefenderVmCheckpointAndResumeTests.cs:295`
- `UnitTests/Collectors/...FalconCollector.Test/FalconResumeRunnerTests.cs:561`
- `UnitTests/Collectors/...FalconCollector.Test/FalconTwoPhaseFindingsTests.cs:1253`
- `UnitTests/Collectors/...FalconCollector.Test/FalconTwoPhaseFindingsTests.cs:1355`
- `UnitTests/Collectors/...FalconCollector.Test/FalconTwoPhaseFindingsTests.cs:1421`
- `UnitTests/Collectors/...InsightVmCloudCollector.Test/InsightVmCloudResumeRunnerTests.cs:148`

One documentation reference: `Collectors/FalconCollector/Recovery/FalconCheckpointState.cs:42` names
`~23h via RecoveryParsingHelper.DefaultStaleThreshold` in a doc comment. Falcon's log lines also hardcode
the number in prose: `FalconCheckpointResumePolicy.cs:84` and `:110` both say
`"exceeds the ~23h resume-age limit"` — a change to the value silently makes those two log messages lie.

### 3d. Counts

- **N = 24 production call sites**, all in `cymulate-integration-adapters`, spanning **16 collectors**.
- **M = 0 pass an explicit threshold.** All 24 take the default.
- Test references to the constant: 7 lines across 5 files, 0 overrides.
- `IntegrationInfra` production call sites: 0. `IntegrationServiceBus` call sites: 0.

**Belief confirmed:** 23h default, every call site uses it with no override.

### 3e. Contradiction risk — ISB has a *different* "stale threshold" that must not be confused with this one

ISB's `grep` for `StaleThreshold` returns hits that have **nothing to do** with the 23h checkpoint bound:

- `Domain/.../Constants/ConfigurationKeys.cs:346` — `Checkpoint:StaleClaimThresholdMinutes`,
  default **10 minutes** (`:348`). This is the *execution-claim* bound used by
  `Application/Commands/ProcessEventCommandHandler.cs:51-53` and
  `Application/Services/CheckpointRecoveryHandler.cs:65-67` — how long a claimed-but-silent row waits
  before another instance may steal it.
- `Domain/.../Constants/ConfigurationKeys.cs:588` — `Query:Recovery:StaleThresholdMinutes`, default
  **15** (`:649`); `:597` — `Query:Recovery:CompletionStaleThresholdMinutes`, default **5** (`:650`).
  Query-integration sweeps only.

These are minutes-scale, configuration-driven, and ISB-owned. The 23h bound is hours-scale, compiled
into a package, and collector-owned. A discussion that says "the recovery sweep's staleness bound"
without qualification is ambiguous across these two — worth pinning down before any decision.

---

## 4. Parameterization surface for a per-request bound

**The question:** an operator-initiated retry wants a LONGER staleness bound than the automatic recovery
sweep, which keeps 23h.

### 4a. The pinch point

The gate is evaluated inside `IResumableAdapter.CanResumeFrom`, whose contract signature is
(`/Users/user/Dev/IntegrationInfra/src/Cymulate.Integration.Client/Contracts/IResumableAdapter.cs:49`):

```csharp
bool CanResumeFrom(AdapterCheckpoint checkpoint);
```

**One argument. No `PlatformEvent`, no execution context, no request.** Compare `ResumeAsync`
(`IResumableAdapter.cs:38-41`), which *does* receive `PlatformEvent platformEvent`.

ISB calls them in that order —
`/Users/user/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Applications/Cymulate.IntegrationServiceBus.Application/Commands/ProcessEventCommandHandler.cs:1165`
(`if (resumable.CanResumeFrom(adapterCheckpoint))`) then `:1179`
(`return await resumable.ResumeAsync(platformEvent, adapterCheckpoint, cancellationToken);`). So the
decision is made **before** the platform event is ever handed to the collector.

Below that, the collector-level helpers narrow further. Every one of the 24 sites sits in a static method
of the shape (e.g. `Collectors/TaegisCollector/Recovery/TaegisCheckpointHelper.cs:7`):

```csharp
public static bool CanResumeFrom(IReadOnlyDictionary<string, string> data, ILogger? logger)
```

— the raw checkpoint blob and a logger. Nothing else is in scope.

### 4b. (a) Can the bound reach the collector through existing plumbing?

**Yes, by exactly one route that requires no contract change: `AdapterCheckpoint.AdapterState`.**

- `AdapterCheckpoint.AdapterState` is `IReadOnlyDictionary<string, string>`
  (`Cymulate.Integration.Client/Models/AdapterCheckpoint.cs:43-44`) — an open-ended bag, no schema.
- ISB fully owns what goes into it at resume time:
  `ProcessEventCommandHandler.cs:1715-1737` (`MapToAdapterCheckpoint`) deserializes
  `entry.AdapterStateJson` into that dictionary. ISB could add a reserved key there — e.g.
  `_resume.staleThresholdHours` — with a **Postgres/ISB-only** change and **no package release**.
- The blob is already in scope at all 24 sites as the `data` parameter, so a central
  `RecoveryParsingHelper` overload that reads the key from `data` is mechanically straightforward.

**Configuration is reachable but is the wrong shape.** The collector *instance* does hold the execution
context: `FalconCollector.cs:49` (`private readonly IAdapterExecutionContext _context;`), and
`IAdapterExecutionContext.Services` (`Contracts/IAdapterExecutionContext.cs:76`) exposes `IConfiguration`
per its own doc (`:65-71`). So a **global, config-driven** bound is reachable inside `CanResumeFrom` with
an adapters-repo-only change. It cannot express *per-request*, which is the actual requirement — config
would move the bound for the automatic sweep too, which the requirement explicitly forbids.

**`PlatformEvent.Metadata` cannot carry it.** `PlatformEvent.Metadata` is
`Dictionary<string, string>` (`Models/PlatformEvent.cs:69`) and does reach `ResumeAsync` — but not
`CanResumeFrom`, which runs first. Using it requires changing `IResumableAdapter.CanResumeFrom`'s
signature: a **breaking change to the `Cymulate.Integration.Client` package** plus all 8 implementors
(`grep "public bool CanResumeFrom(AdapterCheckpoint"` → `IsbLoadTestCollector.cs:426`,
`InsightVmCloudCollector.cs:288`, `DummyCollector.cs:224`, `FalconCollector.cs:454`,
`TenableIoCollector.cs:448`, `TenableScCollector.cs:312`, `InsightVmCollector.cs:403`,
`YamlAdapter.cs:442`).

**The "just move the timestamp" shortcut does not exist cleanly.** The timestamp the gate reads is not
ISB's `entry.CreatedAtUtc` column — it is a key *inside the collector-authored blob*. See
`TaegisCheckpointHelper.cs:42`: `["checkpointCreatedUtc"] = state.CheckpointCreatedUtc.ToString("O")`,
read back at `TaegisCheckpointHelper.cs:16` via the typed state. Forward-dating it from ISB would mean
rewriting collector-owned state, the key name varies per collector, and it would corrupt every other
consumer of that timestamp. Do not do this.

### 4c. (b) Does anything already flow ISB → collector at resume time that could carry it?

Yes — one thing, and only one that reaches the gate:

| carrier | reaches `CanResumeFrom`? | who owns the write | contract change needed |
| --- | --- | --- | --- |
| `AdapterCheckpoint.AdapterState` (`AdapterCheckpoint.cs:43`) | **yes** | ISB, at `ProcessEventCommandHandler.cs:1715-1737` | none |
| `AdapterCheckpoint`'s typed fields (`CurrentPage`, `CursorToken`, `CreatedAtUtc`, …) | yes | ISB, same site | adding a field = Client package change |
| `PlatformEvent.Metadata` (`PlatformEvent.cs:69`) | **no** (arrives only at `ResumeAsync`) | ISB, from the inbound run message | `IResumableAdapter` signature change |
| `IAdapterExecutionContext.Services` → `IConfiguration` | yes (via the collector instance field) | deployment config, not per-request | none, but cannot express per-request |

### 4d. (c) Accessibility and versioning cost

Cheapest viable design, and what it actually costs:

1. **`IntegrationInfra`** — add an overload alongside `IsCheckpointStale`, e.g.
   `IsCheckpointStale(DateTime, IReadOnlyDictionary<string,string> checkpointData)` that reads a reserved
   override key and falls back to `DefaultStaleThreshold`. One file:
   `src/IntegrationInfra/FaultGovernance/Recovery/RecoveryParsingHelper.cs`. Additive, non-breaking.
   Requires a version bump in `/Users/user/Dev/IntegrationInfra/Directory.Build.props:41` — and note both
   packages share that one number and **move together** (`Directory.Build.props:5-8`), so
   `Cymulate.Integration.Client` gets a new version too even with no Client change (exactly what ISB's
   `Directory.Packages.props:120-126` records happening for 1.2.0-preview.0).
2. **`cymulate-integration-adapters`** — 24 mechanical edits at the sites in section 3b, plus a bump of
   `Cymulate.IntegrationInfra` in
   `src/Cymulate.Integration.Adapters/Directory.Packages.props:123`. Every collector picks the package up
   through `src/Cymulate.Integration.Adapters/Collectors/Directory.Build.props:76`, so there is exactly
   one pin to move. Falcon needs two extra touches: the "~23h resume-age limit" strings at
   `FalconCheckpointResumePolicy.cs:84` and `:110`, and the doc comment at `FalconCheckpointState.cs:42`.
   Collectors also ship as versioned S3 artifacts, so each of the 16 affected collectors needs a rebuild
   and re-release before the behaviour reaches production.
3. **`IntegrationServiceBus`** — write the key in `MapToAdapterCheckpoint`
   (`ProcessEventCommandHandler.cs:1725`), gated on whatever marks a run as operator-initiated. ISB does
   **not** reference `Cymulate.IntegrationInfra` (section 5), so ISB cannot name the key from a shared
   constant — it would be a string literal duplicated across two repos, or a new constant added to
   `Cymulate.Integration.Client` (which ISB *does* reference), which is the cleaner option and costs
   nothing extra given the package moves anyway.

**Ordering constraint:** the three repos must land in that order — Infra published, then adapters
rebuilt and re-released against it, then ISB. ISB writing the key before the collectors read it is inert
(harmless, ignored); collectors reading a key ISB never writes is also inert. So the rollout is safe in
either direction, but the *behaviour* only exists once all three ship.

**The cost that should decide this.** Twenty-four call sites across sixteen collectors, each needing an
independent artifact release, to move a bound whose documented purpose
(`RecoveryParsingHelper.cs:10-11`) is to predict *vendor cursor expiry*. A longer bound does not make an
expired vendor export resumable — it makes the collector *try* and fail deeper in. Before paying this,
establish that the failing retries are actually being blocked by the 23h gate rather than by the vendor.
The Falcon decline reason enum (`FalconCheckpointResumePolicy.cs:39`, `FalconResumeDecline.Stale`) and
the warning at `:84`/`:110` make that directly observable in logs for Falcon; other collectors log a
`"is stale"` warning at each of the 24 sites, so the evidence is available everywhere.

---

## 5. Package identity and how consumers bind to it

`IntegrationInfra` ships **two** packages, both versioned off one number
(`/Users/user/Dev/IntegrationInfra/Directory.Build.props:41` — `<Version>1.2.0-preview.0</Version>`;
rationale at `:5-8`, "Both shipped packages … share this one number and move together"):

| package | csproj | contains |
| --- | --- | --- |
| `Cymulate.Integration.Client` | `src/Cymulate.Integration.Client/Cymulate.Integration.Client.csproj:11` | the contract floor: `IResumableAdapter`, `AdapterCheckpoint`, `IAdapterExecutionContext`, `PlatformEvent`, `AdapterResult`, `RunOutcomeReport` |
| `Cymulate.IntegrationInfra` | `src/IntegrationInfra/IntegrationInfra.csproj:20` | the substrate: `RecoveryParsingHelper` (**the 23h bound**), `AdapterDoneEnvelope`, FaultGovernance, Emission, Conversation |

`IntegrationInfra.csproj:45` takes a `ProjectReference` on the Client csproj, so Client flows
transitively to anything referencing Infra. Both publish to CodeArtifact (domain `cym-dom`, repo
`cym-repo-nuget` — `Directory.Build.props:25-27`).

**How the consumers bind — both by `PackageReference`, neither by `ProjectReference`:**

| consumer | package | pinned at |
| --- | --- | --- |
| ISB | `Cymulate.Integration.Client` **1.2.0-preview.0** | `IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Directory.Packages.props:127` |
| ISB | `Cymulate.IntegrationInfra` | **not referenced at all** |
| adapters | `Cymulate.IntegrationInfra` **1.2.0-preview.0** | `cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Directory.Packages.props:123` |
| adapters | `Cymulate.Integration.Client` | transitive via Infra (`Collectors/Directory.Build.props:64`) |

Every collector acquires Infra through one line —
`cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Collectors/Directory.Build.props:76`
(`<PackageReference Include="Cymulate.IntegrationInfra" />`) — so there is a single pin to move for the
whole collector fleet.

ISB's nine `PackageReference Include="Cymulate.Integration.Client"` sites (Domain, Application.Query,
Infrastructure.AWS, Infrastructure.KeyVault, four test projects, the e2e fake adapter) are versionless —
central package management supplies 1.2.0-preview.0.

Restored on this machine: `~/.nuget/packages/cymulate.integration.client/` has
1.0.0-preview.5, 1.0.0-preview.6, 1.1.0-preview.0, 1.1.0-preview.1, 1.1.0-task.4, 1.1.1-preview.0,
1.2.0-preview.0. `~/.nuget/packages/cymulate.integrationinfra/` has the same set minus preview.5, plus a
local-only `1.2.0-local.eof`.

---

## 6. Terminal outcome message: definition site and shape

### CONTRADICTION — `AdapterDoneMessage` is defined **inside ISB**, not in `Cymulate.Integration.Client`

**Real definition site:**
`/Users/user/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Domain/Cymulate.IntegrationServiceBus.Domain/Messaging/AdapterDoneMessage.cs:10`

```csharp
public sealed record AdapterDoneMessage      // namespace Cymulate.IntegrationServiceBus.Domain.Messaging
```

It appears **nowhere** in `/Users/user/Dev/IntegrationInfra` (`grep -rn "AdapterDoneMessage" --include="*.cs" .` → no hits)
and **nowhere** in either restored package binary (`strings` on
`cymulate.integration.client/1.2.0-preview.0/lib/net8.0/Cymulate.Integration.Client.dll` → 0 hits;
same for `Cymulate.IntegrationInfra.dll` → 0 hits).

### Field list — `AdapterDoneMessage` (`AdapterDoneMessage.cs:10-46`)

| field | JSON | type | line |
| --- | --- | --- | --- |
| `Topic` | `topic` | `string` (default `""`) | `:16` |
| `Vendor` | `vendor` | `string` (default `""`) | `:22` |
| `CorrelationId` | `correlationId` | `string` (default `""`) | `:28` |
| `Timestamp` | `timestamp` | `long` (unix ms) | `:34` |
| `Status` | `status` | `string`, default `"success"` | `:40` |
| `Payload` | `payload` | `AdapterDonePayload?` | `:46` |

### `AdapterDonePayload` (`AdapterDoneMessage.cs:52-82`)

| field | JSON | type | line |
| --- | --- | --- | --- |
| `TotalAssetsCollected` | `totalAssetsCollected` | `int` | `:58` |
| `TotalFindingsCollected` | `totalFindingsCollected` | `int` | `:64` |
| `TotalSequences` | `totalSequences` | `int` | `:70` |
| `TotalPages` | `totalPages` | `int` | `:76` |
| `Metadata` | `metadata` | `MessageMetadata?` | `:82` |

`MessageMetadata` (`Domain/Messaging/MessageMetadata.cs:10`) carries `instanceOid`, `instanceId`,
`clientID`, `clientIntegrationId`, `clientIntegrationFlowId`, `integrationSettingId`,
`integrationSettingFlowId`, `storageUrl` (`:16`–`:59`).

### Status values

`Status` is a **free-form `string`**, not an enum. Its doc (`AdapterDoneMessage.cs:37`) says
`"success", "failed", or "partial"` — but that doc is **incomplete and partly wrong** against what ISB
actually emits:

| value | emitted at | in the doc? |
| --- | --- | --- |
| `"success"` | `ProcessEventCommandHandler.cs:1882`, `AdapterEventHub.cs:263`, `AdapterCompletionStatus.cs:28` | yes |
| `"failed"` | `ProcessEventCommandHandler.cs:1882`, `IsbDeliveryFailureNotifier.cs:70`, `CheckpointRecoveryHandler.cs:931`, `KafkaConsumerService.cs:473`, `EventsController.cs:1294`, `AdapterCompletionStatus.cs:29,31` | yes |
| `"partial"` | `AdapterCompletionStatus.cs:30` only, reachable via `AdapterMessageMapper.cs:75` | yes |
| **`"cancelled"`** | `ProcessEventCommandHandler.cs:1971` | **no — undocumented** |

The typed enum behind the mapper path is `AdapterCompletionStatus`
(`Domain/Enums/AdapterCompletionStatus.cs:26-32`, `ToWireFormat`), which has no `Cancelled` member and
falls back to `"failed"` for anything unrecognised (`:31`). So `"cancelled"` only exists on the
hand-built path.

---

## 7. Package/source drift, BOTH directions — and what ISB compiles against

Per R3, checked in both directions. Method note: a naive exact-match of source type names against
`strings` output on the DLL produces **false positives** — the ECMA-335 `#Strings` heap uses suffix
sharing, so e.g. `AdapterResult` is not separately visible when `FromAdapterResult` is present. The
comparison below uses the packaged XML doc file (documented types) as the primary set and substring
checks against the DLL for anything that differed.

### 7a. `Cymulate.Integration.Client` 1.2.0-preview.0 (restored) vs `src/Cymulate.Integration.Client` (HEAD `86de702`)

- **Source → package:** 158 declared types; **1** absent from the package XML: `JsonElementExtensions`
  (`src/Cymulate.Integration.Client/Extensions/JsonElementExtensions.cs:5`). It **is** present in the
  DLL (substring check → 1 hit). It is a static extension class with no XML doc, so its absence from the
  XML is a documentation artifact, **not** drift.
- **Package → source:** 18 XML types absent from source, **all** compiler-generated:
  the regex source-generator emissions (`SemVerRegex_0`, `Md5Hash_1`, `Sha1Hash_2`, `Sha256Hash_3`,
  `AnyHash_4`, `Ipv4Address_5`, `Ipv6Address_6`, `IpAddress_7`, `MacAddress_8`, `Email_9`, `Url_10`,
  `ValidUrl_11`, `FileUrl_12`, `HttpContent_13`) plus its helpers (`Runner`, `RunnerFactory`,
  `Utilities`) and one nested delegate (`CredentialChangeHandler`).

**Conclusion: no drift in either direction for the Client package.**

### 7b. `Cymulate.IntegrationInfra` 1.2.0-preview.0 (restored) vs `src/IntegrationInfra` (HEAD)

For the type in scope — `RecoveryParsingHelper` — the packaged XML
(`cymulate.integrationinfra/1.2.0-preview.0/lib/net8.0/Cymulate.IntegrationInfra.xml`) lists exactly
7 members, identical to source in name, signature and doc text:

- `T:…RecoveryParsingHelper` — same summary as `RecoveryParsingHelper.cs:5-8`
- `F:…DefaultStaleThreshold` — same "23h is a safe threshold" summary as `:9-12`
- `M:…IsFindingsFlow(System.String)`, `M:…IsAssetsFlow(System.String)`
- `M:…IsCheckpointStale(System.DateTime,System.Nullable{System.TimeSpan})` — **the optional override
  parameter is present in the shipped package**, not only in source
- `M:…TryGetInt(...)`, `M:…TryGetBool(...)`, `M:…TryGetDateTime(...)`

Git corroborates: `RecoveryParsingHelper.cs` has one commit in its history (`a3c3548`, the FaultGovernance
relocation) and has not moved since; HEAD (`86de702`) is the commit that sets `Version` to
1.2.0-preview.0. **No drift for this type in either direction.**

Caveat, stated because it bounds the claim: the *value* `23` is a `static readonly` initialised in the
static constructor, so it is not directly readable from the binary without a decompiler. The equality
claim above rests on the unchanged git history plus the identical doc text, not on binary inspection.
`SPECULATION:` I did not decompile the IL to confirm the shipped constant is 23h.

The whole-package type diff shows 79 source types absent from the Infra package XML
(`AdapterDoneEnvelope`, `AdapterDonePayload`, `NdjsonOptions`, the whole `AdapterBus*` family, …) and 6
XML types absent from source (`ReadSlot`, `TryParseState`, `Runner`, `RunnerFactory`, `Utilities`,
`SensitiveQueryValueRegex_0`). The 79 are undocumented types suppressed by
`<NoWarn>$(NoWarn);CS1591</NoWarn>` (`IntegrationInfra.csproj:12`) and so never enter the XML — a
documentation gap, not drift. The 6 are nested types and source-generator emissions.
`SPECULATION:` I did not individually decompile all 79 to prove each is present in the DLL; the
CS1591-suppression explanation is inferred from the csproj, not verified type by type. It does not
affect anything in this task's scope.

### 7c. What ISB actually COMPILES against

- **`AdapterDoneMessage` / `AdapterDonePayload` / `MessageMetadata`:** ISB's **own source**, at
  `Domain/Cymulate.IntegrationServiceBus.Domain/Messaging/AdapterDoneMessage.cs:10` and
  `MessageMetadata.cs:10`. No package is involved. There is no second copy to drift from — the package
  does not contain these types at all.
- **`RecoveryParsingHelper` / the 23h bound:** ISB **does not compile against it at all.** ISB
  references only `Cymulate.Integration.Client` (nine csprojs), never `Cymulate.IntegrationInfra`. The
  23h gate is entirely outside ISB's compilation and outside its control.
- **`IResumableAdapter`, `AdapterCheckpoint`, `PlatformEvent`:** the restored
  `Cymulate.Integration.Client` **1.2.0-preview.0** package, pinned at
  `IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Directory.Packages.props:127`. Source-of-truth
  for reading them is `IntegrationInfra/src/Cymulate.Integration.Client/`, which section 7a shows is not
  drifted from the package.

### 7d. A second, near-twin definition of the done wire contract — in IntegrationInfra

Distinct from drift, and worth flagging because it is the same JSON shape maintained in two repos:

`/Users/user/Dev/IntegrationInfra/src/IntegrationInfra/Reporting/AdapterDoneEnvelope.cs:6` declares
`AdapterDoneEnvelope` with `topic`, `vendor`, `correlationId`, `timestamp`, `status`, `payload` — the
same six JSON names as ISB's `AdapterDoneMessage`. Its payload
(`Reporting/AdapterDonePayload.cs:8`) has the same four totals plus `metadata` — **plus one field ISB
does not have**: `partialCompletion` (`AdapterDonePayload.cs:25`, `[JsonIgnore(WhenWritingNull)]`).
Its `status` is the typed `AdapterRunStatus` enum
(`Envelopes/Common/AdapterRunStatus.cs:25-30`: `Success`, `Failed`, `Partial`) serialized to
`"success"/"failed"/"partial"` by a hand-rolled converter (`AdapterRunStatus.cs:19-21`) — **no
`"cancelled"`**, which ISB emits at `ProcessEventCommandHandler.cs:1971`.

`AdapterDoneEnvelope` has no production caller in either repo: `AdapterEnvelopeBuilder.BuildDone`
(`Reporting/AdapterEnvelopeBuilder.cs:48`) is called only from
`IntegrationInfra/tests/IntegrationInfra.Reporting.Tests/AdapterEnvelopeBuilderTests.cs:44` and
`cymulate-integration-adapters/.../DummyCollector.Test/CollectorEnvelopeBuilderTests.cs:31`. In the
ISB-hosted path it is dead. It is the out-of-process/standalone shape.

---

## 8. Publishers and consumers of the done message

### Publishers — all inside ISB

| site | what it emits |
| --- | --- |
| `Application/Commands/ProcessEventCommandHandler.cs:1903` (`CreateAdapterDoneMessage`) | normal terminal done; status `"success"`/`"failed"` (`:1882`) |
| `Application/Commands/ProcessEventCommandHandler.cs:1965-1982` | `"cancelled"` (`:1971`) on stop |
| `Application/Commands/ProcessEventCommandHandler.cs:2145-2162` | done publish on a second path |
| `Application/Messaging/IsbDeliveryFailureNotifier.cs:64-70` | `"failed"` when a delivery is dead-lettered |
| `Application/Services/CheckpointRecoveryHandler.cs:925-931` | `"failed"` from the recovery sweep |
| `Infrastructure.Core/Services/AdapterEventHub.cs:255-272` | in-proc completion → done |
| `Infrastructure.Core/Messaging/AdapterMessageMapper.cs:67-85` | the only path that can emit `"partial"` |
| `Infrastructure.Kafka/KafkaConsumerService.cs:467-473` | `"failed"` |
| `Hosts/API/Controllers/EventsController.cs:1288-1294` | `"failed"` |

Transport: `IAdapterEventPublisher.PublishDoneAsync`
(`Domain/Interfaces/IAdapterEventPublisher.cs:26`), implemented by
`Infrastructure.RabbitMQ/RabbitMqAdapterEventPublisher.cs:68` and
`Infrastructure.Kafka/KafkaAdapterEventPublisher.cs:73`, with a `NullAdapterEventPublisher.cs:21` and a
`Decorators/ReportingGatedAdapterEventPublisher.cs:36` gate in front. Queue is per category
(`Infrastructure.RabbitMQ/Options/RabbitMqOptions.cs:702-714`); collectors →
**`collectors.done`** (`RabbitMqOptions.cs:504`). SIEM rules deliberately publish no terminal event
(`RabbitMqOptions.cs:712`).

### Consumer — **on this machine**, in `cymulate-integrations`

Contradicting the expectation that the consumer might be off-machine:

- `/Users/user/Dev/cymulate-integrations/apps/integration-connectors-manager/src/app/modules/connectors-manager/controllers/connector-manager.controller.ts:29`
  — `@EventPattern('collectors.done')` → `collectionDone(data, tenantId)`.
- Its TypeScript mirror of the contract:
  `.../services/connector-manager.service.ts:69-76` (`CollectorDoneMessage`: `topic?`, `vendor?`,
  `correlationId?`, `timestamp?`, `status`, `payload`), payload at `:61-67`
  (`totalAssetsCollected?`, `totalFindingsCollected?`, `totalSequences?`, `totalPages?`, `metadata`).
- **Its status union is the fullest description of the contract found anywhere:**
  `.../connector-manager.service.ts:59` —
  `type CollectorDoneStatus = 'success' | 'failed' | 'partial' | 'cancelled';` — four values, matching
  what ISB actually emits and contradicting ISB's own three-value doc comment
  (`AdapterDoneMessage.cs:37`).
- Fields the consumer reads: `data.status`, `data.correlationId`, `data.timestamp`, and the four totals
  (`connector-manager.service.ts:485-493`); `data.payload?.metadata?.instanceId` (`:498`), with
  `correlationId` used as the instance OID (`:474-475`, throws if missing).

The consumer is structurally tolerant of an added field: the handler takes `@Payload() data: any`
(`connector-manager.controller.ts:30`) and the type is a plain TS `type` with no runtime validation, so
an unknown property is ignored rather than rejected.

`collectors.progress` is consumed by the same controller (`:19`). No consumer of
`collectors.partial-done` was found in any repo on this machine.

---

## 9. Release cost of adding a field

**Which repo owns the change:** `IntegrationServiceBus`. `AdapterDoneMessage` is ISB source
(`Domain/.../Messaging/AdapterDoneMessage.cs:10`) — not package surface.

**Release/publish steps required:** **none beyond ISB's own deploy.** No `dotnet pack`, no CodeArtifact
publish, no `Directory.Packages.props` bump in any repo. This is the material correction to the stated
belief.

**Consumers that must be coordinated:**

- `cymulate-integrations` / `integration-connectors-manager` — the real consumer, on this machine. For a
  **purely additive, optional** field it needs **no** change to keep working: `@Payload() data: any`
  (`connector-manager.controller.ts:30`) with no runtime schema. It needs a change only to *use* the
  field, which can follow at its own pace.
- Any consumer not on this machine: none found. `grep` for `collectors.done` across
  `IntegrationsDataFlow`, `cymulate-bas-platform`, `Collectors`, `cymulate-integration-parsers`,
  `Dispatcher`, `ServiceBus`, `AgentService`, `cymulate-magic-integration` returned only one hit, in a
  `cymulate-integration-parsers` research markdown file (not code).
  `SPECULATION:` other consumers could exist in repos not cloned here; I searched only this machine's
  `/Users/user/Dev`, as instructed (no web research).

**Can ISB ship independently? Yes** — for an additive optional field. That is a materially cheaper
change than the belief assumed.

**Two caveats that do have cross-repo weight:**

1. If the field's *value* must come from the collector (a checkpoint state, a resume reason), then the
   collector must be able to report it — and `AdapterResult`/`AdapterCompletion` are the carriers.
   `AdapterResult` is Client-package surface, so *that* becomes a coordinated release with a rebuild of
   all 16 collectors. Note how little ISB currently gets from the collector:
   `CreateAdapterDoneMessage` scrapes only `total` and `findings` out of a
   `Dictionary<string, object>` in `result.Data` (`ProcessEventCommandHandler.cs:1887-1898`) and
   hardcodes `TotalSequences = 1, TotalPages = 1` (`:1914-1915`).
2. Adding a field to ISB's `AdapterDoneMessage` widens its divergence from IntegrationInfra's
   `AdapterDoneEnvelope` (section 7d). Cosmetic today because the envelope has no production caller —
   but the two are already out of step in two ways (`partialCompletion` present there, absent here;
   `"cancelled"` emitted here, unrepresentable there).

---

## 10. Existing fields already carrying progress/resumability

**On `AdapterDoneMessage` itself: nothing.** All five payload fields are terminal counts
(`totalAssetsCollected`, `totalFindingsCollected`, `totalSequences`, `totalPages` —
`AdapterDoneMessage.cs:58-76`) plus identity metadata (`MessageMetadata.cs:16-59`, whose only
non-identity field is `storageUrl`). No checkpoint id, no cursor, no watermark, no attempt count, no
resumable flag. The two totals ISB does populate are themselves scraped best-effort from
`result.Data` and default to 0 (`ProcessEventCommandHandler.cs:1886-1898`), and
`TotalSequences`/`TotalPages` are hardcoded to 1 on that path (`:1914-1915`).

**Three adjacent things already exist. In rough order of usefulness:**

1. **`AdapterPartialDoneMessage` — the closest existing fit, already carries resumability.**
   `/Users/user/Dev/IntegrationServiceBus/src/Cymulate.IntegrationServiceBus/Domain/Messaging/AdapterPartialDoneMessage.cs:15`.
   Fields: `type` (`:21`, constant `"AdapterPartialDone"`), `tenantId` (`:27`), `correlationId` (`:33`),
   `platform` (`:40`), `category` (`:47`), **`scheduledResumeAtUtc`** (`:53`),
   **`resumeAfterSeconds`** (`:59`), **`waitReason`** (`:65`), **`processedSoFar`** (`:71`),
   `metadata` (`:77`).

   Published by `IAdapterEventPublisher.PublishPartialDoneAsync` from
   `Application/Commands/ProcessEventCommandHandler.cs:975-987`, which deliberately re-reads the
   persisted row first so the event carries the real `ProcessedSoFar` (`:966`). Goes to
   `collectors.partial-done` (`Infrastructure.RabbitMQ/Options/RabbitMqOptions.cs:584`;
   Kafka topic at `Infrastructure.Kafka/KafkaAdapterEventPublisher.cs:38`).

   **Two blockers before treating it as a solution.** It is **disabled by default** —
   `Infrastructure.Core/Options/AdapterEventReportingOptions.cs:20,22` set `PartialDone = false`, and the
   file states at `:12` that this is among "the kinds no service consumes". And no consumer of
   `collectors.partial-done` exists in any repo on this machine. So the *shape* is already designed and
   built, but the channel is dark end to end. Turning it on is a config flip plus a consumer — which may
   still be cheaper than a new field, and reuses a DTO whose semantics already match "this run paused
   and will resume".

   It is also explicitly **non-terminal** by design: `RabbitMqOptions.cs:580-583` says the partial-done
   queue is kept separate from the done queue "so consumers can rely on the done queue meaning the
   session has actually finished."

2. **`AdapterPartialCompletionMetadata` — the richest existing shape, but it does not reach ISB.**
   `/Users/user/Dev/IntegrationInfra/src/IntegrationInfra/Envelopes/Common/AdapterPartialCompletionMetadata.cs:9`:
   `partialCompletion` (`:13`), `reason` (`:17`), `coverageKnown` (`:21`), `requestedFromUtc` (`:25`),
   **`watermarkUtc`** (`:29`), **`completedThroughUtc`** (`:33`), `error` (`:37`), plus
   `[JsonExtensionData]` for vendor-specific extras (`:41`).

   It is genuinely produced today — Falcon builds one at
   `cymulate-integration-adapters/.../FalconCollector/Processing/Resilience/FalconResilienceStrategyFactory.cs:45`,
   and `IntegrationInfra/src/IntegrationInfra/FaultGovernance/Logic/AdapterFailureDecisionExecutor.cs:378`
   builds them generically. But the only envelope with a slot for it is `AdapterDonePayload`
   (`IntegrationInfra/src/IntegrationInfra/Reporting/AdapterDonePayload.cs:25`), whose builder is
   test-only (section 7d). **In the ISB-hosted path this metadata is produced and then dropped.**
   Wiring it through would be the highest-value change here, and its cost is the Client/`AdapterResult`
   coordinated release described in section 9 caveat 1.

3. **`AdapterCheckpoint` / the persisted checkpoint row** — carries `CurrentPage`, `ProcessedItems`,
   `ProcessedFindings`, `CurrentSequenceId`, `CursorToken`, `LastProcessedId`, `AdapterState`,
   `CreatedAtUtc`, `CheckpointKind`, `CheckpointReason` (`AdapterCheckpoint.cs:12-70`). This is ISB-side
   state, never published on any message. It is the source a new done-message field would read from —
   ISB already materialises it at `ProcessEventCommandHandler.cs:1715-1737`.

**Assessment.** A new field on `AdapterDoneMessage` is not obviously necessary. `AdapterPartialDoneMessage`
already models "paused, will resume, here is when and how far" and is built, published and switched off.
Decide whether the requirement is *"the done message should say the run is resumable"* (then reuse
partial-done, or add a field — cheap, ISB-only) or *"the platform should know what coverage was actually
achieved"* (then the answer is `AdapterPartialCompletionMetadata`, and the cost is cross-repo).

---

## Contradictions found against stated beliefs

**Belief 1 — "The shared default staleness threshold is 23h and every collector call site uses it with
no override."**
**CONFIRMED.** 23h at `RecoveryParsingHelper.cs:13`; 24 production call sites, 0 overrides (section 3).
One refinement worth carrying: `IsCheckpointStale` **already takes** an optional `TimeSpan? threshold`
(`RecoveryParsingHelper.cs:37`), so the helper is not the obstacle — the 24 call sites and the
one-argument `CanResumeFrom(AdapterCheckpoint)` contract are.

**Belief 2 — "`AdapterDoneMessage` lives in `Cymulate.Integration.Client`, outside the ISB repo."**
**CONTRADICTED — this is the most consequential finding.** It is ISB source at
`Domain/Cymulate.IntegrationServiceBus.Domain/Messaging/AdapterDoneMessage.cs:10`, absent from
IntegrationInfra source and from both restored package binaries. IntegrationInfra has a near-twin
(`Reporting/AdapterDoneEnvelope.cs:6`) with the same JSON shape plus `partialCompletion`, but it has no
production caller and ISB does not reference the package that contains it.

**Belief 3 — "A contract change there is a coordinated cross-repo release."**
**CONTRADICTED for the done message; CONFIRMED for the staleness bound.**
- Adding an optional field to `AdapterDoneMessage`: **ISB ships independently.** No package pack, no
  publish, no version pin anywhere. The one real consumer (`cymulate-integrations`
  `integration-connectors-manager`) takes `@Payload() data: any` with no runtime validation and ignores
  unknown fields. Coordination is only needed for the consumer to *use* the field.
- Parameterizing the staleness bound: genuinely cross-repo — IntegrationInfra publish → 24 edits in
  `cymulate-integration-adapters` + a package bump + 16 collector artifact re-releases → ISB.

**Additional contradiction, not in the stated beliefs.** ISB's `AdapterDoneMessage.Status` doc
(`AdapterDoneMessage.cs:37`) claims three values; ISB emits a fourth, `"cancelled"`
(`ProcessEventCommandHandler.cs:1971`). The consumer already models all four
(`connector-manager.service.ts:59`); IntegrationInfra's `AdapterRunStatus`
(`Envelopes/Common/AdapterRunStatus.cs:25-30`) models only three and cannot express it.

---

## Speculation (explicitly labelled)

- `SPECULATION:` I did not decompile IL to confirm the shipped `DefaultStaleThreshold` in
  `Cymulate.IntegrationInfra` 1.2.0-preview.0 is literally 23h. The claim rests on unchanged git history
  for `RecoveryParsingHelper.cs` plus byte-identical doc text in the packaged XML.
- `SPECULATION:` for the Infra package I did not individually verify each of the 79 source types missing
  from the packaged XML doc. The CS1591-suppression explanation is inferred from
  `IntegrationInfra.csproj:12`. None of the 79 is in this task's scope.
- `SPECULATION:` consumers of `collectors.done` may exist in repos not cloned on this machine. I searched
  only `/Users/user/Dev` and did no web research, per the task rules.
- `SPECULATION:` a collector could in principle stash the inbound `PlatformEvent.Metadata` during
  initialization and read it back inside `CanResumeFrom`, sidestepping the signature limitation. I did
  not verify whether any collector's initialization sequence makes that possible, and it would be a
  fragile design regardless.
- `SPECULATION:` I did not investigate whether an operator-initiated retry path exists in ISB today, or
  how it would be distinguished from the automatic sweep at `MapToAdapterCheckpoint`. That is W1/W2
  territory; section 4 assumes such a marker can be made available and does not verify it.
