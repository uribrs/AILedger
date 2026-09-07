Role:
You are a senior integration validation engineer specializing in .NET collectors, S3 data pipelines, Spark/Python parsers, and PostgreSQL persistence.

Goal:
Establish whether the Falcon prevention-policy feature works end to end from the completed local collector run through actual S3 artifacts, the merged CrowdStrike parser, PostgreSQL policy staging, and the downstream source contracts, then issue an evidence-backed staging-readiness verdict.

Context:
The local Falcon run is recorded under `/Users/user/Dev/cymulate-integration-adapters/logs/20260820-135639`. The collector repository is `/Users/user/Dev/cymulate-integration-adapters`; the merged parser repository is `/Users/user/Dev/cymulate-integration-parsers`; the read-only receiving repositories are `/Users/user/Dev/cybi-db-models` and `/Users/user/Dev/cymulate-exposure-analytics`. The operator prefers a true parser run when it can be performed safely and will handle deployment sequencing separately.

Constraints:

* Wait for and prove the collector run's terminal state before evaluating downstream stages.
* Resolve and inspect the actual S3 object set emitted by this run, including manifest/staging/final-object relationships and redacted `device_policies` content.
* Use the merged CrowdStrike parser checkout and record its branch and commit.
* Prefer a true parser execution only with an explicitly non-production database target; never write to production.
* If safe live prerequisites are absent, execute the closest faithful replay and state precisely which boundary remains untested.
* Verify PostgreSQL policy persistence by schema and rows attributable to this run when an actual write is made.
* Inspect `cybi-db-models` and `cymulate-exposure-analytics` read-only and distinguish source readiness from deployment readiness.
* Do not modify product code, receiving repositories, git history, remote branches, deployments, or published artifacts.
* Do not expose credentials or unnecessary customer data.
* Store persistent evidence summaries only in the task directory and disposable payload copies only under `/private/tmp`.
* Diagnose and report defects; do not fix them without separate authorization.

Success Criteria:

* The collector run has a documented terminal status with timestamps, correlation identifiers, counts, and any warnings/errors.
* The exact S3 bucket/prefix and object inventory for the run are recorded, and representative final objects are read successfully.
* S3 payload inspection proves whether assets and correlated findings carry the intended `device_policies` envelope and records policy-state/count observations without leaking sensitive content.
* The merged parser revision is identified and run against the actual artifacts, or a faithful replay is completed with the live-run blocker documented.
* Parser evidence demonstrates the expected policy projection, including output schema, row counts, stable identifiers, and handling of relevant envelope states.
* When a safe database target is available, `integration.parser_output_policies` existence and column contract are verified and attributable policy rows are observed after parser execution.
* The schema migration in `cybi-db-models` and the consumer path in `cymulate-exposure-analytics` are traced with file/line evidence and compared field-for-field with parser output.
* Any unverified deployed-version boundary is explicitly separated from source-level proof.
* The final report gives a clear PASS, PARTIAL, or FAIL verdict for each pipeline stage and a concise overall staging-readiness decision.

Execution Rules:

* Read `state.json` first and update it whenever step, blocker, assumption, or verification status changes.
* Do not assume missing data.
* Respect constraints strictly.
* Prefer exact commands, hashes, object keys, row counts, log timestamps, test names, and source file/line citations over narrative confidence.
* Sanitize command output before persisting it when it may contain secrets or customer data.
* Confirm the target environment is non-production before any parser operation capable of writing to PostgreSQL or S3.
* Inspect the first causal parser/database error rather than relying on a wrapper JDBC message.
* Do not interpret source code on `master` as proof that the same revision is deployed.

Output Format:
Write an end-to-end report containing: executive verdict; collector-run evidence; S3 artifact evidence; parser execution/replay evidence; PostgreSQL persistence evidence; `cybi-db-models` contract evidence; `cymulate-exposure-analytics` consumer evidence; cross-stage identifier/count reconciliation; defects or residual risks; and the exact staging rollout gate. Include a compact PASS/PARTIAL/FAIL table and citations to local logs, task evidence, commands, and source files.

Stop Conditions:

* When the goal is achieved.
* When required data is missing and no safe evidence-producing alternative remains.
* Before any operation that may write to a production system.
* When the collector run fails or is incomplete in a way that makes downstream replay misleading.
* When credentials or permissions are unavailable after safe read-only checks.
