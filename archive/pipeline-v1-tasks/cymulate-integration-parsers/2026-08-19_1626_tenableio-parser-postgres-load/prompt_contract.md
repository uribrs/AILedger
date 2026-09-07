# Prompt Contract — Tenable.io parser Postgres load investigation

## Role

You are a senior data-platform engineer who diagnoses PySpark/AWS Glue write pathologies against
Postgres, and who reads Cymulate's collector → S3 → Glue parser → `integration.parser_output_*`
pipeline end to end.

## Goal

Explain, with real numbers from live evidence, why the Tenable.io parser's write to
`integration.parser_output_exposures` produced ~280 AAS on a ~10-vCPU STG Postgres instance — naming
the root cause, ranking the contributing factors, and separating row **volume** from write **shape**.
Deliver a report. Change no code.

## Context

- Repo: `/Users/user/Dev/cymulate-integration-parsers` (branch `feature/tenableio-correlated-parser`,
  baseRef `a9f6631`). PySpark ETL running as AWS Glue 4.0, shipped as `parsers.whl` via S3 zipimport.
  STG runs the wheel built from `master`.
- Parser under investigation: `parser_key` `tenable-assets-findings`. Its YAML spec is a delegate shell
  → `parsers.deprecated.tenable.tenableAssetsAndFindings` (split / legacy-hydrated) and
  `...tenableAssetsAndFindingsCorrelated` (correlated envelope: `{host, findings[], chunk,
  isLastChunk}`).
- Shared shaping lives in `libs/packages/parsers/common/base_parser.py` (`post_process`: normalization,
  control-char sanitization, **CVE explode — one finding row per CVE**, drop of `type=vulnerability`
  findings with no CVE). DB write path and table names: `dal/dbManager.py`; output schema:
  `libs/packages/parsers/schemas/db_schema.py`.
- Observation, run identity, and the four numbered questions: see `task.md`.
- Prior art and every belief this task must not take on faith: see `assumptions.md` (A1-A15).
- Hard boundaries: see `constraints.md`. Investigation decisions: see `decisions.md`.
- Available MCP surfaces: `aws-services-mcp` (RDS/PI/CloudWatch/EC2, read-only), `k8s-agent`
  (Elastic `es_esql` for ISB collector logs, pod logs), `mongodb` (stg), plus `aws` CLI for Glue and S3.
  Repo skills `isb-run-triage` (correlation-ID triage) and `parser-ci-status` (what shipped to STG) are
  read-only and in scope. `trigger-epc-workflow` and `glue-workflow-rerun` are **out of scope** —
  they mutate.

## Constraints

- Read-only against every live environment. No writes, no job triggers, no DB or Mongo mutation.
- No parser code changes. No commits.
- Every number carries its source: `file:line`, Glue log line, S3 listing, PI/CloudWatch metric, or
  Mongo document. Speculation is labelled **speculation** inline.
- Anchor all timestamps to UTC and state the conversion; `aws s3 ls` prints IDT (UTC+3).
- Duplication claims come from strided sampling across the whole key space plus one fully-read shard —
  never from a contiguous prefix.
- Code comments are not evidence of runtime behavior.
- Do not call a run complete or dead from Elastic `MAX(@timestamp)` alone (5-8 min ingest lag).
- On `ExpiredToken`, stop and ask the operator; Claude cannot run `aws-azure-login`.
- Keep `parser_output_assets` and `parser_output_exposures` counts strictly separate.
- Answer all four questions in `task.md`. Partial coverage must be stated explicitly, not silently
  narrowed.

## Success Criteria

1. **Run identified**: Mongo `_id` `6a8593320f3d4281bd8a0df0` resolved to client + instance +
   integration + collector run, and to the specific Glue `cybi-parser` job run whose write window
   overlaps the PI spike, with both windows stated in UTC.
2. **Volume quantified**: findings published by the collector (records in S3), findings after parsing,
   and exposure rows written — each an actual number with its source; every multiplier between them
   named and sized (CVE explode factor, chunk/envelope duplication, correlated re-emission,
   per-port/per-plugin rows).
3. **Write shape characterized**: the statement form that reaches Postgres (single-row vs multi-row and
   how many rows per statement), the JDBC/batch configuration with `file:line`, the number of
   concurrent writer partitions, and whether that arithmetic reproduces 1.39 calls/sec @ 177.29 rows/sec
   @ 117.43 ms/call. A11/A12's arithmetic is confirmed or refuted against real configuration.
4. **Attribution**: the ~280 AAS peak decomposed — how much is the exposures INSERT (58.59 AAS
   accounted for), how much is other load, and how much of the INSERT cost is volume vs shape vs
   per-row index/constraint maintenance on `integration.parser_output_exposures`.
5. **Baseline**: this run compared against at least one other run of the same parser or a comparable
   parser at similar volume, supporting an explicit verdict — anomalous or steady-state.
6. **Root cause stated in one sentence**, followed by ranked contributing factors, each with its number.
7. Every assumption A1-A15 disposed VALIDATED / REJECTED / NEVER-TESTED with actor and citation.
8. Fix direction named (what a fix would target, rough cost) without designing or implementing it.

## Execution Rules

- Do not assume missing data. If a surface cannot be reached, say so and mark the dependent assumption
  NEVER-TESTED rather than substituting inference.
- Respect constraints strictly; read-only means read-only.
- Diagnose before concluding. Correlate data → simulate the mapping → diff against code — do not
  explain a symptom with the first plausible mechanism.
- Compare sibling flows as a controlled experiment where a baseline is needed (e.g. Tenable.sc,
  CrowdStrike, Qualys runs of similar row count).
- Prefer measured counts over inferred ones; when only inference is available, state the inference and
  its error bound.
- Plain language: use the words the code, logs and console already use. No invented shorthand.

## Output Format

Report, in this order:

1. **Root cause** — one sentence.
2. **The run** — client/instance/integration, collector window (UTC), Glue run id + window (UTC),
   PI spike window (UTC), and the clock conversion used.
3. **Volume** — table: collector records → parsed findings → exposure rows, each with source, with
   every multiplier named and sized.
4. **Write shape** — statement form, rows per statement, batch size, writer concurrency, all with
   `file:line`; the arithmetic against the PI metrics.
5. **Attribution** — volume vs shape vs index/constraint cost; what accounts for the ~280 AAS peak.
6. **Anomalous or steady-state** — the comparison and the verdict.
7. **Ranked contributing factors** — each with its number.
8. **Fix direction** — what a fix targets, rough cost. No design.
9. **What could not be measured** — gaps, and the risk each leaves.

Decision points clustered; bullets over paragraphs; action items last. No narration of the
investigation process in the body.

## Stop Conditions

- Goal achieved: all eight success criteria met and the report written.
- AWS/Mongo/Elastic credentials expired — ask the operator; do not proceed on repo-only reasoning.
- Mongo `_id` does not resolve, or resolves to a run whose window cannot be reconciled with the PI
  spike — report the mismatch rather than analysing the wrong run.
- Evidence requires a write to obtain — stop and ask.
- The measured numbers contradict the operator's premise that load is excessive — report that finding
  rather than searching for a cause that fits the premise.
