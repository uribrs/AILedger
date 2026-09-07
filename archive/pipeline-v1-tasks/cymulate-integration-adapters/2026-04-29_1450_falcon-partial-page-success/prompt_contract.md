Role:
You are a senior .NET engineer working in the cymulate-integration-adapters repository.

Goal:
Implement FalconCollector intermediate exit behavior so a terminal, non-recoverable failure after at least one collected page publishes DONE status with the failure attached as context.

Context:
FalconCollector already publishes paged results and reports completion/failure through Shared collector orchestration. The requested behavior is a partial success policy: when the collector has collected at least one page and then hits an otherwise terminal failure, the host should receive DONE rather than FAILED, with enough error context to understand why collection stopped early. Failures before any page is collected remain normal failures. Retryable/recoverable errors remain unchanged.

Constraints:

* Keep the change scoped to FalconCollector and its tests unless existing Shared APIs already provide the required completion payload mechanism.
* Preserve existing retryable failure handling.
* Preserve normal failed status behavior when no page has been collected.
* Preserve page counters, checkpoint advancement, NDJSON output, and published records.
* Use existing orchestration/completion publishing paths; do not add an alternate host signaling path.
* Attach the error that caused partial completion to the DONE result payload.
* Add a structured log line for the collected window size when partial completion is used.
* Follow existing Falcon/Tenable/Entra collector patterns and local .NET conventions.
* Do not touch unrelated collector behavior.

Success Criteria:

* FalconCollector returns a successful/DONE result when a non-recoverable failure happens after page count is greater than zero.
* The DONE result includes error context from the exception that stopped collection.
* FalconCollector still returns a failed result when the same kind of failure happens before any page is collected.
* Retryable/recoverable errors remain classified and handled as retryable failures.
* A structured log line records the size of the collected window when partial completion is used.
* Regression tests cover after-first-page partial success and before-first-page failure behavior.
* Targeted Falcon tests pass.

Execution Rules:

* Read `state.json` first, then the markdown task files before editing product code.
* Inspect existing Falcon flow, runner, result, and tests before editing.
* Keep implementation small and local; add helpers only where they reduce duplicated or unclear control flow.
* Update task state when step status or verification status changes.
* Do not assume missing data.
* Respect constraints strictly.

Output Format:
Summary of changed behavior, files changed, tests run with results, and any remaining risks.

Stop Conditions:

* When goal is achieved and targeted verification passes.
* When required data is missing and guessing would cause wrong host completion/error semantics.
