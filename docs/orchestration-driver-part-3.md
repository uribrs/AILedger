# Orchestration driver — Part 3: reusable governed dispatch

Status: implemented; validation below. Parent: [scope and delivery parts](orchestration-driver-scope.md).
This is the shared launch application service, not an orchestration driver. Development used the
existing worktree, temporary ledgers, fake adapters and local subprocess fixtures. Parts 1/2 changes
remain in place; no live ledger, paid provider, global installation, branch change or commit was used.

## API and placement

[DispatchContracts](../src/AILedger.Cli/Dispatch/DispatchContracts.cs) exposes
`IProviderDispatchService.LaunchAsync(ProviderDispatchRequest, CancellationToken)` and typed results.
The request names task, acting actor, optional subject, run/work, provider, resume identity, model,
resource selection, candidate/verifier pairing, timeout, context budget and correlation. Its two
brief-exception fields preserve human CLI compatibility; routine authority refuses them.

`DispatchHostOptions` holds trusted ledger/lesson/cognitive paths, executable, output schema and
assurance authority/store. Those are composition inputs, not child-authored handoff fields or options
that the routine driver can change on an already composed service. The host must supply matching
storage and ledger roots, trusted adapters and trusted configuration.

[ProviderDispatchHost](../src/AILedger.Cli/Dispatch/ProviderDispatchHost.cs) composes the service.
The existing [CLI facade](../src/AILedger.Cli/Providers/ProviderLauncher.cs) parses arguments, invokes
that service and presents its result. Execution never reconstructs a command line or parses CLI
options. CLI stdout, exit codes, cancellation and recovery diagnostics remain transport concerns.
Invalid CLI timeout values become an invalid typed duration, checked at the established request
construction point so the admitted run is cleaned up without claiming delivered-manifest metadata.

The cohesive `Dispatch` folder stays in the existing executable host assembly. That assembly already
owns the authenticated stdio relay, assurance composition, cognitive loading and verification
preflight. A second project would currently require moving that whole host or adding bridging
interfaces solely for assembly separation. Future host code can reference the public interface and
composition root directly. Core still depends on neither CLI, driver nor provider implementation;
its only new dispatch-related fact is a typed classification on an existing governance refusal.

## Authority composition

Trusted startup creates the routine service and gives the driver **only** `IProviderDispatchService`:

```csharp
var dispatch = ProviderDispatchHost.CreateRoutine(
    trustedFileService, routineAuthority, hostOptions, trustedAdapters, contextAssembler);
var receipt = await dispatch.LaunchAsync(request, cancellationToken);
```

`FileGovernedTaskService.BindRoutineOrchestration` installs the Part 2 wrapper at the locked mutation
boundary. Run admission and completion use this restricted service. Before resolving or probing an
adapter, the same authority checks the prospective launch for task/actor binding, expiry/revocation,
assignment changes and forbidden brief exceptions. The actual `StartRunCommand` checks again after
preparation/probing, through existing storage and kernel admission.

The host retains its original service privately for subject-filtered context, inspection-based
assurance authorization and `BindAgentSession`. Agent recording wraps the trusted kernel service,
not the routine handler: a researcher records under its own bounded session, while run lifecycle
commands remain under the delegating routine authority. The driver does not receive an unrestricted
operator service to make recordings work. The confined subprocess fixture exercises this exact
composition and still rejects a forged local operator host.

`CreateHuman` retains authorized local CLI exceptions. It is trusted startup API, never a provider
MCP tool. In-process hostile host code and the privileged OS owner remain outside the Part 2 trust
boundary. No new authentication is inferred from an actor name or from constructing a record.

The same mandatory process isolation, protected paths, grant ceilings, expiring/revocable agent
binding and launcher-held completion secret remain. Provider children receive only their bound
endpoint; neither host service nor routine authority nor completion token reaches their manifest,
environment, return receipt or tools. Acceptance remains unavailable to every provider child,
including one named `operator`.

## Preserved launch sequence

1. Read current state and acting-actor skill digests, build candidate binding, and run the existing
   authorization/admission and directory-grant rules in their established order.
2. Prepare mandatory isolation; require bound-session support; check declared verification
   environment prerequisites; prepare configured assurance and prospective subject context before
   resolving/probing a provider. Preparation/admission refusal never calls `RunAsync`.
3. Resolve the trusted adapter/executable, protect the executable, probe the actual version, generate
   the private completion secret, and submit `StartRunCommand` for fresh locked admission.
4. Reopen/revalidate configured assurance, assemble the admitted subject's brief, validate timeout,
   start the expiring bound recording host, and construct the complete adapter request. Hash the exact
   serialized manifest only at delivery, retaining its artifact count. Dispose the endpoint afterward.
5. Validate returned run identity and retain the raw provider result before ledger closure. Record
   observed session/model/usage/termination, independent first-write measurement and delivery metadata.
   Unobserved telemetry remains null. Thrown provider/preparation faults use existing Failed/Cancelled
   cleanup without manufacturing a provider result.
6. Preserve required verifier/reviewer outputs and candidate applicability. A completed provider with
   missing required output keeps its Completed provider result but gets the existing Failed ledger
   fallback. Keep cancellation-independent cleanup, bounded completion retries and retention failure
   reporting. A provider return never completes a work item or establishes acceptance.

Task-13 preparation, scoped grants, independent principals, blind governed review and the separate
requirements-aware context remain unchanged. The direct routine verifier test receives configured
assurance tools; the subsequent blind reviewer explicitly receives `incompatible_context` before
adapter probing. No profile is broadened or input omitted to make it run. Absence of both assurance
options preserves ordinary CLI behavior and **does not establish assurance**; the future driver must
require the host configuration appropriate to its intended work. A partial configuration is reported
as `PreparationUnavailable`, never silently ignored.

## Typed outcomes and uncertainty

| Result field | Meaning |
|---|---|
| `ProviderResult` | Actual adapter return for this run, including its own status, session, events and observations; absent for thrown faults or rejected foreign run IDs. |
| `Started` / `Completed` | Acknowledged command receipts: event IDs, task version and recorded run status. These are observations, not a universal version fence. |
| `StartRecording` / `CompletionRecording` | `NotAttempted`, `Recorded`, `Refused` or `Unknown`. A storage failure can leave the true ledger outcome unknown even when a result is retained. |
| `Retention` | `NotAttempted`, `Retained(path)` or `Failed(path, diagnostic)`, independent of provider/ledger outcomes. The in-memory provider result survives retention failure. |
| `Failure` | Refusal, invalid request, unavailable/unsupported preparation, provider fault/unsuccessful outcome, cancellation, completion-recording failure or unexpected error. |
| `DeliveredManifestHash` / count | Exact delivery metadata; absent if preparation failed before delivery. |
| `AssurancePreparation` | `NotConfigured`, `Requested`, `Prepared` or `Opened`; absence of assurance is explicit even when ordinary launch succeeds. None of these states asserts acceptance. |
| `MissingRequiredOutput` | The existing kernel output prerequisite that triggered Failed fallback; it is not an assessment of technical correctness. |
| `Phase` | Last phase, or original fault phase when cleanup subsequently ran. |

Do not equate `Failure == null` with durable retention, accepted work or task-13 completion. Inspect
all relevant dimensions. `Error` is a nonserialized compatibility exception for the in-process CLI;
routing uses typed fields. Assurance refusal codes are retained when supplied by the owning service.
Other kernel refusals keep an explicit `Opaque` fallback and their diagnostic. Neither exception
message matching, next-action text/order nor readiness observations control execution.

The preexisting required-output rule now supplies `GovernanceRefusalKind.RequiredRunOutput`; the
storage repetition notice preserves that fact. Rule predicates and event schemas are unchanged.
Historical replay gained no prerequisite. The Part 1 acceptance-gap characterization still applies.

A completion attempt whose acknowledgment is lost can have committed. Existing bounded I/O retries
remain, but an eventual refusal does not erase that uncertainty: the receipt stays `Unknown` until
reconciled. A lost run-start acknowledgment likewise starts no provider and reports an unknown write.
No automatic relaunch, orphan repair, crash-safe recovery, dispatch deduplication or durable ownership
is added. The private completion secret is not persisted or exposed as a recovery credential.

## Typed cognitive host handoffs and Part 4

[HostHandoffs](../src/AILedger.Cli/Dispatch/HostHandoffs.cs) defines two bounded cognitive payloads:

- `GoverningArtifactHandoff`: InternalRecon, PromptContract or OrchestrationPlan content, title and
  optional predecessor. No UserRequest, acceptance, arbitrary artifact kind or command passthrough.
- `LessonConsultationHandoff`: existing consultation purpose, question, tags and claim references.
  Retrieved candidates and their identities must come from trusted storage, not the child.

The request has a stable request ID. `CognitiveHostBinding` is separate and supplied only by the
trusted authenticated session. `HostHandoffReceipt` distinguishes Unsupported, Refused, Recorded and
Unknown, with actual event/artifact references when available. `DispatchHostHandoffs.Support` declares
both operations **unavailable** today. No tool advertises an implemented handoff, and no handler or
arbitrary command execution has been added.

Part 4 must implement the live authenticated transport, strict payload validation, role/capability
checks, host-assigned durable identities, producer-run attribution, exact request/reply semantics,
role-filtered lesson responses and existing kernel mutations. Governing artifacts must be filed
while the producer run remains active; the current launcher closes the run when its adapter returns.
A post-return file transcript is therefore insufficient. It must not claim coverage until that live
path works, nor let the child select the host identity or claim human approval. Other unsupported
cognitive operations (decision/challenge/escalation authoring, claim supersession and lesson marking)
remain unsupported; future typed contracts and actual support declarations are required before
scheduling roles that depend on them. Trusted user decisions and exceptional authorization remain
separate from agent cognitive content.

Part 4 also owns scheduling, waiting, judgment routing and explicit supported lifecycle coverage.
It must not infer successful work from process/run completion. Automatic work completion still
requires Part 6's explicit acceptance bridge.

## Part 5 and adoption limits

Part 5 owns durable dispatch intent/identity, ownership epochs, concurrency coordination and
reconciliation across preparation, start, process, retention and closure crash windows. It must
reconcile `Unknown` writes against the actual ledger and retained data before deciding whether a
new dispatch is safe. These receipts are invocation observations, not a transactional outbox,
idempotent dispatch protocol or recovery journal.

Part 2's macOS-only process boundary and untested live-provider authentication/cache compatibility
remain. Unsupported platforms fail explicitly. No assertion here proves live provider compatibility,
operational cost improvement, a complete orchestration lifecycle or technical acceptance.

## Validation

Validation uses temporary ledgers and fake adapters, including real confined fixture subprocesses.
`ProviderDispatchTests` covers CLI/direct equivalence, routine exceptions and revocation, preparation
refusals, exact delivery metadata, cancellation/fault cleanup, wrong-run results, result-retention
failure, unknown start/completion writes and explicit unavailable handoffs. `DispatchAssuranceTests`
covers typed required-output fallback, repeated refusal facts, provider independence, blind review,
configured routine assurance and incompatible reviewer context. Existing confinement and acceptance
fixtures additionally run through routine composition.

Validation on 2026-10-07:

| Command / check | Result |
|---|---|
| Focused launch, authority, assurance, grant, confinement and dispatch selection | **323 passed**, 0 failed/skipped. |
| Focused dispatch/assurance plus unchanged reruns of both diagnostic failures | **123 passed**, 0 failed/skipped. |
| Initial default `dotnet test --nologo` | **2,538 passed, 2 failed**, 0 skipped (2,439 main passed + 99 memory). Exit 1. |
| Final default `dotnet test --nologo`, after final receipt/helper refinements | **2,540 passed** (2,441 main + 99 memory), 0 failed/skipped. Exit 0. |
| `dotnet test --no-build --no-restore --nologo -- xUnit.ParallelizeTestCollections=false` | **2,540 passed** (2,441 main + 99 memory), 0 failed/skipped. Exit 0. |
| `git diff --check` and delivery-document link/whitespace checks | Passed. |

The initial default run failed these unchanged diagnostic assertions:

- `InspectionMcpTests.TrustedProviderRelaySupportsInspectionRetrievalAndReadinessWithRevocation(codex)`:
  all response assertions passed, then the test found 28 of 36 expected inspection telemetry rows.
  This is the failure family documented in Part 2.
- `FindingsMcpBoundaryTests.BoundConcurrencyRejectsExcessCallsAndKeepsCancellationResponsive`:
  excess-call and cancellation-response assertions passed, then `Assert.Empty(stderr)` received a
  JSON record starting with `transport_attempt_id`. This additional diagnostic failure was observed
  in this run; it was not listed in the Part 2 baseline.

The transport journal's existing fallback emits that shape with `collection_status: unavailable`
when telemetry capture fails. Its 250 ms deadline covers semaphore waiting, append and flush;
the separate application collector also has a 250 ms deadline. The inspection relay supplies a
null diagnostic writer, so failed transport captures can appear as missing rows. Contention/timeouts
are plausible, but the fallback discards the underlying exception and the evidence does not prove
that specific cause. Both failing tests passed unchanged in the focused rerun, and the final default
full run passed. No collector, deadline or failing assertion was altered. The initial default run
remains a failed run; a later pass does not erase it.
