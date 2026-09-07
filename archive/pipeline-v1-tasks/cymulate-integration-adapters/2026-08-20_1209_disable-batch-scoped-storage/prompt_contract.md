Role:
You are a senior .NET engineer maintaining collector emission behavior in `cymulate-integration-adapters`.

Goal:
Disable every production collector opt-in currently setting batch-scoped storage to `true`, while preserving the shared capability and keeping affected collectors/tests correct.

Context:
A production scan identified enabled cases in InsightVM Cloud configuration, Qualys configuration, and the TenableIo correlated findings emitter. Falcon already defaults off and retains an explicit configuration option.

Constraints:

* Change the three identified enabled production cases to `false`.
* Preserve the shared batch-scoped-storage implementation and Falcon's existing default-off option.
* Do not change collector retrieval, parsing, checkpoint, or record semantics beyond storage scoping.
* Update affected tests and misleading comments rather than disabling coverage.
* Preserve unrelated work and follow repository .NET conventions.

Success Criteria:

* No production collector defaults or hard-coded emitter calls set batch-scoped storage to `true`.
* InsightVM Cloud, Qualys, and TenableIo use flat storage scoping by default after the change.
* Tests asserting collector capability/default behavior match the new disabled state.
* Relevant collector test projects build and focused tests pass.
* A production-source scan confirms no remaining literal `true` opt-in.
* Independent verifier and isolated code review report no unresolved correctness issue.

Execution Rules:

* Do not assume missing data.
* Respect constraints strictly.
* Diagnose each collector's configuration and tests before editing.
* Do not remove the shared capability merely to eliminate its current opt-ins.
* Record commands, evidence, and outcomes in task artifacts.

Output Format:
Implement the scoped production/test changes and produce concise execution, verification, and review artifacts under the task directory.

Stop Conditions:

* When the goal is achieved and verification is complete.
* When disabling storage scoping would require an unrequested collector contract redesign.
* When required local dependencies or test fixtures are unavailable.
