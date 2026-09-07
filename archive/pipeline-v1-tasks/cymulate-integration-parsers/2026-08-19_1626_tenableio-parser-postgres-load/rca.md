# Specific RCA — Tenable.io parser Postgres load

Supersedes the width figures in `report.md` §3/§5 (see Correction at the bottom).

## The defect, in one sentence

**`TenableAssetsAndFindings` builds `additional_fields` by walking every non-mandatory `plugin.*`
sub-field (`tenableAssetsAndFindings.py:551-568`), which sweeps in Tenable's *static per-plugin
reference material* — `xrefs`, `see_also`, `vpr`, `cpe`, the four CVSS vector strings — and
`BaseParser.post_process` then explodes one row per CVE (`base_parser.py:295-318`), so that same
reference text is re-serialised into the `jsonb` column of every exposure row: 13.7 MB of distinct
plugin reference data is written as ~49 GB, and that is 80 % of everything the run tried to insert.**

## The measurement

Strided 44-of-2,112-file sample (2.08 %, `KEYS[::48]`, spread across the whole key space — not a
contiguous prefix), `additional_fields` reconstructed exactly as the parser builds it: all
`plugin.*` except the mandatory paths `{name, solution, cve, description}`, plus all root fields
except `{asset, plugin, severity_id, state, first_found, last_found}`. Scripts:
`<scratch>/afmeasure.py`, `affields.py`, `afdup.py`.

### Where the bytes go (CVE-weighted — i.e. bytes actually written per exposure row)

| field | B per exposure row | share | GB over 5,858,751 rows |
|---|---|---|---|
| **`plugin.xrefs`** | **3,255** | **35.1 %** | **19.1** |
| **`plugin.see_also`** | **2,582** | **27.8 %** | **15.1** |
| `plugin.vpr` | 766 | 8.2 % | 4.5 |
| `plugin.cpe` | 551 | 5.9 % | 3.2 |
| `plugin.vpr_v2` | 423 | 4.6 % | 2.5 |
| `root.output` (Nessus plugin text) | 393 | 4.2 % | 2.3 |
| `plugin.cvss_vector` | 215 | 2.3 % | 1.3 |
| `root.scan` | 188 | 2.0 % | 1.1 |
| `plugin.cvss3_vector` | 180 | 1.9 % | 1.1 |
| `plugin.cvss_temporal_vector` | 121 | 1.3 % | 0.7 |
| `plugin.cvss3_temporal_vector` | 115 | 1.2 % | 0.7 |
| `plugin.synopsis` | 72 | 0.8 % | 0.4 |
| everything else (finding_id, indexed, port, …) | ~415 | 4.7 % | ~2.4 |
| **total** | **10,437** | 100 % | **61.1** |

**`xrefs` + `see_also` alone are 63 % of the payload (34.2 GB).** Neither is a Cymulate output field;
both arrive purely because the plugin walk is dynamic and nothing excludes them.

### The redundancy — the number that makes this a defect rather than a data-volume fact

| quantity | sample | full run |
|---|---|---|
| exposure rows (CVE-bearing) | 121,120 | 5,858,751 |
| **distinct `plugin.id`** | **4,516** | (bounded by the account's plugin catalogue) |
| exposure rows per distinct plugin | 26.8 | — |
| static reference bytes **written** | 1,006.3 MB | **≈ 48.7 GB** |
| static reference bytes **distinct** | **13.66 MB** | **13.7 MB+, saturating** |
| **redundancy factor** | **74×** | **≥ 74×** |

Static = `xrefs, see_also, vpr, cpe, vpr_v2, cvss_vector, cvss3_vector, cvss_temporal_vector,
cvss3_temporal_vector, synopsis, exploitability_ease` — all keyed by `plugin.id` and identical for
every asset and every CVE that plugin reports.

The 74× is a floor, not a point estimate: written bytes scale linearly with the sample fraction while
the distinct plugin content **saturates** (the same plugins recur across shards), so the full-run
redundancy is higher. 13.7 MB of real information is being written as ~49 GB.

Worked example of the mechanism: a plugin with 10 CVEs, seen on 1,000 assets, emits 10,000 exposure
rows, each carrying the same ~8.3 KB of `xrefs`/`see_also`/vectors — ~83 MB written for ~8 KB of
distinct content.

## Why this, and not concurrency or batching, is the root cause

- The 61.1 GB payload is what makes the write impossible **before any tuning question arises**. At the
  screenshot's own 0.921 ms/row, 5,858,751 rows is **~90 minutes of single-threaded database time,
  ~11 minutes of a fully saturated 8-vCPU instance** — outside a parser run's budget however the work
  is distributed.
- Each row is ~10.4 KB of `jsonb`, i.e. **~5× the 2 KB TOAST threshold**, so every row pays
  `jsonb_in` plus an LZ compression pass plus out-of-line chunk writes and their WAL. Removing
  `xrefs` + `see_also` alone drops the mean row to ~3.9 KB, and cutting all the static reference
  fields drops it to **~2.1 KB — under the TOAST threshold**, which removes that entire cost class
  rather than reducing it.
- **Load balancing / write concurrency is not the cause.** The run was **alone** (exactly one workflow
  within ±5 min; connections flat at 423–428 for 17 min before and 418–426 after), and the first task
  failed at **12:57:31 with 11.7 GB freeable and 52 % CPU**. Spreading or throttling the ~36 writers
  changes when it fails, not whether. The 2026-08-18 17:12 burst (174 workflow runs in 8 min) is a
  genuinely concurrency-driven failure and is a *separate* problem.
- **Batch size is a real but secondary defect.** `batchsize=50000` cannot widen the statement — pgjdbc
  42.3.6 caps a rewritten multi-row INSERT at 128 tuples (`highestBlockCount = 128`) — so it only
  buffers ~520 MB of bind data per task (50,000 × 10,437 B) and drove executor heap to 0.95. It costs
  memory and buys nothing.
- **The retry storm is an amplifier, not a cause.** 3 app retries × 4 Spark attempts = 436 attempts,
  **0 successes**; that is what drained freeable memory 11.7 GB → 278 MB and what the 84 %-of-load
  figure is actually measuring — load **peaked at 327.46 AAS at 13:04Z, after Glue had already aborted
  at 13:03:44**, and the orphaned backends kept inserting for ~4 more minutes. In `append` mode with no
  cleanup that is also a duplicate-row hazard.

## Fix, in priority order

1. **Exclude the static per-plugin reference fields from `additional_fields`** — `xrefs`, `see_also`,
   `vpr`, `vpr_v2`, `cpe`, and the four CVSS vector strings. −80 % of the payload (61.1 → ~12 GB) and
   it takes the row under the TOAST threshold. Nothing downstream in this repo reads them.
2. If any of them is genuinely needed, **store it once per `plugin.id`** in a reference table and join,
   rather than per exposure row. That is what the 74× redundancy is telling you.
3. Then, and only then, revisit batch size, writer concurrency and the retry policy — with a payload
   that can actually land, those become tuning rather than triage.

## Correction to `report.md`

`report.md` §3 and §5 state `additional_fields` = **14,803 B/row ≈ 86.7 GB**. That figure came from
W1's measurement of the **whole `plugin` object**, which includes the four mandatory paths the parser
**excludes** from `additional_fields` (`plugin.name`, `plugin.solution`, `plugin.cve`,
`plugin.description`) — `plugin.cve` alone accounts for ~9.3 GB.

Measured correctly against the parser's actual exclusion set: **10,437 B/row ≈ 61.1 GB**.

Every conclusion in `report.md` stands; the payload is 30 % smaller than stated and still 61 GB. The
derived figures that must be read from this file instead of `report.md`: per-row `additional_fields`
bytes, the total payload GB, and and the "fraction of payload written", which is **withdrawn entirely** — verifier
pass 1 showed that derivation misattributes a 35-minute `WriteThroughput` mean (contaminated by an
unrelated 12:46 spike) to a 383-second write, and compares Aurora volume-bytes against uncompressed
bind payload. The defensible statement is: **0 of 436 write-task starts succeeded**, while
`tup_inserted` ran 694–5,157 rows/s and peaked *after* the abort — so whether rows committed is an
open question needing a `COUNT(*)`, and a duplicate-row risk under `append` + 3 retries.
