# Code Review 1 — batch-scoped storage / instanceBatchId, NDJSON content digest, SessionAuthRetry

**Reviewed:** uncommitted working tree, `carry/shared-parity-instancebatchid` (vs `origin/dev`).
**Stack:** C# / .NET 8, shared library code consumed by collectors and indicator adapters.
**Classification:** shared library + data-plane egress + identifier generation feeding a store with a
unique index → **High risk**. Reviewed at high depth: failure semantics, resume/idempotency,
concurrency, resource lifetime, identifier correctness.

**Build evidence:** `src/IntegrationInfra` and both touched test projects compile clean
(0 errors; only pre-existing NU1507 / CS1574 warnings). Tests were not executed.

**Verdict:** no Blockers. Three Major findings, all local patches — none needs a refactor.
The v5 UUID derivation is correct (independently verified), and the digest tests are unusually
strong. The Majors are about the *identifier contract's* dependence on caller discipline and on
host behavior, not about the crypto or the streaming.

---

## Major

### M1. `pageNumber` is trusted from the caller and never reconciled with `progressContext.CurrentPage`

`src/IntegrationInfra/Emission/NdjsonBatchEmitter.cs:181` and `:212` (and
`src/IntegrationInfra/Envelopes/Common/BatchScopedStorage.cs:79`)

**Problem.** Both the batch folder and the `instanceBatchId` are a pure function of
`(baseUrl, pageNumber)`. `pageNumber` arrives as an independent method argument. The emitter holds
`progressContext` — which owns the authoritative, checkpoint-restored page counter
(`AdapterProgressContext.CurrentPage`, `RestoreProgress(currentPage, …)`,
`src/Cymulate.Integration.Sdk/Contracts/IAdapterExecutionContext.cs:200,254,309`) — and never
compares the two. The only validation is `pageNumber >= 1`.

**Impact.** The design's replay-stability guarantee ("same run + same page ⇒ same id") silently
inverts into a collision when the caller's page number is not the run's absolute page number.
Concrete scenario: a collector resumes at page 40 (`RestoreProgress(40, …)` restores the watermark
correctly) but its flow loop derives the publish page from a fresh loop-local counter starting at 1.
The resumed run then writes `…/batch_000001/findings_000001.json`, overwriting the object the *first*
attempt published there, and announces `BuildBatchInstanceId(base, 1)` — the id already used for the
first attempt's page 1. Downstream's unique index turns that into an upsert: two batches with
different record sets converge onto one document, and the first attempt's page-1 records are
overwritten in S3 after upstream already parsed them. No log line, no exception, no failed publish.
Nothing in the current test suite would catch it, because no test drives page numbers that disagree
with `CurrentPage`.

This is the failure the deterministic-naming design is *most* exposed to, and it is the one thing the
emitter is positioned to detect for free.

**Recommended fix (local patch).** In `BeginBatchScope`, reject a page number that cannot be the
next page of this context — the cheapest useful form:

```csharp
if (pageNumber <= progressContext.CurrentPage - 1)   // or: != CurrentPage, if the contract is exact
    throw new InvalidOperationException(
        $"Batch-scoped page {pageNumber} would re-use an already-advanced page " +
        $"(CurrentPage={progressContext.CurrentPage}); the folder and instanceBatchId would collide.");
```

If the exact relationship between the caller's page argument and `CurrentPage` is not uniform across
collectors, a `LogWarning` on a *decreasing* page number within one context is the minimum acceptable
substitute — silence is not.

Not a refactor: ~4 lines in a method that already exists (`NdjsonBatchEmitter.cs:254`), plus one test.

---

### M2. `RestoreBase` removes `instanceBatchId` only when `baseStorageUrl` happens to be present

`src/IntegrationInfra/Envelopes/Common/BatchScopedStorage.cs:120-125`

**Problem.** The `instanceBatchId` removal sits *inside* the `baseStorageUrl` lookup guard:

```csharp
if (progressContext.Metadata.TryGetValue(BaseStorageUrlMetadataKey, out string? baseUrl) &&
    !string.IsNullOrWhiteSpace(baseUrl))
{
    progressContext.Metadata[StorageUrlMetadataKey] = baseUrl;
    progressContext.Metadata.Remove(InstanceBatchIdMetadataKey);
}
```

So "release the batch identity" is conditional on a *different* key existing. The type's own doc
(`:175-179`) states that a resume message can round-trip `storageUrl` while **not** carrying
`baseStorageUrl` — meaning the guard is known to be false on exactly the path where a stale value is
most likely to be sitting in the envelope.

**Impact.** `instanceBatchId` was added specifically so the backend reads it directly rather than
parsing the path, i.e. it is a value the platform consumes and may echo. If the host ever puts
`instanceBatchId` into `PlatformEvent.Metadata` on a resume (`AdapterPlatformEventFactory` promotes a
fixed key list and does not strip anything the host already set —
`src/IntegrationInfra/Conducting/AdapterPlatformEventFactory.cs:35-95`), then every release path
becomes a no-op for the id: `CollectorResumeStrategyExecutor.cs:42`,
`CollectorResumeClassifiedExecutor.cs:46`, `CollectorResumePartialSuccessPublisher.cs:35`,
`AdapterBusPartialSuccessPublisher.cs:36`, `AdapterFailureDecisionExecutor.cs:343`,
`AdapterFlowFailureHandling.cs:61`, `AdapterBusEntrypointRunner.cs:129`. The run-level DONE / failed /
partial event then carries a batch identity from a previous attempt, attributing a whole-run outcome
to one specific batch document under the unique index.

**Evidence tier:** the code asymmetry is confirmed; whether the host echoes the key is *unverified*
(likely risk, not confirmed).

**Recommended fix (local patch).** Move the removal out of the guard — releasing an identity should
not depend on another key's presence, and `Remove` on an absent key is already a no-op, so this
cannot regress the non-opted-in case:

```csharp
progressContext.Metadata.Remove(InstanceBatchIdMetadataKey);

if (progressContext.Metadata.TryGetValue(BaseStorageUrlMetadataKey, out string? baseUrl) && …)
{
    progressContext.Metadata[StorageUrlMetadataKey] = baseUrl;
}
```

Add the matching test: metadata pre-seeded with `instanceBatchId` and **no** `baseStorageUrl`,
`RestoreBase` ⇒ id gone. Existing `RestoreBase_WhenNeverScoped_LeavesMetadataUntouched`
(`BatchScopedStorageTests.cs:153`) does not cover it — it seeds neither key.

---

### M3. Scoping introduces cross-thread mutation of a shared, non-thread-safe dictionary; the doc understates the failure mode

`src/IntegrationInfra/Emission/NdjsonBatchEmitter.cs:254-263, 280-288`;
`src/IntegrationInfra/Emission/Ndjson/NdjsonBatchSession.cs:390, 409` (and the Utf8 twin)

**Problem.** Before this change, `AdapterProgressContext.Metadata` was effectively read-only during a
publish. Now the emitter writes three keys into it on entry and rewrites one on exit, from inside a
publish method. `Metadata` is a plain `Dictionary<string, string>`
(`IAdapterExecutionContext.cs:225`). The emitter's own remark
(`NdjsonBatchEmitter.cs:104-107`) and both READMEs describe the hazard as "concurrent pages would race
and could upload to or announce each other's folder." That is the *benign* reading. Two additional
consequences are not stated:

1. Concurrent writers to a `Dictionary` are not merely racy on value — they can corrupt its internal
   structure (lost entries; in the classic case, a spin in a bucket chain inside a later lookup).
   A concurrency bug here does not present as a wrong folder, it presents as a hung or
   nonsensically-failing collector far from the publish site.
2. The *same live dictionary instance* is handed to the publisher as the request metadata
   (`NdjsonBatchSession.cs:390` `Metadata = _progress.Metadata`, `:409` likewise for multipart
   initiate). If any `IAdapterDataPublisher` implementation retains that reference and enumerates it
   after the call returns (a queued or fire-and-forget upload), the emitter's `finally` →
   `RestoreBase` mutation can throw `InvalidOperationException: Collection was modified` inside the
   publisher. *Possible concern* — depends on publisher implementations not reviewed here.

The DUAL_MODE / split-lane pattern in this codebase (assets and findings lanes co-locating on a
shared publisher and resume runner) is exactly the shape that makes a shared progress context
plausible, so this is not a theoretical caller.

**Impact.** An unenforced invariant on shared library code, where violation is silent at the call
site and manifests as data landing in the wrong batch folder, a mismatched `instanceBatchId`, or a
corrupted dictionary.

**Recommended fix (local patch).** A documented invariant that the library can cheaply enforce should
be enforced. A single re-entrancy sentinel makes the violation loud and costs one interlocked write
per page:

- take a marker in `BeginBatchScope` (e.g. `Interlocked.CompareExchange` on a scope-owner field keyed
  by the context, or a `[ThreadStatic]`-free `ConditionalWeakTable<AdapterProgressContext, object>`
  holding a busy flag) and throw `InvalidOperationException` when a second scoped publish enters with
  the same context still scoped;
- release it in the same `finally` that already runs.

If the team consciously prefers documentation over enforcement here, that is a defensible tradeoff —
but then the remark should name dictionary corruption, not only "wrong folder", so the next reader
weighs it correctly.

---

## Minor

### m4. `NdjsonContentHasher` is allocated in a field initializer, ahead of constructor validation

`src/IntegrationInfra/Emission/Ndjson/NdjsonBatchSession.cs:36`,
`src/IntegrationInfra/Emission/Ndjson/NdjsonUtf8BatchSession.cs:36`

Field initializers run before the constructor body, and the constructor can still throw at
`:51-58` (`BaseTargetPath` required; `MaxBytesPerPart`/`MaxBufferedRecords`/`MaxBufferedBytes` > 0) and
at `MemoryPressureOptions.ValidateOrThrow`. On that path the object is never returned, so
`DisposeAsync` never runs and the `IncrementalHash` — which owns an unmanaged hash handle — is
reclaimed only by `SafeHandle` finalization.

Impact is small (misconfiguration is fail-fast at startup, and the handle is finalizable), but it is
a real "disposable created before the object can be disposed" pattern in a type whose whole
`DisposeAsync` is otherwise meticulous. Fix: assign `_contentHasher` at the end of the constructor
body, after validation. Local patch, one line moved.

### m5. Nothing asserts the replay actually carries a *refreshed* credential

`tests/IntegrationInfra.Conversation.Tests/SessionAuthRetryTests.cs:96-117`

`EveryAttempt_GetsAFreshRequest_AndSendsItThroughTheSession` proves the factory was invoked twice and
produced distinct instances. It does not prove the second instance reflects the refresh. The policy's
entire value is "attempt 2 uses the new token": a caller that builds the `Authorization` header from a
value captured *before* the call would replay the stale token, the refresh would be pointless, and
every test in this file would still be green.

Fix: one test where the refresh mutates a token variable and the factory reads it, asserting
`session.SentRequests[1].Headers.Authorization` differs from `[0]` and equals the post-refresh value.
Also untested: `forceRefreshAsync` throwing (the first response is already disposed at that point —
the current code is correct, but nothing pins it).

### m6. `SessionAuthRetry` treats 403 as a refreshable auth failure

`src/IntegrationInfra/Conversation/SessionAuthRetry.cs:46, 59-60`

403 is normally *authorization* — a missing scope or app role — not an expired token. Refreshing and
replaying spends a token-endpoint round trip plus a duplicated vendor call on a deterministically
identical outcome, which the retry lens explicitly calls out ("do not retry auth failures without
refresh… or deterministic failures"). Some vendors do return 403 for stale credentials, so the set is
not simply wrong.

The README (`src/IntegrationInfra/Conversation/README.md:37-48`) documents this as verbatim parity
with production behavior used by three adapters, which is a legitimate constraint — a carry is the
wrong place to change semantics. Recommendation, deferrable: expose the status set as an optional
parameter defaulting to today's `{401, 403}`, so a vendor with role-based 403s can opt out without
forking the helper. Not merge-blocking.

### m7. `ContentHashHex` looks like a field, is an allocating call, and throws after dispose

`NdjsonBatchSession.cs:73`, `NdjsonUtf8BatchSession.cs:73`; consumed at
`NdjsonBatchEmitter.cs:375` and `:489`

Each read calls `IncrementalHash.GetCurrentHash()` (correct choice — it does *not* reset, unlike
`GetHashAndReset`) plus `Convert.ToHexString(...).ToLowerInvariant()`, i.e. two allocations, and it
throws `ObjectDisposedException` once the session is disposed. Both call sites are safely inside the
`await using` scope and fire once per publish, so there is no hot-path cost — this is a per-publish,
not per-record or per-flush, expense.

Two small things worth doing: `Convert.ToHexString(...).ToLowerInvariant()` allocates the string
twice — `Convert.ToHexStringLower(_hash.GetCurrentHash())` (.NET 9) or a manual lowercase hex write
avoids it, though at once-per-publish this is not worth churn today. More usefully, the log arguments
at `:375`/`:489` are evaluated eagerly even when `Information` is disabled; if a future caller reads
`ContentHashHex` per flush, the property's cost and its post-dispose behavior become real. A one-line
`<remarks>` noting "computed on each read; invalid after dispose" is the proportional response.

---

## Observations (no action required)

- **O8 — the v5 UUID is correct.** `BatchScopedStorage.cs:136-155` is RFC 4122 §4.3 conformant:
  namespace written big-endian, name appended as UTF-8, SHA-1, `hash[6] = (hash[6] & 0x0F) | 0x50`,
  `hash[8] = (hash[8] & 0x3F) | 0x80`, first 16 bytes read big-endian. Independently verified against
  Python `uuid.uuid5(UUID('b584b489-…'), 's3://…/batch_000003')` → `a9a3292a-43fe-56d8-8626-fcbd67059ccd`,
  which matches the pinned golden vector at `BatchScopedStorageTests.cs:176` exactly. The `bigEndian`
  overloads of `Guid.TryWriteBytes` / `new Guid(span, bigEndian)` are the right .NET 8 primitives here;
  the naive `new Guid(bytes)` mistake (little-endian field shuffling) was avoided. Pinning the vector
  against an *external* implementation rather than against itself is the right call for a
  frozen-namespace identifier.
- **O9 — global uniqueness rests on `storageUrl` carrying a per-run segment.** The id's name is
  `{baseUrl}/batch_NNNNNN`, so distinctness across runs is entirely inherited from `baseUrl`. That
  holds for the documented shape `stg/raw-data/{clientID}/{settingId}/{instanceId}`
  (`IAdapterExecutionContext.cs:375`) because `instanceId` is per-run. Worth stating explicitly
  somewhere durable: any future change that drops the instance segment from `storageUrl`, or reuses an
  `instanceId`, silently makes two different runs' page N the same document under the unique index.
  Not a defect in this diff.
- **O10 — the digest is appended before the upload is known to have succeeded.**
  `NdjsonBatchSession.cs:284` folds the buffer into the hash, then publishes at `:301`/`:320`. A
  failed flush is terminal for the session today (throw → abort → no success result → digest never
  logged), so the digest can never be observed containing bytes that did not land. Correct as written;
  the ordering only becomes wrong if flush failures ever become recoverable in place.
- **O11 — the digest genuinely describes the final object.** The multipart path disposes and resets
  the buffer per flush (`:366-376`), and the sub-5-MiB early return at `:268` bails *before* the
  append, so no bytes are hashed twice and none are skipped. `FinalizeAsync`'s
  `CompleteStartedMultipartAsync` path appends nothing, correctly. The README claims match the code.
- **O12 — orphan batch folders are possible but self-cleaning.** If a page publishes successfully and
  the collector then fails before `AdvancePage`, the object exists under `batch_NNNNNN/` but no
  progress event ever announced it, so upstream never parses it; the run-level failure event carries
  the run root. Because names are deterministic, a retry of page N overwrites the same keys. The
  README's parenthetical "the aborted multipart uploaded nothing" is true for the abort path but not
  for this narrower one (single-PUT succeeded, then a throw from telemetry or stream disposal) — an
  improbable window, and the outcome is a stale folder, not corrupt data.
- **O13 — `baseStorageUrl` is never removed.** Once `BeginPage` runs, the key stays on the envelope for
  the rest of the run, including on completion/failure events. Harmless (the typed event models are
  documented not to carry it), but it is one-way state on a shared context.
- **O14 — `StripBatchSegment` strips at most one segment** (`:180-203`). Sufficient, because
  `ResolveBaseUrl` prefers the preserved base and only one level can round-trip through `storageUrl`.
  The guard is correctly tight — exact prefix, exact length, all-ASCII-digit tail — and
  `BeginPage_DoesNotStripNonBatchTails` pins it.
- **O15 — `SessionAuthRetry` has no consumer in this repository yet** (only its own tests reference
  it). Fine for a carry, but its wire-level assumption is untested here: the tests replace
  `IHttpSession` wholesale, so they cannot detect a real session that surfaces 401/403 as a thrown
  `AdapterHttpRequestFailedException` instead of a returned response — in which case the 401 branch is
  unreachable and the helper is a no-op. The README's "Defender calls it from 3 sites" in the
  production `Shared` implementation is the evidence that this holds; worth keeping in mind at the
  first real call site.
- **O16 — nit.** `NdjsonContentHasher.cs:28` pairs `segment.Offset` with `(int)stream.Length` rather
  than `segment.Count`. Equivalent for every `MemoryStream` (`TryGetBuffer` returns
  `Count == _length - _origin`, which is exactly `Length`), and it matches the existing neighbor at
  `NdjsonBatchSession.cs:427`, so consistency arguably wins. `segment.Count` is still the correct
  pairing.

---

## What the tests do well

Worth saying plainly, because it is unusual:

- `NdjsonContentDigestTests.cs:43-47` computes the expected digest **from the input**
  (`SHA256.HashData` over the reconstructed NDJSON bytes), not from the implementation, and asserts it
  for both the single-PUT and the multipart run of the same content. It also proves the two runs really
  took different paths (`Assert.Single(singleShot.SinglePayloads)`, `multipart.Parts.Count > 1`) instead
  of trusting the option value. This cannot pass vacuously, and a part-level digest would fail it.
- `RepeatedIdenticalPageUploadsShareOneDigest_WhileProgressChangesIt` builds a same-record-count,
  same-byte-count control pair, so it tests the exact discrimination the feature exists for rather than
  "the hash changed."
- `PublishFindingsPageAsync_*` running every scoping case over both the string and UTF-8 entry points
  (`BatchScopedStorageTests.cs:410-432`) is the right response to two hand-copied lifecycle blocks in
  `NdjsonBatchEmitter.cs:181-198` and `:212-229`; a copy-paste divergence between the twins fails here.
- `…_WhenPublishThrows_ReleasesTheScope` and `…_AfterAFailedPage_RetryReusesTheSameFolderAndId` cover
  the two behaviors the `finally` exists for, and the throwing publisher is reached on a real path
  (50 MiB part size keeps it single-PUT, so the `NotSupportedException` stubs are not what fires).
- Defaults were checked for flake risk: `MemoryPressureOptions.FlushOnMemoryLoadRatioOver` and
  `BufferingOptions.FlushInterval` both default to `null`, so the "single PUT" assertions cannot be
  flipped by a loaded CI box. Good.

The gaps that matter are M1 (no test can catch it because no guard exists), M2 (uncovered branch), and
m5 (the refreshed-credential assertion).
