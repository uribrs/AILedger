# Verifier 1 — Failed-checkpoint retention and operator-initiated retry (RECON)

Scope: read-only verification of `recon_report.md` and the four worker files against
`prompt_contract.md` Success Criteria, `orchestration_plan.md` Attention Items, `assumptions.md`
and `decisions.md`. Base ref `b5e7ba22`. 26 cited `path:line` references independently resolved
across all four workers (≥2 per worker, see §5.1).

Note on the brief: `prompt_contract.md` Success Criteria contains **9** bullets, not 10. All 9 are
covered below.

---

## 1. Success Criteria coverage

| # | Criterion (abbreviated) | Verdict | Evidence and independent check |
|---|---|---|---|
| SC1 | Every assumption A1–A15 disposed with an actor and a citation | **not met** | `recon_report.md` has no assumption-disposition section. Its `## 8` is "Open questions the decision must settle"; there is no `## 9`. Contract Output Format §8 (disposition table) and §9 (open questions **and contradictions**) are collapsed into one section and the table is absent. `assumptions.md` is still 100% `OPEN` (unmodified — `git status` shows the whole `ai/` tree untracked and no disposition text in the file). Workers *did* produce the evidence (W1 "Contradictions found against stated beliefs", W2 §Contradictions, W3 §Contradictions), but synthesis never converted it into the required table. Disposed below by this verifier instead. |
| SC2 | ISB lifecycle mapped end to end — write, retention, recovery selection, cleanup selection, resume entry, each cited | **met** | `recon_report.md` §1; `research/isb-checkpoint-lifecycle.md` §1–§7. Spot-resolved: write `AdapterExecutionContext.cs:992-996` + `CheckpointDbContext.cs:70-71` (jsonb ✓), delete-on-failure `ProcessEventCommandHandler.cs:814`→`:1228` (✓ exact), recovery `CheckpointRepository.cs:670-672` (✓), cleanup `:441-449` + `CheckpointCleanupJob.cs:49,:73` (✓), resume `ProcessEventCommandHandler.cs:1165` (✓). |
| SC3 | Collector landscape enumerated across **all** collectors, count following the shared pattern, every deviation named | **met** | `recon_report.md` §2; `research/collector-resume-landscape.md` §1–§2. Denominator 18/18 given with the `Collectors/YamlCollector` non-project explained. 14 canonical + 7 deviations named. Verified `TenableIoCollector.cs:34` declares `IResumableAdapter` (✓) and 18 types declare it repo-wide (✓). |
| SC4 | Every `IsCheckpointStale` call site enumerated, with override status | **met** | `recon_report.md` §3; W2 §4 and W3 §3b (identical 24-row tables, independently produced — a genuine cross-check). Independently counted: `grep -c IsCheckpointStale(` over `Collectors/` + `YamlAdapter/` excluding bin/obj/tests = **24** (✓). Sampled 3 rows (`TaegisCheckpointHelper.cs:16`, `FalconCheckpointResumePolicy.cs:107`, `TenableScCollector.cs:337`) — all one-argument, no override (✓). Both workers also flag the two mechanisms *outside* the table (YamlAdapter, Dummy), which is the honest form of this answer. |
| SC5 | Parameterization surface concrete: what changes, which repo, what package release | **met** | `recon_report.md` §3 + §7; W3 §4a–4d. Verified the pinch point `IResumableAdapter.CanResumeFrom(AdapterCheckpoint)` (one argument ✓), the already-optional override on `RecoveryParsingHelper.cs:37` (✓), the shared version number `IntegrationInfra/Directory.Build.props:41` and ISB's pin `Directory.Packages.props:127` (✓). **Caveat:** the implementor count attached to the breaking-change option is wrong — see §5.2 F1. |
| SC6 | Trigger owner named from evidence + the inbound message/API a retry reuses | **met** | `recon_report.md` §4; `research/trigger-ownership.md` §3, §6, §7. Verified `collection-scheduler.service.ts:1094-1096` emit, `:67` `@Cron('*/10 * * * * *')`, `collectors-helpers.service.ts:213` `correlationId: String(instance?._id)`, `:725` `delete newInstance._id`, `client-integration-instance.repository.ts:83-84`, `EventsController.cs:988`/`:1055-1056` — all exact. The "no existing re-trigger can reach a retained checkpoint" conclusion is the strongest finding in the report and is properly evidenced. |
| SC7 | `AdapterDoneMessage` definition site + consumer set + cost of adding a field | **met** | `recon_report.md` §5; W3 §6–§9. Independently confirmed: type is ISB source at `Domain/.../Messaging/AdapterDoneMessage.cs:10`; `grep -rn AdapterDoneMessage` over `/Users/user/Dev/IntegrationInfra` returns **zero** hits; `strings` on both restored DLLs returns **zero** hits. Consumer `connector-manager.controller.ts:29` `@EventPattern('collectors.done')` and the four-value status union at `connector-manager.service.ts:59` both verified exactly. |
| SC8 | Minimal ISB change under the service-hub constraint, separating hold from service | **partially met** | `recon_report.md` §6 (a)–(e); W1 §10. The separation is stated and respected (see §3 R-note and §5.3). Deducted because §6(a) asserts the other delete paths — explicitly including the unhandled-exception delete at `:880` — are "correctly left alone", which collides with W2's Falcon finding that a collector deliberately throws from `CanResumeFrom` and thereby destroys the row the feature exists to retain. Unreconciled. See §5.2 F2. |
| SC9 | Per-repo obligations split, sequencing dependencies explicit | **met** | `recon_report.md` §7 table with a `ships independently?` column and an explicit Infra → adapters → re-release → ISB order, matching W3 §4d. The "half-landed states are inert" claim is sourced to W3's both-directions inertness argument. |

---

## 2. Assumption Disposition

Default is NEVER-TESTED. A row is VALIDATED/REJECTED only where a specific line was resolved.
`actor = verifier` means I resolved the cited line myself in this pass; `actor = recon` means a
worker produced it and I confirmed the file/claim without re-resolving every line in the set.

### Prior Art

| id | status | citation | actor |
|----|--------|----------|-------|
| lessons.md#L-882455c1 — an expired vendor cursor surfaces as a classifiable HTTP failure | VALIDATED (narrowly, Falcon assets only) | `cymulate-integration-adapters/.../Flows/Assets/FalconAssetsScrollRunner.cs:131` — `catch (FalconCursorExpiredException ex) when (!string.IsNullOrWhiteSpace(scroll.AfterToken))`; resolved exactly. Generalisation to other vendors is **not** evidenced: W2 labels every non-Falcon vendor TTL `SPECULATION:` ("inferred from the handle's nature"). | verifier |
| lessons.md#L-dae004f6 — Falcon findings reaches `OnTerminalSnapshotWithoutPublishedPage` on a Phase-2 deferral | NEVER-TESTED | No worker examined `OnTerminalSnapshotWithoutPublishedPage`; the symbol appears in no worker file and in no report section. Out of the recon's path and never re-derived. | verifier |
| lessons.md#L-96bb76cc — `Cymulate.Integration.Client` is a superset of the SDK, so a contract change there is additive | NEVER-TESTED | W3 §7a performed the both-directions type-set diff and found **no** drift (1 undocumented static class source→package, 18 compiler-generated package→source). That neither validates nor rejects the superset claim, and it did **not** reproduce the ledger's "33 stale copies + missing SiemRules delta" premise that R3 was built on. The claim's operative half was also mooted: the type at issue is not in the package at all. Do not re-assume the ledger's drift figure without a file-level (not type-set) comparison. | verifier |
| lessons.md#L-57acd253 / #L-1e94a68a — a collector named in this work may have no checkpoint/resume path (Tenable.io, AgentService) | REJECTED for Tenable.io; NEVER-TESTED for AgentService | Contradicted by `Collectors/TenableIoCollector/TenableIoCollector.cs:34` (declares `IResumableAdapter`, resolved exactly) and `:448` (`public bool CanResumeFrom`), plus two staleness call sites at `Recovery/TenableIoCheckpointHelper.cs:33,:56`. **Must not be re-assumed:** that Tenable.io lacks a resume path. AgentService: absent from this repository entirely — no evidence either way, and W2 says so rather than guessing. | verifier |

### Task assumptions

| id | status | citation | actor |
|----|--------|----------|-------|
| A1 — a failed run deletes its checkpoint via `CompleteExecutionAsync` → `FlushAndCleanupCheckpointAsync` → `DeleteAsync`, regardless of `result.Success` | VALIDATED | `ProcessEventCommandHandler.cs:805` signature, `:814` call is the **first** statement, `result.Success` first read at `:831`; `FlushAndCleanupCheckpointAsync` takes no result parameter (`:1213-1217`), early-returns only on a refused write (`:1222-1223`), else `DeleteAsync` at `:1228`. All resolved exactly. | verifier |
| A2 — `CheckpointStatus.Failed` exists in the Domain enum and is written by no production code | VALIDATED, and broader than assumed | `Domain/.../Enums/CheckpointStatus.cs:21` (`Failed`), `:9` (`InFlight`). `grep -rn "CheckpointStatus.Failed\|CheckpointStatus.InFlight"` excluding tests/bin/obj returns **zero** production hits — so `InFlight` is dead too. Column is plain `text`, no CHECK constraint (`20260526144503_*.cs:20-25`). | verifier |
| A3 — `GetRecoverableAsync` filters **only** on `Status != ScheduledWait` plus claim staleness, so a retained unclaimed row is re-dispatched | VALIDATED as to consequence; the word "only" is wrong | `CheckpointRepository.cs:670-671` is the status+claim predicate and `:672` adds a **third** filter, `CheckpointPartition.OwnedByPartitionPredicate(tenantId)`, driven by `TENANT_ID` (`CheckpointRecoveryHandler.cs:56-57`). Resolved exactly. The re-dispatch conclusion holds; the "only" does not. Do not re-assume a retained row is visible to every pod. | verifier |
| A4 — `DeleteExpiredAsync` applies a single cutoff (`Checkpoint:TtlHours`, default 24) to every status except `ScheduledWait` | VALIDATED, incomplete | `CheckpointRepository.cs:441-449` (predicate exactly as assumed), `ConfigurationKeys.cs:342` + `:357` (`DefaultTtlHours = 24`). Incomplete: the same job first runs `DeleteStoppedCheckpointsAsync` with **no age and no status test** (`CheckpointCleanupJob.cs:49`; SQL at `CheckpointRepository.cs:768-782`) and applies the same cutoff to stop-request rows (`CheckpointCleanupJob.cs:73`). All resolved exactly. | verifier |
| A5 — `UnservableTerminalAge` derives from `CheckpointTtl * 0.75` | REJECTED (formula); consequence survives at defaults | `CheckpointRecoveryHandler.cs:728-736` — `max(CheckpointTtl * 0.75, UnservableEscalationAge)` where `UnservableEscalationAge = StaleClaimThreshold * 6` (`:699`). Resolved exactly, including the floor's stated rationale at `:721-726`. **Must not be re-assumed:** that the bound is a plain 0.75 multiple. It is only 18h while the TTL term dominates; lowering TTL or raising the stale threshold changes which term wins. | verifier |
| A6 — resume is decided on checkpoint presence, not on `RetryCount` or how the run was triggered | VALIDATED | `ProcessEventCommandHandler.cs:1160-1161` (null row → `ProcessAsync`), `:1165` (`if (resumable.CanResumeFrom(...))`), `:1167-1171` (`RetryCount` used only in the log line, with an inline comment saying so). Resolved exactly. | verifier |
| A7 — collector `CanResumeFrom` consumes only the `adapter_state` blob and never ISB's flat columns | **REJECTED** | Three collectors read flat columns inside the gate: `YamlAdapter.cs:450` (`DateTime.UtcNow - checkpoint.CreatedAtUtc > _maxCheckpointAge`, resolved exactly — decisive for **every** YAML vendor); `FalconCollector.cs:521` (`checkpoint.CurrentPage > 1 \|\| ProcessedItems > 0 \|\| ProcessedFindings > 0`, resolved exactly — decides throw vs. partial); `DummyCollector.cs:249` (`if (checkpoint is { CurrentPage: > 1 })` → `return true`, resolved exactly). **Must not be re-assumed:** that ISB's flat columns are inert bookkeeping the collector ignores. For YAML vendors the age gate measures *total run age*, not time since last progress. | verifier |
| A8 — `DefaultStaleThreshold` is 23h and every call site uses the default, so a week-old checkpoint is declined fleet-wide and silently restarts fresh | VALIDATED as to the constant and the 24/0 counts; "silently restarts fresh" is not universal | `RecoveryParsingHelper.cs:13` (`TimeSpan.FromHours(23)`) and `:37` (`IsCheckpointStale(DateTime, TimeSpan? threshold = null)`) resolved exactly; repo-wide count independently reproduced as **24** call sites, 3 sampled and all one-argument. Not universal: Falcon converts a `Stale` decline into a recorded `RefusedResume`/partial completion rather than a quiet restart (`FalconCollector.cs:642-656`); `DummyCheckpointHelper` has no gate at all; YamlAdapter uses a separate 24h gate that is runtime-overridable (`YamlAdapter.cs:88`, `:52`, `:190`). | verifier |
| A9 — `ICheckpointStateCompatibility` has zero implementations in the adapters repo | VALIDATED | Repo-wide grep excluding bin/obj returns exactly **one** hit and it is a comment: `cymulate-integration-adapters/src/Cymulate.Integration.Adapters/Directory.Packages.props:120`. Resolved exactly. The ISB pre-claim probe (`CheckpointRecoveryHandler.cs:283-287`) is therefore inert today. | verifier |
| A10 — `FalconResumeDecline` separates damaged (NoFlow/UnknownFlow/LoadFailed) from merely-old (Stale), internal to Falcon | VALIDATED | `Collectors/FalconCollector/Recovery/FalconCheckpointResumePolicy.cs:24` — `internal enum FalconResumeDecline` with exactly those five members and the quoted per-member intent. Read directly. (Cited in the report as `:25-39`; the `internal enum` keyword is on line **24** — one-line shift, claim correct.) The façade overload is likewise internal (`FalconCheckpointHelper.cs:32-36`, per W2). | recon |
| A11 — the checkpoint-helper pattern is uniform, so a per-request staleness bound has one shape to change | **REJECTED** | W2 §2 names 7 deviations; the load-bearing ones I resolved: `YamlAdapter.cs:442-465` implements a wholly separate mechanism that never calls `RecoveryParsingHelper`; `DummyCollector`/`DummyCheckpointHelper` has no staleness test; `TenableScCollector.cs:312` and `InsightVmCollector.cs:403` inline the gate in the collector rather than the helper; `TenableIoCollector.cs:448` routes to a second correlated gate. **Must not be re-assumed:** that editing the helper layer reaches the whole fleet. It misses every YAML vendor entirely. | verifier |
| A12 — a backend repo on this machine owns the collection trigger | VALIDATED | `cymulate-integrations/apps/integration-server/.../collection-scheduler.service.ts:1094-1096` (emit to the `collectors.run` topic) and `:67` (`@Cron('*/10 * * * * *')`), both resolved exactly; envelope built by `libs/collectors-helpers/.../collectors-helpers.service.ts:205-229`, with `correlationId: String(instance?._id)` at `:213`. | verifier |
| A13 — `AdapterDoneMessage` is the failure-reporting contract the backend consumes **and** lives in `Cymulate.Integration.Client` outside this repo | **REJECTED** (as to location); consumption half VALIDATED | Location: `IntegrationServiceBus/.../Domain/.../Messaging/AdapterDoneMessage.cs:10` (resolved exactly); zero `grep` hits across all of `/Users/user/Dev/IntegrationInfra`; zero `strings` hits in `Cymulate.Integration.Client.dll` **and** `Cymulate.IntegrationInfra.dll` at 1.2.0-preview.0 (I ran both). Consumption: `connector-manager.controller.ts:29` `@EventPattern('collectors.done')` with `@Payload() data: any`, and the contract mirror at `connector-manager.service.ts:59,:61-67,:69-76` — resolved exactly. **Must not be re-assumed:** that adding a field here costs a package release. It does not. | verifier |
| A14 — ISB can service a resume request without classifying failures | VALIDATED as a property of today's code; the *designed* endpoint is unbuilt | Today's resume path already contains no ISB judgment: `ProcessEventCommandHandler.cs:1160-1165` consults row presence then delegates the readability call to the collector; the claim-and-dispatch primitives a resume endpoint would reuse exist and were resolved — `CheckpointRecoveryHandler.cs:170-171` (`TryDispatchAsync(..., alreadyClaimed: true)`) and `CheckpointRepository.cs:694-730` (CAS transition + claim in one statement). Residual: nothing yet demonstrates that the *retention marker* can be written without ISB deciding something, and §3's per-request staleness bound puts that in question — see §5.3. | verifier |
| A15 — `adapter_state` is a flat string dictionary, so a week's retention carries no ISB schema-migration cost for that field | VALIDATED | `CheckpointDbContext.cs:70-71` — `AdapterStateJson` → `adapter_state_json`, `HasColumnType("jsonb")`, resolved exactly; written from a `Dictionary<string,string>` at `AdapterExecutionContext.cs:992-996` and read back at `ProcessEventCommandHandler.cs:1718-1723` (per W1). Note this covers only that field: §6(c) still proposes a **new** `retain_until_utc` column, which is a migration. | verifier |

---

## 3. Attention Item Disposition

| id | final disposition | evidence |
|---|---|---|
| R1 — a finding asserted from naming/layout rather than read from source | handled | The named practice (citation on every claim; verifier resolves a sample) was followed. 26 references resolved across all four workers; W1's and W4's are exact to the line, including multi-line ranges. Where a worker could not read something it said so — W2 labels every non-Falcon vendor TTL `SPECULATION:` rather than inferring from the handle's name, and W3 declined to claim the shipped `23h` constant without decompiling. Residual imprecision is off-by-one only (§5.1), never a claim the line does not support. |
| R2 — trigger owner not on this machine, leaving §4 hollow | handled | The named practice was to report absence explicitly and fall back to ISB's inbound surface. Absence did not occur: the owner was found and evidenced (`collection-scheduler.service.ts:1094-1096`, `:67`, `collectors-helpers.service.ts:213`, all resolved exactly), and the fallback was produced *anyway* — W4 §1–§2 enumerates ISB's queues, `AdapterRunMessage` shape and the full `EventsController` surface. W4 also reports what it did **not** read (`auto-remediation.service.ts`, `IntegrationsDomainDocs`) instead of implying coverage. Honest-absence discipline satisfied in the stronger form. |
| R3 — `AdapterDoneMessage` drift check must run in **both** directions | handled | W3 §7a runs source→package (158 types, 1 absent, explained) and package→source (18 absent, all compiler-generated) and §7b repeats it for the Infra package; §7c states what ISB actually compiles against. I re-ran the decisive check independently: zero `AdapterDoneMessage` hits in IntegrationInfra source **and** in both restored DLLs. Two caveats the report drops: the shipped `23h` value rests on git history + doc text, not IL (`SPECULATION:` in W3), and the ledger's "33 stale copies / missing SiemRules delta" premise was not reproduced (§2, L-96bb76cc). |
| R4 — resume uniformity overstated by counting only the haves | handled | The named practice — enumerate ALL collectors first, then mark the have-nots, report the denominator — was followed literally. W2 §1 is an 18-row table of every collector project, with the `Collectors/YamlCollector` non-project explained rather than silently dropped, and `recon_report.md` §2 leads with "**Denominator: 18 of 18**". The ledger's have-not claim is refuted with a citation for Tenable.io (verified) and reported as out-of-repo for AgentService rather than resolved by guess. One nuance the report compresses: the denominator counts *projects*, and `YamlAdapter` is a single row standing for every YAML-defined vendor. |

---

## 4. Decision drift

| decision (`decisions.md`) | outcome |
|---|---|
| Recon scoped to four repos: ISB, adapters, IntegrationInfra, and whichever backend owns the trigger | **changed during execution — widened.** Five repos were read. W4 answered the trigger question from `cymulate-integrations` but also drew load-bearing evidence from `Admin` (`exposureAnalyticsIntegrations.controller.js:882`, verified) and `cymulate-exposure-analytics` (`client-integration-instance.repository.ts:83-84`, verified). Reason: the "every re-trigger mints a new correlationId" finding needs all three re-trigger paths. Harmless widening, materially improves §4. |
| The service-hub constraint is accepted as given and shapes what recon looks for | landed as decided. `recon_report.md` §6 is framed in hold-vs-service terms and states the exclusion explicitly ("None inspects an error code, counts attempts, or judges retryability"). See §5.3 for the one place it strains. |
| Report the per-repo obligation split even where a repo's change is out of ISB's control | landed as decided. §7 table, with a `ships independently?` column and explicit ordering. |
| Proceeding on unverified: the three-repo split (state in ISB, readability in the collector, decision in the backend) is the right decomposition | **landed, but leakier than assumed, and the report does not say so.** A7 is REJECTED: three collectors read ISB-owned flat columns to make the readability call, so "readability lives entirely in the collector" is false at the margin. W3 §4b's only no-contract-change route has ISB *writing the staleness bound into the collector's state blob* — state crossing into readability. The recon's cited facts remain valid; the obligation split in §7 is unaffected in shape, but the decomposition's cleanliness was overstated. |
| Proceeding on unverified: a backend repo owning the collection trigger is present on this machine | landed as decided — validated (A12). The stated fallback was never needed. |

---

## 5. Findings

### 5.1 Citation resolution

26 references resolved across all four workers. Every one supports the claim attached to it; none
failed to resolve to the right construct. Exact-to-the-line, verified personally:

- **W1** — `ProcessEventCommandHandler.cs:805`, `:814`, `:831`, `:111-117`, `:1160-1161`, `:1165`,
  `:1213-1217`, `:1222-1223`, `:1228`, `:1971`; `CheckpointRepository.cs:97`, `:441-449`, `:670`,
  `:672`, `:768-782`; `CheckpointRecoveryHandler.cs:56-57`, `:170-171`, `:699`, `:706-707`,
  `:721-726`, `:728-736`; `CheckpointCleanupJob.cs:49`, `:73`; `CheckpointStatus.cs:9,:12,:18,:21`;
  `ConfigurationKeys.cs:342`, `:357`; `CheckpointDbContext.cs:70-71`; `EventsController.cs:19`,
  `:988`, `:992`, `:1055-1056`; `TriggerFlowMapper.cs:246-248`. W1's line references are the most
  precise in the set — every multi-line range I checked began and ended exactly where claimed.
- **W2** — `YamlAdapter.cs:447`, `:450`, `:52`, `:88`, `:190`; `FalconCollector.cs:521`;
  `DummyCollector.cs:249`; `TenableIoCollector.cs:34`; `SentinelOneCollector.cs:255`;
  `FalconAssetsScrollRunner.cs:131`; `Directory.Packages.props:120`; the 24-site count
  independently reproduced.
- **W3** — `RecoveryParsingHelper.cs:9-12`, `:13`, `:35`, `:37`; `AdapterDoneMessage.cs:10`, `:37`;
  ISB `Directory.Packages.props:127`; `AdapterEventReportingOptions.cs:12`, `:20`, `:22`;
  `AdapterDonePayload.cs:25`; `FalconResilienceStrategyFactory.cs:45`; both DLL `strings` checks.
- **W4** — `collection-scheduler.service.ts:67`, `:716`, `:725`, `:1094-1096`;
  `collectors-helpers.service.ts:207-209`, `:211-213`, `:217-225`;
  `connector-manager.controller.ts:29`; `connector-manager.service.ts:59`, `:61-67`, `:69-76`;
  `exposureAnalyticsIntegrations.controller.js:882`; `client-integration-instance.repository.ts:83-84`.

**Imprecise (claim true, line shifted — not errors of substance):**

- `IResumableAdapter.cs:49` (W3 §4a, and `recon_report.md` §3 by inheritance) — `bool
  CanResumeFrom(AdapterCheckpoint checkpoint);` is on line **50**; line 49 is the `<returns>` doc.
- `AdapterPartialDoneMessage.cs` (W3 §10, `recon_report.md` §5 `:53-71`) — **every** field
  citation in this set is shifted by one: `scheduledResumeAtUtc` is at 51/52 not 53,
  `resumeAfterSeconds` 57/58 not 59, `waitReason` 63/64 not 65, `processedSoFar` 69/70 not 71, the
  record declaration at 14 not 15. The fields all exist as described.
- `FalconCheckpointResumePolicy.cs:25-39` (W2 §5) — `internal enum` is on line 24.
- `CheckpointDbContext.cs:106-108` (W1 §10e) — the unique index spans 105-107.

No citation was found that misrepresents what its line says.

### 5.2 Substantive findings

**F1 — cross-worker contradiction, propagated into the report as fact: the `CanResumeFrom`
implementor count.** `recon_report.md` §3 states that changing the interface signature is "a
breaking Client-package signature change (**+8 implementors**)". That number comes from W3 §4b,
which derived it from `grep "public bool CanResumeFrom(AdapterCheckpoint"`. That grep structurally
cannot see explicit interface implementations, which is the *dominant* form in this fleet — W2 §2
names them as the canonical shape ("a 9-line explicit-interface stub"). I resolved both sets:
8 `public bool CanResumeFrom(...)` **plus 10** `bool IResumableAdapter.CanResumeFrom(...)`
(CloudGuard `:251`, ServiceNowCmdb `:252`, CortexXdr `:288`, MicrosoftEntraId `:254`, Taegis `:248`,
DefenderForCloud `:250`, Qualys `:238`, Guardicore `:248`, SentinelOne `:255`, DefenderVm `:235`) =
**18 implementors**, matching W2's 18-of-18 denominator exactly. The report understates the blast
radius of the breaking option by 125%. W1/W2/W3 disagreed and synthesis took the low number without
noticing that W2's own §1 table contradicted it — precisely the class of cross-worker conflict the
plan's Synthesis Approach said it would resolve.

**F2 — an unreconciled collision between W1 and W2 that undermines the feature's central case.**
`recon_report.md` §6(a) states: "The other delete paths (adapter-not-found `:790`, unhandled
exception `:880`, stop-requested) are correctly left alone." W2 §5.1 reports that Falcon
**deliberately throws** from `CanResumeFrom` when the host's flat counters show durable progress and
the blob names no recognised flow. I verified both ends: `FalconCollector.cs:507-512` says in terms
*"This throw fails the run and the host then DELETES the checkpoint row
(`ProcessEventCommandHandler.HandleUnhandledExceptionAsync` → `TryDeleteCheckpointAsync`)"*, and ISB
does exactly that at `ProcessEventCommandHandler.cs:880`. So under the proposed design, an operator
resume of a retained Falcon row whose blob is damaged **destroys the retained checkpoint on the very
attempt it was retained for** — and the surviving record is a log line. W2 flagged this itself as a
`SPECULATION:` to be checked against ISB source; W1 had the answer and the two were never joined.
This belongs in §6 or §8 of the report and is in neither.

**F3 — the Phase 1 headline is optimistic in a way §2 already refutes.** §0 claims "Phase 1 alone
delivers retained, inspectable state and working resume for runs inside the existing 23h window."
For every YAML vendor that window is not what it sounds like: §2 itself records that
`YamlAdapter.cs:450` gates on the flat, insert-only `CreatedAtUtc`, i.e. **total run age**. A YAML
run that fails at hour 23 of a long collection has a zero-length resume window under Phase 1, no
matter how recently it checkpointed. The report states the fact in §2 and never carries it into the
Phase 1 value claim, so the headline overstates what ships first for the largest vendor family.

**F4 — a `SPECULATION:` dropped rather than promoted, and it is load-bearing.** W2 §9 makes Falcon
findings' week-old viability conditional on an S3 fact it could not read: if
`{storageUrl}/_staging/{generation}/manifest.json` has aged out, a progressed checkpoint does not
restart fresh, it **throws and fails the run** (`FalconFindingsFlow.cs:563-573`), and W2 labels the
lifecycle question `SPECULATION:` explicitly. `recon_report.md` §2 keeps the mechanism but drops the
conditional, and §8 (Open questions) never raises it. Since Falcon findings is the report's own
example of the flow that *is* mechanically viable at a week, the one unknown that could invalidate
it should be an open question, not a deletion. (To the report's credit, §6(e) does preserve its own
`SPECULATION:` label on the index question — so this is an omission, not a pattern.)

**F5 — contract Output Format §8/§9 not delivered (already scored at SC1).** No assumption
disposition table anywhere in the deliverable; `assumptions.md` left entirely `OPEN`; no dedicated
"contradictions found" section, though all three workers produced one and the material is good. The
contradictions are scattered inline across §2/§3/§5, which is exactly where a reader looking for
"what did we believe that was wrong" will not find them.

**F6 — process state not updated.** `state.json` still reports `currentPhase: "execution"`, all six
steps `pending`, all four workers `status: "pending"`, and `verification.status: "not_started"`,
despite four completed worker files and a synthesized report. Not a contract Success Criterion;
recorded because a resumed session would misread where this task stands.

**Claims in `recon_report.md` with no worker file behind them:** none found. Every §1–§7 assertion I
traced lands in W1–W4. §0 and §7 are synthesis, which is their job; the only synthesis claim I
consider unsupported is the Phase 1 window in F3, and the number in F1 is supported by the *wrong*
worker.

### 5.3 Service-hub constraint in §6

**Respected.** Nothing in (a)–(e) inspects an error code, counts attempts, or scores retryability.
(a) branches on the outcome the adapter already reported — that is recording *what* was reported,
not judging *why*, which is the line `task.md` draws. (b) is a status marker and two predicate
widenings. (c) is a TTL, and holding the one-week clock is what the user asked ISB to do. (d) mirrors
`StopRun` and delegates the readability call to the collector's own `CanResumeFrom`
(`:1165`) and `ICheckpointStateCompatibility` — both explicitly named as staying collector-owned.
(e) is a read. The report states the exclusion in terms and it holds.

**One tension the report does not flag.** §3's recommended no-contract-change route has ISB inject a
staleness bound into `AdapterCheckpoint.AdapterState` at `MapToAdapterCheckpoint`. The staleness
bound *is* the readability judgment — `RecoveryParsingHelper.cs:9-12` says so ("e.g. export/cursor
expired"). If the value originates with the requester and ISB merely relays it, that is servicing and
the constraint holds. If it comes from ISB configuration, ISB is deciding how old a blob may be
before the collector should trust it — decision logic wearing a config key. The report never says
which, and §7 sequences the work without resolving it. That question should be settled before
anyone implements §3.

---

## 6. Verdict

**Pass with gaps.**

The recon is strong where it matters most: the citations are real. Across 26 independently resolved
references spanning four repositories, not one misrepresents its line, and W1's and W4's are exact
including multi-line ranges. All four Attention Items were handled by the practice that was planned
for them, including the two easiest to fake — R4's denominator is reported as a denominator, and R2's
trigger owner is evidenced rather than nominated. Three of the report's four headline conclusions
(the delete-on-failure chain, `AdapterDoneMessage` being ISB-owned and independently shippable, and
no existing re-trigger being able to reach a retained checkpoint) I verified end to end.

Named gaps:

1. **SC1 not met.** No assumption disposition table in the deliverable; `assumptions.md` untouched.
   The evidence existed in all three workers' contradiction sections and was never assembled.
   Disposed in §2 above by this verifier: 4 REJECTED (A5 formula, A7, A11, A13 location, plus the
   Tenable.io half of the prior-art row), 3 NEVER-TESTED (L-dae004f6, L-96bb76cc, AgentService).
2. **F1 — `+8 implementors` is wrong; it is 18.** W2 and W3 disagreed and synthesis took the number
   from the worker whose grep could not see the dominant implementation form. Understates a breaking
   change's cost by 10 collectors.
3. **F2 — the Falcon throw / unhandled-exception delete collision is unreconciled.** §6 asserts the
   `:880` delete path is "correctly left alone" while a shipped collector deliberately throws from
   `CanResumeFrom` and thereby deletes the row the feature retains. Both halves are cited in the
   worker files; nobody joined them.
4. **F3/F4 — two places where the report is more confident than its own sources.** The Phase 1 "23h
   window" claim ignores the YAML total-run-age gate the report itself documents, and W2's
   S3-lifecycle `SPECULATION:` — the one unknown that could invalidate the report's flagship
   week-old-resume example — is dropped rather than promoted to §8.
5. **SC8 partially met** on account of F2; **F5/F6** are deliverable-shape and state-file gaps.

None of these overturns the report's architecture. F1 and F2 both change implementation decisions and
should be corrected in `recon_report.md` before it is used to decide anything.
