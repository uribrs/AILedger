# Verifier 3 — failed-checkpoint terminal status (final cycle)

Diff base `d53e347d48b71d7befa9bce6b9086b909a547093`, branch `feat/failed-checkpoint-retention-ttl`.
13 modified files + 3 untracked test files. Zero files under `Migrations/`.

Everything below was re-derived against the current working tree. Prior review text was read for
context but not used as evidence.

## Environment as I measured it

| what | result | how |
|---|---|---|
| `dotnet build …sln` | **succeeded**, 0 warnings, 0 errors | run by me |
| `Application.UnitTests` | **93 passed / 0 failed** | run by me, `--no-build` |
| `Infrastructure.Core.UnitTests` | **435 passed / 0 failed** | run by me |
| `API.UnitTests` — checkpoint non-container suites | **26 passed / 0 failed** | run by me, filtered to `FailedRunLeavesATerminalRowTests`, `CheckpointRecoveryCompatibilityTests`, `StoppingIsNotEndingTests` |
| `API.UnitTests` — `CheckpointRepositoryTests` | **NOT RUN** | `docker info` fails; every case raises `DotNet.Testcontainers.Builders.DockerUnavailableException` at `unix:///var/run/docker.sock`. I confirmed the exception text directly rather than inferring it. |
| baseline count at `d53e347d` (83) | **not re-derived** | taken from the team lead; re-deriving it needs a dirty tree and I am read-only |

Docker being down is an environment fact, not a failure. It does mean the **entire Postgres half of
this change has never executed**: `MarkFailedAsync`'s three-arm `CASE` + owner guard, the
`status <> 'Failed'` claim guard, the `status = CASE …` revival in `TryAcquireExecutionLockAsync`, and
both delete predicates. 37 well-shaped tests exist for exactly this and cannot run.

---

## 1. Success Criteria coverage

| # | criterion | verdict | evidence |
|---|---|---|---|
| 1 | A collector-reported failed done marks `Failed`, releases the claim, strips credentials, does not delete | **MET** | `ProcessEventCommandHandler.cs:1259-1263` calls `TryMarkCheckpointFailedAsync` and returns before the delete. One write in each store: Postgres `CheckpointRepository.cs:456-478` (single `UPDATE` setting status, both claim columns, the stripped payload, `updated_at_utc`); in-memory `InMemoryCheckpointRepository.cs:162-181` (single `TryUpdate` CAS). `R1_SweepNeverOffersAFailedRow` in `FailedRunLeavesATerminalRowTests` drives the **real handler** over the **real in-memory store** and asserts all four facts — passed. |
| 2 | Every other outcome deletes exactly as before | **MET** | `IsReportedFailure` (`:1090-1093`) matches exactly `{Failure, TransientFailure, ValidationFailure}`. I reflected the pinned package (`Cymulate.Integration.Client 1.2.0-preview.0`): `AdapterResultStatus` has **exactly 7** members. The other four — `Success`, `Skipped`, `Cancelled` reach the delete; `PartialWaitRequired` returns at `:284` before `CompleteExecutionAsync`. `R2_OnlyReportedFailureMarksTerminal` — 6/6 passed. Adapter-not-found (`:193`) and unhandled-exception paths both still delete and never mark — 2 tests passed. |
| 3 | `GetRecoverableAsync` never returns a `Failed` row, both stores | **MET, and it was not sufficient on its own** | Postgres `:741`, in-memory `:396-397`. But see F1: the exclusion alone does not prevent re-dispatch; the `TryClaimAsync` guard added this cycle is what closes it. |
| 4 | `DeleteExpiredAsync` deletes a `Failed` row past the window, leaves everything else alone | **MET, by a different shape than the criterion words** | `DeleteExpiredAsync` was *not* repurposed — it keeps the TTL horizon and now excludes `Failed` (Postgres `:492-497`, in-memory `:245-250`); a second method `DeleteTerminalExpiredAsync` owns the retention horizon (Postgres `:501-509`, in-memory `:260-271`). This is the cycle-1 B1 reversal and it is the right call. 8 in-memory tests pin both predicates in both directions — passed. |
| 5 | Stop-request row deletion unchanged | **MET** | `CheckpointCleanupJob.cs:105` still passes the TTL `cutoff`, byte-identical to baseline. `Execute_still_deletes_stop_request_rows_on_the_ttl_not_the_retention_window` captures the actual argument and asserts it is the TTL, not the retention cutoff — passed. `DeleteStoppedCheckpointsAsync` still runs first and has no status predicate in either store (Postgres `:844-850`, in-memory `:470-483`), so an explicit stop outranks the retention hold. |
| 6 | Retention configurable, sane default, clamped against nonsense | **PARTIALLY MET** | `FailedRetentionDays` key, default 7d, floor 1d (`ConfigurationKeys.cs:348,366,372`), `int.TryParse`-not-`GetValue<int>` so an unparseable value falls back instead of throwing. `Execute_floors_a_nonsensical_retention_setting` covers `"0"`, `"-5"`, `"not-a-number"`, `null` — passed. **Floored, not clamped**: `Math.Max` with no ceiling. See F5. |
| 7 | Tests cover the status write + two side effects, each non-failure outcome deleting, sweep exclusion, cleanup predicate, stop still winning, stripped payload keeps everything else | **MET with one hole** | All six covered and runnable except "stop still winning", whose only test is Docker-gated (`DeleteStoppedCheckpointsAsync_still_deletes_a_terminal_row`) — the in-memory suite has no equivalent. Code-level both stores agree. **The hole is the payload strip's edge cases** — see F3. |
| 8 | Builds; affected projects pass; Docker-gated reported as not-run | **MET** | Table above. |
| 9 | No file outside the repo modified; no new migration | **MET** | `git status --porcelain` shows only in-repo paths; `git status --porcelain -- '*Migrations*'` returns 0. `status` is already `text NOT NULL DEFAULT 'Idle'` with **no CHECK constraint** (`20260526144503_AddCheckpointStatusAndScheduledResume.cs:20-25`). |

---

## 2. Assumption Disposition

Rebuilt fresh against the current code. NEVER-TESTED is the default; nothing is marked VALIDATED
without a citation I checked myself this cycle.

### Prior Art

| id | status | citation | actor |
|---|---|---|---|
| L-d13589c6 — retaining a failed checkpoint has no lifecycle consequences | VALIDATED (as refuted; the lesson holds and got *stronger*) | The delete was releasing the claim — replaced by an explicit release in the same write, `CheckpointRepository.cs:459-460`, `InMemoryCheckpointRepository.cs:173-174`. This cycle found a **third** consequence the first two missed: the released claim made the row claimable by the sweep, `CheckpointRepository.cs:565`. | verifier-3 |
| L-716f3c62 — a sweep predicate and a TTL predicate over the same column should agree | VALIDATED (as refuted) | They are now two methods on two horizons with disjoint predicates: `DeleteExpiredAsync` excludes `Failed`, `DeleteTerminalExpiredAsync` selects only `Failed`. `CheckpointCleanupJobTests` asserts the two cutoffs are not each other, in both directions — passed. | verifier-3 |
| L-44a4039e — extending retention has no security dimension | VALIDATED (as refuted) | Three secret carriers, not one: `CheckpointEntry.CredentialsJsonKey`, `HeadersJsonKey`, and the nested `Payload.credentials` blob added this cycle (`CheckpointEntry.cs:14-33`). The nested one was found only in cycle 2. | verifier-3 |
| L-5d94327f — the retention branch is reached by every collector-reported failure | VALIDATED (as refuted) | Four paths return before `CompleteExecutionAsync`: shutdown cancellation (`:302`), claim loss / supersession (`:238-275`), adapter-not-found (`:193`), unhandled exception. All still delete or leave the row for recovery. | verifier-3 |
| L-910c8f52 — the migration and raw SQL added for retention are correct; untested, Docker unavailable | NEVER-TESTED | Unchanged and now larger. Four raw statements, none executed. `docker info` fails; `CheckpointRepositoryTests` raises `DockerUnavailableException`, confirmed by me. | verifier-3 |

### Task assumptions

| id | status | citation | actor |
|---|---|---|---|
| A1 — the mark reaches every collector-reported failed done and no other outcome | **REJECTED** (second half VALIDATED, first half false by design) | "No other outcome" holds exactly: `IsReportedFailure` covers 3 of the enum's 7 members, verified by reflection against the pinned package; `R2` 6/6 passed. "Every" is false: adapter-not-found, unhandled exception, shutdown cancellation and claim loss never reach the branch. Deliberate per `constraints.md`, but the assumption as worded is wrong and should not be carried forward as true. | verifier-3 |
| A2 — nothing writes to a `Failed` row, so `UpdatedAtUtc` is a stable failure timestamp | **REJECTED** | `TryAcquireExecutionLockAsync` flips `Failed → Idle` and sets `updated_at_utc = EXCLUDED.updated_at_utc` on any redelivery (`CheckpointRepository.cs:679-684`, in-memory `:371-377`). The retention clock restarts. Already recorded in `decisions.md`. | verifier-3 |
| A3 — `Status != Failed` on `GetRecoverableAsync` fully prevents re-dispatch; no second dispatch path | **REJECTED** (its second half VALIDATED) | "Fully prevents" was false and is the exact defect cycle 2 fixed — select-then-claim TOCTOU, now closed by `status <> 'Failed'` on the claim itself (`CheckpointRepository.cs:565`, in-memory `:311`). "No second dispatch path" **is** true: the only other selector is `GetScheduledWaitsDueByAsync`, filtered to `Status == ScheduledWait` (`:757`); `ICheckpointRepository.GetAllAsync` has **zero production callers** (grepped all of `src/`). | verifier-3 |
| A4 — narrowing `DeleteExpiredAsync` to terminal rows leaks nothing | **REJECTED** | Cycle-1 B1. Moot now — the method was not narrowed. Both classes of immortal row (unusable `PlatformEventJson`, retired tenant partition) are reaped by the restored TTL. `DeleteExpiredAsync_still_reaps_a_non_terminal_row_past_the_ttl` over `Idle` and `InFlight` — passed. | verifier-3 |
| A5 — `Checkpoint:TtlHours` keeps deleting stop-request rows unchanged | **VALIDATED** | `CheckpointCleanupJob.cs:105` passes `cutoff`, not `failedCutoff`; the call is byte-identical to baseline. Test captures the actual argument — passed. | verifier-3 |
| A6 — `DeleteStoppedCheckpointsAsync` still deletes a terminal row | **VALIDATED** (by inspection; its only test is Docker-gated) | Postgres `:844-850` deletes by `correlation_id IN (SELECT … FROM adapter_stop_requests)` with no status predicate. In-memory `:470-483` filters on `CorrelationId` only. Both stores agree. The test exists at `CheckpointRepositoryTests.DeleteStoppedCheckpointsAsync_still_deletes_a_terminal_row` — **NOT RUN**, and there is no in-memory equivalent. | verifier-3 |
| A7 — releasing the claim in the same write prevents the redelivery dead-letter | **VALIDATED** | One statement in each store sets the claim columns null alongside the status. `TryAcquireExecutionLockAsync`'s `WHERE claimed_by_instance IS NULL OR …` then admits the redelivery, so it never reaches the `EXECUTION_LOCKED` return at `ProcessEventCommandHandler.cs:157`. `R4_MarkingTerminalReleasesTheClaim` asserts the released claim, that the *sweep* is still refused, and that the *redelivery lock* succeeds — passed. | verifier-3 |
| A8 — stripping credentials damages nothing else, and nothing reads them back | **VALIDATED**, with a stated caveat | Damage: `R5_StrippedPayloadKeepsEverythingExceptCredentials` asserts the fixture *carried* each key first, then walks every surviving top-level and `Payload` property with `JsonNode.DeepEquals` — passed. Readback: the only production deserializer of `PlatformEventJson` is `CheckpointRecoveryHandler.cs:249`, fed solely by `GetRecoverableAsync`, which never yields a `Failed` row. **Caveat, and it belongs in the PR body:** the guarantee is *"secret-free while the row stays `Failed`"* — a redelivery restores the full payload via `platform_event_json = EXCLUDED.platform_event_json` (`:681`). | verifier-3 |
| A9 — `Failed` is writable with no schema change | **VALIDATED** (schema half; the write itself is runtime-unverified) | `status text NOT NULL DEFAULT 'Idle'`, **no CHECK constraint** (migration `20260526144503:20-25`). `HasConversion<string>()` (`CheckpointDbContext.cs:87-91`), so EF LINQ `c.Status == CheckpointStatus.Failed` and the raw `status = 'Failed'` are the same predicate. Zero new migration files. No row has actually been written with `'Failed'` — Docker down. | verifier-3 |
| A10 — the early guard behaves correctly when a redelivery meets a `Failed` row | **VALIDATED** (behaviourally; the *semantics* remain the open question) | The guard at `:111` short-circuits on `ScheduledWait` only, so a `Failed` row falls through to the lock, which revives it. No dead-letter, no silent skip, no duplicate outbox row. Whether resuming is the *intended* retry semantics is the deliberately-unresolved item — see F6. | verifier-3 |
| A11 — both stores can express the new predicates identically | **VALIDATED for the predicates; NOT for the strip's edge cases** | Identical: sweep exclusion, both delete predicates, the claim's status guard, the `Failed → Idle` revival. The strip now *also* agrees on a non-object root, a non-object `Payload` and NULL — but by inspection only, with **no test on either side** (F3). Pre-existing and still divergent: in-memory `TryAcquireExecutionLockAsync` discards progress/cursor/adapter state where Postgres `DO UPDATE` preserves them. | verifier-3 |

---

## 3. Attention Item Disposition

I ran each named test myself, per project, by exact filter.

| id | final disposition | evidence |
|---|---|---|
| R1 — the sweep re-dispatches a terminal row | **CLOSED, and it took two guards, not one** | `R1_SweepNeverOffersAFailedRow`: `Infrastructure.Core.UnitTests` **1 passed**; `API.UnitTests` `FailedRunLeavesATerminalRowTests` **1 passed** (real handler → real store → real `CheckpointRecoveryHandler.RecoverAsync` returning 0); `API.UnitTests` `CheckpointRepositoryTests` **NOT RUN** (`DockerUnavailableException`). The exclusion alone was insufficient — `A_row_marked_terminal_after_the_sweep_selected_it_cannot_be_claimed` reproduces the interleaving and passes only because of `status <> 'Failed'` on `TryClaimAsync`. |
| R2 — retention applied to the wrong outcomes | **CLOSED** | `R2_OnlyReportedFailureMarksTerminal`: `Application.UnitTests` **6 passed / 0 failed**. Each row builds its result through the production factory and asserts mark-XOR-delete, never both and never neither. Predicate keys on `result.Status`, never `!result.Success` — constraint satisfied. Coverage is 6 of the enum's 7 members; `PartialWaitRequired` is excluded because it returns at `:284`, which is correct but is itself unpinned by a test. |
| R3 — a claim-lost execution marks another owner's row | **CLOSED** | `R3_RefusedWriteTakesPrecedence`: `Application.UnitTests` **1 passed**. The refused-write early return sits at `:1254-1256`, strictly above the new branch at `:1259`. The test asserts neither `MarkFailedAsync` nor `DeleteAsync` is called. Defence in depth: `MarkFailedAsync` is additionally owner-guarded on `instanceId` in both stores, pinned by `MarkFailedAsync_refuses_an_instance_that_does_not_own_the_row` — passed. |
| R4 — redelivery dead-letters | **CLOSED, and correctly re-aimed** | `R4_MarkingTerminalReleasesTheClaim`: `Infrastructure.Core.UnitTests` **1 passed**; `CheckpointRepositoryTests` **NOT RUN**. The test was inverted this cycle for the right reason — it used to assert the *sweep* could re-claim the released row, which is the R1 regression. It now asserts the sweep is refused and the redelivery lock succeeds and flips to `Idle`. R4's original point survives on the correct method. |
| R5 — the credential strip silently no-ops | **CLOSED for the happy path; the silent-failure mode is NOT fully closed** | `R5_StrippedPayloadKeepsEverythingExceptCredentials`: `Infrastructure.Core.UnitTests` **1 passed**; `CheckpointRepositoryTests` **NOT RUN**. Key derivation is honest: `CredentialsJsonKey`/`HeadersJsonKey`/`PayloadJsonKey` are `nameof`; `PayloadCredentialsJsonKey` is a documented literal because there is no property behind it — I confirmed both ends (writer `PlatformEventFactory.cs:226` `credentials = encryptedCredentials`, reader `ProcessEventCommandHandler.cs:475` `TryGetString(payload, "credentials")`, and `TryGetPropertyValue` is case-sensitive). Two tests assert the serializer actually emits those names. **But** the strip's own failure modes — non-object root, non-object `Payload`, malformed JSON — are untested in both stores (F3). |

---

## 4. Decision drift

`decisions.md` original entries:

1. *"Retention is expressed by status + `updated_at_utc`… a terminal row is not written to again."* — **stale**, and the file's own Corrections section says so. Accurate.
2. *"The recovery sweep is gated on status, not on a timestamp."* — **accurate, and now more so.** Gated in two places: `GetRecoverableAsync` and, since this cycle, `TryClaimAsync`.
3. *"The cleanup job's checkpoint predicate deletes only terminal rows."* — **stale**, superseded correctly in the Corrections section.
4. *"Proceeding on unverified: every collector-reported `failed` done reaches the branch."* — **accurate as a statement of risk**; A1 above confirms it is false as a statement of fact, which is what "unverified" was flagging.
5. *"Proceeding on unverified: nothing reads `Credentials` back off a stored checkpoint."* — **now verified true** (A8). The file can be upgraded from unverified to verified here.

Corrections section, checked line by line against the code:

- **"The cleanup job now runs two deletes… two questions, two horizons."** — accurate. `CheckpointCleanupJob.cs:79` and `:94`.
- **"`TryAcquireExecutionLockAsync` now flips `Failed → Idle`… a terminal row is revivable by design, and its retention clock restarts."** — accurate in both stores.
- **"The secret-free guarantee is conditional… a revived row carries its secrets again."** — accurate, and correctly identified as PR-body material.

**Two gaps in the Corrections section**, both because it predates cycle 2's work:

- It does not record the **`status <> 'Failed'` guard on `TryClaimAsync`**. That is the load-bearing consequence of the claim release and the single most important thing a future reader needs: *the mark's released claim is safe only because the claim path refuses terminal rows.* Nothing in the task directory says this except the code comment.
- The conditional-secret bullet names "credentials and headers". There are now **three** carriers — the nested `Payload.credentials` blob is missing from the list.

Neither is wrong; both are incomplete in the same direction as before — understating what the change does.

---

## 5. Findings

Weighted toward "is anything still wrong, and did cycle 2's fixes break something." I looked
specifically for the pattern of the last two rounds. **I could not find a defect introduced by cycle
2.** The three fixes are each narrower than they look, and I checked each blast radius directly.

### F1 — the `status <> 'Failed'` claim guard breaks no legitimate claim. Both call sites checked.

There are exactly **two** production callers of `TryClaimAsync`, both in `CheckpointRecoveryHandler`
(grepped all of `src/`, excluding tests). Both are fed from `GetRecoverableAsync`, which already
excludes `Failed`, so the guard can only ever fire on the TOCTOU window it exists for.

- **`:295`, the dispatch path.** Reached only from the `GetRecoverableAsync` loop at `:85`. The
  scheduled-wait branch short-circuits it entirely via `alreadyClaimed ||`, so a `ScheduledWait` row
  never reaches the guard.
- **`:828`, `EndUnservableRunAsync` — the unservable path, checked especially.** Same source. Walked
  every state a row can be in when it arrives:
  - Publish succeeds → `MarkFailedAsync` → row is `Failed` **and unclaimed**. The next sweep's
    `GetRecoverableAsync` never offers it, so `:828` is never re-entered. No loop, no double-publish.
  - Publish fails → early `return` at `:865` with the row **still claimed by this instance** and
    **not** `Failed`. The claim ages out on `StaleClaimThreshold` and the next sweep retries. The
    guard is not involved.
  - `MarkFailedAsync` returns `false` or throws → row stays claimed and non-terminal, logged at
    Warning, and the TTL reaps it at 24h. Not immortal.

  The one shape worth naming: **the terminal row `EndUnservableRunAsync` leaves behind is one this
  build could not read.** Its `platform_event_json` is intact (the mark strips only the three secret
  keys), so a redelivery can still revive it — and then the sweep declines it again and, past the
  escalation bound, publishes a *second* terminal done. That path existed before this change (the
  redelivery would have re-created the row), so it is not a regression, but it is now reachable from a
  retained row rather than only from a fresh dispatch. Worth a sentence in the PR body.

### F2 — the in-memory `TryClaimAsync` returning `false` for a missing key breaks nothing, and fixes a real lie.

The old unconditional `true` told a caller it owned a row that does not exist. Postgres has always
returned `false` there (`rowsAffected > 0`), so this **removes** a divergence rather than creating one.

Blast radius checked: both call sites treat `false` as "skip, someone else has it" and are only ever
handed rows that exist. The two hand-written test decorators
(`CheckpointRecoveryCompatibilityTests.ClaimlessStore`, `StoppingIsNotEndingTests`) forward to the
inner store, and one deliberately returns `false` already. All 26 non-container checkpoint tests pass,
and `TryClaimAsync_reports_false_for_a_row_that_does_not_exist` pins the new behaviour. **No caller
relied on the old `true`.**

One cosmetic consequence: `CheckpointRecoveryHandler:314` logs *"claimed by another instance"* at
Debug for what may now be a missing row or a terminal one. Debug-level, three distinct causes, one
message. Nit.

### F3 — the strip's edge cases now agree across the stores, but **nothing tests them**. This is the largest remaining coverage gap.

The code is right. I read the SQL against the actual column types (`platform_event_json` is `jsonb`,
`CheckpointDbContext.cs:73-74`, so `jsonb_typeof`, `#-` and `- text` are all valid; `#-` and `-` are
left-associative at equal precedence, so the chain evaluates in the intended order; a NULL column takes
the `ELSE` arm and `NULL - text` stays NULL). Both stores now behave identically on all four shapes:

| root shape | Postgres | in-memory | agree? |
|---|---|---|---|
| object, `Payload` an object | strip 3 keys | strip 3 keys | yes |
| object, `Payload` not an object | strip 2 top-level keys | strip 2 top-level keys | yes |
| non-object root (scalar / array) | unchanged (first `WHEN`) | unchanged (`is not JsonObject`) | yes |
| NULL / malformed | NULL unchanged / unreachable | returns unchanged, logs Warning, mark still succeeds | yes |

**But there is not one test for any row except the first.** I grepped both suites: no non-object test,
no array test, no scalar test, no malformed-JSON test, in either store. That matters more here than
usual for two reasons: R5's whole premise is that the strip's failure mode is *silence*, and the
Postgres arms cannot be exercised at all right now. The `CASE` written specifically to stop an array
payload having its string *elements* silently deleted is defended by nothing.

`execution_notes.md` claims `Infrastructure.Core.UnitTests` went "up one from earlier this round —
W2's F6 coverage". I enumerated all 21 test methods in
`InMemoryCheckpointRepositoryTerminalStatusTests.cs`; **none of them is F6 coverage.** The +1 is
`TryClaimAsync_reports_false_for_a_row_that_does_not_exist`. The note is wrong about what the test
buys.

Cost to close: four `[InlineData]` rows on a strip test in each store. Small.

### F4 — collapsing the copy paths dropped nothing. Verified mechanically, not by reading.

`CheckpointEntry` has exactly **23** public `{ get; set; }` properties (counted by grep). `Copy` has
exactly **23** `= source.` assignments, and I diffed the names against the property list — no
omission, no substitution. `TransitionFromScheduledWaitAsync` now calls `Copy` and overrides the same
five fields its inline initializer set (`Status`, `ScheduledResumeAtUtc`, `ClaimedByInstance`,
`ClaimedAtUtc`, `UpdatedAtUtc`), so it is behaviour-preserving; the only difference is that
`DateTime.UtcNow` is now read once instead of twice, which can only help.

`The_terminal_mark_carries_every_property_it_does_not_own` walks the entity by reflection with
type-derived distinct values and asserts ≥18 properties were actually compared, so a property added
later is covered the day it is added without editing the test. That is the right shape and it passed.

### F5 — `Math.Max` floors the retention setting but nothing ceilings it (carried from cycle 2, still open).

`CheckpointCleanupJob.cs:47`. `TimeSpan.FromDays(int.MaxValue)` overflows
(`TimeSpan.MaxValue.TotalDays` ≈ 1.07e7). The throw lands inside `Execute`'s `try`, is re-wrapped as
`JobExecutionException(refireImmediately: false)` at `:117`, and **the whole cleanup job stops on
every fire** — including the stop-request sweep and the non-terminal TTL, which have nothing to do
with the bad setting. That is the exact failure class the `int.TryParse` change closed, reached by a
different bad value. Requires an absurd setting, so low severity, but the fix is one token:
`Math.Clamp(days, Minimum, someCeiling)`.

### F6 — the deliberately-unresolved item, stated precisely.

Not treated as a defect, per instruction. The **de-facto behaviour as the code stands**:

> A broker redelivery of a run that was reported failed **resumes from that run's checkpoint**. No
> explicit trigger is required and nothing distinguishes the two.

The chain, each link verified:

1. The early guard at `ProcessEventCommandHandler.cs:111` short-circuits on `ScheduledWait` only, so a
   `Failed` row falls straight through.
2. `TryAcquireExecutionLockAsync` succeeds — the mark released the claim, so
   `claimed_by_instance IS NULL` matches — and flips `Failed → Idle` (`:679-684`).
3. The `ON CONFLICT DO UPDATE` deliberately does **not** reset `current_page`, `processed_items`,
   `cursor_token`, `last_processed_id` or `adapter_state_json` ("Progress fields are NOT reset on
   conflict", `:658`). Only the claim, the payload, the status and `updated_at_utc` move.
4. `platform_event_json = EXCLUDED.platform_event_json` puts **the full payload back**, credentials,
   headers and nested blob included.
5. `ExecuteWithResumeAsync` (`:1183-1199`) reads the row and resumes if `CanResumeFrom` says yes.
   `RetryCount` is **logged, not consulted** (`:1196-1202`) — there is no gate on how the run got here.

Consequences, in the order I would want them weighed:

- For `TransientFailure` this is almost certainly wanted, and `TransientFailure` is also the status that
  drives redelivery — so **mark-then-revive is the normal transient sequence, not an edge case.**
- For `Failure` and `ValidationFailure` the platform has already been told the run failed, and the
  retry silently skips every page the failed attempt collected.
- The retention clock restarts on step 2, and steps 2–4 undo the secret strip.
- **The in-memory store diverges here** (pre-existing, out of scope, flagged since round 2): it
  replaces the whole entry, so progress, cursor, adapter state and `ScheduledResumeAtUtc` are
  discarded. A redelivery restarts from scratch under the Console host and resumes under Postgres.
  Anyone reasoning about this behaviour from the in-memory tests will reach the wrong conclusion.

### F7 — `execution_notes.md` against the diff.

Mostly accurate and unusually well evidenced. Three things unsupported or stale:

1. **The SQL block at `execution_notes.md:587` no longer matches the code.** It shows the round-4
   `CASE` with only the nested-`Payload` guard. The shipped statement has a root guard as its first
   arm and duplicates the two `- text` deletes into both arms. The F6 section at `:733` supersedes it
   and says so, but the earlier block is what a reader hits first.
2. **"up one from earlier this round — W2's F6 coverage"** (`:784`) — not supported. There is no F6
   test. See F3.
3. **"434 passed"** at `:637` vs **"435 passed"** at `:711`. Both are round-4 claims about the same
   project. The final number is right; the earlier one is a stale snapshot.

Everything else I spot-checked held, including the four-`Headers`-writers correction, the
`PlatformEventFactory.cs:226` / `ProcessEventCommandHandler.cs:475` writer-reader pair, and the
23-property count.

### F8 — smaller items, none blocking, all still open from earlier cycles.

- **`Failed` rows now accumulate for 7 days, and no index supports any predicate that reads them.**
  The only index touching `status` is the partial `ix_checkpoints_scheduled_wait`. So
  `DeleteTerminalExpiredAsync` adds a second full scan every 30 minutes, and `GetRecoverableAsync` —
  which runs every **5** minutes — now has to skip up to a week of retained rows on every sweep. This
  is genuinely new: before this change those rows did not exist. Almost certainly fine at current
  table sizes; worth a sentence in the PR body so it is a decision and not a surprise.
- **No metric or count for `Failed` rows.** Retention is the only thing bounding the table and nothing
  tells an operator it is growing. Open since cycle 1.
- **N1 unaddressed.** Nine new unbraced `if`/`foreach` bodies in `InMemoryCheckpointRepository.cs`
  (`:165`, `:168`, `:178`, `:217`, `:233`, `:239`, `:267`, `:305`, `:311`).
  `docs/coding-standards.md:117` — "**Always brace**, even a one-line `if`" — applies to new code.
- **N2 unaddressed.** `CheckpointCleanupJob` now reads its two horizons from two mechanisms: `TtlHours`
  from the Quartz `JobDataMap`, `FailedRetentionDays` from `IConfiguration` at fire time. The new
  `IConfiguration` dependency exists only for that.
- **N4 unaddressed.** `CheckpointStatus.Failed`'s doc comment restates three behaviours owned by three
  other files. `CLAUDE.md` is explicit that this is the shape that rots.
- **`MarkFailedAsync` has no `status <> 'ScheduledWait'` guard** in either store. Unreachable today
  (the early guard and `HandlePartialWaitAsync` both return before completion) and both stores are
  consistently missing it, so it is a nit rather than a divergence.
- **In-memory CAS is weaker than it reads.** `MarkFailedAsync` guards with
  `TryUpdate(key, updated, existing)`, which is reference equality, while `TryClaimAsync`,
  `ReleaseClaimAsync` and `RenewClaimAsync` mutate the stored entry **in place**. A concurrent claim
  change is therefore invisible to the CAS. Test-double and Console-host only; the concurrency test
  races against `UpsertAsync`, which does replace, so the test is meaningful for the case it covers.
- **`AdapterStateJson` was never surveyed.** It is the one field on a retained terminal row nobody has
  checked for secrets, and it now lives seven days. Cycle-1 open question, still open.

---

## 6. Verdict

**Pass with gaps.**

The change does what the contract asks, and cycle 2's three fixes are correct and, unusually for this
task, introduced nothing. I specifically hunted the fix-introduces-defect pattern that burned the last
two rounds and came back empty: the claim guard's only two call sites are both fed from a query that
already excludes `Failed`; the in-memory `false`-for-missing-key removes a divergence rather than
creating one and no caller depended on the old value; the collapsed copy path is 23-for-23 with a
reflection test that will catch the 24th; and the two stores now agree on every payload shape I could
construct. All nine success criteria are met, two of them by a better shape than the criterion words.

The gaps, in the order I would address them:

1. **The Postgres half has never executed.** Four raw statements, 37 tests written for them, zero runs.
   One `docker start` and one `--filter "FullyQualifiedName~CheckpointRepositoryTests"` closes it, and
   the result belongs in the PR body. This is the single largest hole and it is one command wide.
2. **The strip's edge cases are untested in both stores** (F3). The `CASE` arm written to stop an array
   payload having its elements silently deleted is defended by nothing, and `execution_notes.md`
   claims coverage that does not exist. Four `[InlineData]` rows per store.
3. **`decisions.md` Corrections is accurate but incomplete** — it does not record the `TryClaimAsync`
   status guard, which is the fact that makes the released claim safe, and its secret list is missing
   the nested blob.
4. **`execution_notes.md` has one stale SQL block, one unsupported test-count claim, and one stale
   number** (F7).

Open for the operator, decisions rather than defects:

- **Should a broker redelivery of a failed run resume from its checkpoint, or should only an explicit
  trigger?** Today it resumes, unconditionally and without distinguishing `TransientFailure` from
  `Failure`, and the in-memory store disagrees with Postgres about it (F6). Whatever the answer, it
  should be stated in the PR body — it is the one behaviour a reader cannot infer from the diff.
- **The secret-free guarantee is conditional** — true while the row stays `Failed`, undone by the next
  redelivery. PR body.
- **No upper clamp on `Checkpoint:FailedRetentionDays`** (F5); an absurd value stops the entire
  cleanup job on every fire.
- **`Failed` rows now accumulate for a week against an unindexed `status`**, read by a 5-minute sweep
  (F8).
- **Nothing counts or alarms on retained `Failed` rows.**
- **`AdapterStateJson` has never been surveyed for secrets**, and it now outlives its run by the
  retention window.
- **N1/N2/N4 style items** — braces on new code, the two-mechanism config read, the over-long enum
  doc — are cheap and still open.
