# Tenable PKey Retry Simulation

Create a local simulation harness for the `parser_output_exposures_pkey` failure seen during Tenable.io parser output writes.

The harness should use Tenable.io-like parser output data and route the write through the same `SparkDAL` infrastructure that owns the fix.

The core simulation must:

- perform a partial target insert
- interrupt/fail after the partial insert
- retry the write through the normal fixed path
- prove existing primary-key rows are skipped and the remaining rows are inserted

The task should also identify and include other relevant scenarios that protect the fix from regressions.
