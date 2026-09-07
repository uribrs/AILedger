## W1 — schema + persistence

### Files changed

| file | change |
| --- | --- |
| `Domain/.../Models/CheckpointEntry.cs` | `+ public DateTime? RetainUntilUtc { get; set; }` after `ScheduledResumeAtUtc`; `+ CredentialsPropertyName` const |
| `Domain/.../Interfaces/ICheckpointRepository.cs` | `+ SetRetentionAsync(...)` after `DeleteAsync`, signature verbatim from recon §4 |
| `Domain/.../Constants/ConfigurationKeys.cs` | `+ Checkpoint.FailedRetentionDays` key, `+ DefaultFailedRetentionDays = 7`, `+ MaxFailedRetentionDays = 30`, `+ NormalizeFailedRetentionDays(int)` |
| `Infrastructure.Postgres/Persistence/CheckpointDbContext.cs` | `+ entity.Property(e => e.RetainUntilUtc).HasColumnName("retain_until_utc");` after the `scheduled_resume_at_utc` line |
| `Infrastructure.Postgres/Persistence/CheckpointRepository.cs` | `+ SetRetentionAsync` (owner-guarded; releases the claim and strips credentials); predicates on `GetRecoverableAsync` / `DeleteExpiredAsync`; `retain_until_utc = NULL` in the lock upsert |
| `Infrastructure.Postgres/Migrations/20260827124500_AddCheckpointRetainUntil.cs` | new |
| `Infrastructure.Postgres/Migrations/20260827124500_AddCheckpointRetainUntil.Designer.cs` | new |
| `Infrastructure.Postgres/Migrations/CheckpointDbContextModelSnapshot.cs` | `+ RetainUntilUtc` property block |
| `Infrastructure.Core/Services/InMemoryCheckpointRepository.cs` | `+ SetRetentionAsync`; same two predicates; two preserve-guards; lock path clears the hold; `StripCredentials` helper |

Not touched, deliberately (R1): both raw upserts in `CheckpointRepository.cs` — the `UpsertAsync`
INSERT list / `DO UPDATE SET`, and `TryAcquireExecutionLockAsync`. `retain_until_utc` now appears
exactly once in that file, inside `SetRetentionAsync`. Verified by grep.

### Migration

`20260827124500_AddCheckpointRetainUntil` — sorts after `20260610120000_AddAdapterStopRequests`.
`Up` = one `AddColumn<DateTime>(name: "retain_until_utc", table: "adapter_checkpoints",
type: "timestamp with time zone", nullable: true)`; `Down` = one `DropColumn`. No index.

`dotnet ef` is not installed on this machine (`dotnet ef --version` → command not found), so all three
artefacts were hand-written. Landmine 12 says a Designer or snapshot that disagrees with the DbContext
fails `MigrateAsync()` with an error that reads nothing like the real cause, so here is the actual
check rather than an assertion that they agree:

1. Compared the `#pragma warning disable 612, 618` … `#pragma warning restore` model body of the
   previous migration's `20260610120000_AddAdapterStopRequests.Designer.cs` against the same span of
   `CheckpointDbContextModelSnapshot.cs`. **Byte-identical** — so on this context EF's Designer body
   and its snapshot body are the same text, and the snapshot is a valid source for a new Designer.
2. Added the `RetainUntilUtc` block to the snapshot (alphabetical position, between `ProcessedItems`
   and `ScheduledResumeAtUtc`, matching EF's own ordering).
3. Generated `20260827124500_AddCheckpointRetainUntil.Designer.cs` from that updated snapshot,
   substituting only the header: `+ using Microsoft.EntityFrameworkCore.Migrations;`,
   `+ [Migration("20260827124500_AddCheckpointRetainUntil")]`, class renamed, `ModelSnapshot` base and
   `BuildModel` → `BuildTargetModel`.

So Designer and snapshot agree by construction rather than by inspection, and both agree with the
DbContext mapping because the added block is the property EF emits for
`entity.Property(e => e.RetainUntilUtc).HasColumnName("retain_until_utc")` on a `DateTime?`. This is
still weaker evidence than a real `MigrateAsync()` — see Build.

### Predicates

**`GetRecoverableAsync`**

- Postgres before: `Status != ScheduledWait && (ClaimedByInstance == null || ClaimedAtUtc < staleBeforeUtc)`
- Postgres after: `Status != ScheduledWait && (RetainUntilUtc == null || RetainUntilUtc <= nowUtc) && (ClaimedByInstance == null || ClaimedAtUtc < staleBeforeUtc)`
- InMemory before: `Status != ScheduledWait`
- InMemory after: `Status != ScheduledWait && (RetainUntilUtc == null || RetainUntilUtc <= nowUtc)`

Reviewer B2 hardened this from `RetainUntilUtc == null`. The two predicates have to agree on what a
hold means, and they did not: `GetRecoverableAsync` excluded on null-ness while `DeleteExpiredAsync`
excluded on the deadline. A row whose hold had expired was therefore invisible to the sweep **and**,
if anything kept writing to it, safe from deletion — permanently stranded, no done published. Both now
read "no hold outstanding".

**`DeleteExpiredAsync`** (signature unchanged; `nowUtc = DateTime.UtcNow` read once at the top of each method)

- Postgres before: `UpdatedAtUtc < cutoffUtc && Status != ScheduledWait`
- InMemory before: `UpdatedAtUtc < cutoffUtc && Status != ScheduledWait`
- Both after: `Status != ScheduledWait && (RetainUntilUtc == null ? UpdatedAtUtc < cutoffUtc : RetainUntilUtc <= nowUtc)`

**`GetRecoverableAsync`**

- Postgres after: `Status != ScheduledWait && RetainUntilUtc == null && (ClaimedByInstance == null || ClaimedAtUtc < staleBeforeUtc)`
- InMemory after: `Status != ScheduledWait && RetainUntilUtc == null`

### Why the two predicates deliberately DISAGREE

This is the single most-revised decision in the change, and it ended where the frozen contract started.
Anyone reading the two side by side will see `RetainUntilUtc == null` in one and `RetainUntilUtc <= nowUtc`
in the other and reach for consistency. **That disagreement is the design.** The queries answer different
questions:

| query | question | answer |
| --- | --- | --- |
| `GetRecoverableAsync` | should this run be re-dispatched? | **never**, while a hold exists |
| `DeleteExpiredAsync` | should this row be deleted? | **yes**, the moment the hold lapses |

Making them agree — `(RetainUntilUtc == null \|\| RetainUntilUtc <= nowUtc)` in both — reintroduces the
bug the whole feature exists to avoid. An expired hold satisfies every clause of `GetRecoverableAsync`:
status is still `Idle` (stamping does not touch it) and `claimed_by_instance` is NULL (the stamp released
it). `CheckpointRecoveryJob` ticks at 5 minutes against `CheckpointCleanupJob`'s 30, so after expiry the
sweep almost always wins the race — a re-dispatched collection and a second done message for a
correlation id the platform closed a week earlier, for **every** retained row, on schedule. The
delete-on-failure this feature replaces made that impossible.

Revision history, so nobody re-derives a superseded step:

1. Frozen ternary + `RetainUntilUtc == null` — the original contract. **This is what shipped.**
2. Ternary → OR form, for the query plan. Reverted: correctness beats the plan, and held rows are rare.
3. `DeleteExpiredAsync` gains a leading `UpdatedAtUtc < cutoffUtc`, to stop a resumed run being deleted
   mid-flight. Reverted: clear-on-takeover (B2) already solves that — a resumed run has no hold, so it
   lands in the `null` branch and its fresh `updated_at_utc` protects it. The clause was solving a
   problem that was already solved, and it broke deletion of genuinely expired holds.
4. `GetRecoverableAsync` made to agree with `DeleteExpiredAsync`. Reverted for the reason above.

The `CASE` plan cost of the ternary is accepted: held rows are rare, and correctness wins.

### SQL translation — verified, emitted predicates quoted

Verified without Docker: a throwaway console app builds a `CheckpointDbContext` on the Npgsql provider
and calls `ToQueryString()` (no connection opened). The predicates under test are **spliced
programmatically out of `CheckpointRepository.cs` by balanced-paren extraction**, not retyped, so what
was measured is the text that ships.

```sql
-- DeleteExpiredAsync
WHERE a.status <> 'ScheduledWait' AND CASE
    WHEN a.retain_until_utc IS NULL THEN a.updated_at_utc < @__cutoffUtc_0
    ELSE a.retain_until_utc <= @__nowUtc_1
END

-- GetRecoverableAsync
WHERE a.status <> 'ScheduledWait' AND a.retain_until_utc IS NULL
  AND (a.claimed_by_instance IS NULL OR a.claimed_at_utc < @__staleBeforeUtc_0)
```

### Retention and the claim: what a hold means (reviewer B1/B2)

An independent code review reversed one earlier decision and found one regression we were introducing.
Both changed what a hold *is*, so they are recorded together.

**A hold means "this run is dead; keep its state so someone can retry it."** Two consequences follow,
and neither held in the first cut:

1. **A retained row must never be left owned (B1).** The delete this feature replaces took the claim
   with it. Stamping a hold instead left `claimed_by_instance` / `claimed_at_utc` set and freshly
   heartbeated. A `TransientFailure` is redelivered on the same correlation id
   (`IsbPlatformEventDispatcher.cs:258`); the redelivery hits `TryAcquireExecutionLockAsync`, is denied
   on every other replica until the 10-minute stale threshold passes, and `EXECUTION_LOCKED` is **not**
   in `NonTransientErrorCodes` — so it re-retries, burns the budget and dead-letters. `SetRetentionAsync`
   now clears both claim columns **in the same UPDATE** that stamps the hold, in both stores. Same
   statement, not a follow-up call.

2. **Taking the execution lock ends the hold (B2).** This **reverses** the earlier F1 guard.
   Preserving a hold across `TryAcquireExecutionLockAsync` was wrong: the moment something takes the
   lock the run is alive again, and a row that stays held is invisible to `GetRecoverableAsync` for
   life while its moving `updated_at_utc` also keeps it out of `DeleteExpiredAsync` — stranded, no done
   published. Postgres now sets `retain_until_utc = NULL` in the lock upsert's `DO UPDATE SET`; the
   in-memory path clears it rather than carrying it over.

**This supersedes part of R1.** R1 said not to touch either raw upsert, `TryAcquireExecutionLockAsync`
included. That still holds for `UpsertAsync` — an ordinary progress write must preserve a hold — but no
longer for the lock, which now deliberately clears it. The distinction is takeover versus progress, not
"upserts don't mention the column".

### The two InMemory preserve-guards that remain

R1's preserve half still has to be defended by hand in the in-memory store, which has no column-naming
SQL to get it for free. Both are ordinary progress writes, where preserving is right:

1. `TransitionFromScheduledWaitAsync` rebuilds the entry field by field. Added
   `RetainUntilUtc = existing.RetainUntilUtc` — a copy list that omits a new field silently drops it.
2. `UpsertAsync` replaces the whole stored entry (`_store.TryUpdate(key, entry, existing)`). Added,
   immediately before the swap:
   `if (existing.RetainUntilUtc != null && entry.RetainUntilUtc == null) entry.RetainUntilUtc = existing.RetainUntilUtc;`

The third guard, on `TryAcquireExecutionLockAsync`, was **removed** — see B2 above.

**Pre-existing and deliberately NOT fixed.** `TryAcquireExecutionLockAsync`'s wholesale
`_store[key] = entry` also discards `CurrentPage`, `CursorToken` and `AdapterStateJson` on every lock
acquisition, none of which the Postgres lock upsert touches. That predates this change and is out of
scope — repairing it would widen a retention change into a checkpoint-fidelity change.

### I3 — the stamp strips credentials from the stored platform event

`platform_event_json` is a full `JsonSerializer.Serialize(platformEvent)` (`ProcessEventCommandHandler.cs:139`),
and `PlatformEvent.Credentials` is **not reliably encrypted** — `EventsController.cs:479-484` assigns
`GetCredentials()` output directly and the client package's own doc says "may be encrypted". Until now
that row was deleted at the end of every run. Retention would extend the life of stored plaintext vendor
credentials to seven days by default and thirty by configuration, on exactly the runs a human is most
likely to open and read. So `SetRetentionAsync` strips them in the same UPDATE that stamps the hold.

**The key casing was confirmed by execution, not by reading.** A probe referencing the pinned
`Cymulate.Integration.Client` 1.2.0-preview.0 reflected over the type and serialized an instance the way
the handler does — `JsonSerializer.Serialize(platformEvent)`, no options:

- `PlatformEvent.Credentials` is `Dictionary<string, string>`, carries **no** `[JsonPropertyName]`
- default options apply no naming policy, so the emitted key is top-level **`"Credentials"`**, PascalCase
- observed output: `...,"Headers":null,"Credentials":{"api_token":"SENTINEL-SECRET"},"RetryCount":0,...`

**The key is derived, not hardcoded.** A wrong literal here fails *silently* — the UPDATE succeeds and
the credentials stay — so the key is named once, in the Domain entity that owns the column:

```csharp
// CheckpointEntry.cs
public const string CredentialsPropertyName = nameof(Cymulate.Integration.Client.Models.PlatformEvent.Credentials);
```

Fully qualified rather than adding a `using`, because `PlatformType` and `AdapterCategory` exist in both
`Domain.Enums` and the client's `Models` namespace. Both stores reference that constant:

- Postgres: `platform_event_json = platform_event_json - {CheckpointEntry.CredentialsPropertyName}::text`
  — parameterised, cast so `jsonb - text` resolves unambiguously. A NULL column stays NULL.
- In-memory: `StripCredentials` parses with `JsonNode` and `payload.Remove(CheckpointEntry.CredentialsPropertyName)`.

What that buys and what it does not: a **rename** of the source property is now a compile error rather
than a silent leak. It does **not** defend against someone adding a `[JsonPropertyName]` or a naming
policy later — the constant would still compile and still be wrong. That half is W3's test, which
serializes a real `PlatformEvent` and asserts the emitted key equals this same constant. The constant
being public on the entity is what makes that assertion meaningful; a test with its own hardcoded
`"Credentials"` would pass while production leaked.

Verified after wiring, by running the probe against the compiled constant:

```
CheckpointEntry.CredentialsPropertyName = "Credentials"
serializer emits                       = "Credentials"
MATCH — the SQL strip targets the key that is actually written
```

**For whoever builds the retry endpoint: credentials must be re-supplied, not read back off the
checkpoint.** This is safe with the rest of the design — a held row is never sweep-eligible, so nothing
re-dispatches it automatically, and the backend supplies credentials on every run anyway.

### Smaller review findings

- **I1 — `SetRetentionAsync` is now owner-guarded.** Every sibling surgical write guards on the claim;
  this one did not, so a claim-lost execution could stamp a hold on a row it no longer owned
  (landmine 4's failure mode). It now takes `instanceId` and matches
  `TransitionToScheduledWaitAsync`'s `AND claimed_by_instance = {instanceId}`. **This changes the
  frozen signature** — `instanceId` sits after `category` and before `retainUntilUtc`, mirroring the
  sibling. Consequences are in Build.
- **N1 — `SetRetentionAsync` now routes `retainUntilUtc` through the class's `AsUtc` helper**, so a
  caller's `Unspecified`-kind value is not handed to a `timestamp with time zone` column raw.
- **N2 / F7 — `ConfigurationKeys.Checkpoint.NormalizeFailedRetentionDays(int)` bounds the window at
  both ends**, and is the single place that policy lives; W2's `FailedRetention` routes through it.
  - **Low end** — `0` or negative falls back to `DefaultFailedRetentionDays`. Such a value stamps a
    deadline at or before now, which reads as a hold but deletes the row on the next sweep: retention
    silently off rather than visibly misconfigured.
  - **High end (F7)** — capped at `MaxFailedRetentionDays = 30`. The unit trap is the reason: the
    original contract specified the window as **168 hours** and the implementation landed as **7 days**,
    so an operator half-remembering the first and setting `Checkpoint:FailedRetentionDays=168` would get
    168 days — rows unreaped for half a year, silently, with the feature appearing to work. 30 is
    comfortably above any deliberate value for a one-week feature and far below the failure mode.
  - Both ends clamp rather than throw: a config typo should not stop a host from starting.

  Renaming the key to `…RetentionHours` was considered and rejected by the plan owner — it churns three
  workers' files for a naming preference and reintroduces the same trap in the other direction (`=7`
  meaning seven hours). A bounded range is the stronger guarantee.
- **N3 — BOMs stripped** from both hand-written migration files. The convention is genuinely mixed
  across this folder (the `20250101*` block has none, `20260526144503` has them on both files), but the
  most recent pair, `20260610120000_AddAdapterStopRequests`, has none — so no-BOM is the current
  precedent. `CheckpointDbContextModelSnapshot.cs` keeps its pre-existing BOM; stripping it would be
  diff noise in a file this change only appends to.

### Build

`dotnet build src/Cymulate.IntegrationServiceBus/Cymulate.IntegrationServiceBus.sln` — **Build
succeeded, 0 errors.** W2's call site now passes `InstanceId`, so the I1 signature change is absorbed.
`NU1900` on every project as expected.

Tests were **not** run this round, by instruction: W3 is mid-rewrite on the tests that pin the
expired-hold behaviour, and a result from that file would be misleading in either direction.

**The Postgres side remains UNVERIFIED at runtime, and this round widened that gap.** `docker info`
fails on this machine, so `MigrateAsync()` has never executed. The `ToQueryString()` output above
covers the two LINQ predicates only. Everything else this change added to Postgres lives in
hand-written raw SQL that has never run:

- `SetRetentionAsync`'s UPDATE — the claim release, the ownership guard, and
  `platform_event_json - 'Credentials'`
- `retain_until_utc = NULL` in `TryAcquireExecutionLockAsync`'s `DO UPDATE SET`
- the migration's `AddColumn`

The jsonb strip is still the one worth a live check before merge — not for the key any more (that is
compile-derived and test-asserted now), but for the statement itself: `jsonb - text` against a real
column, and the `::text` cast resolving as intended. The InMemory mirror is coverable without Docker and
is the cheaper half of that assurance.

## W3 — tests, round 3 (F1 reversal, B1, I3, I4)

### The frozen surface moved again, mid-round

`ICheckpointRepository.SetRetentionAsync` gained a `string instanceId` parameter (fifth, before
`retainUntilUtc`) and is now **owner-guarded** — it returns false unless the row is still claimed by
that instance. I absorbed the new signature across every file I own: both decorators, the six Moq
matchers in `ProcessEventCommandHandlerTests.cs`, and every call in both retention suites.

The guard is new contract surface with no coverage of its own, so it gained one test per store:
`SetRetentionAsync_is_refused_when_the_row_is_owned_by_another_instance`. It is the store-level echo of
R3 — an execution that already lost the row cannot hold it.

### F1 reversed — the lock clears the hold

`TryAcquireExecutionLockAsync_does_not_clear_an_existing_retention_hold` →
**`TryAcquireExecutionLockAsync_clears_an_existing_retention_hold`**, both stores. A hold means the run
is dead and its state is kept for a retry; taking the lock *is* that retry, so the hold must go.

The `UpsertAsync` preserve-tests are unchanged, and `TransitionFromScheduledWaitAsync` gained the
preserve-test the brief assumed existed (`TransitionFromScheduledWaitAsync_does_not_clear_an_existing_retention_hold`,
in-memory) so both sides of the write-vs-takeover distinction are pinned.

### B1 / I4 — the stamp releases the claim

`SetRetentionAsync_sets_the_hold_and_leaves_the_rest_of_the_row_alone` positively asserted
`ClaimedByInstance == "pod-a"`, which is the B1 defect. Rewritten as
**`SetRetentionAsync_stamps_the_hold_releases_the_claim_and_keeps_the_progress`**: claim columns null,
page/items/sequence/adapter-state intact. In-memory counterpart:
`SetRetentionAsync_releases_the_claim_and_keeps_the_progress`.

Added to both stores: `A_stamped_row_can_be_claimed_again_immediately_by_another_instance` — the
redelivery the broker sends straight after a TransientFailure has to be able to run.

### I3 — seeding no longer uses the mechanism under test

`SeedHeldAsync` now writes a plain row and stamps the hold through `SetRetentionAsync`, then sets
`UpdatedAtUtc` directly on the stored entry (the stamp bumps it, and the deletion predicate reads it).
`Entry`'s `retainUntilUtc` parameter is gone, so no test hands a pre-held entry to `UpsertAsync`.

**R1 is the one exception, deliberately.** Because the stamp now releases the claim, a held row is
unowned, and the in-memory store refuses a write against an unowned row as *orphaned* before any
preserve logic runs. R1 would have failed on the orphan guard and proved nothing about retention, so it
sets `RetainUntilUtc` directly on the stored row to keep it claimed. This is the "set the field
directly if that is cleaner" half of I3.

Worth noting for the plan owner: with B1 fixed, R1's scenario is close to unreachable in production —
a held row is unclaimed, and the path back to claimed runs through the execution lock, which now clears
the hold. The test still pins the write-vs-takeover distinction, which is why it was kept.

### GetRecoverableAsync predicate

`GetRecoverableAsync_excludes_rows_held_by_retention` → `..._whose_hold_is_still_active`, plus a new
`GetRecoverableAsync_offers_a_row_whose_hold_has_expired` in both stores.

### W2 — repair pass 2 (I1 call site + N2 clamp)

One file: `ProcessEventCommandHandler.cs`. Both edits are inside `TryRetainCheckpointAsync` / `FailedRetention`;
no other logic touched.

**I1 — `SetRetentionAsync` call site updated for the ownership guard.** `instanceId` now sits after `category`
and before `retainUntilUtc`. The call at `:1163` passes the handler's static `InstanceId` (`:42`), matching the
three sibling ownership-guarded writes (`TransitionToScheduledWaitAsync:961`, `RenewClaimAsync:1053`,
`ReleaseClaimAsync:1083`). This was the solution's single compile error.

Consequential one-line fix in the same method: the `retained == false` branch logged "No checkpoint row to
hold". With the guard in place `false` now also means "a row exists but this instance no longer owns it", so
the message read as a fact that is no longer implied. Now "No checkpoint row owned by this instance to hold
for …" — true for both causes. Debug level unchanged; still not treated as an error.

No logic change was needed for the two behaviours W1 added underneath: clearing `claimed_by_instance` /
`claimed_at_utc` in the same UPDATE, and the execution lock clearing a hold. Both sit below this seam.
`TryDeleteCheckpointUnlessRetainedAsync` still reads through the unguarded `GetAsync`, which is correct — it
is asking whether a hold exists at all, not claiming ownership of it.

**N2 — retention window clamped.** `FailedRetention` (`:59-62`) now wraps its `GetValue` in
`ConfigurationKeys.Checkpoint.NormalizeFailedRetentionDays(...)`, so a zero or negative
`Checkpoint:FailedRetentionDays` falls back to the 7-day default instead of stamping a deadline at or before
now — which the next cleanup pass would have reaped, turning the feature off silently. Policy stays beside the
constant; only the read is in this layer.

Note this clamps the low end only. F7's hazard — an operator setting `168` and getting 168 **days** — is not
addressed by `NormalizeFailedRetentionDays` and remains open.

**Build:** `dotnet build …sln` → **succeeded, 0 Error(s), 0 Warning(s)** (excluding `NU1900`). No tests were
run by W2 this round; `Infrastructure.Core.UnitTests` should now be executable again, but W3 is concurrently
inverting behaviour-pinning tests, so nothing here is offered as runtime evidence for the retention path.

### Results — round 3

| project | result |
| --- | --- |
| `UnitTests/…Infrastructure.Core.UnitTests` | **Passed — 417/417**, 0 failed (15 in the retention class) |
| `UnitTests/…Application.UnitTests` | **Passed — 93/93**, 0 failed |
| `Tests/…API.UnitTests` — `StoppingIsNotEndingTests` + `CheckpointRecoveryCompatibilityTests` | **Passed — 25/25** (Docker-free; proves both decorators' new 6-arg forwarding at runtime) |
| `Tests/…API.UnitTests` — `CheckpointRepositoryTests` | **NOT RUN — 37/37 failed with `DockerUnavailableException`** |
| solution build | **Build succeeded** |

No spec-vs-implementation mismatch: every test written to this round's spec passes against W1's code,
so the F1 reversal, the B1 claim release and the owner guard are all landed and correct in-memory.

**Timing note.** Mid-round the tree was un-buildable for several minutes because
`ProcessEventCommandHandler.cs:1158` still called the 6-arg `SetRetentionAsync` with 5 arguments while
W2 was updating it. Nothing of mine was implicated; I waited for it rather than touching that file.

**Build warnings are baseline, not new.** A clean `--no-incremental` solution build emits 26 `CS1573`
XML-doc warnings (Domain SiemRules files) and two `CS9113` "parameter 'siemRulesStorageLocator' is
unread" warnings. None of those source lines differ from `origin/dev` — the earlier rounds' "0
Warning(s)" readings were incremental builds that skipped those projects.

**The Postgres side of every retention behaviour remains unverified on this machine.** All 37
`CheckpointRepositoryTests` — the 13 retention ones and the 24 that pre-date this change — fail
identically in ~260ms on `DotNet.Testcontainers.Builders.DockerUnavailableException`. That now includes
the B1 claim-release UPDATE, the owner guard, the lock clearing the hold, and both corrected predicates.
Run this suite with Docker up before the branch is trusted.

### Re-verification after the build went green

Everything in the follow-up brief (owner-guard signature, decorator updates, owner-guard test) had
already been absorbed during round 3, so no further code changes were needed. Re-ran all four suites
against the current tree to confirm the numbers postdate W2's call-site fix rather than the stale
412/0 from round 2:

| project | result |
| --- | --- |
| `UnitTests/…Infrastructure.Core.UnitTests` | **Passed — 417/417** |
| `UnitTests/…Application.UnitTests` | **Passed — 93/93** |
| `Tests/…API.UnitTests` — Docker-free checkpoint classes | **Passed — 25/25** |
| `Tests/…API.UnitTests` — `CheckpointRepositoryTests` | **NOT RUN — 37/37 `DockerUnavailableException`** |
| solution build | **Build succeeded** |

## W3 — tests, round 4 (sweep/cleanup reversal + credential strip)

### Reversal 1 — a hold hides a row from the sweep, expired or not

Two tests inverted, and the sibling pair renamed so no name implies the predicates track each other:

| store | was | now |
| --- | --- | --- |
| in-memory | `GetRecoverableAsync_offers_a_row_whose_hold_has_expired` | `GetRecoverableAsync_never_offers_a_row_whose_hold_has_expired` (asserts empty) |
| in-memory | `..._never_offers_a_row_whose_hold_is_still_active` | `GetRecoverableAsync_never_offers_a_row_carrying_a_hold` |
| Postgres | `GetRecoverableAsync_offers_a_row_whose_hold_has_expired` | `GetRecoverableAsync_excludes_a_row_whose_hold_has_expired` (asserts empty) |
| Postgres | `..._excludes_rows_whose_hold_is_still_active` | `GetRecoverableAsync_excludes_rows_carrying_a_hold` |

The stranding rationale in the old doc comments is gone, replaced with why the two predicates differ:
the sweep asks *should this be re-run?*, the cleanup asks *should this be deleted?*.

### Reversal 2 — an elapsed hold is its own deletion trigger

`DeleteExpiredAsync_keeps_a_held_row_whose_deadline_passed_while_it_is_still_progressing` →
**`DeleteExpiredAsync_deletes_a_held_row_at_its_deadline_however_recently_written`**, both stores; the
assertion flips from survives to deleted. The ordinary-TTL, unheld and `ScheduledWait` cases were
re-checked against the reverted ternary and all still hold unchanged.

Round 2 changed these same two rows in the opposite direction, so for the record the assertion has now
been: delete-on-hold (round 2, wrong per F2) → survive-if-recent (round 3, per F2) → delete-on-hold
(round 4, per this reversal). The round-4 shape matches the original frozen predicate in the recon.

### New — credential stripping (I3)

`SetRetentionAsync_strips_credentials_from_the_stored_platform_event`, both stores. Seeds
`platform_event_json` with a real serialized `PlatformEvent` carrying
`Credentials["accessToken"] = "super-secret-vendor-token"`, asserts the seed genuinely contains the
secret before the stamp (so the test cannot pass vacuously), then after the stamp asserts:

- the secret string appears nowhere in the stored JSON — catches the key being moved rather than removed;
- the credentials property is **absent**, not emptied — checked via `TryGetProperty`;
- the event round-trips: correlation id, product type, client id, metadata and the `Payload` body all
  survive, because a retry needs them.

The key name is taken from `JsonPropertyNameAttribute` on `PlatformEvent.Credentials` by reflection
(`CredentialsJsonKey()`), so no casing is hardcoded. `SeedCheckpointAsync` gained a
`string platformEventJson = "{}"` parameter to carry the payload.

### Results — round 4

| project | result |
| --- | --- |
| `UnitTests/…Infrastructure.Core.UnitTests` | **Passed — 418/418** (16 in the retention class) |
| `UnitTests/…Application.UnitTests` | **Passed — 93/93** |
| `Tests/…API.UnitTests` — Docker-free checkpoint classes | **Passed — 25/25** |
| `Tests/…API.UnitTests` — `CheckpointRepositoryTests` | **NOT RUN — 38/38 `DockerUnavailableException`** |
| solution build | **Build succeeded** |

No spec-vs-implementation mismatch: both reversals and the credential strip pass against W1's code
in-memory. The Postgres side of all of it — including the credential strip, which is the security fix —
is still unverified here; that suite has never run on this machine.

## W3 — tests, round 5 (bind the handler's InstanceId to the owner guard)

`VerifySetRetention` matched the instance id with `It.IsAny<string>()`, so nothing runnable connected
the handler's `InstanceId` to the store's owner guard. Tightened to `Environment.MachineName`, which is
literally what `ProcessEventCommandHandler.cs:42` assigns (`private static readonly string InstanceId =
Environment.MachineName;`) and what seven existing assertions in the same file already compare against.

**Verified the matcher is load-bearing rather than incidentally green.** Mutated the expected value to
`"a-different-pod"` and re-ran `R2_OnlyFailureStatusesStampRetention`: **3 failed, 4 passed** — exactly
the three stamping rows (Failure, TransientFailure, ValidationFailure) broke, and the four no-stamp rows
were unaffected, which is the correct signature. Mutation reverted; the file is back to
`Environment.MachineName`.

The three `Times.Never` verifications (R3, adapter-not-found) were deliberately **left loose**. "No call
with any instance id" is a strictly stronger claim than "no call with this instance id", so narrowing
those matchers would weaken them.

### Results — round 5

| project | result |
| --- | --- |
| `UnitTests/…Infrastructure.Core.UnitTests` | **Passed — 418/418** |
| `UnitTests/…Application.UnitTests` | **Passed — 93/93** |
| `Tests/…API.UnitTests` — Docker-free checkpoint classes | **Passed — 25/25** |
| `Tests/…API.UnitTests` — `CheckpointRepositoryTests` | **NOT RUN — 38/38 `DockerUnavailableException`** |
| solution build | **Build succeeded** |

Recorded for the plan owner, no action taken (both noted as documented-not-changed):
`TryClaimAsync` is a fourth write path outside the preserve/clear rule, and in-memory `UpsertAsync` can
set a hold Postgres cannot.

## W3 — tests, round 6 (serializer-key guard)

### The test

`The_serialized_credentials_key_is_the_one_the_strip_removes`, in
`InMemoryCheckpointRetentionTests.cs` — Docker-free, so it runs on every machine.

It serialises a real `PlatformEvent` carrying credentials exactly as
`ProcessEventCommandHandler.cs:139` writes `PlatformEventJson` — a bare
`JsonSerializer.Serialize(platformEvent)`, no options object — then finds the top-level property that
actually carries the secret and asserts its name equals **`CheckpointEntry.CredentialsPropertyName`**.

Asserting on *the key that carries the secret* rather than on "some key exists" is what makes it a
security test: if a naming policy or attribute renames the emitted key, the carrier's name moves and the
constant does not, so the two stop matching.

### The constant is referenced, not copied

`CheckpointEntry.CredentialsPropertyName` (`Domain/Models/CheckpointEntry.cs:81`, `public const`,
`= nameof(PlatformEvent.Credentials)`) is in `Domain`, which `Infrastructure.Core.UnitTests` already
references. **Reachable — nothing needed exposing, and the literal `"Credentials"` appears nowhere in
the test.**

### Verified the guard actually fires

A test that cannot fail is worth nothing on a control that fails open, so I simulated the exact failure
mode it exists for: re-serialised with `PropertyNamingPolicy = JsonNamingPolicy.CamelCase`, which is a
naming policy changing the emitted key without touching the property name. **The test failed**, as it
must. Mutation reverted; the file is back to the bare `Serialize` call.

Together with the strip test and the `nameof`-derived constant, the three cover: a property rename
breaks the build, an attribute or policy change breaks this test, and the in-memory strip test proves
the credentials actually leave while the rest of the payload survives.

### Results — round 6 (final)

| project | result |
| --- | --- |
| `UnitTests/…Infrastructure.Core.UnitTests` | **Passed — 419/419** (17 in the retention class) |
| `UnitTests/…Application.UnitTests` | **Passed — 93/93** |
| `Tests/…API.UnitTests` — Docker-free checkpoint classes | **Passed — 25/25** |
| `Tests/…API.UnitTests` — `CheckpointRepositoryTests` | **NOT RUN — 38/38 `DockerUnavailableException`** |
| solution build | **Build succeeded** |
