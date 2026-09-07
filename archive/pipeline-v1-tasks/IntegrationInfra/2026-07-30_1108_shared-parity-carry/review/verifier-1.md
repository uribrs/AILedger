# Verifier 1 — Shared → Infra parity carry

**VERDICT: PASS WITH GAPS** — every formal Success Criterion is met and independently confirmed, but the
UTF-8 page publish path (the one real consumers actually use) has zero batch-scoping test coverage, one
new compiler warning was introduced, and `execution_notes.md`'s "warnings at baseline" claim is wrong.

Verified against the working tree at `/Users/user/Dev/IntegrationInfra`, branch
`carry/shared-parity-instancebatchid`, uncommitted. Nothing was fixed, committed, or pushed by this pass.

---

## 1. The load-bearing upstream contract — PASS, verified by value

Independent computation (not code inspection):

```
$ python3 -c "import uuid; ns=uuid.UUID('b584b489-7c3d-4caf-97eb-49d7ff6d78fb'); \
  base='s3://cybi-data/stg/raw-data/tenant/setting/run'; \
  [print(n, uuid.uuid5(ns, f'{base}/batch_{n:06d}')) for n in (1,2,3)]"
1 d02b434b-b891-5524-a2eb-343b3f8d140d
2 257a4213-20df-5489-84ee-872cbf5330b8
3 a9a3292a-43fe-56d8-8626-fcbd67059ccd
```

- `BatchScopedStorageTests.cs:18` defines `BaseUrl = "s3://cybi-data/stg/raw-data/tenant/setting/run"` —
  the same name Python hashed.
- `BatchScopedStorageTests.cs:176` asserts `BuildBatchInstanceId(BaseUrl, 3) == "a9a3292a-43fe-56d8-8626-fcbd67059ccd"`.
  That test passes in the suite run below, so the **implementation's output equals the independently
  computed RFC 4122 v5 UUID**. Byte-for-byte match confirmed for page 3; pages 1 and 2 additionally match
  the values the seam reported when the two pinned assertions failed.
- Version/variant nibbles are correct in the pinned literal (`...-5`6d8-`8`626...).
- Namespace: `BatchScopedStorage.cs:69` — `private static readonly Guid BatchIdNamespace = new("b584b489-7c3d-4caf-97eb-49d7ff6d78fb");`
  Correct value, `private`, `static readonly`, not parameterized anywhere (grep-confirmed: the only
  references are the doc comment and line 144).
- Method body is **identical to Shared's** — `diff` of the extracted `BuildBatchInstanceId` block between
  `Shared/DataPipeline/Egress/BatchScopedStorage.cs` and
  `src/IntegrationInfra/Envelopes/Common/BatchScopedStorage.cs` returned no differences.
- `BeginPage` writes the UUID, not the segment: `BatchScopedStorage.cs:100` —
  `progressContext.Metadata[InstanceBatchIdMetadataKey] = BuildBatchInstanceId(baseUrl, pageNumber);`
  The segment string is only used for the storage *path* at `:97-99`.

## 2. One-boolean opt-in, zero call sites — PASS

- `NdjsonBatchEmitter.cs:112` — `Create(IServiceProvider services, bool batchScopedStorage = false)`;
  ctor at `:57-62` with the same `= false` default. The default is what kept ~24 existing call sites
  compiling (build succeeds with zero test-file churn outside `BatchScopedStorageTests.cs`).
- Scoping is applied **only** in the two generic page methods: `PublishUtf8PageAsync` (`:181` `BeginBatchScope`,
  `:196` `ReleaseBatchScope`) and `PublishPageAsync` (`:212`, `:227`).
- The central methods are **clean**: `PublishAsync` (`:292-386`) and `PublishUtf8Async` (`:388-500`) contain
  no `BeginBatchScope`/`ReleaseBatchScope`/`BatchScopedStorage` reference. The operator's placement
  constraint (request #6) is honored.
- The four named page methods are pure expression-bodied delegations (`:126-168`) — no duplicated logic.
- End-to-end usability confirmed beyond the criterion: terminal events already call `RestoreBase` in Infra
  at 7 sites (`Conducting/AdapterBusEntrypointRunner.cs:129`,
  `Conducting/Bus/Logic/AdapterBusPartialSuccessPublisher.cs:36`,
  `Conducting/Collectors/Recovery/CollectorResumeStrategyExecutor.cs:42`,
  `CollectorResumeClassifiedExecutor.cs:46`, `CollectorResumePartialSuccessPublisher.cs:35`,
  `FaultGovernance/AdapterFlowFailureHandling.cs:61`,
  `FaultGovernance/Logic/AdapterFailureDecisionExecutor.cs:343`). A consumer flipping the boolean gets
  correct completion/failure/partial events with no further work. **The deliverable is immediately usable.**

## 3. The `finally` placement — PASS

- `NdjsonBatchEmitter.cs:192-198` and `:223-229`: `finally { if (!pageEarnedItsScope) ReleaseBatchScope(progressContext); }`
  — the release is the last statement of the method, in a `finally`, inside the page methods only.
- `BuildMandatoryTargetPath` is hoisted above `BeginBatchScope` (`:179`/`:181`, `:210`/`:212`), so an invalid
  output name never scopes-then-unscopes. Matches `decisions.md`.
- Structurally, `BatchScopedStorage.BeginPage` cannot throw after mutating `storageUrl` (both its argument
  guards run before any write), so `BeginBatchScope` sitting outside the `try` is safe.

## 4. The recorded deviation (release on FAILURE) — PASS; rationale holds

Tested, not just asserted in prose:
- `BatchScopedStorageTests.cs:360` `..._WhenPublishThrows_ReleasesTheScope` — uses a `ThrowingPublisher`
  (`:429`) whose `PublishStreamAsync` throws, asserts the exception propagates and that `storageUrl` is back
  to `BaseUrl` with `instanceBatchId` absent.
- `BatchScopedStorageTests.cs:386` `..._AfterAFailedPage_RetryReusesTheSameFolderAndId` — fails page 5, then
  retries page 5 on a fresh context and asserts the upload lands at
  `.../batch_000005/findings_000005.json` with `BuildBatchInstanceId(BaseUrl, 5)`.

**The stated safety rationale holds, and it is stronger than the notes claim.** I checked the two ways it
could fail:
1. *Does release impede resume?* No. `RestoreBase` (`BatchScopedStorage.cs:108-123`) rewrites `storageUrl`
   and removes `instanceBatchId` but **leaves `baseStorageUrl` in place**, and `ResolveBaseUrl` (`:157`)
   prefers the preserved base. So the retry re-derives an identical folder and id. Idempotent, as claimed.
2. *Could a released scope announce something upstream misreads?* No, and the reason is better than "the
   folder would be empty": release also removes `instanceBatchId`, so a failed page's event carries **no
   batch identity at all** — exactly the shape upstream already handles for a dud page. Upstream keys on the
   id, not the URL. The deviation extends existing dud-page semantics rather than inventing a new state.
3. *Could a page have committed an object before throwing?* No. Multi-flush publishes go through multipart
   (aborted on dispose), and the single-PUT path only runs at finalize. Nothing parseable is left behind.

Also verified: the `finally` fires on cancellation too (`OperationCanceledException` propagates past the
`catch` filter at `:381`/`:495`), which is the desired behavior for a PartialResult resume.

## 5. Content hash — PASS

- `ContentHashHex` on both sessions: `NdjsonBatchSession.cs:69-74`, `NdjsonUtf8BatchSession.cs:69-74`.
- `Hash={Hash}` on both publish-completed log lines: `NdjsonBatchEmitter.cs:371`/`:375` and `:485`/`:489`.
- `AppendFlushedPayload` placement is at Shared's exact position — after `flushBytes`, before
  `GcMemorySnapshot.Capture()` — confirmed by line-for-line comparison with Shared
  (`NdjsonBatchSession.cs:277/280/282` in Shared vs. the same relative order in Infra); hasher `Dispose()`
  at the end of `DisposeAsync` in both.
- `NdjsonContentHasher.cs` is a verbatim carry (only the namespace differs, plus an added `<remarks>`
  explaining why the digest is load-bearing).
- **The SHA-256 expectation is genuinely independent of the implementation.** Read
  `NdjsonContentDigestTests.cs:43-44`:
  ```csharp
  byte[] expected = input.SelectMany(r => r.ToArray().Append((byte)'\n')).ToArray();
  string expectedHash = Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant();
  ```
  It hashes the *test's own input records* with BCL `SHA256`, then compares against what the emitter logged.
  Not implementation-vs-itself. Both single-upload and multipart digests are compared to that same value
  (`:46-47`), and the test proves the two runs really took different paths (`:38-40`).
- Runs for **both** sessions via `[InlineData(PublishMode.String)]` / `[InlineData(PublishMode.Utf8)]`.

## 6. SessionAuthRetry — PASS

`src/IntegrationInfra/Conversation/SessionAuthRetry.cs` is a verbatim carry — `diff` against
`Shared/Session/SessionAuthRetry.cs` shows **one** differing line, the namespace.

| Requirement | Evidence |
| --- | --- |
| In `Conversation` | `Conversation/SessionAuthRetry.cs:4` `namespace Cymulate.IntegrationInfra.Conversation;` |
| Still a `static` | `:21` `public static class SessionAuthRetry` |
| Replays exactly once, never loops | Structurally guaranteed — no loop construct in `:43-56`; tested at `SessionAuthRetryTests.cs:43` (`SendCount == 2`, `refreshCount == 1`) |
| Second 401/403 returned unchanged | `SessionAuthRetryTests.cs:43-59` (401→401 returns 401) |
| Both attempt requests disposed, response not | `:43` and `:55` are `using var`; `:56` returns undisposed. Tested at `SessionAuthRetryTests.cs:120-143` via a `DisposeProbeContent` — asserts both request contents disposed, last response content **not** disposed, first (discarded) response content disposed |
| Null guards on all three | `:39-41`; tested at `SessionAuthRetryTests.cs:163-175` incl. `SendCount == 0` |
| `ct` into both sends and the refresh | `:44`, `:53`, `:56`; tested at `SessionAuthRetryTests.cs:146-160` asserting `SentTokens == {cts.Token, cts.Token}` and the captured refresh token |

9 test cases (contract asked for 4). `Conversation.Tests` 11 → 20, matching the notes.

The README's competitive claim is accurate — I checked
`src/Cymulate.Integration.Sdk/Resilience/AdapterResilienceExtensions.cs:14-38`: it is an `HttpClient`
extension with `maxRetries = 2`, a `Func<Task>` refresh with no token, and it never disposes the requests it
builds. "Not a substitute" is correct, not marketing. The "Defender calls it from 3 sites" claim also checks
out (`DefenderIOCFlow.cs:326`, `:805`, `:892`), as does "Zscaler and FortiGate are the same shape" (both
call `AuthSelection.None` in their configuration builders).

## 7. Scope fences — PASS

- `PublishResult` / SDK untouched: `git status --porcelain -- src/Cymulate.Integration.Sdk/` empty;
  `grep -rn ContentHashHex src/Cymulate.Integration.Sdk/` returns nothing.
- No csproj/props/slnx changes: `git diff --name-only origin/dev -- '*.csproj' '*.props' '*.slnx'` empty.
- No version bumps (follows from the above).
- Adapters repo untouched: `git status --porcelain` in the reference worktree is clean, and specifically
  `-- src/Cymulate.Integration.Adapters/Shared/` is clean.
- No identifier named "Legacy". The 6 `Legacy` hits in the repo are pre-existing comments/doc headings in
  files this task never touched (`Job/AdapterRunEnvelopeParser.cs`, `Conducting/AdapterPlatformEventFactory.cs`,
  `Conversation/AdapterHttpClient.cs`, two READMEs) — none is an identifier, none is in the diff.
- Nothing committed: `git log --oneline origin/dev..HEAD` empty. Nothing pushed:
  `git ls-remote --heads origin carry/shared-parity-instancebatchid` empty.

## 8. No test weakened — PASS

`git diff origin/dev -- tests/` removed **exactly two** lines in the entire test tree:

```
-        Assert.Equal("batch_000001", progress.Metadata[BatchScopedStorage.InstanceBatchIdMetadataKey]);
-        Assert.Equal("batch_000002", progress.Metadata[BatchScopedStorage.InstanceBatchIdMetadataKey]);
```

Both are the two sanctioned `instanceBatchId` assertions, and both were *replaced* (not dropped) with
`BuildBatchInstanceId(BaseUrl, N)` equivalents at `:49-51` and `:146-148`. No test deleted, no `Skip =`
anywhere in `tests/`, no assertion loosened, `:139`'s key-presence check preserved. `+219` lines in that
file are all new tests and the `ThrowingPublisher` helper.

## 9. Build and suite — PASS on errors/failures, **FAIL on the warning claim**

```
$ dotnet build IntegrationInfra.slnx --no-incremental
Build succeeded.
    24 Warning(s)
    0 Error(s)
```

```
$ dotnet test IntegrationInfra.slnx --no-build
Passed! - Failed: 0, Passed:   9 ... Cymulate.Integration.Sdk.UnitTests.dll
Passed! - Failed: 0, Passed:  69 ... IntegrationInfra.Kernel.Tests.dll
Passed! - Failed: 0, Passed:  27 ... IntegrationInfra.FaultGovernance.Tests.dll
Passed! - Failed: 0, Passed:  20 ... IntegrationInfra.Conversation.Tests.dll
Passed! - Failed: 0, Passed:  15 ... IntegrationInfra.Reporting.Tests.dll
Passed! - Failed: 0, Passed:  50 ... IntegrationInfra.Job.Tests.dll
Passed! - Failed: 0, Passed:  16 ... IntegrationInfra.Conducting.Tests.dll
Passed! - Failed: 0, Passed:  65 ... IntegrationInfra.Emission.Tests.dll
```

**271 passed, 0 failed, 0 skipped** across 8 projects. Arithmetic checks out: 247 baseline + 24 new
(BatchScopedStorageTests +9, NdjsonContentDigestTests 3 theories × 2 = 6, SessionAuthRetryTests 9 → 15 + 9).

**Warning inventory — 24, not the claimed 23.** 20 NU1507 restore-noise + 4 CS:

| Warning | File | Pre-existing? |
| --- | --- | --- |
| CS1574 | `Conducting/Collectors/ICollectorBusEntrypointSource.cs(11,84)` | yes |
| CS1574 | `Conversation/SessionSpec.cs(32,82)` | yes |
| CS1574 | `FaultGovernance/Logic/RecoveryBudgetEvaluator.cs(9,67)` | yes |
| **CS1734** | **`Conversation/SessionAuthRetry.cs(16,60)`** | **NO — introduced by this task** |

```
src/IntegrationInfra/Conversation/SessionAuthRetry.cs(16,60): warning CS1734: XML comment on
'SessionAuthRetry' has a paramref tag for 'requestFactory', but there is no parameter by that name
```

Cause: the verbatim carry put `<paramref name="requestFactory"/>` inside the **class-level** `<remarks>`
(`SessionAuthRetry.cs:16`), where no parameter of that name is in scope. Shared never emitted this because
its csproj does not surface doc-comment warnings. One-token fix (`<c>requestFactory</c>`), not made here.

---

## Unresolved / overstated, severity-ranked

### MEDIUM — the UTF-8 page path has zero batch-scoping coverage, and it is the path consumers use

All five `batchScopedStorage` tests go through `PublishFindingsPageAsync` — the **string** path
(`BatchScopedStorageTests.cs:292, 320, 371, 396, 409`; `grep -rn batchScopedStorage tests/` finds nothing
else). `PublishUtf8PageAsync`'s scope/release block (`NdjsonBatchEmitter.cs:181-198`) is a **separately
written copy** of `PublishPageAsync`'s (`:212-229`), so the coverage does not transfer — a copy-paste
divergence there would ship green.

This matters because the real Shared consumers use the UTF-8 flavor:
`FalconCollector/Flows/Findings/FalconFindingsFlow.cs:219` and
`QualysCollector/Flows/Findings/QualysFindingsBatchPublisher.cs:41` both call
`PublishFindingsUtf8PageAsync`. The formal criterion ("a test that publishes a page with the flag on and
asserts both the scoped upload path and the dud-page `RestoreBase`") *is* satisfied — but by the path that
nobody ships. The cheapest close is to convert the three core scoping tests to `[Theory]` over the same
`PublishMode` enum `NdjsonContentDigestTests` already uses.

### MEDIUM — `execution_notes.md` "Final state" overstates the warning result

> "Warnings at baseline (20 NU1507 + 3 pre-existing CS1574 in untouched files)."

Actual: 24 warnings including one **new** CS1734 in a file this task created. The same claim appears in the
S3 seam-gate section ("Warning inventory back to baseline"), where it was true — the regression arrived with
W2's carry, after the gate. `state.json`'s `verification.notes` records only the test counts and so does not
contradict the tree, but it also does not catch this.

### LOW — the failure-release deviation is documented on the ctor but not in either README

`NdjsonBatchEmitter.cs:270-272` states the invariant correctly ("A dud page and a failed page both release
it"). But `Emission/README.md:92` and `Emission/README.Publishing.md:89` both describe release as happening
only "when the page produced 0 records", and the class doc on `BatchScopedStorage.cs:33-34` still shows only
the dud-page branch. A consumer reading the docs would not know a failed page also releases — which is the
one behavior that differs from the in-production Shared implementation they may be migrating from.

### LOW — `assumptions.md` A9 still marked OPEN

`execution_notes.md` states W3 resolved A9 (`ContentHashHex` is reachable; W3 chose the capturing-`ILogger`
route instead). `assumptions.md` still carries A9 as `— OPEN` with "W3 to confirm and report". Bookkeeping
only; the code bears out the resolution. Every A1–A8 marked VALIDATED does hold in the tree — I spot-checked
A3 (all four named page methods are expression-bodied delegations, `NdjsonBatchEmitter.cs:126-168`), A4 (build
green with no non-`BatchScopedStorageTests` test churn), A6 (only new import is `IHttpSession`, no csproj
change), and A8 (`NdjsonBatchEmitterReuseTests.cs:56-57` does publish 8 pages concurrently, but with a
**distinct** progress context per page — consistent with the documented caveat, no contradiction).

### LOW — minor test-coverage edges

- No test that the second auth failure path works for a **403** specifically (only 401→401 is covered at
  `SessionAuthRetryTests.cs:45`). The first-attempt 403 is covered; there is no code branch that could differ.
- No test that a 0-record publish logs **no** `Hash` field (the 0-record branch at
  `NdjsonBatchEmitter.cs:365`/`:479` logs a debug line without it). `HashCapturingLogger.HashFieldCount`
  makes this a two-line addition.
- The golden-vector test pins a literal but does not separately assert the version/variant nibbles. The
  literal covers it; noting only for completeness.

### Not a gap — checked and clear

- `state.json` is internally consistent with the tree (`status: in_progress`, `currentPhase: review`, S7
  `in_progress`, `verifierRun: false`) — correct for a mid-review snapshot.
- Coverage beyond the formal criteria is real: the negative control
  (`..._WithoutBatchScopedStorage_UploadsAtTheRunRoot_AndAnnouncesNoBatchId`, `:335`), the resume-stability
  test (`BeginPage_SamePageAfterResume_AnnouncesTheSameBatchId`, `:200`), and the stuck-collector digest test
  (`RepeatedIdenticalPageUploadsShareOneDigest_WhileProgressChangesIt`, `NdjsonContentDigestTests.cs:55`)
  were none of them required. The last one directly answers the operator's stated reason for keeping the hash
  (request #2) rather than merely restoring the field.
- The reference worktree at
  `/Users/user/Dev/cymulate-integration-adapters/.claude/worktrees/dev-work` has since been switched to
  branch `dev-work` @ `c333dcd` by something outside this task. Its working tree is clean and all four
  reference-source diffs I ran against the *current* content still match, so no carry was made against stale
  input.
