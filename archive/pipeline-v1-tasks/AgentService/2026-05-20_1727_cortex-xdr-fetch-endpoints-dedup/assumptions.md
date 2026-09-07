# Assumptions

## A1 — Single Cortex XDR collector consumes `FetchEndpointsAsync`
- Status: **VALIDATED**
- Evidence: `grep -rn "FetchEndpointsAsync" Source --include="*.cs"` returns matches only in `PaloAltoCortexApiBase.cs` (definition), `XdrApi.cs` (subclass), `CortexXdrCollector.cs` (two call sites: assets `:83` and findings via `dumpEndpointsToTempAsync`). No other collector inherits `XdrApi`.
- Implication: A change inside `FetchEndpointsAsync` has bounded blast radius.

## A2 — `endpoint_id` is the stable Cortex agent identifier and is always present on get_endpoint responses
- Status: **VALIDATED**
- Evidence: The duplicate rows captured in `simantec_2/findings_002.ndjson` (e.g., `AWCD078`) carry identical 32-char hex `aid` values across both copies; mapping code in `CortexXdrCollector.MapAsset` (`:478`) treats `endpoint["endpoint_id"]` as the primary key for downstream `finding_ids` lookups (`fetchFindingIdsByEndpointIdAsync`, `:485-544`). The dedup-via-`buildFindingId` helper also keys on `endpoint_id` (`:731`).
- Implication: Safe to dedup on this single field. Whitespace/null guard still required as a defensive measure.

## A3 — Live agents checking in between paginated requests is the primary drift driver
- Status: **VALIDATED**
- Evidence: Duplicate clusters are tight runs of consecutive endpoints with constant intra-cluster offset (+7, +6, +6) — matches the "N endpoints rose past a page boundary" signature. Ties on `last_seen` alone would produce a sparser, lower-volume pattern.
- Implication: Per-walk dedup neutralizes the drift; no need to chase ties separately.

## A4 — Existing test project covers `PaloAltoCortexApiBase`
- Status: **VALIDATED**
- Resolution: Host project is `Tests/Application/Cymulate.Agent.Application.Integrations.Tests/`. Existing class `EDR/XdrApiTests.cs` extends `IntegrationProductsTestsBase` and already wires `A.Fake<IHttp>()` with sequenced `A.CallTo(...).Returns(...).Once().Then.Returns(...)` fakes — the same pattern needed for the pagination drift test. Sibling tests under `Abstractions/PaloAltoNetworks/` confirm parser/reader/executor coverage exists for the base class family.
- Implication: Regression test lands in `Tests/Application/Cymulate.Agent.Application.Integrations.Tests/EDR/`. No new project needed.

## A5 — Logging at Info for skipped duplicates is acceptable noise
- Status: **OPEN**
- Resolves to: VALIDATED unless verifier or code-reviewer flags log spam concerns for tenants with many drifted endpoints (e.g., 50+ duplicates per run). In that case downgrade to a single aggregated count line at end-of-walk.
- Implication: Tradeoff between debuggability and log volume; default to per-occurrence Info because the volume in observed production data was 42 duplicates per ~1000-endpoint walk, which is well within Info-level acceptable noise.

## A6 — Branch for the change
- Status: **OPEN**
- Current branch is `CA-71900-harmonyendpiont-cylanceoptics-to-time-range`, unrelated work. The contract requires leaving changes uncommitted so the operator can decide whether to land this on its own branch (e.g., `CA-NNNNN-cortex-xdr-endpoint-dedup`) or amend the current line of work.
- Plan: Executor does not invoke `git` beyond read-only inspection. Operator stages and commits.

## A7 — `sort.field` on `get_endpoint` accepts an immutable identifier (`endpoint_id`)
- Status: **VALIDATED** (medium-high confidence; tenant-version-gated)
- Resolution: Cortex XDR marketplace changelog v6.3.11 (2026-03-11) added `endpoint_id` to the `sort_by` argument of the XSOAR `xdr-get-endpoints` command, which maps 1:1 to the REST API's `request_data.sort.field`. The change implies the backend started accepting it on or before that date. Older tenant backends will likely reject it with `400 Bad Request`. Research file: [research/cortex-xdr-get-endpoint-sort-fields.md](./research/cortex-xdr-get-endpoint-sort-fields.md).
- Implication: An integrity fix (no duplicates AND no skips) is reachable by switching sort to `endpoint_id ASC` after a one-shot tenant probe. Cost: lose the `lastSeenDate < iBaseDate` early-break — walks become full-tenant. Acceptable trade for correctness but is a separate change beyond the current contract's scope.
- Follow-up: open a sibling task for "tenant-probed sort-key switch + full-walk fallback" once this PR ships.

## A8 — The current fix (HashSet dedup) does NOT address silent skips
- Status: **VALIDATED** (high confidence; mechanical consequence of pagination drift)
- Resolution: For every endpoint emitted twice via drift, a different endpoint that was *about to be emitted* is silently lost (the bumped agent that jumped to position 0 sits forever in a window we've already paged past). The 19 duplicates observed in the simantec_2 dump imply ~19 endpoints missing from the same collection. The HashSet catches the duplicate cost but not the skip cost.
- **Note (2026-05-20 redirect):** The operator chose to address skips directly via the [[A7]] sort-key switch (now implemented in the collector per D11), not through the dedup band-aid. With `endpoint_id DESC` sort and full-tenant walk, both duplicates and skips are eliminated by construction.

## A9 — Walking the full tenant every cycle is operationally acceptable
- Status: **VALIDATED** (operator-accepted)
- Reason: Operator chose D12 ("collect everything") over the incremental-walk optimization. For small tenants (~1k endpoints) the cost is ~10 page fetches per cycle. For very large tenants (~50k) it's ~500 page fetches per cycle, which exceeds the previous incremental-walk floor by a wide margin. Trade-off accepted in exchange for guaranteed completeness and simpler control flow.
- How to apply: code-reviewer should not flag the dropped early-break as a missed optimization; it is intentional. If tenant scale becomes a problem operationally, the right place to mitigate is at the cycle-scheduling layer (longer cadence between collections), not by reintroducing the early-break.

## A10 — Diverging from base class is preferred over modifying it
- Status: **VALIDATED** (operator-stated principle)
- Reason: "we must diverge from the base ... we can't foresee what cascading effect it will have over its other consumers." Although a grep confirms only `CortexXdrCollector` currently uses `FetchEndpointsAsync`, the base class is a generic over `TApiInfo` (`XdrApiInfoModel` and any future `XsiamApiInfoModel`-style siblings), and a behavior change at that layer is a wider blast radius than the operator wants to take in this change.
- How to apply: do not refactor `PaloAltoCortexApiBase.FetchEndpointsAsync` to accept a configurable sort field or filter. Local divergence in `CortexXdrCollector` is the correct architectural answer for this collector's special requirements.

## Rejected alternatives

- **Switch sort field to `first_seen` or `endpoint_id`**: rejected. Defeats the `lastSeenDate < iBaseDate` early-break that CA-48775 introduced for incremental walks. Reverts a deliberate optimization.
- **Dedup at `emitFindingsFromTempAsync` in `CortexXdrCollector`**: rejected. Doesn't cover the assets path, doesn't prevent the wasted disk/CPU of writing duplicates into `endpoints.jsonl`, and places the dedup downstream of where the duplicate enters the system.
- **Server-side keyset cursor (`search_after`)**: rejected. The Cortex XDR `get_endpoint` API accepts only ordinal `search_from`/`search_to`. No keyset variant exists per the current implementation.
