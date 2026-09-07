# Live probe: what identity fields Discover actually returns

Run 2026-08-30 from the main thread against the lab tenant with the operator's own
`appsettings.local.json` credentials. Read-only: `/oauth2/token`,
`/discover/queries/hosts/v1` (offset-paginated, unfiltered), `/discover/entities/hosts/v1`.
Full inventory, not a sample: 303 ids queried, 303 unique, 303 entities hydrated.

## Result

| population | count |
|---|---|
| Discover assets, unfiltered | 303 |
| `entity_type = managed` | 49 |
| `entity_type = unmanaged` | 210 |
| `entity_type = unsupported` | 44 |
| explicit `aid` or `device_id` present | 49 |
| AID derivable from the combined `id` 32-hex suffix | **0** |
| **no AID derivable by any current path** | **254** |

Field presence across all 303:

| field | present |
|---|---|
| `id` | 303 / 303, **all unique** |
| `current_local_ip` | 296 |
| `local_ip_addresses` | 296 |
| `mac_addresses` | 296 |
| `last_seen_timestamp` | 298 |
| `hostname` | 227 |
| `platform_name` | 50 |

A representative AID-less entity:

```json
{
  "id": "8884df8d8f704f43b23dad2e90572984_ASCeLr0LF_zz3dbEFdK-uwHZ4SmMawFCqU54WNcp8aZhuyQL6kxcBfPN",
  "entity_type": "unmanaged",
  "hostname": "TROASTME$",
  "current_local_ip": null,
  "platform_name": null,
  "last_seen_timestamp": null
}
```

## What this overturns

**The handoff's proposed mechanism does not work.** `AidExtractor.ExtractAid`
(`Flows/Findings/Hosts/AidExtractor.cs:71-83`) falls back to
`TryExtractAidFromCombinedHostId`, which returns the combined `id`'s suffix **only when that
suffix is exactly 32 hex characters**. On this tenant the suffix is a 56-character
base64url-style token — see the sample above. The gate rejects it, `ExtractAid` returns null,
and `FalconDiscoverHostScroller.cs:82-86` drops the asset.

So the derived-AID path yields a key for **0 of the 254** lost assets. The earlier
"1 managed + 20 unenriched → 21 unique derived AIDs" observation cannot have come from this
population; it must have come from entities whose `id` suffix happened to be 32-hex.

The handoff's own warning stands and is reinforced: emitting null collapses all 254 into one
Spark partition. There is simply no existing extractor that produces a usable key for them.

## What this establishes instead

The combined `id` is present on every asset and unique across the whole inventory. It is the
natural record key for an AID-less asset, and it requires no derivation, no suffix parsing, and
no length or charset gate that a future tenant can fail.

`ExtractSensorAid` stays authoritative and stays null for these 254, so the policy lane
(`/devices/entities/devices/v2`, which rejects non-AID ids outright — see the docstring at
`AidExtractor.cs:32-44`) is already correct and needs no change. The split between
"record key" and "sensor id" that this fix needs is already the split the type carries:
`DiscoverHost(aid, host, lastSeenUtc, sensorAid)` at `FalconDiscoverHostScroller.cs:89`.

## Consequences to carry into the plan

- The record key for an AID-less asset must be the Discover combined `id`, not a derived AID.
- A key of that shape must never reach the Spotlight `aid:[…]` FQL filter or
  `/devices/entities/devices/v2`. Both already gate on `ExtractSensorAid`, which is null here.
- `last_seen_timestamp` is null on 5 of 303. The spooler's boundary-duplicate check
  (`FalconHostSpooler.cs:204`, `IsBoundaryDuplicate(h, watermarkUtc, boundaryAids)`) and the
  watermark advance both read that field, so a null-last-seen host is a real input, not a
  hypothetical.
- `hostname` is present on only 227 of 303, so no plan may fall back to hostname as an identity.
