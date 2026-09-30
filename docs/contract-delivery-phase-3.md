# Item 64: Phase 3 early launch checks

**Status (2026-09-30): Phase 3 complete; focused and full-suite validation passed.**

Phase 3 implements the remaining ordinary-launch checks from item 64. Development follows the
user's explicit direct-session exception: disposable application/CLI fixtures and stub providers,
without a governed development task, live ledger/lesson writes, agents, provider episodes,
installation, merge or push. Phases 1–2 and unrelated working-tree changes are preserved.

## Launch boundaries

The shared Claude/Codex ordinary fresh-launch host now performs these checks before adapter
resolution, the provider **version probe**, `run.started`, relay creation and `RunAsync`:

| Check | Shared implementation and correction |
| --- | --- |
| Required role-filtered brief | Load actual cognitive artifacts, select actual task/work artifacts, filter by subject role and apply the existing serialized UTF-8 budget. Required records are never dropped. Overflow retains the existing narrower-work/input-reduction/explicit-budget diagnostic. |
| Reviewer binding | Reuse full `ProviderLaunchPreflight` / `RunAdmission` after the existing authority/scope ordering. This includes legacy current-verifier requirements and bound exact members/candidate/working provenance, applicable current verifier output, independent working/verifying providers and fresh sessions. |
| Configured authority/store | Require both options and the existing inspection service, absolute protected paths, readable bounded policy JSON, valid policy, an actual enabled/unexpired principal and compatible governed role. A store may be absent, but cannot name an existing file or descend through one. |
| Assurance inputs and grants | Reuse the principal's transitive configured area selection, actual resolved provider grants, canonical protected-ledger exclusion and the execution snapshot reader's path, symlink, UTF-8, byte-limit and dependency checks. Check dependency grants before reading inputs. |
| Blind review compatibility | The current task-13 policy requires requirement paths in every area. It is incompatible with governed CodeReviewer context, whether legacy or bound. Refuse with the configured area/check IDs and a correction requiring an independently authorized compatible context. No requirements, plan, coordinator contract or verifier narrative is supplied through assurance as a workaround. |

The existing Docker/environment check remains early. The existing resume path does not acquire the
new prospective preparation. Batch preview, reconnect and other ingress paths are not expanded.
Launches without assurance options retain compatibility behavior; absence of configuration is not
task-13 acceptance and does not silently satisfy the default assurance policy.

## Preparation and revalidation

`ContextAssembler.BuildForLaunch` is a read-only seam using the same `Assemble` as `BuildForRun`,
after actual command admission predicates. It projects only work activation through the shared work
status projector. It creates no fictitious run, credential, event, receipt or store write. Explicit
launch selection preserves disjoint bound and legacy reviews without weakening the public
unbound-refresh guard.

The existing `ContextManifestBudget` uses the production serializer, newline accounting and whole
background-lesson omission rules. Prospective sizing reserves the maximum decimal version width
and timestamp precision, including the coordinator packet's observed version. The reserve can
conservatively refuse a brief a few metadata bytes below the limit; it does not replace required
content with an approximation. The sizing copy is never delivered as an observed state.

After the version probe, `ExecuteAsync(StartRunCommand)` still authorizes and admits against current
state under the task lock. Changed provenance/authority can therefore refuse without starting the
requested run. Once admitted, the host still rebuilds from current state/files and budgets again.
The delivered manifest carries its actual observed task version and assembly time. The completion
record hashes the exact serialized bytes passed in `AgentLaunchRequest.StandardInput`, only when
the complete request is ready for the adapter.

`AssuranceHost.PrepareAsync` reads configuration without opening a service/session or canonical
store. `AssuranceConfiguration` shares principal, enabled-grant, area and input-path definitions
with the active service. It reads bounded inputs through `AssuranceSnapshotReader`; no check command
executes during preparation. Protected paths and input grants remain enforced at use.

After run admission, live inspection authorization precedes rereading assurance input files. The host
opens a real run-bound session, builds its discovery artifact and revalidates current configuration
immediately before creating the relay/request. Existing per-operation live inspection, governed role,
policy identity, revocation/expiry, mutable input capture, expected bindings and commit rechecks remain
authoritative. Preparation does not reserve state or make mutable paths immutable.

Compatible launches include a `configured-assurance-discovery` Rules artifact in the existing manifest:
configured area IDs and their check IDs, without requirement/source content or authority/store paths.
It passes through actual assembly and budget enforcement. Tools and grants are not expanded.
The preceding coordinator packet still reports host-dependent checks **unknown**, since those checks
have not run for its next dispatch. Delivery wording and repository coordinator guidance now explain
the preparation boundary; the updated repository cognitive digest is maintained without installation.

## Compatibility and limits

No event schema, provider-result field, command-result shape, launch status, session protocol or
replay predicate is added. The work activation extraction is behavior-preserving for replay.
`RunAdmission` remains command-time authority; existing assurance predicates used by replay are
unchanged. The new checks are confined to the ordinary launch host and prospective context path.
Bound assurance, historical unbound histories, public context filtering and inspection keep their
existing boundaries.

Preparation refusals start no run. Governance refusals, including configured assurance authorization
and compatibility failures, use the existing launch refusal journal. CLI argument/budget errors keep
usage-error handling; malformed policy JSON is diagnosed as a usage error. No provider result is
invented. A later changed input or failure after run start follows existing failed/cancelled cleanup,
manifest-delivery accounting and Phase 2 coordinator return behavior.

This is not a launch transaction, universal preflight, sandbox repair or assurance acceptance.
Store write availability, future filesystem/policy mutations, provider runtime support, executable
check success and technical acceptance remain execution facts. Preparation reads bounded declared
inputs; it does not prove their completeness, repository ref correctness or independent acceptance,
and does not map an asserted governed candidate hash to a real repository diff automatically.
Current requirements-aware task-13 review still needs a separately authorized compatible host context;
blind governed review is not made compatible by this phase.

## Validation

Final focused checks passed **251/251**, followed by **10/10** launch-boundary regressions, with
zero failures or skips. The full sequential suite passed **2,392 main + 99 memory tests**, with zero
failures, skips or runner errors. Solution and runner builds both passed with zero warnings/errors.
Full build outputs and logs are under
`/private/var/folders/v3/pncbbv7x45jgp4bnd81fjnl80000gn/T/ailedger-tests.gs1JRm`;
the launch log is `/tmp/ailedger-phase3-full.log`. The full suite ran once after focused validation;
no production deadline or assertion was relaxed. `git diff --check`, document links and all 12
cognitive digests also passed. Test artifacts and logs are outside the checkout.
Tests use production CLI/application/store paths with disposable stores, stub Claude/Codex
adapters and the real local MCP relay, not real providers or billable episodes.

```sh
dotnet test tests/AILedger.Tests/AILedger.Tests.csproj --artifacts-path /tmp/ailedger-phase3-build -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false --filter 'FullyQualifiedName~LaunchPreparationTests|FullyQualifiedName~ContextManifestBudgetTests|FullyQualifiedName~BundleAssuranceTests|FullyQualifiedName~ContractDelivery|FullyQualifiedName~HandoffAssurance|FullyQualifiedName~OrchestrationPlanFilingTests|FullyQualifiedName~ProviderFindingsConfigurationTests' --logger trx --results-directory /tmp/ailedger-phase3-focused-final
dotnet test tests/AILedger.Tests/AILedger.Tests.csproj --artifacts-path /tmp/ailedger-phase3-build -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false --filter 'FullyQualifiedName~ContextBudgetDeliveryTests|FullyQualifiedName~RefusalJournalLaunchTests|FullyQualifiedName~RunManifestLaunchTests' --logger trx --results-directory /tmp/ailedger-phase3-launch-regressions
sh scripts/test-governed.sh all
```

The tests assert unchanged complete serialized event histories and zero version probes/adapter
requests on initial refusal. They also cover valid required content/filtering and exact delivered
hashes, configured discovery IDs, protected path aliases, missing/revoked/expired/wrong-role setup,
grant/dependency failures, reviewer pair/candidate/member/provenance/session/profile failures,
changed setup during a version probe, live MCP revocation and candidate revalidation, and required
brief growth after preparation. Existing tests cover isolation, disjoint reviews, replay, refusal
ordering, response compatibility, output requirements and termination recovery.

The first focused attempt passed 19/155 and failed 136: 30 new assertions compared nested list
references rather than complete serialized histories; 106 setup failures followed a repository skill
edit made during the run before its digest was refreshed. This was development execution error.
The assertions now compare every serialized event field; the cognitive inputs remain fixed during
tests. The next run passed 153/155; two stale-review fixtures skipped the required Verification stage
on their route back to Review. The fixture sequence was corrected without changing production
stage rules, assertions or deadlines. Logs/TRX remain under `/tmp/ailedger-phase3-focused*`.
The two older launch-boundary tests were updated for the intentional earlier refusal. Resume keeps
its original late overflow assertion, and a new late-capability-revocation variant retains exact
post-start journal-version and failed-run cleanup assertions.

Phase 4, installation and real-task observation remain pending. No measured savings or complete
item-64/universal-launch claim is made.
