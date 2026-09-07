Role:
You are a senior .NET engineer maintaining TenableIo collector configuration.

Goal:
Move TenableIo correlated findings' batch-scoped-storage boolean into collector configuration, defaulting to `false`, and wire it to the emitter.

Context:
The current worktree already changes the TenableIo emitter literal from `true` to `false`, alongside unrelated-in-scope InsightVM Cloud and Qualys boolean flips. The requested refinement is to replace TenableIo's literal with a configuration property like the other collectors.

Constraints:

* Keep `BatchScopedStorage` defaulted to `false`.
* Use existing TenableIo configuration and flow-construction patterns.
* Keep edits within TenableIo source/tests.
* Preserve existing InsightVM Cloud and Qualys changes untouched.
* Do not change storage-layout or recovery semantics.

Success Criteria:

* TenableIo configuration exposes `BatchScopedStorage` with default `false`.
* The correlated findings emitter receives the configured value rather than a literal.
* Tests prove the default and propagation/wiring.
* The TenableIo test project passes and the solution builds.
* Independent verifier and code reviewer report no unresolved issue.

Execution Rules:

* Do not assume missing data.
* Respect constraints strictly.
* Inspect configuration and flow construction before editing.
* Keep the implementation minimal.

Output Format:
Implement the narrow TenableIo configuration wiring and record concise verification artifacts.

Stop Conditions:

* When the goal is achieved and verification is complete.
* When the flag cannot be propagated without an unrequested public contract redesign.
