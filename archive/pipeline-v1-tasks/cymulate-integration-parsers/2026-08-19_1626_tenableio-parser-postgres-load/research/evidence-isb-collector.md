# W3 — ISB collector run evidence

All timestamps **UTC** unless explicitly labelled. `aws s3 ls` output was not used for any clock
claim — every S3 time below comes from `list-objects-v2` / `head-object`, which return UTC.

## Method, surfaces reached, surfaces unreachable

Followed the `isb-run-triage` skill.

Reached:
- **Elastic `logs-*` via k8s-agent MCP `es_esql`, environment `stg`** — the ISB structured run log for
  this correlation id IS in `logs-*` for stg (unlike rfqa). Pod
  `integration-service-bus-694b8ff4df-62mkd`.
- **AWS S3 reads** (`s3://cybi-data`, `s3://integration-service-bus`), account 118330362824,
  us-east-1. Creds valid (`AAD-Platform/urib@cymulate.com`).
- **Secrets Manager read** of `mongo-atlas-eks-stg-yH52mK` — succeeded; it yields a
  `mongodb+srv://eks-stg.yufxk.mongodb.net/...authMechanism=MONGODB-AWS` string (no username/password;
  auth is IAM).

Unreachable — **no Mongo run document was read, and none is quoted or reconstructed below**:
- `mcp__plugin_mongodb__list-connections` → `{"connections":[]}`; two `connect` attempts with the
  secret's connection string returned the generic *"You need to connect to a MongoDB instance"* error.
- Direct read attempt with `pymongo` 4.16.0 (present in the repo `.venv`) against the same string:
  `pymongo.errors.OperationFailure: Authentication failed., code 18, AuthenticationFailed`. The Atlas
  cluster authenticates by IAM (`MONGODB-AWS`) and my SSO role is not a mapped Atlas user. Stopped
  that line of enquiry there.
- `s3api list-object-versions` on `integration-service-bus` → `AccessDenied`
  (`s3:ListBucketVersions` not granted), so I could not build an upload history of the adapter zip.

Partial-availability caveat: the stg Elastic cluster (`es-nonprod`) intermittently returned
`request to ES cluster 'es-nonprod' timed out` for any window wider than roughly 5 hours with a
`message LIKE` wildcard. Narrow windows worked. Consequence: the 14-day run history below is built
from **S3 prefix evidence**, not from a 14-day log scan (see §6 and Caveats).

## 1. Run identity (Mongo _id 6a8593320f3d4281bd8a0df0)

Mongo not reachable (above) — identity is established from the ISB log instead, and it is
unambiguous: the correlation id is carried by the RabbitMQ trigger message and by every run line.

ObjectId timestamp decode: `int('6a859332',16)` → **2026-08-19T11:27:46Z**. The trigger payload's own
`timestamp` field is `1787138870061` → **2026-08-19T11:27:50.061Z**.

```
11:27:50.077Z  RabbitMQ message payload received. Queue: collectors.run, Size: 1054 bytes,
               TenantId: (none), Payload: {"topic":"collector","vendor":"Tenable.io",
               "correlationId":"6a8593320f3d4281bd8a0df0","payload":{[REDACTED],
               "lastRanAt":"2026-08-19T00:00:00Z","flows":["CollectFindings"],"useYaml":false,
               "metadata":{"instanceId":"21f14fe6-ca31-42a7-87aa-c343bbbb777d",
               "clientID":"613f3842d8ddf900117fccfa",
               "clientIntegrationId":"d6d4258a-7671-4268-aaca-33c44d388f55",
               "clientIntegrationFlowId":"c4c23289-c1bb-4045-8b9d-2a6950056e9b",
               "integrationSettingId":"0c90af0f-e52e-4808-a728-a62f9bce94ec",
               "integrationSettingFlowId":"6c4fdda1-4e33-404f-b59f-8666b0271d12",
               "storageUrl":"stg/raw-data/613f3842d8ddf900117fccfa/0c90af0f-e52e-4808-a728-a62f9bce94ec/21f14fe6-ca31-42a7-87aa-c343bbbb777d"}},
               "timestamp":1787138870061}
11:27:50.092Z  Processing trigger flow message for TenableIo. Flows: [CollectFindings], CorrelationId: 6a8593320f3d4281bd8a0df0
11:27:50.212Z  Collection started: EventId=913c6761-513e-4fdd-9a54-daa2d0bd77d4, Platform=TenableIo,
               Category=Collectors, Client=613f3842d8ddf900117fccfa, CorrelationId=6a8593320f3d4281bd8a0df0
11:27:50.248Z  RUN metadata resolved. Vendor=Tenable.io Topic=CollectFindings
               StorageUrl=stg/raw-data/613f3842d8ddf900117fccfa/0c90af0f-e52e-4808-a728-a62f9bce94ec/21f14fe6-ca31-42a7-87aa-c343bbbb777d
               CorrelationId=6a8593320f3d4281bd8a0df0
```

Terminal outcome (same pod):

```
12:39:07.574Z  Publishing completion event. CorrelationId: 6a8593320f3d4281bd8a0df0,
               Category: Collectors, Success: True, Items: 17984, Pages: 2112
12:39:07.587Z  Published done event to collectors.done. CorrelationId: 6a8593320f3d4281bd8a0df0,
               TenantId: (none), Status: success, TotalAssets: 17984
12:39:07.593Z  Adapter final outcome. Success=True Message=Tenable.io collector completed.
               Flow=CollectFindings Total=1950862 TotalAssetsCollected=17984
               TotalFindingsCollected=1950862 DryRun=False
               FlowLifecycle={"shutdownRequested": false} Failure=null
               CorrelationId=6a8593320f3d4281bd8a0df0
12:39:07.605Z  Collection completed: EventId=913c6761-513e-4fdd-9a54-daa2d0bd77d4, Platform=TenableIo,
               Category=Collectors, Client=613f3842d8ddf900117fccfa, Duration=4277383.2296ms,
               CorrelationId=6a8593320f3d4281bd8a0df0
12:39:07.607Z  Successfully handled trigger flow message, acknowledging. Platform: TenableIo,
               CorrelationId: 6a8593320f3d4281bd8a0df0
```

Summary: **status success**, start **11:27:50Z**, end **12:39:07Z**, duration **4,277,383 ms =
71 min 17 s** (matches wall clock), **no partialSuccess flag, `Failure=null`, no error**. The
collector ack'd the RabbitMQ message and fired `collectors.done` — which is what put the Glue parser
on the incident path at 12:41:25Z.

## 2. Adapter and version

**Native adapter, not the YAML engine.** The routing decision is explicit and it is driven by the
trigger payload's `useYaml: false`:

```
11:27:50.095Z  Collector run stays on the native collector: useYaml is disabled.
               CorrelationId: 6a8593320f3d4281bd8a0df0
11:27:50.211Z  [ADAPTER_ACTIVATED] tenable-io-collector for platform TenableIo (clientId: default)
               — assembly: 6.3.0.7, file: 6.3.0.7, info: 6.3.0+0db327314192ba97ae00aa4b4d2e8ee33f04cf59
```

Staleness check (skill-mandated):
`head-object s3://integration-service-bus/stg/Collectors/TenableIoCollector/default/TenableIoCollector.zip`
→ `LastModified 2026-08-18T15:01:50+00:00`, `ContentLength 1765528`. The activation at
2026-08-19T11:27:50Z is **after** the package upload, so the pod was **not** running a stale adapter.

## 3. What it published (counts, chunks, files, elapsed) — UTC

Two-phase publish. Phase A stages an asset "spine" (one JSON per asset) plus a manifest; phase B
streams correlated findings pages, each accompanied by a claims file.

Log evidence for the phase boundary and the first pages:

```
11:27:50.356Z  Creating Tenable.io vulns export. filters.since(unix)=1787097600
11:27:50.409Z  HTTP REQUEST POST https://cloud.tenable.com/vulns/export
11:27:50.626Z  HTTP RESPONSE 200 POST https://cloud.tenable.com/vulns/export | 216ms
11:27:50.752Z  Creating Tenable.io assets export. filters.last_assessed(unix)=1787097600
11:27:50.900Z  HTTP RESPONSE 200 POST https://cloud.tenable.com/assets/export | 146ms
11:27:50.902Z  Tenable.io correlated findings: exports in play.
               VulnsUuid=cbd0865d-8116-4083-8841-ba3c27351c29 AssetsUuid=b1895240-4795-48ba-b953-810a92618648
11:30:13.131Z  Publishing stream to s3://cybi-data/.../21f14fe6-.../findings_000001.json
11:30:13.784Z  Successfully published stream (1761224 bytes) to ...findings_000001.json in 653ms
11:30:13.793Z  Progress reported. Page: 2, Items: 4, Message: Page 1 completed with 4 items
```

`_staging/manifest.json` (189 bytes, S3 LastModified 2026-08-19T11:29:52Z), read verbatim:

```json
{"assetsExportUuid":"b1895240-4795-48ba-b953-810a92618648","stagedAssetCount":17956,
 "skippedChunkCount":0,"baseDateUtc":"2026-08-19T00:00:00Z","completedUtc":"2026-08-19T11:29:51.2923235Z"}
```

S3 object inventory of the publish prefix (`list-objects-v2`, 22,180 objects, 13,758,323,371 bytes
total = 12.81 GiB):

| family | key shape | files | bytes | first LastModified | last LastModified |
|---|---|---|---|---|---|
| asset spine | `_staging/spine/<uuid>.json` | 17,956 | 89,252,197 | 11:28:02Z | 11:29:52Z |
| manifest | `_staging/manifest.json` | 1 | 189 | 11:29:52Z | 11:29:52Z |
| findings pages | `findings_NNNNNN.json` | 2,112 | 13,668,418,677 | 11:30:14Z | **12:39:07Z** |
| claims | `_staging/claims/chunk_N.ids` | 2,111 | 652,308 | 11:30:13Z | 12:38:09Z |

Elapsed: asset staging 11:28:02 → 11:29:51 (**1 min 49 s**); findings streaming 11:30:13 → 12:39:07
(**68 min 54 s**); whole run 71 min 17 s.

What the collector *believes* it sent vs what is on S3 — they agree, with one small gap:

- believes: `Items: 17984` / `TotalAssetsCollected=17984`, `Pages: 2112`, `TotalFindingsCollected=1950862`
- on S3: `2112` findings page files (contiguous `findings_000001` … `findings_002112`, no gaps, no
  duplicates), `17956` spine files
- gap: **17,984 (completion) vs 17,956 (manifest `stagedAssetCount`) = 28 assets**. Unexplained;
  small enough not to matter for volume.

Derived shape of what the parser then had to read: 1,950,862 findings over 17,984 assets =
**108.5 findings per asset**; 13.67 GB / 1.95 M findings ≈ **7.0 KB per finding record**; mean page
file 6.47 MB (largest observed single page 13.0 MB).

Base publish path (paste-ready):

```
s3://cybi-data/stg/raw-data/613f3842d8ddf900117fccfa/0c90af0f-e52e-4808-a728-a62f9bce94ec/21f14fe6-ca31-42a7-87aa-c343bbbb777d/
```

## 4. Full vs incremental (cursor evidence)

**Incremental, with a cursor of midnight-today.** Three independent artefacts agree:

1. trigger payload: `"lastRanAt":"2026-08-19T00:00:00Z"`
2. Tenable export requests: `filters.since(unix)=1787097600` (vulns) and
   `filters.last_assessed(unix)=1787097600` (assets). `1787097600` = **2026-08-19T00:00:00Z**.
3. manifest: `"baseDateUtc":"2026-08-19T00:00:00Z"`

So the window was **11 h 27 m** wide (midnight → trigger), not open-ended, and not a full export.

Corroborating that it really was narrower than a full run: the asset filter admitted only **17,956**
assets, whereas both 2026-08-18 runs of the same flow staged **~101,000** assets (§6). That is the
same account one day apart, so the account's asset universe is ~101 k and today's run saw ~18 % of it.

Was the cursor *reset*? Cannot be answered from the ISB side: `lastRanAt` arrives ready-made in the
trigger from the caller, and ISB logs no prior-cursor value. Two observations bear on it:
- the value is exactly midnight, not a previous run's completion time — it looks like a floor/default
  rather than a carried-forward cursor;
- instance `21f14fe6-…` has **no S3 objects older than 11:28:02Z today**, and its prefix contains
  only this run — consistent with this being that instance's first collection.

**Speculation (labelled):** this was a first run for a newly created integration instance, so the
platform supplied the default "today midnight" cursor rather than a real last-run timestamp; and
Tenable's `vulns/export?since=` returns each matched asset's current vulnerability set rather than
only newly-changed findings, which is how an 11.5-hour window still yields 108 findings per asset.
Neither half of that is proven by anything I read.

## 5. Retry / requeue / double-publish evidence

Evidence **against** any retry, requeue, resume or double publish:

- `11:27:50.225Z  Adapter declined resume from checkpoint. CorrelationId: 6a8593320f3d4281bd8a0df0,
  Page: 0. Starting fresh.` — the run began at page 0 with no checkpoint replay.
- A scan of 11:27:00–12:45:00Z for `*Resume*` / `*retry-count*` / `*requeue*` on
  `service.name == "integration-service-bus"` returned **0 rows**.
- Exactly **one** `RUN metadata resolved. Vendor=Tenable.io …` line exists in stg for the whole of
  2026-08-19 00:00–16:00Z — the target run. No second Tenable.io collection ran that day, for this
  or any other client.
- On S3, `findings_000001` … `findings_002112` is contiguous with no duplicate or higher-numbered
  leftovers, every object's LastModified falls inside 11:28:02–12:39:07Z, and the completion line's
  `Pages: 2112` equals the file count. The parser therefore read exactly one run's output.

For contrast, `Resume cancelled. Vendor=Tenable.io Flow=CollectFindings` warnings *do* exist on
2026-08-18 (12:06:16Z, 12:51:24Z, 13:43:43Z, 13:55:08Z) — during the previous day's runs, not this one.

## 6. Run history for this client+instance (last 14 days, UTC) — volume trend

Elastic could not be scanned over 14 days (timeouts, see Method). History below is from S3:
`list-objects-v2` over `s3://cybi-data/stg/raw-data/613f3842d8ddf900117fccfa/0c90af0f-e52e-4808-a728-a62f9bce94ec/`
(238,205 objects), aggregated per instance prefix. Each prefix retains its instance's **most recent**
run, so this is a per-instance last-run volume series, not a per-run series.

| instance prefix | window (UTC) | staged assets | findings files | findings bytes | layout |
|---|---|---|---|---|---|
| **21f14fe6…bbb777d (target)** | 2026-08-19 11:28:02 → 12:39:07 | 17,956 (spine) | 2,112 | **12.73 GiB** | spine + claims |
| 251f9990…418c4ece | 2026-08-18 10:36:13 → 14:46:48 | 100,829 (spine) | 2,112 | 46.24 GiB | spine + claims |
| e2702554…39fd9109 | 2026-08-18 15:19:33 → 19:16:07 | 101,150 (spine) | 2,109 | 45.78 GiB | spine + claims |
| de004fc1…413fecb8 | 2026-07-12 13:57:22 → 14:02:07 | 101 `assets_*` files | 21 | 0.89 GiB | assets + findings (partial) |
| 2550a777…c13c57a46b | 2026-07-08 15:25:14 → 16:32:16 | 102 `assets_*` files | 984 | 42.64 GiB | assets + findings |
| 6c337dc8…3f92f38e0 | 2026-07-06 09:37:12 → 10:42:57 | 101 `assets_*` files | 982 | 42.61 GiB | assets + findings |
| d836a371…2dc4a8fb | 2026-06-21 14:16:56 → 15:24:56 | 102 `assets_*` files | 950 | 41.35 GiB | assets + findings |
| 43d2ff7e…77bf49375 | 2026-06-21 10:33:12 → 11:28:31 | 102 `assets_*` files | 953 | 41.57 GiB | assets + findings |
| 30e3cb9a…7ba926be | 2026-06-14 10:33:13 → 11:34:24 | 102 `assets_*` files | 936 | 40.87 GiB | assets + findings |
| 04e2fafd…d797225 | 2026-05-04 14:03:38 → 15:20:46 | — | 100 | 3.12 GiB | findings only |
| ~35 other prefixes | 2026-01-19 … 2026-07-20 | — | 1–3 files | ≤0.05 GiB | connection-test sized |

Within the last 14 days there are therefore **three** substantive collections under this
client+integration-setting (2026-08-18 ×2, 2026-08-19 ×1, each on a different instance id), and
**one** for the target instance.

Step changes, with dates:

- **2026-08-18 10:36:13Z — layout/step change.** The `_staging/spine/` + `_staging/claims/chunk_N.ids`
  correlated layout appears for the first time on that date. Everything through 2026-07-12 used
  `assets_NNNNNN.json` + `findings_NNNNNN.json`. The object count per run jumps from ~1,050 to
  ~105,000 because the spine writes one small object per asset.
- **Findings page count** went from ~936–984 files/run (June–July, ~44 MB each) to 2,109–2,112 files
  (from 2026-08-18 on, 22 MB each on 08-18, 6.5 MB each today).
- **Findings byte volume did NOT step up for the target run — it stepped down.** 12.73 GiB versus
  45.78–46.24 GiB the previous day and 40.9–42.6 GiB in June/July: the target run published
  **~3.6× less** than the 2026-08-18 runs of the same flow.

## 7. Collector version timeline in stg

| when (UTC) | evidence | version |
|---|---|---|
| 2026-08-18 10:36:00.536Z | `[ADAPTER_ACTIVATED] tenable-io-collector … assembly: 6.3.0.7, file: 6.3.0.7, info: 6.3.0+ea673fe4119e5e037ce7d2fc21a3c01761d9e762` | 6.3.0.7 / git `ea673fe4` |
| 2026-08-18 15:01:50Z | `head-object` LastModified on `stg/Collectors/TenableIoCollector/default/TenableIoCollector.zip` (1,765,528 bytes) | package replaced |
| 2026-08-19 11:27:50.211Z | `[ADAPTER_ACTIVATED] tenable-io-collector … assembly: 6.3.0.7, file: 6.3.0.7, info: 6.3.0+0db327314192ba97ae00aa4b4d2e8ee33f04cf59` | 6.3.0.7 / git `0db32731` |

So **yes, the Tenable.io collector was changed in stg the day before the incident**: the package was
re-uploaded at 2026-08-18 15:01:50Z, and the run under investigation executed a **different git sha
(`0db32731`) under the same version number (`6.3.0.7`)** than the 2026-08-18 10:36Z run
(`ea673fe4`). No `ADAPTER_ACTIVATED` line exists for the 2026-08-18 15:19Z run, so which sha that run
used is unproven (the marker is emitted on adapter load, not per run). I could not enumerate earlier
uploads of the zip — `s3:ListBucketVersions` is denied to my role.

## Numbers table (quantity | value | source)

| quantity | value | source |
|---|---|---|
| correlation id ObjectId timestamp | 2026-08-19T11:27:46Z | `int('6a859332',16)` decode |
| trigger received | 2026-08-19T11:27:50.077Z | ISB log, pod `…-62mkd` |
| run end / completion published | 2026-08-19T12:39:07.574Z | ISB log |
| duration (collector's own) | 4,277,383.2296 ms = 71 min 17 s | `Collection completed … Duration=` |
| status | success, `Failure=null`, no partialSuccess field | `Adapter final outcome` + `Published done event … Status: success` |
| adapter | native `tenable-io-collector` (YAML engine NOT used) | `stays on the native collector: useYaml is disabled` |
| adapter version | assembly 6.3.0.7, info `6.3.0+0db32731…` | `[ADAPTER_ACTIVATED]` |
| adapter package mtime | 2026-08-18T15:01:50Z (not stale) | `s3api head-object` |
| cursor / since | 1787097600 = 2026-08-19T00:00:00Z (11 h 27 m window) | `filters.since(unix)=`, `filters.last_assessed(unix)=`, `baseDateUtc`, `lastRanAt` |
| vulns export uuid | cbd0865d-8116-4083-8841-ba3c27351c29 | ISB log |
| assets export uuid | b1895240-4795-48ba-b953-810a92618648 | ISB log + manifest |
| assets staged | 17,956 | `_staging/manifest.json` `stagedAssetCount` + 17,956 spine objects |
| assets reported collected | 17,984 | `Items: 17984` / `TotalAssetsCollected=17984` |
| findings collected | 1,950,862 | `TotalFindingsCollected=1950862` |
| pages / findings files | 2,112 (contiguous 000001–002112) | `Pages: 2112` + S3 listing |
| findings bytes published | 13,668,418,677 B = 12.73 GiB | S3 `list-objects-v2` sum |
| whole-prefix bytes | 13,758,323,371 B = 12.81 GiB, 22,180 objects | S3 `--summarize` |
| skipped chunks | 0 | manifest `skippedChunkCount` |
| findings per asset | 108.5 | 1,950,862 / 17,984 |
| bytes per finding | ≈7.0 KB | 13.67 GB / 1,950,862 |
| retries / requeues / resumes | none | `Adapter declined resume … Starting fresh`; 0 rows for Resume/retry-count/requeue; single `RUN metadata` line for 08-19 |
| prior-day comparable runs | 46.24 GiB (08-18 10:36Z), 45.78 GiB (08-18 15:19Z) | S3 per-instance aggregation |
| publish prefix | `s3://cybi-data/stg/raw-data/613f3842d8ddf900117fccfa/0c90af0f-e52e-4808-a728-a62f9bce94ec/21f14fe6-ca31-42a7-87aa-c343bbbb777d/` | `RUN metadata resolved` `StorageUrl=` + `Publishing stream` lines |

## Caveats / unmeasured

- **No Mongo document was read.** Atlas rejects my IAM identity (`AuthenticationFailed`, code 18).
  Everything in §1 is log-derived. The run's stored document — its own status, result metadata and any
  `partialSuccess` field — remains unverified. Someone with a mapped Atlas identity should confirm.
- **Cursor provenance is outside ISB.** `lastRanAt: 2026-08-19T00:00:00Z` is supplied by the caller in
  the trigger. Whether that value is a reset, a default for a first run, or a floor applied by
  cyagentserver cannot be settled from the collector's logs.
- **14-day history is S3-derived, not log-derived**, because `es_esql` timed out on wide windows. Each
  prefix keeps only its instance's latest run, so earlier runs of the same instance would be invisible.
  I did not prove that instance `21f14fe6-…` never ran before today — only that no S3 object under its
  prefix predates 11:28:02Z today.
- **Whether the publish prefix is purged at run start is unverified.** No purge/clean log line was
  looked for successfully. The contiguity and mtime evidence in §5 shows the prefix contained only
  this run's output when the parser read it, which is the operative point.
- 28-asset discrepancy between the manifest (17,956) and the completion event (17,984) is unexplained.
- No `ADAPTER_ACTIVATED` line for the 2026-08-18 15:19Z run, so the version that run used is unknown.
- Elastic ingest lag was not a factor here: the run is 8 hours old and its terminal lines are present;
  the S3 mtime of the last findings object (12:39:07Z) independently confirms the end time.
