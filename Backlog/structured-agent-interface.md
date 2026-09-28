# Structured agent work with install-and-try increments

**Backlog ID: 63. Priority: 1. Status: partial — tasks 1–6 accepted; tasks 7–9 implemented and installed; client trials pending. Kind: feature.**

Created 2026-09-28 from the investigation and interface-design discussion. Respecified after Uri
clarified that the CLI is not the intended agent interface and that usable increments should be
installed and evaluated on tasks he runs. Tasks 1–6 remain accepted. This revision defines the next
work; it does not start implementation or install a new version.

## Purpose

Reduce unnecessary agent and client effort while preserving useful knowledge and substantive
safeguards. Agents should record reasoning, submit results, inspect state and discover blockers
through structured operations, without shell syntax negotiation, manual ID allocation, transcription
handoffs or repeated commands that add no judgment.

There are two related objectives: finish the practical structured interface, and determine whether
focused handoffs and bounded execution improve elapsed delivery and retained information. The first
is an agreed direction; the second remains a hypothesis to test in usable increments.

Develop directly, outside the kernel development workflow. Do not invoke ai-kernel, create governed
development tasks or governance records, dispatch development agents, or change global instructions
or skills. Authorization and validation inside the implementation remain enforced and are exercised
in disposable fixtures. Follow the .NET skill and existing small-method/SRP conventions. Build
outside the checkout. Do not merge, publish or migrate historical tasks as part of these increments.

## How Uri tries each increment

Uri is the client. The delivery loop is **build and verify → install the exact version → Uri runs an
ordinary task → inspect the outcome together → choose the next improvement**. Installation is a
useful checkpoint, not a separate programme at the end of the redesign.

For each checkpoint:

1. Finish the end-to-end slice, required checks and a scoped commit. Report capabilities delivered,
   remaining gaps and the version to install. Preserve a known working version and compatibility facts.
2. When the implementation request authorizes installation, install that verified commit and confirm
   the installed version/build identity. Do not reinstall solely for documentation changes. The prior
   findings installation is recorded as 2.0.163 from `cd3f2d9`; check current identity before any update.
3. Give Uri a short description of the ordinary task that will exercise the change. The client task
   is the work Uri wants done, not a required scripted benchmark or a list of ledger commands.
4. Inspect its actual transcript, outputs and receipts. Identify useful refusals, avoidable friction,
   altered or missing information, unnecessary handoffs, remaining CLI operations and manual intervention.
   Attribute usage once per run; distinguish recording time, model/provider time and elapsed delivery.
5. Record implementation/installation status separately from client observation and acceptance.
   If Uri has not run the task yet, say so. Fix demonstrated failures within scope; let feedback shape
   the next slice. Do not invent favourable observations to finish a checklist.

A task-7 implementation prompt can authorize its exact installation in advance; it does not authorize
billable agent trials, later increments or global instruction/skill changes. Ask about an additional
model episode only if needed and not already authorized, with a concrete plan and cost/time boundary.
No separate experiment is required merely to observe a task Uri has chosen to run.

## What is settled, and what is still being tested

- **Settled:** routine agent work should use coherent structured operations and host-owned bookkeeping.
  Shell command construction is not the intended recording or task-interaction interface. The CLI
  stays available for operator/debugging use; this is not a request for a new graphical client.
- **Settled:** retain actual authorship, durable records, safe retries, evidence relationships,
  explicit decision authority and independent assurance where needed. Recording and approval differ.
- **To implement and try:** the remaining interaction paths, including the narrow recording-authority
  changes needed to eliminate transcription-only handoffs. A tool must call application services,
  not wrap arbitrary CLI strings.
- **To test:** whether selected handoff packages and bounded execution improve Uri's task experience.
  A disappointing episode experiment does not send ordinary recording back to the CLI.

The coverage map in task 10 and results from each checkpoint keep the wider redesign visible.
The findings tool is the first delivered operation, not a claim that the whole experience is fixed.

## Investigation and evidence

The full investigation is [What should survive AILedger 2.0?](../docs/ailedger-episodic-survival-investigation.md). It contains the survival map, minimum governed-episode contract, handoff packages, architecture, historical counterfactuals, risks, and proposed experiment. Keep that report as the supporting analysis rather than duplicating it here.

Its primary sample was nine tasks opened September 23–27, with events frozen through 2026-09-28 07:22:05 UTC:

- 3,577 ledger events, 235 recorded runs, 101 journalled kernel refusals, and 220 available provider transcripts.
- 549 explicit Claude permission denials and 275 C# search-hook refusals in those transcripts. These are separate observation boundaries, not a count of unnecessary kernel refusals.
- Falcon recon RN1 had 40 records prepared, then needed three denied attempts and 11 consecutive successful filing calls, with no intervening investigation. The first denial to the last write spanned about 3m18s.
- S3 run RE3 had an evidence write rejected three times by the C# search hook. The accepted wording omitted an explicit account of the Roslyn query that earlier attempts included.
- Infrastructure run RO3 spent 98.5 seconds transcribing ten alternatives that workers lacked authority to record. That is a separate authorization-policy problem; a structured interface alone will not fix it.
- 187 of 220 available transcripts reported fresh launches. The median recorded run lasted 4.21 minutes. Ten launches failed at the context-size limit. The issue is not simply a shortage of fresh agents; fresh agents also receive accumulated task context.

The evidence supports removing interface friction and testing bounded handoffs. It does not establish a guaranteed overall speedup or a causal law that larger context always produces worse reasoning. Preserve the report's counterevidence: earlier decisions, lessons, dependency corrections, and cross-area synthesis materially improved later work.

## Agreed starting point

Build on the current codebase and existing ledger format. Preserve historical records unchanged. Add a new agent-facing adapter over the application API; keep the CLI available.

```text
Agent
  ↓ structured tool call
record_findings({ request_id, findings: [...], evidence: [...] })
  ↓ application API / adapter
Existing command authorization and kernel validation
  ↓ one durable transaction
Granular immutable events + retry receipt
```

The request describes a coherent recording operation. It should not expose internal stage management or require the agent to know the allocation scheme for claim/evidence IDs. The application layer translates it into today's command model. This boundary should remain usable if a later backend replaces task-wide orchestration.

Relevant existing seams:

- [IGovernedTaskService](../src/AILedger.Core/Contracts/PersistenceContracts.cs:11) currently accepts one command per call.
- [CommandHandler](../src/AILedger.Core/Application/CommandHandler.cs) provides authorization, validation, state advancement, and event production.
- [FileGovernedTaskService](../src/AILedger.Storage/FileGovernedTaskService.cs:81) holds the mutation lock and persists events; its multi-event append already has torn-write recovery markers.
- [RoslynNavigationServer](../src/AILedger.Providers/Navigation/RoslynNavigationServer.cs) and the provider adapters provide an existing structured-tool integration precedent. The new endpoint must call the application service directly, not construct shell commands.

## Format boundaries

Use YAML where people benefit from editing configuration by hand. Keep the execution interfaces and durable records typed and machine-oriented.

| Boundary | Format |
|---|---|
| Human-authored episode specifications, reusable execution profiles, and policy configuration | YAML, when these capabilities are introduced |
| Agent tool requests and responses | Structured JSON |
| Canonical events, retry receipts, and evidence records | JSON / JSONL, preserving existing storage conventions |
| Investigations, explanations, and readable reports | Markdown |

YAML is an authoring format over the same typed application model, not a second validation system or event format. The host parses and validates a specification, normalizes it into that model, and checks requested actions against the actual principal and trusted grants. Editing a YAML file cannot grant authority; policy configuration must also come from an authorized source.

Preserve the original specification and its version/content identity alongside the normalized execution specification. Host-generated execution receipts record what actually ran and link back to that specification; the authored file alone is not evidence of execution.

The first `record_findings` delivery uses structured JSON only. Add YAML support later if the episodic experiment needs human-authored specifications or reusable profiles; it is not a prerequisite for the recording interface.

## First delivery: record_findings

The following illustrates the accepted v1 contract from [task 1](../docs/structured-findings-v1.md), implemented and validated in tasks 2–6:

```json
{
  "schema_version": 1,
  "request_id": "caller-generated-retry-key",
  "findings": [
    {
      "key": "f1",
      "statement": "The failed run retained its checkpoint.",
      "consequence_if_wrong": "A retry may repeat work."
    }
  ],
  "evidence": [
    {
      "key": "e1",
      "source_type": "test-run",
      "citation": "immutable test-result reference",
      "summary": "The checkpoint was present after the induced failure.",
      "supports": [{ "finding": "f1" }],
      "refutes": []
    }
  ]
}
```

Findings initially map to open claims. Evidence points to request-local findings or explicitly typed existing claim references. Recording supporting evidence never silently validates a claim.

Task, actor, run attribution where applicable, and capability grants come from trusted launch/session configuration. The agent cannot obtain more authority by supplying a different actor in its payload. A narrow grant to call the endpoint is not a blanket grant to execute shell commands or mutate ledger files.

The service assigns durable IDs and returns a compact receipt containing the local-to-durable mapping, resulting event IDs and ledger version, request identity, and whether this was a replay of a prior committed request. Failures distinguish request-shape errors, reference errors, authorization/validation refusals, conflicts, and retryable transport/storage failures. Preserve the kernel's actual refusal reason; do not invent a second policy model from its prose.

### Required semantics

- One bounded request may emit many existing events. Validate each operation against the progressively updated candidate state, under the appropriate current authorization.
- A successful response means the entire findings/evidence batch is committed. A rejected batch adds no canonical claim/evidence events; best-effort refusal telemetry may still be recorded.
- Repeating a committed request with the same scoped idempotency key and equivalent payload returns the same receipt and IDs. Reusing that key with different content is a conflict.
- A commit whose response is lost must remain distinguishable from a request that never committed. Persist the retry identity and receipt association consistently with the event transaction; do not rely on an in-memory cache or a separately committed sidecar.
- Local references resolve within the batch. Existing references must exist and be valid for the selected ledger/task. Concurrent requests cannot allocate colliding durable IDs.
- Historical logs continue to replay. New storage metadata must be additive and compatibility-tested; do not add replay requirements that invalidate previously legal events.
- Text containing quotes, backticks, dollar signs, pipes, multiline citations, or tool names remains data. Preserve internal text; existing claim/evidence rules still trim surrounding whitespace, as documented in task 1.
- Request size and item limits are explicit. Encourage small findings batches as work progresses, not one giant end-of-run upload.

## Scope boundaries

The first delivery includes the recording contract, application operation, transaction/retry semantics, a local structured-tool endpoint, provider integration for Claude and Codex, measurement continuity, and a measured pilot.

Tasks 1–6 did not include the remaining interface operations, role-capability changes, YAML authoring or a different execution model. Tasks 7–14 below specify the follow-on increments and their limits. The accepted findings contract remains stable. No task introduces automatic claim validation, arbitrary command batching or retroactive changes to historical grants.

Use fixture ledgers for development and failure testing, then disposable tasks for end-to-end trials. Do not use historical task records as test targets. Existing safety and governance checks must remain in the call path.

## Measurement tools must survive

Preserving measurement is a delivery requirement. Changing the recording interface or later retiring workflow machinery must not erase our ability to inspect cost, delays, refusals, rework, evidence quality, and interrupted work. Preserve the existing readers, historical data, scoring capability, and report entry points. Adapt their inputs where necessary; do not rebuild the measurement suite by default.

Tasks 1–6 inventoried and verified these assets. Subsequent increments must preserve them and extend capture at each changed boundary:

| Existing asset | What must survive |
|---|---|
| [TaskRetrospective](../src/AILedger.Core/Application/TaskRetrospective.cs) | Deterministic cost/activity reports, causal links, authorship, refusal and build-identity partitions, per-field coverage, and explicit `notMeasured` results |
| [CoordinatorMeasurement](../src/AILedger.Core/Application/CoordinatorMeasurement.cs) | Session timing, idle/dispatch delays, rework and reversal analysis, attribution boundaries, and the evidence behind avoidability judgments |
| [RunCostReader](../src/AILedger.Core/Runs/Telemetry/RunCostReader.cs), [ProviderRunRecorder](../src/AILedger.Cli/Providers/ProviderRunRecorder.cs), and [CoordinatorUsageReader](../src/AILedger.Cli/CoordinatorUsageReader.cs) | Provider-specific usage interpretation, process outcomes, first-write timing, manifest identity, transcript provenance, and named missing-data cases |
| [RefusalJournal](../src/AILedger.Storage/RefusalJournal.cs) | Failed-attempt visibility, original reasons and boundary attribution, unreadable-row accounting, and telemetry failure isolated from canonical state |
| [TaskCloseoutEvidence](../src/AILedger.Core/Application/TaskCloseoutEvidence.cs) and [self-scoring rubric](../docs/self-scoring-rubric.md) | Evidence-linked assurance analysis and ten-dimension evaluation, historical reports, and explicit uncertainty; scores remain judgments distinct from observed facts |

Record tool attempt identity separately from the durable request/idempotency key, transaction, event, run, and provider session identities. One tool interaction may emit many events, and several retry attempts may refer to one committed transaction. Preserve the run correlation expected by existing readers while adding batch correlation; replacing it with the request ID could silently break first-write timing and run attribution.

Keep provider permission denials, search-hook denials, request-shape errors, kernel refusals, and storage/transport failures distinguishable. A smaller refusal count alone does not demonstrate better guardrails. Evaluate sampled refusals using the triggering action, available evidence, recovery cost, and resulting behavior; label uncertain usefulness as uncertain.

Historical measurements must remain reproducible from frozen inputs. Missing data stays missing, each total retains its measured population, and provider token buckets retain their meaning. In a later architecture without a coordinating session or stages, report those measures as not applicable and add explicitly defined episode/host measures. Do not manufacture old workflow events to keep a chart populated. Preserve optional scoring tools even if the later experiment removes mandatory scoring from execution.

## Task boundaries and delivery order

**Fourteen planned tasks: six accepted, eight remaining increments.** This is a working delivery
sequence, not a requirement to finish all fourteen before Uri benefits. Each increment includes its
contract, implementation, relevant tests, provider wiring, guidance and measurement; those are not
separate future tasks. Real-task feedback can narrow or reorder later increments.

This revision supersedes the former numbering after task 6. Old task 7 (the episode prototype) is
now divided across tasks 11–13; old task 8 (evaluation) is task 14, with feedback also collected after
every earlier install. The intervening discussion's twelve-task proposal was not an implemented
roadmap. Tasks 7–10 now prioritize the remaining everyday interface friction. Historical validation
and handoffs retain their original numbering and checkpoint meaning.

| Task | Deliverable | Dependency / checkpoint |
|---|---|---|
| 1. Contract and measurement baseline — complete | Findings contract, durable retry design, frozen historical comparisons | Accepted first delivery |
| 2. Reliable application operation — complete | Atomic authorized findings/evidence batches and recovery | Accepted first delivery |
| 3. Structured tool endpoint — complete | MCP endpoint with trusted identity and typed results | Accepted first delivery |
| 4. Provider adoption — complete | Working Codex and Claude tool paths and guidance | Accepted first delivery |
| 5. Measurement continuity — complete | Existing measurements preserved and new attempts observable | Accepted first delivery |
| 6. Pilot and delivery decision — complete | Prepared/live evidence and client acceptance | Accepted; do not repeat |
| 7. Record alternatives directly — next | Small structured batches and narrowly scoped recording authority | Builds on 1–6; install-and-try checkpoint A |
| 8. Submit artifacts coherently — implemented | Content, metadata, identity and version references in one operation | Reuses trusted tool/receipt path; checkpoint B |
| 9. Record explicit claim dispositions — implemented | Authorized grouped judgments over cited evidence | Reuses existing evidence and decision rules; checkpoint C |
| 10. Inspect readiness and retrieve task context | Structured blockers, relevant state and context; explicit map of remaining CLI dependencies | Uses 7–9 where available; checkpoint D |
| 11. Prepare bounded handoffs | Versioned selected inputs, output contracts and preserved material dependencies | Reuses artifact identities from 8 and reads from 10; inspect on a real task |
| 12. Execute bounded work and recover partial output | Installable host path, admission, bounded follow-ups, receipts and interruption discovery | Uses 10–11 and existing provider components; checkpoint E |
| 13. Preserve assurance across handoffs | Candidate/dependency freshness, independent inspection and explicit acceptance | Builds on 11–12; checkpoint F before using the new path for an accepted code change |
| 14. Evaluate the whole working experience | Real-task results, focused matched comparisons where needed, and a client decision on what to keep/change | Evidence from all checkpoints; no automatic architectural replacement |

Tasks 7–10 do not depend on accepting the episode hypothesis. They improve the current experience.
Tasks 11–13 progressively test that hypothesis without suspending the working interface. Artifact
submission and readiness are implemented once and reused; they are not four additional duplicate
endpoints at the end of the roadmap. Measurement is part of every task. Task 14 evaluates outcomes;
it is not the first opportunity to use the work.

### 0. Architectural investigation — complete

- [x] Inspect implementation, recent events, refusals, provider transcripts, retrospectives, and the earlier skills approach.
- [x] Record the survival map, handoff contracts, evidence against the proposal, and conservative historical counterfactuals in the linked report.
- [x] Choose an interface-first change on the existing implementation, with episodic execution evaluated later.

### 1. Settle the contract and measurement baseline — complete

- [x] Define the exact structured JSON request/response schema, local and existing references, batch limits, error envelope, and identity/grant binding.
- [x] Define idempotency-key scope, payload equivalence, changed-payload conflict behavior, and lost-response recovery.
- [x] Trace the existing append/replay boundary and choose where batch validation and durable retry metadata belong.
- [x] Inventory measurement collectors, report/CLI consumers, rubric inputs, and existing test coverage. Capture frozen representative inputs and expected report outputs, including incomplete telemetry.
- [x] Define where attempt/request/transaction/run correlation, timings, refusal boundaries, and collection gaps are recorded. Identify any measurements that currently depend on the CLI path.
- [x] Produce a short implementation design with examples, failure cases, acceptance checks, and the smallest justified file/component split.

**Exit:** a concrete design that explains crash/retry behavior and a measurement compatibility checklist with frozen baselines. This task introduces no production tool or changed governance policy.

**Delivered:** [implementation design](../docs/structured-findings-v1.md), [measurement inventory and frozen baseline](../docs/structured-findings-v1/measurement-baseline.md), [validation record](../docs/structured-findings-v1/task-1-validation.md), and [Astra handoff for task 2](../docs/handoffs/structured-findings-task-2.md). The baseline records one existing timing-sensitive test failure; production source is unchanged.

### 2. Add the atomic, retry-safe application operation — complete

- [x] Implement the typed application operation without an MCP dependency.
- [x] Allocate IDs and map local references within the protected transaction boundary.
- [x] Reuse existing command validation and authorization against sequentially advanced state.
- [x] Commit the resulting events as one recoverable unit and update derived projections appropriately.
- [x] Verify successful recording, invalid references, missing authority, duplicate local keys, conflicting evidence directions, concurrent submissions, and replay compatibility.
- [x] Persist request/receipt identity consistently with the batch commit.
- [x] Return the original receipt after a successful commit whose response was lost.
- [x] Reject changed content under an already-used key and define concurrent identical-request behavior.
- [x] Exercise interruption before append, torn append, committed append before response, restart, and projection-repair failure.

**Exit:** fixture-ledger evidence that an accepted batch lands completely, a rejected batch leaves canonical state unchanged, and retry/restart produces no duplicate findings or ambiguous partial submission. Include concurrent submissions, run attribution, and historical replay. Keep the operation internal until these guarantees are complete.

**Delivered:** the typed `IFindingsRecorder` operation, atomic candidate validation/append, canonical receipts and retry recovery, trusted run binding, scoped attempt capture, and 86 passing findings cases. The full repository-aware suite passed 1,776 main tests and 99 Memory tests; frozen fixtures/reports are unchanged. See the [task-2 validation record](../docs/structured-findings-v1/task-2-validation.md) for standard-test environment failures and the old-storage singleton compatibility limit, and the [task-3 handoff](../docs/handoffs/structured-findings-task-3.md). Task 2 did not include MCP or provider integration.

### 3. Expose the structured tool — complete

- [x] Add a local MCP endpoint for record_findings over the application operation.
- [x] Bind ledger/task/actor and allowed actions to trusted configuration; refuse attempts to impersonate or exceed that grant.
- [x] Return concise typed receipts and actionable errors. Keep diagnostics off the protocol output stream.
- [x] Test adversarial quoting and multiline prose as ordinary payload data, with no shell translation.

**Exit:** an end-to-end tool submission against a disposable fixture succeeds through the real application and kernel path, while unauthorized submissions still fail.

**Delivered:** a trusted local stdio MCP endpoint over `IFindingsRecorder`, strict wire parsing, frozen v1 responses, host binding with per-call grants, and separate transport diagnostics including pre-application and lost-response failures. All 82 new MCP cases and the unchanged 86 task-2 cases pass. The repository-aware full suite passed 1,858 main tests and 99 Memory tests; all frozen hashes/reports match. Standard `dotnet test` reproduces the existing external-output failures; no findings case failed. See the [task-3 validation record](../docs/structured-findings-v1/task-3-validation.md), [endpoint contract](../docs/structured-findings-v1/task-3-endpoint.md), and [task-4 handoff](../docs/handoffs/structured-findings-task-4.md). Provider wiring and live provider acceptance remain task 4.

### 4. Wire providers and update the relevant recording guidance — complete

- [x] Configure the endpoint in Claude and Codex launches, including actual tool permissions.
- [x] Make it discoverable independently of whether Roslyn navigation is enabled.
- [x] Replace claim/evidence shell examples with structured recording guidance where this endpoint is supplied; retain documented CLI support for operators and other operations.
- [x] Check that provider/session attribution survives and evidence prose no longer passes through shell/search classification.
- [x] Verify a real disposable episode on each provider, documenting environmental limitations if either cannot run.

**Exit:** both provider paths can record findings with their real configured grants. No global approval bypass, wider filesystem grant, or fictitious role is used to make the demonstration pass.

**Delivered:** host-owned in-memory findings binding, capability-protected local stdio relay, inline Claude/Codex registration with the exact tool grant, navigation-independent availability and conditional recording guidance. Both real clients completed a disposable commit/retry/capability-revocation sequence with one canonical batch and one usage record. Session observations remain honestly absent from immutable transport bindings and available on the completed run. See [task-4 validation](../docs/structured-findings-v1/task-4-validation.md), [host/trust contract](../docs/structured-findings-v1/task-4-provider-adoption.md), [reproducible live probe](../tools/FindingsProviderProbe/README.md), and [task-5 handoff](../docs/handoffs/structured-findings-task-5.md). Exact negotiated protocol versions are unobserved; the real tool paths succeeded. Existing standard-test and storage compatibility limits remain documented.

### 5. Preserve and connect the measurement tools — complete

- [x] Run the existing readers over task 1's frozen inputs and compare the historical outputs. Explain intentional additive fields; preserve existing metric definitions and values.
- [x] Verify that service and tool attempts, retries, refusals, timing, run/session attribution, and committed event IDs reach the intended collectors and reports on both provider paths.
- [x] Ensure one committed batch with a lost response and a successful retry is visible as multiple attempts and one commit, with no duplicated findings or attributed provider cost.
- [x] Cover absent/truncated telemetry and collector failure. Canonical recording and refusal responses must retain their established behavior when best-effort telemetry is unavailable; reports must expose detectable collection gaps.
- [x] Preserve retrospective, assurance-evidence, and scoring consumers. Document unsupported measurements instead of silently dropping them or reporting zero.

**Exit:** a compatibility report and meaningful regression checks showing historical comparability and visibility of the new path. No existing measurement capability is silently retired.

**Delivered:** opt-in `retrospective build --findings` measurement over storage-validated canonical receipts, distinct transport/application attempts, explicit gaps and per-run completion/session joins. Lost-response recovery and journal failures retain one canonical commit without duplicated cost. Both existing live provider episodes passed read-only comparison; no new model episode ran. All 28 new measurement cases pass; the final repository-aware suite passed 1,895 main and 99 Memory tests. All 11 frozen hashes and historical report/cost baselines are unchanged. Standard `dotnet test` retains task 4's existing failure set. See the [measurement contract](../docs/structured-findings-v1/task-5-measurement.md), [compatibility/validation record](../docs/structured-findings-v1/task-5-validation.md), and [task-6 handoff](../docs/handoffs/structured-findings-task-6.md). Task 6 and conditional tasks 7–8 remain unimplemented.

### 6. Measure the interface on prepared findings and a bounded task — complete

- [x] Submit the historical RN1 findings as a prepared, read-only-derived dataset into a disposable ledger; preserve the original history.
- [x] Measure recording elapsed time, tool interactions, retries, errors, payload fidelity, and duplicate/partial outcomes.
- [x] Exercise early small submissions and failure recovery in a narrow real task using the new interface. Live reference-error correction passed; live replay was deliberate, while injected lost-response recovery remains the prepared-data result.
- [x] Compare with the documented shell baseline while distinguishing a controlled measurement from an observational historical comparison.
- [x] Use the preserved measurement tools to distinguish useful guardrails from interface friction. Report sampled recovery delays, lost or altered content, and unnecessary repeated interactions alongside retained authorization checks.
- [x] Identify further operations that form coherent batches and those separated by an actual reasoning, authority, or dependency boundary. Do not equate events, tool calls, model turns, or useful progress.
- [x] Run the appropriate tests, including dotnet test for implementation changes, and record any unresolved limits.

**Exit:** a usable first delivery with evidence of reliable recording and measured friction. Report whether elapsed time improved; do not claim a whole-workflow or cognitive improvement from this test alone.

**Prepared checkpoint:** the read-only-derived RN1 pilot passed 21 local-MCP trials, including
six failure/recovery scenarios; both full runners passed 1,901 main and 99 Memory tests. This record
remains in the [pilot report](../docs/structured-findings-v1/task-6-pilot.md) and
[validation checkpoint](../docs/structured-findings-v1/task-6-validation.md).

**Live evaluation:** the subsequently authorized single Codex episode passed four tool/application
attempts, two commits and four events, with early persistence, expected reference refusal, correction
and identical replay. Transcript/source review supports both recorded observations; extra task-projection
reads, configuration warnings and the initial pre-provider socket failure are retained as limitations.
See [actual live validation and delivery recommendation](../docs/structured-findings-v1/task-6-live-validation.md)
and [retained measurements](../docs/structured-findings-v1/task-6-live-results.json). Technical evaluation
is finished. **The operator accepted delivery on 2026-09-28 with the documented limitations and no
further operation for now. Task 6 is complete.** See the appended acceptance in the live validation record.

### Historical task-6 acceptance — 2026-09-28

- [x] Review the pilot with the operator and choose the next slice or none: the operator accepted the interface as sufficient for now; no extension selected.
- [x] Separate the endpoint's ergonomics from decisions about who may record versus who may approve; existing authority boundaries remain unchanged.
- [x] Keep any future extensions individually scoped; no unrestricted command batch API is authorized.

**Accepted decision (2026-09-28):** the operator replied “accepted / proceed” to the delivery
recommendation. Accept the current interface with the live review's documented limits and choose no
extension now. Recording remains separate from claim approval. No broader batch API, authority
change, further billable run or task-7/8 work is authorized by this acceptance.

**Exit:** a separately bounded follow-up with purpose and acceptance checks, or an explicit decision that the interface is sufficient for now.

The later client clarification supersedes the forward-looking “no extension now” checkpoint above.
It preserves acceptance of tasks 1–6 while requesting the revised roadmap below. Old references to
tasks 7–8 in historical delivery records describe the earlier numbering, not the current next step.

## Remaining increments — current scope

Task 7 now has a verified, installed implementation; client observation remains a separate checkpoint.
Tasks 8–14 remain planned, unstarted work packages. The [task-7 handoff](../docs/handoffs/structured-agent-interface-task-7.md) provides the
next implementation request. Later increments are refined using the preceding real-task results.

### 7. Record alternatives directly — client trial pending

**User effect:** the agent that investigated an approach can preserve why it rejected it, without
shell transcription or launching a more privileged agent solely to file the reasoning.

- Define a bounded structured alternatives operation with local keys, server-assigned IDs, stable
  retry identity, trusted actor/run attribution and compact receipts. Keep the accepted findings-v1
  schema and results stable; use an additive operation rather than an unrestricted command batch.
- Preserve statement and rejection rationale as data, with the current optional decision/lesson
  links and their validation. Specify one coherent batch's atomicity and changed-key behavior.
- Make the narrow `RecordAlternative` capability usable for researcher, worker, verifier and reviewer
  roles that produce alternatives, retaining existing lead/operator support. Recording an actor's
  rejected approach does not accept a decision, resolve a claim, grant scope or approve an artifact.
  Document the exact role/capability change. Preserve existing recorded grants; do not silently
  upgrade historical assignments. Exercise absent/revoked capability and cross-actor refusal.
- Reuse existing application/storage and provider integration where appropriate. Update both command
  admission and applicable replay handling without making previously legal history unreadable.
- Wire Codex and Claude, narrow tool grants and repository recording guidance. Verify multiline and
  quoted text, late-invalid references, exact fidelity, concurrent IDs, retry/restart and no rejected
  prefix. Preserve observational attempts/refusals and count provider usage once.

**Implementation checkpoint (2026-09-28):** typed `record_alternatives`, atomic receipts/retries,
trusted Codex/Claude binding, narrow author defaults and separate measurement are implemented.
Both full runners pass 1,995 main and 99 Memory tests; all frozen hashes and historical report/cost
baselines match. **2.0.168 from f966952 is installed**, with matching package identity and
passing installed-executable checks; see the [installation receipt](../docs/structured-alternatives-v1/task-7-installation.md).
See the [contract](../docs/structured-alternatives-v1.md) and
[validation record](../docs/structured-alternatives-v1/task-7-validation.md). Implementation and installation are
verified separately. Uri's ordinary-task trial remains pending; task 7 is
not claimed accepted and tasks 8–14 have not started.

**Checkpoint A:** after verification, prepare the exact installable commit and install it when the
implementation request authorizes that step. Uri runs an ordinary task that considers alternatives.
Inspect whether the actual author recorded them through the tool, whether a transcription handoff
was avoided, and what friction remains. No successful live outcome is claimed before that observation.

**Exit:** a verified, usable operation; installation identity and a real-task evaluation recorded
separately. The trial can reveal follow-up work without hiding a failed or incomplete result.
No other interface operation or episode runtime is implemented in this task.

### 8. Submit artifacts coherently

**User effect:** an agent submits a result without manually coordinating file writes, identifiers,
metadata and registration through a sequence of commands.

- Bound the first supported artifact types to the work Uri actually runs. Submit content or an
  immutable content reference with metadata, scope and version/dependency links in one typed operation.
- Verify content existence and identity, producer attribution and supersession; recover safely from
  failure between content storage and registration. Return a receipt for exactly what was accepted.
- Separate permission to record an authored output from permission to endorse it. Any needed
  recording-capability change must be explicit and tested; keep current approval/assurance checks.
- Reuse content identity and receipt primitives later for handoff packages. Avoid building a second
  generic artifact system or changing every artifact kind merely to finish this increment.

**Checkpoint B / exit:** install the verified slice, have Uri run a task that submits that artifact,
and inspect content fidelity, registration/retry behavior and remaining clerical work. Record which
artifact types are still outside the supported path.

### 9. Record explicit claim dispositions

**User effect:** an authorized agent can express several already-reasoned claim judgments without
repeated command construction or a human repairing identifiers.

- Provide a bounded structured operation for explicit validation/rejection with rationale and
  evidence references. Specify partial-versus-atomic behavior for the chosen coherent unit.
- Preserve resolution authority, support/refute direction, provenance and dependency consequences.
  Supporting evidence never automatically validates a claim; a recorded judgment is not proof of truth.
- Keep formal decision acceptance separate where its authority or semantics differ. Do not add all
  decision commands merely because they could share transport; name any remaining gap.
- Include safe retry behavior, actionable per-item diagnostics and existing measurement continuity.

**Checkpoint C / exit:** install and inspect a real task with several dispositions, checking actual
judgment quality as well as mechanical acceptance. No blanket automatic closure of old open claims.

### 10. Inspect readiness and retrieve task context

**User effect:** agents learn what they need and what blocks an action through structured reads,
before spending a run discovering predictable prerequisites through refusals.

- Return relevant task state, cited evidence/decisions, receipts and actionable blockers through
  bounded structured reads. Reuse existing context selection; expose omissions and retrieval paths.
- Provide non-mutating readiness checks over the proposed action, candidate, dependencies, grants,
  context and available environment. Revalidate at execution: a readiness response is not a grant.
- Maintain a coverage table for the task Uri runs: each routine interaction is an agent tool, a
  deterministic host responsibility, or a named remaining gap. Include work preparation, dispatch,
  completion and any retained stage administration; do not hide their CLI dependency behind a claim
  that all agent interaction is now structured.
- Move only justified deterministic bookkeeping to the host. Keep business choices and approvals
  explicit. Do not expose arbitrary CLI execution as a tool or turn readiness into a second rules engine.

**Checkpoint D / exit:** install, run a representative task, and account for every residual CLI
interaction. Missing inputs and blockers are understandable without trial-and-error mutations.
Uncovered operations get a concrete follow-up scope before being claimed delivered.

### 11. Prepare bounded handoffs

**User effect:** the next agent receives the information needed for its objective, including important
corrections and uncertainty, rather than the accumulated task conversation.

- Define EpisodeSpec and result/package contracts: objective, non-goals, input versions, expected
  output, acceptance checks, grants, budgets, stop conditions and retrieval references.
- Use task 8's versioned content and task 10's structured retrieval. Start with inspectable selection;
  preserve decisive alternatives, constraints, contradictions, applicable lessons and corrections.
- Prepare profiles for the narrow handoffs actually tried. Separate technical methods from old
  workflow-filing instructions in repository-local trial guidance; do not change global skills.
- Check omitted-material risks against frozen Axonius and Falcon/S3 slices, withholding later findings.
  These are diagnostic fixtures; also inspect a handoff from Uri's current task.
- Preserve links to additional sources and measure additional reads. Do not call shorter initial
  text a success if the next agent loses a requirement or must reconstruct everything.

**Checkpoint / exit:** Uri can inspect and try a focused handoff with its versioned inputs. Necessary
knowledge survives the handoff checks. YAML is optional only if it improves actual authoring; no
configuration subsystem is a prerequisite for the first trial.

### 12. Execute bounded work and recover partial output

**User effect:** a scoped request launches with ready inputs, and interrupted work leaves discoverable
results rather than forcing Uri or the next agent to reconstruct what happened.

- Compose a narrow execution path over separable provider/recording components. The current launcher
  and findings backend depend on governed task state: resolve that coupling explicitly, preserve the
  accepted agent contract where compatible, and do not invent waived task histories to simulate a new path.
- Keep experimental records distinct and old histories readable. Reuse trusted bindings, durable
  submission and package identity; do not duplicate task 8 or task 10 as separate services.
- Check resources, grants, input freshness and budgets at admission. A small dispatcher executes only
  already-authorized follow-ups; decisions about objectives or material risk still reach Uri.
- Capture actual execution receipts, provider usage, tool attempts, submitted IDs and changed-file
  inventory. On interruption, preserve partial results and distinguish failed, blocked and unknown.
- Exercise retry, cancellation, absent telemetry and resume/reconciliation with disposable fixtures.

**Checkpoint E / exit:** install an explicitly selectable path and let Uri run a bounded research or
other limited task. Inspect the full experience, including an interruption probe. The existing path
remains usable. Do not present this limited trial as full assurance for a code change.

### 13. Preserve assurance across handoffs

**User effect:** useful independent checks and dependency corrections survive the smaller execution
model without requiring every task to traverse the same ceremonial route.

- Bind implementation, review, verification and acceptance to immutable candidate and requirement
  identities. Preserve inspection coverage, independence and actual test/environment receipts.
- Keep contradictions and endorsements explicit. A changed relied-on input marks affected consumers
  for reassessment; unrelated valid work remains usable. Retrieved prose cannot confer authority.
- Prevent self-approval, stale assurance, unsupported passes and fabricated completion. Make
  uninspected or untested areas visible. Use targeted synthesis where cross-area judgment is required.
- Test these conditions and interrupted inspection before using the new path for an accepted code change.

**Checkpoint F / exit:** install the verified assurance slice; Uri runs a bounded implementation task
through it. Inspect actual findings, candidate freshness, repairs, preserved knowledge and human
intervention. A clean schema or a completed process does not substitute for correct work.

### 14. Evaluate the whole working experience and choose the next change

**User effect:** Uri gets a concrete account of what improved, what still gets in the way, and what
should stay, change or be removed from the installed experience.

- Combine evidence from checkpoints A–F with failures and unfinished observations intact. Compare
  elapsed delivery time, usage, interruptions, retained information, defects and human clerical effort.
- Use small matched comparisons where a causal question remains: same source, provider/model, tools,
  objective and checks; vary the context/execution choice being tested. Keep later findings withheld
  and evaluate outputs independently. Extra model episodes need an explicit bounded spend request.
- Preserve historical measurements and make missing or non-comparable fields explicit. Do not
  rerun task 6 merely to generate another favourable recording number.
- Recommend the next installed default or targeted change from evidence. No mandatory architecture
  replacement, forced migration or task completion count serves as the success criterion.

**Exit:** Uri decides what to use next from observed task outcomes. If a smaller interface improvement
is enough, keep it. If bounded execution helps, retain it with its demonstrated limits. Identify
remaining routine CLI dependencies and unresolved safeguards as work, not as implicit success.

## First delivery acceptance and current next step

Tasks 1–6 are accepted and complete. Their contracts, test results, installation and live observations
remain recorded in the linked validation documents. The later clarification broadens the forward
roadmap; it does not reopen or rewrite the accepted pilot.

**Current checkpoints: tasks 7–9 implemented and installed; all three client trials pending.** Uri explicitly selected task 8 before completing task 7's
trial; this does not imply acceptance of either increment. Task 8 supports VerifierOutput and
CodeReviewOutput only, as described in the [contract](../docs/structured-artifacts-v1.md) and
[validation](../docs/structured-artifacts-v1/task-8-validation.md). Task 8 was installed as **2.0.170 from 540d6e6**; its
[installation receipt](../docs/structured-artifacts-v1/task-8-installation.md) retains the exact identity.
The next checkpoint is Uri's ordinary task and inspection of the actual result.
The task-7 [implementation handoff](../docs/handoffs/structured-agent-interface-task-7.md) and
validation remain intact. Task 9 now implements atomic explicit claim dispositions; its
[contract](../docs/structured-claim-dispositions-v1.md) and
[validation](../docs/structured-claim-dispositions-v1/task-9-validation.md) define the supported scope.
Uri authorized proceeding and installing task 9 before the earlier client trials. Task 9 is now installed as **2.0.172 from 6ebb55a**, with the exact
[installation receipt](../docs/structured-claim-dispositions-v1/task-9-installation.md).
No task-9 client observation or acceptance is claimed. Tasks 10–14 remain unstarted.

## Related work

- Item 12: claim-resolution ergonomics, addressed by task 9 only to the extent its own criteria are met.
- Item 31: workers recording alternatives, directly relevant to task 7; record the capability decision.
- Items 36 and 58: early persistence and interrupted-work discovery; task 12 owns the remaining host work.
- Item 57: stable refusal identities; preserve current errors and observations without making a full rule-ID migration a prerequisite.
- Item 59: context budgeting; relevant to tasks 11–12, with source retrieval and omission checks.

Other backlog items remain open until their own acceptance criteria are delivered. This roadmap
neither launches work nor authorizes new model spend by its existence. Historical item IDs remain stable.
