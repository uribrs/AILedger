# Execution Notes

## 2026-05-20 — Initial execution (HashSet dedup, since reverted)

Implemented per the original contract: in-method `HashSet<string>` dedup inside `PaloAltoCortexApiBase.FetchEndpointsAsync`, plus a regression test `XdrApiPaginationTests.cs`. Built + tested green. Committed by the operator to branch `fix-cortexxdr-duplicat-asets-pull` (commit `056dfab3d`) for testing.

## 2026-05-20 — Operator redirect

The operator rejected the HashSet approach:
1. The base class fix surface has uncertain blast radius on other consumers, even though current usage is limited to `CortexXdrCollector`.
2. The dedup only fixed duplicates, not silent skips. Operator wanted a fix that addressed both.
3. After discussion: switch to `endpoint_id DESC` sort (immutable key → stable pagination → both failure modes eliminated by construction). Drop the `iBaseDate` filter entirely — "collect everything, enrich everything, no removals." Implement in the collector, not the base.

Recorded as decisions D11–D13 and assumptions A9, A10. D9 and D10 from the previous design were marked superseded.

## 2026-05-20 — Final execution

**Files changed**

| File | Change |
|---|---|
| `Source/Application/Cymulate.Agent.Application.Actions/Actions/QueryIntegration/Logic/Clients/BaseApiClients/PaloAltoNetworks/PaloAltoCortexApiBase.cs` | Reverted to pre-change state (HashSet dedup + comment removed). |
| `Source/CybiCollectors/CortexXdrCollector/CortexXdrCollector.cs` | Added private `FetchEndpointsSortedByEndpointIdAsync` (mirrors base method body with `"field": "endpoint_id"` and no early-break). Replaced all three `FetchEndpointsAsync` call sites in this file (two in `CollectAssetsAsync`, one in `dumpEndpointsToTempAsync`). Removed local `baseDate` variables in `CollectAssetsAsync` and `CollectFindingsAsync`. Dropped `iBaseDate` parameter from `dumpEndpointsToTempAsync`. Updated log message in `CollectFindingsAsync` to drop "baseDate window" wording. |
| `Source/CybiCollectors/CortexXdrCollector/_Usings.cs` | Added `global using System.Net.Mime;` and `global using System.Text;` (required by the new method's `StringContent` constructor). |
| `Tests/Application/Cymulate.Agent.Application.Integrations.Tests/EDR/XdrApiPaginationTests.cs` | Deleted. Both tests exercised behavior (HashSet dedup) that no longer exists. No replacement test added in this change. |

**Commands run**

- `dotnet build Source/CybiCollectors/CortexXdrCollector/CortexXdrCollector.csproj /p:Configuration=DebugMac /p:Platform=x64 /p:DefineConstants="DEBUG%3Bx64%3BMac"` → Build succeeded, 0 errors, 0 warnings.
- `dotnet build Tests/Application/Cymulate.Agent.Application.Integrations.Tests/Cymulate.Agent.Application.Integrations.Tests.csproj /p:Configuration=DebugMac /p:Platform=AnyCPU /p:DefineConstants="DEBUG%3BMac"` → Build succeeded, 0 errors, pre-existing warnings only.
- `dotnet test ... --filter "FullyQualifiedName~XdrApi"` → 5 passed, 3 pre-existing skips, 0 failed.

**Behavior delta vs pre-change**

| Aspect | Before | After |
|---|---|---|
| Endpoint pagination sort | `last_seen DESC` (mutable; drift-prone) | `endpoint_id DESC` (immutable; stable across pages) |
| Pagination termination | Either early-break at `last_seen < iBaseDate` or short page | Short page only (full-tenant walk) |
| Stale-endpoint emission | Filtered out via early-break | All endpoints emitted, regardless of `last_seen` |
| Duplicates from drift | Present (~2% observed in `simantec_2`) | Eliminated by construction |
| Silent skips from drift | Present (~2% inferred) | Eliminated by construction |
| Code surface touched by fix | Base class shared by all Palo Alto Cortex APIs | Collector only |
| Per-cycle page-fetch count | `min(walk-back-to-baseDate, full-tenant)` | Always `ceil(tenant-size / 100)` |

**Residual risks**

- **Tenant-version compat (A7).** Older Cortex backends (pre-March-2026) may reject `"field": "endpoint_id"` with `400 Bad Request`. No probe-or-fallback was implemented. If a customer's tenant rejects the new request, asset collection will hard-fail for them until they're on a newer backend or a fallback is added.
- **Bandwidth.** Every collection cycle now walks the full tenant. For large tenants (~50k endpoints) this is ~500 page fetches per cycle vs. potentially ~10 on the previous incremental walks. No cycle-cadence change was made; if customer-facing teams need throttling, that's a separate change.
- **`iBaseDate` is now dead-parameter inside the collector** even though it's still part of the `IAsyncAssetsCollector` / `IAsyncFindingsCollector` interfaces. No compiler warning (C# doesn't flag unused method parameters by default). Future readers may be confused about why a `baseDate` is passed in but never used; the comment on `FetchEndpointsSortedByEndpointIdAsync` documents the divergence.

## 2026-05-20 — Dry-run regression caught and fixed

Operator flagged: "dry run preserved?" — and they were right to. The original `FetchEndpointsAsync` relied on `iBaseDate = DateTime.UtcNow` to trigger the early-break on the first endpoint, giving dry-run a "probe one row and stop" semantics. My initial divergence dropped that early-break entirely, which would have made dry-run do a full-tenant walk — wrong.

Fix applied: `FetchEndpointsSortedByEndpointIdAsync` now takes a `bool iIsDryRun` parameter. After the first `await iOnEndpointAsync(endpoint).ConfigureAwait(false);` call, when `iIsDryRun is true`, the method returns immediately. All three call sites in `CortexXdrCollector` updated to pass `iIsDryRun` through. `dumpEndpointsToTempAsync` also gained a `bool iIsDryRun` parameter.

Build is still clean (0 errors, 0 warnings).

**Open items**

- Branch: operator's current branch is `fix-cortexxdr-duplicat-asets-pull`. The remaining changes after the operator's `056dfab3d` commit are uncommitted — they constitute the revert + new design. Operator decides commit/push strategy.
- Verifier subagent (S7) and code-reviewer subagent (S8) per the orchestration plan have NOT been run yet. The substantial scope change (revert + reimplement) means the contract docs and the executed code have just been re-aligned; a verifier pass against the updated contract is warranted before code-reviewer.
- Adapter repo (`cymulate-integration-adapters`): same bug exists there. Operator already tried the partial fix locally (sort-field swap without dropping the early-break) — that fix is INCOMPLETE because the adapter's `ReachedCutoff` early-break is wrong under `endpoint_id` sort. A sibling fix in the adapter repo is needed for symmetry. Not in scope of this task.
