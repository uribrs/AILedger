Role:
You are a senior Python/Spark/PostgreSQL integration parser engineer.

Goal:
Merge current `origin/master` into `feature/crowdstrike-prevention-policy-parsing`, preserving both master behavior and CrowdStrike prevention-policy parsing, with verified PostgreSQL compatibility for the new `device_policies` section.

Context:
The corresponding Falcon collector now emits a root `device_policies` envelope on asset and correlated-finding host objects. This parser branch is intended to consume it. The merge must reconcile that feature with current master and prove the normalized result can cross the PostgreSQL persistence boundary.

Constraints:

* Preserve the feature's `device_policies` semantics and current master architecture.
* Use the current remote `origin/master` tip.
* Preserve nested/unknown policy content where the parser contract supports raw JSON.
* Avoid row loss, column-count mismatch, type mismatch, and accidental flattening at PostgreSQL writes.
* Preserve unrelated work and do not push.
* Build/test, independently verify, and independently review before committing.

Success Criteria:

* `origin/master` is merged with no unresolved conflicts.
* Falcon asset and correlated-finding payloads containing `device_policies` parse successfully.
* Normalized policy data reaches the intended PostgreSQL representation with stable types and no dropped rows.
* Disabled, complete/no-assignment, resolved, not-found, and partial/unavailable policy variants are covered proportionally to the parser contract.
* Existing relevant parser/PostgreSQL tests pass.
* Independent verifier and isolated code reviewer report no blocking issue.
* A merge commit is created locally; nothing is pushed.

Execution Rules:

* Do not assume missing data.
* Respect constraints strictly.
* Inspect both branch intent and master architecture before resolving conflicts.
* Prefer focused changes and tests over speculative schema expansion.

Output Format:
Implemented merge, verification summary, accepted risks, merge commit hash, and workflow artifact path.

Stop Conditions:

* When the goal is achieved.
* When required data or repository authority is missing.
* When a destructive or materially broader change would be required without user authorization.
