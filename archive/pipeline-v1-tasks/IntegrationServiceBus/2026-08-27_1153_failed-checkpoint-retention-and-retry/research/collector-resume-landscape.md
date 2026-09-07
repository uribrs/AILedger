# Collector Resume Landscape (W2)

Repo: `/Users/user/Dev/cymulate-integration-adapters` (read-only). Shared substrate definitions read from
`/Users/user/Dev/IntegrationInfra` for reference only.

Rules consulted: `/Users/user/Dev/cymulate-integration-adapters/CLAUDE.md` (adapter kinds, `Recovery/` folder
convention, resumability-is-first-class section), `ai/skills/collector-recovery/SKILL.md` (the checkpoint-helper
procedure, the "never resume onto a persisted vendor cursor" rule, the 23h-vs-cursor-TTL separation),
`ai/skills/collector-execution-and-recovery/SKILL.md`.

Every claim below carries a `path:line`. Paths are relative to
`/Users/user/Dev/cymulate-integration-adapters/src/Cymulate.Integration.Adapters/` unless prefixed.

---

## 1. Fleet denominator (all collectors, resume-capable or not)

**18 of 18 collector projects in this repo are resume-capable. There are no have-nots here.**

`Collectors/` holds 18 directories, but `Collectors/YamlCollector/` contains **no source and no `.csproj`** —
only leftover `bin`/`obj` and a vendored `Cymulate.Integration.Yaml.Engine` build output. It is not a project;
CLAUDE.md's "the copy that used to sit under `Collectors/YamlCollector` went away with that project"
(`Directory.Packages.props:125`) confirms it. So the collector fleet is 17 native projects + the `YamlAdapter`
assembly, which stamps the same `IsCollector` metadata and serves every YAML-defined vendor.

| # | Collector | Declares `IResumableAdapter` | Real resume path? |
|---|---|---|---|
| 1 | CloudGuard | `Collectors/CloudGuardCollector/CloudGuardCollector.cs:33` | yes — `Recovery/CloudGuardCheckpointHelper.cs`, `Recovery/CloudGuardResumeRunner.cs` |
| 2 | CortexXdr | `Collectors/CortexXdrCollector/CortexXdrCollector.cs:33` | yes — `Recovery/CortexXdrCheckpointHelper.cs:96`, `Recovery/CortexXdrResumeRunner.cs` |
| 3 | DefenderForCloud | `Collectors/DefenderForCloudCollector/DefenderForCloudCollector.cs:31` | yes — `Recovery/DefenderForCloudCheckpointHelper.cs:57` |
| 4 | DefenderVm | `Collectors/DefenderVmCollector/DefenderVmCollector.cs:32` | yes — `Recovery/DefenderVmCheckpointHelper.cs:71` |
| 5 | Dummy (test collector) | `Collectors/DummyCollector/DummyCollector.cs:29` | yes, but deviant — see §2 |
| 6 | Falcon | `Collectors/FalconCollector/FalconCollector.cs:45` | yes — richest; `Recovery/` has 9 files |
| 7 | Guardicore | `Collectors/GuardicoreCollector/GuardicoreCollector.cs:33` | yes — `Recovery/GuardicoreCheckpointHelper.cs:16` |
| 8 | InsightVmCloud | `Collectors/InsightVmCloudCollector/InsightVmCloudCollector.cs:37` | yes — `Recovery/InsightVmCloudCheckpointHelper.cs:33` |
| 9 | InsightVm | `Collectors/InsightVmCollector/InsightVmCollector.cs:38` | yes, but gate is inline — see §2 |
| 10 | IsbLoadTest | `Collectors/IsbLoadTestCollector/.../IsbLoadTestCollector.cs:36` | yes — `Recovery/IsbLoadTestCheckpointHelper.cs:28` |
| 11 | MicrosoftEntraId | `Collectors/MicrosoftEntraIdCollector/MicrosoftEntraIdCollector.cs:33` | yes — `Recovery/MicrosoftEntraIdCheckpointHelper.cs:16` |
| 12 | Qualys | `Collectors/QualysCollector/QualysCollector.cs:33` | yes — `Recovery/QualysCheckpointHelper.cs:16` |
| 13 | SentinelOne | `Collectors/SentinelOneCollector/SentinelOneCollector.cs:33` | yes — `Recovery/SentinelOneCheckpointHelper.cs:16` |
| 14 | ServiceNowCmdb | `Collectors/ServiceNowCmdbCollector/ServiceNowCmdbCollector.cs:33` | yes — `Recovery/ServiceNowCmdbCheckpointHelper.cs:16` |
| 15 | Taegis | `Collectors/TaegisCollector/TaegisCollector.cs:33` | yes — `Recovery/TaegisCheckpointHelper.cs:16` |
| 16 | **TenableIo** | `Collectors/TenableIoCollector/TenableIoCollector.cs:34` | **yes** — `Recovery/TenableIoCheckpointHelper.cs`, `Recovery/TenableIoResumeRunner.cs`, `Flows/Findings/Correlated/TenableIoCorrelatedCheckpoint.cs` |
| 17 | TenableSc | `Collectors/TenableScCollector/TenableScCollector.cs:34` | yes, but gate is inline and there is no resume runner — see §2 |
| 18 | YamlAdapter (all YAML vendors) | `YamlAdapter/Cymulate.Integration.Adapters.YamlAdapter/YamlAdapter.cs:49` | yes, but a wholly separate mechanism — see §2 |

Non-collector adapters in the repo (`Indicators/`, `Exclusions/`, YamlAdapter's SiemRules facet) do not resume
and are not expected to: `YamlAdapter/.../SiemRules/SiemRulesFacet.cs:25` states the SIEM-rules capability is
deliberately non-resumable (spec D7), enforced by the fact that only collector flows write a
`YamlCollectorCheckpointState` (`YamlAdapter.cs:437-446`).

**AgentService does not exist in this repo at all** — no directory, no project. If prior work found it lacking a
resume path, that finding belongs to another repository.

---

## 2. Helper-pattern uniformity and deviations

The canonical shape (14 of 18 follow it exactly):

- `Recovery/<Name>CheckpointState.cs` — one record per flow, always carrying `Flow`, `Page`,
  `CheckpointCreatedUtc`, `BaseDateUtc`, `IsDryRun`.
- `Recovery/<Name>CheckpointHelper.cs` — `CanResumeFrom(IReadOnlyDictionary<string,string>, ILogger)`,
  `Save<Flow>State(...)`, `TryLoad<Flow>State(...)`.
- `Recovery/<Name>ResumeRunner.cs` — wraps the substrate's `CollectorResumeRunner`.
- The collector's `IResumableAdapter.CanResumeFrom` is a 9-line explicit-interface stub: `HasData` guard →
  `CheckpointAdapter.GetData` → delegate to the helper. E.g.
  `Collectors/SentinelOneCollector/SentinelOneCollector.cs:255-264`, identical in CloudGuard (`:251`),
  CortexXdr (`:288`), DefenderForCloud (`:250`), DefenderVm (`:235`), Guardicore (`:248`),
  MicrosoftEntraId (`:254`), Qualys (`:238`), ServiceNowCmdb (`:252`), Taegis (`:248`).

**Deviations, with what deviates:**

1. **YamlAdapter — a different mechanism end to end.** No `Recovery/*CheckpointHelper`; state is one JSON blob
   under `AdapterState["yaml.state"]` (`YamlAdapter/.../Recovery/YamlCollectorCheckpointState.cs:13`). Its
   `CanResumeFrom` (`YamlAdapter/.../YamlAdapter.cs:443-465`) does **not** call `RecoveryParsingHelper` at all —
   it uses its own age gate against a host column (§4), its own version allow-list, and a per-strategy cursor
   check. It does use `CollectorResumeRunner` (`YamlAdapter.cs:432`).
2. **DummyCollector — forked substrate helper, and no staleness gate.** It carries its own private copy of the
   substrate type at `Collectors/DummyCollector/Recovery/CheckpointAdapter.cs:9` (duplicating
   `IntegrationInfra/FaultGovernance/Recovery/CheckpointAdapter.cs:9`), and
   `Recovery/DummyCheckpointHelper.cs` has **no `IsCheckpointStale` call anywhere** — it parses
   `checkpointCreatedUtc` (`:69`) and never compares it. It also has no resume runner and a flat-column
   fallback path (`DummyCollector.cs:247-256`, `:295-309`).
3. **TenableSc — no helper delegation, no resume runner.** `CanResumeFrom` is written inline in the collector
   (`Collectors/TenableScCollector/TenableScCollector.cs:312-353`), including the flow parse and the staleness
   call at `:337`. `Recovery/` holds only a helper and a state file; there is no `TenableScResumeRunner.cs` and
   no `CollectorResumeRunner` reference in the project.
4. **InsightVm — gate inline in the collector.** `CanResumeFrom` at
   `Collectors/InsightVmCollector/InsightVmCollector.cs:403-470` parses the flow and calls staleness twice
   (`:428`, `:452`) itself, rather than delegating to `Recovery/InsightVmCheckpointHelper`. It does have a
   `Recovery/InsightVmResumeRunner.cs`.
5. **TenableIo — two gates, correlated one wins.** The collector's `CanResumeFrom`
   (`Collectors/TenableIoCollector/TenableIoCollector.cs:448-464`) deliberately routes to
   `TenableIoCorrelatedCheckpoint.CanResumeFrom` (`Flows/Findings/Correlated/TenableIoCorrelatedCheckpoint.cs:81`),
   not to `Recovery/TenableIoCheckpointHelper.CanResumeFrom`. The correlated gate adds a format-version and
   combined-run refusal, then chains into the helper (`TenableIoCorrelatedCheckpoint.cs:88-89`), so the
   staleness check still runs.
6. **Falcon — policy split out of the helper.** `Recovery/FalconCheckpointHelper.cs` is a thin façade over four
   focused types (`FalconCheckpointKeys`, `Serializer`, `Deserializer`, `FalconCheckpointResumePolicy`), and the
   collector wraps the result in a corruption guard (§5). Nine files in `Recovery/`; every other collector has
   two or three.
7. **InsightVmCloud / IsbLoadTest** — helper present and correct, but the collector reads `GetData` into a local
   before delegating rather than inlining it (`InsightVmCloudCollector.cs:296-297`,
   `IsbLoadTestCollector.cs:434-435`). Cosmetic only; noted for completeness.

---

## 3. What `CanResumeFrom` consumes, per collector

Parameter type is `AdapterCheckpoint` (from the `Cymulate.Integration.Client` package) for **all 18**.

The substrate accessor **reads only `AdapterState`**:
`IntegrationInfra/src/IntegrationInfra/FaultGovernance/Recovery/CheckpointAdapter.cs:16-25` returns a copy of
`checkpoint.AdapterState`, or an empty dictionary. `HasData` (`:33-36`) is `GetData(...).Count > 0`. So every
collector that goes through `CheckpointAdapter` sees the string dictionary and nothing else.

**Two collectors read ISB's flat columns inside `CanResumeFrom`, and one of them makes a decision on them:**

| Collector | Reads in `CanResumeFrom` | Flat columns? |
|---|---|---|
| CloudGuard | `flow`, then `CloudGuardCheckpointState` (page, `SearchAfter`, `HasMorePages`, `CheckpointCreatedUtc`) | no |
| CortexXdr | `flow`, then assets/findings state (`NextSearchFrom`, stage, `NextCveIndex`, counters, `CheckpointCreatedUtc`) | no |
| DefenderForCloud | `flow`, `QueryKind`, `SkipToken`, `HasMorePages`, `CheckpointCreatedUtc` | no |
| DefenderVm | `flow`, `NextUrl`, `ResourceKind`, `PendingRecommendationReferences`, `CheckpointCreatedUtc` | no |
| **Dummy** | `flow` + full state; **falls back to `checkpoint.CurrentPage > 1`** when `AdapterState` is empty (`DummyCollector.cs:249`) and returns **true** on the flat counter alone (`:255`) | **YES — decisive** |
| **Falcon** | `flow` + flow state via `FalconCheckpointHelper` (`FalconCollector.cs:462-463`); on a decline it then reads `checkpoint.CurrentPage`, `ProcessedItems`, `ProcessedFindings` (`FalconCollector.cs:521`) to decide whether to **throw** | **YES — decisive on the decline path** |
| Guardicore | `flow`, `NextOffset`, `HasMorePages`, `CheckpointCreatedUtc` | no |
| InsightVmCloud | `flow`, `PageCursor`, `PageSize`, `CheckpointCreatedUtc` | no |
| InsightVm | `flow`, page/`PageSize`/`TotalPages`, `CheckpointCreatedUtc` | no |
| IsbLoadTest | `flow`, `ExportUuid`, `ChunkId`, `CheckpointCreatedUtc` | no |
| MicrosoftEntraId | `flow`, `StageIndex`, `NextLink`, per-kind counters, `CheckpointCreatedUtc` | no |
| Qualys | `flow`, `CompletedBatchCount`, `PublishedPageCount`, `MaxConcurrency`, `CheckpointCreatedUtc` | no |
| SentinelOne | `flow`, `NextCursor`, `HasMorePages`, `CheckpointCreatedUtc` | no |
| ServiceNowCmdb | `flow`, `Query`, `NextOffset`, `CheckpointCreatedUtc` | no |
| Taegis | `flow`, `NextOffset`, `CheckpointCreatedUtc` | no |
| TenableIo | `flow`, `findingsFormatVersion`, `combinedRun`, then `ExportUuid`/`AssetsExportUuid`/`processedTenableChunkIds`/`CheckpointCreatedUtc` | no |
| TenableSc | `flow`, page/`PageSize`/`TotalPages`, `CheckpointCreatedUtc` | no |
| **YamlAdapter** | `AdapterState["yaml.state"]` JSON (`V`, `Strategy`, `Cursor`, `NextPage`, `Workflow`) **plus `checkpoint.CreatedAtUtc`** (`YamlAdapter.cs:450`) | **YES — decisive** |

`ResumeAsync` reads flat columns more widely — Dummy (`:322-325`), TenableSc (`:421-424`), TenableIo (`:478-479`),
Falcon assets alignment (`Flows/Assets/FalconAssetsScrollRunner.cs:207-221`) — but that is progress restoration,
not the resume decision.

---

## 4. Staleness call sites (exhaustive, with override status)

`RecoveryParsingHelper.IsCheckpointStale(DateTime, TimeSpan? threshold = null)` is defined at
`IntegrationInfra/src/IntegrationInfra/FaultGovernance/Recovery/RecoveryParsingHelper.cs:37-41`, and
`DefaultStaleThreshold = TimeSpan.FromHours(23)` at `:13`.

**24 call sites in the repo. All 24 pass one argument. Not one supplies an explicit threshold.**

| # | Call site | Flow | Threshold |
|---|---|---|---|
| 1 | `Collectors/CloudGuardCollector/Recovery/CloudGuardCheckpointHelper.cs:63` | assets | default |
| 2 | `Collectors/CloudGuardCollector/Recovery/CloudGuardCheckpointHelper.cs:80` | findings | default |
| 3 | `Collectors/CortexXdrCollector/Recovery/CortexXdrCheckpointHelper.cs:96` | assets | default |
| 4 | `Collectors/CortexXdrCollector/Recovery/CortexXdrCheckpointHelper.cs:121` | findings | default |
| 5 | `Collectors/DefenderForCloudCollector/Recovery/DefenderForCloudCheckpointHelper.cs:57` | assets | default |
| 6 | `Collectors/DefenderForCloudCollector/Recovery/DefenderForCloudCheckpointHelper.cs:74` | findings | default |
| 7 | `Collectors/DefenderVmCollector/Recovery/DefenderVmCheckpointHelper.cs:71` | assets | default |
| 8 | `Collectors/DefenderVmCollector/Recovery/DefenderVmCheckpointHelper.cs:94` | findings | default |
| 9 | `Collectors/FalconCollector/Recovery/FalconCheckpointResumePolicy.cs:81` | assets | default |
| 10 | `Collectors/FalconCollector/Recovery/FalconCheckpointResumePolicy.cs:107` | findings | default |
| 11 | `Collectors/GuardicoreCollector/Recovery/GuardicoreCheckpointHelper.cs:16` | assets | default |
| 12 | `Collectors/InsightVmCloudCollector/Recovery/InsightVmCloudCheckpointHelper.cs:33` | assets | default |
| 13 | `Collectors/InsightVmCloudCollector/Recovery/InsightVmCloudCheckpointHelper.cs:56` | findings | default |
| 14 | `Collectors/InsightVmCollector/InsightVmCollector.cs:428` | assets | default |
| 15 | `Collectors/InsightVmCollector/InsightVmCollector.cs:452` | findings | default |
| 16 | `Collectors/IsbLoadTestCollector/.../Recovery/IsbLoadTestCheckpointHelper.cs:28` | findings | default |
| 17 | `Collectors/MicrosoftEntraIdCollector/Recovery/MicrosoftEntraIdCheckpointHelper.cs:16` | assets | default |
| 18 | `Collectors/QualysCollector/Recovery/QualysCheckpointHelper.cs:16` | findings | default |
| 19 | `Collectors/SentinelOneCollector/Recovery/SentinelOneCheckpointHelper.cs:16` | assets | default |
| 20 | `Collectors/ServiceNowCmdbCollector/Recovery/ServiceNowCmdbCheckpointHelper.cs:16` | assets | default |
| 21 | `Collectors/TaegisCollector/Recovery/TaegisCheckpointHelper.cs:16` | assets | default |
| 22 | `Collectors/TenableIoCollector/Recovery/TenableIoCheckpointHelper.cs:33` | findings | default |
| 23 | `Collectors/TenableIoCollector/Recovery/TenableIoCheckpointHelper.cs:56` | assets | default |
| 24 | `Collectors/TenableScCollector/TenableScCollector.cs:337` | findings | default |

**Two collectors sit outside this table entirely:**

- **YamlAdapter — a second, independent staleness gate.**
  `YamlAdapter/Cymulate.Integration.Adapters.YamlAdapter/YamlAdapter.cs:450`:
  `if (DateTime.UtcNow - checkpoint.CreatedAtUtc > _maxCheckpointAge) return false;`
  `_maxCheckpointAge` defaults to `TimeSpan.FromHours(24)` (`YamlAdapter.cs:88`) and is **overridable at
  runtime** from adapter configuration via the `maxCheckpointAgeHours` key (`YamlAdapter.cs:52`, applied at
  `:186-191`). Two consequences for a retention change: (a) it measures against the **host's insert-only
  `CreatedAtUtc`**, so for YAML vendors this is a cap on *total run duration*, not on time-since-last-progress —
  the exact semantics Falcon explicitly reverted (`Recovery/FalconCheckpointState.cs:47-56`); (b) it is the one
  staleness threshold on the fleet that can already be lengthened **without a code change**.
- **DummyCollector — no staleness gate at all.** `Recovery/DummyCheckpointHelper.cs` parses
  `checkpointCreatedUtc` (`:69`) and never tests it. Test-only collector, but it means a Dummy checkpoint of any
  age resumes.

No other age arithmetic exists in collector recovery code — a sweep for `DateTime.UtcNow -` / `TimeSpan.FromHours`
/ `TimeSpan.FromDays` across `Collectors/` and `YamlAdapter/` returns only log-message age formatting and
unrelated flow-duration timers.

---

## 5. Decline taxonomy and whether the reason escapes

**Exactly one collector distinguishes why.** `FalconResumeDecline` at
`Collectors/FalconCollector/Recovery/FalconCheckpointResumePolicy.cs:25-39`:

| Value | Meaning (verbatim intent from the source) |
|---|---|
| `None` | resumable |
| `NoFlow` | no `flow` key or blank — "every writer emits one, so absence means a damaged blob" (`:28`) |
| `UnknownFlow` | a flow value this build doesn't recognise — "most plausibly written by a newer collector" (`:31`) |
| `LoadFailed` | known flow, deserialization failed (`:34`) |
| `Stale` | loaded cleanly, older than the age limit — "a normal restart-fresh path" (`:37`) |

**Accessibility: `internal`** (`FalconCheckpointResumePolicy.cs:25`), exposed to the collector only through an
`internal` overload on the public façade (`Recovery/FalconCheckpointHelper.cs:32-36`), whose doc comment says
so explicitly: *"Internal deliberately: the reason is a collector-internal discriminator and not part of the
checkpoint contract."*

**`CanResumeFrom`'s return type does NOT carry the reason.** `IResumableAdapter.CanResumeFrom` returns `bool`;
the reason travels through an `out` parameter on the internal overload only
(`FalconCheckpointResumePolicy.cs:60-64`).

**Does the reason escape the collector?** Not as data — but its *effect* does, in two ways, and both matter for
a retention decision:

1. **It can fail the run and get the checkpoint row deleted.** `FalconCollector.cs:513-517` documents that
   throwing from `CanResumeFrom` causes the host to delete the checkpoint row
   (`ProcessEventCommandHandler.HandleUnhandledExceptionAsync` → `TryDeleteCheckpointAsync`), and calls that an
   *undocumented host dependency*. The throw fires at `FalconCollector.cs:617-640` whenever the host's flat
   counters show durable progress **and** the blob names no recognised flow (`NoFlow`/`UnknownFlow`).
2. **Otherwise it becomes a partial completion.** For `Stale` and `LoadFailed` (both of which imply a recognised
   flow), the decline is recorded as a `RefusedResume` record (`FalconCollector.cs:642-656`) and reported out of
   `ProcessAsync` as partial success rather than discarded. The reason string reaches the operator only through
   the `LogError` at `FalconCollector.cs:583-592`.

Every other collector returns a bare `false` with a `LogWarning` and nothing structured. Tenable.io comes
closest — three distinct refusal messages (assets-checkpoint-for-correlated-run at
`Flows/Findings/Correlated/TenableIoCorrelatedCheckpoint.cs:120`, format mismatch at `:130`, combined-run assets
at `:151`) — but they are log text, not a value.

---

## 6. State-version gating

Three collectors version their persisted state; the rest do not.

- **Falcon findings — `findingsFormatVersion`, current value `4`.**
  Constant: `Recovery/FalconCheckpointState.cs:153`. Wire key: `Recovery/FalconCheckpointKeys.cs:28`. Written at
  `Recovery/FalconCheckpointSerializer.cs:49`.
  **Rejection site: `Recovery/FalconCheckpointDeserializer.cs:196-203`** — `TryLoadFindingsStateCore` returns
  false unless the parsed version equals `CurrentFormatVersion`, so absent/older/newer all fail the load, which
  surfaces upstream as `FalconResumeDecline.LoadFailed`. v2 (watermark-resume) and v3 (single-ordinal) are
  named as explicitly not accepted (`FalconCheckpointState.cs:141-151`).
  Falcon assets have **no** version field.
- **Tenable.io findings — `findingsFormatVersion`, current value `"2"` (a string).**
  Constant: `Flows/Findings/Correlated/TenableIoCorrelatedCheckpoint.cs:40`. Written at `:68`.
  **Rejection site: `Flows/Findings/Correlated/TenableIoCorrelatedCheckpoint.cs:127-136`** — ordinal string
  compare; mismatch or absence logs and returns false. A separate refusal covers retired combined-run assets
  checkpoints (`:145-156`).
- **YamlAdapter — `V`, current value `5`, with a backward-compatible allow-list.**
  `YamlAdapter/.../Recovery/YamlCollectorCheckpointState.cs:22` (`CurrentVersion = 5`) and `:31`
  (`IsSupportedVersion(int v) => v is 1 or 2 or 3 or 4 or 5`).
  **Rejection site: `YamlAdapter/.../YamlAdapter.cs:447-448`** — `state is null || !IsSupportedVersion(state.V)`
  → false. Unlike Falcon and Tenable.io this *accepts* older versions; the record's doc says the newer fields
  default to null/empty (`YamlCollectorCheckpointState.cs:20-21`). A malformed blob also yields null via the
  `JsonException` catch at `:118-121`.

No other collector's state carries a version field.

---

## 7. `ICheckpointStateCompatibility` implementations

**Zero.** A repo-wide search (excluding `bin`/`obj`) returns exactly one hit, and it is a comment:
`src/Cymulate.Integration.Adapters/Directory.Packages.props:120` — a note that
`Cymulate.IntegrationInfra 1.1.1-preview.0` "carries the recovery-outcome contract types (RunOutcome,
RunOutcomeReport, CheckpointWriteResult/Refusal, ICheckpointStateCompatibility, ResumeWaste) that the Falcon
resume path compiles against."

No type implements it and no `EvaluateStoredState` method exists anywhere in the repo. The interface is defined
in the `Cymulate.Integration.Client` package
(`Cymulate.Integration.Client.Contracts.ICheckpointStateCompatibility`, XML doc at
`/Users/user/Dev/IntegrationInfra/src/IntegrationInfra/bin/Release/net8.0/Cymulate.Integration.Client.xml:703`),
which describes it as the pre-claim question a host may ask — *"a host treats a collector that does not
implement this interface exactly as before — it claims the row and then calls `CanResumeFrom`"*. That is the
behaviour every collector in this repo gets today.

Note the mismatch worth flagging: the props comment says the Falcon resume path "compiles against" those types,
and the pinned version has since moved to `1.2.0-preview.0` (`Directory.Packages.props:123`), but no Falcon
source references the interface. The comment is stale as to `ICheckpointStateCompatibility`.

---

## 8. Falcon state keys: read vs diagnostic; self-contained vs vendor-handle

The supplied production blob is a **findings v4** checkpoint (`flow: CollectFindings`,
`findingsFormatVersion: "4"`), and its key set matches
`Recovery/FalconCheckpointSerializer.cs:44-72` exactly, plus the three `_resume.*` keys and five
`_resilience.recovery.*` keys added by other writers.

### Keys the resume path reads (decision- or position-bearing)

| Key | Read where | Role |
|---|---|---|
| `flow` | `Recovery/FalconCheckpointResumePolicy.cs:66`, `:73`, `:98` | routes assets vs findings; absence ⇒ `NoFlow` |
| `findingsFormatVersion` | `Recovery/FalconCheckpointDeserializer.cs:196-197` | hard gate; `!= 4` ⇒ load fails ⇒ restart fresh |
| `checkpointCreatedUtc` | `Recovery/FalconCheckpointResumePolicy.cs:107` | **the staleness gate** (23h default) |
| `stagingGenerationId` | `Flows/Findings/FalconFindingsFlow.cs:557-559` | names the Phase-1 generation whose frozen key list to publish from |
| `lastCompletedStagedPage` | `Flows/Findings/FalconFindingsFlow.cs:561-562`, `:278` | **half the resume position** |
| `lastCompletedBatchIndex` | `Flows/Findings/FalconFindingsFlow.cs:561-562`, `:278` | **the other half** |
| `page` | `Recovery/FalconCheckpointResumePolicy.cs:117`; deserialized `Page` | object-naming counter, restored |
| `totalItems` | `FalconCheckpointResumePolicy.cs:118` | findings counter, restored |
| `totalAssetsEmitted` | `FalconCollector.cs:655`, `FalconCheckpointResumePolicy.cs:119` | asset-envelope counter, restored; also read by the refusal path |
| `fql`, `apiPageSize`, `baseDateUtc`, `isDryRun` | deserializer `:93-130` | run-shape fields rehydrated into config |

### Diagnostic only — parsed but never used to decide or position

- `discoverWatermarkUtc` — *"Diagnostic only in the two-phase model … It is no longer a resume position"*
  (`Recovery/FalconCheckpointState.cs:196-199`).
- `discoverWatermarkAids` — *"Phase 2 resume does not consult it"* (`:203-206`).
- `aidBatchSize` — *"diagnostic only … NOT validated against the resumed configuration"* (`:209-213`).
- `stagedHostCount` — *"diagnostics: the denominator Phase 2 is working through"* (`:169`).
- `_resume.reachedPosition` / `_resume.resumedFromPosition` / `_resume.positionUnit` — written for the **host** to
  measure resume waste and detect a stalled resume; the collector never reads them back
  (`Recovery/FalconResumeReach.cs:64-76`). Their string literals are a cross-repo contract with ISB's
  `Domain/Constants/CheckpointStateKeys.cs` (`FalconResumeReach.cs:28-37`).
- `_resilience.recovery.*` (all five, empty in this blob) — substrate recovery-budget bookkeeping, owned by
  `IntegrationInfra/FaultGovernance/Logic/AdapterRecoveryBudget.cs:14-26`, not by Falcon.

### Self-contained or vendor-handle? — per flow

**Findings (`CollectFindings`), the flow in the blob: SELF-CONTAINED with respect to CrowdStrike, but dependent
on the run's own S3 staging area.**

- No vendor cursor is persisted at all: `SaveFindingsState` (`Recovery/FalconCheckpointSerializer.cs:44-72`)
  has no `afterToken` key, and `Recovery/FalconCheckpointResumePolicy.cs:10-13` states *"Correlated findings
  resume carries no persisted CrowdStrike cursors — Phase 2 walks a frozen key list from the last published
  output page — so there is no cursor-TTL sanitizer step here anymore."*
- The position `(lastCompletedStagedPage, lastCompletedBatchIndex)` is a coordinate into a **frozen, immutable
  list** — `Recovery/FalconCheckpointState.cs:174-186`: *"a position in a list that cannot move."*
- **The dependency that can expire is Cymulate's own object store, not CrowdStrike's.** The frozen list lives at
  `{storageUrl}/_staging/{generation}/` (`Flows/Findings/FalconFindingsFlow.cs:27`,
  `Flows/Findings/TwoPhase/FalconStagingPaths.cs:24-30`) inside the **run's own prefix**
  (`FalconFindingsFlow.cs:640-647`). On resume the flow reads the manifest for the named generation
  (`FalconFindingsFlow.cs:557-560`); **if a progressed checkpoint's generation manifest is gone, the run
  throws** (`FalconFindingsFlow.cs:563-573`): *"that generation's manifest is unavailable … Fail this run and
  restart from a clean generation."*
- Phase 2's Spotlight queries are re-issued per aid batch against the staged AIDs; the Spotlight scroll
  re-anchors in-process on cursor expiry (`Flows/Findings/Correlated/FalconSpotlightBatchScroller.cs:177`).

**Assets (`CollectAssets`): NOT self-contained — it persists and resumes onto a live CrowdStrike cursor.**

- `afterToken` is serialized (`Recovery/FalconCheckpointSerializer.cs:23`) and applied directly on resume:
  `Flows/Assets/FalconAssetsScrollRunner.cs:200` — `scroll.AfterToken = resumeState.AfterToken;` — then used to
  build the request URL at `:112`.
- CrowdStrike `after` cursors live **120 seconds** (`Recovery/FalconCheckpointState.cs:57-59`;
  `ai/skills/collector-recovery/SKILL.md`), so on any host-scheduled resume the token is already dead.
- The fallback: a 404 raises `FalconCursorExpiredException`, caught at
  `Flows/Assets/FalconAssetsScrollRunner.cs:131`, which re-anchors the scroll from
  `lastWatermark`/`lastWatermarkIds` (`:151`) and continues — **but only if a watermark exists**. With no
  watermark it throws `FalconResumeNotPossibleException` (`:141-145`).

So Falcon is a **split verdict by flow**, and the split is exactly where the retention question bites.

---

## 9. Week-old resume viability

Answering strictly from code read.

**Falcon findings — mechanically viable at one week, subject to one non-code dependency.**
Nothing in the findings resume path consults a vendor-side handle. The gates a 7-day-old blob would meet are:
(a) the 23h staleness check at `Recovery/FalconCheckpointResumePolicy.cs:107` — the only thing standing in the
way, and the thing under discussion; (b) the format-version equality at
`Recovery/FalconCheckpointDeserializer.cs:196-197` — satisfied as long as no v5 ships in the interim; (c) the
staging manifest lookup at `Flows/Findings/FalconFindingsFlow.cs:557-573` — **this is the real expiry risk**. If
the run's `{storageUrl}/_staging/{generation}/manifest.json` has been aged out by an S3 lifecycle rule or a
cleanup job within the week, a progressed checkpoint does not restart fresh; it **throws and fails the run**
(`FalconFindingsFlow.cs:565-572`). Whether that object survives seven days is an S3-lifecycle fact I could not
read from this repo — `SPECULATION:` flagged below.

Second-order concern, from code: `checkpointCreatedUtc` is re-stamped on every checkpoint write
(`Recovery/FalconCheckpointState.cs:41-46`), so a 7-day-old value means seven days *without a single
checkpointed batch*. A blob that old is by construction from a run that stopped, not one that is slow.

**Falcon assets — NOT viable at one week without a code change.** The persisted `afterToken` is dead after 120s
(`FalconCheckpointState.cs:57-59`). The 404 re-anchor path recovers it (`FalconAssetsScrollRunner.cs:131-153`),
so it degrades to one wasted request rather than a failure — **provided `lastWatermark` is populated**. When it
is not, `FalconResumeNotPossibleException` fails the run (`:141-145`). Relaxing the age gate for assets converts
a silent restart-fresh into a possible hard failure on watermark-less checkpoints.

**The rest of the fleet, by what their state holds** (relevant if the relaxation is fleet-wide rather than
Falcon-only):

- Genuinely self-contained coordinates, safe at any age: Guardicore `NextOffset`
  (`Recovery/GuardicoreCheckpointState.cs:7`), Taegis `NextOffset` (`:7`), ServiceNowCmdb `NextOffset` (`:8`),
  CortexXdr `NextSearchFrom` (`Recovery/CortexXdrCheckpointState.cs:7`), Qualys batch counters
  (`Recovery/QualysFindingsCheckpointState.cs:6-7`), InsightVm page/`TotalPages`
  (`Recovery/InsightVmCheckpointState.cs:6,14-15`), TenableSc page/`TotalPages`
  (`Recovery/TenableScCheckpointState.cs:9,22-23`).
- Hold an expiring vendor handle with **no watermark fallback in state**, so a week-old resume would resume onto
  a dead handle: DefenderForCloud `SkipToken` (`Recovery/DefenderForCloudCheckpointState.cs:19`), DefenderVm
  `NextUrl` (`Recovery/DefenderVmCheckpointState.cs:13`), MicrosoftEntraId `NextLink`
  (`Recovery/MicrosoftEntraIdCheckpointState.cs:8`), SentinelOne `NextCursor`
  (`Recovery/SentinelOneCheckpointState.cs:7`), InsightVmCloud `PageCursor`
  (`Recovery/InsightVmCloudCheckpointState.cs:16,22`), CloudGuard `SearchAfter`
  (`Recovery/CloudGuardCheckpointState.cs:9`).
- **Tenable.io is the clearest no.** Its state carries `exportUuid` and `assetsExportUuid`
  (`Recovery/TenableIoCheckpointKeys.cs:19,43`) — server-side export jobs — and its own staleness message says
  why the 23h limit was chosen: *"Tenable.io exports may expire after ~24 hours"*
  (`Recovery/TenableIoCheckpointHelper.cs:36`, `:59`). A week-old Tenable.io resume points at an export that no
  longer exists.
- **YamlAdapter is a per-vendor answer, and its gate is already configurable.** `cursor`/`link_header` strategies
  require a non-empty persisted `Cursor` (`YamlAdapter.cs:460`) — a live vendor handle. `offset`/`page_number`
  are self-contained (`:461`, `:463`). `scroll` and `none` are already excluded from the allow-list
  (`YamlAdapter.cs:78`) precisely because *"Server-side scroll contexts expire"* (`:458-459`). Note that
  raising `maxCheckpointAgeHours` (`YamlAdapter.cs:52,186-191`) would relax the age gate for **all** strategies
  at once, including the cursor family.

**Bottom line for the retention decision:** retaining a failed run's checkpoint for a week is mechanically
useful for Falcon findings and for the offset/page-number collectors. It is inert-to-harmful for every collector
whose position is a vendor handle, and actively wrong for Tenable.io. The age gate is uniform today (§4), so
relaxing it in the shared helper relaxes it for all of them at once.

---

## Contradictions found against stated beliefs

1. **CONTRADICTED — "Collector `CanResumeFrom` consumes ONLY the `adapter_state` blob, never ISB's flat
   columns."** Three collectors read flat columns, two of them decisively:
   - **YamlAdapter** gates staleness on `checkpoint.CreatedAtUtc` (`YamlAdapter.cs:450`) — a flat, insert-only
     host column, not `adapter_state`. This is the single most consequential contradiction: for every YAML
     vendor the age gate measures total run age, not time since last progress.
   - **Falcon** reads `CurrentPage`/`ProcessedItems`/`ProcessedFindings` (`FalconCollector.cs:521`) and on that
     basis either throws (failing the run and causing the host to delete the checkpoint row,
     `FalconCollector.cs:513-517`) or records a partial completion.
   - **DummyCollector** returns `true` from `CanResumeFrom` on `checkpoint.CurrentPage > 1` alone when
     `AdapterState` is empty (`DummyCollector.cs:249-255`).
   The belief is correct for the other 15.
2. **CONTRADICTED — "Prior work found collectors (Tenable.io, AgentService) with no resume path at all."**
   Tenable.io has a full resume implementation: `IResumableAdapter` at `TenableIoCollector.cs:34`, a
   `Recovery/` folder with helper + resume runner, a correlated-format gate
   (`Flows/Findings/Correlated/TenableIoCorrelatedCheckpoint.cs:81`) and two staleness call sites. AgentService
   does not exist in this repository. **The denominator is 18 of 18, not a subset.**
3. **CONFIRMED — "Every collector staleness call site uses the shared default with no override."** All 24
   `RecoveryParsingHelper.IsCheckpointStale` calls pass one argument (§4). But the belief is *incomplete*: it
   silently assumes that helper is the only staleness mechanism. YamlAdapter has a second one with its own
   default (24h) that **is** runtime-overridable, and DummyCollector has none.
4. **CONFIRMED — "`ICheckpointStateCompatibility` has zero implementations in this repo."** One hit, a stale
   comment in `Directory.Packages.props:120`.
5. **CONFIRMED — "`FalconResumeDecline` distinguishes damaged blob (NoFlow/UnknownFlow/LoadFailed) from
   merely-old (Stale), and is internal to Falcon."** `FalconCheckpointResumePolicy.cs:25-39`; internal, and the
   façade overload is internal too (`FalconCheckpointHelper.cs:32-36`). Refinement: the reason does not escape
   as a value, but its *consequence* escapes as either a thrown exception (which gets the checkpoint row
   deleted) or a partial-completion DONE payload.
6. **CONFIRMED — "Falcon's correlated findings resume carries no persisted vendor cursors."**
   `FalconCheckpointSerializer.cs:44-72` writes no `afterToken`; `FalconCheckpointResumePolicy.cs:10-13` states
   it. **Important qualification the belief omits:** Falcon's **assets** flow does persist and resume onto a
   CrowdStrike `after` cursor (`FalconAssetsScrollRunner.cs:200`), and the findings flow depends instead on the
   run's own S3 staging manifest, whose absence on a progressed checkpoint is a hard failure
   (`FalconFindingsFlow.cs:563-573`).

---

## Speculation (explicitly labelled)

- `SPECULATION:` Whether a Falcon run's `{storageUrl}/_staging/{generation}/` objects survive seven days is an
  S3 lifecycle / cleanup-job question. Nothing in this repo configures or reads a retention policy for it —
  `FalconStagingArea` receives an optional `IAdapterObjectPruner` from host DI
  (`FalconFindingsFlow.cs:660-661`) and only ever deletes *abandoned* generations within a run
  (`FalconFindingsFlow.cs:623`). This must be verified outside this repo before treating a week-old Falcon
  findings resume as safe.
- `SPECULATION:` The vendor-side TTLs quoted for the non-Falcon handles (Azure ARG `SkipToken`, Graph
  `NextLink`, SentinelOne cursor, InsightVM Cloud `PageCursor`, CloudGuard `SearchAfter`) are inferred from the
  handle's nature and from each collector's own choice to gate at ~23h; I did not find a documented TTL for any
  of them in this repo. Tenable.io's ~24h is the one exception — stated in the code at
  `Recovery/TenableIoCheckpointHelper.cs:36`.
- `SPECULATION:` Whether ISB actually re-invokes `CanResumeFrom` on a week-old retained row, and in what order
  relative to claiming it, is ISB-side behaviour outside my scope (W1's brief). The only ISB behaviour I read
  from this repo is a comment asserting that a throw from `CanResumeFrom` leads to
  `ProcessEventCommandHandler.HandleUnhandledExceptionAsync` → `TryDeleteCheckpointAsync`
  (`FalconCollector.cs:513-517`) — that should be verified against ISB source, since a retention feature that
  keeps rows for a week is directly undermined by a delete-on-failure path.
