# Task 7 — structured alternatives recording

This is task 7 in the **respecified install-and-try roadmap**, not the former task-7 episode prototype.
The governing scope is [item 63](../../Backlog/structured-agent-interface.md#7-record-alternatives-directly--client-trial-pending).
Tasks 1–6 were accepted; their latest evidence commit is `69b4301` and acceptance commit is `abd3f49`.
Inspect later commits and worktree changes before acting; do not reset to those checkpoints.

## Outcome

The agent that considered an approach records why it rejected it through a typed tool, with its own
attribution, preserved text, generated identifiers and safe retries. A second agent is not needed
solely because the author cannot file the reasoning. Recording a rejected approach grants no decision
acceptance, claim resolution, scope management or artifact approval powers.

Finish an increment Uri can install and try on a task he actually wants done. Do not start the episode
runtime or all remaining interface tools. The CLI remains available to the operator; this operation
must call the application service directly and become the normal supplied agent recording path.

## Start and constraints

Work in `/Users/user/.codex/worktrees/structured-findings-contract/AILedger`, branch
`codex/structured-findings-contract`. Inspect Git status first and preserve subsequent changes.
Kernel development workflow remains OFF: no ai-kernel, governed development task, development
record, agent dispatch or global instruction/skill changes. Kernel checks inside the implementation
and disposable test fixtures still apply. Follow the .NET skill and small-method/SRP conventions.
Build outside the checkout. Do not merge, publish, migrate old tasks or rewrite old validation records.

Read the backlog's purpose, client loop, stable findings contract, measurement requirements and task 7;
then the relevant accepted interface/validation documents:

- [v1 contract](../structured-findings-v1.md)
- [provider binding and grants](../structured-findings-v1/task-4-provider-adoption.md)
- [measurement continuity](../structured-findings-v1/task-5-measurement.md)
- [actual live review and accepted limitations](../structured-findings-v1/task-6-live-validation.md)
- [external build/test instructions](../structured-findings-v1/repository-discovery-validation.md)

Useful implementation entry points:

- `src/AILedger.Core/Alternatives/Contracts/Commands/RecordAlternativeCommand.cs`
- `src/AILedger.Core/Alternatives/Logic/AlternativeRules.cs` and `AlternativeEventValidator.cs`
- `src/AILedger.Core/Domain/AuthorizationPolicy.cs` and `src/AILedger.Cli/RoleDefaults.cs`
- `src/AILedger.Core/Findings/Contracts/FindingsContracts.cs`
- `src/AILedger.Storage/Findings/FileGovernedTaskService.Findings.cs`
- `src/AILedger.Cli/Findings/` and `src/AILedger.Providers/Adapters/FindingsRecording.cs`

## Implementation boundaries

1. Settle a concise additive contract before coding: bounded alternatives, local keys, optional
   existing decision/lesson links, exact normalization rules, atomic commitment, retry-key scope,
   changed-content conflicts, error paths and receipt mappings. Preserve findings-v1 request/response
   compatibility. Reuse mechanics without inventing a generic arbitrary-command framework.
2. Explicitly support the narrow `RecordAlternative` capability for researcher, worker, verifier and
   code-reviewer roles that author alternatives; retain existing lead/operator support. Enforce the
   actual assignment and trusted endpoint binding on every call. Keep approval capabilities separate.
   Do not modify historical grants or bypass a refused assignment. Existing actors need an explicit
   supported assignment change if the grant is absent; fixtures and guidance must make this visible.
3. Preserve actual source statements/rationale, direction of any defined references and authored
   provenance. Existing lesson-recall and decision-reference requirements remain unless a separately
   evidenced semantic issue needs a scope decision. Do not weaken unrelated rules to pass a trial.
4. Keep command-time validation and replay compatible. Previously legal histories must remain readable;
   do not apply a stricter new rule retroactively. Verify unauthorized/revoked/cross-actor behavior.
5. Integrate the exact tool with Codex and Claude, provider-bound identity and repository guidance.
   Include receipt/retry/restart, concurrent IDs, late invalid references, text fidelity and no-prefix
   failure checks. Fixtures suffice until a billable live episode is separately authorized.
6. Preserve existing historical reports and findings measurements. Add the observations needed to
   inspect the new operation without counting alternative events as findings or multiplying run cost.
   Keep collection gaps and unknown outcomes visible. Document any unsupported observations.

## Verification, installation and client feedback

Run meaningful operation/provider tests and the repository's external `dotnet test` and full runner
for implementation changes. Verify the 11 frozen fixture hashes and read-only report/cost baselines;
never recapture expected outputs to force a pass. Previous full suites passed 1,901 main and 99 Memory
tests; that is historical evidence, not an expected fixed count after adding tests.

Commit only scoped task-7 work and retain actual validation results. Prepare an exact installable
commit and version. The copyable prompt below authorizes installation after verification; when used,
confirm the existing installed identity, install the verified version using the established external
build/pack path, and confirm its new identity. Do not copy a dirty checkout or unrelated work into the
installation. Installation permission covers this tool update, not global instructions or skills.

Then give Uri a short ordinary-task suggestion that exercises alternatives. Uri runs the task.
When its records are available, inspect actual author attribution, original versus accepted text,
refusals, retries, transcription handoffs, time/usage and remaining CLI work. Distinguish “implemented
and installed; client observation pending” from “tried and accepted”. Do not keep launching agents
while waiting for Uri, or invent a successful client trial. Retain failures and resulting scope changes.

The prior global findings installation was 2.0.163 from `cd3f2d9`; it was not refreshed for the task-6
or roadmap documentation. Check rather than assume the installed version is still unchanged.

## Copyable next-step prompt

```text
Implement task 7 only from the respecified Backlog/structured-agent-interface.md:
structured alternatives recording. Follow docs/handoffs/structured-agent-interface-task-7.md.
This is the new task 7, not the old bounded-episode prototype.

Work directly in /Users/user/.codex/worktrees/structured-findings-contract/AILedger,
branch codex/structured-findings-contract. Inspect git status first and preserve later changes.
Tasks 1–6 are accepted; do not redo their completed pilot.

Deliver a typed, bounded, atomic and retry-safe alternatives operation through the existing
application boundary, with generated IDs, trusted actor/run attribution, useful errors and
working Codex/Claude integration. Include the narrowly scoped RecordAlternative capability
for researcher, worker, verifier and reviewer authors, without granting approval powers or
silently changing historical assignments. Preserve existing findings-v1 behavior, historical
replay and measurement. Do not build a generic CLI wrapper or start tasks 8–14.

Kernel development workflow remains OFF: no ai-kernel, governed development records, agent
dispatch or global instruction/skill changes. Follow the .NET skill, build externally, use
disposable fixtures and run the appropriate full checks and frozen-baseline verification.
Do not merge, publish or migrate historical tasks.

After verification, commit the scoped change and install that exact verified version for me;
this prompt authorizes that installation. Confirm the installed identity and tell me what
ordinary task to run to exercise it. I will run the task; then we will inspect how it went.
Do not launch a billable episode without separate authorization. Keep implementation,
installation and client-trial acceptance statuses distinct. Tell me before context gets tight.
```
