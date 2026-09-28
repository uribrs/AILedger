# Handoff: task 6 only — measured pilot and delivery decision

## Start here

Work directly in `/Users/user/.codex/worktrees/structured-findings-contract/AILedger`, branch
`codex/structured-findings-contract`. Inspect `git status` first and preserve subsequent changes.
Task 4 is `a9e8fa8`; find task 5's scoped measurement-continuity commit with `git log`. Do not install
the global tool, copy into the original checkout, merge or publish. Build outside the checkout.

The user explicitly turned the kernel development workflow **OFF** for this redesign. Do not invoke
ai-kernel, open governed development tasks, or write development governance records. Kernel rules
inside implementation and disposable fixtures remain authoritative. Do not change global skills or
instructions. Do not dispatch agents. Follow the .NET skill and the repository's small-method/SRP
conventions for any implementation work.

This handoff does not authorize new billable provider episodes. Ask for authorization before running
them unless the task-6 assignment explicitly grants it. Pure readers, prepared-data fixtures and
deterministic failure probes need no model execution. Keep a live trial distinct from a scripted
adapter fixture and from rereading an old live episode.

## Reading order

1. [Task-5 validation](../structured-findings-v1/task-5-validation.md), then
   [measurement contract and usage](../structured-findings-v1/task-5-measurement.md).
2. [Backlog 63](../../Backlog/structured-agent-interface.md), specifically task 6 and its delivery
   decision, then the [survival investigation](../ailedger-episodic-survival-investigation.md) for
   Falcon RN1's prepared findings, source citations, shell baseline and counterevidence.
3. [Task-4 validation](../structured-findings-v1/task-4-validation.md) and
   [trusted provider composition](../structured-findings-v1/task-4-provider-adoption.md).
4. [Measurement inventory/frozen baseline](../structured-findings-v1/measurement-baseline.md).
   Verify all 11 hashes before edits. Read the [settled v1 contract](../structured-findings-v1.md),
   task-2 durability/compatibility and task-3 strict endpoint documents as needed.
5. Relevant implementation/tests: `Core/Findings/Measurement`, storage's
   `FileGovernedTaskService.FindingsMeasurement` and `FindingsObservationReader`, CLI retrospective
   integration, `FindingsMeasurementTests`, and `ProviderFindingsLaunchTests`.
6. [Provider probe](../../tools/FindingsProviderProbe/README.md) and the read-only
   `tools/FindingsBaseline/verify-provider-measurement.py`. The first consumes provider usage;
   the second only checks existing artifacts through disposable copies.

## What task 5 delivers

`retrospective build --findings` adds one optional section; default historical outputs are unchanged.
`--findings-telemetry DIRECTORY` selects a standalone host's diagnostics directory and also enables
the section. Production defaults are `<task>/findings-attempts.jsonl` and
`<task>/telemetry/findings-transport-*.jsonl`. Do not infer no calls from a missing source.

Reports distinguish transport/tool attempts, application attempts, canonical transactions, granular
events, and original per-run model turns. Cost is still measured once per run, with independent
coverage for each token field. Receipts come from the task-2 canonical group reader. Conflicting
joins, missing/truncated/unreadable rows, collection failure and changing canonical snapshots are
explicit gaps. There is no completeness watermark, so attempt counts are observed lower bounds.

The lost-response tests use real recorder/MCP/storage paths: failed response delivery followed by
stable-key retry gives two attempts and one canonical transaction. Blocking either or both journal
paths cannot change recording or recovery. Unknown outcomes stay unknown on the original row even
when a separate canonical join proves a later recovered commit.

Both task-4 live episodes were successfully reread through this report. Each has three tool calls,
three application attempts, one canonical claim/evidence transaction, two granular findings events,
one kernel refusal and one completion. Claude additionally has one retained protocol observation;
its method and exact negotiated MCP version remain unknown. No new provider ran for task 5.

The immutable transport session field stays null. `joinedProviderSessionId` comes from canonical
run completion and is labelled as a join, not a transport observation. Codex turns/served model
remain absent, its cache-write count is the observed zero, and original manifest/first-write/launch
outcome fields remain unchanged. The new reader does not reinterpret raw provider sidecars; the
existing cost reader/launcher remain their owners. The comparison probe checks sidecars against
the preserved acceptance/completion records.

## Task 6 acceptance to pursue

- Derive the historical Falcon RN1 prepared findings read-only, with provenance, and submit them
  into a disposable ledger. Preserve original logs and text; do not alter frozen fixtures.
- Measure elapsed recording, tool/application attempts, retries, errors, fidelity and duplicate/
  partial outcomes. Preserve stable request body/key/attribution for retries. Include early small
  submissions and deterministic failure recovery before any authorized bounded live trial.
- Use the new retrospective section and original measurement consumers. Keep nested intervals
  separate, real provider usage once per run, and local response flush distinct from acknowledgement.
- Compare the controlled prepared-data trial to the documented observational shell baseline;
  state comparability limits. Inspect sampled guardrails using the triggering action, prevented
  consequence where evidenced, recovery delay and lost/altered content. Do not call every refusal
  friction or every recorded event useful progress.
- Produce a delivery decision grounded in observed reliability and recording cost. State whether
  recording elapsed time improved. Do not infer whole-workflow or cognitive improvement.
- Identify potential coherent future batches and reasoning/authority boundaries in the report;
  do not implement new operations as part of the pilot.

Tasks 7–8 are conditional and **not** part of task 6. No YAML, broader batch API, role/stage-policy
change, episode implementation, workflow retirement, global migration or automatic rollout belongs
in this assignment.

## Preserved trust and compatibility boundaries

- Production bindings/recorder live in the trusted launcher's memory. The relay gets only connection
  coordinates and a capability. Do not replace that boundary with agent-writable files/environment.
- Exact Claude/Codex tool grants, shell/filesystem/navigation permissions and every kernel check
  remain in place. No broader grants to make a demonstration pass.
- Task-2 storage or newer is required for general findings-enabled ledger readers/writers; task-1
  storage rejects singleton markers. Typed-reader tolerance is not mixed-writer compatibility.
- Existing durability is process-crash/retry recovery, not distributed or physical power-loss proof.
  This local host is not an OS sandbox against direct process/ledger authority.
- Canonical validation fails closed. A receipt-shaped telemetry row is not proof of commitment.
  Cross-actor recovery is unsupported; a changed run/attribution under the same key conflicts.
- A missing terminal journal row or failed authentication can be invisible. No exact MCP version,
  complete attempt census, client acknowledgement, or direct refusal-ID join is available.
- The report CLI can repair derived projections through its existing state read. For historical
  comparisons, use pure reducers or disposable copies; do not run a mutating experiment on history.

## Verification and completion

Use the commands and final counts in task-5 validation. Run standard `dotnet test`, the existing
repository-aware full runner, the hash checker and read-only frozen report probe for code changes.
The standard external-artifact test host has known repository-discovery failures; report those as
failures. The independent-clock R8 test remains timing-sensitive and unchanged. The repository-aware
runner is a build/xUnit wrapper, not a kernel development workflow. Existing tests need local sockets
and application-data access; do not weaken assertions to fit sandbox restrictions.

Commit only task 6's scoped changes, update its backlog status based on actual acceptance, document
limits honestly, and leave a clear delivery decision. No merge/publish/global installation.
