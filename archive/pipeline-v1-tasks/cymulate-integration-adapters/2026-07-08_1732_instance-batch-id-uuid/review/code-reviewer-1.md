# Code Review 1 — instanceBatchId → deterministic UUIDv5

**Scope:** `git diff origin/dev` on branch `batchful-uploads-uuid` — 5 files: `BatchScopedStorage.cs` (new `BuildBatchInstanceId`, doc updates), `Egress/README.md`, 3 test files.

**Calibration:** shared library code on the egress hot path (once per published page in long-running collectors), feeding a cross-service identity contract (backend unique index + upsert dedup). Risk: **High** (persistence identity, replay/idempotency semantics). Reviewed at high depth with .NET lenses.

## Verdict

**Approve.** No blockers, no majors. The cryptographic core is correct and I verified it independently: byte order, version/variant bit placement, buffer sizes, and encoding all match RFC 4122 §4.3, and the golden vector reproduces exactly against Python's `uuid.uuid5` for the frozen namespace (`a9a3292a-43fe-56d8-8626-fcbd67059ccd` — confirmed by independent computation, not just the pinned test). Findings below are minors and observations.

### Correctness verification detail (confirmed, not speculative)

- `BatchIdNamespace.TryWriteBytes(input, bigEndian: true, ...)` writes the namespace in RFC network byte order — required by §4.3; the default little-endian mixed layout would silently produce non-interoperable ids. Correct.
- `input` sized `16 + Encoding.UTF8.GetByteCount(name)`; `GetBytes(name, input.AsSpan(16))` fills exactly the remainder. No over/under-allocation.
- `stackalloc byte[20]` matches SHA-1's digest size; `SHA1.HashData(ReadOnlySpan, Span)` throws if the destination were short, so the size is checked twice over.
- `hash[6] = (hash[6] & 0x0F) | 0x50` sets version 5 in the high nibble of octet 6; `hash[8] = (hash[8] & 0x3F) | 0x80` sets the `10x` RFC variant. Both operate on the big-endian layout, then `new Guid(hash[..16], bigEndian: true)` reads it back in the same convention — the round-trip is internally consistent.
- `Guid.ToString()` ("D" format) is lowercase hyphenated, matching the doc comment.
- All three APIs used are .NET 8-available (`TryWriteBytes` bigEndian overload and the bigEndian `Guid` ctor are new in .NET 8; `SHA1.HashData` since .NET 5). Static one-shot `HashData` is thread-safe and allocation-light — the right choice over an `IncrementalHash`/`SHA1` instance.
- SHA-1 here is not a security use — RFC 4122 v5 mandates it for name-based ids; collision resistance requirements are naming-level, not adversarial. No concern.

## Findings

### 1. Minor — `BuildBatchInstanceId` is a public identity-minting API with no argument validation

`BatchScopedStorage.cs:127`

**Problem:** `BeginPage` guards its inputs (`ThrowIfNull`, `pageNumber <= 0` throws), but the new public `BuildBatchInstanceId(string baseUrl, int pageNumber)` accepts anything. `baseUrl: null` interpolates to `"/batch_000001"` and silently mints a syntactically valid UUID; `pageNumber: -1` mints an id for `"{base}/batch_-00001"` — a name no `BeginPage` path can ever produce.

**Impact:** A future caller (backend tooling, a debug script, another flow computing an expected id) that passes a null/unset base or a raw vendor page index gets a plausible-looking id that will never match any announced batch — a silent dedup miss rather than a loud failure. Identity functions should fail loudly on garbage.

**Fix (local patch):** `ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);` and the same `pageNumber <= 0` guard `BeginPage` uses (or `ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageNumber)`).

### 2. Minor — wire-format change of `instanceBatchId` is a cross-service contract break; nothing in-repo pins the consumer side

`BatchScopedStorage.cs:94`, `README.md`

**Problem:** The metadata value changes shape from `batch_000003` to a UUID string. A repo-wide grep confirms no in-repo consumer parses the value — the only reader is the external backend the doc comment references ("the backing store has a unique index on it").

**Impact:** This is a deploy-coordination hazard, not a code defect: a collector fleet emitting UUIDs against a backend still expecting `batch_NNNNNN` (or vice versa during a rolling deploy) means every in-flight run's batch identity misses the index/upsert path for the skew window. Also note there is no versioning or shape marker on the value, so the backend cannot distinguish old-format from new-format events except by parsing.

**Fix:** No code change required if the backend tolerates both shapes during rollout (a UUID never starts with `batch_`, so discrimination is trivial). Worth one sentence in the README/PR stating the rollout ordering assumption. Flagging because the code itself carries no trace of the compatibility story.

### 3. Observation — "globally unique across runs" is inherited, not guaranteed by this code

`BatchScopedStorage.cs:49`

The v5 id is exactly as unique as its input name. Uniqueness across runs holds iff the base storage URL is run-unique (the test fixture's `.../tenant/setting/run` shape suggests it is). If two runs ever shared a storage URL, their page-N batches would collide on the backend's unique index — and the upsert would *merge* them, which is precisely the replay-stability behavior the design wants for resumes but wrong for genuinely distinct runs. This is the intentional tradeoff (deterministic replay convergence over collision immunity) and the doc comment states the mechanism honestly; recording the dependency here so it's explicit: **the id's uniqueness contract lives in whatever mints the storage URL, not in this file.**

### 4. Observation — collector-level tests are now tautological against the function under test; acceptable because the golden vector anchors the value

`InsightVmCloudBatchScopedStorageTests.cs:60,86`, `QualysBatchScopedStorageTests.cs:55`, `BatchScopedStorageTests.cs:49,148`

These assertions compare `Metadata[...]` to `BuildBatchInstanceId(...)` — the implementation to itself. In isolation that would verify nothing about the value. It is fine here because (a) the collector tests' job is wiring (the id reaches the metadata at the right lifecycle points), not the algorithm, and (b) `BuildBatchInstanceId_MatchesRfc4122V5GoldenVector` pins the actual bytes against an external implementation with a comment explaining what a failure means. The `BeginPage_SamePageAfterResume_AnnouncesTheSameBatchId` test earns its keep — it covers the one subtle path (scoped URL round-tripped as the base on resume) where the id could plausibly diverge. Test suite is well-shaped; no action.

### 5. Nit — `TryWriteBytes` result discarded

`BatchScopedStorage.cs:132`

The `bool` return and `bytesWritten` are both discarded. The buffer is provably ≥ 16 bytes today, but a `Try*` method whose failure mode is *silently leaving 16 zero bytes as the namespace* deserves at least a `Debug.Assert` on the return if the buffer math ever changes. Style-level only.

### Non-findings (checked, fine)

- **Allocation profile:** one `byte[]`, one interpolated `name` string, one result string per call — per published page, this is noise next to the NDJSON upload it accompanies. Stackalloc-ing the input buffer under a length threshold would be over-optimization for this frequency; the current shape is the right simplicity/perf tradeoff.
- **Doc accuracy:** the "Pinned against Python's uuid.uuid5" test comment is true (verified), the XML docs match the behavior (lowercase, hyphenated, base-derived, removed on `RestoreBase`), and the README edit correctly upgrades the "determinism is load-bearing" warning to cover both the storage keys and the id.
- **Frozen-namespace discipline:** the private `BatchIdNamespace` with a "frozen forever" comment plus a golden-vector test that fails on any drift is exactly the right enforcement mechanism — the invariant is guarded by a test, not just prose.
- **Thread safety:** all state is method-local or `static readonly`; `SHA1.HashData` is static and reentrant. No shared mutable state.
