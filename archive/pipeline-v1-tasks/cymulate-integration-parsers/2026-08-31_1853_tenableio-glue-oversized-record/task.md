# Tenable.io ingest fails on an oversized NDJSON record

## What is broken

A Glue parser run for the Tenable.io Assets and Findings flow aborts because one NDJSON
record in the staged findings lane is larger than AWS Glue's 64 MiB split size. The whole
staged run is discarded; nothing downstream runs. The client's data goes stale silently.

## Evidence (established, do not re-derive)

- prod-eu. correlation_id `b882ac4f-1ebd-40d5-8159-48e169ec8f2e`, client `637bcc2885fc6e40413b86ce`,
  instance `8bc0a7db-4688-422a-97a2-58b7e49ad5bd`, flow "Tenable.io - Tenable.io Assets and Findings".
- Error: `com.amazonaws.services.glue.util.NonFatalException: Record larger than the Split size: 67108864`
  from `TapeHadoopRecordReaderSplittable`, surfacing as `Py4JJavaError` at `.toDF()`.
  Task 1133 in stage 0.0 failed 4 times.
- Call path: `tenableAssetsAndFindings.py:418` `pre_process` -> `helpers.fetch_df_from_file`
  -> `helpers.py:43-49` `context.create_dynamic_frame.from_options(format="json", multiline=False, ...).toDF()`.
- Offending record: `findings_000849.json` line 31 = 67,195,203 bytes (64.082 MiB).
  uuid `3012fc51-9d75-4127-9f4c-ec2d5d1d53f0`, chunk 0, isLastChunk true, findingsInChunk 210.
  Two findings carry giant `output` strings: 34.08 MiB (plugin 167251) and 18.38 MiB (plugin 167252).
  Median finding 2.6 KiB.
- `output` is 79.4% of that record but only 6.5-25.1% of whole-file bytes. Tail problem, not volume.
- Root cause: `TenableIoVulnPhase.cs:85` `FindingsPerEnvelopeCap = 2000` caps envelopes by COUNT, never bytes.
- Scale discarded: 431.7 GiB across 167,995 objects.
- Collector writes `host` on chunk 0 ONLY (`TenableIoVulnPhase.cs:500`
  `carriesHost = slice == 0 && host is not null`), confirmed against real S3 data — chunks 1..N
  carry no `host` key at all.

## What to build

Three items, in two repos. See `prompt_contract.md`.

1. `cymulate-integration-parsers` — scoped Glue reader fallback. This is the floor: it alone unblocks the client.
2. `cymulate-integration-adapters` — field-agnostic byte budget on envelope slicing. Prevents recurrence.
3. `cymulate-integration-parsers` — correct the false host-per-chunk docstring and filter null hosts out of the assets lane.

## Known limit that must survive into the design

A byte budget cannot bound a SINGLE finding — one finding is atomic. A single finding over
64 MiB would still break the Glue reader. Item 1 is therefore the floor, not an optional extra.
The design must not claim item 2 removes the need for item 1.
