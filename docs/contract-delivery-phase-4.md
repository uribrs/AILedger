# Item 64: Phase 4 integrated validation and observation preparation

**2026-09-30: integrated engineering validation passed; observation preparation is complete.
Installation is complete: **2.0.186-dirty from 1ba8aa0**. User-owned observed use has not run.
Phase 4 and item 64 remain open.
Phases 1–3 remain complete.**

Development followed the user's explicit direct-session exception: no governed development task,
agents, real providers, billable episodes, live ledger/lesson writes, installation, global skill
edits, merge or push. Existing phases 1–3 and unrelated changes are preserved. All new execution
uses temporary application/kernel stores and stub adapters; the MCP relay is a real local process.
The existing sequential test script is a test runner, not development governance.
After engineering completion, the user explicitly authorized installation; that separate result is
recorded below. No real task/provider observation was authorized or performed.

## Production path and evidence map

The covered path remains initial coordinator `context build`, ordinary fresh `provider launch`
and its return, and coordinator artifact/stage responses between launches. No prepare call,
acknowledgment or extra discovery round trip is needed. Resume, reconnect, batch, arbitrary direct
launches and universal ingress delivery remain outside this claim.

`ContextBriefingCliCommands.BuildAsync` → `ContextAssembler.Build/Assemble` →
`NextActionContracts.Observe` supplies the initial packet. The context version is the version
assembled **before** the existing context-built event. `CliCommandExecutor.ExecuteAsync` supplies
artifact/stage packets from the mutation's committed version. `ProviderLauncher.LaunchAsync` →
`WriteProviderReturnAsync` supplies termination packets from current persisted state, after cleanup
or completion, before returning control. Both Claude and Codex use this host path.

Launch order is grants/admission/environment checks → actual prospective role-filtered assembly,
budget and configured assurance preparation → adapter resolution/version probe → current locked
`StartRunCommand` admission → current assembly and assurance revalidation → supplied relay/request →
adapter `RunAsync` → durable termination/output checks → coordinator response. The version probe
is a provider interaction, but is distinct from model execution. Early-refusal tests assert neither
occurs. Changed-state tests deliberately mutate during probing and assert the later boundary.

Links below name the owning source and executable test evidence. Tests establish mechanical
behavior, not model comprehension, technical correctness of authored findings or assurance acceptance.

| V1 criterion | Production owner | Exact evidence |
| --- | --- | --- |
| Contract arrives in the preceding completed response, before each next dispatch | [CLI responses](../src/AILedger.Cli/Routing/CliCommandExecutor.cs), [launcher](../src/AILedger.Cli/Providers/ProviderLauncher.cs) | [IntegratedValidationTests.ExternalResearchRevisionAndEveryLaunchRetainDeliveredContracts](../tests/AILedger.Tests/ContractDelivery/IntegratedValidationTests.cs), both provider assignments: initial → recon → external researcher → refreshed recon → planning → worker → verifier → bound reviewer. Trace distinguishes response, probe, `run.started` and adapter execution; no stage waivers. [ContractDeliveryTests.CompletedResponsesDeliverBeforeEveryNextDispatchAndIsolateReview](../tests/AILedger.Tests/ContractDelivery/ContractDeliveryTests.cs) retains the ordinary internal-only branch. |
| Relevant revisions/stages refresh version, current references and action | [NextActionContracts](../src/AILedger.Core/ContextBriefing/Delivery/NextActionContracts.cs) | Integrated test files recon supersession after actual claim resolution and PLAN1→PLAN2 through CLI; checks committed response versions and exact latest references. `ArtifactRevisionAndStageResponsesRefreshAndPreserveFields` covers revision and backward stage response. |
| Required authoring shapes and applicable exact bindings survive delivery | [planning definitions](../src/AILedger.Core/ContextBriefing/Delivery/NextActionContracts.Planning.cs), [assurance definitions](../src/AILedger.Core/ContextBriefing/Delivery/NextActionContracts.Assurance.cs) | Integrated test asserts shared plan/verifier shapes before authorship, candidate, single exact member/working provenance and verifier pair across return and Review transition. [ContractBoundaryTests.MemberBindingsRemainExactAndStalePairStillRefusesAfterDelivery](../tests/AILedger.Tests/ContractDelivery/ContractBoundaryTests.cs) retains all four member bindings. |
| Malformed plans fail at filing; verifier shares definition; history stays readable | [OrchestrationPlanDocuments](../src/AILedger.Core/Artifacts/Logic/OrchestrationPlanDocuments.cs), ArtifactRules/VerifierOutputRules | Integrated plan filing checks named BAD refusal and byte-identical event log, then files and revises a real attention row and disposes it in verifier output. [OrchestrationPlanFilingTests](../tests/AILedger.Tests/Artifacts/Plans/OrchestrationPlanFilingTests.cs): `AuthoringShapeFilesAndVerifierConsumesItsAttentionIds`, `MalformedNewOrRevisedPlanIsRefusedBeforeAnyArtifactIsCommitted`, `HistoricalMalformedPlanReplaysAndSelectionDiagnosesSupportedRepair`; vocabulary and dependent-claim cases remain required. |
| Required brief overflow precedes probe/start/execution | [prospective assembly](../src/AILedger.Core/ContextBriefing/Logic/ContextAssembler.cs), [budget](../src/AILedger.Cli/ContextBriefing/ContextManifestBudget.cs) | [LaunchPreparationTests.RequiredOverflowRefusesBeforeProbeAndStartWithoutDroppingRequiredContent](../tests/AILedger.Tests/ContractDelivery/LaunchPreparationTests.cs), both providers: full unchanged event history, zero probes/requests, then valid content-preserving launch. |
| Invalid reviewer pair/setup/profile and assurance grants fail early | [AssuranceHost](../src/AILedger.Cli/Assurance/AssuranceHost.cs), ProviderLaunchPreflight/RunAdmission | `LaunchPreparationTests.ReviewerBindingsAndProfileFailBeforeProbeAndStart` and `InvalidAssuranceSetupNeverStartsRunOrAdapter`: candidate/member/provenance/session/profile, missing/revoked/expired/wrong-role config, dependency grants, protected/symlinked inputs, missing files and invalid stores. Both providers; no run, probe or request. |
| Valid launches keep required content, role filtering, observed versions and exact delivered hash | ContextAssembler, ProviderRunRecorder | Integrated test asserts each of seven actual requests has the active state's version and subject role; coordinator packet version matches that manifest; non-coordinators get none; every completed run hash equals SHA-256 of its exact StandardInput. `LaunchPreparationTests.ConfiguredLaunchDeliversDiscoveryAndRechecksAuthorityAndInputsAtToolUse` checks actual area/check IDs. [BundleAssuranceTests.DeliveredMembershipIsRunTruthNotContextDedup](../tests/AILedger.Tests/Assurance/BundleAssuranceTests.cs) covers member union. |
| Guidance is not authority or a reservation; mutable inputs rechecked | RunAdmission, [AssuranceConfiguration](../src/AILedger.Providers/Assurance/AssuranceConfiguration.cs), AssuranceService | `LaunchPreparationTests.ChangedWorkingProvenanceIsRecheckedByDurableAdmission`, `ConfigurationChangedDuringVersionProbeFailsAtUseWithExistingCleanup`, `RequiredBriefThatGrowsAfterPreparationIsRebuiltAndRefusedAtUse`, configured-launch live MCP mutation/revocation case; `ContractDeliveryTests.FileContextCarriesContractAndLaterAuthorityStillRefusesLaunch`; [InspectionAssuranceTests.DependencyChangeBetweenReadinessAndExecutionCannotCompleteStaleWork](../tests/AILedger.Tests/Inspection/InspectionAssuranceTests.cs). |
| Completed/failed/cancelled process, blocked/partial self-report, missing output and historical unknown remain distinct | [ContractObservations](../src/AILedger.Core/ContextBriefing/Delivery/ContractObservations.cs), producer-outcome rules | Integrated `CancelledReturnRetainsPartialEvidenceAndClosesRun`: both providers, returned cancellation and thrown cancellation, durable evidence and one ending. `ContractDeliveryTests.ResearchReturnsBlockedPartialFindingsWithoutPromotingTermination`, `WorkerSelfReportLeavesRepairBranchAndAcceptanceUnknown`, `MissingVerifierOutputPreservesProviderCompletedAndRecordedFailed`, `UndeclaredResearchReturnIsUnknownDespiteBlockedText`; [ProducerOutcomeTests.MissingHistoricalOutcomeRemainsUnknownAndNewEventReplays](../tests/AILedger.Tests/ContractDelivery/ProducerOutcomeTests.cs). |
| Refusal journals, cleanup and existing response formats preserved | RefusalJournal, ProviderRunRecorder, CliCommandExecutor | [RefusalJournalLaunchTests](../tests/AILedger.Tests/Cli/RefusalJournalLaunchTests.cs), [RunManifestLaunchTests](../tests/AILedger.Tests/Cli/RunManifestLaunchTests.cs), [BundleFailureTests.PostStartFailureRecoversEveryMember](../tests/AILedger.Tests/Assurance/BundleFailureTests.cs); original JSON fields/exit semantics retained, thrown cancellation emits no invented provider result. |
| Blind reviewer receives no task requirements, plans, coordinator packet or verifier narrative through supplied channels | ContextRolePolicy, bound ContextAssembler, [inspection selection](../src/AILedger.Storage/Inspection/FileGovernedTaskService.Retrieval.cs), AssuranceHost | Integrated test checks serialized entire AgentLaunchRequest and real supplied MCP index/direct retrieval for all five excluded artifact types. `BundleAssuranceTests.R4_ReviewerInputHasNoDirectOrIndirectNarrative` also inspects adapter process input, and `ActiveBoundReviewerContextRefreshRefusesBeforeDelivery` prevents unbound refresh. `InspectionAssuranceTests.BoundReviewerCannotEscapeIsolationAndAssuranceReadinessNeverPretendsPhysicalInspection` checks both selections. Configured requirements-aware assurance is refused, rather than opening an alternative channel. |
| Claim/consultation, independence and replay requirements preserved | InternalReconRules, StagePrerequisiteRules, AssuranceRules, TaskTransitionValidator | Integrated research resolves a supported claim as operator and refreshes lead-owned recon/consultation before Design. [InternalReconGateTests.R1_DesignRequiresBothArmsForExternalClaims](../tests/AILedger.Tests/Stages/InternalReconGateTests.cs) retains negative arms. Existing BundleAssuranceTests independence cases and [BundleLifecycleTests.R5_OldAndRevertedCoverageReplayAsBaseline](../tests/AILedger.Tests/Assurance/BundleLifecycleTests.cs) remain unchanged. |
| Assurance evidence, explicit acceptance and governed completion stay separate | AssuranceService and WorkItemVerificationRules | [AssuranceOperationTests.IndependentEvidenceSupportsOnlyExplicitAuthorizedAcceptance](../tests/AILedger.Tests/HandoffAssurance/AssuranceOperationTests.cs) proves complete independent reports remain unaccepted until authorized acceptance; partial/unknown/failed/contradictory variants refuse. Integrated governed flow leaves work uncompleted and technical acceptance unknown after reviewer return; `BundleAssuranceTests.RelatedBundleFilesClosesAndCompletesEveryMember` separately exercises governed completion. No task-13 acceptance is inferred from these governed outputs. |
| Packet costs bounded and coverage explicit | [ContractPacketBudget](../src/AILedger.Core/ContextBriefing/Delivery/ContractPacketBudget.cs) | Current serialized measurements below; ContractBoundaryTests covers 129 claims, four members and explicit incomplete 5,000-member fallback. Required shapes remain; fallback does not pretend to retain usable exact bindings. |

Actual adapter process construction is additionally covered by
[ReviewerAmbientInputTests.OnlyTypedReviewerSuppressesAmbientInstructions](../tests/AILedger.Tests/Providers/ReviewerAmbientInputTests.cs)
for Claude and Codex, including ambient instruction controls, arguments and environment; those
regressions run in the full suite. The supplied MCP endpoint is tested without calling real models.

The fresh candidate-bound path is the isolation claim. Legacy artifact filtering does not establish
complete environmental isolation: [item 26](../Backlog/the-reviewers-isolation-is-artifact-deep-only.md)
describes older narrative channels. Ambient filesystem access and external client authentication are
not made universally blind by item 64. The ordinary-path fixtures exercise supplied request/MCP
content and preserve the existing isolation gates; they do not prove that a model obeys its guidance.

## Defects and failure investigations

No production defect was established by the integrated, focused and full-suite checks; no production code was changed in Phase 4. Added coverage closes the positive
external-research/recon-refresh, complete per-launch version/hash, denied artifact retrieval and
cancelled-return gaps. Existing focused integration fixtures are reused for other criteria.

Preserved development failures (all outputs outside the checkout):

1. `/tmp/ailedger-phase4-integrated-first.log`: initial test scaffolding failed compilation on the
   evidence-command name, producer-outcome constructor and history API. Corrected to existing APIs.
2. `/tmp/ailedger-phase4-integrated-second.log` and matching TRX directory: build succeeded;
   VSTest aborted before any test because sandbox IPC socket binding was denied. Subsequent tests
   use the supported test-host access. This is not a passing test run.
3. `/tmp/ailedger-phase4-integrated-third.log` and TRX directory: 4 passed, 2 failed. Claim resolution
   invalidated the recon claim hash. The fixture had attempted Design without refreshing recon.
   Added the prescribed fresh lead run, recon consultation and superseding artifact. No stage/hash
   gate was relaxed; the extra cognition is a legitimate prerequisite, not an avoidable-run saving.
4. `/tmp/ailedger-phase4-integrated-fourth.log` and TRX directory: 4 passed, 2 failed. The direct
   retrieval probe omitted required `expected_sha256`, so it reached schema refusal rather than
   visibility enforcement. Added a well-formed guessed reference and asserted exact `not_visible`.
   No production schema or isolation assertion was weakened.

Phase 2's intermittent diagnostic-count failures remain a historical caveat in its own record.
A later passing suite cannot establish their cause or claim them fixed. No production deadline,
assertion or runner has been weakened in this phase.

## Validation results and size measurements

Focused validation: **278 passed, 0 failed, 0 skipped**, including six new integrated cases.
TRX and logs: `/tmp/ailedger-phase4-focused/` and `/tmp/ailedger-phase4-focused.log`.
The full sequential suite passed **2,398 main + 99 memory tests**, with **zero failures, skips or
runner errors**. Both solution and test-runner builds had **zero warnings/errors**. The full suite
ran once, after focused validation, and needed no retry. Launch log: `/tmp/ailedger-phase4-full.log`.
Full build and suite logs:
`/private/var/folders/v3/pncbbv7x45jgp4bnd81fjnl80000gn/T/ailedger-tests.FQbAvl/`.
The source/cognitive inputs stayed fixed throughout this run. Subsequent edits only finalized this
record and backlog status. All 12 cognitive digests, document links and `git diff --check` passed.
The initial file-digest inventory confirms unrelated pre-existing files, including the two lesson
files, retain their original bytes; the only previously existing files changed in this session are
the delivery fixture and the two requested backlog documents.
Phase 3's 2,392 main + 99 memory baseline is not substituted for this validation.

```sh
dotnet test tests/AILedger.Tests/AILedger.Tests.csproj --artifacts-path /tmp/ailedger-phase4-build -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false --no-restore --filter 'FullyQualifiedName~ContractDelivery|FullyQualifiedName~ContextManifestBudgetTests|FullyQualifiedName~ContextBudgetDeliveryTests|FullyQualifiedName~BundleAssuranceTests|FullyQualifiedName~HandoffAssurance|FullyQualifiedName~OrchestrationPlanFilingTests|FullyQualifiedName~ProviderFindingsConfigurationTests|FullyQualifiedName~InspectionAssuranceTests|FullyQualifiedName~RefusalJournalLaunchTests|FullyQualifiedName~RunManifestLaunchTests|FullyQualifiedName~InternalReconGateTests' --logger trx --results-directory /tmp/ailedger-phase4-focused
sh scripts/test-governed.sh all
```

Measurements come from `Encoding.UTF8.GetByteCount(packet.GetRawText())` on actual indented CLI
JSON responses in the focused TRX. This includes the packet field's serialized whitespace/escaping;
it excludes the surrounding response, other manifest content, tool definitions and model output.
Token values are **ceil(bytes / 4) estimates**, not tokenizer measurements. Both provider assignments
produced the same sizes. Phase 3's added preparation guidance changes sizes from the older Phase 2
measurements; the earlier below-8-KiB result is not a bound for every current response.

| Current response/fixture | UTF-8 bytes | Estimated tokens |
| --- | ---: | ---: |
| Ordinary initial coordinator context | 7,766 | 1,942 |
| Ordinary worker with declaration | 7,790 | 1,948 |
| Ordinary single-member verifier return | 7,719 | 1,930 |
| External-flow recon return | 8,180 | 2,045 |
| External-flow researcher return | 8,375 | 2,094 |
| Refreshed external recon return | 8,020 | 2,005 |
| Planning return after actual attention-table revision | 9,578 | 2,395 |
| External-flow worker without declaration | 7,510 | 1,878 |
| External-flow verifier with attention disposition | 7,755 | 1,939 |
| External-flow reviewer return | 7,553 | 1,889 |
| Larger admitted fixture: four exact assurance members | 8,041 | 2,011 |
| Larger initial context: 129 claims | 7,770 | 1,943 |
| Defensive 5,000-member incomplete fallback | 6,959 | 1,740 |

Ordinary/larger covered fixtures retain `observed` delivery, inline critical shapes and exact
applicable bindings under the 64-KiB packet ceiling. The synthetic 5,000-member fallback is not an
admitted launch and explicitly drops oversize binding coverage with `incomplete`; its smaller size
is not a successful compression claim. These numbers measure payload overhead only. No time,
cost, comprehension, avoided refusal or avoided model-run benefit follows from fixtures.

## Authorized installation (2026-09-30)

The user subsequently requested installation so the validated work could be used. Installed
**AILedger.Cli 2.0.186-dirty**, reporting `2.0.186-dirty  from 1ba8aa0`, from the unchanged pinned
source inventory below. `dirty` accurately identifies uncommitted/untracked work; HEAD alone is
not the source identity. No commit, merge or push was performed.

The Release package was built outside the checkout at
`/tmp/ailedger-phase4-install/package/AILedger.Cli.2.0.186-dirty.nupkg`.
Package SHA-256: `a88d71c35322dce65d3ffb84dba818a20a6a6bf1397ed57f3b94936eebbedf21`.
Installed CLI DLL SHA-256: `178ea81986cd718cba0bad561b64a1dd85cf03ec99ed2251c6abfa84703f813b`.
The installed cached package and all **11** packaged tool files match their expected hashes.
See the [installation receipt](contract-delivery-installation.json) for every hash, command, log,
smoke result and rollback location. The Release package differs from the earlier Debug validation
binary by configuration/build identity; no source or cognitive inputs changed.

Both the candidate Release executable and the installed `ailedger` passed fresh disposable CLI
smokes: initial coordinator contract/inline plan shape, exact context/stage observed versions,
and required brief overflow before run start or the marker provider's version probe/execution.
Event history stayed byte-identical on refusal. These checks use explicit temporary ledger and
lesson roots, no real provider. Transcripts are under `/tmp/ailedger-phase4-install/`.
No full-suite rerun was warranted solely for packaging; the prior full suite and these Release/
installed boundary checks are separate evidence. Packing succeeded; its log retains the NuGet
missing-package-readme notice.

Installed with:

```sh
dotnet tool update --global AILedger.Cli --version 2.0.186-dirty --source /tmp/ailedger-phase4-install/package --no-http-cache
```

The previous **2.0.185** package was retained at
`/Users/user/.local/share/ailedger/rollback/ailedger.cli.2.0.185.nupkg`. If an authorized rollback is
needed, use the exact `rollback_command` array in the receipt. No uninstall-first gap was needed.

**Skills:** the four repository role skills already updated in phases 2–3 (workflow coordinator,
task orchestrator, technical researcher and contract-driven execution) are supplied from the
repository's `cognitive/` layer. All **12** manifest digests were reverified and the installed smoke
loaded that layer. Existing Codex and Claude `ai-kernel` bootstrap skills already delegate role
procedures to the served manifest and preserve the assurance/isolation boundary. Their substantive
instructions are aligned; provider naming/front-matter differences do not require an update.
**No global skill update is needed for this installation, and none was made.** Use the existing
explicit `--cognitive-root` when outside this repository; the tool package does not bundle that layer.

## Proposed real-task observation

Uri owns the real-task observation and final judgment, as clarified during this session.
The plan below is a handoff for that observation, not a request to let this development session
perform it. Installation is now authorized and complete; a billable observed task remains outside
this session's authorization. No live task
or assurance store has been opened, and a scripted fixture is not observed real work.

### Candidate and installation

The candidate is the preserved dirty working tree over commit
`1ba8aa0147d1bb61a80c56f249f839ff3b91cae1`, including the untracked phases 1–3 implementation.
HEAD alone is not the candidate. The immutable build/test/cognitive input inventory is
`/tmp/ailedger-phase4-candidate/source-sha256.json` (845 files), SHA-256
`2162b3c3d01a90024643ffaaee1ca74b8793588b4f6b0ca45ae4c78df9732dff`.
Its source archive is `/tmp/ailedger-phase4-candidate/source.tar.gz`, SHA-256
`47db944d1518663fff2bdbbc911adaa481587bcd134b65659c7e8b211f4ecf09`.
The inventory covers source, tests, tools, scripts, cognitive content, root build configuration and
embedded JSON schemas; it excludes live ledgers, lessons, build outputs and prose documentation.
Use the inventoried source bytes, not a later moving checkout, for authorized packaging/installation.
The full-suite build's CLI output inventory is
`/tmp/ailedger-phase4-candidate/tested-build-sha256.json`, SHA-256
`b122d335251bfee6e9afa23cac28fe22b9075ef96fae6743ad7d9c95dc3a67a9`.
The tested `AILedger.Cli.dll` SHA-256 is
`f4f2d8a29089cecce9c650a1ebc532ab6b3379d91ca690e9b1abf6cb88785342`.
This pins the development binary, not a global installation or a separately tested Release package.
This is a source candidate, not an installation receipt. Revalidate its inventory before packaging;
any changed build/cognitive input requires a new identity and appropriate validation.
The installation above now supplies this source candidate. Do not rebuild from a later moving
checkout and treat it as the same validated package. Global skills remain unchanged; the trial
supplies the pinned repository `cognitive` directory explicitly.

### Task, roles and bounds to approve

Uri must select a real medium task: repository/ref, desired change, success criteria, non-goals,
external systems and available test environment. Do not choose arbitrary work merely to obtain a
trial. Select a task whose **complete material** candidate/requirements/source/dependency closure
fits task 13: at most eight areas, 32 paths and 64 KiB per area, and bounded configured checks.
A medium repository task is not automatically within those bounds. An omitted build dependency or
unavailable environment is a stop, not permission to shrink the declaration dishonestly.

Proposed assignment: Codex for lead recon/planning and implementation, Claude for external
research when needed and independent governed verification, Codex in a fresh isolated CodeReviewer
session for blind code review. Record exact available model/version selections during authorized
preparation; do not infer them from development stub names. The coordinator and accepting authority
are Uri/current authorized operator; preserve separate actor identities and role grants.

Expected model episodes: five for internal-only work (recon, plan, worker, verifier, blind reviewer);
up to seven if external research and a necessary recon refresh are needed. A separate authorized
requirements-aware task-13 review adds one independent episode, or can be performed by a genuinely
independent authenticated human. Reserve at most four additional episodes for **one** actual repair
(worker, verifier, blind reviewer and independent task-13 reviewer). Proposed total ceiling:
**12 model episodes, 15 minutes each, $25 total incremental provider spend** including any separately
billed coordinator usage. These are proposed bounds, not prices or measured savings.

Before dispatch, demonstrate an actual executor/account budget control that can enforce the dollar
ceiling including in-flight usage, or agree a different enforceable provider bound. The current
launcher has timeouts, not a hard aggregate dollar gate; missing usage is unknown. Do not claim
post-run accounting is a hard cap. If no suitable budget control exists, paid observation remains
blocked until Uri chooses an explicit alternative spending arrangement. No retries beyond the
episode/time/spend limits are implicit.

### Blind review and complete assurance

The current requirements-aware task-13 profile **does not permit the proposed whole flow in the
single governed blind reviewer session**. That launch must still refuse before probe/run start.
Merely authorizing installation or dollars does not remove this technical boundary.

A separately approved way to assess the complete outcome uses the already supported external
`assurance serve` host for a **different, requirements-aware** independent reviewer principal/session,
in addition to the normal blind CodeReviewer. This does not make the blind profile compatible.
It needs explicit approval of the extra recipient, identity, context and spend; no such principal,
profile or grant is created here. If this separation is not appropriate for the selected task, stop
at the boundary and record incomplete assurance; any compatible-profile development is separate work.

The governed verifier can use its actual configured task-13 verification grant and must still file
VerifierOutput. The blind reviewer files CodeReviewOutput without task-13 requirement input. The
separate requirements-aware reviewer records its own independent task-13 evidence. Uri, through an
actual independently authorized acceptance principal, explicitly accepts only applicable complete
independent reports with findings disposition. Then assess existing governed completion separately.
Neither legacy completion nor a successful process substitutes for task-13 acceptance.

### Access and environment preparation

Before observation dispatch, bind the selected task to exact repository and
base/candidate references, writable work scopes and least required external access. Identify actual
credentials/owners and test runtime; confirm network/sandbox/service availability, pinned executable
hashes/arguments and meaningful check IDs. For Elastic/Jest or other external prerequisites, require
actual access/executability evidence rather than assuming a profile change repairs the environment.

Prepare reviewable, operator-owned authority JSON naming exact principals, independent sessions,
implementer attribution, expiry, selected areas, complete transitive inputs and configured checks.
Keep authority/store outside all provider write grants and all supplied inputs outside the protected
ledger. Verify the configured tool subsets and live authorization; do not widen grants to fit.
Task 13's check limits (1–8 checks/batch, eight batches/case, at most 300 seconds per batch) must fit
the selected validation plan. No arbitrary shell authority is requested through a model tool.

### Baseline, measurements and outcome assessment

Use the closed Glue task's recorded **21 runs, 76 agent-minutes and 15 journaled refusals** only as
historical context from item 64, not a matched causal baseline. It differs in task complexity and
closed without complete task-13 acceptance; the estimated eight avoidable runs/hours are not measured
savings. No paid control rerun is proposed. A descriptive one-task observation can measure friction
and usefulness; it cannot establish a reliable treatment effect.

Retain exact CLI responses, dispatch request timestamps, probe/model-start timestamps where observed,
run endings, supplied brief hashes/versions, contract JSON, refusal journals, revisions, consultation
evidence, task-13 policies/snapshots/read/check/report/acceptance receipts and final applicability.
Count every coordinator exchange, CLI/tool formulation, early **and** late refusal, repeated unmet
requirement, unusable dispatch, manual intervention, model episode and repair. Measure elapsed time
from each completed return to the next valid dispatch, serialized packet bytes, available tokenizer
counts/usage and billed cost. Keep estimates, absent usage, coordinator work and process time distinct.

For each recon/research/verifier/reviewer finding, assess whether it was substantive, correct,
actionable, retained across handoffs and addressed by evidence. Assess criterion coverage, actual
test relevance, unchanged/changed candidate identities, independence, unresolved disagreements and
explicit acceptance separately from coordination counts. Uri assesses the actual outcome. Faster
handoffs with weaker findings or incomplete assurance do not demonstrate a successful improvement.

### Stop conditions and recovery

Stop before paid dispatch for stale candidate/build identity, incomplete inputs, missing environment,
unapproved principal/scope, incompatible blind profile or unenforceable/unapproved spend bounds.
Stop further episodes at limits, repeated unexplained refusal, missing required contract/shape/binding,
isolation leakage, changed authority, output loss or unresolved substantive defect. Preserve evidence;
do not disable gates, skip assurance or present an earlier receipt as current acceptance.

Use existing authorized repair/replanning and consultation paths. Changed work/candidate/requirements
needs fresh applicable verification/review. For uncertain tool writes retain the original request
body/key/session and inspect/retry it; do not create a new key to conceal uncertainty. For interrupted
host checks, stop the remaining process and perform the existing authorized reconciliation before
requesting a new check. Follow [checkpoint-F guidance](handoff-assurance-v1/trial.md) where applicable;
do not add an artificial billable interruption experiment to this item-64 observation. A further
repair or spend extension needs separate approval after the retained evidence is reviewed.

### Unresolved prerequisites

Exact missing inputs: Uri's selected medium task and environment/access details;
approved provider/model/spend controls; actual trusted assurance principals,
policy/store and complete input closure; and an explicit decision authorizing a separate independent
requirements-aware reviewer or otherwise addressing the incompatible profile in separate scope.
Engineering fixtures cannot supply these decisions or certify the real task's assurance suitability.
Observed use, checkpoint-F/client acceptance and measured benefit remain pending independently.
