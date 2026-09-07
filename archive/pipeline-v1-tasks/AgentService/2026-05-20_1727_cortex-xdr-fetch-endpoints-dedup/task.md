# Task

Fix duplicate emission of Cortex XDR endpoints during agent collection.

## Symptom

In on-disk batch-staging files captured from a production agent (`findings_001.ndjson` … `findings_005.ndjson`, 990 rows total across five files just under the 30 MB byte cap), 42 of 953 unique endpoints appear exactly twice. Duplicates are byte-identical (same `aid` / `endpoint_id`, same `last_seen`, same vulnerability list). The duplicate rows form three tight clusters with constant intra-cluster offsets — 7, 6, 6 — all inside `findings_002.ndjson`. Example: `AWCD078` (`aid b5d4ace03e384c46bf6e9ba7f76f0490`) appears at rows 42 and 49.

## Root cause

`PaloAltoCortexApiBase.FetchEndpointsAsync` (`Source/Application/Cymulate.Agent.Application.Actions/Actions/QueryIntegration/Logic/Clients/BaseApiClients/PaloAltoNetworks/PaloAltoCortexApiBase.cs:83-190`) paginates the Cortex XDR `/public_api/v1/endpoints/get_endpoint` API by ordinal offset (`search_from` / `search_to`, page size 100) over a server-side sort of `"field": "last_seen", "keyword": "DESC"`. `last_seen` is mutable; when live agents check in between consecutive page fetches their `last_seen` is bumped, they rise in the sort, and every endpoint below them shifts back by one position. The result: the last N items of page K reappear as the first N items of page K+1. The `+7 / +6 / +6` cluster pattern is exactly the count of agents whose `last_seen` advanced past each page boundary during this collection.

The collector has no per-walk dedup, so each duplicated endpoint produces a duplicate downstream row in both the assets flow and the findings flow.

## Out of scope

- Switching sort field away from `last_seen` (would defeat CA-48775's incremental walk optimization, the `lastSeenDate < iBaseDate` early-break).
- The related "premature early-break" risk (a drifted row with a stale `last_seen` triggering `shouldContinue = false` earlier than the true tail) — flagged for follow-up only.
- Cleanup of the collector findings temp dir (already handled by `CybiActionManager.DeleteCollectedDataBeforeZipping`).
- Any change to `CybiBatchUploader` or to the batching loop in `CortexXdrCollector` (both verified innocent end-to-end).
