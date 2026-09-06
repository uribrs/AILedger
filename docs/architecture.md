# Architecture and governance

## Boundary of responsibility

AILedger separates cognition from governance:

| Boundary | Responsibility |
|---|---|
| Operator | Defines the goal, opens the task, assigns roles and capabilities, sets governed work scope, and resolves matters requiring human authority. |
| Cognitive skills | Describe the planning, research, orchestration, execution, verification, review, and learning methodology. |
| Core kernel | Authorizes commands, validates references and lifecycle transitions, emits causal events, invalidates dependent work, and assembles role-aware context. |
| Storage | Serializes each task mutation, atomically commits a logically append-only event history, replays authoritative state, and publishes readable projections. |
| Provider adapters | Translate a provider-neutral run request into a constrained Codex or Claude process and normalize its JSONL result. |
| Codex and Claude | Reason within the supplied role, capabilities, task state, skill context, working directory, and provider sandbox. Their output is not task truth until recorded through a governed command. |

Opening a task grants the opening actor the `Operator` role and all capabilities. Thereafter only an operator may assign roles or capabilities, and no actor may assign or expand its own authority. Adding a work item requires both `ManageWork` and `ManageScope`; non-operator CLI role assignments cannot receive `ManageRoles` or `ManageScope`.

v0.1 is a cooperative, single-OS-principal orchestration tool, not an authentication or tamper-resistance boundary. Actor IDs provide governed attribution inside the Ledger but are asserted by the caller; a process that can modify the Ledger files as the same operating-system user can forge history. Provider workspaces and the authoritative Ledger root must therefore be disjoint in both containment directions. Deployments that treat providers or local processes as hostile require a future broker that authenticates actor identity and owns signed or otherwise tamper-evident storage outside provider reach.

## Runtime flow

1. The CLI parses an explicit operator or actor command.
2. The file service obtains an in-process semaphore and exclusive per-task file lock.
3. It rebuilds current state from `events.jsonl`.
4. The core authorizes and validates the command, then emits one or more causal events.
5. Storage durably writes the previous history plus the new event batch to a temporary file and atomically replaces `events.jsonl`; that rename is the commit point.
6. Materialized state and Markdown projections are repaired as disposable views without making an already committed command appear to fail.
6. For a provider command, the CLI first records an active run, assembles and verifies context, probes the provider CLI, launches or resumes it, validates its terminal protocol, and records the terminal run status.

There is no background coordinator in v0.1. Starting both providers means issuing two explicit commands, normally against separate governed work items. Events do not automatically trigger another actor.

## Rule-to-mechanism mapping

| Methodology rule | v0.1 mechanism |
|---|---|
| The operator controls authority and scope. | Task opening creates the operator assignment; authorization gates every later command; self-assignment is rejected; Core prevents non-operator roles from receiving authority-management capabilities; provider directories must stay inside governed work scope and be fully disjoint from the Ledger root. Within v0.1 this is cooperative governance, not authenticated isolation between processes owned by the same OS user. |
| Task continuity must outlive a chat or process. | A logically append-only per-task event log is atomically replaced at batch boundaries and replayed by new service instances; envelope checks and the shared domain transition validator reject causally or semantically impossible histories. |
| Claims need evidence before factual resolution. | `Validated` and `Rejected` claim resolutions require existing evidence IDs. Evidence records whether it supports or refutes existing claims. |
| Bad assumptions must affect downstream work. | Rejecting a claim, or superseding it as a correction, emits invalidation events for dependent current decisions and work; active work becomes `Blocked`, other dependent work becomes `Stale`; later commands cannot create/start new dependents, and run completion cannot erase `Blocked`/`Stale`. |
| Superseding a claim is not the same act as rejecting it. | A supersession must name the claim that replaces it, and the kernel derives the consequence from state rather than from a flag set by the actor — who is usually an agent. A **refinement** — the replacement is already `Validated` and no evidence refutes the original — emits no invalidation and re-points dependent decisions and work at the replacement. Anything else is a **correction** and invalidates them as a rejection does. The replay validator re-derives the outcome from the same inputs and refuses an event whose recorded outcome does not match. |
| A challenge that is upheld must change something. | Disposing a challenge `Supported` appends a consequence chosen by target type: a decision is overturned to `Invalidated`, a work item is blocked with a reason naming the challenge, or a claim is rejected using the challenge's own evidence, which then cascades to that claim's dependents. No challenge can be supported without evidence; a claim challenge additionally requires every piece of its evidence to refute that claim, since direction is only modelled for claims. No challenge can be supported when its consequence is already true, and the disposing actor must hold the capability its consequence requires — not merely `DisposeChallenge`. |
| Roles receive only appropriate context. | Context assembly requires an assigned role plus `BuildContext`, filters skills by canonical role, scopes task artifacts to a work item, and orders output deterministically. |
| Code review must remain independent. | Code-reviewer context excludes the user request, prompt contract, orchestration plan, verifier output, and open escalations, and selects the code-reviewer skill rather than orchestration skills. |
| Execution should have one governed orchestration tree. | At most one `Active` run may exist for a given work item. The storage lock makes concurrent check-and-start atomic across processes. Different work items may run concurrently. |
| Resume must continue the intended provider session. | Resume requires an exact session ID. Provider output must report the expected session identity. |
| Interrupted work should remain recoverable. | Caller cancellation returns a partial terminal result carrying any learned or preassigned provider session; Ledger closes the run with a fresh token before preserving cancellation exit semantics. Terminal persistence uses a lock-compatible deadline and bounded retries, and the result is still emitted if closure ultimately fails. |
| Provider output is not success merely because it is parseable. | Success requires exit code zero, a successful terminal JSONL event, and a valid session identity; malformed streams and protocol mismatches become terminal failures. |
| Provider output must not exhaust the host process. | The process reader rejects lines over 1,048,576 characters and streams over 8,388,608 characters; the adapter also caps combined retained output at 8,388,608 characters and turns overflow into a protocol error. |
| Provider processes receive least ambient authority. | Child environments start from a small operational allowlist plus explicit invocation variables; retained stdout JSON, final output, and stderr redact explicit secret values. Provider authentication through local configuration remains a host trust dependency. |
| Only the operator's own decisions interrupt the operator. | An escalation must declare `BusinessDecision` — at least two distinct options plus a recommendation naming one — or `TrueUnknown` — at least one evidence record standing as proof of the attempt that failed. The kernel refuses either kind without its payload, and only an operator resolves one. |
| A rejected approach must not be silently re-proposed. | Alternatives are recorded with a required rejection rationale and an optional link to the decision that replaced them. They are always eligible for assembled context, so work-item narrowing cannot hide them. |
| Governed constraints are operator-controlled and current. | Constraints are task state with `Active`/`Superseded` status; adding or superseding one requires the operator role, and only active constraints enter context. |
| Finished work is asserted, not inferred. | A completed provider run moves its work item to `Paused`. `work complete` is an explicit governed command, refused while a run is active or an escalation on the item is open. `work block` records a reason and may cite the escalation it waits on, and `work unblock` returns it to `Paused` — but never for an item whose claim was rejected, so unblocking cannot undo causal invalidation. |
| Lifecycle changes are governed. | A finite transition policy permits forward movement and selected repair/research loops. Execution requires a work item; archive rejects active runs or open challenges and mints lessons from governed findings. |

The cognitive text has intentionally not been rewritten in v0.1. The six skills and governing `RULES.md` are a byte-for-byte snapshot recorded in `cognitive/manifest.json`. Future revisions can replace portions of prose with mechanisms, but each replacement should preserve the rule's intent, tests, provenance, and an explicit mapping such as the table above.

## Context behavior

Every manifest includes task identity/version, actor, role, optional work item, capabilities, selected artifacts, stop conditions, and assembly time. Rules, relevant skills, task goals, constraints, stop conditions, recorded alternatives, and open escalations are always eligible; other task facts are narrowed to the selected work item's dependency graph. Constraints and alternatives are unnarrowed deliberately: a governing rule and a discarded approach are only useful if the next actor sees them whatever work item is in hand. Open escalations are the exception to that eligibility for one role — a code reviewer never receives them, because an escalation carries the leads' recommendation and a blind review must not learn the answer the team wants.

Canonical skill selection is:

| Role | Skills selected when present in the cognitive snapshot |
|---|---|
| Planning lead | `workflow-coordinator`, `prompt-contract-designer`, `task-orchestrator`, `technical-researcher` |
| Implementation lead | `workflow-coordinator`, `prompt-contract-designer`, `task-orchestrator`, `contract-driven-execution` |
| Researcher | `technical-researcher` |
| Worker | `contract-driven-execution` |
| Verifier | `task-orchestrator` (its full-context verifier protocol) |
| Code reviewer | `code-reviewer` |

The current snapshot contains six skill directories plus the governing rules, for nine Markdown artifacts. A role-specific artifact can only be supplied if it exists in that snapshot or another future artifact source. The operator role is not skill-filtered.

## Explicitly deferred

v0.1 does not implement:

- automatic event dispatch, provider wake-up, or role assignment;
- new swarm infrastructure beyond the subagent/decomposition semantics already described by the copied skill pipeline;
- Ledger-level hard enforcement of host filesystem, network, or tool access (the adapters request provider sandbox modes, but those providers enforce them);
- terminal-task compression, archive storage, retention, or TTL deletion;
- a database-backed store, daemon/server, remote API, MCP transport, or distributed lock;
- automatic translation of a textual rule into code or removal of superseded skill text;
- authenticated actor identities, a trusted mutation broker, or cryptographic tamper evidence against processes running as the same OS user;
- unbounded task histories: v0.1 caps each task at 1,000 events and a 16 MiB event log because atomic full-history commits and replay are deliberately optimized for small local ledgers.

Every rule above is expressed twice: once in `CommandHandler` at command time, and again in
`TaskTransitionValidator`, which re-validates each event during replay. The two copies must be changed
together — `tests/AILedger.Tests/Storage/NewCommandReplayTests.cs` commits every command through the
real file store and replays it in a fresh service to prove they agree, and
`tests/AILedger.Tests/Core/EventRegistrationTests.cs` proves no event type is missing from the
validator's switch. An unregistered event type would make a task permanently unreadable.

`Archive` does not move, compress, freeze, or delete task files. It is also the close-out boundary:
validated claims, rejected alternatives, and resolved escalations become lesson events. A later task
opened under the same ledger root recalls those archived lessons into its own event history and context.
