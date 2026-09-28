# Handoff: task 5 only — measurement continuity across structured findings

## Assignment and worktree

Continue in `/Users/user/.codex/worktrees/structured-findings-contract/AILedger`, branch
`codex/structured-findings-contract`. Inspect `git status` first and preserve subsequent changes.
Task 1 is `a62053c`, task 2 `00270fb`, task 3 `57f9dcb`; locate the task-4 commit with `git log`
(it contains this handoff). Do not install the global tool, copy into the original checkout, merge
or publish. Build outside the checkout.

The user explicitly disabled the kernel development workflow for this redesign. Do not invoke
ai-kernel, open a governed development task or write development governance records. Kernel
validation inside implementation/disposable tests remains authoritative. Do not change global
instructions/skills or dispatch agents without a new instruction. Follow the .NET skill and the
repository's small-method/SRP conventions.

Implement **task 5 only** of [backlog 63](../../Backlog/structured-agent-interface.md). Task 4 has
real acceptance on both providers. Task 5 connects measurements; it does not expand the operation,
change policy/stages, implement YAML or broader batch operations, run task 6's benchmark, or retire
the workflow. Tasks 6–8 remain separate.

## Read in order

1. [Task-4 validation](../structured-findings-v1/task-4-validation.md), then
   [provider composition/trust](../structured-findings-v1/task-4-provider-adoption.md).
2. [Measurement inventory and frozen baseline](../structured-findings-v1/measurement-baseline.md).
   Verify the 11 frozen hashes before production edits. Read the frozen schemas/examples,
   [task-1 contract](../structured-findings-v1.md), task-2 validation/compatibility limits and
   task-3 endpoint/validation as needed; those contracts are settled.
3. Existing consumers named in the measurement inventory: TaskRetrospective, CoordinatorMeasurement,
   RunCostReader, ProviderRunRecorder, TaskCloseoutEvidence, scoring/assurance consumers, refusal
   readers and applicable Memory ingestion. Determine the narrowest appropriate report extension
   from the current source and backlog acceptance, rather than inventing a second reporting system.
4. New sources: `Storage/Findings/FindingsAttempt.cs`, receipt envelopes, `Cli/Findings/FindingsTransportAttempt.cs`,
   `ProviderFindingsSession.cs`, `ProviderLauncher.cs`; then their tests. `ProviderRunRecorder`,
   usage parsing and existing report implementations were not modified by task 4.
5. [Live probe](../../tools/FindingsProviderProbe/README.md) and its actual acceptance artifacts listed
   in the validation record. They may expire. Re-run real providers only when authorized for the
   new assignment; the tool consumes usage. Never treat scripted provider output as live acceptance.

## Delivered facts and settled boundaries

- Both Claude 2.1.283 and Codex 0.155.0-alpha.9.2 completed a production-launched disposable episode:
  commit, exact stable-key retry, trusted host revocation of AddEvidence, atomic `kernel_refused`.
  Canonical state has one claim/evidence pair and one provider completion; ordinary evidence prose
  survived exactly. Spoofed agent-written host.json had no effect on researcher/R1 attribution.
- Provider composition owns binding/recorder in the trusted launcher's **memory**. It gives each
  client an inline stdio relay registration and the exact record_findings tool grant. The relay
  carries only a loopback port and random connection capability, never authority/configuration.
  Do not replace this with an agent-writable binding JSON, environment identity or provider home file.
- Navigation is independent. Claude uses inline strict MCP configuration; Codex uses a CLI MCP
  override rather than an authority-bearing CODEX_HOME file. Existing shell/filesystem grants,
  navigation hooks, approval modes and role rules are preserved.
- The immutable transport configuration has actual task/subject/run/correlation/optional causation
  and selected provider, but **provider_session_id is null**. Actual provider session is observed
  later and recorded on the provider result/run completion. Do not backfill transport rows or
  interpret requested/preassigned/resume identity as an observation. A run-based enrichment can
  distinguish a joined observation from one captured by the transport.
- Real clients successfully initialized/discovered/called the endpoint. The exact negotiated
  protocol-version string was not retained and remains unobserved. Claude recorded one harmless
  protocol rejection without application entry; its method is not retained. Do not infer it.
- Stable-key retry preserves task, actor, run, correlation, causation, original body and key. A new
  run with different attribution conflicts. Cross-actor receipt recovery remains unsupported.
- The endpoint still calls IFindingsRecorder directly. Task-2 atomic append, receipt recovery,
  task-3 strict parser and frozen envelopes are unchanged. Canonical events remain granular;
  the transaction/receipt is additive envelope metadata, not an extra event.

## Measurement work to finish

Use actual stored fields and keep these distinctions visible:

| Source | Join / interpretation |
|---|---|
| `<task>/findings-attempts.jsonl` | Application `attempt_id`, request/fingerprint, task/actor/run, transaction/event IDs when observed; collection may be unavailable |
| `<task>/telemetry/findings-transport-<connection>.jsonl` | Transport attempt/session, `application_attempt_id`, receipt transaction/event joins, request, run, response delivery, nullable observations; no duplicated prose or provider cost |
| `<task>/events.jsonl` | Canonical receipt on first complete marked findings-group event; task-2 storage owns valid/torn/corrupt group semantics |
| `<task>/runs/<run>.json` and `run.completed` | Original actual provider/session, token buckets, optional turns/model, first write, timeout/truncation/outcome, exact manifest identity/count |
| Refusal journal | Existing command boundary and actor/reason/build; not every earlier validated candidate in a failed batch |

Confirm paths against source before coding; the application journal is directly under the task,
whereas production provider transport files are under `telemetry`. Standalone task-3 hosts can
choose other diagnostics directories. Reports must not assume every historical host used the new
provider path or that a missing directory means zero attempts.

Task 5's acceptance requires:

- Historical readers over frozen inputs remain semantically identical. Additive new fields must
  be deliberate; do not recapture expected reports to hide a difference.
- Distinguish tool attempts, application attempts, canonical transactions, granular events and
  model turns. A lost response plus successful retry is multiple observations and one commit.
- Never multiply run cost by either calls, retries or event count. Preserve Codex's missing turn
  count and missing served model, actual zero cache-write tokens, and all existing coverage rules.
- Transport/application timings nest. Do not sum them into elapsed task time. A local response
  flush is not client acknowledgement; commit outcome and delivery are separate.
- Pre-application refusals have no application attempt ID. Exceptions can lack application
  observations. A crash or failed journal write can omit terminal rows. Missing/truncated/unreadable
  telemetry must produce explicit coverage gaps, not fabricated zeros/success.
- Receipt recovery can be committed even when telemetry is missing; canonical receipt and events
  prove the commit. A denial that cannot inspect an old receipt has unknown commit state.
- Preserve retrospective, scoring, assurance-evidence, manifest and first-write consumers. Name
  unsupported measurements rather than silently dropping them. Do not make recording depend on
  report/telemetry availability or put CollectionStatus in the frozen tool response.

Prefer focused behavioral tests with real disposable logs and deterministic faults. Reuse the
existing repository-aware runner. No new model execution is needed to prove pure report joins
when task-4 observations or suitable fixtures already establish their sources.

## Validation and known limits

Run `dotnet test`, the repository-aware full runner, frozen hash checker and read-only baseline
probe. Task-4 validation records final counts and temporary logs. Standard VSTest with external
artifacts still fails repository discovery: the old failure set plus the new launcher integration
test whose context setup needs the checkout. This is a failed invocation, not a green suite.
The existing independent-clock race in
`ReconsiderationConsultantTests.R8_ConsultationRequiresARecordedSubjectRole` remains unchanged.

General storage readers/writers must be task 2 or newer: task-1 storage rejects singleton findings
markers. Existing durability is process-crash/retry recovery, not power-loss/distributed proof.
The local host is not protection against direct OS process/ledger authority. Relay authentication
failures happen before MCP/application entry and currently have no task-3 attempt row; do not count
absence as an observed successful connection.

Finish with a scoped commit, task-5 backlog status based on actual acceptance, a measurement
compatibility/validation report, and a focused handoff for task 6. Preserve task-4 limitations and
historical fixtures; do not broaden this assignment into the experiments.
