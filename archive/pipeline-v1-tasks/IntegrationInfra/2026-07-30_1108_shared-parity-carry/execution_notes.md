# Execution Notes

## S1 — Branch (complete)
Branched `carry/shared-parity-instancebatchid` off `origin/dev` (`ad2699a`). Local `dev` was 2 behind;
`absorb/sdk-source` was 0 ahead of `origin/dev`, so the remote ref was the correct base.

## S2 — Seam (complete, main agent)

`Envelopes/Common/BatchScopedStorage.cs`
- Added `System.Security.Cryptography` + `System.Text` usings.
- Restored `BatchIdNamespace` = `b584b489-7c3d-4caf-97eb-49d7ff6d78fb` (private static readonly, frozen).
- Restored `BuildBatchInstanceId(string baseUrl, int pageNumber)` verbatim from Shared.
- `BeginPage` now writes `BuildBatchInstanceId(baseUrl, pageNumber)` instead of the raw `batchSegment`.
- Expanded the `InstanceBatchIdMetadataKey` doc to Shared's wording (unique index + replay stability).
- Rewrote the class-level usage contract: the emitter now owns the lifecycle, the old example referenced
  `CollectorNdjsonPublisher`, a Shared type that does not exist here. Recorded that step 3
  (`AdvancePage` sequencing) stays consumer-side.

`Emission/Ndjson/NdjsonContentHasher.cs` (new)
- Verbatim carry. Added a remark stating why the digest is load-bearing (stuck-collector detection).

`Emission/Ndjson/NdjsonBatchSession.cs` + `NdjsonUtf8BatchSession.cs` (identical edits)
- `_contentHasher` field; `ContentHashHex` property; `AppendFlushedPayload(stream)` placed after
  `flushBytes` and before `GcMemorySnapshot.Capture()` (Shared's exact position); `Dispose()` at the end
  of `DisposeAsync` after the stream is released. Verified single occurrence of each anchor in both files.

`Emission/NdjsonBatchEmitter.cs`
- `bool batchScopedStorage = false` on the ctor and on `Create`; `_batchScopedStorage` field.
- Private `BeginBatchScope` / `EndBatchScope` helpers; called from `PublishUtf8PageAsync` and
  `PublishPageAsync` only. Batch-level `PublishAsync`/`PublishUtf8Async` left unscoped.
- `EndBatchScope` is deliberately NOT a `finally` — matches the hand-rolled consumer pattern; the shared
  terminal publication paths already call `RestoreBase` before publishing. Documented in the helper.
- `Hash={Hash}` + `session.ContentHashHex` restored on both publish-completed log lines.
- Reconciled the concurrency claim: "safe for concurrent publishes" was unqualified and becomes untrue
  for the scoped path. Now states publishes against DISTINCT progress contexts are safe, and scoped page
  publishes sharing one progress context must be sequential.
- Fixed `<see cref="Create(IServiceProvider)"/>` → `Create(IServiceProvider, bool)` (stale after the
  overload change; would have emitted CS1574).
- Added `<param>` tags for all five ctor params and both `Create` params. A lone `<param>` tag had
  introduced 5 new CS1573 warnings; these are now gone.

### S3 — Seam gate (passed)
- `dotnet build IntegrationInfra.slnx --no-incremental`: **0 errors**. Warning inventory back to
  baseline — the only `CS` warnings are 3 pre-existing CS1574s in files this task never touched
  (`ICollectorBusEntrypointSource.cs`, `SessionSpec.cs`, `RecoveryBudgetEvaluator.cs`); the remaining 20
  are NU1507 source-mapping noise.
- Full suite: **245 passed, 2 failed** — exactly the tolerated set. Both failures are the pinned
  assertions at `BatchScopedStorageTests.cs:49` and `:145`, failing with
  `Expected "batch_000001" / Actual "d02b434b-b891-5524-a2eb-343b3f8d140d"` and
  `Expected "batch_000002" / Actual "257a4213-20df-5489-84ee-872cbf5330b8"`. Handed to W1.
- **Upstream contract verified independently, by value not inspection.** `BuildBatchInstanceId(BaseUrl, 3)`
  → `a9a3292a-43fe-56d8-8626-fcbd67059ccd`, identical to Shared's golden vector AND to Python's
  `uuid.uuid5(b584b489-…, "s3://cybi-data/stg/raw-data/tenant/setting/run/batch_000003")`. Pages 1 and 2
  also match Python. Implementation is canonical RFC 4122 v5.

## Post-fan-out seam amendment (main agent, operator-directed)

Operator ruled that scope release belongs in a `finally`, and that it must be the final step of a method
called only when needed rather than pushed into anything central. Applied to both generic page methods:

- `BuildMandatoryTargetPath` hoisted above `BeginBatchScope` — validation before mutation.
- `try { publish; pageEarnedItsScope = result.RecordCount > 0; return ...; } finally { if (!pageEarnedItsScope) ReleaseBatchScope(...); }`
- `EndBatchScope(ctx, result)` → `ReleaseBatchScope(ctx)`; the result inspection collapsed into one flag.
- Placement confirmed against the operator's constraint: the logic remains in `PublishUtf8PageAsync` /
  `PublishPageAsync` only, never in the central `PublishAsync`/`PublishUtf8Async` that all publishes funnel
  through, so unscoped batch publishes are untouched.

This is a recorded BEHAVIOR DEVIATION from Shared (see decisions.md). I shipped it initially with no test —
my defect, since the change created new behavior. Closed with two tests in `BatchScopedStorageTests.cs`:
`..._WhenPublishThrows_ReleasesTheScope` and `..._AfterAFailedPage_RetryReusesTheSameFolderAndId`. The
second is the load-bearing one: it proves releasing on failure does not impede resumption.

Process risk taken knowingly: this seam edit happened while W1/W2 were still live. It did not bite (final
suite green) but it was not safe by design. Future runs of this shape should freeze seam files after
fan-out or force a worker re-run.

## Worker outcomes (verified against disk, not accepted on report)

- **W1** — 2 deleted lines in the entire diff, both the sanctioned assertions; nothing weakened or skipped.
  Line 139's key-presence check preserved. Added an unasked-for resume-stability test. Emission READMEs
  updated (+32 / +48 lines).
- **W2** — shipped a `StubSession` missing 12 `IHttpSession` members + `IAsyncDisposable.DisposeAsync`
  (13 × CS0535), which broke the whole solution build. W3 caught it; W2 self-repaired before I verified.
  Conversation README +17 lines. Conversation.Tests 11 → 20 tests.
- **W3** — resolved A9: `ContentHashHex` is reachable, and it chose the capturing-`ILogger` route over
  direct session construction on the grounds that the log line is the only operator-visible surface, so a
  session-level test would still pass if the emitter stopped logging the digest. Correctly labelled its
  "direct construction would compile" finding as declaration-level, not executed. Confirmed the SHA-256
  expectation is computed from the test's own inputs, independent of the implementation. 6 tests, new file
  `NdjsonContentDigestTests.cs`, no production change.

## Final state
- `dotnet build IntegrationInfra.slnx`: 0 errors. Warnings at baseline (20 NU1507 + 3 pre-existing CS1574
  in untouched files).
- `dotnet test IntegrationInfra.slnx`: **271 passed, 0 failed** (247 baseline + 24 new).
- Nothing committed, nothing pushed.

## Verifier-1 repairs (main agent)

Verdict was PASS WITH GAPS. Repairs applied:

- **MEDIUM — UTF-8 page path had zero batch-scoping coverage.** Correct and important: the two generic page
  methods each carry their OWN copy of the scope/release block, so string-path coverage does not transfer,
  and the real consumers (`FalconFindingsFlow.cs:219`, `QualysFindingsBatchPublisher.cs:41`) use the UTF-8
  flavor. All five scoping tests converted to `[Theory]` over `AtomicStreamedObjectsTests.PublishMode`, via
  a `PublishFindingsPageAsync` dispatch helper. Scoping coverage 5 → 10 cases; a copy-paste divergence
  between the twins now fails instead of shipping green.
- **MEDIUM — my "warnings at baseline" claim was wrong.** Actual was 24, including a NEW `CS1734` at
  `Conversation/SessionAuthRetry.cs:16`: the verbatim carry put `<paramref name="requestFactory"/>` in a
  CLASS-level `<remarks>` where no such parameter is in scope. Shared never emitted it because its csproj
  does not surface doc warnings. Fixed to `<c>requestFactory</c>`. Inventory now genuinely at baseline —
  only the 3 pre-existing CS1574s in untouched files. My error was having the evidence (a 20 → 24 warning
  delta after the seam amendment) and not chasing it.
- **LOW — docs described release as dud-page-only.** `Emission/README.md`, `README.Publishing.md` and
  `BatchScopedStorage`'s class doc now state that a FAILED page releases too, name it as a deliberate
  divergence from `Shared`, and record that release is idempotent so a retry re-derives the same folder
  and id.
- **LOW — A9 marked VALIDATED** in `assumptions.md` with W3's resolution and its reasoning.

Consciously DECLINED, with reasons:
- *No test that a 0-record publish logs no `Hash` field.* This asserts the absence of a field on a
  debug-level line, not behavior; the 0-record path is already pinned by the dud-page theories. Adding it
  would pad coverage without guarding anything.
- *No test for a second 403 (only 401→401).* There is no code branch that distinguishes them —
  `IsAuthFailure` treats both identically and the first-attempt 403 is covered. A test here would assert
  the compiler.

## Final state after repairs
- `dotnet build IntegrationInfra.slnx --no-incremental`: **0 errors**, 20 NU1507 + 3 pre-existing CS1574. No
  warning introduced by this task.
- `dotnet test IntegrationInfra.slnx`: **276 passed, 0 failed, 0 skipped** (247 baseline + 29 new).
- Nothing committed, nothing pushed.

## Code-reviewer-1 repairs (main agent)

No Blockers. 3 Major, 4 Minor, 9 Observations. Reviewer independently re-derived the v5 UUID and confirmed
RFC 4122 §4.3 conformance, and specifically noted the naive `new Guid(bytes)` little-endian mistake was
avoided.

**Fixed:**
- **M2 — `RestoreBase` released the batch id only when `baseStorageUrl` happened to be present.** Real
  asymmetry: the `Remove` sat inside a different key's guard, and the type's own doc says a resume can
  round-trip `storageUrl` WITHOUT `baseStorageUrl`. If the host ever echoes `instanceBatchId` back, all 7
  terminal release paths became no-ops for the id and a run-level DONE/failed/partial event would carry a
  stale batch identity into a store with a unique index on it. Moved the `Remove` out of the guard
  (`Remove` on an absent key is a no-op, so the non-opted-in case cannot regress) + test
  `RestoreBase_ReleasesTheBatchId_EvenWhenNoPreservedBaseIsPresent`.
- **M3 — concurrency doc understated the failure mode.** Took the documentation option over enforcement
  (a `ConditionalWeakTable` sentinel is real complexity for a hazard no current consumer exercises), but the
  reviewer was right that the wording was too soft. Now names unsynchronized `Dictionary` writes corrupting
  internal structure — surfacing as a lost entry or a hang in an unrelated lookup, far from the publish site
  — and the retained-reference hazard: the live dictionary is handed to the publisher as request metadata, so
  a publisher that enumerates it after returning can see the `finally`-time mutation as "collection was
  modified".
- **m4 — hasher allocated in a field initializer, ahead of ctor validation.** Field initializers run before
  the ctor body, which can still throw, leaving the `IncrementalHash`'s unmanaged handle to finalization on
  an object `DisposeAsync` never sees. Moved to the end of the ctor body in both sessions.
- **m5 — the auth tests could pass vacuously.** The strongest minor: nothing asserted the replay carries a
  REFRESHED credential. A caller capturing the header value before the call would replay the stale token and
  every test stayed green. Added `Replay_CarriesTheRefreshedCredential_NotTheStaleOne` and
  `WhenTheRefreshThrows_ItPropagates_AndTheFirstResponseWasAlreadyDisposed`.
- **m7 — `ContentHashHex` reads like a field but allocates and throws post-dispose.** Documented; also noted
  it does not reset the running hash. No code change: both call sites are once-per-publish inside the
  session scope. (`Convert.ToHexStringLower` is .NET 9; this targets net8.0.)
- Two doc slips of my own found while editing: a nested `<para>`, and the ctor text still saying release
  happens only on an empty page. Both corrected.

**Declined, with reasons:**
- **m6 — `SessionAuthRetry` treats 403 as refreshable.** Reviewer flagged it as not merge-blocking and noted
  some vendors do return 403 for stale credentials. Changing auth semantics inside a verbatim carry is
  exactly the wrong place; the suggested optional status-set parameter is a reasonable future change with a
  real consumer behind it.
- **O16 — `segment.Count` vs `(int)stream.Length`.** Equivalent for every `MemoryStream`, and the existing
  neighbor at `NdjsonBatchSession.cs:427` uses the same pairing. Consistency wins over churn in a verbatim
  carry.

**ESCALATED, not fixed — see the M1 entry in decisions.md.**

## Final state after both review passes
- `dotnet build IntegrationInfra.slnx --no-incremental`: **0 errors, 23 warnings** = 20 NU1507 + the 3
  pre-existing CS1574s in untouched files. Zero warnings introduced by this task.
- `dotnet test IntegrationInfra.slnx`: **279 passed, 0 failed, 0 skipped** (247 baseline + 32 new).
- Nothing committed, nothing pushed.

## M1 fixed (operator-directed), plus the two observations I had skipped

### M1 — retrograde-page guard in `BatchScopedStorage.BeginPage`

I had escalated this claiming the caller-page ↔ `CurrentPage` contract was unverifiable inside this task's
scope fence. That was wrong: the fence forbids *modifying* the adapters repo, not *reading* it. Reading it
settled the question in minutes.

**Evidence gathered:**
- `CurrentPage` is 1-based, defaults to 1 (`IAdapterExecutionContext.cs:119`), and is the page being worked
  on — `AdvancePage` increments it after a page completes ("Page {newPage - 1} completed").
- All three batch-scoped consumers seed their page counter FROM the resume state, so none restarts at 1:
  Falcon `outputPage = resumeState?.Page ?? 0` (`FalconFindingsFlow.cs:119`), Qualys
  `nextPageNumber = resumeState?.PublishedPageCount ?? 0` (`QualysFindingsFlow.cs:80`), InsightVmCloud
  `page = resumeState?.Page ?? 0` (`InsightVmCloudFindingsFlow.cs:64`). **M1 is therefore a latent hazard,
  not an active bug** — which is exactly why a guard is safe to add.
- Three distinct page/watermark relationships exist: Falcon calls `RestoreProgress(outputPage)` then
  increments, giving `page == CurrentPage + 1`; Qualys/IVMC advance after publishing, giving
  `page == CurrentPage`; Qualys/IVMC never call `RestoreProgress` at all, so `CurrentPage` sits at its
  default while their own counter resumes high. A strict `page == CurrentPage` check — the reviewer's first
  suggestion — would have broken Falcon.
- The false-positive path I was most worried about does not exist in Infra: the resilience executor
  deliberately does NOT call `AdvancePage(0,0)`, triggering `OnCheckpoint` directly precisely so
  `CurrentPage` is not inflated (`AdapterFailureDecisionExecutor.cs:333-339`). My worry came from CLAUDE.md
  describing the older Shared behavior, not from Infra's code.

**Fix:** `BeginPage` throws `InvalidOperationException` when `pageNumber < progressContext.CurrentPage`. A
page may equal or lead the watermark; it can never trail it. Placed in `BeginPage` rather than the emitter's
`BeginBatchScope` so the invariant travels with the identity derivation and also covers direct callers. The
message names the remedy (derive from the run's absolute counter, not a loop-local one).

**Tests:** `BeginPage_WhenPageTrailsTheRestoredWatermark_ThrowsInsteadOfCollidingTheBatchId` (asserts the
throw AND that the context is left untouched), plus
`BeginPage_AllowsEveryShippingConsumersPagePattern` — a `[Theory]` over all three real relationships
(40→41, 40→40, 1→40) that acts as a regression fence: any future tightening of the guard that would break a
shipping collector fails here instead of in production.

**Honest limitation, documented on the type:** the guard only bites for collectors that restore their page
counter into the progress context. Qualys and IVMC do not call `RestoreProgress`, so it is inert for them —
safe, but unprotected. Closing that needs a consumer-side change, which is genuinely outside this task.

### O9 — batch-id uniqueness depends on the base URL (was: noted, no action)
Recorded durably on `BatchScopedStorage`'s class doc. The id hashes `{baseUrl}/batch_NNNNNN`, so
cross-run distinctness is inherited entirely from `baseUrl` carrying a per-run segment (`instanceId` today).
Any future change that drops it, or reuses an `instanceId`, makes two runs' page N one document under the
unique index. No local check is possible; it is a property of how the host builds the URL.

### O12 — README overclaimed that a failed page uploads nothing (was: noted, no action)
`README.Publishing.md` said "the aborted multipart uploaded nothing", true for the abort path but not for the
narrower single-PUT-succeeded-then-threw window, which leaves a real object. Rewritten to describe both
windows and to state the actual outcome: an unannounced, deterministically-named stale folder that a retry
overwrites — never corrupt or double-counted data.

### Still consciously open, unchanged
m6 (403 treated as refreshable — auth semantics do not change inside a verbatim carry), O16
(`segment.Count` vs `stream.Length` — equivalent, matches neighbor), O13 (`baseStorageUrl` never removed —
one-way state, harmless), O15 (`SessionAuthRetry` has no live consumer here, so the stubs cannot prove a real
session returns rather than throws on 401/403 — needs the first real call site), and `ContentHashHex` on
`PublishResult` (SDK surface, owned by the absorption task).

## Final state
- `dotnet build IntegrationInfra.slnx --no-incremental`: **0 errors, 23 warnings** = 20 NU1507 + the 3
  pre-existing CS1574s. Zero introduced by this task.
- `dotnet test IntegrationInfra.slnx`: **283 passed, 0 failed, 0 skipped** (247 baseline + 36 new).
- Nothing committed, nothing pushed.
