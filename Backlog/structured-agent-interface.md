# Structured agent interface, then bounded episode experiments

**Priority: 1. Backlog ID: 63. Status: open. Kind: feature.**

Created 2026-09-28 from the architectural investigation and the subsequent interface-design discussion. The investigation and task 1 are complete; production implementation has not started. Work through the remaining tasks separately, with a concrete result and verification at each task.

## Purpose

Make recording findings a reliable, small part of an agent's work. Agents should submit structured data without negotiating shell quoting, allocating ledger identifiers, repeating commands after uncertain outcomes, or spending separate reasoning turns on each record.

The larger objective is elapsed time and preservation of useful cognition through bounded episodes. This first delivery isolates the interface improvement: retain the current kernel's validation and event semantics while replacing the agent-facing write path. A later experiment can evaluate episodic execution against the same stable interface.

Develop directly, outside the kernel workflow, as requested for this effort. Exercising kernel validation in isolated tests is part of implementation; using the kernel to govern the development process is not. This entry does not change global instructions, installed skills, or other tasks.

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

The following illustrates the contract settled in [task 1](../docs/structured-findings-v1.md); it is not an already-supported API:

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

It does not include a YAML parser/configuration system, a new workflow engine, global ledger migration, automatic claim resolution, changing role capabilities, removing stages, changing assurance requirements, or exposing every CLI command as a tool. Alternative recording, decision acceptance, artifact submission, and claim resolution can become later tools with their own authority checks. The existing inability of some roles to record alternatives remains visible until deliberately addressed.

Use fixture ledgers for development and failure testing, then disposable tasks for end-to-end trials. Do not use historical task records as test targets. Existing safety and governance checks must remain in the call path.

## Measurement tools must survive

Preserving measurement is a delivery requirement. Changing the recording interface or later retiring workflow machinery must not erase our ability to inspect cost, delays, refusals, rework, evidence quality, and interrupted work. Preserve the existing readers, historical data, scoring capability, and report entry points. Adapt their inputs where necessary; do not rebuild the measurement suite by default.

The source inventory already identifies these assets; task 1 must check the remaining collectors and consumers before implementation:

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

**Six tasks deliver the first usable interface. Two additional, conditional tasks evaluate the episodic design.** The investigation is already complete and is not counted. These are scoped work packages within backlog item 63, not newly created Codex tasks or a commitment to a full kernel replacement.

| Task | Concrete deliverable | Depends on | Explicit boundary |
|---|---|---|---|
| 1. Contract and measurement baseline — complete | Reviewed JSON contract, atomic/retry design, measurement inventory and frozen comparison fixtures | Existing investigation | Design and fixtures only; no production endpoint |
| 2. Reliable application operation — complete | Atomic, authorized, retry-safe findings/evidence submission | 1 | Application/storage and tests; no MCP or provider changes |
| 3. Structured tool endpoint — complete | Local MCP adapter with trusted identity binding and typed responses | 2 | Transport and tool diagnostics; no shell wrapper or broader authority |
| 4. Provider adoption | Claude and Codex configured to use the tool with correct attribution and guidance | 3 | Provider integration; no new workflow or role policy |
| 5. Measurement continuity | Existing reports verified, new transport attempts measurable and joined to committed results | 2–4; capture requirements fixed in 1 | Adapt collectors/readers as needed; no scoring-rubric redesign or dashboard rebuild |
| 6. Measured pilot and delivery decision | Prepared-findings trial, bounded live trial, reliability evidence and comparison report | 5 | Evaluate this interface; no claim of proven cognitive improvement |
| 7. Bounded episode prototype — conditional | Minimal package/receipt path with explicit inputs and preserved measurement | Decision after 6 | One narrow experiment; no global migration |
| 8. Paired evaluation — conditional | Comparable runs, information-loss checks, and go/no-go report | 7 | Evidence for the next design decision; no automatic retirement of the kernel |

Each task owns its relevant verification. Task 5 verifies continuity end to end; tasks 2–4 must implement their capture points as they are built, so instrumentation is not left until after the pilot. Atomicity and durable retry handling belong in one task because an endpoint is not safely usable with only half that contract.

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

### 4. Wire providers and update the relevant recording guidance

- [ ] Configure the endpoint in Claude and Codex launches, including actual tool permissions.
- [ ] Make it discoverable independently of whether Roslyn navigation is enabled.
- [ ] Replace claim/evidence shell examples with structured recording guidance where this endpoint is supplied; retain documented CLI support for operators and other operations.
- [ ] Check that provider/session attribution survives and evidence prose no longer passes through shell/search classification.
- [ ] Verify a real disposable episode on each provider, documenting environmental limitations if either cannot run.

**Exit:** both provider paths can record findings with their real configured grants. No global approval bypass, wider filesystem grant, or fictitious role is used to make the demonstration pass.

### 5. Preserve and connect the measurement tools

- [ ] Run the existing readers over task 1's frozen inputs and compare the historical outputs. Explain intentional additive fields; preserve existing metric definitions and values.
- [ ] Verify that service and tool attempts, retries, refusals, timing, run/session attribution, and committed event IDs reach the intended collectors and reports on both provider paths.
- [ ] Ensure one committed batch with a lost response and a successful retry is visible as multiple attempts and one commit, with no duplicated findings or attributed provider cost.
- [ ] Cover absent/truncated telemetry and collector failure. Canonical recording and refusal responses must retain their established behavior when best-effort telemetry is unavailable; reports must expose detectable collection gaps.
- [ ] Preserve retrospective, assurance-evidence, and scoring consumers. Document unsupported measurements instead of silently dropping them or reporting zero.

**Exit:** a compatibility report and meaningful regression checks showing historical comparability and visibility of the new path. No existing measurement capability is silently retired.

### 6. Measure the interface on prepared findings and a bounded task

- [ ] Submit the historical RN1 findings as a prepared, read-only-derived dataset into a disposable ledger; preserve the original history.
- [ ] Measure recording elapsed time, tool interactions, retries, errors, payload fidelity, and duplicate/partial outcomes.
- [ ] Exercise early small submissions and failure recovery in a narrow real task using the new interface.
- [ ] Compare with the documented shell baseline while distinguishing a controlled measurement from an observational historical comparison.
- [ ] Use the preserved measurement tools to distinguish useful guardrails from interface friction. Report sampled recovery delays, lost or altered content, and unnecessary repeated interactions alongside retained authorization checks.
- [ ] Identify further operations that form coherent batches and those separated by an actual reasoning, authority, or dependency boundary. Do not equate events, tool calls, model turns, or useful progress.
- [ ] Run the appropriate tests, including dotnet test for implementation changes, and record any unresolved limits.

**Exit:** a usable first delivery with evidence of reliable recording and measured friction. Report whether elapsed time improved; do not claim a whole-workflow or cognitive improvement from this test alone.

### Decision after task 6 — choose the next slice

- [ ] Review the pilot with the operator and choose the next small slice: alternatives, artifact submission, explicit claim dispositions, or readiness inspection.
- [ ] Separate the endpoint's ergonomics from decisions about who may record versus who may approve.
- [ ] Keep those extensions individually scoped; do not turn the first tool into an unrestricted command batch API.

**Exit:** a separately bounded follow-up with purpose and acceptance checks, or an explicit decision that the interface is sufficient for now.

### 7. Build a bounded episode prototype — conditional

- [ ] If warranted, run the report's bounded-package sidecar experiment over the same stable interface, with fresh sessions and a small deterministic dispatcher.
- [ ] If hand-authored episode specifications or reusable profiles are useful, add YAML authoring that normalizes into the same typed model. Preserve source identity and normalized specifications, bind execution receipts to them, and validate requested permissions against trusted grants.
- [ ] Preserve durable decisions, contradictions, dependency invalidation, independent assurance, and interrupted-run outputs.
- [ ] Adapt the surviving measurement inputs for episode/host execution, preserving historical readers. Name any non-comparable metrics and define their replacements before trials.

**Exit:** a narrow executable prototype with inspectable packages, observable outcomes, and recovery checks. This task does not migrate existing tasks or establish that the architecture is better.

### 8. Evaluate the episodic prototype — conditional

- [ ] Compare elapsed delivery time and retained information under matched provider/tool conditions. Keep later historical findings withheld from the benchmark inputs.
- [ ] Use the same instruments on both arms and check constraint retention, contradictions, candidate freshness, assurance, and interrupted work as well as speed and cost.
- [ ] Decide from the evidence which workflow machinery to retire; do not treat a successful recording endpoint as approval for a kernel rewrite.

**Exit:** a go/no-go decision on the episodic architecture. This is a later decision point, not part of the initial interface's definition of done.

## First delivery acceptance

Tasks 1–6 deliver the first usable increment. It is complete when authorized agents on both supported provider paths can submit coherent findings/evidence batches without shell construction, receive durable compact receipts, retry safely after uncertain outcomes, and preserve the same kernel validation and historical replay behavior. Existing measurement tools remain usable, historical outputs stay comparable, and new-path measurements and residual limitations accompany the result.

The next piece of work is **task 4 only**, using the [scoped handoff](../docs/handoffs/structured-findings-task-4.md). Complete and review each task's result before moving to the next; this backlog entry does not request implementing all tasks in one run. Tasks 7–8 are conditional experiments; any production replacement or migration will need its own scope after their results.

## Related work

- Item 12: claim-resolution ergonomics. Share structured batching concepts, but explicit resolution and its authority remain separate from recording findings; supporting links alone are not a verdict.
- Item 31: workers recording alternatives. RO3 motivates revisiting that policy later; this interface does not silently grant the missing capability.
- Items 36 and 58: late writes and interrupted-run recovery. Small early batches and durable receipts address part of the problem; mapping all filesystem leftovers is a separate recovery capability.
- Item 57: stable refusal identities. Use a structured error envelope now; do not require completing a repository-wide rule-ID migration before the first recording tool.
- Item 59 and the investigation: context budgeting. Relevant to the later episodic experiment, not a prerequisite for the interface.

Existing backlog items remain open unless their own acceptance criteria are actually delivered. This item takes first place in priority order without renumbering historical backlog IDs.
