# Code Review — S3 object-store capability + Falcon two-phase correlation

Independent review of uncommitted work on `feat/s3-capability-falcon-two-phase` across three repos.

## Calibration

| Repo | Change type | Risk | Depth applied |
|---|---|---|---|
| IntegrationInfra `Ingestion/` + `Contracts/` | shared library / public contract | **High** — new concern consumed by every collector, concurrency primitives, persistence boundary | architecture, failure semantics, concurrency, scale |
| IntegrationServiceBus `Infrastructure.AWS` | infrastructure, persistence + destructive IAM action | **High** — S3 read/write/delete, deployment-gated | failure semantics, idempotency, error propagation |
| cymulate-integration-parsers | feature logic (dedupe semantics) | **Medium-High** — silently changes which vulnerability row reaches the customer | determinism, null handling, partitioning |

### What was executed

- `IntegrationInfra.Ingestion.Tests` — **58 passed**.
- `Cymulate.IntegrationServiceBus.Infrastructure.AWS.UnitTests` — **18 passed**.
- `tests/test_input_resolver.py` — **8 passed** (includes the new decoy test).
- `tests/test_crowdstrike_live_edge_replay.py` — **NOT RUN**: no JVM in this environment (`Unable to locate a Java Runtime`), so all 6 Spark tests error at session start. Every finding on the PySpark change below is from code reading, not execution.
- Clean-cache restore probe (evidence for BLOCKER-1) — run and captured.

---

## BLOCKER-1 — The ISB solution does not restore on any machine but this one

`src/Cymulate.IntegrationServiceBus/Directory.Packages.props:106`

```xml
<PackageVersion Include="Cymulate.Integration.Client" Version="1.0.0-preview.7-local" />
```

**Problem.** That version exists only in `/Users/user/Dev/.local-nuget-preview/`, a folder feed registered in the developer's **user-level** `~/.nuget/NuGet/NuGet.Config`. The repo's own `src/Cymulate.IntegrationServiceBus/nuget.config` does `<clear />` (dropping inherited sources) and maps `Cymulate.*` exclusively to CodeArtifact via `packageSourceMapping`. So the local feed is unreachable through the repo config by two independent mechanisms. It restores today only because the package is already sitting in `~/.nuget/packages/`.

**Impact — confirmed, not predicted.** Restore with a fresh package folder and the repo's config:

```
error NU1102: Unable to find package Cymulate.Integration.Client with version (>= 1.0.0-preview.7-local)
  - Found 4 version(s) in cym-dom/cym-repo-nuget [ Nearest version: 1.0.0-preview.6 ]
```

This fails for `Infrastructure.AWS`, `Domain`, and `Application.Query` — i.e. the whole solution, not just the new code. CI cannot build this branch, and neither can a second developer.

**Recommended fix.** Publish a real `preview.7` to CodeArtifact and pin to it. The in-repo comment already says this ("BEFORE MERGE this must become a real published preview.7+"), so the requirement is understood — the finding is that the branch is currently un-buildable anywhere else, which makes every other test result on it unverifiable by CI.

Local patch. Also note the same `Contracts/*.cs` files exist as **source** in IntegrationInfra and as a **packed binary** in ISB; until they are one published version, the two can drift silently and produce a `MissingMethodException` at runtime rather than a compile error.

---

## BLOCKER-2 — Read slots are held across `yield return`, so the intended composition of the shipped staging primitives deadlocks

`src/IntegrationInfra/Ingestion/GuardedObjectStore.cs:306-328` (`AcquireReadSlotAsync`), `:187-198` (`ReadNdjsonLinesAsync`), `src/IntegrationInfra/Ingestion/Staging/FrozenKeyList.cs:98-106`

**Problem.** `ReadNdjsonLinesAsync` holds a read stream — and therefore its concurrency slot — for the entire duration of the enumeration, and `yield return`s to the consumer while holding it. `FrozenKeyList.ReadBatchesAsync` sits directly on top and yields each batch to its caller while that slot is still held. So the consumer's loop body executes inside the slot.

The primitives shipped in this same folder invite exactly the nested read that this makes fatal:

```csharp
await foreach (var batch in FrozenKeyList.ReadBatchesAsync(store, list, 500, ct))   // holds slot #1
    foreach (var key in batch)
        var prior = await states.ReadAsync<DeviceState>(key, ct);                   // wants slot #2
```

`PriorStateStore.ReadAsync` → `GuardedObjectStore.ReadAllBytesAsync` → `OpenReadAsync` → `AcquireReadSlotAsync`. Two ways this hangs forever:

1. **Pressure gate.** `_pressureSlot` is `new(1, 1)` (line 40). While `MemoryPressureGate.IsUnderPressure` is true, the outer enumeration holds the single permit until it finishes; the inner read waits on it. Nesting depth 2 is enough. This is not a rare state — it is the steady state of a pod that is over the configured load ratio, which is precisely when the option is meant to be on.
2. **Depth semaphore.** Independent of memory pressure: nest deeper than `MaxConcurrentReads` and `_readSlots.WaitAsync` never returns. With `MaxConcurrentReads: 1` — a legal, indeed the natural, setting for a memory-constrained pod, and the value two of the tests themselves use — a *single* nested read deadlocks unconditionally.

There is no timeout on either wait. The only escape is a cancellable token, and `ReadAsync`'s `cancellationToken` defaults to `default`.

**This is a known failure class in this file that was reasoned about and then applied inconsistently.** `ListAsync`'s own doc comment (line 205-208) says: *"It deliberately does NOT take a read slot: a listing loop that opens objects inside itself would then be waiting on a slot it is holding."* Identical reasoning applies to a batch loop that opens objects inside itself, and it was not applied. `README.Ingestion.md:81-82` presents the single-permit narrowing as the design's virtue ("pressure narrows the pool to one without resizing the semaphore") without noting that it also makes nesting impossible.

**Not covered by tests.** `_pressureSlot` is reachable in exactly one test path, `Memory_pressure_tightens_the_buffered_read_ceiling_to_control_artifact_size` (GuardedObjectStoreTests.cs:75-86) — and there the size check throws from `StatAsync` *before* `OpenReadAsync` is ever called, so the gate is never acquired. `_pressureSlot` has **zero** coverage, and the documented "effective depth drops to one" behaviour is asserted nowhere.

**Recommended fix (refactor, small).** Do not hold a slot across a `yield return` to user code. Either bound the *open* rather than the *stream lifetime* for the enumerating helpers, or make slot acquisition re-entrant per logical operation (e.g. an `AsyncLocal<int>` depth counter that lets a nested read pass the gate it already owns). If the current semantics are kept deliberately, the pressure gate must at minimum become re-entrant and every helper that yields while holding a stream must say so in its own doc comment. A timeout on `WaitAsync` converts a silent hang into a diagnosable error and is worth having regardless.

---

## MAJOR-3 — Every streamed write reports `SizeBytes = 0`, and the README's justification for it is false

`Infrastructure.AWS/Services/S3AdapterObjectStore.cs:103-107`

```csharp
var stream = request.ContentStream!;
putRequest.InputStream = stream;
// Only known upfront when the caller's stream is seekable; PutObjectAsync happily streams
// the rest, so this is purely for the byte count we report back, not a write constraint.
size = stream.CanSeek ? stream.Length : 0;
```

**Problem.** The only production caller of the stream form is `FrozenKeyList.FreezeAsync`, which passes `NewlineDelimitedTextStream` — non-seekable by construction (`CanSeek => false`, `Length` throws; NewlineDelimitedTextStream.cs:27-31). So `size` is **always 0** on that path, and `FreezeAsync` returns `FrozenKeyListResult(location, 0, AlreadyFrozen: false)` (FrozenKeyList.cs:71).

The resulting field is incoherent across the two branches of the same method: a **first** freeze reports `SizeBytes: 0`, a **resumed** freeze reports the real size from `StatAsync` (line 63). Any caller that logs it, compares it, or sanity-checks the frozen universe against it gets a number that means "0 bytes" on the run that actually wrote the data.

**Second, separate concern — the comment's factual claim.** `README.Ingestion.md:130-132` states the host "must handle it (its S3 path already streams via multipart)". The `IAdapterObjectStore` S3 path does **not** use multipart: it calls `PutObjectAsync` with the raw stream. Multipart lives in a different class, `S3AdapterDataPublisher` (Emission's lane). The strings in AWSSDK.S3 4.0.24.5 confirm that the non-seekable handling (`UploadUnseekableStreamAsync`, `ConstructUploadPartRequestForNonSeekableStream`, `MakeStreamSeekable`) belongs to `Amazon.S3.Transfer.Internal.MultipartUploadCommand` — the TransferUtility path — not to `PutObjectAsync`.

I could not execute a real `PutObjectAsync` against S3 here, so I am labelling the consequence at two confidence levels:
- **Confirmed:** `SizeBytes` is 0 for every non-seekable write; the README's multipart claim does not describe this code path.
- **Likely risk (unverified against live S3):** `PutObjectAsync` with a non-seekable, unknown-length `InputStream` may fail outright, since S3 requires a content length and no `Headers.ContentLength` is set. The sibling publisher takes a caller-supplied `ContentLength` for exactly this case (`S3AdapterDataPublisher.cs:201`); the new contract gives a caller no way to supply one. **This needs one integration test against real S3 or LocalStack before merge** — if it does fail, `FreezeAsync` never works in production and the entire two-phase flow is dead on arrival.

**Recommended fix.** Add an optional `ContentLength` to `ObjectWriteRequest`, have `FreezeAsync` supply it (or make the key list count its bytes as it streams and report that), and set `putRequest.Headers.ContentLength` when known. Local patch, plus one contract field.

---

## MAJOR-4 — `Rebase` splits on the base key without a delimiter, so listed objects get the wrong `Location`

`Infrastructure.AWS/Services/S3AdapterLocationResolver.cs:41-50`

```csharp
var relative = objectKey.Length > baseKey.Length
    ? objectKey[baseKey.Length..].TrimStart('/')
    : string.Empty;
```

**Problem.** S3 prefix matching is a byte-prefix match with no notion of a path boundary, and this strips `baseKey.Length` characters without checking that the next character is a separator.

**Failure scenario.** `storageUrl = s3://bucket/tenant/run-1` (no trailing slash — the common form), and the bucket also holds `tenant/run-10/page.json`. `ListObjectsV2(Prefix: "tenant/run-1")` matches it. `Rebase` strips `tenant/run-1` → relative `0/page.json` → the yielded `ObjectStat.Location` resolves back to `tenant/run-1/0/page.json`, **an object that is not the one that was listed**. Same for a sibling file `tenant/run-1.json` → relative `.json` → `tenant/run-1/.json`.

**Impact.**
- `GuardedObjectStore.ListAsync` consumers that read what they listed read the wrong key or 404.
- `S3AdapterObjectPruner.DeletePrefixAsync:76` re-resolves each entry's Location into a key and deletes *that*. So it issues a delete for `tenant/run-1/0/page.json` — which S3's multi-object delete reports as successfully **Deleted** even though it never existed (idempotency) — while `tenant/run-10/page.json`, the key it actually listed, survives. Combined with MAJOR-5 the method returns a healthy count and leaves the staging area behind.

**Recommended fix.** Require the boundary: skip an entry unless `objectKey.Length > baseKey.Length && objectKey[baseKey.Length] == '/'` (or `baseKey` is empty / already ends in `/`). Better still, carry the raw S3 key through instead of round-tripping the key → URL → key. Local patch.

**Test gap.** Neither suite can see this. `S3AdapterObjectStoreTests.ListAsync_PagesLazily...` uses only keys that are true path children (`env/run-id/a.json`). `InMemoryObjectStore.ListAsync` (IngestionTestDoubles.cs:94) yields `new ObjectLocation(key)` — the whole key as `BaseUrl`, a materially different shape from what the real implementation produces — so the Infra suite never exercises the rebase seam at all.

---

## MAJOR-5 — The pruner reports success when deletes are denied

`Infrastructure.AWS/Services/S3AdapterObjectPruner.cs:114-121`

```csharp
foreach (var error in response.DeleteErrors ?? [])
    logger.LogWarning("Failed to delete {Key} from {Bucket}: {Code} {Message}", ...);

return response.DeletedObjects?.Count ?? 0;
```

**Problem.** `DeleteErrors` are logged and discarded. The method's only channel is an `int`, and the contract documents it as "how many were removed" — so a caller cannot distinguish complete success from systematic failure.

**Failure scenario.** The deployment sets `ObjectPruningEnabled=true` but the IAM role lacks `s3:DeleteObject`. Every key comes back in `DeleteErrors` with `AccessDenied`; `DeletedObjects` is empty; `DeletePrefixAsync` returns **0** and throws nothing. A collector that tears down its staging area and then marks the phase complete proceeds happily, and the staging area accumulates on every run forever. The only signal is a warning line per key — 1000 of them per batch, i.e. a log flood that is also the sole indication of a hard misconfiguration.

This inverts the stated design intent. The `ObjectPruningEnabled` gate exists (per the DI comment) so that "a deployment without delete permission fails to resolve `IAdapterObjectPruner` instead of a caller forgetting to check a flag" — a structural gate rather than a runtime flag. But the flag being *on* while the IAM grant is *absent* is precisely the case that then fails silently.

**Recommended fix.** Throw when `DeleteErrors` is non-empty after excluding genuinely benign codes, or return a result type carrying `(deleted, failed, firstError)`. A structural gate deserves a structural failure. Local patch.

**Test gap.** `DeleteErrors` is never non-empty in any test (`S3AdapterObjectPrunerTests.cs:75` sets it to `[]` explicitly). The most operationally significant branch of the destructive class has no coverage. The bucket/region `GroupBy` at line 44-46 — added with a comment claiming "a mixed batch still deletes correctly" — is likewise untested: every test uses one bucket.

---

## MAJOR-6 — `StatAsync` breaks the resume primitive under a least-privilege IAM policy

`Infrastructure.AWS/Services/S3AdapterObjectStore.cs:39-47`

**Problem.** Only `AmazonS3Exception` with `StatusCode == NotFound` maps to `null`. S3's documented `HeadObject` behaviour is that a principal **without** `s3:ListBucket` on the bucket receives **403 Forbidden** for a key that does not exist, rather than 404. A read/write-only policy (`GetObject` + `PutObject`, no `ListBucket`) is the normal least-privilege shape.

**Failure scenario.** Under such a policy, `StatAsync` on an absent object throws `AmazonS3Exception(403)`. `ExistsAsync` therefore throws instead of returning `false`. Both interface docs name this the resume primitive — "how a caller proves a deterministic-key write already landed, which is what lets a resumed run skip completed work without consulting a cursor" (`IAdapterObjectStore.cs:34-36`). So on a deployment that has not been granted `ListBucket`, `FrozenKeyList.FreezeAsync:60` throws before it can freeze anything, and every resume check fails. The failure is total and depends only on the IAM policy shape, not on data.

**Recommended fix.** Treat 403 on the stat path as indeterminate: either map it to `null` with a warning (accepting that a genuine permission error then reads as "absent"), or document `s3:ListBucket` as a hard requirement of `IAdapterObjectStore` and assert it at startup. The second is safer — silently reading `AccessDenied` as "not there" would make a resumed run redo all its work. Local patch + a deployment note.

Related, lower severity: `OpenReadAsync:63-67` logs a 404 at **Error** and throws `FileNotFoundException`, while `StatAsync` treats the same condition as an unremarkable `null`. `FrozenKeyList.ReadBatchesAsync` documents "a missing list yields nothing rather than throwing", but its `ExistsAsync`-then-open sequence (FrozenKeyList.cs:92-98) can still surface `FileNotFoundException` if the object disappears in between, and a normal absent object produces an Error-level log line.

---

## MAJOR-7 — `WriteAsync_WithContentStream_PutsObjectFromStream` passes for the wrong reason

`Cymulate.IntegrationServiceBus.Infrastructure.AWS.UnitTests/S3AdapterObjectStoreTests.cs:121-134`

```csharp
using var stream = new MemoryStream([5, 6, 7]);
var result = await _sut.WriteAsync(new ObjectWriteRequest(Location, ContentStream: stream));
result.SizeBytes.Should().Be(3);
```

**Problem.** `MemoryStream` is seekable, so this exercises `size = stream.Length` — the branch that **never executes in production**. The only real caller supplies a non-seekable stream and gets `size = 0`. The test asserts `SizeBytes == 3` and passes, which reads as "streamed writes report their size correctly" when the opposite is true on the live path. It would not fail if the non-seekable branch were deleted.

The Infra side seals the same blind spot from the other direction: `InMemoryObjectStore.WriteAsync` (IngestionTestDoubles.cs:64-80) buffers the stream and returns the **real** `payload.Length`, so `FrozenKeyListTests` sees a correct size where production returns 0. Neither suite can observe the defect, and `FrozenKeyListTests.Round_trips_keys_in_order:22` asserts `AlreadyFrozen` but never `SizeBytes`.

**Recommended fix.** Test the stream form with a deliberately non-seekable stream and assert the intended contract (whatever it is decided to be). Local patch. Ideally the same fake would be shared, or the Infra double would be made to reproduce the host's reporting behaviour.

---

## MAJOR-8 — Two parser tests assert nothing about the behaviour they are named for

`tests/test_crowdstrike_live_edge_replay.py`

**(a) A stale test that documents removed behaviour** — `test_replayed_finding_with_changed_status_is_nondeterministic:216-239`. The name says `is_nondeterministic` and the docstring says *"dropDuplicates(["id"]) keeps an arbitrary one; this records which"* — describing the code that this very change deleted. It then asserts only `assets_df.count() == 1` and `len(rows) == 1`; it never checks *which* copy survived, so it "records which" by `print` alone. It passes identically with the change reverted.

Worse, it is the one test that actually reaches the `monotonically_increasing_id` fallback: both findings use `_finding`'s default `updated_timestamp="2026-07-01T00:00:00Z"`, so `updated_timestamp` and `created_timestamp` tie and the winner is decided by `_record_order` alone. The single case exercising the least trustworthy tiebreak asserts nothing about its outcome.

**(b) A dead assertion** — `test_live_edge_replay_same_shard_single_chunk:145-147`:

```python
# last-wins spine: the surviving asset must carry the LATER last_seen
last_seen = assets_df.select("last_seen").collect()[0]["last_seen"]
print(f"  surviving last_seen: {last_seen}")
```

The comment states an invariant; the code prints it and moves on. The test passes if the spine keeps the **stale** host — the exact regression the comment is guarding against.

**Recommended fix.** Rename (a) to what it now verifies and assert the surviving `(status, severity)`; add a case with tied timestamps asserting the documented record-order tiebreak, or drop the claim. Turn (b)'s print into `assert last_seen == <later>`. Local patch.

**Credit where due:** `test_status_drift_winner_depends_on_lane_order:242-276` is a genuinely good test — parametrized over both lane orders, asserting the *same* winner, checked against the post-processed output (`("resolved", "low")`, so normalization is covered end to end). It would fail under the old `dropDuplicates`. That is the assertion that earns the change. `test_live_edge_replay_across_shards_real_path` correctly goes through `prepare_parser_options` rather than the `input_mode` shortcut the other four take.

---

## MAJOR-9 — The dedupe docstring promises determinism that `monotonically_increasing_id` cannot deliver

`libs/packages/parsers/deprecated/crowdstrike/CrowdstrikeAssetsFindingsCorrelated.py:232-245`

**Problem.** The docstring closes with: *"and finally to source record order ... so two rows with identical timestamps still resolve the same way on every run."* `F.monotonically_increasing_id()` is a **non-deterministic** Spark expression. Its value is `partition_index << 33 | row_index`, so it depends on how the input was split — which depends on file listing order, `spark.sql.files.maxPartitionBytes`, the number of shard files, and AQE decisions. The same input re-read with different split sizing yields different `_record_order`, and among timestamp-tied rows the winner flips. "The same way on every run" is not a property this mechanism has.

**Impact.** Bounded but real: only rows tied on both `updated_timestamp` and `created_timestamp` are affected, and for those the two candidates are usually replay copies of the same finding. But that is exactly the case where the copies can differ in `status` — which is the whole motivation for the change. Rerunning the same Glue job over the same S3 shards can then emit `open` on one run and `resolved` on the next.

A secondary hazard from non-determinism: if the returned DataFrame is consumed more than once without caching, `monotonically_increasing_id` is regenerated and the two evaluations can select different survivors, so a finding count and the findings themselves can disagree.

**Context — partly pre-existing.** `_build_asset_spine:195-202` already uses this idiom, and there `_record_order` is the **sole** ordering key, which is the more fragile case; it is not part of this diff. The new code demotes it to a third-level tiebreak, which is an improvement. The finding is the docstring's determinism claim, and the missing test for the tied case.

**Recommended fix.** Either soften the claim to "a stable-enough tiebreak within a single job run", or make it genuinely deterministic by ordering on a real value — `input_file_name()` plus a per-file row index, or the vendor's own `id`/`aid` as a final deterministic tiebreak. `F.col("id")` is already the partition key, so a deterministic last resort is nearly free. Local patch.

**Ordering logic otherwise reviewed and sound:**
- `Window.partitionBy("id")` is the correct partitioning — one shuffle keyed on the dedupe key, no unpartitioned window, no driver-side collect.
- `_record_order` is assigned **before** the explode (line 232), so all findings from one source record share one order value. That is the right granularity for "source record order"; assigning it after the explode would have been meaningless.
- `desc_nulls_last()` is the correct choice in both positions: a row that has a timestamp beats a row that does not.
- `row_number() == 1` always yields exactly one row per `id`, including when all three keys tie.
- Null `id` collapses all null-id findings to one row — unchanged from `dropDuplicates(["id"])`, not a regression.
- `Window` **is** imported (line 56); no `NameError`.
- Cost is comparable to the `dropDuplicates` it replaces (both shuffle; the window adds a per-partition sort). Proportionate for the correctness gained.

---

## MINOR findings

**M-1 — `FrozenKeyList` validation throws from inside the PUT, so production sees a different exception than the tests do.** `FrozenKeyList.cs:66,118-136`. `Validated()` throws `DataPipelineException` lazily as `NewlineDelimitedTextStream` is drained — which in production happens inside `PutObjectAsync`, while the SDK reads the request body. `S3AdapterObjectStore` catches only `AmazonS3Exception`, so the `DataPipelineException` surfaces wrapped in whatever the SDK's send path produces, not as itself. `FrozenKeyListTests.Rejects_a_key_that_would_corrupt_the_framing:72-76` asserts `DataPipelineException` because the in-memory double calls `CopyToAsync` directly. Validate keys eagerly, or accept that the caller sees a transport exception and say so.

**M-2 — Lexicographic string comparison standing in for chronological.** `CrowdstrikeAssetsFindingsCorrelated.py:237-238`. `updated_timestamp` is a JSON string and is never cast; ordering is `StringType`. That equals chronological order only while every value is fixed-width UTC with a `Z` suffix (as the fixtures are). A `+02:00` offset form or varying fractional-second precision breaks it silently. `F.to_timestamp` would make the intent explicit.

**M-3 — Misconfiguration silently becomes the default.** `IngestionOptions.cs:171-181`. `TryReadEnvLong`/`TryReadSectionInt` return `null` on an unparseable value, which falls through to the built-in default with no log and no throw. `Ingestion__MaxConcurrentReads=four` yields 4 and looks deliberate. This sits oddly next to `ValidateOrThrow`, which loudly rejects a value that is out of range — parsed-but-invalid throws, unparseable is ignored. Make an unparseable-but-present key a misconfiguration.

**M-4 — `long` limits, `int` implementation.** `GuardedObjectStore.cs:158,176`. `new MemoryStream(capacity: (int)Math.Min(...))` and `(int)buffer.Length` overflow if `MaxInMemoryObjectBytes` is configured above `int.MaxValue`, which `ValidateOrThrow` permits (it checks only `> 0`). Result is a negative capacity and an `ArgumentOutOfRangeException` from a guard class, rather than a clear misconfiguration error. Cap it at `int.MaxValue` in validation.

**M-5 — `GuardedObjectStore` holds two `SemaphoreSlim`s and is not disposable.** `GuardedObjectStore.cs:39-40`. Documented as per-run state ("build one per run"), so instances are created and abandoned repeatedly. `SemaphoreSlim` without `AvailableWaitHandle` does not strictly leak, but a per-run type owning synchronization primitives should implement `IDisposable`.

**M-6 — The DI comment describes a gate that is not the one implemented.** `Infrastructure.AWS/DependencyInjection.cs:+153-154`. "Register the control-artifact object store alongside the data publisher — same bucket, same gate." It is not the same gate: the publisher requires `hasDataBucket && DataPublishingEnabled`, the store requires `hasDataBucket` alone. A deployment with `DataPublishingEnabled=false` gets an object store but no publisher. That may well be intended; the comment should say so rather than assert equivalence.

**M-7 — `FreezeAsync` takes `IEnumerable<string>`, so a lazily-paged universe blocks the SDK's IO thread.** `FrozenKeyList.cs:54`, `NewlineDelimitedTextStream.cs:65-75`. `ReadAsync` is `Task.FromResult(Read(...))` — fully synchronous — and `Read` drives `_lines.MoveNext()`. If the caller's enumerable fetches vendor pages (the documented motivation: "several hundred thousand device ids"), each `MoveNext` blocks synchronously inside the SDK's write loop, with no cancellation after the initial check. The bounded-memory claim in the doc holds only if the source enumerable is already materialized — which is the thing the design says it is avoiding. An `IAsyncEnumerable<string>` overload would resolve it.

**M-8 — Same invariant, two exception types.** `GuardedObjectStore.WriteAsync:245-250` throws `DataPipelineException` for the both/neither content form; `S3AdapterObjectStore.WriteAsync:79-83` throws `ArgumentException` for the identical condition. Callers see a different type depending on which layer they entered through. Both suites assert their local type, so neither notices.

**M-9 — The new parser test reaches into private helpers.** `tests/test_input_resolver.py:+109-111` calls `helpers._resolve_strategy_file_paths`. Also, the closing `assert not any("_staging" in Path(p).parts ...)` is unreachable-by-construction after the two exact-equality assertions above it. The test's own docstring is admirably honest that it locks the **local** path while Glue runs the S3 path — worth acting on: the S3 branch is the one that ships.

---

## Observations (no action)

- The `Ingestion` folder's documentation is unusually good and states real constraints (write-once rationale, "a manifest is a description, never a cursor", last-writer-wins on `PriorStateStore`, "dispose is not optional"). Two claims are wrong and are called out above (multipart in MAJOR-3, run-to-run determinism in MAJOR-9); the rest matched the code.
- Untracked `bin/`/`obj/` output under both new test projects is correctly covered by the existing `.gitignore` in each repo — verified with `git check-ignore`.
- The developer's user-level `~/.nuget/NuGet/NuGet.Config` stores a CodeArtifact token as `ClearTextPassword`. Pre-existing and outside this diff; noted only because BLOCKER-1 required reading that file.
- `S3AdapterObjectStore.WriteAsync:98` does `new MemoryStream(content.ToArray())`, an extra full copy of an already-contiguous buffer. Bounded by `MaxInMemoryObjectBytes` (8 MiB default), so not worth changing on its own.
- `CappedNdjsonLineReader:70` uses `Array.IndexOf`; `readBuffer.AsSpan(offset, read - offset).IndexOf(Newline)` is the vectorized form. Measurable only on large lanes.

## Checked and found sound

- **`CappedNdjsonLineReader`** — the most delicate code here and it is correct. Buffer growth is capped before allocation (`Append:126-142`, `grown >= required` holds because the cap check precedes it); `ArrayPool` rent/return is balanced through the `ref byte[]` swap and the `finally` observes the swapped buffer; a line exactly at the cap is accepted and cap+1 throws; CR trimming happens on the accumulated line so CRLF is safe across read boundaries; the trailing unterminated line is emitted; each row is a fresh array. The tests cover the cap boundary exactly, cross-buffer framing, blank/whitespace skipping, line numbering, and the empty object.
- **`ReadSlotStream`** — `Dispose`/`DisposeAsync` are idempotent, the slot is released in a `finally` so an inner-dispose failure still returns it, `ReadSlot.Dispose` guards with `Interlocked.Exchange`, and `base.DisposeAsync()` re-entering `Dispose(true)` is harmless.
- **`GuardedObjectStore.ReadAllBytesAsync`** — the stat-then-verify-during-copy double check is the right call, and it is tested against a deliberately lying store (`StatSizeOverride`). Per-call caps tighten and never widen. `OpenReadAsync`'s failure path disposes the slot before rethrowing, and that is tested.
- **Batching boundaries** — all three are off-by-one-free: `MaxListedObjects` yields exactly `max` then throws on the next (`GuardedObjectStore.cs:223`); `FrozenKeyList.ReadBatchesAsync` emits full batches plus a short tail, verified by a `[7,3]→[3,3,1]` theory including the `[1,1]` and `batchSize > count` edges; `DeletePrefixAsync` flushes at exactly 1000 and then the remainder, and the yielded `List` is a fresh instance each time so no aliasing.
- **Laziness is real end to end, and is tested as such rather than asserted in a comment** — `ListAsync_PagesLazily` counts SDK page calls per `MoveNextAsync`; `DeletePrefixAsync...` asserts the first batch fires when exactly 1000 items have been *produced*, not after the prefix was fully listed; `Ndjson_lines_release_the_read_stream_when_the_consumer_stops_early` asserts the stream is disposed on `break`. These are the tests I would have asked for.
- **`ListObjectsV2` pagination** — `IsTruncated is true` gates the continuation rather than a lingering token, and `S3Objects ?? []` handles the empty page.
- **Traversal rejection** — `RejectTraversal` splits on `/` and compares whole segments (not a substring match), so `a..b` is allowed and `../` and `a/../b` are refused; `PriorStateStore.ValidateKey` additionally rejects separators and control characters. Both are tested, including the control-character case.
- **`ToUtc`** — `DateTime.SpecifyKind` on the S3 timestamps is correct and the reason is documented; without it an implicit conversion would shift object age by the host's offset. Tested.
- **`PriorStateStore`** — deterministic key derivation, the read-modify-write round trip, and the `mutate → null` skip-the-write path are all tested, and the un-fixable last-writer-wins limitation is documented at the type level rather than papered over.
- **The Infra/host boundary holds.** No cloud SDK reference reached `IntegrationInfra`; `ObjectLocation` stays path-shaped and the host owns decomposition. The pruner-as-separate-interface decision is a good one — a structural permission gate beats a runtime flag. MAJOR-5 is a defect in how that gate reports failure, not in the design.

---

## Verdict

Two blockers. **BLOCKER-1** means CI cannot build the branch at all, so no test result on it is currently reproducible. **BLOCKER-2** is the more serious engineering problem: the two staging primitives shipped in the same folder deadlock when composed the way their own documentation suggests, and the file already contains the correct reasoning about that failure class applied to a different method.

MAJOR-3 additionally carries an unresolved question that only an integration test can settle — whether `PutObjectAsync` accepts the non-seekable stream `FreezeAsync` hands it at all. If it does not, the frozen-key-list primitive has never worked outside the in-memory double, and no unit test in either repo would reveal that.

The craft on display is high — the NDJSON reader, the forced-streaming guard, the laziness tests, and the honesty of the documentation are all above the bar. The defects cluster in one specific place: the seam between the Infra façade and the S3 implementation, where both suites use doubles whose behaviour diverges from the real host in exactly the way that hides the bug.
