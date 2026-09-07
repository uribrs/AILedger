# W1 — S3 / collector published volume

Target (frozen): `s3://cybi-data/stg/raw-data/613f3842d8ddf900117fccfa/0c90af0f-e52e-4808-a728-a62f9bce94ec/21f14fe6-ca31-42a7-87aa-c343bbbb777d/`
Account 118330362824, region us-east-1. Read-only throughout; only `get_object` / `list_objects_v2` were used.

## Method & clock

- **Every timestamp in this document is UTC**, taken from `list-objects-v2` / `head-object` JSON (`LastModified`), never from `aws s3 ls`. The one `aws s3 ls`-style output I looked at early was discarded.
- Credentials verified before starting: `aws sts get-caller-identity` → `arn:aws:sts::118330362824:assumed-role/AAD-Platform/urib@cymulate.com`. No `ExpiredToken` at any point; the whole scan completed on one credential.
- **The data was read in full, not sampled.** All 2,112 `findings_*.json` objects (13,668,418,677 bytes) were downloaded and every NDJSON line JSON-parsed. Script: `<scratch>/scan.py` (12-process pool, boto3 `get_object`, streaming per-line parse; per-shard aggregates + 64-bit blake2b hash streams for identity/duplication analysis), aggregation `<scratch>/aggregate.py`. Wall time ~22 min at ~10.6 MB/s. `json_errors: 0` over all 17,984 lines — no truncated or corrupt records.
- Two quantities are sampled, and both are labelled inline: (a) serialized finding/plugin byte sizes at 1-in-20 findings (n=96,534), (b) CVE-weighted row width from a **strided** 44-of-2,112 file sample (`KEYS[::48]`, spread across the whole key space, 2.08% of files / 2.10% of bytes — not a contiguous prefix). The strided sample was validated against the exact full-read numbers: cve/finding 2.947 vs exact 3.003 (−1.9%), mean plugin 3,169 B vs exact 3,217 B (−1.5%), mean output 2,472 B vs exact 2,458 B (+0.6%).
- `<scratch>` = `/private/tmp/claude-501/-Users-user-Dev-cymulate-integration-parsers/adbf1114-0abc-4883-8523-fb6b6af5f1ab/scratchpad`.

## Inventory (with the exact commands)

```bash
aws s3api list-objects-v2 --bucket cybi-data \
  --prefix "stg/raw-data/613f3842d8ddf900117fccfa/0c90af0f-e52e-4808-a728-a62f9bce94ec/21f14fe6-ca31-42a7-87aa-c343bbbb777d" \
  --output json --region us-east-1 > target_listing.json     # IsTruncated: absent → single page set, CLI auto-paginated
```

Totals: **22,180 objects, 13,758,323,371 bytes (13.758 GB)**, LastModified **2026-08-19T11:28:02Z .. 12:39:07Z**.

Broken down by name pattern (python over `target_listing.json`, digits collapsed to `N`):

| group | objects | bytes | GB | first LastModified (UTC) | last LastModified (UTC) |
|---|---|---|---|---|---|
| `findings_N.json` (flat, at prefix root) | 2,112 | 13,668,418,677 | 13.668 | 2026-08-19T11:30:14Z | 2026-08-19T12:39:07Z |
| `_staging/spine/<uuid>.json` | 17,956 | 89,252,197 | 0.089 | 2026-08-19T11:28:02Z | 2026-08-19T11:29:52Z |
| `_staging/claims/chunk_N.ids` | 2,111 | 652,308 | 0.001 | 2026-08-19T11:30:13Z | 2026-08-19T12:38:09Z |
| `_staging/manifest.json` | 1 | 189 | 0.000 | 2026-08-19T11:29:52Z | — |

Layout facts (all verified, none assumed):

- **Flat layout. There are NO `batch_*/` subfolders** under this instance prefix. The only subdirectory is `_staging/` (confirmed with `--delimiter /` on `.../_staging/` → CommonPrefixes `['claims','spine']`).
- **There is no `findings.json`** — only `findings_000001.json` .. `findings_002112.json`. Numbering is **contiguous with zero gaps** (checked `range(1,2113)` against the parsed key set), so no chance of a `findings.json` + `findings_000001.json` overlap.
- **There are NO `assets*.json` files.** Assets are published instead as 17,956 one-object-per-host files under `_staging/spine/`. (Older sibling runs of this same integration setting *do* use `assets_N.json` — see next section. This run's collector version does not.)
- `findings_*.json` size distribution: min 347,610 B, median 5,976,082 B, mean 6,471,789 B, max 19,718,572 B.
- Write window for findings: 4,133 s (68.9 min), 11:30:14Z → 12:39:07Z.
- **The parser started reading at 12:41:25Z, 2 min 18 s after the last object was written (12:39:07Z), and the collector wrote nothing during the parser run.** So nothing here is a mid-write / partial-file artifact.

`_staging/manifest.json` (full content):

```json
{"assetsExportUuid":"b1895240-4795-48ba-b953-810a92618648","stagedAssetCount":17956,
 "skippedChunkCount":0,"baseDateUtc":"2026-08-19T00:00:00Z","completedUtc":"2026-08-19T11:29:51.2923235Z"}
```

`_staging/spine/<uuid>.json` is a **single-line JSON object per host** (no trailing newline; `wc -l` = 0), schema = the Tenable host record (`id, has_agent, created_at, first_seen, last_seen, ipv4s, fqdns, hostnames, operating_systems, tags, ...`) — i.e. the same shape as `record.host` in the envelopes, **with no `findings` / `chunk` / `isLastChunk` keys**.
`_staging/claims/chunk_N.ids` is a newline-separated list of host UUIDs (the chunk→host claim map).

> **Flag for the write-path / reader analysis (W2):** 17,957 of the 22,180 objects inside the prefix the parser reads are `.json` files with a *different schema* (`_staging/spine/*.json` + `_staging/manifest.json`). If the Glue reader resolves this prefix recursively for `*.json`, it will read 17,957 extra tiny JSON files whose schema does not union with the envelope schema. I did not verify the reader's glob — that is W2's call — but the objects are unambiguously there.

## Sibling prefixes under this instance

```bash
aws s3api list-objects-v2 --bucket cybi-data \
  --prefix "stg/raw-data/613f3842d8ddf900117fccfa/0c90af0f-e52e-4808-a728-a62f9bce94ec/" \
  --delimiter "/" --output json --region us-east-1        # → 50 CommonPrefixes, 0 objects at this level
aws s3api list-objects-v2 --bucket cybi-data \
  --prefix "stg/raw-data/613f3842d8ddf900117fccfa/0c90af0f-e52e-4808-a728-a62f9bce94ec/" \
  --output json --region us-east-1 > parent_listing.json  # full recursive, 121 MB of JSON
```

**Under the integration-setting prefix `0c90af0f-…-94ec/` there are 50 sibling instance directories totalling 238,205 objects / 346,987,023,097 bytes (346.99 GB), spanning 2026-01-19 .. 2026-08-19.**

The target run is **one** of those 50. Largest by bytes:

| instance | objects | bytes | GB | first LastModified (UTC) | last LastModified (UTC) | layout |
|---|---|---|---|---|---|---|
| 251f9990-d0cb-4fa4-84e2-a2ad418c4ece | 105,053 | 50,079,221,691 | 50.079 | 2026-08-18T10:36:13Z | 2026-08-18T14:46:48Z | **`batch_N/findings_N.json`** ×2112 + spine ×100,829 |
| e2702554-aa68-4aba-97e0-cc3639fd9109 | 105,368 | 49,585,501,941 | 49.586 | 2026-08-18T15:19:33Z | 2026-08-18T19:16:07Z | flat `findings_N.json` ×2109 + spine ×101,150 |
| 2550a777-eb10-4110-acbc-36c13c57a46b | 1,086 | 46,209,182,339 | 46.209 | 2026-07-08T15:25:14Z | 2026-07-08T16:32:16Z | legacy `findings_N.json` ×984 + **`assets_N.json` ×102** |
| 6c337dc8-a8ba-410d-9e5a-0243f92f38e0 | 1,083 | 46,168,395,608 | 46.168 | 2026-07-06T09:37:12Z | 2026-07-06T10:42:57Z | legacy findings+assets |
| 43d2ff7e-8b20-459d-9546-1d977bf49375 | 1,055 | 45,047,980,339 | 45.048 | 2026-06-21T10:33:12Z | 2026-06-21T11:28:31Z | legacy findings+assets |
| d836a371-7fde-4268-b7dd-566dc2e4a8fb | 1,052 | 44,812,213,497 | 44.812 | 2026-06-21T14:16:56Z | 2026-06-21T15:24:56Z | legacy findings+assets |
| 30e3cb9a-f25f-4790-9db9-53ff7ba926be | 1,038 | 44,291,790,091 | 44.292 | 2026-06-14T10:33:13Z | 2026-06-14T11:34:24Z | legacy findings+assets |
| **21f14fe6-ca31-42a7-87aa-c343bbbb777d (TARGET)** | **22,180** | **13,758,323,371** | **13.758** | **2026-08-19T11:28:02Z** | **2026-08-19T12:39:07Z** | flat findings + spine |
| 04e2fafd-728c-490c-a6b1-fc685d797225 | 101 | 3,351,443,760 | 3.351 | 2026-05-04T14:03:38Z | 2026-05-04T15:20:46Z | |
| fb7701d0-f7ad-44c9-a765-c82a497bd91a | 15 | 2,166,949,523 | 2.167 | 2026-03-08T16:48:22Z | 2026-03-08T17:35:00Z | |
| de004fc1-5b17-4513-9609-6877413fecb8 | 122 | 1,369,761,341 | 1.370 | 2026-07-12T13:57:22Z | 2026-07-12T14:02:07Z | |

(remaining 39 prefixes are ≤ 52 MB each, mostly 1–3 objects, Jan–Jun 2026)

**Verdict on multi-run accumulation:**

- **Inside the prefix the parser was told to read, there is exactly ONE collector run.** All 22,180 objects fall in a single contiguous window 11:28:02Z–12:39:07Z on 2026-08-19, and the manifest's `completedUtc` (11:29:51Z) sits inside it. **No stale data from previous runs is mixed in.** So the 13.67 GB is not an accumulation artifact — the collector genuinely published 13.67 GB / 1.95 M findings in one run.
- **However, one level up is a 346.99 GB / 238,205-object junk drawer.** 50 never-cleaned-up runs from Jan–Aug 2026 sit under the setting prefix. If any code path ever resolves the input at `…/0c90af0f-…-94ec/` rather than `…/21f14fe6-…-777d/`, it pulls 25× the target's bytes from 50 mutually-duplicating runs. **Nothing was cleaned up after any of the 50 runs.**
- Sibling-run context worth carrying: the two runs of the day before staged ~101 K assets each (`stagedAssetCount` 100,829 and 101,149, `baseDateUtc` 30 days back) versus the target's **17,956** (`baseDateUtc` 2026-08-19T00:00:00Z, i.e. a same-day window). The target run therefore covers **5.6× fewer assets** than the previous day's runs and still emitted 13.67 GB. Volume here is driven by findings-per-asset, not by asset count.
- One sibling (251f9990, 2026-08-18) uses the **`batch_NNNNNN/findings_NNNNNN.json`** layout; the target and e2702554 use the flat layout. The collector changed its output layout between 2026-08-18 10:36Z and 2026-08-18 15:19Z.

## Record & finding counts

Exact, from the full read of all 2,112 files (`scan.py` → `aggregate.py`):

- **NDJSON envelope records (lines): 17,984** — plus 2,112 blank lines, exactly one trailing newline per file. `json_errors: 0`.
- **Total findings (Σ `len(record.findings)`): 1,950,862**
- **Distinct `host.id`: 17,984** — union of per-shard host-id sets. Equal to the line count → **exactly one envelope record per host**.
- **Distinct `finding_id`: 1,950,862.** `finding_id` is present on **every** finding (`fid_missing: 0`).
- **`chunk` distribution: `{0: 17984}`** — every record is chunk 0.
- **`isLastChunk` distribution: `{True: 17984}`** — every record is terminal. **Zero hosts span more than one chunk record. There is no chunking fan-out at all in this run.**
- **`findingsInChunk` vs `len(findings)`: agree on all 17,984 records** (`fic_mismatch: 0`, `fic_missing: 0`). The self-declared count is trustworthy.
- Findings per host: mean **108.48**, max in one record — the biggest single file `findings_000556.json` holds 16 records / 2,784 findings.
- Lines per file distribution: `{1:2, 2:8, 3:44, 4:84, 5:139, 6:211, 7:328, 8:327, 9:301, 10:232, 11:176, 12:121, 13:71, 14:39, 15:13, 16:9, 17:5, 18:1, 297:1}`. The 297-line outlier is `findings_002112.json`, the only file with **0 findings** — 297 bare hosts with `findingsInChunk: 0`.

Host-universe cross-check against `_staging/spine/`:

- spine host files: **17,956**; hosts appearing in findings envelopes: **17,984**
- in findings but not in spine: **28**; in spine but not in findings: **0**
- So the asset universe is ~18 K either way; the 28 extra hosts were discovered during the findings pass and never staged. Not a duplication signal.

## Duplication

**There is no duplication in this data. The duplication factor is 1.0000.** Every check below was run over the *entire* dataset (all 2,112 files, all 17,984 records, all 1,950,862 findings), not a sample — hashes were streamed to disk as uint64 arrays and reduced with `numpy.unique(..., return_counts=True)`.

| check | occurrences | distinct | surplus (duplicate) | max multiplicity |
|---|---|---|---|---|
| byte-identical envelope lines (blake2b-64 of the raw line) | 17,984 | 17,984 | **0** | 1 |
| `finding_id` | 1,950,862 | 1,950,862 | **0** | 1 |
| `(host.id, chunk)` | 17,984 | 17,984 | **0** | 1 |
| `(host.id, cve)` | 5,858,751 | 5,652,470 | 206,281 (3.52%) | 20 |

- **No envelope record is a byte-identical duplicate of another.**
- **No `finding_id` appears twice.** Average multiplicity 1.0000, worst case 1.
- **No `(host.id, chunk)` pair repeats** — consistent with one chunk-0 record per host.
- **No overlapping files.** There is no `findings.json`, the `findings_NNNNNN.json` sequence is gap-free 1..2112, and there are no `batch_*/` duplicates of the flat files. The only cross-file redundancy is by design: `_staging/spine/*.json` restates the same host record that `record.host` carries inside the envelopes (17,956 hosts restated), and `_staging/claims/*.ids` restates the host UUIDs.
- The only real multiplicity is **`(host, cve)` at 3.52% surplus**, max 20 — the same CVE reaching one host through several plugins/ports. Modest, and it *adds* rows rather than duplicating them.

**Conclusion: the volume problem is not duplicated input.** Whatever the parser is choking on, the collector published 1.95 M distinct findings across 17,984 distinct hosts with no redundancy.

## CVE fan-out (predicted exposure rows)

CVEs live at **`finding.plugin.cve[]`** (a string array). Verified by key-frequency census over a whole shard: `cve` present on 593/1,334 findings; no other CVE-bearing field exists on the finding or the plugin (the neighbours are `cvss3_base_score`, `cvss_vector`, `vpr`, `epss_score`, `xrefs`, `bid`, `cpe` — none of which carry CVE ids).

Exact, over all 1,950,862 findings:

- findings with **≥1 CVE: 867,700 (44.48%)**
- findings with **0 CVE: 1,083,162 (55.52%)**
- **Σ `len(plugin.cve)` = 5,858,751 → this is the predicted exposure row count after the parser's one-row-per-CVE explode, before any dedup.**
- CVE-per-finding distribution: mean **3.003** (over all findings) / **6.752** (over CVE-bearing findings only); **p50 = 0**, p90 = 5, p95 = 10, p99 = **49**, **max = 753**.
- Head of the histogram: `{0: 1,083,162, 1: 412,503, 2: 125,936, 3: 82,218, 4: 42,123, 5: 37,459, 6: 26,099, 7: 16,638, 8: 18,933, 9: 7,105, 10: 11,538, 11: 10,709, …}`
- The distribution is brutally long-tailed: the median finding contributes 0 rows, but the p99 finding contributes 49 and the worst contributes 753. **Most of the row volume comes from a small minority of high-CVE findings.**
- `(host, cve)` repeats: 5,858,751 occurrences → 5,652,470 distinct, so **206,281 rows (3.52%) are a repeat of a (host, cve) pair already present** via another plugin or port, worst case 20×. Dedup on `(host, cve)` alone would remove only 3.5%; dedup keyed on `(host, plugin, cve)` would remove essentially nothing.

**Fan-out factors:**

| factor | value |
|---|---|
| exposure rows per collector finding | **5,858,751 / 1,950,862 = 3.003×** |
| exposure rows per CVE-bearing finding | 5,858,751 / 867,700 = 6.752× |
| exposure rows per asset | 5,858,751 / 17,984 = **325.8** |
| exposure rows per GB of collector output | 5,858,751 / 13.668 GB = 428,600 |

BaseParser also drops vulnerability findings with no CVE, which is the 1,083,162 zero-CVE findings — so the parser reads 1.95 M findings, throws away 55.5% of them, and writes **5.86 M** rows from the remaining 44.5%. Both halves cost work.

## Row width

`output` field, exact over all findings (present on 1,950,860 of 1,950,862 — `out_missing: 2`):

| stat | bytes |
|---|---|
| mean | **2,458.2** |
| p50 | 251 |
| p95 | 5,358 |
| p99 | **56,531** |
| max | **5,473,543** (a single 5.47 MB plugin-text field) |
| total across all findings | 4,795,607,512 (4.80 GB) |

Serialized JSON size, **1-in-20 finding sample, n = 96,534** (buckets of 256 B for the percentiles):

| object | mean | p50 | p95 | max |
|---|---|---|---|---|
| whole finding | **6,926 B** | ~5,120 B | ~12,800 B | ~5,425,664 B |
| `plugin` sub-object | **3,217 B** | ~1,280 B | ~7,424 B | ~60,672 B |

**The number that actually drives INSERT cost — CVE-weighted width.** Because plugin size and CVE count are strongly positively correlated (a 753-CVE plugin carries a 753-element `cve[]` plus a huge `cpe[]`), the *unweighted* mean badly understates what gets written once the row is duplicated per CVE. Measured on the strided 44-file / 2.08% sample (script `<scratch>/rowweight.py`, validated above to within 2%):

| per-exposure-row mean | bytes | ×5,858,751 rows |
|---|---|---|
| `plugin` object (what lands in the wide `additional_fields` column) | **14,803** | **≈ 86.7 GB** |
| whole finding object | **16,320** | ≈ 95.6 GB |
| `output` field alone | 379 | ≈ 2.2 GB |

So the CVE explode multiplies the plugin payload by **4.6×** relative to its unweighted mean (14,803 / 3,217), on top of the 3.0× row multiplication. Note the inversion: `output` is *cheap* per exposure row (379 B weighted vs 2,458 B unweighted) because high-CVE findings have short `output` text, while `plugin` is *expensive* (14,803 B vs 3,217 B). **A fix that trims `output` saves almost nothing; a fix that trims `plugin`/`additional_fields` saves ~86 GB of writes.**

## Numbers table

| quantity | value | how measured |
|---|---|---|
| objects under target prefix | 22,180 | `list-objects-v2` full listing, python count |
| bytes under target prefix | 13,758,323,371 (13.758 GB) | same, Σ `Size` |
| `findings_NNNNNN.json` files | 2,112 (000001..002112, no gaps) | same, name pattern group |
| findings bytes | 13,668,418,677 (13.668 GB) | same |
| findings file size mean / median / max | 6,471,789 / 5,976,082 / 19,718,572 B | same |
| `_staging/spine/*.json` files | 17,956 (89,252,197 B) | same |
| `_staging/claims/chunk_N.ids` files | 2,111 (652,308 B) | same |
| `batch_*/` subfolders present | **no** (only `_staging/`) | `--delimiter /` listings |
| `assets*.json` present | **no** | name pattern group |
| LastModified range, whole prefix (UTC) | 2026-08-19T11:28:02Z .. 12:39:07Z | `LastModified` from listing JSON |
| LastModified range, findings only (UTC) | 2026-08-19T11:30:14Z .. 12:39:07Z | same |
| gap between last write and parser start | 2 min 18 s (12:39:07Z → 12:41:25Z) | listing JSON vs given run start |
| `stagedAssetCount` (manifest) | 17,956 | `_staging/manifest.json` |
| sibling instance prefixes under the setting | 50 | `--delimiter /` on setting prefix |
| objects / bytes under the setting prefix | 238,205 / 346,987,023,097 (346.99 GB) | full recursive listing, python rollup |
| collector runs inside the read path | **1** | single contiguous 71-min write window |
| envelope records (NDJSON lines) | **17,984** | full read of all 2,112 files |
| JSON parse errors | 0 | same |
| total findings | **1,950,862** | Σ `len(record.findings)` |
| distinct `host.id` | **17,984** | union of per-shard sets |
| distinct `finding_id` | **1,950,862** | numpy unique over 1.95 M hashes |
| `finding_id` missing | 0 | same pass |
| `chunk` values seen | `{0: 17984}` | same pass |
| `isLastChunk` values | `{True: 17984}` | same pass |
| hosts with >1 chunk record | **0** | same pass |
| `findingsInChunk` ≠ `len(findings)` | **0 of 17,984** | same pass |
| byte-identical duplicate records | **0** | blake2b-64 per line, numpy unique |
| duplicate `finding_id` occurrences | **0** | numpy unique, max multiplicity 1 |
| duplicate `(host.id, chunk)` | **0** | numpy unique |
| **duplication factor** | **1.0000** | distinct / total = 1,950,862 / 1,950,862 |
| findings with ≥1 CVE | 867,700 (44.48%) | full read, `len(plugin.cve)>0` |
| findings with 0 CVE | 1,083,162 (55.52%) | same |
| **Σ len(plugin.cve) = predicted exposure rows** | **5,858,751** | same |
| CVE/finding mean, p50/p95/p99/max | 3.003, 0/10/49/753 | exact histogram over 1.95 M findings |
| distinct `(host, cve)` | 5,652,470 (3.52% surplus, max 20×) | numpy unique over 5.86 M hashes |
| **fan-out: exposure rows / collector finding** | **3.003×** | 5,858,751 / 1,950,862 |
| exposure rows per asset | 325.8 | 5,858,751 / 17,984 |
| `output` bytes mean / p50 / p95 / p99 / max | 2,458 / 251 / 5,358 / 56,531 / 5,473,543 | exact histogram, all findings |
| `output` bytes total | 4,795,607,512 (4.80 GB) | same |
| finding JSON mean bytes | 6,926 | 1-in-20 sample, n=96,534 |
| `plugin` JSON mean bytes | 3,217 | same sample |
| **CVE-weighted `plugin` bytes / exposure row** | **14,803** | strided 2.08% sample (44/2112), validated ±2% |
| CVE-weighted whole-finding bytes / row | 16,320 | same |
| **predicted plugin payload written** | **≈ 86.7 GB** | 5,858,751 × 14,803 |
| predicted whole-finding payload | ≈ 95.6 GB | 5,858,751 × 16,320 |

## Caveats / what I could not measure

- **Envelope `uuid` global distinctness was only verified per file.** Each shard's `uuid` set had size == its line count, and the global line-hash and `host.id` sets are fully distinct (17,984/17,984), so a cross-file `uuid` collision is effectively excluded — but I did not build a global `uuid` set.
- **Count of hosts with zero findings is a lower bound of 297** (all records in `findings_002112.json`). I aggregated findings-per-*file*, not per-*record*, so zero-finding records scattered in other shards were not counted. Re-measuring needs another full 13.67 GB pass; I judged it not worth ~22 min since it does not change the row-volume math.
- **Serialized finding/plugin byte sizes are sampled**, not exact: 1-in-20 findings for the unweighted means/percentiles, and a strided 44-of-2,112 file sample (2.08% of files, 2.10% of bytes, taken as `KEYS[::48]` across the whole key space, never a contiguous prefix) for the CVE-weighted figures. The strided sample reproduced three independently-known exact quantities to within 2% (cve/finding −1.9%, mean plugin −1.5%, mean output +0.6%), so I quote the 14,803 B/row and the derived ≈86.7 GB with a **±2% error bound**. The percentile figures from the 1-in-20 sample are bucketed to 256 B and are therefore approximate to that granularity (marked `~`).
- **`_staging` byte counts come from the listing only**; I read the manifest, one spine file, and one claims file, not all 20,067 of them. Their schema claim ("host record, no `findings` key") rests on the one spine file I opened plus the shared shape of `record.host` in the envelopes — **treat "all 17,956 spine files have that schema" as inference, not measurement**.
- **I did not verify what the Glue reader actually globs.** Whether the 17,957 `_staging/*.json` objects are read, and whether the parser resolves the instance-level or setting-level prefix, is W2's/recon's determination. I only established that the objects exist at those paths.
- **Predicted exposure rows (5,858,751) is the pre-dedup, pre-`post_process` count.** It assumes one row per `(finding, cve)` and does not model any other row-dropping or row-adding the parser does beyond the documented CVE explode and no-CVE-vulnerability drop. Comparing it to what actually landed in `integration.parser_output_exposures` is not my scope.
- No AWS mutation of any kind was performed. Only `sts get-caller-identity`, `s3api list-objects-v2`, `s3 cp` to local disk, and boto3 `get_object`.
