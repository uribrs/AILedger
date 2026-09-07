# Verifier Pass 1 — Falcon Policy Plane Restructure

Verifier: independent pass, 2026-08-27. Compared against `git diff b14d6d64e1f317bd6c5fae71d04f8ccca6c9e481`
in the working tree on branch `fix/falcon-disable-prevention-policy-enrichment`, not against the plan's
narrative. Every test result below was re-run by this verifier, not copied from `execution_notes.md`.

## 1. Verdict and Success Criteria

**Verdict: PASS with two findings.**

The operator's headline requirement — the manifest is written first, and a policy failure can never discard
a completed Discover scroll — landed, is correct in the code (not just in a comment), and is pinned by a
store-level write-order assertion rather than inferred. All ten contract Success Criteria are met. Two
findings sit outside the numbered criteria:

- **F1 (not met).** `FalconDocs/CollectorDocs/06-prevention-policy-contract.md` and
  `07-prevention-policy-observed-variants.md` were **not touched**. `research/internal-recon.md:58` states
  they "**must be updated by items 3 and 4**" and `:13` names both as output-contract artifacts the
  flow-patterns skill requires be maintained. `git log -1 -- '*06-prevention-policy-contract.md'` returns
  `2483ff72 Mon Jul 27` — no change from this task. The published contract now describes the pre-restructure
  world: one composition site, a definition embedded per host, no policy plane, no `pgen_` generation, no
  stage ledger.
- **F2 (gap, not a criterion breach).** The `~1000-id` batch bound has no batch-degradation path. If the
  bound is wrong, a 400 with empty `resources` routes to `FalconAssignmentOutcome.AllRejected(aids)`
  (`FalconDevicePolicyClient.cs:181-184`) — the whole page's AIDs are counted rejected, the ledger goes
  `Degraded`, the stage reports **traversed**, and spotlight unblocks with zero policy edges for the tenant.
  That is correct against the operator's "policies are not mandatory" ruling and does not fail the stage, but
  `decisions.md` said the partial-rejection path "must degrade to a smaller batch rather than fail the
  stage", and no smaller-batch retry exists. The outcome is silent-but-logged zero policy coverage rather
  than a report. See also the contract's own Stop Condition on this bound.

| # | Success Criterion | Status | Evidence |
|---|---|---|---|
| SC1 | A policy-stage failure leaves a completed Discover scroll intact; manifest exists; next leg resumes without re-spooling; demonstrated by a test that fails the policy stage after the freeze and asserts no new generation | **met** | `FalconHostSpooler.cs:289` — `WriteManifestAsync` is the last statement of `SpoolAsync` before its log line and `return`; all policy work is in `RunPolicyStageAsync` (`:355-460`), a separate method reached only after `SpoolAsync` returns (`FalconFindingsFlow.cs:618-624`). Test `FalconTwoPhaseFindingsTests.cs:3378 PolicyStageFailureAfterTheFreeze_LeavesTheManifestIntact_AndTheNextLegCreatesNoNewGeneration` — **PASSED** (re-run 2026-08-27). It asserts `secondLegDiscoverScrolls == 0`, `frozen.Edges.State == Pending`, and a single `gen_` folder equal to the original `generationId`. |
| SC2 | No code path rewrites a staged host page after the manifest is written | **met** | Sole writer is `FalconStagingArea.WriteHostPageAsync` (`:192`); `grep -rn WriteHostPageAsync` over the collector returns exactly one caller, `FalconHostSpooler.cs:232`, inside the scroll loop and above `:289`. Baseline's `EnrichStagedPagesAsync` (`FalconStagingArea.cs:188` at baseRef) is **deleted** — `grep -rn EnrichStagedPagesAsync` returns nothing. Pinned at the store level by `FalconTwoPhaseFindingsTests.cs:609-626`: `store.Writes.Should().Equal(hostPageKey, manifestKey, edgePageKey, definitionsKey, manifestKey)` plus "no host page write at or after the manifest's first write". **PASSED**. |
| SC3 | A staged host page's size is independent of the number of distinct policies; the per-host edge carries ids and status only, never a definition body | **met** (wording deviation) | `FalconStagedHostPage.cs:29-32` — "A staged page carries no policy data"; the codec's line shape is `{"aid","sensorAid","lastSeen","host"}` with no policy property. Test `FalconTwoPhaseFindingsTests.cs:3508 StagedHostPageBytes_AreIndependentOfHowManyDistinctPoliciesTheVendorReturns` — **PASSED**; two runs over identical Discover output differing only in distinct-policy count, host pages byte-identical. Deviation: `FalconPolicyEdge` carries the **verbatim vendor `assignment` object** plus `definitionStatus`, not an extracted id/status pair (`FalconPolicyEdge.cs`; `FalconStagedHostPage.cs:191`). It is per-host and definition-free so the size property holds, and the verbatim form is what keeps SC-adjacent R5 true, but it is not literally "ids and status only". |
| SC4 | `DataPipelineException` classifies as non-retryable, with a test asserting it does not reach `UnknownFlowRetryPolicy` | **met** (sanctioned deviation) | `FalconFlowExceptionClassifier.cs:127` adds the arm; `ClassifyIngestionGuardFailure` (`:134-171`) returns `IsRetryable: false` / `FALCON_INGESTION_GUARD_REFUSED` for the over-ceiling cause. `FalconFlowExceptionClassifierTests.cs:177 R4_DataPipelineExceptionArmDiscriminatesCeilingFromReadSlot` — **PASSED**. The "does not reach `UnknownFlowRetryPolicy`" assertion is `ceilingHandling.Should().NotBeNull("an unclassified store guard fault falls to the blind unknown retry")`, which is the exact escape from `_ => null`; the class's own header (`:19-26`) documents that a null return is what falls through to `UnknownFlowRetryPolicy`, and `FalconCollector.cs:318-319` shows the two are alternatives. Deviation: the read-slot-timeout cause is classified **retryable** (`IsRetryable: true`), so not every `DataPipelineException` is non-retryable. Sanctioned by `orchestration_plan.md` R4 and correct — `GuardedObjectStore.cs:374-376` is a genuinely transient site. No test drives the actual Polly pipeline; the assertion is at the classifier boundary. |
| SC5 | A 404 or 400 naming rejected ids yields empty envelopes for exactly those ids, survivors keep assignments, counts appear in the ledger; tests for 404-with-empty-`resources`, 400-with-`resources`-plus-`errors`, and 200-carrying-a-404-under-`errors` | **met** | All three exist and **PASSED**: `FalconPolicyEnrichmentTests.cs:765 Rejection_404WithEmptyResources_LeavesEveryAidUnattributedAndCounted`, `:804 Rejection_400WithPopulatedResourcesAndErrors_KeepsSurvivorsAndCountsTheNamedId`, `:850 Rejection_200WithA404UnderErrors_IsStillCountedRatherThanReadAsNoPolicy`. Fourth case beyond the criterion: `:882 Rejection_200ThatSimplyOmitsAnId_IsDerivedNotVendorStated`. Attribution uses only structured `errors[].id` (`FalconDevicePolicyClient.cs:657-675`) and requested-minus-returned (`:196-199`) — no prose parsed, per constraint. Ledger counters `Rejected`/`Unresolved` are `FalconPhase1Manifest.cs:52-68`, fed by `PolicyStageCounters.Record` (`FalconHostSpooler.cs:498`) and written by `WithPolicyStages` (`FalconHostSpooler.cs:435-448`). |
| SC6 | `ProbePreventionPolicyAsync` does not fail init on 404; 401/403 still propagate; test both | **met** | `FalconAccessProber.cs:63` — `catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)`, scoped to 404 only, wrapping the whole chain. 404 tests: `FalconAccessProberTests.cs:298`, `:320`, `:342` (device, definition, Discover). Propagation tests: `:247`, `:263` (`Theory` over 401/403), `:365`. All **PASSED**. |
| SC7 | A mid-scroll HTTP 500 re-anchors from the watermark and the scroll completes with no generation lost; test asserts one generation id across the fault | **met** | `FalconHostSpooler.cs:160-198` — `catch (HttpRequestException ex) when (IsServerError(ex))`, re-anchors in-process from the watermark, bounded by `MaxConsecutiveServerErrorReanchors = 5` (`:70`), and deliberately does not reset the boundary-AID or seen-AID sets. Test `FalconTwoPhaseFindingsTests.cs:3696 MidScrollServerError_ReanchorsFromTheWatermark_AndKeepsOneGenerationAcrossTheFault` — **PASSED**; asserts `store.Writes.Select(GenerationOf).Distinct().Should().HaveCount(1)`, `manifest.HostCount == 2` (boundary AID deduplicated), and exactly `{h1, h2}` emitted once each. |
| SC8 | A tenant with zero prevention policies still produces a written policy plane object and a ledger entry distinguishing traversed-and-empty from not-traversed | **met** | Test `FalconTwoPhaseFindingsTests.cs:3785 TenantWithNoPreventionPolicies_WritesThePlaneObject_AndALedgerLineThatIsTraversedNotPending` — **PASSED**. The distinction is structural: `FalconStageState.Pending` is the only non-terminal state (`FalconPhase1Manifest.cs:16`), `IsTraversed` (`:121-127`) is false only for `Pending`, and `UnitsEmpty` counts covered-but-empty units separately (`:46-49`). `WritePolicyEdgePageAsync` is documented and coded to run for every unit including an empty one (`FalconStagingArea.cs:216-221`). |
| SC9 | Existing Falcon collector unit tests pass, including the two-phase findings tests | **met** (one pre-existing failure) | Five change-relevant classes, re-run by this verifier: **165 passed, 1 failed, 166 total, 14 m 10 s**. (`execution_notes.md` reports 162/1/163 — this verifier's `FullyQualifiedName~` filter matched three tests more; same single failure, so the discrepancy is filter breadth, not a result difference.) The single failure is `FalconTwoPhaseFindingsTests.ResumedLegThatPublishesNothing_LeavesTheCoordinateUnchanged_SoTheNoProgressBudgetAccumulates`. **Independently confirmed pre-existing**: this verifier built a fresh `git worktree --detach` at `b14d6d64` and ran that test alone — `Failed: 1, Passed: 0`, byte-identical error message (`Expected persisted["_resilience.recovery.lastProgressCoordinate"] to be "2:200:200" … but "" has a length of 0`). `git diff b14d6d64 -- '*FalconTwoPhaseFindingsTests.cs'` contains no hunk touching that test. `FalconPolicyEnrichmentTests` is `446 added / 0 removed` — every pre-existing envelope assertion is intact and all 51 pass. The 59 removed lines in `FalconTwoPhaseFindingsTests.cs` are exactly the assertions of the old behaviour (manifest-last, enriched staged page, `PolicyEnvelopeSchemaVersion`/`PolicyEnrichmentEnabled`); no assertion was weakened to make a new test pass. |
| SC10 | `assumptions.md` entries are disposed by the verifier with actor and citation | **met** | Section 2 below; written back into `assumptions.md`. |
| **F1** | *(recon-mandated, not a numbered criterion)* FalconDocs 06 and 07 updated | **not met** | `git diff --stat b14d6d64 -- '*FalconDocs*'` is empty; `git status --short` shows no FalconDocs entry. Required by `research/internal-recon.md:13,58`. |

## 2. Assumption Disposition

15 assumptions (8 under Prior Art, 5 under Task assumptions, 2 under Classified Prior Art). Numbered A1–A15
in file order. **Never-tested is the default.** No status was moved by the fact that the work completed or
that the tests passed. Several of these are vendor-behaviour claims this task deliberately did not exercise;
those are NEVER-TESTED, and that is the correct answer.

| id | status | citation | actor |
|---|---|---|---|
| A1 — `POST /devices/entities/devices/v2` accepts ~1000 ids per request | **NEVER-TESTED** | No live vendor call was made. Every rejection test uses a fabricated response (`FalconPolicyEnrichmentTests.cs:765,804,850`). The code was deliberately written to encode no batch bound at all (`FalconDevicePolicyClient.cs:93` — "no batch-size bound is encoded"), so the assumption is neither relied on nor exercised. See F2: the mitigation `decisions.md` promised (degrade to a smaller batch) is absent. | verifier, 2026-08-27 |
| A2 — A Falcon 404 arrives as an HTTP 404 status | **NEVER-TESTED** | No vendor evidence obtained. The defense was coded both ways instead, and each way is pinned by a fixture, not by the vendor: `Rejection_404WithEmptyResources_...` (`:765`) and `Rejection_200WithA404UnderErrors_...` (`:850`). `FalconDevicePolicyClient.cs:181-199` keys on body `errors[]` and requested-minus-returned, never on status alone, so the assumption no longer changes any outcome. | verifier, 2026-08-27 |
| A3 — Policy edge keys resolve against Exposure Analytics' asset match key | **NEVER-TESTED** | The `verify:` query (`select count(*) from cybi.asset_policy ap join cybi.asset a on a.value = ap.asset_match_key`) was not run — no database was touched by this task, and no downstream artifact was produced. Previously REFUTED; the restructure moves the emission site, so the risk is live and unmeasured. Mitigating fact, not evidence: the emitted envelope is byte-identical to the unmodified assets flow's (see A-note under R5). | verifier, 2026-08-27 |
| A4 — `rules[].value` reaches Postgres as native JSON | **NEVER-TESTED** | The `verify:` query (`select distinct jsonb_typeof(value) from cybi.security_policy_rule`) was not run. Definitions now travel through a new NDJSON staging codec (`FalconStagedPolicyDefinitions`, `FalconStagedHostPage.cs:336-412`) and back through `RehydrateDefinitions`, which is a changed serialization path on the very field that broke before. No downstream measurement was taken. | verifier, 2026-08-27 |
| A5 — Per-batch and per-stage memory cost is known well enough to trade against | **NEVER-TESTED** | No memory measurement exists anywhere in this task's artifacts. `execution_notes.md` reports wall-clock only (14m 8s vs 14m 10s baseline), which is not a memory figure. This task introduces two new allocations the assumption was about — a per-page assignment batch at frozen-page size (~1000 AIDs) and a run-scoped AID→edge dictionary (`FalconHostSpooler.cs:402-420`) — and neither was measured. | verifier, 2026-08-27 |
| A6 — Clamping a retry ladder's configuration inputs bounds how long the ladder can stall the pipeline | **NEVER-TESTED** | No clamp was added or exercised. This task added a classifier arm instead (`FalconFlowExceptionClassifier.cs:127`), which is a different mechanism; the assumption's subject does not appear in the diff. | verifier, 2026-08-27 |
| A7 — The findings flow shares the assets flow's checkpoint-writer paths | **REJECTED** | The assumption's own `verify:` command was run: `grep -rn 'OnTerminalSnapshotWithoutPublishedPage' src/…/FalconCollector/` returns `Flows/Assets/FalconAssetsScrollRunner.cs:339`, `:365`, `Flows/Assets/FalconAssetsCheckpointWriter.cs:114` — all three call sites under `Flows/Assets/`. The only other hit, `Flows/Findings/FalconFindingsFlow.cs:494`, is a doc-comment reference, not a call. The paths are not shared. | verifier, 2026-08-27 |
| A8 — Emission order does not affect identity | **NEVER-TESTED** | No test asserts ordering determinism for the new plane artifacts. The definitions object is serialized from `_cache.Snapshot()` and the edge page from `outcome.Edges` — both `Dictionary` enumerations with no contractual order (`FalconPreventionPolicyCache.cs:52`, `FalconStagedHostPage.cs:360`). Structurally the risk is avoided rather than tested: both artifacts are read back id-keyed into a `Dictionary` (`FalconStagingArea.cs:449`, `FalconStagedHostPage.cs:387-412`) and neither is published, so plane order cannot reach a consumer. That is an argument, not a citation for the assumption. | verifier, 2026-08-27 |
| A9 — `device_policies.prevention.settings_hash` exists and is populated | **NEVER-TESTED** | No live vendor response was inspected. The landed edge carries the vendor `assignment` object verbatim (`FalconPolicyEdge.cs`; `FalconDevicePoliciesEnvelope.cs:104`), so `settings_hash` is neither extracted nor asserted — the field's presence is exactly as unverified as at baseRef, which is what `decisions.md` predicted. | verifier, 2026-08-27 |
| A10 — `GET /policy/combined/prevention/v1` returns `precedence` and an `is_default` indicator per policy | **NEVER-TESTED** | The endpoint is **not called**. `FalconUrls.BuildPreventionPolicySweepUrl` (`:94`) has no caller — `grep -rn BuildPreventionPolicySweepUrl` over the collector returns only its own definition and a doc reference. Definitions still come from `GET /policy/entities/prevention/v1` via `FalconPreventionPolicyClient.cs:60`, which is **unmodified by this task** (`git diff --stat` empty). The assumption is unreachable in the landed code. See Decision Drift D6/D7. | verifier, 2026-08-27 |
| A11 — ~12 distinct prevention policies is representative for this tenant | **NEVER-TESTED** | No tenant measurement was taken. The claim is load-bearing only for the single-page sweep sizing that did not land (A10), so nothing in the diff depends on it any more; the per-id hydration that did land chunks at `FalconPreventionPolicyClient.MaxIdsPerRequest` and is bound-independent. | verifier, 2026-08-27 |
| A12 — Writing the manifest before policy work does not create a resumable state where spotlight reads host pages a later policy stage would have altered | **VALIDATED** | Host-page immutability holds in fact, not just intent: the only writer is `WriteHostPageAsync` (`FalconStagingArea.cs:192`) with one caller above the manifest write (`FalconHostSpooler.cs:232` vs `:289`), and the staged page carries no policy property at all (`FalconStagedHostPage.cs:29-32`, asserted as `staged.Should().NotContain("\"device_policies\"")` at `FalconTwoPhaseFindingsTests.cs:634`). The resumable state is guarded rather than merely safe: `FalconFindingsFlow.cs:214-222` throws if Phase 2 is entered with a non-traversed edge stage. Tests `FalconTwoPhaseFindingsTests.cs:574` (write order) and `:3378` (failure then resume) **PASSED**. | verifier, 2026-08-27 |
| A13 — The mid-scroll 500 re-anchor is lossless | **VALIDATED** (fixture scope) | `FalconTwoPhaseFindingsTests.cs:3696` **PASSED** and asserts losslessness directly, not by implication: `manifest.HostCount == 2` with the boundary AID re-served by the `>=` query and collapsed, `reanchoredScrolls == 1`, one generation, and `capture.StreamBatches` yielding exactly `{h1, h2}` — every host emitted exactly once across the fault. Scope of the evidence: one fault at page 1 in a fixture; not a live 97k-host scroll, and not the multi-fault path bounded at `MaxConsecutiveServerErrorReanchors = 5`. | verifier, 2026-08-27 |
| A14 — The staged ledger fits inside the manifest read ceiling | **VALIDATED** (with the stated mechanism REJECTED) | Fit claim validated: `FalconTwoPhaseFindingsTests.cs:3177 R1_LedgerStaysUnderControlArtifactCeilingAt20kPages` **PASSED**, driving a real `GuardedObjectStore` with **shipped** `IngestionOptions` defaults, a fully-populated ledger and 20,000 page keys, round-tripping through `WriteManifestAsync`/`TryReadManifestAsync`, and asserting the ledger's contribution is a constant (`(sizeBytes - oneStagedPageBytes) > ledgerOnlyBytes * 10`). Field enumeration confirms O(1): `FalconStageLedgerEntry` is 8 scalars (`FalconPhase1Manifest.cs:69-78`) and the three new manifest members are three such entries plus `ledgerVersion` (int) and `policyGenerationId` (string) — no per-page or per-host term. The assumption's *stated failure mechanism* is **rejected**: an over-ceiling read does not return null. `GuardedObjectStore.ReadAllBytesAsync` throws `DataPipelineException` (`GuardedObjectStore.cs:260,283`; empirically confirmed by R4's ceiling arm), and `TryReadManifestAsync` now converts that into a named `InvalidOperationException` (`FalconStagingArea.cs:381-392`) so an oversized manifest fails loudly instead of re-spooling. A 75%-of-ceiling warning was added at `FalconStagingArea.cs:301-322`. | verifier, 2026-08-27 |
| A15 — Classifying `DataPipelineException` as non-retryable is safe | **REJECTED** | Not safe as stated, and the work establishes why. `DataPipelineException` is `sealed`, adds no members over `InvalidOperationException`, and carries no inner exception (`IntegrationInfra/Kernel/Exceptions/DataPipelineException.cs:6-20`), while `GuardedObjectStore` raises it from five sites of which `:374-376` (read-slot acquisition timeout, `Ingestion__MaxConcurrentReads=`) is transient. A flat non-retryable arm would make read contention permanently fatal. The landed arm discriminates by cause (`FalconFlowExceptionClassifier.cs:134-171`) and `R4_DataPipelineExceptionArmDiscriminatesCeilingFromReadSlot` **PASSED**, driving a real `GuardedObjectStore` down both paths and asserting `ceiling.GetType() == readSlot.GetType()` and both inner exceptions null — i.e. asserting there is nothing to discriminate on but the message. Residual: a cause the marker cannot identify defaults to non-retryable. | verifier, 2026-08-27 |

Summary of the 15 rows: **VALIDATED 3** (A12, A13, A14 — A14's fit claim validated, its stated null-return
mechanism separately rejected) · **REJECTED 2** (A7, A15) · **NEVER-TESTED 10** (A1, A2, A3, A4, A5, A6, A8,
A9, A10, A11). These statuses are terminal.

## 3. Attention Item Disposition

R-ids verified against **this** task's artifacts only. The colliding `R2_RetriedFetch_*`,
`R3_DegreeOne_*`, `R4_Build_*` and `R5_StagedPageReadDrop_*` tests belong to
`2026-08-26_1001_falcon-spotlight-transport-retry` and are **not** credited here. All five named artifacts
were run by this verifier: `Failed: 0, Passed: 5, Total: 5`.

| id | final disposition | evidence |
|---|---|---|
| R1 — staged ledger grows past the 1 MB manifest read ceiling | **handled** | `FalconTwoPhaseFindingsTests.cs:3177 R1_LedgerStaysUnderControlArtifactCeilingAt20kPages` exists and **PASSES** (verifier run). It drives the real `FalconStagingArea` over a real `GuardedObjectStore` with untuned `IngestionOptions`, and asserts both the fit at 20k pages and — separately — that the ledger's byte contribution is a constant rather than merely small. Field enumeration independently confirms O(1): `FalconStageLedgerEntry` = `State, Units, UnitsCovered, UnitsEmpty, Items, Rejected, Unresolved, CompletedUtc` — eight scalars, no collection (`FalconPhase1Manifest.cs:69-78`). The failure mode the item named (over-ceiling read → null → silent re-spool) was additionally closed at source: `FalconStagingArea.cs:381-392` converts the throw into a named failure, so a null now only ever means absent. |
| R2 — policy artifacts accumulate forever under every run prefix | **handled** | `FalconTwoPhaseFindingsTests.cs:3270 R2_AbandonedPolicyGenerationsArePruned` exists and **PASSES**. The guard is present: `FalconStagingArea.DeleteAbandonedPolicyGenerationsAsync` (`:574-617`) lists `PolicyAreaPrefix` and keys on `TryGetPolicyGenerationId` (`FalconStagingPaths.cs:223`); it is called from the policy stage, which is the only place that knows the live id (`FalconHostSpooler.cs:382-384`). The complementary half also holds: the host pruner's `continue` on a null generation id is now documented as deliberate (`FalconStagingArea.cs:517-521`), so the two arms cannot sweep each other's artifacts. |
| R3 — unscrubbed vendor body reaches logs, or the call loses its retry pipeline | **handled** | `FalconPolicyEnrichmentTests.cs:1081 R3_RejectionPathScrubsBodyAndAppliesPolicies` exists and **PASSES**, and asserts the raw credential token is **absent** rather than merely that a redaction marker is present; a companion `:1138 R3_PropagatedFailureScrubsTheExceptionItRaises` also passes. The guards are present at their sites: `LogRedaction.Scrub` at `FalconDevicePolicyClient.cs:233-236` (degraded-response log), `:260-262` (error log) and `:272-274` (the exception's message, `Url` and `BodySnippet` before publication); the Polly pipeline is retained by routing through `IHttpSession.StreamResponseAsync` (`:130`) with the resilience-bearing session rather than a bare handler, tagged `R3 — policy runner` at `:122`. |
| R4 — a transient read-slot timeout is classified terminal | **handled** | `FalconFlowExceptionClassifierTests.cs:177 R4_DataPipelineExceptionArmDiscriminatesCeilingFromReadSlot` exists and **PASSES**. The test drives a **real** `GuardedObjectStore` (constructed over an in-memory `IAdapterObjectStore`, which is the store the guard is designed to wrap — the guard itself, the type that throws, is genuine and untuned) down both causes: `ReadAllBytesAsync` over a 512-byte ceiling, and a genuinely contended `OpenReadAsync` with `MaxConcurrentReads = 1, ReadSlotTimeoutSeconds = 1` while the only slot is held. Not a hand-built stand-in: neither exception is constructed in the test. The arm's message marker `Ingestion__MaxConcurrentReads=` was independently checked against `GuardedObjectStore.cs` — it appears at exactly one of the five `throw new DataPipelineException` sites (`:374-376`); the others (`:139`, `:260`, `:283`) do not carry it. **Proportionality judgement:** the message match is justified. `DataPipelineException` is `sealed`, adds no members over `InvalidOperationException`, and every Ingestion throw site passes a bare message with no inner exception, so there is provably nothing else to key on; the token chosen is a configuration key (public contract) rather than prose; the coupling is pinned by a test that breaks the build on a reword; and the repo rule being bent is named in the code that bends it (`FalconFlowExceptionClassifier.cs:154-160`). This is the right call, not a shortcut. |
| R5 — the two flows stop emitting a canonically identical host envelope | **handled** | `FalconCollectorTests.cs:2231 R5_BothFlowsEmitCanonicallyIdenticalDevicePolicies` exists and **PASSES**. It drives **both flows end to end** over one shared route table and compares `findingsEnvelope.GetRawText()` against `assetsEnvelope.GetRawText()` — emitted bytes, property order included — and guards against vacuity by first asserting the envelope is the fully-resolved shape (`collection_status: complete`, `definition_status: resolved`, a real `policy_id` and a hydrated `definition.id`). |

## 4. Decision Drift

| # | Decision | Disposition |
|---|---|---|
| D1 | Manifest written after the Discover scroll, not after enrichment; becomes a per-stage ledger | **landed as decided.** `FalconHostSpooler.cs:270-289`; ledger types at `FalconPhase1Manifest.cs:69-131`. |
| D2 | `bool? PolicyEnrichmentEnabled` + `HasCompatiblePolicyContract` replaced by staged terminal state plus coverage counters | **landed as decided.** `grep -rn 'HasCompatiblePolicyContract\|PolicyEnrichmentEnabled\b\|CurrentPolicyEnvelopeSchemaVersion'` over `src/` returns only one doc-comment reference in a test. Successors: `IsPreLedger` (`:349`) and `EdgesContradictMode` (`:396`), both asserted at `FalconTwoPhaseFindingsTests.cs:655`. |
| D3 | Policy work moves out of `SpoolAsync` into a post-freeze stage; `ApplyPolicyContractAsync` becomes the normal path | **landed as decided** (renamed). `ApplyPolicyContractAsync` is gone; the normal path is `FalconFindingsFlow.EnsurePolicyPlaneAsync` → `FalconHostSpooler.RunPolicyStageAsync`, and `FalconFindingsFlow.cs:702-712` states explicitly that it is the normal path for all three arrival states rather than a migration branch. Name change only. |
| D4 | Policy output is a plane: definitions once, per-host edges separately | **landed as decided.** `_staging/policies/pgen_<id>/definitions.…` (`FalconStagingPaths.cs:170-188`) and per-page edge objects (`:203`). |
| D5 | Per-host edge carries `policy_id`, `applied`, `settings_hash`, `collection_status`, `definition_status` | **changed during execution.** The edge carries the **verbatim vendor `assignment` object** plus `definitionStatus` (`FalconPolicyEdge.cs`; staged line shape at `FalconStagedHostPage.cs:191`). Reason: extracting fields would have rebuilt the emitted `prevention.assignment` from parts and broken the "canonically identical" contract that R5 pins; the named fields ride inside the verbatim object. Better than decided — it removes a transformation rather than adding one. |
| D6 | Per-policy record carries name, platform, enabled, description, settings, rules, precedence, `is_default` | **changed during execution.** No field projection exists. The definitions object stores the vendor definition verbatim (`{"id","definition"}`, `FalconStagedHostPage.cs:337-365`), sourced from `/policy/entities/prevention/v1`. `precedence` and `is_default` are therefore **not obtained** — they are fields of the combined endpoint that was not called (D7). No reason recorded in `execution_notes.md`. |
| D7 | Definitions come from `GET /policy/combined/prevention/v1`, host-independent, own `pgen` generation | **partially abandoned, silently.** The `pgen` half landed. The **source** did not: `FalconUrls.BuildPreventionPolicySweepUrl` (`:94`) was written and has **no caller**; `FalconPreventionPolicyClient.cs` is unmodified by this task and still calls `BuildPreventionPolicyUrl` → `/policy/entities/prevention/v1` per id (`:55-69`). So definitions are still per-id hydrated, now hoisted to the policy stage and cached run-scoped, and the sweep is dead code. `execution_notes.md` records no decision to drop it — W1's deliverable 3 was cancelled over the `FalconUrls.cs` ownership violation, and the sweep's *consumer* appears to have gone with it. Functionally benign (the cache makes hydration effectively once per tenant, and reusing the entities endpoint is why the emitted definition body is unchanged), but it is undocumented drift and it silently voids A10 and A11. |
| D8 | Assignments batched at frozen-page size (~1000 AIDs), not at `aidBatchSize` | **landed as decided.** `FalconHostSpooler.cs:395-405` resolves one outcome per staged page. Test `FalconTwoPhaseFindingsTests.cs:3621 PolicyStage_MakesOneAssignmentCallPerStagedPage_NotPerAidBatch` **PASSES**, asserting `deviceCalls == 1` at `aidBatchSize: 1`. |
| D9 | Spotlight gates on traversed, not succeeded | **landed as decided.** `IsSpotlightUnblocked => Edges.IsTraversed` (`FalconPhase1Manifest.cs:387`); the definitions sweep is deliberately excluded from the gate (`:374-380`); asserted rather than assumed at the call site (`FalconFindingsFlow.cs:214-222`). |
| D10 | Proceeding on unverified ~1000-id bound; mitigation (degrade to a smaller batch) required regardless | **changed during execution.** The survivor-preserving half landed (`FalconDevicePolicyClient.cs:181-208`) and no bound is encoded. The **batch-degradation** half did not: a 400 with empty `resources` becomes `AllRejected(aids)` for the whole page, with no smaller-batch retry. See F2. |
| D11 | Proceeding on unverified `settings_hash` presence | **landed as decided.** Unchanged path; rides inside the verbatim assignment. A9 stays NEVER-TESTED, as predicted. |
| D12 | Defense keys on body `errors[]` and requested-vs-returned, not HTTP status alone | **landed as decided.** `FalconDevicePolicyClient.cs:181-199`; only structured `errors[].id` is read (`:657-675`), no prose. |

## 5. Things checked hard

**Manifest-first, read from the code.** `SpoolAsync` (`FalconHostSpooler.cs:99-297`) was read in full, line
by line. It contains **no policy call of any kind**. `_policyEnricher` is referenced at exactly six places
in the file — `:85` (constructor), `:363`, `:402`, `:409`, `:414`, `:430` — and all five non-constructor
references are inside `RunPolicyStageAsync` (`:355-460`), which is a different method. `WriteManifestAsync`
is `:289`, followed only by a log line and `return manifest`. Tracing the flow: `ResolveFrozenKeyListAsync`
→ `SpoolAsync` (`FalconFindingsFlow.cs:618`) → **returns** → `EnsurePolicyPlaneAsync` (`:622`) →
`RunPolicyStageAsync` (`:709`). There is no path that reaches a policy call before the manifest write in the
freeze path. The comment at `:284-287` is not the evidence; the call graph is. Additionally checked: the
`finally` block at `:255-262` between the loop and the manifest write only observes a pending prefetch — no
vendor request is initiated there. And the ordering is pinned at the store level, so a future edit that
reintroduces the defect fails a test rather than a review: `FalconTwoPhaseFindingsTests.cs:609-626`.

*One boundary worth naming, not a breach:* `FalconAccessProber.ProbePreventionPolicyAsync` does hit the
policy endpoints, but at init-time configuration validation, entirely outside and before any generation
exists. The operator's invariant is about the freeze, and it holds.

**Staged ledger O(1).** Fields enumerated, not assumed. `FalconStageLedgerEntry` =
`State, Units, UnitsCovered, UnitsEmpty, Items, Rejected, Unresolved, CompletedUtc` — eight scalars, no
collection, no per-page or per-host member (`FalconPhase1Manifest.cs:69-78`). The manifest gained
`ledgerVersion` (int), three such entries, and `policyGenerationId` (string). The only members that grow
with the traversal are `PageKeys` (pre-existing) and `DiscoverWatermarkAids` (pre-existing, bounded by
`MaxBoundaryAids = 5000`). Confirmed empirically by R1's constant-vs-linear assertion.

**Emitted `device_policies` unchanged versus baseRef.** Verified three independent ways, because the whole
scope decision rested on it.
1. *The shape functions are byte-identical to baseRef.* `git diff b14d6d64 -- FalconDevicePoliciesEnvelope.cs`
   is a **pure addition**: only `Compose` was added. `BuildEnvelope`, `ForAssignment`, `Empty`, `Disabled`
   and `ToWireValue` — the entire wire vocabulary (`schema_version`, `collection_status`,
   `definition_status`, `resolved`/`not_found`/`unavailable`) — are untouched, and `Compose` funnels every
   branch back through `ForAssignment`, so it cannot produce a shape `ForAssignment` cannot.
2. *The definition body's source is unchanged.* Definitions still come from `/policy/entities/prevention/v1`
   via `FalconPreventionPolicyClient.cs:60`, a file this task did not modify. The combined-sweep endpoint,
   which would have returned a different per-policy shape, is not called (D7). So `prevention.definition` is
   the same vendor object as at baseRef.
3. *Cross-checked against an unmodified emitter.* `Flows/Assets/*` has **zero diff** against baseRef, and
   R5 asserts the findings envelope equals the assets envelope byte-for-byte including property order over a
   fully-resolved payload. Findings-emitted `device_policies` therefore equals the output of an emitter this
   task did not touch. Corroborated by `FalconPolicyEnrichmentTests.cs` being `446 added / 0 removed` — all
   51 pre-existing envelope assertions intact and passing — and by
   `PublishedEnvelope_IsByteCompatibleWithTheCurrentParserContract`.
   Caveat recorded: `FalconPolicyEnricher.EnrichAsync` now composes via `Compose` (`:162`) rather than
   inline, so the assets flow's *code path* changed even though its files did not. Points 1 and 3 cover it.

**FalconDocs 06 and 07.** **Not updated — finding F1.** Both files are unmodified since `2483ff72`
(2026-07-27). The docs are now stale in specific, checkable ways: §6.1's two composition sites are correct
by luck rather than by documentation; there is no record of the policy plane, the `pgen_` generation, the
stage ledger, the traversed-not-succeeded gate, or the partial-rejection semantics. `07-observed-variants`
records no new variant for the 400-with-populated-`resources` or the 200-carrying-a-404, both of which this
task built defenses for and both of which are exactly what that document exists to hold.

**Pre-existing operator changes and `Flows/Assets/*`.** All intact.
`Collectors/Directory.Build.props` — `CollectorVersion` `6.3.3` → `6.3.4`, present.
`FalconCollectorConfiguration.cs:179` — `EnablePreventionPolicyEnrichment … = false`, present.
`git diff --stat b14d6d64 -- …/Flows/Assets/` is empty; `git status --short` lists nothing under `Flows/Assets/`.

**`FalconPhase1Manifest`'s "NO ORDINAL DERIVATION LIVES HERE ANY MORE" block.** Nothing reintroduces what it
forbids. The block itself is preserved verbatim (diffed against baseRef line-for-line) and was **extended**
with a paragraph that names the new hazard and rules it out: "A stage entry counts UNITS COVERED. It is never
read as a position, nothing walks it back into a coordinate, and Phase 2's resume point is still read from
the checkpoint and nowhere else" (`:266-275`). Checked against the code, not the comment: `Units` /
`UnitsCovered` are consumed only by `IsTraversed`, `Finished` and `ToString`; no `OutputPageFor`,
`BatchesPerPage`, `LastPossibleOutputPage`, `PositionAfter` or `pageHostCounts` reappears; `IsSpotlightUnblocked`
is a boolean, and its only consumer is a guard (`FalconFindingsFlow.cs:216`), not an arithmetic.

**W2's message-match arm.** Judged proportionate — see R4 above for the full reasoning and the pinning test's
authenticity. Two residual risks, both accepted and both named in the code: a message reword in
IntegrationInfra breaks the build rather than production (by design), and an unidentifiable future cause
defaults to non-retryable, which is the correct prior given four of the five throw sites are deterministic.

## 6. Findings to act on

1. **F1 — FalconDocs 06 and 07 not updated.** Recon-mandated, not done. The only unmet obligation in this
   task. Cheap to fix and it is the artifact the next reader of this subsystem will trust.
2. **F2 — no batch-degradation path for the ~1000-id bound.** `decisions.md` and the contract's Stop
   Condition both call for degrading the batch on a live 400; the landed code counts the whole page rejected
   and continues. Outcome if the bound is wrong: tenant-wide zero policy coverage, ledger `Degraded`, run
   green.
3. **D7 — undocumented decision drift.** `BuildPreventionPolicySweepUrl` is dead code and the combined-sweep
   decision was dropped without a note. Either wire it up or delete the builder; leaving an uncalled,
   fully-documented URL builder is how the next engineer concludes the sweep is the live path.
4. **Pre-existing failure worth its own task.** `ResumedLegThatPublishesNothing_LeavesTheCoordinateUnchanged_SoTheNoProgressBudgetAccumulates`
   fails at baseRef, independently confirmed here. Not attributable to this task, and not fixed by it.
