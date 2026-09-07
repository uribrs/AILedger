# Verifier pass 2 — repair round 1

Verifier: independent verifier subagent, 2026-08-17. Companion to `review/verifier-1.md` (not superseded —
pass 1 holds for everything outside the deltas below).
Branch `feat/tenableio-correlated-findings-staged`, baseRef `1f7a2ba6`.
Scope: only the deltas the lead declared, plus one delta it did not (§10).

---

## 1. Verdict

**Every declared repair landed and does what it claims.** I re-read the rewritten
`TenableIoAssetSpoolPhase.cs` (314 → 495 lines) rather than trusting the pass-1 citations, and re-ran the
tests myself.

- **D1 closed.** 429 and the 5xx family now retry, `Retry-After` is honoured, and `ComputeDelay`'s `ex`
  parameter is live.
- **D2 closed.** The ceiling trips before the manifest write, and no manifest is written when it trips.
- **D3 closed** for the failure mode that mattered — a differently-spelled listing no longer silently
  under-sweeps.
- **D4 closed at the unit level.** Both guards exist and throw; still nothing in production calls them,
  because there is still no flow entry point.
- **SC8 now largely met.** `Documentation/01` and `collector-flow-patterns/SKILL.md` carry the design;
  `collector-recovery` does not (§8).
- **CR#3, #7, #8 all verified as described.**

**One new defect, and it is the valuable one: the skipped-chunk ceiling can be evaded entirely.** It counts
only chunks that *failed*, never chunks that were never offered. A `FINISHED` export reporting
`total_chunks: 4` but only ever listing chunk 1 as available produces a spine built from a quarter of the
export, with **no warning and no failure** — the exact hazard D2 was added to close, reached by the other
road. This is not hypothetical: **the repair's own test suite encodes it as a pass** (§3).

Recommend one more small repair (reconcile the chunk accounting at end of pass), then this slice is done.

---

## 2. D1 — the 429/5xx retry: closed

The dedicated branch is at `TenableIoAssetSpoolPhase.cs:283-291`:

```csharp
catch (AdapterHttpRequestFailedException ex)
    when (!isLastAttempt && TenableIoAssetsExportClient.IsTransientOrRateLimited(ex))
{
    TimeSpan delay = TenableIoChunkRetry.ComputeDelay(config, attempt, ex);
    await DelayChunkRetryAsync(chunkId, attempt, delay, ex, cancellationToken)…
}
```

Checked, not assumed:
- The filter uses `IsTransientOrRateLimited`, which is exactly `{429, 502, 503, 504}`
  (`TenableIoVulnsExportClient.cs:123-129`), i.e. precisely the set
  `HttpTransportFailureClassifier.IsRetryableTransportFailure` cannot see.
- **`ComputeDelay`'s `ex` is no longer dead.** Line 289 passes it, so `TenableIoChunkRetry.cs:17-25` is
  reachable: `ex.RetryAfter` wins, then `429 → 60s × attempt`, then jittered exponential. The
  transport/breaker branch at `:292-298` still passes `ex: null`, which is correct — those exceptions carry
  no vendor hint.
- Branch **ordering** is right: the HTTP branch precedes the generic transport branch, and both precede the
  terminal skip. An `AdapterHttpRequestFailedException` can no longer reach `:299` while retries remain.
- The new tests genuinely exercise it rather than the classifier: the stub returns a real
  `429` with `Retry-After: 1` (`TenableIoAssetSpineTests.cs:365-370`) and real `502/503/504` responses
  (`:415`), and both assert `chunkAttempts == 2` — so a *second* HTTP call provably happened. The
  rate-limit test also asserts both asset objects exist, so "retried" is not confused with "recovered
  empty".
- `MaxChunkRetryAttempts = 2` / `ChunkRetryBaseDelaySeconds = 1` in the harness (`:648-649`) with a stated
  reason. Correct call: production defaults would have made these ten-second tests, which is how retry
  paths end up untested.

**Does SC2's phase-1 half now hold?** For the *retry* half, yes — no single 429 or 5xx costs a chunk any
more. For the invariant as a whole, **not yet**: see §3. The route through "chunk was never offered" is
still an unbounded, unreported loss.

## 3. D2 — the ceiling: closed as specified, and it revealed a bigger hole

**What was asked for is there and correct.** `EnforceSkippedChunkCeiling` is called at
`TenableIoAssetSpoolPhase.cs:92`, strictly before `spine.WriteManifestAsync` at `:96`, and throws at
`:371-374`. `RunAsync` has no `catch`, so the throw propagates and the manifest write never runs. Verified
by assertion, not by inspection: `SpoolPhase_WhenTooManyChunksAreLost_FailsInsteadOfWritingACompletionManifest`
asserts `store.Contains("_staging/manifest.json")` is false **and** `TryReadManifestAsync` returns null
(`TenableIoAssetSpineTests.cs:464-465`). The pure `ExceedsSkippedChunkRatio` is directly asserted over four
rows including the exact 50 % boundary (`:469-475`). `MaxSkippedChunkRatio = 0.5` with `>=` matches both
live flows verbatim (`TenableIoFindingsFlow.cs:27,391`; `TenableIoAssetsFlow.cs:33,367`).

### NEW DEFECT — N1 (HIGH). The ceiling counts failures, not absences.

`EnforceSkippedChunkCeiling` returns immediately when `pass.Excluded.Count == 0` (`:357-360`), and
`ExceedsSkippedChunkRatio` compares `excludedCount` against `totalExpected`. Nothing anywhere compares
`Processed.Count + Excluded.Count` against `TotalChunks`. And chunks are only ever attempted if they appear
in `chunks_available` — `FindNewChunkIds` iterates `status.ChunksAvailable` and nothing else
(`TenableIoExportPollHelpers.cs:13`).

So a chunk that is neither available nor reported failed is **invisible**: never downloaded, never counted,
never warned about, and it cannot move the ceiling.

**The repair's own test proves it.** `SpoolPhase_WhenAChunkIsFailedServerSide_RecordsItAsSkippedRatherThanFailingTheRun`
now stubs `{"status":"FINISHED","total_chunks":4,"chunks_available":[1],"chunks_failed":[4]}`
(`TenableIoAssetSpineTests.cs:315`). Chunks **2 and 3 are in neither list.** The test then asserts the pass
succeeds with `StagedAssetCount == 1`, `SkippedChunkCount == 1`, and a manifest written (`:330-333`) — and it
passes. Half the declared export vanished with the run reporting success and the manifest declaring
completion. Since the manifest is the phase-1 completion proof, a later leg would skip re-spooling it.

**Proof that chunks 2 and 3 are never even attempted, from the passing test itself.** That stub returns `404`
for `/chunks/2` and `/chunks/3`. If either were requested, `404` fails `IsTransientOrRateLimited` and fails
`IsRetryableStreamFailure`, so it would land on the terminal skip and join `Excluded` — making it 3 of 4, which
trips the ceiling and throws. The test asserts `SkippedChunkCount == 1` and passes, so those two chunks were
never requested at all. Absence is not merely uncounted; it is unvisited.

This makes the class doc comment at `:41-46` — "**Incompleteness is bounded, not silent**… beyond it the
phase fails instead of writing a manifest that claims a spine it does not have" — false as written. It is
bounded against download failure and unbounded against non-delivery.

Likelihood: a vendor `FINISHED` should imply every chunk is available, so this is a defence-in-depth gap
rather than an expected path. But it is the same gap D2 was raised for, the fix is a few lines, and the
alternative is trusting a vendor status field with no reconciliation — which is exactly what the ceiling
exists to stop doing.

**Suggested repair:** after `PumpUntilExportFinishedAsync`, treat
`TotalChunks - (Processed.Count + Excluded.Count)` (when `TotalChunks > 0`) as missing chunks, log them, and
feed them into the same ceiling — or fail with a distinct message. Then fix the test stub to declare
`total_chunks: 2` (or list 2 and 3) so it stops asserting the hole is acceptable.

### On the run-failure judgement call

The lead flagged introducing a run failure against the operator's "no failures, no fallbacks" preference.
**I agree with the lead's reading, and it is on firmer ground than a judgement call.**

- The operator's statement is recorded in `decisions.md` under Fork A and is specifically about *designs
  that carry a size cliff*: "RAM needs a bound; a bound needs a behaviour at the bound; that behaviour is
  either a failure or a degrade." The objection is to an **alternate output path**, not to refusing to
  proceed on bad input.
- The same decision explicitly accepts hard failures of this class: "Staging's own risks are preconditions
  and retries, not runtime alternate paths… an unregistered `IAdapterObjectStore` is a hard stop at flow
  entry, as in Falcon."
- `constraints.md` does not leave it to judgement at all: "*Keep* the existing 409 `active_job_id` reuse,
  progressive chunk loop, per-chunk retry/exclusion, **and the `MaxSkippedChunkRatio` guard**." Both live
  flows fail on this exact ratio today. This is preserved behaviour, not new behaviour.

So the ceiling needs no defence. Silently writing a completion manifest over a half-empty spine would have
been the novel behaviour.

## 4. CR#3 — export re-creation cap: confirmed

- Cap is **2 re-creations**, enforced at `:197` (`pass.Recreations >= MaxExportRecreations`, `MaxExportRecreations = 2`
  at `:75`), incremented in `SpoolPass.Restart` (`:487`). Traced: 1st 404 → `0 >= 2` false → recreate
  (`Recreations = 1`); 2nd → recreate (`= 2`); 3rd → throws `InvalidOperationException` wrapping the 404 as
  `innerException`.
- A delay precedes **each** re-creation (`Task.Delay(ExportStatusPollIntervalSeconds)` at `:212`, before
  `CreateExportAsync` at `:215`), so a persistent 404 cannot become a hot POST/GET loop.
- **No manifest results**: the throw is inside `PumpUntilExportFinishedAsync`, called at `:90`, before the
  write at `:96`. Asserted: `SpoolPhase_WhenTheExportKeepsExpiring_GivesUpInsteadOfLoopingAgainstTheApi`
  checks `creates == 3` (initial + exactly 2) and `store.Contains("_staging/manifest.json")` is false
  (`TenableIoAssetSpineTests.cs:507-508`).
- Residual, unchanged from pass-1 D7 and now less important: `timeoutAt` is still computed once
  (`:117`) and is not extended across a re-creation. With the cap in place this is a bounded total budget
  and reads as deliberate.

## 5. D3 — silent under-sweep: closed

`TryGetStagedAssetId` (`TenableIoAssetSpine.cs:205-210`) now locates `_staging/spine/` **inside** the
canonical URL via `LastIndexOf` and parses from there, instead of stripping this spine's own base off the
front. The dead `Relative()` helper is gone (`grep` for it in the file returns nothing).

The silent failure is genuinely closed, and closed twice over:
1. the parse no longer depends on how the host spells the base, so the mismatch does not arise;
2. anything still unrecognized is **counted** and logged once with `{Unrecognized}`, `{Recognized}` and the
   first offending location (`:183-191`), with the reasoning recorded — the same reasoning Falcon's
   `WarnIfNotRelativeToBase` carries.

The test is a real test, not a tautology: `ListStagedAssetIds_StillFindsAssetsWhenTheStoreSpellsLocationsDifferently`
overrides the double's `LocationFactory` to re-spell the base as
`ObjectLocation.Create("s3://cybi-data", "stg/raw-data/tenant/setting/run/" + key)` (`:596`) — same canonical
URL, different split — and asserts both ids come back. Under the old implementation that yielded an empty
sweep.

Residual worth one line (not a defect): the check is now "does this URL contain my spine prefix", so it no
longer verifies the object is beneath *this* run's base. A store that over-lists past the requested prefix
could inject a foreign run's assets instead of dropping them. That requires a store violating its own prefix
contract, and the previous behaviour on the same broken store was worse (drop everything, silently), so this
is the right trade — just no longer a guard against that direction.

## 6. D4 — hard failure: implemented, still uninvoked

Both guards exist and throw:
- `RequireBaseStorageUrl` (`TenableIoAssetSpine.cs:91-95`) wraps `TryResolveBaseStorageUrl` and throws when
  the run carries no `storageUrl`.
- `RequireObjectStore` (`:104-109`) resolves `IAdapterObjectStore` from DI and throws naming what to
  register.

Tested for real: `RequireObjectStore_WithNothingRegistered_FailsLoudlyRatherThanFallingBack` and
`RequireBaseStorageUrl_WhenTheRunCarriesNoStorageUrl_FailsLoudly`, the latter building a genuine
`AdapterProgressContext.FromPlatformEvent` with empty metadata rather than a mock (`:622-630`).

So the answer to "implemented rather than potential" is **yes at the unit level, not yet at the flow
level**: no production code path calls either, because phase 1 is still unwired by design. The claim to make
is "the guard exists and is proven to throw", not "the flow hard-fails" — the latter becomes true when
phase 2 wires the entry point. Two notes:
- `RequireObjectStore` duplicates the throw `GuardedObjectStore.Create(services)` already performs
  (`GuardedObjectStore.cs:86-89`). Harmless, and the collector-specific message is more useful; just make
  sure the wiring calls one of them, not neither.
- `RequireObjectStore` returns the raw `IAdapterObjectStore`. The constraint is that the collector reaches
  the store *only* through `GuardedObjectStore` — so whatever wires phase 2 must not hold onto this
  return value directly. Nothing does today.

## 7. CR#7 and #8 — the smaller fixes

| Item | Verified |
|---|---|
| `OperationCanceledException` filter discriminates | `:276` is `catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)`. Traced the other branch: an internal OCE from `Parallel.ForEachAsync` fails the filter, then fails `IsRetryableStreamFailure` (the classifier returns `false` for `OperationCanceledException` outright, `HttpTransportFailureClassifier.cs:54-57`), and lands on the terminal skip — which is what the comment says it should. Correct. |
| Poll counting out of the exception filter | `IsRetryablePollFailure` (`:392-395`) is now pure and static; counting/logging moved into `TryAbsorbPollFailure` (`:406-422`) called from the handler body (`:177`). The stated reason — a filter that logs lets a throwing logger reclassify a retryable failure as fatal — is sound. |
| Circuit-breaker failures count toward the ceiling | `TryAbsorbPollFailure` increments unconditionally; the old exemption is gone, and the remark at `:401-405` records why. Correct: an exempt breaker could poll for the whole export timeout with the adjacent ceiling silently inert. |
| Test double locks every member | `_gate` with `lock` at 8 sites incl. the `Writes`/`Reads`/`Keys` snapshot getters (`InMemoryTenableIoStagingStore.cs:44-158`). Snapshots return copies, so an assertion cannot tear against an in-flight write. Matters now that a test drives a 500-asset 32-way fan-out. |
| `GuardedObjectStore` disposed | Test class implements `IDisposable` and disposes every façade it created (`TenableIoAssetSpineTests.cs:22-36`). Correct — the façade owns semaphores. |
| `RunAsync` decomposed | `SpoolPass` private state class (`:465-493`) with `Restart` owning the reset; `TenableIoAssetSpoolRequest` record (`:14`); pump split into `PumpUntilExportFinishedAsync` / `TryPollStatusAsync` / `RecreateExpiredExportAsync` / `StageAvailableChunksAsync` / `TryStageChunkAsync` / `StageChunkOnceAsync` / `DelayChunkRetryAsync`. Reads well and matches the repo's small-methods convention. Constructor now takes `assetsClient` and `spine`, so the per-call parameter bag is gone. |
| Doc claim: "one addressed fetch" | Restated accurately at `TenableIoAssetSpine.cs:128-144`: stat-then-read, one of `MaxConcurrentReads` (4, or 1 under pressure), **and** the caveat that a 1–8 MiB record throws `DataPipelineException` under memory pressure instead of returning bytes — explicitly flagged as an exception on a path documented as exception-free, with the decision handed to phase 2. Matches `GuardedObjectStore.EffectiveBufferedReadCap`. Correct and appropriately uncomfortable. |
| Doc claim: reusable-buffer rationale | Now argued from ownership and clarity rather than from a false aliasing hazard (`TenableIoAssetSpoolReader.cs`). Pass-1 D5 resolved. The new 500-asset saturation test (`:538-582`) asserts every id carries its own marker, which is the empirical form of the same claim. |
| Doc claim: "one path" | Restated at `TenableIoAssetSpoolPhase.cs:29-34` — failure paths are acknowledged (retry, re-creation, capped skip, timeout), and "one path" is scoped to *ways of producing output*. Accurate now. |

## 8. SC8 — docs: mostly met

- `Documentation/01-collection-strategy.md` (+42 lines) adds "In progress: the correlated model (phase 1
  landed, NOT wired)": the staged spine and its key schema, the leading-`_` requirement with the
  parser-prefix reason, one-object-per-asset with the read-amplification argument, and a dedicated section
  on the asset-complete premise that states **what the vendor does not promise** ("non-straddling is
  *implied by the split-by-asset-ID model, not promised*"), the 150-chunk probe, the absence of any
  asset-UUID parameter on `POST /vulns/export`, and the thin-host tolerance. It also states plainly that the
  live path is untouched. This is accurate against both the code and the vendor page I re-fetched in pass 1.
- `ai/skills/collector-flow-patterns/SKILL.md` (+17 lines) records the look-up-vs-enumerate divergence and,
  usefully, the *transferable* rules: key by identity not position, don't copy Falcon's generation concept
  without checking whether your keys are positional, `GuardedObjectStore` not `PriorStateStore` for verbatim
  bytes, a lookup is stat+read on a 4-slot semaphore, the façade bounds reads only.
- **`ai/skills/collector-recovery` is untouched** (`git diff 1f7a2ba6 --stat -- ai/skills/collector-recovery/`
  is empty). SC8 names it explicitly. Defensible to defer — the recovery story is Fork B, i.e. phase 2 — but
  it is not met, and the manifest-as-completion-proof primitive that *did* land is a recovery concept.

### A1a, revisited

Pass 1 marked A1a "REJECTED as closed — the gap is still open". **Half of it is now closed.** The
`Documentation/01` half is done, and done better than asked (it states the vendor's silence, not just the
premise). The **test** half is not: no test asserts that a straddled asset yields a thin second host rather
than a lost one, because that lives in phase 2's `BuildRecordsForChunk`. A1a is now **partially closed**;
the remaining obligation is a phase-2 test.

## 9. Tests and build — re-run by me

```
dotnet build …TenableIoCollector.Test.csproj
  → 0 Warning(s), 0 Error(s)

dotnet vstest …TenableIoCollector.Test.dll --TestCaseFilter:"FullyQualifiedName~TenableIoAssetSpineTests"
  → Passed!  Failed: 0, Passed: 43, Skipped: 0, Total: 43, Duration: 13 s

dotnet build Cymulate.Integration.Adapters.sln
  → 0 Warning(s), 81 Error(s) — all 162 error lines resolve to
    Cymulate.Integration.Adapters.YamlAdapter.Test.csproj, unchanged from baseRef
```

**43/43 confirmed**, matching the lead's number exactly. I counted the cases independently from the source
(9 schema/guard + 5 id validation + 5 spine read/write + 3 manifest/prune + 2 original spool + 4 new retry +
5 ceiling + 2 export-lifecycle + 1 fan-out + 1 listing + 2 preconditions) and got 43.

**Solution build: still red at baseRef, for the YamlAdapter.Test reason only.** 81 errors, all CS0106/CS1022
syntax errors from an unclosed brace in `YamlAdapterTests.cs` (first at line 565), in a file byte-identical
to `1f7a2ba6`. Nothing in this repair touches it, and every other project in the solution — including the
modified `LocalAdapterRunner` — compiles clean.

Full TenableIo suite, unfiltered, re-run by me:
```
Failed!  - Failed: 1, Passed: 57, Skipped: 0, Total: 58, Duration: 5 m 14 s
[FAIL] TenableIoCollectorTests.ResumeAsync_Findings_WhenTransportResponseEndsPrematurely_ReturnsRetryableAdapterFailure
       Assert.Empty() Failure: Collection was not empty
       Collection: [CompletionRequest { Context = AdapterProgressContext, Success = False }]
       at TenableIoCollectorTests.cs:line 696
```
**57/1/58 confirmed**, matching the lead's number exactly, and the single failure is the same pre-existing
dev defect (identical test, assertion, line and ~5m14s duration as in pass 1). Cross-check: 58 − 43 = 15
non-spine tests, the same 15 as pass 1 (43 − 28), so the delta is purely the 15 new spine tests and nothing
else moved.

## 10. UNDECLARED DELTA — the LocalAdapterRunner

Not in the lead's list, and it is the only change to a **tracked, live** file in this round:

```
 M src/…/Tools/…LocalAdapterRunner/Program.cs                  (+16)
?? src/…/Tools/…LocalAdapterRunner/Publishing/LocalFileAdapterObjectStore.cs  (194 lines)
```

`Program.cs:219-234` registers a filesystem `IAdapterObjectStore` + `IAdapterObjectPruner` **when none was
wired**, under `{outputDir}/_object-store`, and reports it in the run's object-store line as "mechanics only,
not S3 semantics". `LocalFileAdapterObjectStore` implements the real contract carefully — absence as `null`
from `StatAsync` vs `ObjectNotFoundException` from `OpenReadAsync`, write-to-`.partial`-then-`File.Move` for
all-or-nothing visibility, separator-boundary prefix listing, `.partial` files excluded from listings — and
its doc comment states plainly what it cannot prove (IAM, request cost, throughput, consistency) with an
explicit "read a green local run as 'the mechanics are right', never as 'this works in the cluster'".

Assessment: **well-built, correctly caveated, genuinely needed to make S9 executable at all** (the concrete
S3 store lives in the ISB repo behind `HAS_ISB_AWS`, which no committed build defines). Two things the lead
should decide consciously rather than inherit:

1. **It changes the local-run semantics of every staged collector, not just Tenable.** Falcon's two-phase
   flow previously hard-failed locally with no store — which was itself a signal, and the local
   demonstration of the A12 design intent. It will now quietly succeed against the filesystem. The log line
   is the only distinguisher.
2. **Same-URL-different-split locations map to different files.** `PathFor` hashes `location.BaseUrl` and
   appends `location.RelativePath`, so `Create("s3://b/run", "_staging/x.json")` and
   `Create("s3://b", "run/_staging/x.json")` — identical canonical URLs — land in different directories. The
   spine is internally consistent (always one split), and `ListAsync` returns base-relative paths that the
   spine's new `LastIndexOf` parse handles, so nothing is broken today. But it is precisely the
   "the split is not part of a location's identity" property that D3 was fixed to respect, and this store
   depends on it. Worth a line in the type's remarks.

Neither is a blocker. Flagging the undeclared scope because a tracked-file change to a shared dev tool is
not something a verifier should have to discover.

## 11. The changed test — right direction, and it now hides N1

The question was whether `SpoolPhase_WhenAChunkIsFailedServerSide` moving from 1-of-2 to 1-of-4 was a test
bent to fit the code. **It was not.**

- The `>=`-at-0.5 boundary is not an invention of this change: both live flows use
  `permanentFailures >= totalExpected * 0.5` verbatim (`TenableIoFindingsFlow.cs:391`,
  `TenableIoAssetsFlow.cs:367`), and the constraints require keeping that guard. 1-of-2 sits exactly on a
  boundary the codebase already refused everywhere else.
- The boundary is **still covered**, and covered better — directly, by the pure theory row
  `[InlineData(4, 0, 2, true)]` (`TenableIoAssetSpineTests.cs:469`), instead of incidentally through a
  full-pass test.
- The test's actual purpose is "a server-failed chunk is tolerated rather than fatal". 1-of-2 was
  accidentally asserting two things at once, one of which is now false. Moving to 1-of-4 isolates the
  purpose. That is a sharpened test.

**But the same edit introduced the stub that demonstrates N1**: `total_chunks: 4` with only chunk 1
available and only chunk 4 failed leaves chunks 2 and 3 unaccounted for, and the test asserts that as
success. Once N1 is fixed, this stub must change too — `total_chunks: 2`, or list 2 and 3 — or the test will
start failing for the right reason.

## 12. Certainty

- **High** on all six declared repairs landing as described: each verified by reading the rewritten source
  and by a test whose assertions I traced to a real second HTTP call / a real absent manifest / a real
  re-spelled location.
- **High** on N1. It follows from three lines of code (`:357`, `:387`, `TenableIoExportPollHelpers.cs:13`)
  and is demonstrated by a passing test in the repair's own suite.
- **High** on the test-change direction and on the run-failure judgement, both anchored in
  `constraints.md` and the live flows rather than in my preference.
- **Medium** on the LocalAdapterRunner assessment: I read the file in full and it builds, but no local run
  was executed, so its behaviour is verified by construction, not by use.
- **Unchanged from pass 1 and still unverified**: A12a (`s3:ListBucket`), A12 (store registration), A15
  (parser shape), and everything phase 2–3. The new local store deliberately cannot answer A12a — its own
  doc comment says so.
