# Code Review (re-review) — YAML Engine declarative enrichment

Re-examination of the repaired uncommitted diff, scoped to my prior findings (`code-reviewer-1.md`) plus any new defects the repairs introduced.

**Build:** clean (0 warn / 0 err). **Tests:** 544 passed / 0 failed (up from 540 — 4 new tests, including targeted coverage for the two Major fixes).

Per-finding outcome: **#1 fixed · #2 fixed · #4 fixed (defensible posture) · #7 fixed · #3/#5/#6 skipped (maintainer-deferred).**

---

## #1 — resume data loss → **fixed**

`Workflow/WorkflowRunner.cs:57-66`. A workflow containing any `merge_into` stage now discards incoming resume state and runs fresh (`resume = null`) with an INFO log; the class doc (`:35-40`) is corrected to describe exactly this exception. This removes the silent-loss path: the target stage is no longer skipped, so `heldByStage` is always populated before the merge consumes it.

**Completeness / no-new-defect check (the counter & S3-key question):**
- On the forced-fresh path `resume` is null, so `pageCounters` starts empty and per-topic page numbers restart at 1 (`++pageCounters[topic]` from 0), and `result.PublishedByTopic` starts at 0.
- I verified the storage key is deterministic and page-numbered: `WorkflowPublishSink` writes `findings_NNNNNN.json` / `assets_NNNNNN.json` via `CollectorNdjsonPublisher`, keyed solely by the runner's page number, into a batch directory that is stable across resume (it rides the `PlatformEvent` storage URL, not the checkpoint). So a fresh rerun re-emits pages `1..N` to the **same** object keys and overwrites deterministically — no orphaned or duplicated objects for the normal case.
- `Resume_MergeWorkflow_IgnoresCheckpoint_RunsFresh_PublishesAllEnriched` locks this down: a checkpoint claiming `CompletedStages=2, FindingsPublished=99, PageCounters[findings]=5` is fed in, and the run asserts `findings == 2` (not `99+2`) and `PageNumbers == [1]` (not continuing from 5). Directly validates counter/page reset.

**Two observations (neither blocking):**
- *Orphan-on-shrink (very low likelihood):* the deterministic-overwrite guarantee holds only while the fresh complete run emits at least as many pages as any prior interrupted attempt. If a prior partial attempt published pages `1..k` and a later complete rerun emits only `1..M` with `M < k`, pages `M+1..k` from the old attempt are left in the batch dir. This requires the complete dataset to be smaller than a partial prefix of an earlier attempt (dramatic vendor-side shrinkage between runs) — essentially unreachable in practice, and it is a general property of any restart-from-page-1 flow rather than something this change introduces. If the team wants belt-and-suspenders, clear the batch dir at the start of a merge-forced-fresh run. Not required for ship.
- *No cross-resume forward progress for merge workflows:* a merge workflow that repeatedly dies mid-run redoes all work each time. This is the conscious v1 tradeoff (correctness over resumability) and is now documented in the class summary. Accepted.

## #2 — mid-chunk cache eviction dropping enrichment → **fixed**

`Workflow/WorkflowRunner.cs:455-500`. Per-chunk matches are now resolved into a plain, unbounded `chunkMatches` dictionary: step 1 seeds it from the cross-chunk cache on a hit and queues misses; `FetchIntoCacheAsync` (`:507-538`) writes each fetched record into **both** `chunkMatches` and the bounded cache; embedding (`EnrichArrayAnchor`/`EnrichRecordAnchor`, now taking `IReadOnlyDictionary`) reads **only** `chunkMatches`. The `BoundedKeyCache` is therefore a cross-chunk reuse optimization that the embed path never consults, so a chunk whose distinct-key count exceeds `cache_size` still enriches in full.

I confirmed there is no JsonNode re-parenting hazard: cached/matched nodes are parsed standalone and embedded via `DeepClone()`, so the same source node can back multiple target records without a single-parent violation.

**Test strength:** `Cache_SmallerThanChunkDistinctKeys_AllKeysStillEmbed` (`:439-453`) sets `cache_size: 1` with three distinct keys in one default-size chunk and asserts all three embed — this is the exact scenario the original code got wrong and the old `page_size:1` eviction test could never reach. The pre-existing cross-chunk tests (`Cache_SecondChunkSameKey_DoesNotReInvokeSource`, `Cache_SizeBound_EvictsOldest_ReFetches`) still pass, so cross-chunk reuse and bounded eviction are preserved. Good coverage.

## #4 — follow_url credential forwarding → **fixed (defensible v1 posture)**

`Pagination/BodyCursorPaginator.cs:113-128`, `PaginatorFactory.cs`, `IntegrationEngine.cs:228`. The paginator now takes an optional `ILogger` (defaults to `NullLogger`), the factory forwards it, and the engine passes its real logger — so `WarnOnHostChange` is live, not dead code. A cross-host next-page URL logs a warning naming both hosts and stating credentials will be re-applied; the run proceeds, and `PaginationConfig.FollowUrl` carries the trust-model note.

**Is warn-and-proceed defensible?** Yes for v1. Body-/link-driven continuation URLs on sibling or regional hosts (CDN, sharded API endpoints, regional redirects) are legitimate and common; hard-blocking cross-host would break real vendors. The residual risk (a compromised vendor response naming an attacker host to harvest the API credential) requires the already-trusted vendor channel to be hostile, and the absolute-http/https guard plus an audit-grade warning is a proportionate stance that matches how link-header pagination already behaves. Reasonable to ship; a host allowlist can be a later hardening if a security review demands it.

*Minor observation:* the warning fires on every page of a cross-host paginated run (once per `ApplyToRequest`), so a legitimately host-hopping vendor produces one warning per page. Log noise, not a defect — arguably desirable for audit. Leave as-is or downgrade to first-occurrence-only if it proves noisy.

## #7 — schema diff reviewability → **fixed**

The whitespace re-expansion is gone. `git diff` on `integration.schema.json` is now `+30 / -2`, a single targeted hunk adding the `body_cursor` enum value + three `body_cursor` fields, the `$self` self-envelope in `mappingValue`, and the `merge_into` stage block. `git diff -w` confirms no formatting-only churn remains. Fully reviewable, and I confirmed every semantic addition from the feature is intact.

## #3 / #5 / #6 — **skipped**

Maintainer-deferred (unconditional `ExtractArray` object coercion; no negative caching; `{{keys}}` comma-join ambiguity). I re-checked each against the repaired code — none changed and none crossed into new severity. No objection to deferring.

---

## New defects introduced by the repairs

None blocking. The only new-behavior notes are the two low-severity observations under #1 (orphan-on-shrink; no cross-resume progress — both accepted tradeoffs) and the per-page host-change log line under #4. Nothing that changes correctness for realistic inputs.

---

## Verdict

**ship** — both Major silent-data-loss findings are correctly and completely fixed, each with a direct regression test; the fixes introduce no new defect for realistic inputs; the follow_url posture is a defensible, documented v1 tradeoff; the schema diff is now clean. 544 tests green, clean build.
