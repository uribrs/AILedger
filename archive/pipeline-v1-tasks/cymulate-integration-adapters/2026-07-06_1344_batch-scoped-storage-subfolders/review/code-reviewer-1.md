# Code Review — batch-scoped storage subfolders (working tree, branch `batchful-uploads`)

Reviewer: independent code-reviewer pass 1
Scope: `BatchScopedStorage.cs` (new), 7 shared orchestration/resilience call sites, `BatchScopedStorageTests.cs` (new), one executor test, README/SKILL doc updates.
Risk classification: shared library code touching event publication, checkpointing, and resume flow — **High** risk category; reviewed at that depth.

## Verdict

The implementation is sound. The core mechanism — rewriting `Metadata["storageUrl"]` per page, preserving the pristine base under `baseStorageUrl`, deriving every batch path from the preserved base so paths never stack — is correct, and I verified the two properties the design leans on:

- **No concurrent-flush hazard.** `ResultsBatchPublisher.PublishCoreAsync` creates the NDJSON session inside the publish call and disposes it before returning (ResultsBatchPublisher.cs:94, 205), and `FlushInterval` is checked inline at append time, not by a background timer (NdjsonBatchSession.cs:133, 156). All flushes for page N therefore run strictly between `BeginPage(N)` and the awaited publish return — the documented `BeginPage → publish → AdvancePage` ordering is sufficient, not merely conventional.
- **Restore coverage is complete for the shared publication surface.** All four `CompletionRequest` construction sites in Shared (entrypoint success DONE, failure completion, both partial-success publishers), the `ErrorRequest` path, both resume-success paths, and the deferred-wait snapshot restore the base before publishing. Cancellation deliberately publishes nothing (AdapterBusEntrypointRunner.cs:129-144), so it needs no restore.

No blockers. Findings below are hardening and consistency items, ranked most severe first.

---

## 1. Major (likely risk, contingent on host behavior) — `ResolveBaseUrl` will adopt a batch path as the base, producing nested `batch_*/batch_*` folders

**Where:** `Shared/.../DataPipeline/Egress/BatchScopedStorage.cs:100-116`

**Problem:** The anti-nesting guarantee rests entirely on two out-of-repo assumptions: (a) every event the host might mirror into a resume `PlatformEvent` carries the run-root URL, and (b) when the host does mirror metadata, it carries `baseStorageUrl` along with `storageUrl`. By design, page-advance progress events *do* carry the batch path in `storageUrl` (that is the feature). If a resume event is ever constructed by echoing one of those events' metadata **selectively** — `storageUrl` copied, the unknown `baseStorageUrl` key dropped, which is exactly what a typed-model round-trip like `CollectorEventMetadata` (only `storageUrl` has a `JsonPropertyName`) would do — then on resume `ResolveBaseUrl` finds no preserved base, treats `.../run/batch_000007` as the base, and every subsequent page uploads to `.../run/batch_000007/batch_000008/...`. The DONE event then announces the nested path. Nothing in the code detects or repairs this; it compounds on every subsequent resume.

**Why it matters at runtime:** silent data-layout corruption in a bucket that is never purged, discovered only when upstream parsing can't find batches where the run root says they should be. It is the one failure mode of this feature that is invisible to the collector and unrecoverable by overwrite semantics.

**Fix (local patch, cheap):** make `ResolveBaseUrl` self-healing — after trimming, strip a trailing batch segment before returning:

```csharp
// A batch path must never become the base: strip a trailing "batch_NNNNNN" segment defensively.
int lastSlash = candidate.LastIndexOf('/');
if (lastSlash >= 0)
{
    string lastSegment = candidate[(lastSlash + 1)..];
    if (lastSegment.StartsWith(BatchSegmentPrefix, StringComparison.Ordinal) &&
        lastSegment.Length == BatchSegmentPrefix.Length + 6 &&
        lastSegment[BatchSegmentPrefix.Length..].All(char.IsAsciiDigit))
    {
        candidate = candidate[..lastSlash];
    }
}
```

Apply to both the preserved and the live-key branch (one shared helper). Deterministic segment naming makes this check exact — there are no false positives unless a tenant's run root legitimately ends in `batch_` + 6 digits, which the fixed format makes effectively impossible. Add one test: `BeginPage` on a context whose `storageUrl` already ends in `batch_000007` (and no `baseStorageUrl`) yields `.../run/batch_000008`, not a nested path.

I label this *likely risk*, not confirmed bug: if the host always builds resume metadata from the platform-side run record (the repo's own convention says the batch location "rides the PlatformEvent storageUrl, stable across resume"), the scenario never fires. But the seven `RestoreBase` call sites in this very diff exist precisely because the author does not fully trust event/metadata echo paths — the defense should extend to the one place a bad value would actually take root.

## 2. Minor — run-level-event invariant is enforced by seven scattered call sites, i.e. by convention

**Where:** `AdapterBusEntrypointRunner.cs:117`, `AdapterFlowFailureHandling.cs:57`, `AdapterBusPartialSuccessPublisher.cs:36`, `CollectorResumePartialSuccessPublisher.cs:35`, `CollectorResumeLegacyExecutor.cs:44`, `CollectorResumeStrategyExecutor.cs:42`, `AdapterFailureDecisionExecutor.cs:344`

**Problem:** the invariant "run-describing events carry the run-root URL" has no structural owner. Publication funnels through the SDK's `context.PublishAsync` with distinct request types, so there is no in-repo choke point — every current site must call `RestoreBase`, and every *future* publication path (a sixth completion variant, a new snapshot writer) must remember to. Omission fails no test and produces no error; it just quietly ships a page path on a run-level event.

**Impact:** maintenance trap rather than a present bug. Coverage today is complete (verified above), the identical grep-friendly comment at each site helps, and the README documents the rule.

**Fix:** no refactor required now. Two cheap mitigations, either sufficient: (a) fold the restore into a tiny local wrapper where a shared seam already exists — e.g. both partial-success publishers share the `CompletionRequest(…, Success: true)` shape; (b) add the guard test from finding 4 so removal/omission is at least detectable. Revisit a structural fix only if an eighth site appears.

## 3. Minor — new `StorageUrlMetadataKey` constant creates a second source of truth; existing readers still hardcode `"storageUrl"`

**Where:** constant at `BatchScopedStorage.cs:39`; hardcoded literals at `NdjsonBatchSession.cs:429`, `NdjsonUtf8BatchSession.cs:423`, `AdapterPlatformEventFactory.cs:23,82`, `AdapterBusEntrypointRunner.cs:201`

**Problem:** the correctness of the whole mechanism depends on the writer (`BatchScopedStorage`) and the readers (the two NDJSON sessions, at flush time) agreeing on one string. The change introduces the canonical constant but leaves every pre-existing reader on its own literal. The repo's own doctrine (Glossary = single source of truth for strings) argues for one owner.

**Impact:** a future rename or a typo'd new reader silently decouples upload targeting from the scoping helper; the compiler can't catch it.

**Fix (local patch):** point the five existing read sites at `BatchScopedStorage.StorageUrlMetadataKey` (Egress already sits below both Orchestration and DataPipeline consumers, so no dependency problem), or move the key into Glossary if Egress feels like the wrong host. Ten-minute change, no behavior delta.

## 4. Minor — only 1 of 7 `RestoreBase` call sites is pinned by a test

**Where:** tests cover `AdapterFailureDecisionExecutor.PersistDeferredWaitSnapshot` only (`AdapterFailureDecisionExecutorTests.cs:150-176`). The success-DONE restore (`AdapterBusEntrypointRunner.cs:117`), the error/failure path, both partial-success publishers, and both resume-success paths are untested.

**Problem:** delete the `RestoreBase` line at any of the six other sites and the suite stays green. Given finding 2 (invariant-by-convention), the tests are the only enforcement mechanism available, and they currently guard the least-trafficked site while leaving the most-trafficked one (every successful run's DONE) unguarded.

**Impact:** regression risk on exactly the property this change exists to provide.

**Fix:** one representative end-to-end-ish test at the entrypoint-runner level (the DummyCollector test project already drives `AdapterBusEntrypointRunner`): scope a page mid-flow, let the run complete, assert the context's `storageUrl` equals the base at completion-publish time. That plus the existing executor test covers the two structurally distinct restore layers; pinning all seven individually is not necessary.

## 5. Observation — `BeginPage` ships with zero production callers; the documented opt-in flag does not exist

**Where:** `BatchScopedStorage.cs:51`; grep confirms no non-test caller of `BeginPage` anywhere in `src/`.

The XML doc says a collector opts in "via its own configuration flag" — no such flag exists in any collector yet. The public surface (three members, a documented ordering contract, README + SKILL entries) ships ahead of its first consumer, so the usage contract has never been exercised by a real flow; `PageLifecycle_HoldsEachPagesPathUntilItsAdvance` (`BatchScopedStorageTests.cs:142`) simulates the contract in-test rather than observing a collector. This is acceptable scaffolding for a feature branch named `batchful-uploads`, and the API is small enough that the risk is low — but expect the contract details (especially the dud-page `RestoreBase` step, which is easy to forget) to get their first genuine validation only when the first collector wires in. No action required now; just don't let the helper sit unconsumed across releases.

## 6. Observation — first Orchestration→Egress and Resilience→Egress dependency edges; placement is nonetheless correct

**Where:** new `using ...DataPipeline.Egress` in five Orchestration files and one Resilience file.

These are the first references from those folders into Egress. Per the subsystem READMEs, `RestoreBase` is arguably an event-metadata (orchestration) concern. But the helper must live where the key is consumed — the NDJSON sessions read `storageUrl` live at flush time — and splitting `BeginPage` (Egress) from `RestoreBase` (Orchestration) would smear one mechanism across two subsystems. Single home in Egress with the composition layer calling down is the right call; the Egress README documents it. No action.

---

## Nits

- `RestoreBase` restores the *trimmed* base, not the verbatim original (a trailing `/` on the incoming URL is dropped permanently after the first `BeginPage`). Semantically harmless — both sessions normalize via `NormalizeStorageUrlToKeyPrefix` — but worth one clause in the XML doc so nobody chases a "changed URL" diff in event payloads.
- After opt-in, `baseStorageUrl` stays in metadata for the rest of the run, so run-level events carry a redundant key alongside an identical `storageUrl`. Harmless (typed envelope models ignore unknown keys) and actually load-bearing for `ResolveBaseUrl`'s preference order — intentional, leave as is.
- `BuildBatchSegment_PadsPageToSixDigits` restates the format string; low signal, but the segment name is an on-the-wire contract with upstream parsing, so pinning it is defensible.
- `BeginPage`'s mixed contract — throws for `pageNumber <= 0`, returns `null` for absent metadata — is the right split (programmer error vs. environmental absence); noting only so it isn't "unified" later.

## Test quality note (asked-for explicitly)

The `BatchScopedStorageTests` suite pins real behavior, not the implementation: the two `PublishAsync_*` tests drive the actual `NdjsonBatchSession` through `ResultsBatchPublisher` with only the terminal `IAdapterDataPublisher` faked, and assert the final key (`stg/raw-data/.../batch_000003/findings_000003.json`) — which also locks the `s3://bucket/` stripping interplay. The never-stacks, crash-resume-determinism, dud-page, and no-op-when-not-opted-in tests each encode a distinct externally observable guarantee. The gap is coverage breadth at the orchestration call sites (finding 4), not test construction.
