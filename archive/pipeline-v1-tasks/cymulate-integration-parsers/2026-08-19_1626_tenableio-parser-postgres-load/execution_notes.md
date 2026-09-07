# Execution Notes

## 2026-08-19 — phase 0 (main thread): run identity + "what changed"

Run identity frozen in `research/run-identity.md`. Clock anchor confirmed: the operator's PI graph is
IDT (UTC+3); the spike 15:58-16:07 IDT = **12:58-13:07 UTC**, inside the Glue parser run
12:41:25-13:03:44 UTC that **FAILED** on `o1845.jdbc ... This connection has been closed`.

### The deploy that changed this parser (main-thread evidence, not a worker's)

| when (UTC) | what | source |
|---|---|---|
| 2026-08-18 13:56:46 | PR #214 `feature/tenableio-correlated-parser` merged to `master` (commit `37bdd02`, parents `a7f1c12` + `a9f6631`) | `git log -1 37bdd02` |
| 2026-08-18 13:58 | Jenkins `github-repos/cymulate-integration-parsers/libs/parsers/master` **#273** starts, SUCCESS, 15m59s | jenkins-agent `get_build_info` |
| 2026-08-18 14:14:37 | `s3://cym-glue/stg/libs/parsers.whl` rebuilt (249,104 bytes) — this IS the deploy | `aws s3 ls s3://cym-glue/stg/libs/` (17:14:37 IDT) |
| 2026-08-18 14:42-14:54 | burst of ~13 concurrent `stg-cybi-parser` runs, mostly FAILED | `aws glue get-job-runs` (17:42-17:54 IDT) |
| 2026-08-19 12:41:25 | the run under investigation starts, on that wheel (`extra-py-files s3://cym-glue/stg/libs/parsers.whl`) | workflow run properties |

`git merge-base --is-ancestor 1e8a880 a7f1c12` → **false**: the Tenable.io correlated commit was NOT in
master before PR #214. So the correlated Tenable.io lane reached STG **for the first time on
2026-08-18 14:14 UTC**, 22 hours before this run. Prior master (build #272, 6 days earlier) had only
the split / legacy-hydrated lanes.

PR #214 diff (`git diff --stat a7f1c12 37bdd02`), 11 files / +848 / -26 — the parser-behavior files are:
- `parsers/deprecated/tenable/tenableAssetsAndFindingsCorrelated.py` (**new**, +100)
- `parsers/deprecated/tenable/tenableAssetsAndFindings.py` (+48/-…)
- `parsers/yaml_engine/specs/tenable-assets-findings.yaml` (+27)
- `parsers/deprecated/crowdstrike/CrowdstrikeAssetsFindingsCorrelated.py` (+50), `crowdstrikeAssetsFindings.py` (+14)

Blocked, recorded: `s3://cym-glue/stg/libs/parsers.whl` cannot be downloaded with the current role
(`ListBucket` allowed, `GetObject` → 403 Forbidden), so the wheel's contents were not inspected
directly. A6 is instead established from the merge → build → wheel-mtime chain above, plus whatever
code path the Glue driver log names (W2).

## Workers dispatched (phase 1, all fresh, all read-only)

- RECON → `research/internal-recon.md` — repo write path + row fan-out
- W1 → `research/evidence-s3-collector.md` — S3 volume, duplication, CVE fan-out
- W2 → `research/evidence-glue-db.md` — Glue logs, Glue/RDS CloudWatch, Performance Insights API, baseline
- W3 → `research/evidence-isb-collector.md` — ISB collector run, full-vs-incremental, requeue

## 2026-08-19 — phase 1 synthesis (main thread)

All four evidence packets returned plus one external-research packet. Reconciliation order per the plan.

**Volume chain closed with two independent measurements that agree exactly.** S3 full-read
Σ`len(plugin.cve)` = 5,858,751; Glue driver `DataFrame row count: 5858751`. Collector-published
findings 1,950,862 confirmed both by ISB `TotalFindingsCollected` and by the S3 full read. Duplication
factor measured at 1.0000 over the entire dataset (not sampled) — so no input duplication, no chunk
fan-out (every host is a single chunk-0 record), and the join dedup provably precedes the join.

**The width term was not in the original hypothesis set and turned out to be half the answer.** The
whole `plugin` object — including the `cve[]` and `cpe[]` arrays that drive the row multiplication — is
copied into `additional_fields` on every exploded row. CVE-weighted that is 14,803 B/row ≈ 86.7 GB for
this run, 4.6× the unweighted mean. `output` is a red herring at 379 B CVE-weighted.

**Shape arithmetic resolved the screenshot's apparent self-contradiction.** pgjdbc 42.3.6 caps a
rewritten multi-row INSERT at 128 tuples (`highestBlockCount = 128`), so 127.5 rows/call is normal
driver behavior and `batchsize=50000` buys only memory pressure. The 1.39 calls/sec vs 58.59 AAS
tension is explained by PI splitting the INSERT across 5 tokenized variants and by the screenshot's
30-minute window diluting a 9-minute event; measured by API the write window is 126.35 AAS on the
INSERT out of 150.98 total (84 %), peaking at 141.45 AAS on 8 vCPUs.

**Causal ordering corrected a wrong first reading.** The first task failed at 12:57:31 with 11.7 GB
freeable and 52 % CPU; the memory collapse to 278 MB starts a minute later. Memory exhaustion is the
*product* of the 436-attempt retry storm, not its trigger. Reported separately in the report.

**The reported error is an artifact.** `This connection has been closed` is thrown by Spark's own
`finally` (`conn.rollback()` before `conn.close()`) on an already-dead connection, destroying the
primary exception — and skipping `close()`, which is why connections leaked 425→570. W2 recovered the
real cause from sibling runs: `server closed the connection unexpectedly`, `EOFException`,
`I/O error … sending to the backend`, `Timed-out waiting to acquire database connection`.

**Main-thread evidence that closed W2's largest self-declared gap.** W2 could not read the index set
without a DB connection. `cybi-db-models` is checked out locally, so the authoritative definition was
read from the migrations instead: **11 B-tree indexes, 3 keyed on random v4 UUIDs, no triggers**
(`research/db-schema-authoritative.md`).

**Cross-check that nothing landed:** measured `WriteThroughput` mean 3.34 MB/s over the 383 s write
≈ 1.3 GB, i.e. ~1.5 % of the 86.7 GB payload. 84 % of database load for 6½ minutes, ~zero committed rows.

Deliverable: `report.md`. No repo code changed; `git status` clean apart from the task directory.

### Not code-bearing

This task produced a diagnostic report only. `git diff a9f6631..HEAD` over the repo is empty and no
config, migration, infra or script artifact was authored, so the code-reviewer pass does not apply
(`workflow.codeReviewerRun` stays false with this reason recorded). The verifier pass is mandatory and ran.

## 2026-08-19 — verifier repairs + specific RCA (main thread)

Verifier pass 1 returned **PASS-WITH-GAPS** (`review/verifier-1.md`): 2 HIGH, 5 MED, 4 LOW. All repaired
in `report.md` / `rca.md`:

- **HIGH** the measured peak (**327.46 AAS at 13:04Z**, baseline ~3, mean 58.62, **41:1** on 8 vCPUs) was
  missing and the 141.45 window-mean was mislabelled "the true peak". Added the 60 s curve; the
  operator's ~280 / 58.59 is corroborated and slightly conservative (measured mean 58.62). Also added the
  finding that the peak is **after** the abort, with `tup_inserted` peaking at 13:07.
- **HIGH** the "~1.5 % of payload written" claim is **withdrawn** — it multiplied a 35-minute
  `WriteThroughput` mean (contaminated by an unrelated 12:46 spike) by a 383 s duration, and compared
  Aurora volume-bytes to uncompressed bind payload. Replaced with "0 of 436 task starts succeeded" plus
  the honest open question on commits.
- **MED** "does the arithmetic reproduce the screenshot? Yes" → ratio yes, absolute rates no (and why).
  **A12's "~499 concurrent writers" explicitly refuted** (measured 37).
- **MED** two W2-labelled speculations were being asserted: the "died on its first `executeBatch`"
  mechanism (now labelled in the root-cause sentence) and the +145-connection attribution (now
  "inference").
- **MED** disclosed that the volume/shape/per-row split is quantified on the volume axis only.
- **MED** error bar added to the screenshot-derived 0.921 ms/row.
- **LOW** added to §8: read `idle_in_transaction_session_timeout` / `transaction_timeout` (cheapest step
  toward the unexplained 22.6 s floor); added the RDS Proxy finding (**457** connections for 36 writers,
  **536** pinning warnings ⇒ no multiplexing benefit, which is why `DEALLOCATE ALL` is the instance's
  2nd-heaviest statement).
- **LOW** the 328+83 corroboration reworded to match W2's own caveats; `state.json` synced.

### Independent of the verifier: the operator asked for a sharper RCA, and it corrected a number

Measuring `additional_fields` against the parser's **actual exclusion set** (all `plugin.*` except the
mandatory paths `{name, solution, cve, description}`, plus root fields except
`{asset, plugin, severity_id, state, first_found, last_found}`) gives **10,437 B per exposure row ≈
61.1 GB**, not W1's 14,803 B ≈ 86.7 GB — that figure was the **whole `plugin` object**, including the
four paths the parser excludes (`plugin.cve` alone ≈ 9.3 GB). Field-level breakdown then located the
defect: **`plugin.xrefs` 35.1 % + `plugin.see_also` 27.8 % = 63 % of the payload**, and the static
per-plugin reference set carries a **≥74× redundancy** — 13.7 MB of distinct content written as
~48.7 GB. Written up in `rca.md`. Scripts: `<scratch>/afmeasure.py`, `affields.py`, `afdup.py`
(strided `KEYS[::48]`, 2.08 %, whole key space).

### The working copy changed branch mid-task — reproducibility note

`state.json.baseRef` is `a9f6631` on `feature/tenableio-correlated-parser`, which is the commit that
merged to master as `37bdd02` and produced the deployed wheel. **During this task the working copy moved
to `feature/crowdstrike-prevention-policy-parsing` (HEAD `60119b33`), which predates PR #214 and does
NOT contain the correlated Tenable lane** — `_correlated_input`, the envelope sniff and
`TenableAssetsAndFindingsCorrelated` dispatch are all absent there. Not done by this task
(`git status --porcelain` clean; `ai/active/` is gitignored per `.gitignore:73`), so presumably the
operator or a peer session switched it.

Consequence, checked rather than assumed: the two regions the RCA depends on are **byte-identical**
between `a9f6631` and the current HEAD, at the same line numbers — the mandatory-field map
(`:343-370`) and the plugin walk / exclusion set (`:560`, `:569`, `:573`), verified with
`git diff a9f6631..HEAD` filtered to those constructs and by re-extracting them from
`git show a9f6631:`. So every citation in `rca.md` resolves against the code that actually ran.
`tenableAssetsAndFindingsCorrelated.py` line citations in `internal-recon.md`, however, will **not**
resolve against the current checkout — use `git show a9f6631:<path>`.
