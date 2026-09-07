# Verifier Pass 1

## Verdict

**PASS-WITH-GAPS** — the root cause is correct, independently measured, and arithmetically sound (every one of the 13 headline figures I recomputed checks out), but the report omits the single largest measured number in the evidence (`db.load.avg` peak **327.46 AAS**), calls a window-averaged 141.45 "the true peak", rests its "nothing landed" cross-check on a misattributed metric mean, and answers "does the arithmetic reproduce the screenshot?" with "yes" when W2 explicitly could not reproduce calls/sec.

---

## Success Criteria coverage

| # | criterion | status | evidence |
|---|---|---|---|
| 1 | Run identified — Mongo `_id` → client/instance/integration/collector run, and the Glue run, both windows UTC | **MET** (via a substitute surface, disclosed) | `research/run-identity.md` full identity table; `evidence-isb-collector.md` §1 quotes the correlationId inside the RabbitMQ trigger payload with `instanceId 21f14fe6…`, `clientID 613f3842…`, `integrationSettingId 0c90af0f…`, `StorageUrl` = the Glue `ZIP_FILE_KEY`. Collector 11:27:50Z→12:39:07Z; parser 12:41:25Z→13:03:44Z; spike 12:58Z→13:07Z. The Mongo document itself was never read (Atlas `AuthenticationFailed`, code 18) — disclosed in `report.md:256`. |
| 2 | Volume quantified — collector findings, parsed findings, exposure rows, every multiplier named and sized | **MET, strongest part of the work** | 1,950,862 findings from two independent sources (ISB `TotalFindingsCollected=1950862`; W1 full read of all 2,112 files). 5,858,751 rows from two independent sources (W1 Σ`len(plugin.cve)`; W2 driver `DataFrame row count: 5858751` @12:57:04.041). Multiplier table `report.md:53-61` sizes CVE explode 3.003×, chunk 1.000×, input dup 1.0000, join fan-out 1×, per-port/plugin none, `(host,cve)` repeats +3.52%. |
| 3 | Write shape — statement form, rows/statement, JDBC config with `file:line`, writer concurrency, and whether it reproduces 1.39 calls/s @ 177.29 rows/s @ 117.43 ms/call; A11/A12 confirmed or refuted | **PARTIALLY MET** | Shape itself is fully nailed: multi-row `VALUES` measured from PI text (20 binds/tuple, ≥38 tuples before the 4,095-char cap); 128-tuple cap from pgjdbc `REL42.3.6 PgPreparedStatement.java:1678-1734`; `batchsize=50000` at `script.py:70`; `reWriteBatchedInserts` at `sparkDAL.py:209`; 100 partitions / 37 measured concurrent connections. **But**: rows/call (127.5) is reproduced and calls/sec is *not* — W2 states "I could not reproduce the calls/sec figure", yet `report.md:105` answers the question **"Yes"**. And **A12's ~499-statements-in-flight inference is never disposed anywhere in the report**, though this criterion names A12 explicitly. |
| 4 | Attribution — the ~280 AAS peak decomposed; INSERT vs other load; volume vs shape vs per-row index/constraint cost | **PARTIALLY MET** | INSERT-vs-other is measured and clean: 126.35 of 150.98 AAS = 84% (12:56–13:10Z), all EA services ~24 AAS. Volume vs shape is separated in physical units. **But** the "~280 AAS peak" is never decomposed as such — the report does not mention 280 or the measured 327.46, and substitutes `141.45` described as "the true peak" (`report.md:120`). And the three-way cost split is qualitative: index / `jsonb_in` / TOAST are listed as the components of 0.921 ms/row (`report.md:132-140`) with **no size attached to any of them**. |
| 5 | Baseline — compared to at least one other run, explicit anomalous/steady-state verdict | **MET** | `evidence-glue-db.md` §C: 3,000 job runs (FAILED 2,709 / SUCCEEDED 290 / STOPPED 1); 25 h graph pass (Tenable.io FAILED 1,347 / SUCCEEDED 8); SUCCEEDED band 195–524 s vs every run ≥1,104 s failed; target instance has exactly 1 run in 20 days; ±5 min concurrency = 1 (itself); 2026-08-18 burst as contrast. Verdict stated at `report.md:181`. |
| 6 | Root cause in one sentence + ranked factors each with its number | **MET** | `report.md:10-15` one sentence; `report.md:211-226` nine ranked factors, each carrying a number. |
| 7 | Every assumption A1–A15 disposed VALIDATED / REJECTED / NEVER-TESTED with actor and citation | **NOT MET at handoff** | No disposition exists in `report.md`, `execution_notes.md`, or `assumptions.md` (which still reads "Entries are OPEN unless…" with only A11 resolved). Closed by this pass — see below and the `## Final Disposition` section now appended to `assumptions.md`. |
| 8 | Fix direction named, rough cost, no design | **MET** | `report.md:230-248`, explicitly "Not designed here", seven directions with the highest-ratio one named first. One omission noted in Findings #8. |

---

## Original-request coverage beyond the criteria

The user asked four things. Three are fully delivered.

- **"investigate this"** — done across four disjoint measured surfaces plus one external-source packet.
- **"and the parsers implementation"** — done, and this is where the investigation earned its keep. `research/internal-recon.md` traces the write path (`sparkDAL.py:225-230`), batching (`script.py:69-72`), the partition formula (`sparkDAL.py:63-66`), the retry loop (`sparkDAL.py:222-247`), the CVE explode (`base_parser.py:295-318`), the CVE-less drop (`:291-303`), and proves the asset dedup at `tenableAssetsAndFindings.py:526` runs **before** the join at `:619` — killing the join-fan-out hypothesis on code rather than on a comment. The report's largest original finding is an implementation finding that was not in the hypothesis set: the whole `plugin` object, including the `cve[]` array that drove the multiplication, is copied into `additional_fields` on every exploded row (`tenableAssetsAndFindings.py:551-579`), which CVE-weighted is 14,803 B/row.
- **"then report"** — delivered, 265 lines, in the contract's prescribed 9-section order, decision points clustered, action items last.
- **"you can check the actual numbers in AWS/glue, the collector in ISB and S3 for the real data"** — all three surfaces reached. The fourth the user named implicitly (Mongo, via the `_id`) was **not** reached; disclosed, with the identity established from the ISB trigger payload instead.

**Immediately usable by the operator?** Yes. §8 is actionable, §9 states each gap with the risk it leaves, and the evidence-expiry warning (Glue logs 3-day retention, gone 2026-08-22) is a genuine operational flag. The one thing an operator would be misled about is the magnitude of the peak — see Finding #1.

---

## Findings

### 1. HIGH — the measured load peak (327.46 AAS) is missing, and 141.45 is mislabelled "the true peak"

`report.md:118-121` presents `12:55–13:06 (peak) | 141.45` and concludes "the true peak is **~141 AAS on 8 vCPUs — 17.7× the vCPU count**". That figure is the *dominant INSERT variant's* AAS **averaged over an 11-minute window** (`evidence-glue-db.md:613-615`). The actual measured peak database load is **327.46 AAS at 13:04Z**, baseline ~3, mean 58.62, giving **41:1** against 8 vCPUs (`evidence-glue-db.md:673, 681, 1021-1022`). `grep -n "327\|280" report.md` returns nothing.

Three consequences:
- The report **understates the incident by 2.3×** and the AAS/vCPU ratio by 2.3× (17.7:1 vs the measured 41:1).
- W2 explicitly asked for this to be recorded: *"Two screenshot figures I contradict with measurement: vCPUs is 8, not ~10 … **and peak AAS is 327.5, not ~280**"* (`evidence-glue-db.md:1063-1065`). The report carries the vCPU correction (`report.md:36`) and drops the peak correction.
- Success Criterion 4 and `task.md` question 3 both say "attribute the **~280 AAS** peak". Because the report never names 280 or 327, that question is formally unanswered — even though the evidence to answer it exists and is decisive (the peak is 24 minutes of the same INSERT across 5 tokenized variants, **109.54 AAS of it still running after the job aborted**, `evidence-glue-db.md:690-695`).

**Fix:** add the measured 60 s curve (45.6 → 96.6 → 148.3 → 220.9 → 268.5 → 296.1 → **327.5** → 281.8 → 213.7 → 61.6), state that the operator's ~280 is corroborated and slightly conservative, and reserve the word "peak" for the 60 s maximum. Keep 126.35/141.45 but label them "window-mean AAS on the INSERT" — which is what they are.

### 2. HIGH — "~1.5 % of the payload written" rests on a misattributed mean and an unstated unit equivalence

`report.md:159-161`: *"Measured `WriteThroughput` averaged 3.34 MB/s over the write — ~1.3 GB, about 1.5 % of the 86.7 GB payload."* Two defects:

- **Window misattribution.** `3,339,086 B/s` is the **mean over 12:40:00Z–13:15:00Z (35 minutes)**, not over the 383 s write (`evidence-glue-db.md:714`, period 60 s, that window). The report multiplies a 35-minute mean by a 383-second duration. W2 further warns the `WriteThroughput` **peak at 12:46 predates the exposures write by 11 minutes and is not attributable to it** (`evidence-glue-db.md:752-753`), i.e. the mean is contaminated by unrelated traffic in exactly the direction that makes this arithmetic unsafe.
- **Unit equivalence never stated.** Aurora `WriteThroughput` measures bytes to the storage volume (WAL/log records, post-TOAST-compression, post-page-coalescing). The 86.7 GB is uncompressed `jsonb` **bind payload on the wire**. Dividing one by the other is not a "% of payload written" in any physical sense, and the report offers no bridging assumption.

The *conclusion* ("essentially zero committed rows") is well supported by a different and much stronger fact — **0 successful write tasks in all three stages** (`Finished task` = 0, 0, 0). W2 also flags the honest ambiguity: `tup_inserted` ran 694–5,157 rows/s and the INSERT held 109.54 AAS after abort, so *some* orphaned backends were demonstrably inserting (`evidence-glue-db.md:781-787`). `report.md:258` lists this in §9 as a gap — which is correct and directly contradicts the tone of §5's "bought essentially zero committed rows".

**Fix:** delete the 1.5 % figure, or re-derive it from `WriteThroughput` restricted to 12:57–13:04 with the unit caveat stated. Lead with "0 of 436 write-task attempts succeeded" and keep §9's commit ambiguity as the standing question.

### 3. MED — "Does the arithmetic reproduce the screenshot? **Yes**" overclaims; A12 never disposed

`report.md:105` answers **"Yes, and the screenshot understates the peak."** What is actually reproduced is the *ratio* 177.29 / 1.39 = 127.5 rows/call, which matches the pgjdbc cap. The absolute rates are not reproduced and cannot be:

- W2: *"the per-SQL rate statistics that would give rows-per-call are unavailable on this engine"* and *"I flag the arithmetic tension rather than resolve it, since I could not reproduce the calls/sec figure"* (`evidence-glue-db.md:558-573`).
- The report's stated resolution — "PI splits this INSERT across **5 tokenized variants**, and the screenshot's 30-minute window dilutes a 9-minute event" — does not close the gap arithmetically. Variant splitting cannot explain it (the dominant variant carries **98.9 %** of the INSERT AAS, `evidence-glue-db.md:602-603`), and window dilution is a factor of ~3.3, not the ~200× shortfall. At 5,858,751 rows over 383 s the run pushed ~15,300 rows/s ≈ 120 calls/s, against the screenshot's 1.39 calls/s and 177.29 rows/s. The tension is *explained away*, not resolved.
- **A12 is never disposed.** Criterion 3 says "A11/A12's arithmetic is confirmed or refuted against real configuration." A11 is confirmed explicitly and well. A12's "~499 statements in flight" is refuted by W2's measurement (37 concurrent write tasks, 457 proxy connections, 570 total DB connections) — but the report never says so, so a reader carrying A12 forward still believes ~499 concurrent writers.

**Fix:** change the answer to "the ratio yes, the absolute rates no — and here is why the screenshot's rates cannot be taken as run throughput", and add one line refuting A12's 499.

### 4. MED — two W2-labelled speculations are asserted as fact, one of them in the root-cause sentence

W2 lists exactly three labelled speculations (`evidence-glue-db.md:1068-1079`). The report handles one correctly and promotes the other two:

- **Promoted, in the headline.** `report.md:14`: *"every write task died ~23 s into its first flush"*. W2's speculation #1 is precisely this: *"the most economical reading … is that ~22–23 s is how long a write task needs to … marshal its first 50,000-row batch, so every task is dying on its first `executeBatch`. **I label that last step speculation**: I have no log line that times the first `executeBatch`"* (`evidence-glue-db.md:238-242`). The *floor* is measured (22.6 s, n=331); the *"on its first flush"* mechanism is not. §9 line 263 admits "the trigger of the *first* failure is inferred, not proven", but the root-cause sentence — the most-read line in the document — carries the mechanism unlabelled.
- **Promoted, causally.** `report.md:154-157`: *"Connections rose 425 → **570** … **because** Spark's `savePartition` `finally` calls `conn.rollback()` before `conn.close()`"*, and §7 factor 7 states the leak as a factor. W2's speculation #3: *"the attribution of the specific connection surplus to that path is inference"*. The `finally` structure and the thrown `rollback()` are established; the +145 attribution is not.
- **Handled correctly:** the PI-"CPU" mechanism, labelled inline at `report.md:166-167` and again at `:262`. This is the model the other two should follow.

### 5. MED — Criterion 7 was unmet at handoff

No A1–A15 disposition existed in any artifact when `report.md` was declared the deliverable. `assumptions.md` still carried "Entries are OPEN unless a downstream skill resolved them (A11: VALIDATED…)". Since the contract makes this an explicit success criterion and the orchestration plan makes it a verification obligation, it should have been produced by the synthesis, not discovered missing by the verifier. Now closed (table below, written back to `assumptions.md`).

### 6. MED — the volume/shape/per-row-cost split is quantified on one axis only

D3 and Criterion 4 ask for three shares. What landed:
- **Volume**: quantified (~5,400 s of DB time) — but see the provenance caveat in Finding #7.
- **Shape**: quantified in bytes and concurrency (740 MB/task, ~26 GB in flight, 36 writers, 128-tuple statements) but **not in AAS or DB time**.
- **Per-row index/constraint cost**: **not quantified at all.** `report.md:132-140` lists 11 index inserts, `jsonb_in` on 14.8 KB, and TOAST at ~7× the 2 KB threshold as the components of 0.921 ms/row, with no share attached to any. A13's "material share of the INSERT CPU" is therefore an unmeasured inference (see disposition).

This is not the "single undifferentiated 'too much load'" that D3 called a failed deliverable — the separation is real and physically grounded. But the arithmetic decomposition Criterion 4 asks for is one-third complete, and the report does not say so.

### 7. MED — the volume side's only cost basis is a screenshot-derived per-row figure

`report.md:127-130` builds the volume floor from *"the screenshot's own 0.921 ms/row"* → ~5,400 s → "90 minutes single-threaded, ~11 minutes of a fully saturated 8-vCPU instance … the floor for this payload even with a perfect writer."

Provenance is correctly attributed to the operator (good — no measurement laundering). But 0.921 ms/row = 117.43 ms/call ÷ 127.5 rows/call is a quotient of **two screenshot numbers**, one of which (1.39 calls/sec, inside the 127.5) the report itself shows cannot be reconciled with the run's real throughput (Finding #3). So the entire volume half of the attribution hangs on a figure the report elsewhere treats as unreproducible. As a sanity check it survives — the measured 126.35 AAS over 840 s is ~106,000 s of DB time, ~20× the claimed floor, which is consistent with a retry storm rather than contradictory — but the number deserves an explicit error bar, and "that is the floor … even with a perfect writer" is stated far more firmly than its source supports.

### 8. LOW — §8 omits the cheapest read-only step toward the *actual* failure trigger

The researcher's most defensible next actions include *"Read `idle_in_transaction_session_timeout` and `transaction_timeout` from the parameter group"* — both terminate sessions, both are plausible direct causes of the observed closures, and both are reads (`pgjdbc-batch-rewrite.md:735-737, 764-766`). §9 lists them as unknown, but §8 never turns them into an action item, while it does recommend the much larger `COPY` and row-shrinking work. Given that the 22.6 s floor is the one thing still unexplained, this belongs in §8.

### 9. LOW — the RDS Proxy's amplifying role is under-reported

W2's discovery that the write goes **through** `stg-cybi-aurora-cluster-proxy` was not in the brief and changes the topology (`evidence-glue-db.md:807-851`): **457** proxy connections for a write needing 36, **536** session-pinning warnings meaning the proxy could not multiplex at all and every connection cost a real Aurora backend, and each session reset costing a `DEALLOCATE ALL` — which is why that statement is the instance's 2nd-heaviest. The report names the proxy in §2, attributes `DEALLOCATE ALL` to "proxy session resets" in §7, and points the borrow-timeout error at it in §5, but never states the 457 or the 536 or the "no pooling benefit" conclusion. That conclusion bears directly on fix direction.

### 10. LOW — "328 + 83 corroborate 436" is presented as agreement without W2's own caveat

`report.md:153-155` cites `numFailedTasks 328 + numKilledTasks 83` as corroborating 436 attempts. 328 + 83 = **411**, a 6 % gap. W2 is careful here: the killed count matches **exactly** (83 = 36+36+11), while the failed count is "328 (CW) vs my log-derived 336 … agreement within 2 %" — and 436 is the count of *task starts*, a third quantity (`evidence-glue-db.md:993-996`). W2's own internal figures also do not fully reconcile (419 `Lost task` + 83 killed = 502 against 436 starts, with n=331 for computable lifetimes). The report should say "436 starts; 83 killed matches exactly; 328 vs 336 failed agrees within 2 %" rather than implying a clean sum.

### 11. LOW — minor internal inconsistencies, none affecting a conclusion

- **A11's text says "21 binds/row"** (→ 1,560 rows under the protocol ceiling); W2 **measured 20 binds/tuple** from the PI statement text, consistent with legacy mode (`IS_BATCH` not set, so no `batch_id` — recon: 21 in batch mode, 20 in legacy). `min(32766/20, 128) = 128` either way, so the cap conclusion is untouched.
- **58,587 vs 58,588 rows/partition** — 5,858,751/100 = 58,587.51; the report uses 58,587, W2 uses 58,588. Immaterial.
- **0.921 vs 0.917 ms/row** — the report divides by 127.5, the researcher by 128. Immaterial.
- **`research/pgjdbc-batch-rewrite.md` contains a non-UTF-8 byte**, so `file` reports it as `data` and plain `grep` silently skips it. Use `grep -a`. Worth knowing before anyone tries to verify that packet by grep and concludes it is empty.
- **`state.json` is stale**: S6/S7/S8 still `pending`, W2 still `in_progress`, `verification.status: not_started`, `workflow.verifierRun: false`, `status: in_progress` — all contradicted by a finished `report.md`. Matters only for resumability, which is the point of the directory.

---

## Arithmetic re-check

Every figure the brief named, recomputed from the packets' primitives. **All 13 agree.**

| figure | report says | I compute | agrees? |
|---|---|---|---|
| CVE explode factor | 3.003× | 5,858,751 / 1,950,862 = **3.0032** | ✅ |
| rows per CVE-bearing finding | 6.752× | 5,858,751 / 867,700 = **6.7520** | ✅ |
| exposure rows | 5,858,751 | Σ`len(plugin.cve)` (W1 full read) **=** driver `DataFrame row count` (W2) | ✅ two independent sources, exact |
| CVE-bearing / no-CVE split | 44.5 % / 55.5 % | 867,700/1,950,862 = **44.48 %**; 1,083,162/1,950,862 = **55.52 %**; sum = 1,950,862 exactly | ✅ |
| additional_fields payload | ≈ 86.7 GB | 14,803 × 5,858,751 = **86.73 GB** (decimal; 80.77 GiB) | ✅ (units are decimal GB throughout — consistent) |
| whole-finding payload | ≈ 95.6 GB | 16,320 × 5,858,751 = **95.61 GB** | ✅ |
| `output` payload | ≈ 2.2 GB | 379 × 5,858,751 = **2.22 GB** | ✅ |
| CVE-weight inflation | 4.6× | 14,803 / 3,217 = **4.60** | ✅ |
| index entries | 64.4 M | 5,858,751 × 11 = **64,446,261** | ✅ |
| rows per asset | 325.8 | 5,858,751 / 17,984 = **325.8** | ✅ (326.3 on the 17,956 spine count — the report uses the ISB figure, consistent with its own volume table) |
| INSERT share of load | 84 % | 126.35 / 150.98 = **83.69 %** | ✅ |
| per-row cost | 0.921 ms/row | 117.43 / (177.29/1.39) = **0.9207** | ✅ arithmetic sound; **provenance is the screenshot** (Finding #7) |
| total DB time for the payload | ~5,400 s | 5,858,751 × 0.921 ms = **5,395.9 s** = 89.9 min single-threaded = **11.24 min** on 8 saturated vCPUs | ✅ |
| payload actually written | 1.5 % | 3.34 MB/s × 383 s = 1,279 MB → 1,279/86,730 = **1.48 %** | ⚠️ arithmetic sound, **inputs misattributed** (Finding #2) |
| bind data per batch | ~740 MB | 50,000 × 14,803 = **740.1 MB** (705.9 MiB) | ✅ |
| in flight at once | ~26 GB | 36 × 740.1 MB = **26.65 GB** | ✅ |
| statements per `executeBatch` | 390×128 + 64 + 16 | from the quoted 42.3.6 source: `fullValueBlocksCount` = 50000/128 = **390**; `partialValueBlocksCount` = `bitCount(50000 % 128)` = `bitCount(80)` = **2** → blocks 64 and 16; total **392**; mean 50000/392 = **127.55** | ✅ derived independently from the algorithm, not just quoted |
| 128-tuple cap | binding | `min(max(1, 32766/20), 128) = 128`, then `highestOneBit(128) = 128`. At 21 binds: `min(1560,128) = 128`. Cap binds either way | ✅ |
| peak AAS / vCPU | 17.7× | 141.45 / 8 = **17.68** — but the measured peak is **327.46 / 8 = 41:1** | ⚠️ see Finding #1 |
| retry multiplier | up to 12× | 3 app × 4 Spark = **12** | ✅ |
| failure-rate baseline | ~90 % | 2,709 / 3,000 = **90.3 %** (2,709+290+1 STOPPED = 3,000) | ✅ |
| task-attempt corroboration | 328 + 83 "corroborate" 436 | 328 + 83 = **411** ≠ 436 | ⚠️ see Finding #10 |

---

## Constraint compliance

**Read-only — SATISFIED, by all four workers.**
- W1: `sts get-caller-identity`, `s3api list-objects-v2`, `s3 cp` to local disk, boto3 `get_object`; states "No AWS mutation of any kind was performed."
- W2: `get-*` / `describe-*` / `start-query` only, and states plainly **"No connection to Postgres was attempted at any point."** Several calls returned `AccessDenied` (`DescribeDBParameters`, `DescribeDBProxies`, `DescribeDBLogFiles`) and were reported as gaps rather than worked around.
- W3: Elastic `es_esql` reads, S3 reads, one Secrets Manager read, two failed Mongo *connect* attempts (reads).
- No `glue start-job-run`, no workflow trigger or resume, no Mongo write, no DB mutation. `git diff a9f6631..HEAD` is **empty** and `git status` is **clean** — confirming report-only, as the contract required.
- **On W1's 13.67 GB download:** this is a read of S3 into local scratch, not a write to any live environment, so it is inside "read-only". Worth noting for hygiene rather than compliance: the scratchpad now holds 328 MB (per-shard aggregates in `agg/`, pgjdbc/Spark sources, Glue log extracts) — the raw 13.67 GB of customer vulnerability data was evidently streamed and not retained. Good outcome; it was not stated as a deliberate choice anywhere.

**Clock discipline (D4) — SATISFIED.** This was handled unusually well. W1 states every timestamp is UTC from `list-objects-v2`/`head-object` and that it *discarded* the one `aws s3 ls`-style output it looked at. W3 states no `aws s3 ls` output was used for any clock claim. W2 names the two surfaces that render local time, labels every affected table header IDT, and independently validates the anchor (Glue log prefix `26/08/19 12:57:31` is UTC and agrees with CloudWatch `@timestamp` to the millisecond; RDS Proxy embedded event time runs ~5 s behind ingest `@timestamp` and was re-sorted on the embedded time). The report states the IDT→UTC conversion at `:32-34`, and every IDT time in §6 is labelled IDT. **No unlabelled clock mixing found.** Nit: §5's bare times (`12:57:31`, `12:58`, `13:03`, `13:07`) omit the `Z`, relying on §2's frame — fine in context, but §6 sits nearby with labelled IDT times.

**Speculation labelling — PARTIALLY VIOLATED.** One of W2's three labelled speculations is carried correctly (the PI-"CPU" mechanism, labelled twice). Two are asserted as fact, one of them inside the root-cause sentence. See Finding #4.

**Screenshot vs measurement separation — SUBSTANTIALLY SATISFIED.** W2 devotes a caveat block to it and lists the screenshot figures as "not my measurements", including the explicit warning that **127.5 rows/call "exists *only* in the screenshot … flagged so the main thread does not promote it."** The report honors this: `report.md:107` presents 127.5 under the heading "Does the arithmetic reproduce the screenshot?"; `:120` says "the operator's 58.59 AAS"; `:127` says "the screenshot's own 0.921 ms/row"; `:36` corrects the screenshot's vCPU count against measurement. **No screenshot figure is passed off as a measurement.** The failure is the mirror image — a *measurement* (327.46) that contradicts the screenshot was dropped instead of reported (Finding #1).

**No contiguous-sample duplication claim — SATISFIED, and exceeded.** W1's duplication claim is a **census, not a sample**: all 2,112 files / 13,668,418,677 bytes downloaded and every NDJSON line parsed, `json_errors: 0`, hashes reduced with `numpy.unique`. Duplication factor 1.0000 over all 1,950,862 `finding_id`s and all 17,984 envelope lines. The two genuinely sampled quantities are labelled inline and **strided**: 1-in-20 findings for unweighted byte sizes, and `KEYS[::48]` (44 of 2,112 files, 2.08 % of files / 2.10 % of bytes, "not a contiguous prefix") for the CVE-weighted width — validated against three independently-known exact values to within 2 % and quoted with a **±2 % error bound**. D5 asked for strided sampling plus one fully-read shard; W1 delivered a full read plus strided sampling. This constraint was the one most at risk given the recorded contiguous-sample lesson, and it was met properly.

**Other constraints:** counts for `parser_output_assets` (17,984, succeeded) and `parser_output_exposures` (5,858,751, failed) are kept strictly separate throughout. No `ExpiredToken` occurred. No claim rests on Elastic `MAX(@timestamp)` — W3 explicitly cross-checks against the S3 mtime of the last findings object. Code comments were not used as evidence; recon explicitly flags `tenableAssetsAndFindingsCorrelated.py:22-26` as "a comment, not a runtime guarantee".

---

## Assumption Disposition

Compared against what actually landed — the five evidence packets and `report.md`, not the plan's intentions. `git diff a9f6631..HEAD` is empty, so scope is the task-directory artifacts.

| id | status | citation | actor |
|----|--------|----------|-------|
| A1 | **REJECTED** (premise) / conclusion VALIDATED by another route | Premise "the collector has no checkpoint/resume path" is contradicted: `evidence-isb-collector.md` §5 quotes `11:27:50.225Z Adapter declined resume from checkpoint … Page: 0. Starting fresh.` and records `Resume cancelled. Vendor=Tenable.io Flow=CollectFindings` on 2026-08-18 at 12:06:16Z / 12:51:24Z / 13:43:43Z / 13:55:08Z. A resume path **exists**; it was declined on this run. The AgentService caveat was never resolved by transfer — it was made moot: "no duplicate findings in S3" is independently VALIDATED by `evidence-s3-collector.md` (0 duplicate `finding_id` over 1,950,862; 0 byte-identical duplicate lines; factor 1.0000, full read). | W3 + W1 |
| A2 | **NEVER-TESTED** | No packet examined `TenableIoVulnPhase.cs` or the publish pump's concurrency model. W3 measured publish *output* (2,112 contiguous pages, single run window) — that is not the architecture claim. No citation moves it. | none |
| A3 | **VALIDATED** | `internal-recon.md`: `specs/tenable-assets-findings.yaml:29-35` (delegate shell, `skip_post_shaping: true`), `delegate_hook.py:47-48`, `tenableAssetsAndFindingsCorrelated.py:74-77` (assets lane = `full_df.select("host.*")`), `:96-99` (findings explode). Runtime: driver log 12:47:56.166 `Tenable IO correlated parser: derived assets and findings lanes from the envelope frame`. | recon + W2 |
| A4 | **VALIDATED for this run** (the re-measurement the assumption demanded) | `evidence-s3-collector.md`, full read of all 2,112 files: `chunk` = `{0: 17984}`, `isLastChunk` = `{True: 17984}`, **hosts with >1 chunk record = 0**, `findingsInChunk` == `len(findings)` on all 17,984 records, 0 duplicate `(host.id, chunk)` pairs. The stale 2026-07-06 observation was not carried forward — it was re-measured on this tenant. | W1 |
| A5 | **VALIDATED** | `base_parser.py:295-318` (explode, one row per CVE, fresh `id`), `:291-303` (drop CVE-less `type=vulnerability`), `tenableAssetsAndFindings.py:344` (`type` hard-coded `vulnerability`), `:369-370` (`cve_ids` ← `plugin.cve`). Numeric closure: Σ`len(plugin.cve)` = 5,858,751 (W1) **==** driver `DataFrame row count: 5858751` @12:57:04.041 (W2). | recon + W1 + W2 |
| A6 | **VALIDATED — but by runtime log, not by the wheel** | The wheel was **not** inspectable: `s3://cym-glue/stg/libs/parsers.whl` `GetObject` → **403 Forbidden** (`execution_notes.md`). The merge→build→mtime chain (PR #214 → Jenkins master #273 → wheel rebuilt 2026-08-18 14:14:37Z; `git merge-base --is-ancestor 1e8a880 a7f1c12` = false) is circumstantial on its own. What closes it is direct runtime evidence: driver log 12:47:55.329 `Tenable IO parser façade: resolved mode=hydrated (correlated envelope)` and 12:47:56.166 `Tenable IO correlated parser: derived assets and findings lanes from the envelope frame` — strings recon maps to `tenableAssetsAndFindings.py:426-427` and `tenableAssetsAndFindingsCorrelated.py:80-82`, emitted only by the correlated lane. The code path that ran is proven; the wheel's bytes remain uninspected and need not be. | W2 + recon (deploy chain: main thread) |
| A7 | **VALIDATED** | `run-identity.md`: `aws glue get-job-runs` returns `StartedOn 2026-08-19T15:41:25.406000+03:00` for the run bracketing the spike. `evidence-glue-db.md` §Method: Glue's own log prefix is UTC and agrees with CloudWatch `@timestamp` to the millisecond — no skew inside Glue. Spike = **12:58–13:07Z**. | W0 + W2 |
| A8 | **SPLIT — identity VALIDATED from ISB; the Mongo clause NEVER-TESTED** | Identity: `evidence-isb-collector.md` §1 — the RabbitMQ trigger payload carries `correlationId 6a8593320f3d4281bd8a0df0` together with `instanceId 21f14fe6-…`, `clientID 613f3842…`, `integrationSettingId 0c90af0f-…`, and a `StorageUrl` equal to the Glue run's `ZIP_FILE_KEY`; `int('6a859332',16)` = 2026-08-19T11:27:46Z, trigger `timestamp` = 11:27:50.061Z. **No Mongo document was read** — `list-connections` empty, and `pymongo` returned `OperationFailure: Authentication failed., code 18` because Atlas authenticates by IAM (`MONGODB-AWS`) and the SSO role is not a mapped Atlas user. So "identifies the run document in the stg cluster" is untested; what it stood for is established elsewhere. | W3 |
| A9 | **VALIDATED** | `evidence-glue-db.md`: RDS Proxy log client IPs on the writer endpoint are **exactly the nine Glue executor IPs** plus the driver (10.103.41.13); PI `db.user` = `cybi` **154.30** of 155.27 AAS; the PI statement's quoted 20-column list matches Spark's `JdbcUtils.getInsertStatement` output order (recon). Multi-row `VALUES` can only come from the driver-side rewrite, which is Spark's path, not psycopg2's — and recon's `grep -rni "insert into" libs/ jobs/` finds exactly one hit, in `elasticDAL.py`. | W2 + recon |
| A10 | **REJECTED as worded** (useful half survives) | Partition count was **100** (`DataFrame row count: 5858751, target partitions: 100`), but measured max concurrent write tasks was **37 (36 steady)** — established three independent ways: 9 `Registered executor` lines; `spark.executor.cores=4` / `spark.default.parallelism=36` from `SparkListenerJobStart`; and a replay of `Starting`/`Lost`/`Finished task` per stage 68/69/70. Concurrency = `min(partitions, task slots)`, **not** partition count. The "not 1" half holds. | W2 |
| A11 | **VALIDATED** (already so at handoff; re-derived here) | pgjdbc `REL42.3.6 PgPreparedStatement.java:1678-1734` `final int highestBlockCount = 128;` with `Integer.highestOneBit` round-down; second enforcement in `core/v3/BatchedQuery.java:47-68`; `PARAM_LIMIT` = `Short.MAX_VALUE - 1` = 32766. I re-derived the block split from the quoted algorithm rather than accepting it: `50000/128 = 390` full blocks, `bitCount(50000 % 128) = bitCount(80) = 2` partial blocks of 64 and 16, total **392**, mean **127.55**. Cap present in REL42.2.27 / 42.3.6 / 42.5.4 / 42.7.7 / master. Version grounding is AWS's Glue 4.0 Appendix B, **not** a read of the running classpath — the researcher lists that as unverified item #1, low risk precisely because every released 42.x carries the cap. Note the text's "21 binds/row" vs W2's measured 20; `min(32766/20, 128) = 128` regardless. | researcher (+ verifier re-derivation) |
| A12 | **SPLIT — first clause REJECTED, second clause VALIDATED, and neither disposed in the report** | The arithmetic is right (58.59 / 0.11743 = **498.9**) but the inference "≈499 concurrent writers" is contradicted by measurement: **37** max concurrent write tasks, **457** proxy connections across the whole storm, **570** peak `DatabaseConnections`. PI AAS is not a count of parser statements in flight. Second clause VALIDATED: PI CPU **152.10** AAS (12:56–13:10Z) while host CPU peaked **68.71 %** (CloudWatch) / **71.10 %** (PI) — runnable-but-not-progressing, not compute-bound. Criterion 3 named A12 explicitly and the report never addresses the 499. | W2 |
| A13 | **SPLIT — readability VALIDATED, materiality NEVER-TESTED** | Readable without a DB write: yes — `db-schema-authoritative.md` reads **11 B-tree indexes** (3 keyed on random v4 UUIDs: `id`, `asset_id`, and the `id` tail of `(instance_id, client_id, id)`) and **no triggers** from `cybi-db-models` `prisma/schema/integration.parser-output-exposures.prisma` plus `migrations/20260330120000_…` and `migrations/20260505150010_…`. **Two limits, both material:** (a) that is the migration repo at local HEAD `98b95949` (2026-05-06), **not STG's live catalog** — its own Caveat section says so, and W2 called the catalog "the largest remaining gap"; (b) the assumption's actual claim — that per-row index maintenance is a **material share of the INSERT CPU** — was **never measured**. No packet contains a per-row cost breakdown between index maintenance, `jsonb_in` and TOAST. `report.md:132-140` asserts the components without sizing any. | main thread (list) / nobody (cost share) |
| A14 | **VALIDATED, with its premise corrected upward** | The peak is **not** explained by the INSERT's 58.59 AAS — but the measured peak is **327.46 AAS at 13:04Z** (baseline ~3, mean 58.62), not ~280. The remainder is overwhelmingly **the same INSERT**: 5 tokenized variants summing **126.348 AAS** = **84 %** of the 150.98 AAS top-25 total (12:56–13:10Z), plus **109.54 AAS still on that INSERT during 13:04–13:09Z, after the job aborted**, with `tup_inserted` peaking at 5,157 rows/s at 13:07Z. The assumption's alternative hypothesis — downstream consumers such as `detectChanged` — is **refuted**: all EA services combined are ~24 AAS. The report reaches the right conclusion about post-abort load but omits the 327.46 figure entirely. | W2 |
| A15 | **VALIDATED, with a twist that changes its use** | Retrievable: `aws glue get-job-runs` 3,000 runs back to 2026-07-29 (FAILED 2,709 / SUCCEEDED 290 / STOPPED 1); `get-workflow-runs` 2,900 runs back to 2026-07-30; a 25 h `--include-graph` pass giving per-flow parser outcomes and the 195–524 s SUCCEEDED band. **Twist:** the target instance `21f14fe6` has **exactly one run in 20 days — this one** — so no same-instance baseline exists and the comparison had to be built from sibling instances and other vendors. The 14-day *log* history was unobtainable (stg Elastic timed out on windows >~5 h); W3 substituted S3 prefix aggregation and labelled it as such. | W2 (+ W3 for the S3 volume trend) |

**Summary:** VALIDATED 7 (A3, A4, A5, A6, A7, A9, A11) · VALIDATED-with-correction 2 (A14, A15) · SPLIT 3 (A8, A12, A13) · REJECTED 2 (A1 premise, A10) · NEVER-TESTED 1 (A2).

---

## Decision drift

`decisions.md` carries **D1–D11**, not D1–D9 — D10 and D11 were appended during execution from the researcher packet. All eleven assessed.

| id | landed as decided? | note |
|----|--------------------|------|
| D1 | **Yes** | Report-only. `git diff a9f6631..HEAD` empty, `git status` clean. No deploy, no trigger. |
| D2 | **Yes** | Four independent surfaces reconciled (recon, W1, W2, W3) plus one external packet (R1). The plan's "conclusion supported by only one surface is reported as unconfirmed" was honored on the big number: 5,858,751 arrives from S3 and from the Glue driver independently. |
| D3 | **Partially** | Volume and shape *are* separated in physical units — not the "undifferentiated too much load" D3 forbade. But the AAS/DB-time attribution is quantified on the volume axis only, and that one figure is screenshot-derived. Index/`jsonb_in`/TOAST shares never sized. See Findings #6, #7. |
| D4 | **Yes** | Best-executed decision in the task. See Constraint compliance. |
| D5 | **Exceeded** | Duplication became a **full census** (all 2,112 files read, `numpy.unique` over 1.95 M hashes) rather than strided sampling; the two sampled quantities are strided (`KEYS[::48]`), labelled, and validated to ±2 %. |
| D6 | **Yes, and in the right order** | The IDT→UTC anchor was confirmed in phase 0 (`run-identity.md`) **before** any worker was dispatched, exactly as D6 required, so no counting was done against an unverified clock. |
| D7 | **Changed — route substituted, outcome achieved** | The wheel could not be confirmed by inspection: `GetObject` on `s3://cym-glue/stg/libs/parsers.whl` → **403**. Recorded as blocked in `execution_notes.md`, and replaced by the driver log's correlated-lane strings, which is stronger evidence anyway (what ran, not what shipped). D7's stated risk — "the code path analysed is not the code path that ran" — is retired. |
| D8 | **Not exercised** | No `ExpiredToken` occurred; all three live-surface workers confirm one valid credential throughout. Several `AccessDenied` results were reported as gaps, not worked around — the spirit of D8 held. |
| D9 | **Yes** | §8 names targets and rough cost, opens with "Not designed here — this is scope for a follow-up contract", and designs nothing. |
| D10 | **Yes** | `batchsize`-is-not-a-lever is carried into §4 ("does **not** widen the statement"), §7 factor 5 ("the memory cost of a huge batch and none of the benefit"), and §8 ("50,000 buys memory pressure and nothing else"). |
| D11 | **Yes, with the acknowledged limit** | The 08003 is explicitly labelled an artifact (§5 "do not tune against it"); the **first** executor exception (12:57:31.333) was used; the primary cause was recovered from sibling runs (`server closed the connection unexpectedly` ×10, `EOFException`, `I/O error … sending to the backend` ×4, `Timed-out waiting to acquire database connection` ×6). D11's other half — the PostgreSQL server log for the same instant — was **unobtainable**: no `/aws/rds/cluster/stg-pg-cybi-aurora-cluster/postgresql` log group exists. Correctly reported as a gap and as the top fix-direction item. |

---

## What a future task must not re-assume

1. **That ~141 AAS was the peak.** The measured peak is **327.46 AAS at 13:04Z** on 8 vCPUs (**41:1**), baseline ~3. 126.35 and 141.45 are *window-mean* AAS on the INSERT, not peaks. Anyone sizing an instance or a rate limit off 141 will under-provision by 2.3×.
2. **That ~1.5 % of the payload was written.** That number multiplies a 35-minute `WriteThroughput` mean by a 383-second window and equates Aurora storage bytes with `jsonb` bind bytes. Use the solid fact instead: **0 of 436 write-task attempts succeeded** — and treat "did any rows commit, and did the 3 `append` retries duplicate them" as **open**, because `tup_inserted` ran 694–5,157 rows/s and the INSERT held 109.54 AAS *after* the abort. A `COUNT(*)` for this client/instance settles it.
3. **That the screenshot's 1.39 calls/sec and 177.29 rows/sec describe this run's throughput.** They do not — the run pushed ~15,300 rows/s (~120 calls/s) during the write. Only their *ratio* (127.5 rows/call) is meaningful, and it is meaningful only because it matches the pgjdbc cap. Do not build another cost model on 0.921 ms/row without an independently measured per-row cost.
4. **That ~499 statements were in flight (A12).** Measured: 37 concurrent write tasks, 457 proxy connections across the storm, 570 peak connections. PI AAS is not a count of parser writers.
5. **That concurrent Postgres connections equal the partition count (A10).** They equal `min(partitions, task slots)` = `min(100, 9 executors × 4 cores)` = **36**. Above 5 M rows, extra volume becomes *more rows per connection*, never more connections — `MAX_PARTITIONS = 100` and `ROWS_PER_PARTITION = 50000` stop agreeing at exactly 5,000,000 rows.
6. **That the 11-index list is STG's live index set (A13).** It is `cybi-db-models` at local HEAD 98b95949 (2026-05-06). And the *cost share* of index maintenance was never measured — "11 indexes are a material share of the INSERT CPU" is still an inference. `\d+ integration.parser_output_exposures` plus `avg(pg_column_size(additional_fields))` are both reads and would settle both halves.
7. **That the ISB Tenable.io collector has no checkpoint/resume path (A1).** It has one; it declined it on this run (`Adapter declined resume from checkpoint … Starting fresh`) and `Resume cancelled` warnings exist on 2026-08-18. Duplication must keep being *measured* in S3, never inferred from an absent feature. The AgentService ledger row never transferred and should not be cited as if it had.
8. **That the write goes direct to Aurora.** It goes through **RDS Proxy** `stg-cybi-aurora-cluster-proxy`, which for this workload provides **no pooling benefit** — 536 session-pinning warnings mean every session held a dedicated backend, and each reset cost a `DEALLOCATE ALL` (7.61 AAS, the instance's 2nd-heaviest statement). The proxy config (`ConnectionBorrowTimeout`, `MaxConnectionsPercent`) is still **unread** (`DescribeDBProxies` AccessDenied) while one sibling error names its borrow timeout.
9. **That memory exhaustion or DB saturation triggered the failure.** Ordering rules it out: first failure 12:57:31 with **11.7 GB freeable and 52 % CPU**; the collapse to 278 MB *starts* a minute later. Memory exhaustion is the retry storm's product. A fix aimed only at instance size would not prevent phase 1.
10. **That the 22.6 s failure floor is explained.** It is measured (n=331, 81 % in 22.6–30 s) but unexplained — "dying on the first `executeBatch`" is **speculation**, and the report's root-cause sentence states it unlabelled. The cheapest reads that would close it are still undone: `idle_in_transaction_session_timeout`, `transaction_timeout`, `statement_timeout` from the parameter group, and the PG server log (no log group exists — enabling the `postgresql` export is the top observability item).
11. **That this evidence can be re-verified later.** All three `/aws-glue/jobs*` groups have **3-day retention** and the two richest are `INFREQUENT_ACCESS` (Insights-only). **Glue evidence expires 2026-08-22**; PI retention is 7 days. Extracts are in the session scratchpad only.
12. **Practical note:** `research/pgjdbc-batch-rewrite.md` contains a non-UTF-8 byte — `file` calls it `data` and plain `grep` silently returns nothing. Use `grep -a`.
