# Code Review — YAML Engine declarative enrichment (`merge_into`, `body_cursor`, `$self`)

**Scope:** all uncommitted changes on `feature/yaml-engine-declarative-enrichment`.
**Stack:** C# / .NET 8, YAML-driven integration engine + xUnit tests.
**Risk classification:** shared library / feature logic on a resumable, paginated collector data plane → **High** (persistence, pagination state, resume/idempotency, large-data iteration).
**Build:** clean (0 warn / 0 err). **Tests:** 540 passed / 0 failed.

Reviewed against runtime behavior under realistic failure, not just green tests. Two silent-data-loss findings dominate; the rest are minor or observational.

---

## 1. [Major] `merge_into` + mid-workflow resume silently loses the un-published remainder of the target dataset

**Files:**
`Workflow/WorkflowRunner.cs:475` (per-chunk checkpoint), `:412` (completion checkpoint), `:88-95` (merge dispatch), `:69-74` (held-records map);
`Execution/YamlOperationRunner.cs:204-237` (resume is genuinely wired: fingerprint-gated restore + per-stage checkpoint persistence via `progress.SetState`/`AdvancePage`).

**What breaks.** Workflow resume is real and honored: `if (stageIndex < completedStages) continue;`. A merge target stage marks itself complete like any other data stage (`BuildCheckpoint(stageIndex + 1, …)` at `:116`/`:141`), and the merge stage additionally emits an advancing per-chunk checkpoint at `CompletedStages = mergeStageIndex` (`:475`). The target's enriched records only exist in the in-memory `heldByStage` map, rebuilt solely by *running the target stage*. On any re-invocation after the target completed (crash, cancellation, pod eviction mid-merge), the restored `completedStages` is `>= targetIndex + 1`, so the target stage is **skipped**, `heldByStage[target]` is empty, and the merge iterates zero records — it publishes nothing for everything not yet published, then marks itself complete. No exception, no duplicate, just missing findings.

**Failure scenario.** The motivating use case is host→vuln enrichment (Qualys-style QID lookup) over a large scan — precisely the long-running job most likely to be interrupted and resumed. A pod restart three-quarters through the merge silently drops the last quarter of enriched records and reports success.

**Why the per-chunk checkpoint can't be made correct as-is.** It carries no chunk cursor (`forEachIndex` is always `0` at `:475`), so it can neither prevent re-publication nor locate where to resume — and the held records + run-scoped `BoundedKeyCache` are inherently non-serializable. `merge_into` is fundamentally a single-invocation construct.

**Recommended fix (proportionate, v1).** Make the target→merge span non-resumable so the outcome is a deterministic full re-run rather than silent partial loss. Simplest: when `workflow.Stages` contains any `merge_into`, ignore the resume checkpoint in `YamlOperationRunner` (force restart, same path as the existing fingerprint-mismatch branch at `:209`), and drop the per-chunk advancing checkpoint at `:475` (keep published-count reporting only if it does not advance `CompletedStages` past the target). This is a local patch, not a refactor. If genuine mid-merge resumability is wanted later, it needs persisted merge progress — out of scope for v1.

Note: the `WorkflowRunner` class doc (`:36`) claims "a resumed run restarts the workflow from the first stage," which contradicts the active skip-completed-stages logic. Align the doc with reality as part of this fix.

---

## 2. [Major] `BoundedKeyCache` eviction during a single chunk's fetch silently drops enrichment

**File:** `Workflow/WorkflowRunner.cs:432-475` (`ProcessMergeChunkAsync`), `:558-585` (`BoundedKeyCache`).

**What breaks.** Per chunk: step 1 collects uncached distinct keys into `needed`; step 2 fetches them into the FIFO-evicting cache; step 3 embeds by `cache.TryGet(key)`. If the number of distinct keys resolved for the chunk exceeds `cacheSize`, keys fetched earlier in step 2 — **or keys carried over from a previous chunk** — are evicted before step 3 runs, so `TryGet` misses and the record is treated as unmatched. With `unmatched: keep` the field is silently omitted; with `unmatched: drop` the record/element is silently removed.

**Failure scenario.** Two triggers, both silent:
- Misconfiguration: `cache_size` set below `page_size` (schema only enforces `minimum: 1`). Nothing validates the relationship.
- Array anchors amplify keys: `page_size` records × N array elements each can produce far more distinct keys than `page_size`, so even the default `cache_size: 10000` with `page_size: 500` can be exceeded on wide `DETECTION_LIST[]`-style records. The existing eviction test (`Cache_SizeBound_EvictsOldest_ReFetches`) uses `page_size: 1`, so exactly one key is live per chunk — it never exercises intra-chunk eviction and gives false confidence.

**Recommended fix.** Decouple correctness from the cross-chunk optimization: resolve the current chunk's keys into a plain, non-evicting `Dictionary` that lives for the chunk, and use `BoundedKeyCache` only to *avoid re-fetching* across chunks. A chunk's own resolved keys must never be evicted before that chunk is embedded. (A load-time `cache_size >= page_size` guard is necessary but not sufficient because of array-anchor amplification, so prefer the per-chunk map.) Local patch to `ProcessMergeChunkAsync`.

---

## 3. [Minor] `ExtractArray` object→single-element coercion is unconditional and changes behavior for JSON callers too

**File:** `Mapping/JsonPathHelper.cs:84-86`; blast radius: `Mapping/ResponseMapper.cs:37` (primary records path) and `IntegrationEngine.cs:1542` (id-list path).

**Problem.** The coercion is justified for XML→JSON (a single repeated element renders as an object), but it lives in a shared helper used by all-JSON responses. A JSON `records_path` that points at a wrapper object (a misconfig) previously produced 0 records — a loud, easily-caught failure — and now emits that whole wrapper as one "record," i.e. quiet wrong data downstream.

**Impact.** Low real-world likelihood (configs are authored and tested), but it converts a loud failure into a quiet one on the hottest path.

**Fix.** Either scope the coercion to XML-origin responses (thread the origin flag), or accept it and document that a non-array `records_path` now yields a single record. Local patch. Deferrable.

---

## 4. [Minor] `follow_url` (and any cross-host next-page URL) forwards vendor auth to whatever host the response body names

**File:** `Pagination/BodyCursorPaginator.cs:38-44`, `:109-120`.

**Problem.** In follow-url mode the next request URI is taken verbatim from the vendor response body (guarded only to be an absolute http/https URL). If the session applies auth headers/API keys per request regardless of host, a compromised or malicious vendor response can point the next page at an attacker-controlled host and exfiltrate credentials. This is the same latent property as link-header pagination, but worth stating for a security product.

**Fix.** Constrain follow-url (and link-header) continuations to the integration's `base_url` host / same origin unless there's a documented reason to cross hosts. Confirm whether the `IHttpSession` re-applies auth on a swapped `RequestUri`. Observation-grade unless auth is host-agnostic — worth a one-line check.

---

## 5. [Observation] No negative caching → repeated re-fetch of unmatched keys across chunks

**File:** `Workflow/WorkflowRunner.cs:449-457` (`FetchIntoCacheAsync`).

A key with no source record is never recorded as "resolved," so `cache.Contains` stays false and it re-enters `needed` in every subsequent chunk it appears in. For a common unmatched key spread across many pages this is redundant vendor calls (remote calls dominate cost here). Minor efficiency; consider caching misses (e.g. a sentinel) if profiling shows it.

---

## 6. [Observation] `{{keys}}` comma-join is ambiguous for keys containing commas

**File:** `Workflow/WorkflowRunner.cs:451` (`string.Join(",", batch)`).

Vendor IDs rarely contain commas, but `CanonicalKey` admits arbitrary strings. If a key can contain `,`, the source op receives a mis-split batch. Low risk; note the assumption.

---

## 7. [Nit] `integration.schema.json` diff is ~95% whitespace reformatting mixed with the real additions

The schema file shows 1075 changed lines, almost entirely a one-property-per-line re-expansion of unchanged content. The substantive additions (`merge_into` block, `body_cursor` fields, `$self` self-envelope in `mappingValue`, cache/page/batch sizing) are buried in it. Not a correctness issue, but it defeats reviewable diffs and risks masking a real schema change in future edits. Consider committing formatting separately from semantics.

---

## What's solid (no action)

- Egress streaming is preserved end-to-end: held target read lazily from the NDJSON spill file (`EnumerateRecordsAsNodes`, `:600-618`), chunk memory bounded by `page_size`, source cache bounded by `cache_size` — no full-dataset materialization.
- `body_cursor` regex is timeout-guarded (1s) and compiled-once/cached via `DelayResolver.GetRegex`, with `RegexMatchTimeoutException` handled as clean stop; follow-url values validated to absolute http/https.
- `CanonicalKey` handles number/string cross-type joins correctly, with explicit tests both directions.
- Loader `merge_into` validation is fail-fast, semantic (earlier-target, target-has-topic, merge-has-no-topic, `on` parses, single-merge-per-target) and well covered.
- Fully sequential execution — no concurrency/shared-mutable-state hazards.
- `$self` clones `JsonElement`/nested nodes so the serializer emits real JSON structure rather than escaped strings; the `except` path is straightforward.
- Test suite is thorough (540 pass) across anchor kinds, unmatched keep/drop, batching, canonicalization, and checkpoint state hygiene.

---

## Verdict

**needs-work** — the implementation is clean, idiomatic, and well-tested, but `merge_into` is wired into an active resumable path where a mid-merge re-invocation silently drops the un-published remainder of the dataset (#1), and intra-chunk cache eviction can silently drop enrichment on wide array anchors or a low `cache_size` (#2). Both are silent data loss under realistic conditions on a production vuln collector. Both have small, local fixes (force-restart for merge workflows; a non-evicting per-chunk key map). Address #1 and #2 before merge; #3–#7 are deferrable.
