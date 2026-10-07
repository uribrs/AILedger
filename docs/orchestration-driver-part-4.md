# Orchestration driver — Part 4: driver and decision handoffs

Status: implemented for the bounded profiles below; validation recorded at the end.
Parent: [scope and delivery parts](orchestration-driver-scope.md).
Development used the existing worktree, temporary ledgers, fake adapters and local fixture
processes. No live ledger, real provider, global installation, branch change or commit was used.
Parts 1–3 remain in place. **This driver is not ready for unattended real work.**

## API and CLI

[OrchestrationHost](../src/AILedger.Cli/Orchestration/OrchestrationHost.cs) creates an
`IOrchestrationDriver` from a trusted `FileGovernedTaskService`, `DriverHostOptions`, adapters
and context assembler. `DriveAsync(DriverRequest, CancellationToken)` routes one selected work
item, or task-wide cognitive phases, until a typed stopping boundary. Each driver instance is
single-invocation; it does not provide durable ownership or resumption.

`DriverHostOptions` supplies the task/operator delegation, working directory, cognitive root,
protected dispatch/assurance configuration, per-operation subject/provider/model profiles,
timeout and expiry. `DriverRequest` selects an existing work item and optional recorded serial
justification. There is no waiver, acceptance, arbitrary command, actor replacement or approval field.

The cohesive `Orchestration` and `Cognitive` folders remain in the existing CLI execution-host
assembly, alongside shared dispatch, authenticated transport and assurance composition. Core has
no dependency on them. The new Core routing-assessment command/event records authored judgment;
both command handling and replay validate only this new event. No historical completion predicate
or stage prerequisite was tightened.

```sh
dotnet run --project src/AILedger.Cli -- orchestrate coverage --root /absolute/fixture/tasks

dotnet run --project src/AILedger.Cli -- orchestrate run \
  --root /absolute/fixture/tasks --lesson-root /absolute/fixture/lessons \
  --task fixture-task --actor operator --work W1 \
  --profiles /absolute/protected/profiles.json \
  --working-directory /absolute/fixture/repository --cognitive-root /absolute/cognitive \
  --assurance-authority /absolute/protected/authority.json \
  --assurance-store /absolute/protected/store
```

These examples describe an explicitly prepared fixture; they are not authorization for provider
spend. Profiles are strict JSON, at most 16 KiB, with no identities inferred from model prose:

```json
{
  "schema_version": 1,
  "agents": [
    { "work": "implementation", "subject": "worker", "provider": "codex" },
    { "work": "repair", "subject": "worker", "provider": "codex" },
    { "work": "verification", "subject": "verifier", "provider": "claude" },
    { "work": "review", "subject": "reviewer", "provider": "codex" },
    { "work": "findings", "subject": "lead", "provider": "codex" }
  ]
}
```

Task-wide profiles use `discovery`, `recon`, `research`, `design`, `scope` and `closeout`.
Research requires a Researcher; the others and findings interpretation require a lead. Profiles
do not assign roles or expand existing capabilities. Optional `model` is passed to shared dispatch.

The CLI prints `DriverResult`, including typed status, stable code, diagnostic, observed stage/version,
full shared-dispatch receipts, acknowledged transition event IDs and open escalation records.
Exit 4 means blocked, unsupported, awaiting human decision or awaiting acceptance; 5 means unknown;
130 means cancelled. Exit 0 only observes an archived ledger and makes no assertion of correctness.

## Deterministic routing and supported coverage

`orchestrate coverage` publishes version 1 and every `TaskStage` value. New/unknown stages and
unconfigured cognitive roles stop explicitly. Routing never reads NextActionContract action text,
action order, exception messages or readiness observations as instructions.

| Stage | Supported operation | Explicit boundary |
|---|---|---|
| Discovery | Lead investigation, findings, proposed decisions and escalations | Existing operator-authored UserRequest and trusted constraints required; no model-authored intake authority |
| Research | Live recon with lesson consultation; Researcher for typed external recon assessments; lead refresh after research | Existing kernel checks current claim-set hash, external claim dispositions and research episode consultations |
| Design | Lead planning/replanning and live PromptContract filing | Business decisions stay on the separate trusted path |
| Scope | Lead OrchestrationPlan filing | Automatic work creation, role staffing and scope changes are unsupported |
| Ready | Advance one explicitly selected prepared item through existing stage rules | Missing work or a replanned scope stops for trusted preparation |
| Execution | Scoped Worker; explicit authored routing assessment | Missing task-13 configuration, invalid grants or incompatible roles block before work dispatch |
| Verification | Independent verifier with task-13 tools and candidate-bound governed output | Missing output/judgment or provider failure does not become success |
| Repair | Same-scope Worker repair; fresh verification and paired review | No repair count establishes success; cancellation/unknowns stop; scope changes go to planning |
| Review | Candidate-bound, paired blind governed CodeReviewer | Requirements-aware independent assurance review, acceptance and completion are still pending |
| Learn | Lead closeout synthesis, lesson marking and governed archive | Every work item must already be completed or abandoned through a separate authorized path |
| Archive | Terminal observation | No reopening, retrospective authoring or retention deletion |

Single-item sequencing can coexist with other prepared items in the ledger, but does not schedule
those items. Bundle scheduling, concurrent workers, task-12 episodes, arbitrary build profiles and
multi-task routing are unsupported. Existing serial-execution prerequisites remain authoritative;
`--serial-justification` references an already recorded alternative.

Each provider must record a `RoutingAssessment`: assigned cognitive work, `Proceed`, `Repair`,
`Replan` or `Blocked`, rationale and 1–16 own current-run evidence IDs. The kernel verifies the
owning active run, role, AddEvidence capability and reference provenance. The assessment is immutable.
The host additionally checks its work against the assigned operation. Completed runs and artifacts
remain lifecycle facts; they cannot substitute for this explicit judgment or for technical acceptance.

A blocked work assessment routes to a configured lead for bounded findings interpretation. A genuine
business decision or investigated unknown is filed as an escalation, whose options/recommendation or
attempt evidence are returned to the caller. The driver never resolves it. Repository questions do
not automatically become user escalations. Replanning follows legal backward edges, then stops at
Ready for renewed trusted scope preparation. The driver never creates or completes work itself.

## Live cognitive handoffs

The existing authenticated provider relay advertises `cognitive_handoff`. No standalone writable
configuration can enable it: its grant is composed only by the live execution host. The strict
128 KiB request contains only `request_id` and a discriminated `operation`. Tool schemas and parsers
reject unknown/duplicate members, unsupported operations, invalid enums and caller identity/approval
fields before mutation. Existing findings, alternatives, claim dispositions and verification/review
artifact tools remain available through their existing services.

| Operation | Governed effect |
|---|---|
| `governing_artifact` | InternalRecon, PromptContract or OrchestrationPlan; host-generated artifact ID, active bound producer, existing content/stage/revision rules |
| `lesson_consultation` | Existing storage selects lessons, kernel filters by role/audience and records episode/claim-set consultation; only served lessons are returned |
| `routing_assessment` | New immutable per-run authored routing judgment; not acceptance |
| `decision_proposal` | Host-generated proposed decision; no resolution/approval authority |
| `escalation` | Host-generated business-decision or true-unknown record bound to the producer's work scope; existing shape/evidence requirements |
| `lesson_mark` | Existing lead-authorized Learn-stage marking; no command is executed from its verify text |
| `closeout_synthesis` | Existing lead-authored task-wide synthesis rules; producer remains null as required by the existing closeout contract |

Governing filing and consultation happen while the producer is active, before the launcher closes
its run. This is not post-return transcription. Closeout synthesis intentionally follows its existing
non-producer artifact contract, while requiring a current authenticated lead session at the host.

`HostHandoffReceipt` distinguishes Recorded, Refused, Unsupported and Unknown and retains acknowledged
event IDs, host-assigned identity, task version and filtered lesson results where applicable. The
session serializes handoffs and caches up to 128 original key/body/receipt entries across relay
reconnections. Exact successful retries return the same receipt. A conflicting body cannot reuse a
key. An unknown write preserves any assigned identity, freezes further cognitive mutations and stays
unknown on retry; it does not automatically execute again. Revocation, expiry, run closure or assignment
change is checked before cached replies as well as new operations.

The session cache is in-memory, not durable recovery. A crash requires Part 5 reconciliation before
retry. Dispatch drains/disposes the endpoint before collecting cognitive receipts, then preserves them
alongside the provider result and independent start/completion/retention observations. Cognitive
telemetry gets its own population; existing collector deadlines and diagnostic assertions are unchanged.

## Authority and assurance

`OrchestrationHost` creates one task-bound `RoutineOrchestrationAuthority`. The driver receives a
narrow reader/brief/transition surface backed by the restricted service, plus Part 3 dispatch services.
It never receives an unrestricted operator service or falls back to human composition. Mutations
recheck authority through the locked storage/kernel path. Human exceptions, staffing, scope changes,
escalation resolution and acceptance require their separate trusted entry points.

The execution host retains subject-filtered context, expiring bound recording sessions and private
launcher-owned completion. Process confinement is unchanged. Ledger, lessons, profiles and assurance
configuration/store paths stay protected even for workers and blind reviewers that receive no
requirements-aware assurance tools. Model-authored approval text grants no authority. Unsupported
platforms remain unsupported; live provider credential/cache compatibility remains unproven.

Before code dispatch, the driver uses existing assurance preparation to validate the configured verifier,
complete declared area/dependency closure, protected paths, input grants and supported bounds. It
requires a provider independent of actual/configured work. `CaptureBindingIdentityAsync` reuses the
existing task-13 snapshot reader and hashes the ordered declared area binding digests. It neither
omits inputs to fit nor invents a successful check or acceptance receipt.

Verification and review use this observed candidate identity with the existing Part 3 provenance binding.
Review names the actual completed verifier. Recapture before review detects changed configured inputs;
a different candidate stops for fresh verification. Repair gets fresh working provenance and verification.
The candidate identity describes configured captured inputs only; trusted setup remains responsible
for declaring all material inputs. It is not a transactional snapshot of an arbitrary repository and
is not Part 6's acceptance association.

Blind review uses the existing candidate-bound isolation, excluding goals, requirements, planning and
verifier narratives. Requirements-aware task-13 review cannot be placed in that context. A supplied
incompatible review profile returns `incompatible_context`; no grant or profile is widened. The separate
requirements-aware review and accepting authority remain explicit pending work at the final boundary.
No provider child receives `accept_assurance`. A `Proceed` review stops at `AwaitingAcceptance`; it never
calls permissive work completion, changes the task to Learn, or implies task-13 acceptance.

## Part 5/6 handoff and limits

Part 5 must add durable owner epochs, dispatch intent/deduplication, version fencing and crash-safe
reconciliation. UUID run IDs in this prototype are not a dispatch deduplication protocol. It refuses
active runs and does not re-execute unknown dispatches. A latest failed/cancelled working run stops for
physical candidate reconciliation, preserving Part 1's historical acceptance-gap tests. Cancellation
while a driver mutation is unacknowledged remains unknown. Concurrent independent drivers are not safe;
the local single-invocation guard is not a cross-process lease.

Part 6 must implement independent requirements-aware review association, applicable task-13 acceptance,
finding disposition, candidate/work provenance revalidation and correct automatic completion. The
current router deliberately stops before that bridge. Changed inputs, interrupted work, required outputs
and explicit judgments remain distinct facts. No existing acceptance gap was repaired incidentally and
no historical legal replay was tightened.

## Validation

Validation uses temporary ledgers and fake adapters, with live authenticated relay processes. It covers
implementation → verification → blind review, verifier and reviewer repair loops, external research
and live recon/plan filing, closeout/lesson minting, role-filtered consultation, key/body retries,
identity/approval spoofing, role/expiry/run revocation, uncertainty freezes, changed candidates,
independent-provider selection, missing/incompatible assurance, engineering judgment routing, genuine
escalations, cancellation/provider failures, CLI boundaries and stopping before work completion.
Existing shared-dispatch and real confined subprocess fixtures remain in the regression selection.

Validation on 2026-10-07:

| Command / check | Actual result |
|---|---|
| Final focused orchestration, dispatch, authority, confinement, findings-session, assurance-provider and CLI-catalog selection | **206 passed**, 0 failed/skipped. |
| Initial default `dotnet test --nologo` | **2,577 passed, 1 failed**, 0 skipped (2,478 main passed + 99 memory). Exit 1. Command-catalog expectation omitted the two new commands; corrected without weakening the inventory assertion. |
| Final default `dotnet test --nologo` | **2,577 passed, 1 failed**, 0 skipped (2,478 main passed + 99 memory). Exit 1. The unchanged Claude inspection fixture retained 22 of 36 expected telemetry rows. |
| Unchanged focused rerun of both inspection provider cases and the related concurrency diagnostic fixture | **3 passed**, 0 failed/skipped. |
| `dotnet test --no-build --no-restore --nologo -- xUnit.ParallelizeTestCollections=false` | **2,578 passed** (2,479 main + 99 memory), 0 failed/skipped. Exit 0. Additional check; default result above remains failed. |
| Built CLI `orchestrate coverage` with temporary roots | Passed: eleven stages, seven implemented cognitive operations, explicit fixture-only status. |
| `git diff --check` and Part 4 documentation links | Passed. |

The final default failure was
`InspectionMcpTests.TrustedProviderRelaySupportsInspectionRetrievalAndReadinessWithRevocation(claude)`.
All response assertions, including revocation, passed before the diagnostic-row count failed.
This is the same missing-inspection-telemetry failure family documented in Part 3; that delivery's
initial failure affected the Codex case. Inspection assertions and collector behavior were not changed.
The transport collector still has its existing 250 ms deadline spanning semaphore admission,
append and flush. Its fallback does not preserve the underlying exception, so contention/timeouts
remain a plausible explanation rather than an established cause. Part 4 adds only a separate
cognitive-handoff telemetry population/path, leaving inspection rows and collection deadlines intact.
No skips, relaxed assertions or unrelated collector changes were used. A focused rerun or serialized
pass does not make this failed default run green.

Subsequent startup and deployment observations are in [Part 7](orchestration-driver-part-7.md).
