# Internal Recon

## Durable sources read

- `/Users/user/Dev/IntegrationServiceBus/CLAUDE.md` — layout, dependency direction, DI conventions, "no `var` in new code", do-not-commit working agreement.
- `/Users/user/Dev/IntegrationServiceBus/docs/coding-standards.md` — naming (`docs/coding-standards.md:36-45` explicit types), parameter ordering tail group (`:67-83`), comment bar (`:123-141`).
- `/Users/user/Dev/IntegrationServiceBus/docs/clean-architecture.md` — port declared in the layer that consumes it, adapter outward; options live with the code that reads them; "give every new option a working default".

Not re-read (already audited in the prior recon): the checkpoint lifecycle narrative.

## Files in scope

### 1. `FlushAndCleanupCheckpointAsync`

`/Users/user/Dev/IntegrationServiceBus/Applications/.../ProcessEventCommandHandler.cs` — full path
`src/Cymulate.IntegrationServiceBus/Applications/Cymulate.IntegrationServiceBus.Application/Commands/ProcessEventCommandHandler.cs`

- Signature — `:1213-1218`
  ```csharp
  private async Task<AdapterResult?> FlushAndCleanupCheckpointAsync(
      AdapterExecutionContext? executionContext,
      string tenantId,
      PlatformEvent platformEvent,
      AdapterCategory category)
  ```
  No `AdapterResult` parameter, no `CancellationToken`. Returns `null` on the normal path (`:1241`); returns a non-null `AdapterResult` **only** as the refused-write rejection.
- **Refused-write early return — `:1219-1224`** (must keep precedence over any retention branch):
  ```csharp
  if (executionContext != null)
  {
      await executionContext.AwaitLastCheckpointAsync();
      if (executionContext.HasRejectedCheckpointWrite)
          return await HandleRejectedCheckpointWriteAsync(tenantId, platformEvent, category, executionContext);
  }
  ```
- Unconditional delete it guards — `:1226-1239` (`checkpointRepository.DeleteAsync(...)` in a try/catch that logs `"Failed to clean up checkpoint … TTL job will handle it."`).
- **Sole call site** — `:814-817`, inside `CompleteExecutionAsync`:
  ```csharp
  var checkpointRejectionResult = await FlushAndCleanupCheckpointAsync(
      executionContext, tenantId, platformEvent, category);
  if (checkpointRejectionResult != null)
      return checkpointRejectionResult;
  ```
- **`CompleteExecutionAsync` has the `AdapterResult` in hand at the call site** — it is parameter `result` at `:811`, and the method already branches on `result.Success` at `:831` / `:853`. Signature `:805-814`; params in order: `executionContext, completionTracker, tenantId, platformEvent, category, result, cancellationToken`.
- Only other reference to the method name: a comment in `Applications/.../Services/CheckpointRecoveryHandler.cs:420`.

### 2. `TryDeleteCheckpointAsync`

- Definition — `ProcessEventCommandHandler.cs:1093-1106`
  ```csharp
  private async Task TryDeleteCheckpointAsync(string tenantId, PlatformEvent platformEvent, AdapterCategory category)
  ```
  try/`checkpointRepository.DeleteAsync(tenantId, platformEvent.CorrelationId, platformEvent.ProductType, category)`/catch-log-warning. No cancellation token passed (repo default).
- Exactly **two** call sites, as expected:
  - `:790` — inside `HandleAdapterNotFoundAsync` (`:775-801`). Result built at `:792-794` = `AdapterResult.FailureResult(..., "ADAPTER_NOT_FOUND")`.
  - `:880` — inside **`HandleUnhandledExceptionAsync`** (`:867-`). Result built at `:882-885` = `AdapterResult.FailureResult(..., "UNHANDLED_EXCEPTION", ex)`.

### 3. `CheckpointEntry`

`src/Cymulate.IntegrationServiceBus/Domain/Cymulate.IntegrationServiceBus.Domain/Models/CheckpointEntry.cs`

| line | property | type |
| --- | --- | --- |
| 11 | `Id` | `Guid` |
| 17 | `TenantId` | `string` (= `string.Empty`) |
| 22 | `CorrelationId` | `string` (= `string.Empty`) |
| 27 | `PlatformType` | `PlatformType` |
| 32 | `Category` | `AdapterCategory` |
| 37 | `CurrentPage` | `int` |
| 42 | `ProcessedItems` | `int` |
| 47 | `ProcessedFindings` | `int` |
| 53 | `SequenceId` | `int` |
| 58 | `CursorToken` | `string?` |
| 63 | `LastProcessedId` | `string?` |
| 68 | `AdapterStateJson` | `string?` |
| 74 | `PlatformEventJson` | `string?` |
| 80 | `ClaimedByInstance` | `string?` |
| **86** | **`ClaimedAtUtc`** | **`DateTime?`** |
| 91 | `CreatedAtUtc` | `DateTime` |
| 96 | `UpdatedAtUtc` | `DateTime` |
| 102 | `Status` | `CheckpointStatus` (= `Idle`) |
| **108** | **`ScheduledResumeAtUtc`** | **`DateTime?`** |
| 114 | `CheckpointReason` | `string?` |
| 120 | `CheckpointKind` | `string?` |
| 126 | `CheckpointItemsInBatch` | `int?` |
| 132 | `CheckpointFindingsInBatch` | `int?` |

Both existing `DateTime?` properties (`:86`, `:108`) are plain auto-properties with a 2–3 line `<summary>` and no attributes. A new one mirrors that exactly. Class is a plain `class` with `{ get; set; }` — not a record, no primary constructor.

### 4. `CheckpointDbContext`

`src/Cymulate.IntegrationServiceBus/Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Postgres/Persistence/CheckpointDbContext.cs`

- `adapter_checkpoints` block: `:30-117`. `entity.ToTable("adapter_checkpoints")` `:32`, `HasKey(e => e.Id)` `:34`.
- **How an existing nullable timestamp is mapped — no `HasColumnType`, name only:**
  - `:79` `entity.Property(e => e.ClaimedAtUtc).HasColumnName("claimed_at_utc");`
  - `:93` `entity.Property(e => e.ScheduledResumeAtUtc).HasColumnName("scheduled_resume_at_utc");`
  A `DateTime?` maps to `timestamp with time zone` by Npgsql convention (confirmed in `Migrations/CheckpointDbContextModelSnapshot.cs:108-110` and `Migrations/20260526144503_...cs:14-18`). The new property gets the same one-liner, placed after `:93` to keep declaration order aligned with `CheckpointEntry`.
- **All indexes on the table:**
  | name | columns | declared at |
  | --- | --- | --- |
  | `ix_adapter_checkpoints_tenant_correlation_platform_category` (UNIQUE) | TenantId, CorrelationId, PlatformType, Category | `CheckpointDbContext.cs:106-108` |
  | `ix_adapter_checkpoints_tenant_claim` | TenantId, ClaimedByInstance, ClaimedAtUtc | `:111-112` |
  | `ix_adapter_checkpoints_updated_at_utc` | UpdatedAtUtc | `:115-116` |
  | `ix_checkpoints_scheduled_wait` (partial, `WHERE status = 'ScheduledWait'`) | scheduled_resume_at_utc | **raw SQL only**, `Migrations/20260526144503_AddCheckpointStatusAndScheduledResume.cs:29-33`; not in the model or the snapshot |

### 5. `UpsertAsync` (Postgres) — raw SQL

`src/Cymulate.IntegrationServiceBus/Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Postgres/Persistence/CheckpointRepository.cs:24-…`, SQL string `:56-127`.

- **INSERT column list — `:59-65`:** `checkpoint_id, tenant_id, correlation_id, platform_type, category, current_page, processed_items, processed_findings, sequence_id, cursor_token, last_processed_id, adapter_state_json, platform_event_json, claimed_by_instance, claimed_at_utc, status, scheduled_resume_at_utc, checkpoint_reason, checkpoint_kind, checkpoint_items_in_batch, checkpoint_findings_in_batch, created_at_utc, updated_at_utc`
- The INSERT is a `SELECT … WHERE NOT EXISTS (stop request)` (`:67-80`), not a `VALUES`.
- **`ON CONFLICT (tenant_id, correlation_id, platform_type, category) DO UPDATE SET` — `:81-96`:** `current_page, processed_items, processed_findings, sequence_id, cursor_token, last_processed_id, adapter_state_json, claimed_at_utc, status, scheduled_resume_at_utc, checkpoint_reason, checkpoint_kind, checkpoint_items_in_batch, checkpoint_findings_in_batch, updated_at_utc`. Note `platform_event_json` and `claimed_by_instance` are deliberately absent (`:42-47` comment).
- **WHERE guard on the DO UPDATE — `:97-109`:**
  ```sql
  WHERE adapter_checkpoints.status <> 'ScheduledWait'
    AND adapter_checkpoints.current_page <= EXCLUDED.current_page
    AND NOT EXISTS (SELECT 1 FROM adapter_stop_requests sr WHERE sr.correlation_id = EXCLUDED.correlation_id)
    AND ((EXCLUDED.claimed_by_instance IS NOT NULL AND adapter_checkpoints.claimed_by_instance = EXCLUDED.claimed_by_instance)
         OR (EXCLUDED.claimed_by_instance IS NULL AND adapter_checkpoints.claimed_by_instance IS NULL))
  ```
- Outer classification SELECT `:112-127`; parameter binding `:147-169`; `TimestampTz` helper `:367`; `AsUtc` `:392`.
- **Does adding a column require editing this SQL? NO — and it must not be edited.** The statement names every column explicitly. Omitting `retain_until_utc` means: on INSERT the column takes its (absent) default → `NULL`; on conflict the `DO UPDATE SET` list does not mention it → **the stored value is preserved verbatim**. That is exactly the required behaviour: **a later upsert cannot clear a previously-set `retain_until_utc`.** Adding it to either list would break that.
- Second raw upsert, `TryAcquireExecutionLockAsync` `:573-`, INSERT list `:596-`, `ON CONFLICT … DO UPDATE SET` `:610-`. Same reasoning — leave it alone. SPECULATION: worth a deliberate decision whether acquiring a fresh execution lock on a retained row should *clear* retention; as written it will not.

### 6. `GetRecoverableAsync`

- **Postgres** — `CheckpointRepository.cs:658-675`; predicate `:670-672`:
  ```csharp
  .Where(c => c.Status != CheckpointStatus.ScheduledWait
              && (c.ClaimedByInstance == null || c.ClaimedAtUtc < staleBeforeUtc))
  .Where(CheckpointPartition.OwnedByPartitionPredicate(tenantId));
  ```
  `staleBeforeUtc = DateTime.UtcNow - staleThreshold` at `:666`.
- **InMemory** — `src/Cymulate.IntegrationServiceBus/Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Core/Services/InMemoryCheckpointRepository.cs:264-276`; predicate `:272-273`:
  ```csharp
  .Where(c => c.Status != CheckpointStatus.ScheduledWait)
  .Where(ownedByPartition)
  ```
  (no staleness filter — single process).
- Only production call site: `Applications/.../Services/CheckpointRecoveryHandler.cs:85`.

### 7. `DeleteExpiredAsync`

- Interface — `Domain/.../Interfaces/ICheckpointRepository.cs:55`, `Task<int> DeleteExpiredAsync(DateTime cutoffUtc, CancellationToken cancellationToken = default)`.
- **Postgres** — `CheckpointRepository.cs:441-449`; predicate `:447`:
  ```csharp
  .Where(c => c.UpdatedAtUtc < cutoffUtc && c.Status != CheckpointStatus.ScheduledWait)
  .ExecuteDeleteAsync(cancellationToken);
  ```
- **InMemory** — `InMemoryCheckpointRepository.cs:152-163`; predicate `:155`:
  ```csharp
  .Where(kvp => kvp.Value.UpdatedAtUtc < cutoffUtc && kvp.Value.Status != CheckpointStatus.ScheduledWait)
  ```

### 8. `CheckpointCleanupJob`

`src/Cymulate.IntegrationServiceBus/Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Core/Jobs/CheckpointCleanupJob.cs`

- Constants `:17-30`: `JobName`, `GroupName`, `DefaultIntervalMinutes = 30`, `DefaultTtlHours = ConfigurationKeys.Checkpoint.DefaultTtlHours` (`:25`), `TtlHoursKey = "TtlHours"` (`:30`).
- Reads the map — `:38-40`:
  ```csharp
  var ttlHours = context.MergedJobDataMap.ContainsKey(TtlHoursKey)
      ? context.MergedJobDataMap.GetInt(TtlHoursKey)
      : DefaultTtlHours;
  ```
  Then `cutoff = DateTime.UtcNow.AddHours(-ttlHours)` `:42`. Order of work: `DeleteStoppedCheckpointsAsync` `:49` → `DeleteExpiredAsync(cutoff)` `:58` → `stopRequestRepository.DeleteExpiredAsync(cutoff)` `:73`. Failure wraps in `JobExecutionException(ex, refireImmediately: false)` `:84`.
- **How it gets there** — `Infrastructure.Core/DependencyInjection.cs:274-301`:
  ```csharp
  var checkpointTtlHours = CheckpointCleanupJob.DefaultTtlHours;                    // :275
  var ttlConfig = configuration[ConfigurationKeys.Checkpoint.TtlHours];             // :276
  if (!string.IsNullOrEmpty(ttlConfig) && int.TryParse(ttlConfig, out var parsedTtl))
      checkpointTtlHours = parsedTtl;                                               // :277-280
  ...
  q.AddJob<CheckpointCleanupJob>(opts => opts
      .WithIdentity(checkpointCleanupJobKey)
      .UsingJobData(CheckpointCleanupJob.TtlHoursKey, checkpointTtlHours.ToString())); // :290-292
  ```
  A second key follows this shape verbatim: const on the job, `configuration[...]` + `int.TryParse` block above `AddJob`, one more chained `.UsingJobData(Key, value.ToString())`.
  *Whether a second key is needed at all is a design call: deleting by `retain_until_utc <= now()` needs no configuration in the job.* See **Shared surface**.

### 9. `ConfigurationKeys.Checkpoint`

`src/Cymulate.IntegrationServiceBus/Domain/Cymulate.IntegrationServiceBus.Domain/Constants/ConfigurationKeys.cs:340-358` — the block in full:

```csharp
public static class Checkpoint
{
    public const string TtlHours = "Checkpoint:TtlHours";
    public const string CleanupIntervalMinutes = "Checkpoint:CleanupIntervalMinutes";
    public const string RecoveryIntervalMinutes = "Checkpoint:RecoveryIntervalMinutes";
    public const string ShutdownDrainTimeoutSeconds = "Checkpoint:ShutdownDrainTimeoutSeconds";
    public const string StaleClaimThresholdMinutes = "Checkpoint:StaleClaimThresholdMinutes";
    public const string HeartbeatIntervalMinutes = "Checkpoint:HeartbeatIntervalMinutes";
    public const int DefaultStaleClaimThresholdMinutes = 10;
    public const int DefaultHeartbeatIntervalMinutes = 5;

    /// <summary> … 5-line summary … </summary>
    public const int DefaultTtlHours = 24;                                     // :357
}
```

Style: key strings first, then `Default*` ints; only the one with a cross-component invariant carries a doc comment. No `Checkpoint` section exists in any host `appsettings.json` (grepped `Hosts/*/appsettings*.json` — zero hits), so defaults live only in these constants.

Reader-side pattern in the Application layer — `ProcessEventCommandHandler.cs:51-57`:
```csharp
private TimeSpan StaleClaimThreshold => TimeSpan.FromMinutes(
    configuration.GetValue(ConfigurationKeys.Checkpoint.StaleClaimThresholdMinutes,
        ConfigurationKeys.Checkpoint.DefaultStaleClaimThresholdMinutes));
```
`IConfiguration configuration` is already a primary-constructor parameter of the handler (`:34`), so no new dependency is needed to read a retention setting there. Same pattern in `CheckpointRecoveryHandler.cs:706-707`.

### 10. Migrations

`src/Cymulate.IntegrationServiceBus/Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Postgres/Migrations/`

| timestamp | name |
| --- | --- |
| 20250101000000 | InitialCreate |
| 20250101000001 | SnakeCaseColumns |
| 20250101000002 | CursorTokenToText |
| 20250101000003 | AddPlatformEventJson |
| 20250101000004 | AddCheckpointClaim |
| 20250101000005 | AddSequenceId |
| 20250101000006 | RenameClaimColumn |
| 20250101000007 | AddTenantId |
| 20260526144503 | AddCheckpointStatusAndScheduledResume |
| 20260604183000 | AddCheckpointSnapshotMetadata |
| **20260610120000** | **AddAdapterStopRequests** (most recent) |

Convention: `yyyyMMddHHmmss_PascalCaseName.cs` + `…​.Designer.cs`, plus **one** shared `CheckpointDbContextModelSnapshot.cs`. The `20250101*` block is hand-numbered; the three 2026 ones use real timestamps, the last two rounded (`183000`, `120000`). A new one must sort after `20260610120000`.

Structure of the most recent (`20260610120000_AddAdapterStopRequests.cs`):
- `using System; using Microsoft.EntityFrameworkCore.Migrations;`, `#nullable disable`, namespace `Cymulate.IntegrationServiceBus.Infrastructure.Postgres.Migrations`, `/// <inheritdoc />` on the class and on both `Up`/`Down`.
- `Up` `:12-31` / `Down` `:33-38`.
- Companion `20260610120000_AddAdapterStopRequests.Designer.cs`: `// <auto-generated />`, `[DbContext(typeof(CheckpointDbContext))]`, `[Migration("20260610120000_AddAdapterStopRequests")]`, `partial class AddAdapterStopRequests` with `BuildTargetModel`, `HasAnnotation("ProductVersion", "8.0.27")`.

The closest template for this change is `20260526144503_AddCheckpointStatusAndScheduledResume.cs:14-18`:
```csharp
migrationBuilder.AddColumn<DateTime>(
    name: "scheduled_resume_at_utc",
    table: "adapter_checkpoints",
    type: "timestamp with time zone",
    nullable: true);
```
with `Down` = `migrationBuilder.DropColumn(name: …, table: "adapter_checkpoints")`.

**A new additive nullable-column migration must include:** (a) `<timestamp>_AddCheckpointRetainUntil.cs` with `Up` = one `AddColumn<DateTime>(… nullable: true)` and `Down` = one `DropColumn`; (b) the `.Designer.cs` companion carrying the *post*-change model; (c) an edit to `CheckpointDbContextModelSnapshot.cs` adding the `b.Property<DateTime?>("RetainUntilUtc")` block (mirror `:108-110`); (d) if a supporting index is wanted, either an EF `HasIndex` in the DbContext **and** a `CreateIndex` in the migration, or — for a partial index — `migrationBuilder.Sql(...)` + `DROP INDEX IF EXISTS` in `Down`, as `20260526144503_...cs:29-33 / :39` does. Generating with `dotnet ef migrations add` produces (a)–(c) correctly; hand-writing means all three must be kept consistent or `MigrateAsync()` in `CheckpointRepositoryTests.InitializeAsync` (`:68`) will fail.

The second migration folder, `Infrastructure.Core/Migrations/Operational/`, ends in `20250101000003_RemoveCheckpoints` and `OperationalDbContextModelSnapshot.cs` contains **zero** `CheckpointEntry` references — the SQLite operational context no longer maps this entity. **No second migration is needed.**

### 11. Existing tests that encode current behaviour

Searched `src/Cymulate.IntegrationServiceBus/Tests/` and `.../UnitTests/`.

**Tests asserting a failed run deletes its checkpoint: none.** Every `Verify(r => r.DeleteAsync(...))` in the suite asserts `Times.Never`:

| path:line | test | assertion |
| --- | --- | --- |
| `UnitTests/Cymulate.IntegrationServiceBus.Application.UnitTests/ProcessEventCommandHandlerTests.cs:617-619` | (claim-loss / rejected-write scenario ending `:595` `SuccessResult("ok")`) | `DeleteAsync` `Times.Never` |
| `…/ProcessEventCommandHandlerTests.cs:793-795` | same family | `Times.Never` |
| `…/ProcessEventCommandHandlerTests.cs:1310-1312` | bounce/cancel scenario | `Times.Never` |
| `…/ProcessEventCommandHandlerTests.cs:1375-1377` | `Handle_keeps_same_owner_rejection_on_existing_handoff_path` region | `Times.Never` |
| `…/ProcessEventCommandHandlerTests.cs:1515-1520` | scenario asserting `Assert.NotEqual(AdapterResultStatus.Cancelled, result.Status)` `:1505` | `DeleteAsync` and `DeleteByCorrelationIdAsync` `Times.Never` |

So **no existing unit test breaks by replacing the failure-path delete with a retention stamp.** The mock is `Mock<ICheckpointRepository>` (Moq), so a *new* repository method is `null`-safe by default only if it returns a value type / `Task`; a strict-mock or a `Task<bool>`-returning method needs `.Setup(...)` in the fixture — check `_checkpointRepository` construction before adding one.

`GetRecoverableAsync` / `DeleteExpiredAsync` tests:

| path:line | test |
| --- | --- |
| `Tests/Cymulate.IntegrationServiceBus.API.UnitTests/Checkpoints/CheckpointRepositoryTests.cs:314-332` | `DeleteExpiredAsync_does_not_delete_ScheduledWait_regardless_of_age` |
| `…/CheckpointRepositoryTests.cs:338-356` | `GetRecoverableAsync_excludes_ScheduledWait_rows` |
| `…/StoppingIsNotEndingTests.cs:212-244` | `An_unclassified_refusal_stops_the_execution_and_leaves_the_run_recoverable` (asserts via `GetRecoverableAsync` at `:237`) |
| `…/StoppingIsNotEndingTests.cs:245-…` | `A_stop_request_ends_the_run_and_the_sweep_does_not_offer_it_again` (`GetRecoverableAsync` `:261` `.Should().BeEmpty()`) |

Both `StoppingIsNotEndingTests.cs:71-93` and `CheckpointRecoveryCompatibilityTests.cs:606-625` contain **hand-written `ICheckpointRepository` decorators that forward every member**. Adding a method to the interface **breaks compilation of both** until each gains a forwarding member.

`CheckpointCleanupJob`: **no test exists anywhere** (zero hits across `Tests/` and `UnitTests/`).

### 12. Test conventions

- `CheckpointRepositoryTests` lives in `src/Cymulate.IntegrationServiceBus/Tests/Cymulate.IntegrationServiceBus.API.UnitTests/Checkpoints/CheckpointRepositoryTests.cs` — **Testcontainers**, not InMemory: `PostgreSqlBuilder("postgres:16-alpine")` `:20`, `IAsyncLifetime` `:18`, `await db.Database.MigrateAsync()` `:68`. Docker-gated (see `CLAUDE.md` — `DockerUnavailableException` is expected offline).
- Arrangement: `Sut` property `:73`; private `SeedCheckpointAsync(...)` `:80-120` writes rows **directly via the DbContext** deliberately, "to avoid going through UpsertAsync's ON CONFLICT logic" (`:75-79`) — a retention test seeds `RetainUntilUtc` the same way, by adding an optional `DateTime? retainUntilUtc = null` parameter to that helper. `SeedStopRequestAsync` `:122-`. Assertions are FluentAssertions (`.Should()...`), `[Fact]`, snake_case-ish descriptive test names, and section banners (`// ──── Task N: <behaviour>`) grouping related facts (`:310-312`, `:334-336`).
- The InMemory store's own behaviour is covered from `UnitTests/Cymulate.IntegrationServiceBus.Infrastructure.Core.UnitTests/InMemoryCheckpointRepositoryRefusalTests.cs` — that is where an InMemory-parity test for the new predicate belongs (no Docker needed).
- Handler-level behaviour is covered in `UnitTests/Cymulate.IntegrationServiceBus.Application.UnitTests/ProcessEventCommandHandlerTests.cs` with Moq + `Verify(..., Times.X)`.

### 13. `AdapterResult` — what indicates failure

From the pinned package `Cymulate.Integration.Client` **1.2.0-preview.0** (`Directory.Packages.props:127`), decompiled from `~/.nuget/packages/cymulate.integration.client/1.2.0-preview.0/lib/net8.0/Cymulate.Integration.Client.dll`:

**Both.** `bool Success { get; init; }` and `AdapterResultStatus Status { get; init; }`, and the XML doc on `Status` says *"New consumers should switch on Status instead of Success. Success remains a settable init property for backwards binary compatibility."*

```csharp
public enum AdapterResultStatus { Success, PartialWaitRequired, Failure, TransientFailure, ValidationFailure, Skipped, Cancelled }
```

Factory → (`Status`, `Success`) mapping, verbatim from the decompiled source:

| factory | `Status` | `Success` |
| --- | --- | --- |
| `SuccessResult` | `Success` | **true** |
| `FailureResult` | `TransientFailure` or `Failure` (decided by the `NonTransientErrorCodes` set) | **false** |
| `TransientFailure` | `TransientFailure` | **false** |
| `ValidationFailure` | `ValidationFailure` | **false** |
| `SkippedResult` | `Skipped` | **true** |
| `CancelledResult` | `Cancelled` | **false** |
| `PartialResult` | `PartialWaitRequired` | **false** |

`NonTransientErrorCodes` includes `ADAPTER_NOT_FOUND`, `CLAIM_LOST`, `EXECUTION_LOCKED`, `OPERATION_CANCELLED`, `AUTH_FAILED`, … — so `FailureResult(..., "ADAPTER_NOT_FOUND")` yields `Status = Failure` (not transient).

Keying the retention branch **on the reported outcome without inspecting error codes** therefore means:
`result.Status is AdapterResultStatus.Failure or AdapterResultStatus.TransientFailure or AdapterResultStatus.ValidationFailure`.
`!result.Success` is *not* equivalent — it also captures `Cancelled` and `PartialWaitRequired`.

## Patterns to mirror

- **Nullable timestamp property** — `CheckpointEntry.cs:104-108` (`ScheduledResumeAtUtc`): plain `public DateTime? X { get; set; }` with a two-line `<summary>` stating when it is null.
- **DbContext mapping** — `CheckpointDbContext.cs:93`: `entity.Property(e => e.X).HasColumnName("x");` — one line, no `HasColumnType`, blank line between properties.
- **Additive migration** — `20260526144503_AddCheckpointStatusAndScheduledResume.cs:14-18` / `:41-43`.
- **Config key + default** — `ConfigurationKeys.cs:342-357`; reader `ProcessEventCommandHandler.cs:51-53` (`configuration.GetValue(Key, Default)` expression-bodied property).
- **Quartz job data** — `DependencyInjection.cs:275-292`.
- **Surgical, owner-guarded UPDATE on one row** — `CheckpointRepository.TransitionToScheduledWaitAsync` `:732-762` is the exact precedent for "set one nullable timestamp + `updated_at_utc` and touch nothing else"; its interface doc is `ICheckpointRepository.cs:171-188`. `ExecuteSqlInterpolatedAsync` form at `:773-779`.
- **Interface doc style** — `ICheckpointRepository.cs`: `<summary>` naming the guard, `<returns>` naming the boolean's meaning.
- **Explicit types in new code** — `docs/coding-standards.md:36-45`. Note the surrounding repository and handler are heavily `var`; do not copy that.

## Shared surface to freeze

Two workers code against exactly this. Nothing here is negotiable mid-flight.

1. **Domain property** — `CheckpointEntry.cs`, inserted after `ScheduledResumeAtUtc` (`:108`):
   ```csharp
   public DateTime? RetainUntilUtc { get; set; }
   ```
   Nullable. `null` = no retention hold (every existing row, and every live run).

2. **Column** — `retain_until_utc`, type `timestamp with time zone`, `nullable: true`, no default, on table `adapter_checkpoints`.
   Mapping line in `CheckpointDbContext.cs` (after `:93`):
   ```csharp
   entity.Property(e => e.RetainUntilUtc).HasColumnName("retain_until_utc");
   ```
   Snapshot block to add to `CheckpointDbContextModelSnapshot.cs`:
   ```csharp
   b.Property<DateTime?>("RetainUntilUtc")
       .HasColumnType("timestamp with time zone")
       .HasColumnName("retain_until_utc");
   ```

3. **Config key + default** — added to `ConfigurationKeys.Checkpoint` (`ConfigurationKeys.cs:340-358`):
   ```csharp
   public const string FailedRetentionDays = "Checkpoint:FailedRetentionDays";
   public const int DefaultFailedRetentionDays = 7;
   ```
   Read in the Application layer via the existing injected `IConfiguration` (`ProcessEventCommandHandler.cs:34`), as an expression-bodied property mirroring `:51-53`:
   ```csharp
   private TimeSpan FailedRetention => TimeSpan.FromDays(
       configuration.GetValue(ConfigurationKeys.Checkpoint.FailedRetentionDays,
           ConfigurationKeys.Checkpoint.DefaultFailedRetentionDays));
   ```
   **No new Quartz job-data key.** The cleanup job deletes on `retain_until_utc <= now()`; the 7 days is stamped at write time, so `CheckpointCleanupJob` needs no second `UsingJobData` and `DependencyInjection.cs` is untouched. (If the plan owner wants the horizon owned by the job instead, that is a different design and must be decided before either worker starts — it moves the config read from `Application` to `Infrastructure.Core`.)

4. **New repository method** — added to `ICheckpointRepository` (`ICheckpointRepository.cs`), placed immediately after `DeleteAsync` (`:41`), signature frozen as:
   ```csharp
   Task<bool> SetRetentionAsync(
       string tenantId,
       string correlationId,
       PlatformType platformType,
       AdapterCategory category,
       DateTime retainUntilUtc,
       CancellationToken cancellationToken = default);
   ```
   Returns `true` when a row was updated, `false` when none matched — same shape and doc style as `TransitionToScheduledWaitAsync` (`:181-188`). It is a **surgical UPDATE** of `retain_until_utc` (and `updated_at_utc`) only. Parameter order follows the existing composite-key convention with the new value before the tail token (`docs/coding-standards.md:67-83`).
   Implementations required in **both** stores: `CheckpointRepository.cs` (Postgres, mirror `TransitionToScheduledWaitAsync:732-762`) and `InMemoryCheckpointRepository.cs`.
   **Every hand-written `ICheckpointRepository` decorator must gain a forwarding member** — `StoppingIsNotEndingTests.cs:71-93` and `CheckpointRecoveryCompatibilityTests.cs:606-625`. Compilation breaks otherwise.

5. **Existing repository signatures that change: none.** `GetRecoverableAsync` and `DeleteExpiredAsync` keep their signatures; only their predicates change:
   - `GetRecoverableAsync` gains `&& c.RetainUntilUtc == null` — Postgres `CheckpointRepository.cs:670-671`, InMemory `InMemoryCheckpointRepository.cs:272`.
   - `DeleteExpiredAsync` becomes "delete when (`UpdatedAtUtc < cutoffUtc` **and** no retention hold) **or** the hold has passed", still exempting `ScheduledWait` — Postgres `:447`, InMemory `:155`. Frozen predicate for both:
     ```
     Status != ScheduledWait
     && (RetainUntilUtc == null ? UpdatedAtUtc < cutoffUtc : RetainUntilUtc <= nowUtc)
     ```
     `nowUtc` is `DateTime.UtcNow` read once at the top of the method (do not add a parameter — `ICheckpointRepository.cs:55` is a wire-stable signature used by the job at `CheckpointCleanupJob.cs:58`).

6. **Failure predicate for stamping** — frozen, keyed on `Status` only, never on `ErrorCode`:
   ```csharp
   result.Status is AdapterResultStatus.Failure
                 or AdapterResultStatus.TransientFailure
                 or AdapterResultStatus.ValidationFailure
   ```
   No `CheckpointStatus` enum member is added (`Domain/.../Enums/CheckpointStatus.cs` untouched), per the contract.

## Disjoint sets available

**Partially — and the natural split is *not* `src/` vs tests.** The interface change in item 4 forces edits inside two test files, and the test fixtures depend on the Domain property. A clean two-worker split with no shared file:

**Set A — schema + persistence (Infrastructure.Postgres + Domain + Infrastructure.Core)**
- `src/Cymulate.IntegrationServiceBus/Domain/Cymulate.IntegrationServiceBus.Domain/Models/CheckpointEntry.cs`
- `src/Cymulate.IntegrationServiceBus/Domain/Cymulate.IntegrationServiceBus.Domain/Interfaces/ICheckpointRepository.cs`
- `src/Cymulate.IntegrationServiceBus/Domain/Cymulate.IntegrationServiceBus.Domain/Constants/ConfigurationKeys.cs`
- `src/Cymulate.IntegrationServiceBus/Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Postgres/Persistence/CheckpointDbContext.cs`
- `src/Cymulate.IntegrationServiceBus/Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Postgres/Persistence/CheckpointRepository.cs`
- `src/Cymulate.IntegrationServiceBus/Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Postgres/Migrations/<new>_AddCheckpointRetainUntil.cs` + `.Designer.cs`
- `src/Cymulate.IntegrationServiceBus/Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Postgres/Migrations/CheckpointDbContextModelSnapshot.cs`
- `src/Cymulate.IntegrationServiceBus/Infrastructure/Cymulate.IntegrationServiceBus.Infrastructure.Core/Services/InMemoryCheckpointRepository.cs`

**Set B — lifecycle + tests (Application + all test projects)**
- `src/Cymulate.IntegrationServiceBus/Applications/Cymulate.IntegrationServiceBus.Application/Commands/ProcessEventCommandHandler.cs`
- `src/Cymulate.IntegrationServiceBus/Tests/Cymulate.IntegrationServiceBus.API.UnitTests/Checkpoints/CheckpointRepositoryTests.cs`
- `src/Cymulate.IntegrationServiceBus/Tests/Cymulate.IntegrationServiceBus.API.UnitTests/Checkpoints/StoppingIsNotEndingTests.cs` *(forwarding member only)*
- `src/Cymulate.IntegrationServiceBus/Tests/Cymulate.IntegrationServiceBus.API.UnitTests/Checkpoints/CheckpointRecoveryCompatibilityTests.cs` *(forwarding member only)*
- `src/Cymulate.IntegrationServiceBus/UnitTests/Cymulate.IntegrationServiceBus.Application.UnitTests/ProcessEventCommandHandlerTests.cs`
- `src/Cymulate.IntegrationServiceBus/UnitTests/Cymulate.IntegrationServiceBus.Infrastructure.Core.UnitTests/InMemoryCheckpointRepositoryRefusalTests.cs`

The sets are file-disjoint. B does not compile until A lands the interface member and the Domain property — so **A must be ordered first**, or B must code strictly against the frozen surface above and accept a red build until A merges. `Infrastructure.Core/Jobs/CheckpointCleanupJob.cs` and `Infrastructure.Core/DependencyInjection.cs` are in **neither** set: nothing in this design touches them.

## Landmines

1. **Do not add `retain_until_utc` to either raw upsert.** `CheckpointRepository.cs:59-65` (INSERT list) and `:81-96` (`DO UPDATE SET` list) name every column explicitly. Leaving the new column out of both is what makes a later upsert *preserve* a set `retain_until_utc`; adding it to `DO UPDATE SET` would silently clear the hold on the next checkpoint write, and adding it to the INSERT list without a matching `SELECT` expression will not even compile as SQL. Same for `TryAcquireExecutionLockAsync` (`:596`, `:610`).

2. **`!result.Success` is the wrong predicate.** `CancelledResult` and `PartialResult` both set `Success = false` (decompiled, section 13). `PartialWaitRequired` returns at `ProcessEventCommandHandler.cs:284-288` before `CompleteExecutionAsync`, but `Cancelled` can reach it — an adapter that returns `CancelledResult` itself is not routed away. Retaining a cancelled run's checkpoint contradicts the stop path, which deletes by correlation id (`:800` region, `TryDeleteCheckpointsByCorrelationIdAsync`). Key on the three failure `Status` members only.

3. **`SkippedResult` sets `Success = true`.** The native-fallback branch (`:292-297`) returns before `CompleteExecutionAsync` only when `TryEnqueueNativeFallbackAsync` succeeds; a `Skipped` result that falls through reaches `CompleteExecutionAsync` with `Success == true` and must **not** be retained. A `Status`-based predicate handles this for free; a `!Success` predicate would not (and would also not retain it — different bug, same root).

4. **The refused-write early return must keep precedence.** `FlushAndCleanupCheckpointAsync:1219-1224` returns `HandleRejectedCheckpointWriteAsync(...)` *before* any cleanup. A retention branch inserted above it would stamp `retain_until_utc` on a row this execution no longer owns — the whole point of that guard is that a claim-lost execution touches nothing. Put the retention decision **after** `:1224` and in place of the delete at `:1226-1239`, or pass the outcome down from `CompleteExecutionAsync:814` and branch there.

5. **`FlushAndCleanupCheckpointAsync` has no `AdapterResult` parameter today.** Adding one goes at the **end** of the real parameter list per `docs/coding-standards.md:67-83`. There is exactly one call site (`:814`) so this is cheap — but note the method takes no `CancellationToken` either, and the new repository call should follow that (the existing `DeleteAsync` at `:1228` is called without one).

6. **`TryDeleteCheckpointAsync`'s two call sites are not symmetric.** `HandleAdapterNotFoundAsync` (`:790`) has no `executionContext` and never wrote a checkpoint — there may be no row at all, so `SetRetentionAsync` returns `false` and that is normal, not an error. `HandleUnhandledExceptionAsync` (`:880`) *does* run after a possible partial collection and is the case retention exists for. Whether both switch to retention, or only the second, is a scope decision the contract must state — silently changing both changes the adapter-not-found story too.

7. **Two hand-written `ICheckpointRepository` decorators break the build.** `Tests/.../StoppingIsNotEndingTests.cs:71-93` and `Tests/.../CheckpointRecoveryCompatibilityTests.cs:606-625` forward every interface member explicitly. Adding `SetRetentionAsync` to the interface requires a forwarding member in each. This is a compile error, not a test failure — it will look like an unrelated breakage.

8. **`ix_checkpoints_scheduled_wait` exists only in raw SQL, not in the model.** `20260526144503_...cs:29-33`. If a retention index is added, do not expect `dotnet ef migrations add` to notice or reproduce that one; and do not "fix" its absence from the snapshot.

9. **The recovery sweep has a TTL-derived give-up bound.** `CheckpointRecoveryHandler.cs:701-707` reads `ConfigurationKeys.Checkpoint.TtlHours` so its terminal age (`UnservableTerminalAge`, three quarters of the TTL — `:713-728`) cannot drift past the deletion horizon. A 7-day retention hold is **longer than the 24h TTL**, so a retained row now outlives that bound while being invisible to the sweep (item 5 predicate). That is intended here, but it means the doc comments at `:692-698` and `CheckpointCleanupJob.cs:21-25` — both of which assert the two horizons are the same number — become **wrong for retained rows**. Update them in the same change or leave a knowingly-stale comment, which `docs/coding-standards.md:131-135` explicitly forbids.

10. **`DeleteStoppedCheckpointsAsync` ignores everything.** `CheckpointRepository.cs:768-782` deletes by correlation-id membership in `adapter_stop_requests` with no status or retention predicate, and `CheckpointCleanupJob.cs:49` runs it **first**, before `DeleteExpiredAsync`. A retained failed run whose correlation id later gets a stop request is deleted immediately, retention hold or not. SPECULATION: probably correct (an explicit stop should win), but it is unguarded and nobody will notice it.

11. **`CheckpointCleanupJob` has zero test coverage.** Nothing in `Tests/` or `UnitTests/` references it. A behaviour change to `DeleteExpiredAsync`'s predicate is only caught by `CheckpointRepositoryTests` (Docker-gated) and the InMemory tests — so on a machine without Docker the Postgres predicate change is **completely unverified**. Say so rather than reporting green.

12. **`CheckpointRepositoryTests` runs `MigrateAsync()`** (`:68`). A hand-written migration whose `.Designer.cs` or snapshot disagrees with the DbContext fails there with an EF model error that reads nothing like "you forgot to update the snapshot".

13. **Mocks in `ProcessEventCommandHandlerTests`.** `_checkpointRepository` is a Moq mock; a new `Task<bool>` method returns `false` under a loose mock by default, which silently means "no row updated". Any test asserting the retention stamp must `.Setup(...)` it and `.Verify(...)` the timestamp argument with a tolerance — `DateTime.UtcNow.AddDays(7)` is computed inside the handler and will never equal a value computed in the test.
