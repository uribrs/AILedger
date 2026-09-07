# Execution Notes

## Slice 1 — asset spine staging (phase 1 only)

Branch `feat/tenableio-correlated-findings-staged`, base `1f7a2ba6`. Purely additive: `git status` shows one
new folder and two new test files, nothing modified. The live two-lane `CollectFindings` path is untouched.

### Created

Production — `Collectors/TenableIoCollector/Flows/Findings/Correlated/`:

| File | What it is |
|---|---|
| `TenableIoSpinePaths.cs` | Key schema (`_staging/spine/{assetId}.json`, `_staging/manifest.json`), parser-visibility guard + `AssertNotParserVisible()`, asset-id validation, id↔key round-trip |
| `TenableIoAssetSpineManifest.cs` | Manifest record: export uuid, staged count, skipped chunks, base date, completed timestamp |
| `TenableIoAssetSpine.cs` | The spine over `GuardedObjectStore`: keyed `StageAsync` / `TryReadAsync`, lazy `ListStagedAssetIdsAsync`, manifest read/write, best-effort `DeleteSpineAsync` |
| `TenableIoAssetSpoolPhase.cs` | `/assets/export` poll+stream loop staging every record, bounded write fan-out, manifest written last. Returns `TenableIoAssetSpoolResult` |
| `TenableIoAssetSpoolReader.cs` | Chunk → `(id, verbatim bytes)` entries; records without `id` skipped |
| `TenableIoExportPollHelpers.cs`, `TenableIoChunkRetry.cs` | Ported poll/backoff helpers |

Tests — `UnitTests/.../TenableIoCollector.Test/`: `InMemoryTenableIoStagingStore.cs` (real
`IAdapterObjectStore` + `IAdapterObjectPruner` double, separator-boundary listing, `ObjectNotFoundException`
on absence) and `TenableIoAssetSpineTests.cs`.

### Deliberately NOT ported

The prior branch's RAM-spool machinery does not exist in this tree and was never written: compressed spool,
`SpoolBudgetBytes`, overflow degrade, `overflowMarkers`, host-less chunk-1 records,
`TenableIoOverflowChannelPublisher`, and the `IAssetSpool` windowed-join seam. `grep -r "SpoolBudget\|overflowMarker\|IAssetSpool"`
over the collector returns nothing. No size budget and no degrade path exist to reintroduce.

### Design decisions taken during execution

- **No generation folder**, diverging from `FalconStagingPaths`. Falcon needs one because its staged keys are
  *positional* (`hosts_000001`) and page boundaries are unstable across attempts, so a shared folder lets a
  re-spool overwrite a key with *different* hosts — silent corruption. Our keys are the asset's own id, so an
  overwrite can only replace an asset with a fresher copy of itself. The hazard is unrepresentable, and a
  per-attempt folder would add a concept, a discovery path and a cleanup path for nothing. Reasoned in the
  `TenableIoSpinePaths` type remarks.
- **`GuardedObjectStore` directly, not `PriorStateStore`** — the latter round-trips through `TState` and would
  break verbatim `host` passthrough; its own guard says "per-key state is a watermark, not a copy of the entity"
  and caps at 1 MiB.
- **No tighter read ceiling** on a spine read: an asset record is data, not a control artifact, so the façade's
  `MaxInMemoryObjectBytes` (8 MiB) is the right guard rather than the 1 MiB control cap.
- **Write concurrency is the collector's** (`SpineWriteConcurrency = 32`, a const, not a config knob) because
  the façade bounds reads only. Serial writes would add tens of minutes at ~100K objects.
- **Fresh byte array per record** in the reader, never `NormalizedUtf8Json`'s reusable-buffer overload: writes
  are in flight while the enumerator advances, so a shared buffer would put the wrong bytes under the right key
  with no exception. Documented at the call site as a correctness constraint, not a style choice.
- **Manifest absence degrades to redo.** Infra's staging guidance says a manifest must never be a cursor;
  completion here cannot be derived from the objects alone (nothing says how many assets the export would
  yield), so the manifest records it and is written last. Missing → re-spool, which converges.

### Verification performed

- `dotnet build` collector project: **0 warnings, 0 errors**.
- `dotnet build` test project: **0 warnings, 0 errors**.
- `dotnet vstest --TestCaseFilter:"FullyQualifiedName~TenableIoAssetSpineTests"`:
  **Passed 28, Failed 0** (255 ms).
- Full TenableIo suite: **Passed 42, Failed 1, Total 43**.

### The one failing test is pre-existing on dev, proven not assumed

`TenableIoCollectorTests.ResumeAsync_Findings_WhenTransportResponseEndsPrematurely_ReturnsRetryableAdapterFailure`
fails with `Assert.Empty() Failure: Collection was not empty` / `CompletionRequest { Success = False }` at
`TenableIoCollectorTests.cs:696`.

Reproduced on a clean `origin/dev` worktree at the same commit: identical assertion, identical line, ~5m28s
versus ~5m21s on this branch. Not a regression from this slice.

**Probable cause (strong, not confirmed):** dev commit `06f26d3c` records that Infra 1.2.0-preview.0 removed
`RecoveryBudgetEvaluator.MaxBudgetedRecoveryAge` and the `backstop-recovery-age` reason, and updated three
DummyCollector sites to the "same setup, opposite expectation". The TenableIo assertion looks like a fourth
site that was not followed through.

**Both readings matter and the operator should pick:** either the test's expectation is stale, or TenableIo's
resume now publishes a *failed completion* where it should return a retryable failure — which on the recovery
plane is a false terminal DONE on an ordinary transport error. Out of this slice's scope; not touched.

### Residual risks / open items

- **A12a (`s3:ListBucket`)** is the sharpest deployment risk and is unresolved. Absence turns every ordinary
  spine miss into `ObjectStoreAccessDeniedException` instead of a thin-host miss, because the contract forbids
  reporting a permission failure as `null`. Misses are a hot path, so this fails runs rather than degrading them.
- **A12** (`S3AdapterObjectStore` registered wherever this collector runs) unresolved; a staged design hard-fails
  at flow entry without it, by design.
- Phase 1 is **not wired** into `TenableIoCollector`. Nothing runs it in production yet, by intent.
- The LocalAdapterRunner phase-1 proof run was **not executed** — it needs operator-supplied Tenable credentials
  and AWS access. It is the remaining evidence for "the spine landed": staged object count vs tenant asset count,
  manifest present, and nothing parser-visible under the run prefix.
- `InMemoryTenableIoStagingStore` near-duplicates Falcon's equivalent. Consolidation into
  `Collectors.Tests.Infrastructure` is worth doing at the third staging collector; doing it now would drag
  Falcon's test project into this diff.
- `CollectorVersion` untouched. Note the default in `Collectors/Directory.Build.props` is now **6.2.3**, so the
  prior branch's proposed `5.0.0` is behind the current default — a MAJOR bump for the output-contract change
  would be `7.0.0`. Operator's call.

---

## Repair round 1 — after verifier-1 and code-reviewer-1

Both passes ran against the same tree snapshot (launched concurrently, so neither saw the other's output).
They independently converged on the same top two defects.

### Fixed

| Finding | What was wrong | Fix |
|---|---|---|
| CR#1 / D1 — **blocker** | A `429`/`502`/`503`/`504` on a chunk download was not retried: the retry gate only asked the transport classifier, which inspects socket errors and message markers, never status codes. One rate-limit response permanently skipped a chunk — up to 1,000 assets — and the run still reported success. Confirmed by the reviewer's execution probe, not inferred. The tell was `ComputeDelay`'s live `AdapterHttpRequestFailedException` parameter with `Retry-After`/429 branches that the only call site passed `null` to: written, never wired | Dedicated retry branch on `IsTransientOrRateLimited`, with the exception passed through so `Retry-After` is honoured. Mirrors what the live assets flow already does |
| CR#2 / D2 | No ceiling on skipped chunks. An arbitrarily incomplete spine still got a manifest — and since the manifest is the completion proof, a later leg would *skip re-spooling* a mostly-empty spine. `SkippedChunkCount` was recorded and read by nobody | `MaxSkippedChunkRatio` (0.5, the same value both live flows use) enforced **before** the manifest is written |
| CR#3 | Mid-spool 404 re-created the export in a tight uncapped loop — up to 3h of POST+GET against a quota-limited API, generating the very rate limits that CR#1 turned into data loss | Capped at 2 re-creations with a delay before each; exhaustion throws |
| CR#4 / D3 | A base-URL spelling mismatch made the sweep listing yield **nothing**, silently — no exception, no log — dropping every findings-less asset and looking identical to a tenant where every asset had a finding | Parse the id from the spine prefix *within* the URL instead of stripping this spine's own base; count and warn on anything unrecognized. Mirrors Falcon's warn-once diagnostic |
| D4 | The contract's "missing object store is a hard failure" existed only potentially, at a call site that does not exist | `RequireObjectStore` / `RequireBaseStorageUrl` added and tested |
| CR#7 | `catch (OperationCanceledException)` could not tell caller cancellation from `Parallel.ForEachAsync`'s internal cancellation after a body throws | Filtered on `cancellationToken.IsCancellationRequested` |
| CR#8 | Poll-failure counter mutated and logged inside an exception filter; circuit-breaker rejections bypassed the consecutive-failure ceiling | Counting moved into the handler body; breaker failures now count like any other |
| CR#9 | Test double locked only the writer while the phase writes 32-way | Every member locks; listing takes a snapshot |
| CR#10 | `GuardedObjectStore` never disposed in tests, leaking its read semaphores | Test class owns and disposes them |
| CR#6 / coverage | Every failure path was untested, which is why CR#1 and CR#3 shipped | Added: 429-then-succeed, the 5xx family as a theory, ratio guard (driven **and** pure), 404 re-creation cap, export-reports-failure, 500-asset run that actually saturates the fan-out with per-id byte assertions, listing under a mismatched base, both hard-failure preconditions |
| Self-caught | `RunAsync` was 122 lines with six pieces of loop state threaded by hand | Split into short methods; state moved to a private `SpoolPass`; `RunAsync` now takes a `TenableIoAssetSpoolRequest` record |
| CR doc claims | Three comments overstated the code: "one addressed fetch" (it is stat + read on a 4-slot semaphore), the reader's reason for a fresh array (the pinned overload also copies — the real reason is not resting a correctness requirement on a package's implementation detail), and "this phase has one path" | All three restated accurately, including the memory-pressure caveat that a 1–8 MiB record throws rather than returning bytes |

One test of mine had to change rather than the code: it asserted that 1 skipped chunk of 2 was tolerated,
which is exactly the 50 % boundary the new ceiling refuses. The ceiling matches both live flows, so the
scenario moved to 1 of 4.

### Verification after repairs

- Collector + test project: **0 warnings, 0 errors**.
- `TenableIoAssetSpineTests`: **43 passed, 0 failed** (15 s), up from 28.
- Full TenableIo suite: **57 passed, 1 failed, 58 total** — the single failure is the pre-existing dev defect
  reproduced on a clean `origin/dev` worktree.

### Local runner

`LocalFileAdapterObjectStore` added: a filesystem `IAdapterObjectStore` + `IAdapterObjectPruner`, wired as the
fallback when no S3 store is registered. This removes a standing blocker — as committed the runner had **no**
object-store binding at all (the concrete S3 store lives in the ISB repo behind `HAS_ISB_AWS`, which no
committed build defines), so no staged collector, Falcon's included, could be driven locally without a sibling
checkout and a ProjectReference that must never be committed.

It exercises the real contract, so key schemas, manifest ordering, byte fidelity and object counts are provable
locally. It cannot stand in for IAM behaviour, request cost, throughput, or consistency. **It is currently
compiled but unexercised** — nothing calls the spine yet, so the first real exercise arrives with phase 2.

### Why the phase-1 proof run is not delivered

The runner drives collectors only through their public adapter surface — Falcon has no `InternalsVisibleTo` for
it, and its two-phase staging runs *inside* `ProcessAsync`. Driving phase 1 today would need either internals
exposed to the runner (breaking that boundary) or phase 1 wired into the live flow with phases 2–3 stubbed
(the second runtime path that was ruled out). So the end-to-end spine proof legitimately belongs with phase 2,
when `CollectFindings` has a real reason to call the spine. Recorded rather than worked around.

---

## Repair round 2 — the two pre-existing dev breakages, and one new defect the verifier caught

### The solution build was red on dev, and a whole test project was dark

`YamlAdapterTests.cs` had a comment block plus an entire second YAML constant pasted **inside** the first
raw string literal, truncating it and orphaning the second constant's body (`CS9000` at 492, everything after
cascading). Fixed by moving both out past the closing delimiter.

Consequence worth stating plainly: that project had not compiled, so **285 tests were silently not running**,
and no full-solution build was possible. Solution now builds 0 warnings / 0 errors; the revived suite is
healthy at **284 passed, 1 skipped, 0 failed**.

### The red resume test was a production defect, not a stale expectation — and my hypothesis was wrong

I had guessed the recovery-budget backstop rework (dev `06f26d3c`). **Disproved by measurement:** no policy
returns a deferred recovery on this path, so `RecoveryBudgetEvaluator` is never reached. `06f26d3c` and Infra
`86de702` are unrelated, and TenableIo is not a fourth site of that change.

The actual defect is more general and worth remembering:

> **Supplying a resilience strategy silently disables a collector's vendor flow classification.**
> `CollectorResumeDefinition.ClassifyFlowException` is consulted at exactly one site,
> `CollectorResumeClassifiedExecutor`, and `CollectorResumeRunner` selects `CollectorResumeStrategyExecutor`
> instead whenever `ResilienceStrategy` is non-null. A stub classifier in the strategy chain therefore does
> not fall back to the vendor mapping — it discards it.

Regression timeline: the test was introduced 2026-03-04 (`360d57b4`), tightened 2026-03-08 (`378d3941`), and
went red on 2026-06-01 (`da2543e6`), which wired `ResilienceFactory.Create()` into both TenableIo resume call
sites while the strategy's classifier still returned `null`. That commit's message — and the stub's own
comment — both asserted the opposite of what the code does.

Measured chain before the fix: vendor classifier says retryable → strategy stub returns null → no policy
matches → `UnknownFlowFailurePolicy` → `RethrowForUnknownRetry` → the unknown-flow Polly pipeline burns
30s+60s+120s (this was the 5-minute runtime) → outer catch → `ClassifyUnhandledException` sees a bare
`InvalidOperationException` → publishes a **non-retryable** error **and** a failed completion.

Why that matters beyond a red test: a failed DONE is terminal on the recovery plane, and the failure class it
mislabels is "too many chunks failed", exactly the case Tenable recovers from with a fresh export. It is also
not resume-only — `TenableIoCollector.cs:491` aliases the same stub for `ProcessAsync`. This is
`Documentation/03-current-concerns.md` C5, and the fix follows its stated direction.

Fix applied: the strategy-chain classifier now delegates to the real vendor classifier and maps its DTO onto
`FlowExceptionHandling`. `TenableIoResilienceStrategyFactory` deliberately untouched — with no
`retryableBackoff`, `MappedFailurePolicy` yields `PublishFailure(retryable)`, which publishes a retryable
error and **no** completion. A deferred recovery would be wrong here: a host-scheduled resume would re-poll
the same dead export uuid and fail identically.

Result: **the test passes in 26 s instead of 5 m 22 s** — the decision short-circuits the three unknown-flow
retries, which independently corroborates the diagnosis.

Two adjacent findings, both follow-ups rather than fixes now:
- **MicrosoftEntraId** has the identical orphaned-classifier shape, but both its mappings are
  `IsRetryable: false`, so dropping them degrades error *codes* only. No false-terminal risk.
- **DefenderVm**'s equivalent test is green but calls the resume runner **without** a strategy, so it
  exercises the classified executor rather than the path production takes. The trap is currently harmless
  there, but the test is not covering production.

### The skipped-chunk ceiling was evadable, and my own test encoded the evasion

Verifier pass 2 confirmed every repair-round-1 fix landed, and found one new defect: the ceiling counted only
chunks that **failed**, never chunks that were **never offered**. A `FINISHED` export declaring
`total_chunks: 4` while only ever listing chunk 1 produced a quarter-full spine with nothing failed, no
warning, and a manifest written over it — the same hazard the ceiling exists to close, reached by the other
road. My `SpoolPhase_WhenAChunkIsFailedServerSide` test asserted that shape as a pass.

Fixed by reconciling at end of pass: `unaccounted = max(0, totalChunks - (processed + excluded))`, and
`lost = excluded + unaccounted` measured against the ratio. The two kinds are logged **separately** because
the diagnosis differs — "we could not fetch this" versus "the vendor finished without ever offering it".
The offending test now offers what it declares, and a new test pins the never-offered case.

Spine tests: **48 passed, 0 failed**.

---

## Phases 2 + 3, wiring, and repair round 3

Run through the pipeline with four workers on partitioned file sets (W1 YamlAdapter syntax, W2 resume defect
diagnosis, W3 phases 2+3, W4 collector-level e2e contract), plus two verifier passes and two isolated code
reviews.

### What now exists

`CollectFindings` is the correlated flow. One run: stage the asset spine (phase 1) → correlate vulnerability
chunks against it by key, one atomic object per chunk (phase 2) → sweep the assets no chunk claimed (phase 3).
The two-lane assets-then-findings sequencing, `WriteFindingsPhaseTransitionCheckpoint`, and the combined-run
chaining in `ResumeAssetsAsync` are gone. Standalone `CollectAssets` is untouched.

### Defects found and fixed AFTER the code was written and green

Worth recording as a set, because every one of them was invisible to a passing test suite:

1. **429/5xx dropped a whole chunk** (phase 1, confirmed by reviewer execution). The retry gate asked only the
   transport classifier, which does not look at status codes. Fixed with a dedicated catch; `ComputeDelay`'s
   dead `ex` parameter — written but never wired — was the fingerprint.
2. **The skipped-chunk ceiling was absent**, so an arbitrarily incomplete spine still got a manifest, and the
   manifest is the completion proof, so a later leg would skip re-spooling it.
3. **The ceiling was then evadable**: it counted failed chunks but not chunks a finished export never offered.
   A `total_chunks: 4` export offering one produced a quarter-full spine with nothing failed. **My own test
   asserted that shape as a pass.** Both kinds now count and are logged separately.
4. **A base-URL spelling mismatch silently under-swept**: the listing yielded nothing, no exception, no log,
   every findings-less asset absent — indistinguishable from a tenant where every asset had a finding.
5. **The vendor flow classifier was disabled by its own resilience strategy.** `ClassifyFlowException` is read
   only by the classified resume executor, which the resume runner bypasses whenever a strategy is present.
   TenableIo regressed on 2026-06-01 (`da2543e6`); the commit message and the stub's comment both asserted the
   opposite of what the code does. Consequence: a retryable failure published as non-retryable **plus a failed
   completion** — terminal on the recovery plane, on `ProcessAsync` as well as resume.
6. **Then the same defect came back through a rewording.** Phase 2's new ceiling message no longer matched the
   classifier's `"too many chunks failed"` substring, so it fell through to the unknown-flow policy again.
   Fixed at the root: `TenableIoExportIncompleteException`, matched by **type**, carrying failed /
   never-offered / expected counts. Substring rules kept as a net for unconverted sites.
7. **The correlated resume format gate was unreachable.** `TenableIoCorrelatedCheckpoint.CanResumeFrom`/
   `TryLoad` were referenced only from tests; the collector still used the passthrough gate, so a two-lane
   checkpoint was accepted and resumed into the correlated flow — the mixture the version key exists to
   prevent. Wired at both points, including the runner's `tryLoadState` seam.

The through-line: **five of the seven were failures of classification or accounting that no test noticed
because the run still ended.** That is the failure mode this collector's recovery plane is most exposed to,
and it is why the classifier now has its own direct unit tests keyed on type rather than on message text.

### Verification

- Full solution: **0 warnings, 0 errors** (was 81 errors at baseRef — the YamlAdapter test project had not
  compiled, so 285 tests were silently not running; that suite is now green at 284 passed / 1 skipped).
- TenableIo suite: **102 passed, 0 failed, 17 s.** Baseline after wiring was 89 tests / 3 failed / 10 m 30 s;
  the three failures were each exactly 3 m 30 s of unknown-flow retry backoff.
- Method length: nothing in the new code over 45 lines.

### Local testing

`LocalFileAdapterObjectStore` gives the runner a filesystem `IAdapterObjectStore`/`IAdapterObjectPruner`,
wired as the fallback when `storageUrl` is not `s3://`. Before this the runner had no object-store binding at
all, so no staged collector — Falcon included — could be driven locally without a sibling ISB checkout and an
uncommittable `ProjectReference`. Mechanics are provable locally; request cost, throughput at a real 32-way
fan-out, and S3 consistency are not.

### Left deliberately

- The passthrough `TenableIoFindingsFlow` / `ChunkProcessor` / `Stats` are now **dead code** (nothing but a
  stale doc comment references them). Deletion is the single-path choice; left pending the operator's call
  because Fork C — parser support for the correlated shape — is unresolved and the passthrough flow is what
  would be wanted back if the parser is not ready.
- `CollectorVersion` unset. Operator has accepted MAJOR relative to the `6.2.3` default, i.e. `7.0.0`, and
  will make the bump in `Directory.Build.props`.

---

## Code review 2 (phases 2+3) — findings and dispatch

Full report: `review/code-reviewer-2.md`. Two blockers, both confirmed by the reviewer running probe tests
rather than reasoning from source. Probes were reverted; the tree was left as found.

### The blocker that matters most: a lost chunk becomes a positive false claim

An abandoned vuln chunk never claims its assets, so phase 3 sweeps them as host-bearing envelopes with
`findingsInChunk: 0` and `isLastChunk: true`. That is not an absent asset — it is an assertion that the asset
has **no vulnerabilities**. Real findings read as resolved downstream, and the 50% skipped-chunk ceiling
tolerates it silently. Probe: 4 chunks, one HTTP 500 on chunk 2, run reports success, that chunk's asset
published as clean with its two real findings gone.

**Why the original reasoning was backwards.** W3 chose this deliberately — a failed chunk does not claim its
assets, so "those assets get swept with a hydrated host instead of vanishing". The asymmetry it missed:
under-reporting an asset is corrected by the next run; publishing it as clean overwrites truth with a
falsehood, and nothing downstream can tell the difference. Silence is the safe degradation; a clean bill is
not. The sweep may now only assert "no findings" when phase 2 accounted for every chunk.

### The second blocker: resume collides with its own earlier output

Claims are in-RAM and start empty on resume while `processedChunkIds` is seeded, so the sweep re-emits every
asset a previous leg already published as a SECOND chunk 0 — `isLastChunk: true`, zero findings — colliding at
the same (uuid, chunk) key with the leg that carried the real findings.

This had been recorded as accepted behaviour ("duplicate empty envelopes on resume, absorbed by per-uuid
aggregation"). It is not the same thing: a duplicate for an unclaimed asset is harmless, whereas a collision
with a real chunk 0 only survives if the parser's aggregation rule happens to favour us — a rule this repo
cannot see. Shipping on that would be resting correctness on someone else's unexamined tie-break. Fixed with
durable per-chunk claim markers, written before the data object so a crash leaves a claim without data (safe:
skipped then re-emitted) rather than data without a claim (unsafe: the sweep contradicts it).

### The rest, dispatched in parallel

- **`num_assets` is still 500 in configuration** while every line of the new code, its comments and its tests
  assume 50 — the measured basis for "10–43 MB per chunk". At 500 that is ~10x, and the flow holds a chunk
  twice (buckets plus a materialized envelope list): ~200–860 MB per chunk. Default corrected and the double
  buffering removed.
- **The unknown-flow retry loop overwrites its own output.** An unclassified mid-flow failure is rethrown into
  a Polly loop that re-runs the whole flow up to 3x in one invocation with a fresh instance, so batch numbering
  restarts at 1 and overwrites the abandoned attempt's folders — leaving higher-numbered orphans already
  announced upstream.
- **Every checkpoint under-reports by exactly the batch it covers** (checkpoint built before the stats fold),
  so each resume permanently drops a batch from the totals and poisons parity checks.
- **A 500 or 408 on a chunk download is abandoned after one attempt** while a 502 gets five — the predicate
  covers 429 and some 5xx, and the transport classifier ignores status codes entirely.
- **A spine read that throws discards the whole chunk**, including the memory-pressure case where the façade
  tightens its buffered-read ceiling to 1 MiB and a legitimate large record throws. Retrying cannot help, which
  contradicts the flow's own rule that a finding is never lost because its host could not be fetched. Now a
  miss; genuine store faults still fail the chunk.

### Pattern across the whole task, worth keeping

Of everything found in this work, nearly all were **accounting or classification errors that left the run
reporting success**: a retry predicate that did not cover a status code, a ceiling that counted one kind of
loss, a classifier keyed on a message that got reworded, a checkpoint folded one batch early, a claim set that
did not survive a leg, and a sweep that turned absence into a positive assertion. None of them made anything
go red. Tests were written and passing throughout. The defects were found by execution probes and by reading
the accounting, never by the suite.

## Repair round 3 — review findings closed

All seven reviewer findings are now closed. Two blockers, four majors, one minor, plus the two queued patches
applied by hand and verified.

**Blocker 1 (false clean bill) — closed and verified by reading the guard, not by trusting the plumbing.**
`TenableIoVulnAccounting.IsComplete` requires `FailedChunks == 0 && UnaccountedChunks == 0 &&
ChunksWithUnreadableClaims == 0`; the sweep returns early and logs when it is false. The third term was not in
my brief — a claim marker that cannot be read means the claims for that chunk are *unknown*, and unknown must
not license "this asset has no findings" any more than a failed chunk does.

**Blocker 2 (resume collision) — closed with a durable claim ledger.** One marker per published chunk under a
sibling `_staging/claims/` folder (line-delimited ids; ids cannot contain a newline because the id validator
rejects control characters), written BEFORE the chunk's data object. The ordering is the point: a crash between
them leaves a claim with no data, which is safe (the asset is skipped by the sweep and re-emitted when the
chunk re-runs), rather than data with no claim, which is not (the sweep would contradict published findings).
Markers sit outside the spine folder so the sweep's listing never has to filter them out.

**Where the reviewer was wrong, and it mattered.** Finding 7 asked for `DataPipelineException` to be treated as
a miss while "genuine store faults still fail the chunk". That is not implementable as specified: the type
carries no reason code and `GuardedObjectStore` raises it for three different conditions on this path — the
size refusal, a read-slot timeout, and a null stream. A blanket catch would have converted the systemic case
(a slot timeout, whose likelihood *rises* under exactly the memory pressure that tightens the ceiling) into a
chunk of falsely thin hosts. That is the same defect class the finding was written to prevent, introduced by
its own fix. The distinction the type permits is by volume, so a refusal is a miss only while refusals stay a
minority of the chunk; past that the chunk fails. Accepted risk: up to half a chunk can become thin hosts
before the guard trips, each one individually logged.

**Also closed:** `num_assets` 500 → 50 with builder bounds to match (the flow was sized against 50 and held a
chunk twice, so 500 projected to ~200–860 MB per chunk); envelopes now built lazily, removing the second copy;
500 and 408 added to the transient-retry predicate — the session's Polly pipeline had been retrying all six
codes all along, so only the collector-side predicate disagreed, and the codes it omitted were exactly the ones
where the chunk loop gave up after Polly exhausted; the checkpoint's stats fold moved above the checkpoint
build, so a checkpoint no longer under-reports by exactly the batch it covers.

**W5 never sent a report.** Its code is in, green, and I verified its two load-bearing properties by reading the
source rather than taking them on trust: the claim write precedes the publish, and the sweep gate suppresses
rather than merely receives the accounting. W6's negative-control method is worth copying — it reverted each
fix individually and confirmed exactly the intended tests failed, which is stronger evidence than green.

### Final state

- Solution: **0 warnings, 0 errors** on a clean `--no-incremental` rebuild.
- TenableIo suite: **135 passed, 0 failed, 18 s.**
- YamlAdapter suite: **284 passed, 1 skipped.**
- Longest method in the new code: 49 lines.

### W5's report (arrived after its code was verified) — two corrections to MY brief

Worth recording because both were errors in the instruction, not the implementation:

1. I told W5 to seed the processed-chunk set from the progress context for the in-invocation retry fix. **That
   alone would have been a data-loss bug.** With `resumeState == null` the flow creates a NEW vulns export, and
   chunk ids are export-relative — so a seeded processed set would skip ids belonging to a different export and
   drop their findings silently. The fix must recover the whole position, export uuids included, so attempt 2 is
   a true resume of attempt 1's export. Asserted by `VulnExportCreates == 1`.
2. I suggested `progressContext.CurrentPage` as the source. It is 1-based and starts at 1, so it cannot
   distinguish "nothing published" from "batch 1 published". The persisted `lastPublishedPage` is the exact
   value.

W5 also deviated from the brief deliberately and correctly: a claim marker is written for **every** processed
chunk including empty ones. Without that, the ~0.2% phantom chunks (allocated, never populated) reach the
processed set via a later chunk's publish while having no marker of their own — so "processed but unmarked"
would be an ordinary case and could not be used as the anomaly signal that suppresses the sweep.

It left one latent-but-unreachable case unguarded on purpose — a correlated checkpoint carrying an empty
`ExportUuid` — reasoning that a branch for an unrepresentable state is what the "no fallback paths" ruling
argues against. Correct application of the constraint; flagged for the operator rather than taken silently.

Both workers verified by **mutation**: reverting each fix individually and confirming exactly the intended tests
failed. That is materially stronger evidence than a green suite, and is the practice to keep from this task.

---

## First real-tenant run against real S3 — 2026-08-17, run 20260817-153214

Correlation `b431623c-fcde-448d-b5d1-4fa845873d75`, storage
`s3://cybi-data/Uri-Tests/tenableio-two-phase/first-001`. Driven through LocalAdapterRunner with the
ISB AWS reference restored locally (`HAS_ISB_AWS`), so this exercised the production S3 store, not the
filesystem stand-in.

**Observed, from `aws s3 ls` rather than from the log:**

- Spine objects landing as `_staging/spine/{uuid}.json` — key layout correct on real storage.
- 11,646 objects a couple of minutes in; ~1000 assets per 6 s chunk, ~167 PUTs/sec at the 32-way write
  fan-out.
- Object sizes 2.9–4.8 KB. This independently reproduces the prior task's measured ~4 KB/asset (avg
  4,025 B, p50 2,952 B) — arrived at without that figure being fed to the run, so **A2's spine sizing is
  now confirmed on live data** rather than carried as stale evidence.
- Both exports created up front (`POST /vulns/export` 15:32:16, `POST /assets/export` 15:32:17) —
  the concurrent-generation design holds in practice.
- Zero retries, zero warnings through phase 1 (`attempts: 1` on every chunk fetch).

**The ordering invariant was visible on real storage:** while the spine was still filling, the prefix
contained `spine/` and nothing else — no `manifest.json`, no `claims/`. That is the property the whole
recovery model rests on. A crash at that moment leaves no completion marker, so the next run re-spools
instead of trusting a partial spine.

**What this run does NOT yet evidence:** phases 2 and 3 had not started at the point of observation, so
correlation, the claim markers, the sweep, and the marker-before-data ordering on real storage remain
unobserved outside unit tests. The `num_assets` 500→50 change also has its wall-clock cost still
unmeasured — ten times the chunks, each with a poll, a download, ~50 spine GETs at 4-way concurrency and
one publish.

**One behaviour worth remembering:** the assets export reported `total_chunks` as 0 throughout early
streaming (log shows `staged chunk 7/0`). That is correct — the export is still PROCESSING — but it means
the never-offered-chunk reconciliation is inert until the vendor declares a total, so that guard only has
teeth once the export finishes. Not a defect; a property to know.

---

## Parser validation — the correlated envelope, proven end to end (2026-08-17)

Repo `~/Dev/cymulate-integration-parsers`, branch `feature/tenableio-correlated-parser`, commit
`1e8a880` ("Tenable.io parser: correlated (collector v5+) input generation"). Written 2026-07-08 against
the PRIOR branch's grammar — which is why keeping our grammar byte-identical to it mattered.

**VERDICT: the parser branch handles our real output. No parser change needed.**

Evidence, from a real batch of run 20260817-153214 (`batch_000001/findings_000001.json`, 6 envelopes,
284 findings) run through the actual parser in the repo's own Docker test image:

- The grammar sniff (`is_correlated_envelope`: columns `{host, findings, isLastChunk}` + `host` a struct)
  recognises real output. Log: `resolved mode=hydrated (correlated envelope)`.
- **Exact parity with the passthrough path.** The same batch, split into the two old lanes
  (`host` → assets, `flatten(findings)` → findings) and run in `split` mode, produced *identical*
  output: 6 assets, 495 findings, and the same per-host distribution down to each host. The commit's
  claim of "parity by construction" is now measured, not asserted.
- Zero orphan findings — every finding correlated to an asset.
- The `batch_NNNNNN/` layout is compatible: in per-batch mode ICM sets `ZIP_FILE_KEY` to the batch
  prefix, so the resolver's flat `findings*.json` pattern resolves inside it.

**Why 284 findings became 495 rows, resolved to source.** Not a defect and not severity-related. The
base parser drops any `type == "vulnerability"` finding whose `cve_ids` is empty, then explodes the
survivors one row per CVE (`libs/packages/parsers/common/base_parser.py:285-306`). Every number follows:
an AWS host with 48 findings and zero CVEs → 0 rows; four identical `*.isp.sky.com` routers with 2
CVE-bearing findings each → 6 rows each; a server with 72 CVE-bearing findings → 471 rows.

**Operator ruling (2026-08-17):** the collector deliberately ships every severity and filters nothing,
even though the parser keeps only CVE-bearing findings — roughly 75 % of the payload in that batch. This
is BY DESIGN: the raw feed is an archive, and "no one knows what will be needed tomorrow". Do not raise
it again as waste, and do not add a collector-side severity or CVE filter to "optimize" it.

**Incidental fix, left in the parsers repo, uncommitted:** added `.dockerignore`. Without it
`Dockerfile.test`'s `COPY . .` shipped the developer's macOS `.venv` into the image and clobbered the one
`uv sync` had built, so `uv run` rebuilt it and the test dependencies vanished — the test image could not
run the suite at all. Also note the tests need `uv run --extra test`, not bare `uv run`.

**Version:** operator set the bump at **6.3.0**, not the 7.0.0 I proposed. His call; it goes in
`Collectors/Directory.Build.props`.

## Run 20260817-153214 progress at 16:33

Phase 1 complete (29,515 spine objects, manifest `stagedAssetCount: 29515`, `skippedChunkCount: 0`).
Phase 2 in flight: 1,045 published objects / 6.81 GB, on vuln chunk ~1047, **zero warnings and zero
errors** in the whole log. Steady 3.3 s per chunk from the first batch to the thousandth — keyed spine
lookups do not degrade as the spine grows, which was the main scaling worry.

Still unobserved: the sweep (phase 3), and whether the vulns export ever declares `total_chunks` — while
it reports 0, the never-offered-chunk guard has nothing to reconcile against and only real failures count.

---

## Live run 20260817-153214 — terminated on a hang. Verdict: NOT STG-ready.

Killed 18:12 by operator instruction after a 42-minute stall. SIGINT unwound cleanly
(`Success=false Message=Operation cancelled`, sessions disposed).

### Final state
- 2079 batches published; 33,674 objects / 14.7 GB in S3; last write batch_002079 at 17:30:18.
- Vulns export DOES declare `total_chunks` = 2110 (settles the open question; assets export reports 0).
- 1 warning in the whole run: chunk 1999 failed **server-side** at Tenable, reported via the status
  response's failed list — never requested by us.
- `checkpoint.json` (17:30, 10,239 bytes) is retained at the stall point: a real resume fixture.

### The hang — root cause
Requested `/chunks/1501` 17:30:17, got 200 headers 17:30:18, then nothing for 42 min.

Evidence:
- `dotnet-stack`: **no thread in collector code** — no socket-read frame, no semaphore wait of ours;
  3 pool workers parked normally. A pending async await, NOT a deadlock. This positively rules out
  read-slot exhaustion (`MaxConcurrentReads=4`), which was the competing hypothesis.
- `Recv-Q` = 0 on the one socket: nothing arrived unread. Server owed us bytes.
- Socket stayed `ESTABLISHED` 42 min, no FIN, no RST. Silently dead connection.
- `Session.StreamResponse` span **closed Success at 515ms** — retry, timeout, circuit breaker and
  rate limiter had all unwound before body consumption began.
- All 2117 requests used `timeout.threshold_ms=300000`. No short-timeout variant exists.

**Mechanism (high confidence):** the 300s timeout covers *acquiring* the response, not *reading* the
body. Body consumption runs outside every policy with no deadline. Server stops sending -> awaits forever.
**Upstream culprit (unknown):** Tenable vs Cloudflare vs a NAT hop — indistinguishable without a capture.
Contributing factor: one session / one connection reused for 2,100+ requests over 5 hours.

**Cancellation works.** SIGINT broke the stuck read, so the body read honors its token — it lacks a
deadline, not cancellability. A `CancellationTokenSource(timeout)` wrapper is therefore sufficient.

### Why the existing retry ladder cannot help
`TenableIoChunkRetry` / `IsRetryableStreamFailure` / the 429+5xx gate all require an exception. A hang
raises none. The blocker-1 fix defended against errors, not against silence.

### K8s consequence — reverses the earlier "trust it to STG" read
A hang yields no error, no `PartialResult`, no checkpoint advance. The pod sits. ISB's 30s force-reclaim
vs 5-min heartbeat then steals the claim and starts a duplicate run (the known zombie double-execution
issue), while the hung pod may later publish a false result. Lands on our weakest known ISB behaviour.

### Corrections to earlier readings in this file
1. **The late chunks were NOT retries.** Chunks 999/1357/1500/1501 were each requested exactly once,
   all at 17:30, never earlier. `FindNewChunkIds` diffs `chunks_available` against processed/excluded
   and **sorts** the result, so ids ascend *within* a poll — which is why the run looked monotonic.
   Tenable builds chunks in parallel across partitions; those low ids completed last. My earlier
   "drain pass over previously-deferred chunks" was wrong.
2. **`chunk N/2110` is a chunk id, not a count.** It reads like progress and is not. It produced a
   wrong-by-reasoning ETA (right by luck, since ids happened to ascend) and hid the stragglers.
   `pass.Processed.Count` is real progress. Observability defect — will mislead on STG exactly as here.
3. **A clean sweep was never observable in this run.** Chunk 1999's server-side failure makes
   `FailedChunks=1`, so `IsComplete` is false and phase 3 suppresses itself by design (blocker-1 gate
   working). Observing the happy-path sweep needs a run where Tenable fails nothing.

### Open before STG
- Read deadline on chunk body consumption -> turns silence into a classified retryable exception that the
  existing ladder already handles. Proper home is Http.Package; bounded version is collector-side.
- Whether other streaming collectors (Falcon, Qualys, DefenderVm, Cortex) share the exposure. NOT verified.
- Fix the id-vs-count progress log.
- Kill-and-resume test, now backed by a real checkpoint at 17:30.
- Still pending from before: version 6.3.0 in Directory.Build.props (operator's call); revert the
  LOCAL-ONLY `HAS_ISB_AWS` block in the runner csproj before commit; dead-code decision on
  `TenableIoFindingsFlow`/`ChunkProcessor`/`Stats` and the `Legacy*` checkpoint keys.

---

## Success/emission contract restored to pre-change parity (operator ruling, 2026-08-18)

### Baseline comparison the operator asked for — what dev actually did
`Flows/Findings/TenableIoFindingsFlow.cs` + `Flows/Assets/TenableIoAssetsFlow.cs` on dev:
- Chunk fails -> `excludedChunkIds.Add(id)`, warning logged, **loop continues**.
- Only `permanentFailures >= totalExpected * 0.5` throws. Transport failures are subtracted from the count
  before the comparison. Below the ratio the run **reports success**.
- No `unaccounted` concept: only offered-and-failed chunks counted; never-offered chunks were invisible.
- Assets came from a **separate passthrough lane** that emitted every asset unconditionally. So a vuln-less
  asset was always emitted, never gated on findings-side health.

=> The blocker-1 sweep suppression was a **unilateral tightening** introduced by this work, stricter than dev,
and it is why a complete run stopped signalling success. The exposure it defended against was not new either:
pre-change, a failed findings chunk left its assets emitted by the assets lane with no findings attached, which
reads downstream the same as zero-vuln. Same risk, shipped for as long as this collector has existed.

### Operator's three rules, implemented
1. Unobtainable chunk -> skipped and logged, work continues. (Unchanged; already the behaviour.)
2. Asset with no vulns -> emitted as is, not a failure. (**Changed** — suppression removed.)
3. All chunks of both exports processed -> success. (Unchanged; the >=50% ceiling stays as the wholesale guard.)

### Changes
- `TenableIoZeroVulnSweep`: gate is now `ChunksWithUnreadableClaims > 0` instead of `!Accounting.IsComplete`.
  A failed or never-offered chunk no longer stands the sweep down.
- `TenableIoVulnAccounting` **deleted** (0 refs remain). `TenableIoZeroVulnSweepRequest.Accounting` ->
  `int ChunksWithUnreadableClaims`. `LogSuppressed` rewritten for the one remaining case.
- Class doc rewritten: the "refuses to guess" doctrine replaced with the actual contract.
- `TenableIoCorrelatedFindingsFlow:151` call site updated.
- Test `Flow_WhenAVulnChunkIsLost_LeavesItsAssetsOutRatherThanPublishingThemAsClean` ->
  `Flow_WhenAVulnChunkIsLost_StillSweepsEveryUnclaimedAsset_AndTheRunSucceeds`; now asserts the lost chunk's
  asset IS emitted with zero findings, `IsLastChunk`, host present, and that only the 3 real findings landed.
- `Documentation/01-collection-strategy.md`: new "What counts as success, and what gets emitted" section.
- `ai/skills/collector-flow-patterns/SKILL.md`: the over-reaching "gate any such sweep" rule replaced with
  (a) do not tighten a success/emission contract as a side effect of restructuring, and (b) separate "vendor lost
  a unit" from "we lost our own bookkeeping".

### One case kept, deliberately, and flagged
An **unreadable claim marker** still suppresses the sweep. It is not a statement about vendor completeness: the
chunk WAS published, so its assets already carry findings in this run's output, and sweeping them emits the same
asset twice in one collection — once with findings, once asserting it has none. That is a contradiction inside a
single run rather than a gap the next run fills. Operator's rules did not cover this case.

### Verification
Solution: 0 warnings / 0 errors. TenableIo suite: **135 passed / 0 failed** (18s).

### Resume viability for run 20260817-153214 — confirmed live 2026-08-18 09:30
`GET /vulns/export/1721e46e-.../status` -> HTTP 200, `status: FINISHED`, `total_chunks: 2110`,
`chunks_available: 2109`, `chunks_failed: [1999]`. Export still alive ~18h after creation.
Spine 29,515 objects + 2,079 claims + manifest all intact in S3; checkpoint holds all 2,079 processed ids and
both export uuids (`findingsFormatVersion=2`). So resume costs ~30 chunks, not two hours.
**With the change above, a resume of this export now DOES exercise phase 3** — chunk 1999 stays failed, but a
failed chunk no longer suppresses the sweep. This was not true before today's change.

---

## Run 20260818-101401 — phases 1-2-3 all observed live. Success=true.

Base date `2026-08-18T04:00:00Z` (3h window), storage `s3://cybi-data/Uri-Tests/tenableio-two-phase/first-002`.
10:14:01 -> 12:00:20 (~1h46m). Assets export `b57dbc22`, vulns export `0a8f580a`.

### Final outcome
`Success=true Flow=CollectFindings Total=1279313 TotalAssetsCollected=12068 Failure=null`
2,112 objects / 8.69 GB. **0 errors.** 2 warnings, both expected (see below).

### Phase 3 ran — first live observation
```
sweep: 11959 asset(s) staged, 12003 claimed by vulnerability chunks, 65 to sweep as having no findings.
sweep: 65 zero-vulnerability asset(s) published as batch 2112.
```
Envelope shape verified by downloading `batch_002112/findings_002112.json` (203,583 bytes):
65 lines, 65 unique uuids, `findings` empty in all, `chunk`=0, `findingsInChunk`=0, `isLastChunk`=true,
`host` a populated object in all 65 (full Tenable asset field set incl. `acr_score`, aws_* fields).

### The run hit the exact condition today's change was about
```
[WRN] 2/2113 chunk(s) missing from the output — 0 failed and 2 were never offered by a finished export.
      Their findings are absent.
Chunks=2111/2113 Skipped=0
```
The export declared 2113, reported FINISHED, and only ever offered 2111. That is the "unaccounted" road,
and under **yesterday's** code `UnaccountedChunks=2` -> `IsComplete=false` -> sweep suppressed -> the 65
zero-vuln assets silently dropped **while still reporting success**. With the operator's rules in place the
chunks were logged, the run continued, the 65 assets were emitted, and the outcome is success. The first
fresh run reproduced the defect condition, so this was not a hypothetical.

### Arithmetic reconciles exactly (correctness proof for the claim/sweep split)
- 11,959 staged in the spine
- 12,003 claimed by vuln chunks, of which **109 are thin-host misses** (in the vulns export, absent from the
  assets export)
- 12,003 - 109 = 11,894 staged assets claimed
- 11,959 - 11,894 = **65 unclaimed -> swept**, matching the sweep's own count
- emitted total 12,003 + 65 = **12,068**, matching `TotalAssetsCollected=12068`

### Spine GC confirmed
`_staging/` is **0 objects** after success — `DeleteSpineAsync` ran. Only the 2,112 `batch_*` objects remain,
so the parser sees nothing but data and the stale-spine hazard cannot outlive a successful run.

### Two observability nits found while reading this run
1. `12003 claimed` vs `11959 staged` reads like a contradiction; it is only reconcilable by subtracting the
   109 thin-host misses. The line should separate "claimed and staged" from "claimed but not staged".
2. Fixed this session: the spine-reuse log said "from **this run's** completed spool" when the spool may
   belong to an earlier attempt at the same storage location. It now names the base date and states that the
   assets are as fresh as that spool, not as fresh as now. This message directly caused a misread earlier today.

### Spine reuse is by design, not a bug (investigated after operator challenge)
`TenableIoCorrelatedFindingsFlow:245-254` reuses a staged spine only when `manifest.BaseDateUtc == baseDateUtc`.
Two aborted attempts at 05:00Z matched and correctly skipped the spool; relaunching at 04:00Z took the
re-spool path with a warning, as designed. A wider window is also a superset of the narrower one, so the
re-spool overwrote every stale object — no phantom sweep entries, confirmed by staged=11,959 matching the
spool total.

### Vendor facts learned
- `total_chunks` is driven by the tenant's **asset count, not the time window**: 2110 for a ~1-day base date,
  2113 for a 2-hour one, 2113 for 3 hours. A narrow window makes chunks **sparse, not fewer** — so run length
  is roughly constant per tenant and cannot be shortened by narrowing the window.
- A finished export can declare more chunks than it ever offers (2113 declared / 2111 offered here).
- `--timeout-minutes` on the local runner is a hard `CancellationTokenSource` over the whole run; 60 was too
  low and would have cancelled just before the sweep.

---

## Asset-count probe, 2026-08-18 — nothing is missing; the base date is the only control

Client states ~100K assets; run 20260818-101401 staged 11,959. Probed the tenant directly (read-only, via
`/assets/export` chunk counts at `chunk_size=5000`, chunks counted from status without downloading).

| scope | chunks | assets (upper bound) |
|---|---|---|
| `last_assessed` >= 3h  (the run) | — | **11,959** actual |
| `last_assessed` >= 1 day (2026-08-17 run) | — | **29,515** actual |
| `last_assessed` >= 30 days | 21 | <= 105,000 |
| `last_assessed` >= 90 days | 22 | <= 110,000 |
| **no filter** | 123 | <= **615,000** |
| `GET /workbenches/assets?limit=1` -> `total` | — | **610,872** |

**Conclusion: the actively-assessed population is ~105K, which is the client's ~100K.** The 611K is Tenable's
historical record store — assets not assessed in 90+ days (stale/decommissioned). 30 days (105K) and 90 days
(110K) are nearly identical, so the curve flattens: essentially everything real is reassessed within a month.

### This also explains why total_chunks is constant regardless of window
`/vulns/export` partitions by `num_assets=50` over the **active asset population**: 105,650 / 50 = **2113
chunks**, exactly the number observed for a 2h, 3h, and ~1-day window. The `since` filter changes only each
chunk's *content*, not the partitioning — which is why per-chunk logs showed ~5.7 assets each rather than 50,
and why a narrow window makes chunks **sparse, not fewer**. Run length is therefore fixed per tenant by its
active asset count and cannot be shortened by narrowing the window.

### Production implication for onboarding (not a defect)
Coverage is controlled entirely by the base date. A **baseline/first** run needs a base date >= ~30 days back to
reach the ~105K active inventory; a 24-hour initial watermark collects only ~30K and the remainder arrives only
as assets are reassessed. Note also that wider is NOT strictly better — an unfiltered run would pull ~611K
records including long-dead assets. The useful band is ~30-90 days.

### Sweep log nit fixed (operator request)
`TenableIoZeroVulnSweep.CollectUnclaimedAsync` now reports staged / staged-and-claimed / to-sweep, plus
claimed-but-never-staged separately, with a comment explaining that the claim set is keyed by what the VULNS
export referenced and so can legitimately exceed the spine (those are thin hosts). Previously one "claimed"
total read as a contradiction against "staged" and was only reconcilable by subtracting the 109 misses.

---

## ISB recovery-path audit, 2026-08-18 — five assumptions checked against source

Read `/Users/user/Dev/IntegrationServiceBus` @ branch `Client-version-for-release`, HEAD `190a529c`.
**Caveat: that is a release branch, not proof of what STG runs.** Confirm the deployed build separately.

### A. Checkpoint state size — HOLDS
`adapter_state_json` is **`jsonb`, no length cap** (`CheckpointDbContextModelSnapshot.cs`). Our
`processedTenableChunkIds` is ~2,100 comma-joined ints (~10 KB) plus both export uuids — nowhere near a limit.
Nothing truncates it, so the resume cannot silently lose ids. (`CursorTokenToText` migration shows a prior
varchar cap existed elsewhere; the state column is not affected.)

### B. Retryable failure -> fresh collection, no loop — HOLDS, by a stronger mechanism than assumed
The fear: export expires -> we classify retryable -> ISB re-delivers the SAME checkpoint -> `CanResumeFrom`
passes (it validates format, not export liveness) -> resume onto the dead uuid -> 404 -> loop forever.

It cannot happen. `ProcessEventCommandHandler.CompleteExecutionAsync` calls
`FlushAndCleanupCheckpointAsync`, which calls `checkpointRepository.DeleteAsync(...)` **unconditionally for
any terminal result**. The `if (result.Success)` below it only chooses which log line to write. So a retryable
failure deletes the row, and the next scheduled run starts fresh and creates a new export. Our
`TenableIoExportExpiredException` -> retryable mapping does exactly what its doc comment claims.

**Flip side, worth stating:** a retryable failure discards the ENTIRE position. A Tenable failure at chunk
2000/2113 re-collects from scratch (~70 min, full re-publish). Correct but expensive — and it means
`PartialResult`/ScheduledWait is the only vehicle that preserves position. Falcon has the same property, so
this is not a Tenable regression.

### C. storageUrl on a fresh run — NOT ANSWERABLE IN ISB
ISB does not mint `storageUrl`; it arrives on the run message and `S3StorageKeyResolver.ToKey` only strips the
scheme/bucket and applies the env prefix. Whether a fresh collection gets a new prefix is decided upstream.
Bounded either way: the spine is deleted on success, so a leftover spine implies a prior failed run, and a
failed run does not advance the watermark — so the base-date guard reuses that spine for *the same window*,
which is the desirable outcome rather than a stale one. Not a blocker; confirm upstream when convenient.

### D. ScheduledWait is honoured and cannot be reaped — HOLDS
`CheckpointRepository.DeleteExpiredAsync` filters `c.Status != CheckpointStatus.ScheduledWait` (line 447), so a
parked wait is never TTL-deleted. TTL is 24 h for other rows (`ConfigurationKeys.Checkpoint.DefaultTtlHours =
24`). A new event arriving during a wait is skipped with a success result rather than double-executing
(`ProcessEventCommandHandler.cs:109-114`). `TransitionToScheduledWaitAsync` carries an explicit guard against
erasing adapter-flushed AdapterState/cursor/page.

### E. Zombie double-execution — MATERIALLY FIXED
The 30-second startup force-reclaim is **gone**: `RecoverAsync(CancellationToken)` has no `forceReclaim`
overload and `CheckpointRecoveryStartupService` calls that same method, so startup uses the ordinary threshold.
Stale threshold is 10 min = 2x the 5-min heartbeat, refreshed by persisted pages *and* the timer. CLAIM_LOST is
settled without redelivery and the claim stays with the owner. See `dcab434e`, `672ce996`.
Memory `project_isb_zombie_double_execution` updated — it described this as live and no longer does.

### The operator's caveat, tested
He noted the recent fixes target Falcon fail points, not Tenable. True of the motivation, **not** of the blast
radius: the commits touch `CheckpointRecoveryHandler`, `ProcessEventCommandHandler`, `CheckpointRepository`,
`AdapterExecutionContext`, `MessageSettlementMap` — all shared recovery plane. Grepping the last 14 commits'
diffs for vendor names returns only `PlatformType.SentinelOne` inside test fixtures; there is no production
branch on vendor anywhere in them. Tenable inherits the fixes identically.

### Verdict
Sufficient to go to STG on the recovery front. The two residual items are (1) confirm the deployed ISB build
carries these commits, and (2) the position-loss cost of a retryable failure, which is a known trade rather
than a defect.
