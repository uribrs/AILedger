Role:
You are a senior data-platform engineer familiar with AWS Glue, S3-based ingestion, and Python/Spark parser projects.

Goal:
Create a practical runbook explaining how Glue is triggered from `/Users/user/Dev/Uri/localprojects/RunGlue` and what files must be uploaded to S3, and how, to run `/Users/user/Dev/cymulate-integration-parsers/` through the EPC Glue workflow.

Context:
The RunGlue project is a local Python CLI that resolves environment, integration, flow, identifiers, and S3 input path, then optionally calls AWS Glue `start_workflow_run`.
The parser project is `/Users/user/Dev/cymulate-integration-parsers/`.
The planning output belongs under `/Users/user/Dev/Uri/Planning`.

Constraints:

* Keep planning artifacts under `/Users/user/Dev/Uri/Planning`.
* Do not modify product code.
* Do not trigger AWS Glue or upload to S3.
* Base conclusions on local repository evidence.
* Mark unresolved environment-specific or AWS-specific details as residual risks.
* Include concrete AWS CLI examples as examples only.

Success Criteria:

* The runbook explains the practical trigger path from local command to Glue workflow run.
* The runbook identifies the RunProperties that matter to the Glue jobs.
* The runbook explains the S3 bucket/key relationship, including `READ_BUCKET_NAME` and `ZIP_FILE_KEY`.
* The runbook states what local input files should be packaged/uploaded for parser execution, grounded in parser project evidence.
* The runbook includes a repeatable dry-run, upload, execute, monitor, and diagnose flow.
* The runbook lists unresolved assumptions or risks clearly.

Execution Rules:

* Do not assume missing data.
* Respect constraints strictly.
* Prefer `rg` for code search.
* Keep evidence references to exact local file paths.
* Update task state and execution notes after execution.

Output Format:
A Markdown runbook file in `/Users/user/Dev/Uri/Planning`, plus updated task state files.

Stop Conditions:

* When the runbook is created and validated against local code.
* When required data is missing and cannot be safely inferred from local repositories.
