# Prompt Contract

Role:
You are a senior data-pipeline engineer working across a PySpark/AWS Glue parser repo
(`cymulate-integration-parsers`) and a C# vendor-collector repo (`cymulate-integration-adapters`).

Goal:
Make the Tenable.io ingest survive an NDJSON record larger than AWS Glue's 64 MiB split size,
and stop the collector from producing such records. Ship item 1 so the affected client can
ingest; ship item 2 so the failure class does not recur; ship item 3 so the path item 2
exercises more often is correct.

Context:
- Failure: `com.amazonaws.services.glue.util.NonFatalException: Record larger than the Split size: 67108864`
  from `TapeHadoopRecordReaderSplittable`, raised as `Py4JJavaError` at `.toDF()`.
- Reader: `helpers.py:43-49`, `context.create_dynamic_frame.from_options(format="json", multiline=False, ...).toDF()`.
- Offending record: 67,195,203 bytes, 210 findings, two of which carry `output` strings of
  34.08 MiB and 18.38 MiB. Median finding 2.6 KiB.
- Root cause: `TenableIoVulnPhase.cs:85` `FindingsPerEnvelopeCap = 2000` bounds envelopes by
  count, never by bytes.
- Full evidence in `task.md`. Prior art and untested beliefs in `assumptions.md`.
  Rejected alternatives and their reasons in `decisions.md`. Hard rules in `constraints.md`.

Constraints:
* See `constraints.md`. Every entry there is binding.
* The collector must remain field-agnostic — no vendor-field parsing, naming, or remapping.
* The envelope wire format must not change, and `host` continues to ride chunk 0 only.
* The parser fallback must be scoped to the split-size failure and must be a no-op on the
  healthy path for all 33 call sites of `fetch_df_from_file`.
* Branch parsers work off `master`, not the current unrelated feature branch.
* Do not build the `output` cap, the `SparkConf` split-size change, or the concurrent-run guard.
* Resolve A7 (native reader has no per-record limit) BEFORE writing item 1. The entire approach
  depends on it.
* Resolve A10 (checkpoint does not encode envelope boundaries) BEFORE shipping item 2.

Success Criteria:
* `helpers.fetch_df_from_file` catches the split-size `Py4JJavaError` specifically and falls
  back to `spark.read.option("multiline", use_multiline).json(file_paths)`; unrelated
  `Py4JJavaError`s still propagate unchanged.
* `fetch_df_from_strategy` is either given the same fallback, or documented in
  `execution_notes.md` as not sharing the risk, with the reason.
* A test covers the healthy path and the fallback path for item 1.
* `TenableIoVulnPhase.SliceEnvelopes` closes an envelope on the byte budget or
  `FindingsPerEnvelopeCap`, whichever is reached first, using a named constant.
* A unit test proves the emitted envelopes reconstruct the input findings list exactly once,
  in order, with no duplication and no loss — including the case where a single finding exceeds
  the budget on its own.
* `isLastChunk` is true on exactly one envelope per asset, and `host` appears on chunk 0 only.
* The false invariant in `tenableAssetsAndFindingsCorrelated.py:23-26` is corrected to state
  that `host` rides chunk 0 only.
* The assets lane in `pre_process` filters out null hosts.
* Existing tests in both repos still pass. No test weakened or deleted to accommodate a change.
* `execution_notes.md` records which assumptions were tested and how, and explicitly states
  that item 2 does not remove the need for item 1.

Execution Rules:
* Do not assume missing data.
* Respect constraints strictly.
* Verify a belief before building on it when `decisions.md` marks it unverified.
* If A7 is refuted, STOP and report — do not fall back to the out-of-scope split-size change.
* If A10 is refuted, STOP on item 2 and report — do not ship a slicing change that a resumed
  run can duplicate across. Item 1 may still ship.
* If A8 is refuted, report the schema divergence rather than silently accepting it.
* Report any oversized record found beyond the one already identified (A15).

Output Format:
* Code changes in both repos, on appropriately-branched working trees.
* Tests alongside the code they cover, in each repo's existing test project.
* `execution_notes.md` appended with: what changed per repo, which assumptions were tested and
  the evidence, and anything left unresolved.

Stop Conditions:
* When all Success Criteria are met.
* When A7 or A10 is refuted (see Execution Rules).
* When a constraint cannot be satisfied without violating another.
* When required data is missing.
