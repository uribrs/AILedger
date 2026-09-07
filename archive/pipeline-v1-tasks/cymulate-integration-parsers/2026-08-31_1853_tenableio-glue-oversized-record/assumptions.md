# Assumptions

All entries are OPEN. The contract-designer holds no evidence and may not write any other status.

## Prior Art

Tags: `cymulate-integration-parsers`, `cymulate-integration-adapters`, `tenable`, `glue`, `spark`, `envelope-chunking`.
Matched 7 rows after head-filtering (`L-57acd253` dropped — superseded by `L-9c94d22d`). No truncation.
Followed 3 task directories. Four `verify:` commands were re-run today and all re-confirmed.

- **A1 OPEN** — The reported Glue Py4J error names the true cause of the failure.
  Our message nests an executor-side `NonFatalException`, so it looks named — but prior art
  showed Spark's `savePartition` finally-block can DISCARD the primary exception and report a
  secondary one instead. Confirm the split-size exception is the first executor-side failure,
  not a mask.
  source: lessons.md#L-9a2a1c4d (researcher, 2026-08-19)

- **A2 OPEN** — The Tenable `output` field is carried into `additional_fields` because it is a
  root field absent from the exclusion set.
  Re-ran the lesson's verify today: `tenableAssetsAndFindings.py:560,569,573` still present, and
  the exclusion set is `{"asset","plugin"}` plus root-level mandatory paths, which does not
  include `output`.
  source: lessons.md#L-b345971b (verifier, 2026-08-19)

- **A3 OPEN** — Tenable.io HAS a checkpoint/resume path, so changing envelope slicing interacts
  with resume. Re-ran verify today: `TenableIoCollector.cs:34` declares `IResumableAdapter`,
  `:448` `CanResumeFrom`, `:463` routes to `TenableIoCorrelatedCheckpoint.CanResumeFrom`.
  A run resumed from a checkpoint written by the OLD slicing could re-slice differently.
  source: lessons.md#L-9c94d22d (verifier, 2026-08-27)

- **A4 OPEN** — A cap or ceiling must be sized against measured vendor data, never an assumed
  length. Prior art measured 89 chars where 65 was assumed and every derived figure had to be redone.
  source: lessons.md#L-ee097772 (verifier, 2026-08-30)

- **A5 OPEN** — A control artifact carrying a growable list is not bounded just because a sibling
  artifact is. Directly analogous to the byte budget not bounding a single finding.
  source: lessons.md#L-35b88c57 (verifier, 2026-08-30)

- **A6 OPEN** — A deploy is verified by grepping the driver log for a log string unique to the
  code path, not by downloading and inspecting `parsers.whl` (GetObject is 403 to the operator's role).
  source: lessons.md#L-727045fe (executor, 2026-08-19)

## Task-specific

- **A7 OPEN** — Native Spark `spark.read.json(..., multiline=False)` has no per-record split-size
  limit: `LineRecordReader` reads a line across split boundaries, bounded only by
  `mapreduce.input.linerecordreader.line.maxlength` (default `Integer.MAX_VALUE`). This is the
  entire premise of item 1 and is currently held on general Hadoop knowledge, not on a citation.

- **A8 OPEN** — The DataFrame produced by the `spark.read.json` fallback is schema-compatible
  enough with the `DynamicFrame.toDF()` output that downstream Tenable parsing is unaffected.
  `DynamicFrame` resolves type conflicts with choice types; `spark.read.json` infers by sampling
  and can emit `_corrupt_record`. Divergence is plausible and untested.

- **A9 OPEN** — `fetch_df_from_strategy` (`helpers.py:87`) shares the identical Glue reader risk
  and needs the same fallback.

- **A10 OPEN** — `TenableIoCorrelatedCheckpoint` does not record run position in a way that a
  changed envelope-slicing boundary invalidates on resume (i.e. it does not key on chunk index
  or emitted-envelope count). If it does, item 2 can duplicate or skip findings on a resumed run.

- **A11 OPEN** — A ~16 MiB byte budget keeps every record under the 64 MiB split size for observed
  data. Expected new ceiling is `budget + largest single finding`; largest observed single finding
  is 34.08 MiB. Must be checked against measured data per A4.

- **A12 OPEN** — `create-entities-tp` does not already cap `additional_fields` size
  (`cymulate-exposure-analytics` `apps/create-entities-tp/.../parsed-data.repository.ts`).
  Affects only the urgency of the out-of-scope `output` cap, not this task's build.

- **A13 OPEN** — The all-null asset rows produced by continuation chunks collapse to a single junk
  row under `dropDuplicates(["_asset_correlation_id"])` at `tenableAssetsAndFindings.py:526`, and
  are not otherwise persisted. If wrong, item 3 is a correctness fix rather than hygiene.

- **A14 OPEN** — An existing unit-test project covers the Tenable.io collector in
  `cymulate-integration-adapters`, so the `SliceEnvelopes` test has a home and does not require
  new test infrastructure.

- **A15 OPEN** — Only one oversized record exists in the affected run. 8 of 4,302 findings files
  were scanned (chosen by file size); the other 4,294 were not. The fix must not depend on there
  being exactly one.
