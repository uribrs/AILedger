# Item 64: Phase 2 automatic contract delivery

**Status (2026-09-30): Phase 2 complete; focused and full-suite validation passed.**

Phase 2 implementation and validation record. The user explicitly authorized direct development,
disposable ledgers and stub providers, with no development-task governance, live ledger/lesson
mutation, installation, global skill edits, agent dispatch or billable provider episodes. Phase 1's
working-tree implementation is preserved. Phase 3's subsequent launch-preparation changes are
documented in [its own record](contract-delivery-phase-3.md); Phase 4 and real-task observation remain pending.

## Response boundaries

| Completed response | Delivery and observed version |
| --- | --- |
| `context build`, coordinator role | Additive `nextActionContract` in the existing manifest, including when `--output` writes the manifest and stdout remains the pathname. Uses the manifest's observed task version, before the existing context-built recording. Operator, PlanningLead and ImplementationLead only. |
| Ordinary `provider launch`, shared Claude/Codex launcher | Additive field in the existing provider-result JSON after durable termination is attempted and before control returns. Existing provider fields, status and exit behavior remain intact. The packet reads the recorded run separately, so provider `completed` can coexist honestly with recorded `Failed` for missing required output. |
| Ordinary launch throws after starting a run | After the existing cleanup records termination, emit a JSON object containing the contract; preserve the original exception/exit behavior. No invented provider result is emitted. Partial ledger findings survive. |
| Successful coordinator `artifact record`, including revisions | Additive field alongside existing taskId/version/stage/events, using committed state and the artifact's producer. Current revision references and shape observations are recomputed. Non-coordinator artifact responses remain filtered. |
| Successful coordinator `stage transition` | Additive field alongside unchanged mutation fields, using committed stage/version. The applicable branch set is recomputed. |

The context assembler supplies the shared application projection. `CliCommandExecutor` carries it
on existing JSON responses; `ProviderLauncher` calls the same delivery path for both providers.
No added prose corrupts JSON. Observation failures yield an explicit unknown contract rather than
reclassifying a committed mutation or changing the provider's outcome. A terminal-state read has a
separate two-second cancellation budget. A delivery failure is not an admission decision.

Resume, batch fan-out, reconnect, arbitrary launches skipping preceding coordinator context and
other ingress paths have no delivery-order claim. Their existing execution admission remains in
place. Structured artifact submission inside a verifier/reviewer session is still producer output;
it does not send coordinator material to that child. The outer launch return delivers the packet.
No preparation command, acknowledgment, receipt, scheduler or generalized step engine was added.

## Contract selection and policy boundaries

[NextActionContracts](../src/AILedger.Core/ContextBriefing/Delivery/NextActionContracts.cs) selects
small branch sets by current stage and selected/returned run. Research exposes recon/research and
planning authoring; Design/Scope expose planning and work preparation where a plan exists; worker
returns expose verification and repair/replanning; verifier returns expose isolated review and
repair, or verifier-output recovery when required output is missing. Selection is not acceptance.

Each action includes actor/subject roles, concrete requirements, an inline authoring shape, per-check
satisfied/missing/unknown status, supporting references, authority category and source identifiers.
Packets include observed version and available work, producer, candidate, exact assurance membership,
working provenance and verifier bindings. Unselected dispatch details remain unknown.

Definitions remain with their owners:

- Plan filing and delivery use Phase 1's `OrchestrationPlanDocuments` attention template/parser.
- Verifier delivery and admission share `VerifierOutputDocuments` column definitions. Existing
  verifier dispositions remain required; the parser and accepted vocabulary are unchanged.
- Stage observations invoke `StagePrerequisiteRules.EnsureSatisfied`; current artifact selection,
  latest working provenance and verifier currency use existing artifact/assurance/work predicates.
  These are observations of individual prerequisites, not a synthetic launch or mutation.
- Stage readiness's broader Ready staffing, Worker-only Verification entry, and Worker/Researcher
  work-completion predicates remain distinct. A stage prerequisite result does not promise the
  current stage has a legal immediate edge; repair guidance separately lists legal targets.
- The four broader plan sections remain workflow requirements, not executable filing schemas.
  Coordinator/orchestrator guidance now states this gap accurately.
- No physical candidate, repository ref, brief-size, assurance authority/store/grant, configured
  area/check identifier or review-profile check is claimed satisfied by a coordinator packet.
  Phase 3 now prepares the required brief and configured assurance setup before ordinary fresh
  launch, and refuses incompatible blind-review profiles. These checks have not run for the next
  dispatch when its preceding packet is delivered, so that packet still reports unknown.
  Task-13 store reports/checks/acceptance are not present in the governed
  task projection and remain explicitly unknown; this code does not open that store under a new
  authority or turn a governed Markdown artifact into task-13 acceptance.
- Requirements-aware assurance may conflict with a blind reviewer. No profile repair, authority
  expansion, skipped assurance or narrative widening is introduced.

Packets are built only for coordinator roles. Neither legacy nor bound reviewer manifests receive
this field. The supplied reviewer request and inspection paths retain existing role filtering;
coordinator skills and verifier narratives are not added. The declaration tool is enabled by the
ordinary launch host only for Worker/Researcher roles, and remains absent from reviewer grants.

## Affected implementation

- [Delivery projection and branch selection](../src/AILedger.Core/ContextBriefing/Delivery/NextActionContracts.cs),
  its planning/assurance partials, observations, models and packet budget live beside Context Briefing.
- [CLI response composition](../src/AILedger.Cli/Routing/CliCommandExecutor.cs) and the
  [shared launcher](../src/AILedger.Cli/Providers/ProviderLauncher.cs) attach additive response fields.
- [Producer declaration rules](../src/AILedger.Core/Runs/Outcomes/ProducerOutcomeRules.cs), the
  nullable run field, command/event discriminators, command authorization, replay validation and
  both state projectors supply the minimal durable declaration.
- [Supplied tool handler](../src/AILedger.Cli/Findings/FindingsMcpServer.ProducerOutcome.cs), trusted
  host/endpoint grants and [adapter guidance](../src/AILedger.Providers/Adapters/FindingsRecording.cs)
  expose that declaration through existing Claude/Codex sessions.
- [Shared verifier document definitions](../src/AILedger.Core/Artifacts/Logic/VerifierOutputDocuments.cs)
  retain admission semantics while exposing authoring columns and attributed disposition counts.
- [Response-path tests](../tests/AILedger.Tests/ContractDelivery/ContractDeliveryTests.cs), boundary
  fixtures, producer-outcome tests and adapter configuration cases cover the new behavior.
- Four repository role skills and their existing SHA-256 manifest entries were refreshed. All
  12 manifest digests validate; installed/global skills remain unchanged.

## Termination, output and outcome

`return` reports recorded run termination independently of output presence and attributed outcome.
Verifier/reviewer presence uses existing current/applicable producer and member predicates.
Verifier disposition counts come from admitted structured Markdown tables using the shared columns;
they are labeled authored dispositions, never an overall favorable verdict. Technical acceptance
remains `unknown`; a filed report, even from a Completed run, cannot establish it mechanically.

Worker/researcher output has no governed report artifact. Their new minimal declaration references
existing evidence that describes outputs or blockers:

```json
{"outcome":"blocked","output_evidence_ids":[],"blocker_evidence_ids":["existing-evidence-id"]}
```

The supplied `declare_producer_outcome` MCP tool accepts `reported-complete`, `blocked` or `partial`.
The trusted host supplies task, actor and run; the body cannot override them. Existing `AddEvidence`
capability, current matching Worker/Researcher role, producer ownership and an active run are required.
There are 1–16 unique evidence references in total, each at most 256 characters, already recorded by
the producer during that run's time interval. Blocked needs blocker evidence; reported-complete
needs output evidence and no blockers. Evidence ownership/time validation does not certify its truth
or that a cited file exists. Attribution is to the declaration's host-bound run.

One declaration is immutable per run. Identical retries revalidate current authority and return
without appending, including after termination; conflicting bodies are refused. A new declaration
cannot be added to a terminated run. There is no child run/work completion authority. This path uses
the existing task mutation lock and command/event validation, without a new general receipt protocol.
Unknown transport/storage outcomes instruct exact retry on the original binding.

The nullable `AgentRun.ProducerOutcome` and new `run.outcome-declared` event preserve old histories:
absence stays unknown. Only the new event/field receives new replay validation; old start/complete
histories gain no declaration requirement. A run cannot smuggle a declaration into `run.started`.
Canonical and memory historical projections both retain declarations. Existing run completion,
assurance pairing, independence and execution checks remain authoritative. The field is not an
acceptance input and does not change existing completion predicates.

No `BLOCKED` string matching, empty-diff heuristic, zero-write heuristic or process-status inference
is used. Failed runs can retain output/blocker evidence. Missing declarations and missing typed
semantic verdicts remain unknown. Declaration guidance is supplied through both adapters and the
affected repository role skills; no installed skill was changed.

## Packet bounds and measurements

The packet has a conservative serialized **64 KiB** ceiling. It includes only action requirements,
compact shapes and binding metadata, never full task/manual/plan/report text. Supporting artifact
and subject reference samples are limited to eight with total counts exposed; exact assurance member/provenance bindings are
retained on the normal path. Measurements use the actual indented CLI JSON field's UTF-8 bytes.
No `tiktoken` package was available in the local Python runtime, so tokens below are explicitly
**estimates: ceil(bytes / 4)**, not model-tokenizer counts.

| Production packet / fixture | UTF-8 bytes | Estimated tokens |
| --- | ---: | ---: |
| Initial coordinator response | 7,563 | 1,891 |
| Worker return with declaration | 7,587 | 1,897 |
| Verifier return with exact single-member binding | 7,409 | 1,853 |
| Four-member verifier return, exact bindings retained | 7,731 | 1,933 |
| Initial context with 129 claims | 7,567 | 1,892 |
| Defensive 5,000-member incomplete fallback | 6,756 | 1,689 |

The two provider variants produced identical normal-path sizes. All ordinary/larger covered
fixtures retained their authoring requirements and exact applicable binding metadata, below 8 KiB.
The ceiling is a defensive maximum, not a target or a model-context budget.

If exceptional bindings or diagnostics exceed the ceiling, delivery explicitly says `incomplete`,
retains essential authoring shapes and requirements, and omits the oversized binding/outcome with
an explicit coverage diagnostic. It does not silently truncate an exact candidate membership into
a usable binding or assert readiness. That selection has no complete bounded-delivery claim.
The 5,000-member defensive fixture exercises this limit; it is not a legal admitted launch fixture.
Ordinary and larger admitted fixtures must remain `observed` with their exact relevant bindings.

## Validation

Final focused run: **81 passed, 0 failed, 0 skipped**. This includes Phase 2 contracts/outcomes,
both adapter configurations, and Phase 1 plan filing/authority/revision regressions.

Final full run: **2,334 main + 99 memory tests passed, zero failures, skips or framework errors**,
using the repository's existing sequential xUnit runner, `sh scripts/test-governed.sh all`.
Both builds passed with zero warnings or errors. Build artifacts and full logs are under
`/private/var/folders/v3/pncbbv7x45jgp4bnd81fjnl80000gn/T/ailedger-tests.LgtqMQ`.
No implementation, assertion, diagnostic deadline or runner change was made between the earlier
assessment and this passing full run. Subsequent edits only update this record and backlog status.

An earlier serialized VSTest run passed **2,333/2,334 main tests and all 99 memory tests**,
with an artifact-submission application diagnostic count failure
(one row instead of two). Its commit/replay, authorization and tool-response assertions preceding
that assertion passed. Both provider variants passed in the subsequent isolated run (2/2).
Two inspection telemetry cases from the parallel full run also passed in isolation (2/2).
Builds passed; no tests were skipped in these completed runs. Outputs remain under
`/tmp/ailedger-phase2-build`, `/tmp/ailedger-phase2-focused-final`,
`/tmp/ailedger-phase2-full-serial`, `/tmp/ailedger-phase2-artifact-isolated` and
`/tmp/ailedger-phase2-inspection-isolated`.

```sh
dotnet test tests/AILedger.Tests/AILedger.Tests.csproj --artifacts-path /tmp/ailedger-phase2-build -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false --no-restore --filter 'FullyQualifiedName~ContractDelivery|FullyQualifiedName~ProviderFindingsConfigurationTests|FullyQualifiedName~OrchestrationPlanFilingTests|FullyQualifiedName~ArtifactAuthorityTests|FullyQualifiedName~ArtifactRecordTests' --logger trx --results-directory /tmp/ailedger-phase2-focused-final
dotnet test AILedger.sln --artifacts-path /tmp/ailedger-phase2-build -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false --no-restore --logger trx --results-directory /tmp/ailedger-phase2-full-serial -- xUnit.ParallelizeTestCollections=false
sh scripts/test-governed.sh all
```

Tests use production CLI/application/store responses and stub providers, including a real local MCP
relay process. They assert the response is complete before the next dispatch, the plan shape appears
before planning, version/ref refresh, honest blocked/partial/failed/missing output, session binding,
retry/revocation/replay rules, both adapter grants, reviewer manifest/request/inspection isolation,
and rejection of changed authority or stale verifier provenance after earlier guidance.

The initial VSTest attempt aborted before tests because the sandbox denied its socket bind. The
supported test-host access was then used. Early fixture failures exposed a missing consultation tag,
a forbidden self-authority edit, and process-wide stdin contention; fixtures were corrected to use
existing governance rules and the existing StandardInput test collection. On the final revision,
a parallel full run passed 2,332/2,334 main tests and all 99 memory tests: the two existing
`TrustedProviderRelaySupportsInspectionRetrievalAndReadinessWithRevocation` variants observed
28/32 diagnostic rows instead of 36. Their operation assertions passed. Both cases then passed
in isolation (2/2). The serialized full run failed
`ArtifactSubmissionProviderTests.TrustedRelayAdvertisesSubmitsReconnectsAndRevokesWithoutAgentIdentityFields(provider: "claude")`
at its `ObservedApplicationAttempts` assertion (expected 2, actual 1).

The subsequent `sh scripts/test-governed.sh all` attempt built successfully and passed all 99 memory
tests, but was canceled at the user's stop request before the main suite reported a total. It is
not a passing full run. Its logs are under
`/private/var/folders/v3/pncbbv7x45jgp4bnd81fjnl80000gn/T/ailedger-tests.ZilbLX`.
Despite its name, that existing script builds and invokes xUnit against disposable fixtures; it
does not open a governed development task or invoke a billable provider.

### Assessment of the earlier intermittent failures

The application diagnostic writer
[ArtifactSubmissionAttempt](../src/AILedger.Storage/Artifacts/ArtifactSubmissionAttempt.cs) and
the transport diagnostic writer
[FindingsTransportJournal](../src/AILedger.Cli/Findings/FindingsTransportAttempt.cs) each use a
250 ms best-effort write deadline and tolerate collection failure. Phase 2 does not change those
deadlines or failure handling; its transport change adds a separate producer-outcome category.
The failed tests exercise artifact submission and inspection, not that new category. Missing
diagnostic rows are consistent with the existing collection failure paths, but the saved results
do not establish the exact I/O exception or prove the failures unrelated to this change.
Passing isolated reruns alone did not discharge the full-suite completion gate. The subsequent
complete sequential run passed every test, including these cases. The earlier intermittency is
retained as a validation caveat; this result does not establish its cause or claim it was fixed.

The user first authorized a bounded assessment, then explicitly requested continuing directly to
completion. The final sequential run above completed that validation. No assertion or production
timeout was relaxed, and no speculative telemetry fix was added. Kernel checks exposed real fixture
mistakes earlier in this work; those corrections and the existing authorization, consultation,
assurance and isolation checks remain intact. `git diff --check`, document links and all 12 cognitive
manifest digests were also verified.

Phase 2 completion does not claim complete V1, early launch validation, real client observation,
installation or reduced time/cost. Phase 1's validation remains its historical baseline.
