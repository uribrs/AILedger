# Does Falcon FQL `last_seen_timestamp:>=` match a host whose `last_seen_timestamp` is null?

**No. Confirmed live, lab tenant, 2026-08-30.**

| query | vendor `total` |
|---|---|
| `/discover/queries/hosts/v1`, no filter | **303** |
| `last_seen_timestamp:>='2000-01-01T00:00:00Z'` | 298 |
| `last_seen_timestamp:>='2020-01-01T00:00:00Z'` | 298 |
| `last_seen_timestamp:!null` | HTTP 400 |
| `last_seen_timestamp:null` | HTTP 400 |

The 5-asset gap is exactly the 5 assets the entity probe found with a null
`last_seen_timestamp` (`research/lab-tenant-identity-probe.md`). A floor of 2000-01-01 excludes
them just as a floor of 2020-01-01 does, so this is null-exclusion, not a date boundary. FQL
offers no null predicate on this field — both spellings are rejected outright — so those assets
cannot be recovered by widening or OR-ing the filter.

## What it means

`FalconHostFilters.BuildLastSeenTimestampGateFilter` (`:19-23`) composes this gate. It is applied
on every re-anchor (`FalconHostSpooler.cs:254`, `:319`) and from the first request on any run
carrying a base date. So:

- On an unfiltered run that never re-anchors, all 303 are reachable.
- On any run that re-anchors — cursor expiry, mid-scroll 5xx, depth cap — every asset with a null
  `last_seen_timestamp` that had not yet been served is lost for that run, silently.
- On any run with a base date, they are never returned at all.

## Disposition

This is a **third, independent defect** inside the contract's own premise ("no asset Falcon
returns may be dropped at any stage"). It is NOT addressed by either defect this task fixes, and
fixing it is not a small change: there is no FQL predicate that admits nulls, so it needs a
different traversal (a second unfiltered pass, or dropping the gate and filtering client-side),
which touches the spool's design — explicitly out of bounds under `constraints.md`.

Recorded, measured, not fixed. Assumption A17. Scale on the lab tenant: 5 of 303, ~1.6%.
