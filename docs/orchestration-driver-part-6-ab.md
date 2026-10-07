# Orchestration driver — Part 6A + 6B

## Delivery boundary

Historical delivery record: [Part 6C+D](orchestration-driver-part-6-cd.md) now supersedes the
repair/reuse, required-check, physical-reconciliation and completion/closeout restart handoffs below.
The validation results in this record are preserved unchanged.

This increment connects task-13 explicit acceptance to governed work completion and adds independent,
evidenced finding adjudication. It extends the existing assurance service, shared provider dispatch,
restricted driver and kernel completion owners. It does not add an acceptance ledger or launch path.

**6A and 6B are implemented for the bounded profile below. 6C and 6D remain pending.** This is not
an unattended real-work readiness claim. Development used the user's direct-work exemption, temporary
ledgers, fake providers and local checks. No live ledger, actual model provider, global installation,
commit or publication was used. Existing uncommitted Parts 1–5 were retained.

## Contracts and owners

| Contract / owner | Responsibility |
| --- | --- |
| Optional `AssurancePolicy.governed`, schema 1 | Explicit task ID and complete work-member set, configured by the trusted host rather than a provider request. |
| `AssuranceSnapshot.governed`, schema 1 | Actual physical candidate identity, completed working-run versions, governing-context digest and authority digest. |
| `AssuranceService` | Existing reads, check observations, report history, evidence validation, explicit acceptance and applicability. Canonical receipts remain in the existing task-13 journal. |
| `GovernedAssuranceContext` | Current governed role, capability, run/session, expiry and actual implementer association under the task mutation lock. |
| `GovernedCompletionHost` | Narrow trusted adapter from existing assurance admission to storage completion. It observes acceptance; it cannot author acceptance. |
| `IWorkCompletionAdmission` / lease | Process-local observation retained through append, with final applicability revalidation. Not a provider or serialized command grant. |
| `WorkItemCompleted.Acceptance`, schema 1 | Exact task-13 receipts, governed basis, verifier run and paired blind reviewer run recorded with completion. This is an association, not a second decision store. |
| `finding_judgments[]`, each schema 1 | Independent synthesis decision, original finding ID, affected requirements, rationale, uncertainty and concrete receipt references. |

Core remains independent of CLI/provider implementation. `WorkItemLifecycleRules` retains working,
verification, independent-provider and code-review gates and adds current acceptance admission.
`FileGovernedTaskService` strips caller-supplied admission, obtains a fresh host lease and checks the
expected ledger version and coordination fence. Only that service supplies process-local admission.
The restricted routine authority permits completion only with that admission and no verification waiver.

Command-time admission is intentionally stricter than historical replay. `WorkItemEventValidator`
validates the optional new receipt when present; old events need no new fields, hashes or acceptance.
Old optional-null assurance shapes retain their serialized identities. Tests separately demonstrate
new refusal and historical legal replay, including old negative verifier and later failed-work histories.

The pre-existing explicit operator verification waiver remains an exceptional trusted operator path.
It is not available to the driver or provider children, not an implicit fallback, and not a task-13
residual-risk acceptance. The bridge never requests it. Ordinary privileged `work complete` without
host admission cannot substitute completed runs for acceptance on resource-scoped work.

## Applicability and authority

The physical candidate is the same aggregate of declared candidate, requirement, source and transitive
dependency observations used by dispatch. Every bridge participant must already have the full declared
area closure in its grant. Inputs must remain in the governed work's resource roots and outside the
ledger. Authority files and the canonical store remain outside provider write grants and the ledger.
Missing grants or configuration stop admission; the host does not widen them or omit inputs.

The governed basis also includes task/work identity, completed working runs, current governing request,
prompt contract and orchestration plan, claims, constraints, decisions, and role assignments. The
normalized policy identity covers grants and check configuration. Changes conservatively invalidate
assurance. Disabling/expiring a producer also independently invalidates its endorsements without
preventing historical receipt recovery. Selective reuse after changes belongs to 6C.

Verification receipts must identify the actual paired governed verifier principal, run ID, provider and
recording time within that run. The governed reviewer remains blind: it receives neither task-13
requirements-aware tools nor the requirements narrative. A separate principal and session supplies
requirements-aware review through the trusted external assurance adapter. For this profile that actor
must be a current PlanningLead or Researcher with BuildContext. Verification remains an independently
provided Verifier run. The implementer cannot supply independent evidence for its own candidate.

The acceptor must have a current scoped task-13 acceptance grant and be a live governed Operator with
BuildContext and ManageWork. It must differ from implementation and selected evidence producers.
Acceptance is an explicit external trusted-client judgment; provider launch refuses acceptance-role
sessions. A provider-authored approval statement or generic routing Proceed grants nothing.

All governed assurance operations, including interrupted-check reconciliation, acquire the task lock
before the assurance journal lock. Completion holds both through the owning append. This serializes
ledger changes and findings with admission. Policy, producer authority, physical inputs and receipts
are reread at the final commit boundary. A late revocation or changed candidate refuses the write.
The existing owner lease/fence is rechecked; expiry does not imply any process stopped. Launcher
credentials remain transient and are never restored from receipts.

An exact retry returns the recorded receipt on its original body/key/session/policy binding. It does
not renew authority or assert current applicability. Inspection and completion independently recompute
applicability after restart. A newer failed, cancelled or active worker/assurance run blocks old evidence,
even if the last completed run still looks good. Required checks cannot be absent, failed, unknown,
not checked, interrupted or hidden behind an older passing report. Honest negative reports remain
recordable. A newer negative host check requires newer independent successful check evidence.

## Findings and disposition

Original `<report-id>:<key>` findings remain immutable and retrievable across report supersession,
policy/candidate changes and later worker assertions. Optional requirement IDs and uncertainties are
preserved in the finding. An adjudication must name current affected criteria, retaining every original
requirement ID. The original principal, session, provider, policy, physical candidate and governed
working basis remain available through the original receipt/snapshot.

Only an explicitly scoped independent synthesis principal may adjudicate. It cannot be the implementer
or original finding producer. A judgment must cite either:

- `source_trace`: an exact nonempty quote and path from the adjudicator's own current-session host
  read receipt; or
- `reproduction`: an exact stdout/stderr observation and configured check ID from a distinct principal's
  current, complete host test receipt. Unknown, truncated or interrupted reproduction is insufficient.

`upheld` records that the finding stands and prevents acceptance. `refuted` and `not_applicable` can
support acceptance when all current independent judgments agree and their evidence remains applicable.
`repaired` additionally requires changed physical candidate bytes and fresh independent evidence.
Judgments may honestly retain uncertainty, but uncertain judgments cannot dispose a finding at acceptance.
Conflicting adjudicators must resolve their disagreement with explicit new evidenced checkpoints;
selecting only the convenient report does not work. Every original finding needs a matching disposition.
Severity alone grants no dismissal. **This policy authorizes no residual-risk waiver or accepted-risk
finding disposition.** No failure/unknown check can be relabeled a pass through synthesis.

Receipt validation establishes observed evidence and provenance; it does not prove that a source quote
logically establishes the author's conclusion. Semantic relevance remains an independent judgment.
The existing governed challenge mechanism also remains binding: open challenges/escalations prevent
acceptance/completion, and unresolved independent governed claims cannot disappear through supersession.

The driver routes unresolved task-13 findings through its existing bounded Findings role when configured.
That PlanningLead/ImplementationLead receives only its existing scoped synthesis tools through shared
dispatch. Its Repair/Replan routing uses the existing paths; Proceed only retries owning admission.
It cannot manufacture acceptance. No finding-role profile means an explicit unresolved pause. A repair
count is never success: unchanged unresolved work stays paused or follows further authorized repair.
Business decisions, exceptional authority and investigated external unknowns retain their separate
trusted escalation path.

## Supported setup and use

This profile supports one selected governed work member and one or more bounded declared task-13 areas,
including explicit dependent areas. Each area needs independent review/verification and explicit
acceptance; dependencies must be accepted first on the same governed basis. It does not automatically
create scopes or convert a multi-member task into an accepted whole.

1. Prepare current work, governing artifacts, independent role/provider profiles, protected task-13
   policy/store and existing complete resource grants through the established trusted setup. Configure
   the actual latest worker as `implementer`. Extend the existing schema-1 policy with:

   ```json
   "governed": { "schema_version": 1, "task_id": "T1", "work_item_ids": ["A"] }
   ```

2. Configure task-13 principals for the governed verifier (`verification`), separate requirements-aware
   reviewer (`review`), optional independent Findings lead (`synthesis`), and external acceptor
   (`acceptance`). Associate each with its real governed actor and complete existing area grants.
   Configure all required checks using executable digests and declared inputs. Do not supply the
   requirements-aware policy to the blind reviewer.
3. Run the existing driver with `--assurance-authority`, `--assurance-store`, and
   `--acceptance-principal`. That option names the trusted completion observer; it does not make an
   acceptance decision. A prepared current verifier and blind reviewer can finish, then the driver
   returns `AwaitingAcceptance` until the separate evidence/decision exists.
4. Use the existing trusted `assurance` CLI/MCP adapter with `--governed-root` for requirements-aware
   review and explicit acceptance. This root is the task-store directory containing `<task>/events.jsonl`.
   Follow `inspect_assurance`, `read_assurance`, `record_assurance` and `accept_assurance` schemas in
   `docs/handoff-assurance-v1/`. A command shape for the explicit decision is:

   ```sh
   dotnet src/AILedger.Cli/bin/Debug/net8.0/AILedger.Cli.dll assurance accept_assurance \
     --authority /protected/authority.json --store /protected/assurance \
     --governed-root /repository/.ailedger/tasks \
     --principal operator --session explicit-acceptance-session --body-stdin < decision.json
   ```

   `decision.json` must contain the exact current binding, applicable independent report receipt IDs,
   reasoned acceptance and evidenced dispositions for every original finding. The host validates these;
   placeholders or prose approval cannot stand in for receipts.
5. Resume `orchestrate run` with the same selected work and acceptance configuration. Existing current
   verification/review are reused without another provider dispatch. The completion host revalidates,
   records the association through ordinary work completion, and the driver attempts Learn. Remaining
   unaccepted members prevent closeout. Without the bridge option, the old explicit pending boundary
   remains; direct ordinary `work complete` does not secretly open an acceptance service.

The bounded host-check profile retains task-13's limits (including its existing eight check batches
per case). Exhaustion pauses with unresolved work; it is not a repair cap that produces success.
Configure `--owner-lease-seconds` longer than the longest bounded governed assurance call plus IO margin:
the task lock is held during checks, so heartbeat renewal can wait. Lease loss fences further writes;
reconciliation must establish process termination separately. Broad operating guidance is still 6D.

## Validation record

| Check | Actual result |
| --- | --- |
| Final focused assurance/orchestration/bundle/historical replay selection | **293 passed**, 0 failed/skipped; 3m08s. |
| Corrected current-command and replay regression selection | **113 passed**, 0 failed/skipped. |
| Integrated independent finding disposition and dependent-area completion | **2 passed**, 0 failed/skipped; also included in the final 293. |
| Integrated built CLI | Acceptance receipt replay and restarted driver completion passed in the final selection; no provider dispatch on resume. |
| Final implementation, default `dotnet test --nologo` | **2,642 passed, 1 failed, 0 skipped**: main 2,543/1, memory 99/0; main 6m30s. |
| Unchanged isolated telemetry diagnostic | **2 passed**, 0 failed/skipped (both provider variants). |
| Additional serialized full suite | **2,643 passed**, 0 failed/skipped: main 2,544 + memory 99; main 8m43s. |
| `git diff --check` | Clean. |

The final default run's sole failure was
`InspectionMcpTests.TrustedProviderRelaySupportsInspectionRetrievalAndReadinessWithRevocation(provider: "claude")`:
**20 of 36 telemetry rows** were retained. Functional inspection/retrieval/readiness/revocation assertions
passed before the row-count assertion failed. Both unchanged variants passed in isolation afterward.
The existing 250 ms telemetry-write deadline is a plausible contributor under load, not an established
cause. This remains a failed default run; an isolated or serialized pass does not turn it green.

Commands for final checks:

```sh
dotnet test tests/AILedger.Tests/AILedger.Tests.csproj --nologo --filter 'FullyQualifiedName~HandoffAssurance|FullyQualifiedName~Orchestration|FullyQualifiedName~OrchestrationInventoryTests|FullyQualifiedName~VerificationSequenceTests|FullyQualifiedName~BundleAssuranceTests|FullyQualifiedName~BundleLifecycleTests|FullyQualifiedName~BundleArtifactTests|FullyQualifiedName~BundleProjectionTests'
dotnet test --nologo
dotnet test tests/AILedger.Tests/AILedger.Tests.csproj --no-build --no-restore --nologo --filter 'FullyQualifiedName~TrustedProviderRelaySupportsInspectionRetrievalAndReadinessWithRevocation'
dotnet test --no-build --no-restore --nologo -- xUnit.ParallelizeTestCollections=false
```

Initial failures were retained as evidence of the work rather than hidden. The first broad focused
run had 209 passes and 21 failures: permissive completion expectations and an old unsupported
not-applicable disposition. After updating current-command expectations and retaining separate legal
historical replay, a broader selection had 252 passes and two fixture failures (an accidentally changed
unscoped expectation and a nonsequential historical event ID). Both were corrected. A later focused
selection passed 34 tests, including built CLI acceptance replay/completion and candidate mutation
immediately before append.

The first default full run had **2,535 passes / 5 failures** in the main project, plus **99 memory
passes**, no skips. All five expected the old current-command behavior: TaskDebt, StagePrerequisite,
NewCommandReplay, WorkItemCli and InspectionAssurance. They now assert missing acceptance where
appropriate; historical replay remains separately exercised. The resulting regression selection passed
113 tests. A new integration fixture initially failed compilation (optional properties used as
constructor arguments); it was corrected. Its first runnable dependent-area case exposed the missing
governed binding in check materialization (31 passes / 1 failure); the owning dependency comparison
was corrected rather than weakening the check.

No telemetry assertions or unrelated collection deadlines were changed. Part 5's earlier 34/36
Claude telemetry observation remains in its own record; a serialized pass here cannot erase a failed
default run. Interrupted/host-suspended elapsed time from an early focused run is not a performance metric.

## Concrete 6C / 6D handoff

- **6C:** replace conservative whole-basis invalidation with demonstrated repair-impact and dependency
  assessment; schedule required checks before dependent assurance; establish safe selective reuse after
  changed, failed or cancelled work. Preserve original finding IDs and current authority. Investigate
  repeated and repair-induced defect classes using existing lessons and bounded Findings routing.
- **6D:** assemble A–C with trusted intake, multiple repairs, scoped preparation, interrupted checks,
  driver ownership loss, restart, explicit acceptance, completion and closeout. Broaden CLI negative and
  deployment coverage, measure the user experience, and publish full operating instructions only for
  demonstrated profiles. Test recovery when work completion committed but the subsequent Learn transition
  did not: current restart preserves completed work and pauses instead of reopening or redispatching it.
- Keep multi-member orchestration outside the supported single-selection bridge until its lifecycle is
  demonstrated. Complete member A does not imply member B or the task is accepted. The supported positive
  fixture deliberately stops with other members unresolved.
- Physical snapshots are observations, not OS filesystem reservations. Final revalidation detects the
  tested concurrent mutation before append, but arbitrary external writes after the final read are not
  atomically excluded. Declared-input completeness and check relevance still need independent judgment;
  checks are bounded, not a hermetic build system.
- New candidate, governing or authority state requires fresh applicable evidence. Unsupported repair or
  reconciliation never reuses an old green receipt by assumption. Real-provider runs, live use,
  installation and publication need separate authorization; none was performed here.

Subsequent delivery: [Part 6C+D](orchestration-driver-part-6-cd.md) implements the repair/recovery
handoff above; [Part 7](orchestration-driver-part-7.md) records startup diagnostics, behavioral
validation and the remaining real-provider/adoption boundary.
