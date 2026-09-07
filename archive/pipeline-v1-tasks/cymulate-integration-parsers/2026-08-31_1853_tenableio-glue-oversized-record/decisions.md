# Decisions

- Item 1 (parser reader fallback) is the floor and ships regardless of item 2. A byte budget
  cannot bound a single atomic finding, so only the reader change removes the failure mode.
- Item 1 and item 2 are independently shippable. Item 1 alone unblocks the affected client.
- The byte budget lives in the collector, not the parser, because the collector already holds
  the byte lengths and needs no vendor-field knowledge to use them.
- The `output` cap is NOT built. It reaches customers via a documented external API field, so it
  is a product decision, not an engineering cleanup.
- Field-selective capping in the collector is rejected outright: it would force a parse and a
  vendor model into a component whose design property is byte pass-through, and it addresses only
  ~15% of bulk volume while the actual failure is a tail outlier.
- Glue split-size tuning via `SparkConf` is rejected: `JobBase` is shared by every integration's
  parser job, so the blast radius is repo-wide OOM risk for one client's record.
- The fallback is scoped to the split-size exception rather than replacing the reader globally,
  because `fetch_df_from_file` has 33 call sites across 20+ parsers.
- Proceeding on unverified: native `spark.read.json` has no per-record split limit (A7).
  If wrong: item 1 does not fix the crash and the whole approach collapses to split-size tuning,
  which is out of scope. Verify this first, before writing item 1.
- Proceeding on unverified: the checkpoint does not encode envelope boundaries (A10).
  If wrong: item 2 can duplicate or skip findings on a resumed run, and needs a checkpoint
  version guard before it can ship.
- Proceeding on unverified: the fallback DataFrame is schema-compatible with the DynamicFrame
  output (A8). If wrong: the fallback path silently produces differently-typed columns for
  exactly the clients that trip it — the hardest possible case to notice.
- Item 3 is treated as hygiene, not a correctness fix, on the basis of A13. If A13 is refuted,
  reclassify and re-scope.
