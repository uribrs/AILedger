# Code Review — TenableIo correlated findings, staged asset spine

Reviewer: code-reviewer-1
Date: 2026-08-17
Stack: C# / .NET 8, xUnit + Moq

## Calibration

**Change type:** feature logic in a collector, sitting directly on a shared persistence boundary
(`IAdapterObjectStore` via `GuardedObjectStore`).

**Risk level: High.** Persistence, retry/recovery semantics, a 32-way concurrent write fan-out, a
cross-repo key-schema coupling that can corrupt customer output, and ~100K objects per run at
reference scale. Scrutiny applied at the high-risk depth: failure semantics, concurrency, recovery
idempotency, scale behaviour, observability.

**Verification performed:**
- `dotnet build` on the collector and the test project: clean, 0 warnings.
- `dotnet vstest --TestCaseFilter:"FullyQualifiedName~TenableIoAssetSpineTests"`: 28/28 pass.
- Two temporary probe tests written, executed, and deleted (working tree restored, test assembly
  rebuilt). Results are cited inline below where they are load-bearing.
- Read the Infra side of every boundary this code touches: `GuardedObjectStore`, `IngestionOptions`,
  `IAdapterObjectStore` / `IAdapterObjectPruner`, `ObjectLocation`, `NormalizedUtf8Json`,
  `TopLevelJsonStreamArrayReader`, `HttpTransportFailureClassifier`,
  `AdapterHttpRequestFailedException`, `BatchScopedStorage`.

## What is genuinely well done

Stated up front because several of these are the kind of tradeoff a reviewer should confirm rather
than re-litigate.

- **Buffer ownership across the fan-out is correct, and I verified it empirically.** A probe staging
  500 assets through one chunk (well past the 32-way fan-out) asserted every object's bytes against
  its own id: all 500 matched exactly. `NormalizedUtf8Json.SerializeToSingleLine(JsonElement)`
  returns a detached `byte[]` (`WrittenSpan.ToArray()`), so the `yield return` inside the reader's
  `using (owned)` block is safe even though `Parallel.ForEachAsync` advances the enumerator — and
  therefore disposes the `JsonDocument` — while up to 31 write bodies are still in flight. This is
  the single most dangerous thing in the change and it is right.
- **The identity-derived key schema is the correct choice** and the reasoning in
  `TenableIoSpinePaths` for *not* copying Falcon's generation folder is sound: positional keys need
  generation isolation, identity keys do not. Replace-on-write plus id-derived keys genuinely makes
  re-spool converge.
- **Manifest-written-last** is correctly implemented and correctly tested (`Writes[^1]` plus
  `DoesNotContain` over `Writes[..^1]`).
- **`AssertNotParserVisible()`** in the constructor is a good instinct: it converts an
  uncheckable cross-repo coupling into a fail-fast at run start, with the hazard defined once so the
  assertion and its test cannot drift.
- **Delete-as-garbage-collection** (no correctness claim on the pruner, `CanPrune` surfaced,
  swallow-and-log) matches the `IAdapterObjectPruner` contract's explicit guidance.
- **`ValidateAssetId` rejecting rather than normalizing** separators is the right call, and the
  remark correctly identifies that `ObjectLocation` *collapses* separators and so cannot be relied on
  for this.

---

## Findings, most severe first

### 1. BLOCKER — A single HTTP 429/502/503/504 on a chunk download permanently discards up to 1000 assets, and the run reports success

**Problem.** `TryStageChunkAsync`
(`TenableIoAssetSpoolPhase.cs:196-245`) gates its retry on
`TenableIoChunkRetry.IsRetryableStreamFailure`, which is only:

```
HttpTransportFailureClassifier.IsRetryableTransportFailure(ex) || IsCircuitBreakerException(ex)
```

`IsRetryableTransportFailure` does not look at HTTP status codes at all — it matches
`SocketException` codes and message markers on `HttpRequestException`/`IOException`
("connection reset", "unexpected eof", …). `AdapterHttpRequestFailedException` derives from
`HttpRequestException`, but its message is the client's own context string, so no marker matches.
A 429 therefore falls straight through to the blanket `catch (Exception ex)` at line 236, which
logs "permanently skipped" and returns `null`. The caller adds the chunk to `excludedChunkIds`, the
phase completes, writes the manifest, and returns a successful `TenableIoAssetSpoolResult`.

The sibling flow in the same collector does handle this. `TenableIoFindingsFlow.cs:593` has a
dedicated `catch (AdapterHttpRequestFailedException ex) when (!isLastAttempt &&
TenableIoVulnsExportClient.IsTransientOrRateLimited(ex))` and computes its delay from
`Retry-After`. The new code dropped that catch. Corroborating evidence that it was dropped rather
than deliberately removed: `TenableIoChunkRetry.ComputeDelay` still carries an
`AdapterHttpRequestFailedException? ex` parameter whose `RetryAfter` and 429 branches are live code,
but the only call site (`TenableIoAssetSpoolPhase.cs:230`) hardcodes `ex: null`. The rate-limit
handling was written and then never wired up.

**Verified, not inferred.** Probe: chunk 1 returns 429 once, then valid data. Result:

```
chunkAttempts=1  staged=0  skipped=1  keys=_staging/manifest.json
```

Zero retries. One 429 → the whole chunk gone, spine empty, manifest written, phase returns
"success".

**Impact.** `AssetsChunkSize` defaults to 1000, so each 429 silently converts up to 1000 assets into
thin-host misses. Tenable rate-limits exports, and a 32-way write fan-out running against the same
tenant makes a 429 during the chunk GET a normal event, not an exotic one. The failure is invisible
in the run outcome: no exception, no partial result, only a `LogWarning` counting skipped chunks. On
a parity investigation this presents as "the vendor didn't return those assets".

**Recommended fix (local patch).** Add the missing catch ahead of the blanket one, and pass the
exception through so `ComputeDelay`'s existing `Retry-After` and 429 branches actually run:

```csharp
catch (AdapterHttpRequestFailedException ex)
    when (!isLastAttempt && TenableIoAssetsExportClient.IsTransientOrRateLimited(ex))
{
    TimeSpan delay = TenableIoChunkRetry.ComputeDelay(config, attempt, ex);
    // log + Task.Delay
}
```

Alternatively fold the status check into `IsRetryableStreamFailure` and change the existing call site
to `ComputeDelay(config, attempt, ex as AdapterHttpRequestFailedException)`. Either way the
`ex: null` at line 230 must go — it is the tell.

### 2. MAJOR — Nothing bounds the damage from skipped chunks; a knowingly-incomplete spine reports success

**Problem.** `excludedChunkIds` grows without any ceiling (server-side failures via
`TrackServerSideFailures`, plus every chunk abandoned by finding #1). At
`TenableIoAssetSpoolPhase.cs:152-158` the phase logs a warning and then proceeds to write the
manifest and return normally, whatever the count. A run where the vendor failed every chunk produces
a manifest saying `StagedAssetCount = 0, SkippedChunkCount = N` and is indistinguishable, to the
caller, from a tenant with no assets.

**Impact.** This is the amplifier that turns finding #1 from "a bug" into "silent, unbounded data
loss". It is also independently reachable via `chunks_failed` / `chunks_cancelled`. There is no
threshold at which the phase declines to declare the spine usable, and because `SkippedChunkCount`
is recorded in the manifest but never *read* by any decision, it is telemetry, not a guard.

**Recommended fix (local patch).** Fail the phase when the skipped fraction exceeds a configured
tolerance — e.g. throw when `excludedChunkIds.Count > 0 && totalChunks > 0` and the ratio exceeds a
small threshold, or (simplest defensible rule) when `excludedChunkIds.Count == totalChunks`. Given
this repo's recovery model, the cleanest option is to let the exception reach the collector's
`FlowExceptionClassifier` and be classified retryable, so the host reschedules rather than publishing
a hollow success. State the tolerance as a constant on the configuration record with a comment, so
the "how much loss is acceptable" decision is written down rather than implied by the absence of a
check.

### 3. MAJOR — Mid-spool 404 re-creates the export in a tight loop, with no delay and no cap

**Problem.** `TenableIoAssetSpoolPhase.cs:82-97`. On a 404 the handler calls `CreateExportAsync`,
clears all state, and `continue`s — jumping straight back to `GetExportStatusAsync` with **no delay**
and **no counter**. The only bound is `timeoutAt`, which defaults to
`ExportTimeoutMinutes = 180`.

**Impact.** If the vendor answers 404 persistently for the new export too (expired credentials
scoped oddly, an export reaped immediately, a tenant-side quota condition surfacing as 404), this is
a three-hour hot loop of `POST /assets/export` + `GET .../status` at full speed against a
rate-limited API with export concurrency quotas. It will also generate 429s, which via finding #1
destroy chunks. `consecutivePollFailures` does not apply here — the 404 catch is a separate handler
that never touches it.

Note also that `timeoutAt` is *not* extended on re-creation. That is defensible as a total budget,
but it means a 404 at minute 179 gives the redo one minute before `TimeoutException`. Worth a comment
either way.

**Recommended fix (local patch).** Count re-creations (2–3 is plenty), delay
`ExportStatusPollIntervalSeconds` before the `continue`, and throw once the cap is hit. The redo-based
crash model makes throwing safe here.

### 4. MAJOR — `ListStagedAssetIdsAsync` silently yields nothing on a base-URL mismatch, and the zero-vuln sweep cannot tell that from "no assets"

**Problem.** `TenableIoAssetSpine.Relative()` (lines 183-188) returns `string.Empty` when a listed
object's canonical `Url` does not start with `BaseStorageUrl + "/"`. `TryGetAssetId("")` then returns
`null`, and `ListStagedAssetIdsAsync` skips it — with no log, no counter, and no exception.

The prefix strings can diverge. The spine does `baseStorageUrl.TrimEnd('/')`; `ObjectLocation.Create`
does `baseUrl.Trim().TrimEnd('/')`. A `storageUrl` with surrounding whitespace (`"s3://b/run "`)
produces a `BaseStorageUrl` that keeps the whitespace while every `ObjectLocation.Url` has it
stripped, so **every** listed key fails the prefix test. More generally the listing's
`ObjectStat.Location` is built by the host, and `IAdapterObjectStore.ListAsync` explicitly leaves the
base/relative split free — only `Url` is contractual.

**Impact.** The zero-vulnerability sweep enumerates nothing and every asset with no vulnerabilities is
dropped from the run. The symptom is identical to a legitimately empty spine, so it will be diagnosed
as a vendor or correlation problem, not a string-prefix problem. The sweep is precisely the lane this
whole design exists to make possible, which is what raises this above Minor.

**Recommended fix (local patch).** Two small changes:
1. Normalize identically — construct `BaseStorageUrl` as
   `ObjectLocation.Create(baseStorageUrl).Url` so the spine and every location it builds are
   canonicalized by the same code.
2. Make the skip observable. Count keys that fell under the listing prefix but produced no id and log
   at warning when the count is non-zero. A foreign key under `_staging/` is ordinary (the manifest is
   one); *every* key failing is a defect, and the count distinguishes them.

The existing test (`ListStagedAssetIds_YieldsStagedAssetsAndIgnoresEverythingElse…`) cannot catch
this: the double is constructed from the same constant the spine is, so the two prefixes are equal by
construction.

### 5. MAJOR — The hot lookup path is two round trips per asset through a 4-slot semaphore, and the doc comment says otherwise

**Problem.** `TryReadAsync` (line 105) delegates to `GuardedObjectStore.ReadAllBytesAsync`, which
does `StatAsync` **then** `OpenReadCoreAsync` — two addressed requests per lookup — and takes one of
`IngestionOptions.MaxConcurrentReads` slots, whose default is **4**.

The XML comment on `TryReadAsync` says "One addressed fetch; no listing, no scan", and the class
remarks say "a lookup is an addressed fetch — nothing on the hot path scans or lists". The
second clause is true and is the important architectural claim. The "one fetch" part is not, and this
is the method the not-yet-written correlation phase will call once per finding.

**Impact.** At reference scale this doubles the request count on the busiest path in the flow, and
caps effective lookup concurrency at 4 regardless of how wide the caller fans out. Secondary: under
memory pressure `EffectiveBufferedReadCap` silently tightens to `MaxControlArtifactBytes` (1 MiB), so
a large asset record that reads fine normally throws `DataPipelineException` under pressure —
breaking the "absence is a value, not an error … this path must stay exception-free" invariant the
class documents. The remark reasons carefully about the 8 MiB ceiling but does not mention the
pressure-time 1 MiB one.

**Recommended fix.** No refactor needed now, but decide deliberately before the correlation phase
lands, because it is the caller that makes this hot:
- Correct the comment (cheapest, do it regardless) — say "one addressed lookup, no listing or scan",
  and note the stat+read cost and the pressure-time cap explicitly.
- If the doubled request count matters, the miss signal is available without the stat:
  `OpenReadAsync` throws `ObjectNotFoundException` for an absent object per contract, so a
  `try/catch (ObjectNotFoundException) → null` around a streamed read is one round trip. That trades
  away the pre-transfer size guard and makes the miss path an exception path, which the class
  explicitly does not want — so if you go this way, do it as a new `GuardedObjectStore` method on the
  Infra side rather than by bypassing the façade here.
- Whichever way, `MaxConcurrentReads = 4` needs to be raised deliberately for this deployment, or the
  lookup fan-out sized to it.

### 6. MAJOR (test coverage) — Every failure path in the spool phase is untested, and the concurrency claim the design rests on has no test

**Problem.** The 28 passing tests cover the key schema, the happy path, and the manifest ordering.
Untested: chunk retry and backoff (`TenableIoChunkRetry` in full), the 404 re-creation and its state
reset, the `TimeoutException` path, `consecutivePollFailures` exhaustion, the circuit-breaker
branches, and `ThrowIfExportFailed`. Both spool tests use a single chunk of 1–3 assets, so the 32-way
fan-out never engages.

**Impact.** For a phase whose stated purpose is failure behaviour ("crash behaviour is redo",
"absence degrades to redo"), none of that behaviour is pinned. Concretely: findings #1 and #3 are
both in code paths with zero tests, which is why they shipped. And the reader's central correctness
argument — per-record allocation because a write may still be in flight when the enumerator advances
— would survive being reverted to the reusable-buffer overload without a single test failing.

**Recommended fix (local patch).** Two tests are worth far more than the rest:
1. **The fan-out test.** Stage ~500 assets in one chunk with a distinguishable body per id, then
   assert every object holds its own id's bytes. I ran exactly this as a probe and it passes today —
   so it is a cheap regression lock on the property, not a bug hunt. Add it.
2. **The 429 test.** Return 429 once then data; assert the chunk is retried and staged. This is
   finding #1's regression test and it fails on the current code.

Then the 404-re-creation test (assert the counters reset and the new uuid lands in the manifest) and a
timeout test.

### 7. MINOR — `catch (OperationCanceledException) { throw; }` cannot distinguish caller cancellation from the parallel loop's internal cancellation

**Problem.** `TenableIoAssetSpoolPhase.cs:222-225`. When a body passed to `Parallel.ForEachAsync`
throws, the loop cancels its internal token to stop the remaining bodies; those can surface as
`OperationCanceledException`. Awaiting a task faulted with several exceptions rethrows only the
first, and which one that is is not ordered by cause. If an internal-cancellation OCE lands first,
this handler rethrows it and aborts the entire spool phase instead of retrying the chunk.

**Evidence level: likely risk, not confirmed.** I did not build a probe that forces the interleaving,
so I am not claiming it reproduces. The reasoning rests on documented `Parallel.ForEachAsync`
cancellation behaviour and on multi-exception `Task` rethrow semantics.

**Recommended fix (local patch).** Scope the guard to the token you actually own:

```csharp
catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
```

This is the idiomatic form regardless of whether the interleaving occurs, and it costs nothing.

### 8. MINOR — `TryHandlePollFailure` mutates state and logs from inside an exception filter

**Problem.** `TenableIoAssetSpoolPhase.cs:98` — `catch (Exception ex) when
(TryHandlePollFailure(ex, ref consecutivePollFailures))`. The filter increments a counter and writes
log lines. Filters run during the first pass of exception handling, before unwinding, and an
exception thrown *inside* a filter is swallowed and treated as `false` — so a logger that throws
would silently reclassify a retryable failure as fatal.

Also in the same method: the circuit-breaker branch (line 259) returns `true` **without**
incrementing `consecutivePollFailures`, so `MaxConsecutivePollFailures` does not apply to it. A
persistently open breaker spins the poll loop until `ExportTimeoutMinutes` rather than giving up at
5. Bounded, so not a hang — but it is an inconsistency between two adjacent retry ceilings and I do
not think it is deliberate.

**Recommended fix (local patch).** Catch broadly and classify inside the handler body
(`catch (Exception ex) { if (!TryHandle(...)) throw; … }`), which also removes the `ref` parameter.
Separately, decide whether breaker failures count toward the ceiling and make it explicit either way.

### 9. MINOR — The test double is not thread-safe, while the code under test writes 32-way concurrently

**Problem.** `InMemoryTenableIoStagingStore` guards `SortedDictionary` mutation with `lock (_objects)`
in `WriteAsync` only. `StatAsync`, `OpenReadAsync` (which also does an unsynchronized
`Reads.Add`), `ListAsync`, `Keys` and `Content` all touch `_objects` without the lock.

Today's tests are safe: during the fan-out only writes happen, and those are locked. It becomes a
real flake the moment a test reads while staging — which the correlation phase's tests will do, since
that phase reads the spine while chunks stream. An unsynchronized read of a `SortedDictionary`
mid-mutation can return wrong results or spin.

**Impact.** Test-only, and latent. Raised because the double is explicitly positioned as a faithful
stand-in ("a double that diverges from the host in exactly the place a guard lives is how a defect
hides"), and thread-safety is exactly such a place.

**Recommended fix (local patch).** Switch to `ConcurrentDictionary` (dropping ordered `Keys`, or
sorting on read), or put every access behind the existing lock, including the `Writes`/`Reads` lists.

### 10. NIT — `GuardedObjectStore` is never disposed in the tests

`CreateSpine` and the two probe-style harnesses construct a `GuardedObjectStore` and drop it; it owns
two `SemaphoreSlim`s and implements `IDisposable`. Harmless in practice (no OS handle is allocated
unless `AvailableWaitHandle` is touched), but the analyzer-visible pattern is worth not teaching.

### 11. OBSERVATION — `SpineWriteConcurrency = 32` is the only tuning knob in this phase that is not configuration

Every other bound here comes off `TenableIoCollectorConfiguration` (`MaxChunkRetryAttempts`,
`ChunkRetryBaseDelaySeconds`, `ExportTimeoutMinutes`, `AssetsChunkSize`). The write fan-out is a
`private const`. The justification comment is good and 32 is a reasonable number; I am not asking for
a config knob speculatively. Noting it because if a tenant turns out to need it lowered — 32
concurrent PUTs per pod interacts with the host's `MaxConnectionsPerServer` for the store client,
which this repo does not own — it will need a code change and a release. Falcon's staging spooler,
the nearest sibling, writes sequentially, so there is no precedent in this repo to calibrate against.

### 12. OBSERVATION — Nothing outside `Flows/Findings/Correlated/` references these types yet

`grep` confirms no consumer of `TenableIoAssetSpine` or `TenableIoAssetSpoolPhase` outside the new
folder and its tests. Expected for a staged slice and not a defect. Flagged only so it is on the
record that the read-side contract (`TryReadAsync`, `ListStagedAssetIdsAsync`) has no real caller
yet — which is why findings #4 and #5 are worth settling before that caller exists rather than after.

---

## Documentation accuracy

The XML documentation in this change is unusually substantive and mostly load-bearing rather than
decorative — the `TenableIoSpinePaths` argument about generation folders and the
`TenableIoAssetSpineManifest` argument about manifest-versus-cursor are both genuinely useful to a
future maintainer. Three claims do not match the code, and because these comments are the kind
readers will trust, the inaccuracies are worth correcting:

1. **`TryReadAsync`: "One addressed fetch"** — it is a stat plus a read. See finding #5.
2. **`TenableIoAssetSpoolReader` remarks: using the reusable-buffer overload "would be a correctness
   bug … Shared buffer contents would be overwritten underneath it".** Not true of the current Infra
   code: `NormalizedUtf8Json.SerializeToSingleLine(element, reusableBuffer)` also ends with
   `WrittenSpan.ToArray()`, so it returns a detached copy too and aliases nothing. The *decision* is
   still right (the fresh-array overload is the one whose safety does not depend on an Infra
   implementation detail), so keep the code and restate the reason: the hazard is that the overload's
   contract invites a future zero-copy implementation, and this call site could not tolerate one.
   As written, the comment asserts a bug that does not currently exist.
3. **`TenableIoAssetSpoolPhase` class remarks: "This phase has one path."** It has a retry path, a
   permanent-skip path, an export-re-creation path and a timeout path. The intended claim — no memory
   budget and therefore no degrade path — is true and worth keeping; the sentence overstates it.

## Verdict

The core design is sound and the hardest part of it — buffer ownership across a bounded write
fan-out, and the identity-derived key schema that makes re-spool idempotent — is correct, and I
verified both rather than taking the comments' word for it.

What is not safe to merge is the failure handling around the chunk download. Finding #1 is confirmed
by execution: one 429 discards up to 1000 assets with no retry, and the phase reports success.
Finding #2 removes any ceiling on how much of that can accumulate, and finding #3 supplies a loop
that will generate 429s. Those three compose into unbounded silent data loss that presents as a
vendor problem, on a path with no test coverage (#6).

**Merge-blocking: #1.** **Fix before merge unless consciously accepted: #2, #3, #4, #6.** #5 needs
its comment corrected now and its cost decided before the correlation phase calls it. The rest are
improvements.
