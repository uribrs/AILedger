# Verifier Pass 2 — Falcon Policy Plane Restructure

Verifier: independent second pass, 2026-08-27, after three repair rounds (verifier-1's F1/F2/A10, then
code-review-1's B0/B1/M2/M3/M5 and minors). Compared against `git diff b14d6d64e1f317bd6c5fae71d04f8ccca6c9e481`
in the working tree on branch `fix/falcon-disable-prevention-policy-enrichment`. Every table below was rebuilt
from the code and from re-run commands; nothing was carried over from `review/verifier-1.md`.

Cycle-2 note on method: the repairs moved enough code that most of verifier-1's line citations are now stale.
Two of its conclusions need distinguishing. Its D7 finding ("partially abandoned, silently") was **correct when
written and has since been closed** by the A10 repair — superseded, not wrong. Its third piece of evidence that
the emitted envelope is unchanged ("the definition body's source is unchanged … definitions still come from
`/policy/entities/prevention/v1`") is now **false for the findings flow**, again because of that same repair.
The conclusion it supported survives on other grounds; the reasoning does not. Both are handled in §5.

---

## 1. Verdict and Success Criteria

**Verdict: PASS with four findings, none of which is a Success-Criterion breach.**

The task's one invariant holds and is pinned at the store level. All ten contract Success Criteria are met.
The findings are (i) `execution_notes.md` misreports three dispositions — most importantly it records M4 as an
accepted risk when the fix actually landed, and records B1+M3 as FIXED when the emission sites never adopted the
new state; (ii) the `collection_status: "unavailable"` state and its `ComposeFor` entry point have **zero
production callers**, so two of the repair round's tripwire tests assert over dead code; (iii) the A10 repair
(wiring the definitions sweep) introduced a new cross-flow divergence that doc 06 both creates and contradicts;
(iv) the M4 resume-skip and the m11 policy-generation adoption both landed with **no test at all**.

| # | Success Criterion | Status | Evidence |
|---|---|---|---|
| SC1 | A policy-stage failure leaves a completed Discover scroll intact; manifest exists; next leg resumes without re-spooling; demonstrated by a test that fails the policy stage after the freeze and asserts no new generation | **met** | `FalconHostSpooler.SpoolAsync` read in full (`:99-296`): `WriteManifestAsync` at `:289` is the last statement before a log line and `return manifest`. All policy work lives in `RunPolicyStageAsync` (`:355-528`), reached only after `SpoolAsync` returns (`FalconFindingsFlow.cs:619` → `:622` → `:712`). Test `FalconTwoPhaseFindingsTests.cs:3394 PolicyStageFailureAfterTheFreeze_LeavesTheManifestIntact_AndTheNextLegCreatesNoNewGeneration` — **PASSED** (this verifier's full-suite run). It asserts `frozen.Freeze.IsTerminal`, `frozen.Edges.State == Pending`, no edge object written, and one generation. |
| SC2 | No code path rewrites a staged host page after the manifest is written | **met** | `grep -rn WriteHostPageAsync` over the collector returns exactly two hits: the definition (`FalconStagingArea.cs:193`) and one caller (`FalconHostSpooler.cs:232`), inside the scroll loop, 57 lines above the manifest write at `:289`. `EnrichStagedPagesAsync` (baseRef's rewriter) returns nothing. Pinned at the store level by `FalconTwoPhaseFindingsTests.cs:590 Phase1_StagesDiscoverHostsUnderStorageUrl_AndWritesTheManifestBeforeAnyPolicyWork` — **PASSED**. |
| SC3 | A staged host page's size is independent of the number of distinct policies; the per-host edge carries ids and status only, never a definition body | **met** (same wording deviation as cycle 1) | Staged host line shape is `{"aid","sensorAid","lastSeen","host"}` (`FalconStagedHostPage.cs:15,38-41`) — no policy property. Edge line shape is `{"aid","assignment","definitionStatus"}` (`:201`), definition-free. Test `FalconTwoPhaseFindingsTests.cs:3524 StagedHostPageBytes_AreIndependentOfHowManyDistinctPoliciesTheVendorReturns` — **PASSED**. Deviation unchanged: the edge carries the verbatim vendor `assignment` object rather than an extracted id/status pair. The size property holds regardless. |
| SC4 | `DataPipelineException` classifies as non-retryable, with a test asserting it does not reach `UnknownFlowRetryPolicy` | **met** (sanctioned deviation) | Arm present; discriminates by cause. Ceiling → `FALCON_INGESTION_GUARD_REFUSED`, `IsRetryable: false`. Read-slot → `FALCON_INGESTION_READ_SLOT_TIMEOUT`, `IsRetryable: true`, keyed on `IngestionReadSlotMarker = "Ingestion__MaxConcurrentReads="` (`FalconFlowExceptionClassifier.cs:204`). Marker independently re-verified against `GuardedObjectStore.cs`: it appears at exactly one of the five `throw new DataPipelineException` sites (`:374-379`); `:139`, `:260`, `:283`, `:330` do not carry it. `FalconFlowExceptionClassifierTests.cs:177 R4_DataPipelineExceptionArmDiscriminatesCeilingFromReadSlot` — **PASSED**. Deviation (read-slot retryable) is sanctioned by `orchestration_plan.md` R4 and is correct. |
| SC5 | A 404 or 400 naming rejected ids yields empty envelopes for exactly those ids, survivors keep assignments, counts appear in the ledger; tests for 404-with-empty-`resources`, 400-with-`resources`-plus-`errors`, and 200-carrying-a-404-under-`errors` | **met** | All three shapes exist and **PASSED**: `FalconPolicyEnrichmentTests.cs:907 Rejection_404WithEmptyResources_…`, `:950 Rejection_400WithPopulatedResourcesAndErrors_…`, `:996 Rejection_200WithA404UnderErrors_…`, plus a fourth beyond the criterion at `:1028 Rejection_200ThatSimplyOmitsAnId_IsDerivedNotVendorStated`. Attribution uses only structured `errors[].id` (`FalconDevicePolicyClient.cs:882`) and requested-minus-returned (`:322`) — no prose parsed. Ledger counters fed by `PolicyStageCounters.Record` (`FalconHostSpooler.cs:568-594`) and written by `WithPolicyStages` (`:499-512`). **Read the criterion literally: it asks for _empty_ envelopes for the rejected ids, and that is what ships (`complete` / `prevention: null`).** Code-review-1's M3 argued that is the wrong design; that is a critique of the contract, not a breach of it. See finding 2. |
| SC6 | `ProbePreventionPolicyAsync` does not fail init on 404; 401/403 still propagate; test both | **met** | `FalconAccessProber.cs:68` — `catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.NotFound)`, scoped to 404 only. 401/403 propagation preserved (`:237-242` is the unrelated 401 hint path). 404 tests `FalconAccessProberTests.cs:298`, `:320`, `:342`; propagation `Theory` at `:365`. All **PASSED** in the full run. |
| SC7 | A mid-scroll HTTP 500 re-anchors from the watermark and the scroll completes with no generation lost; test asserts one generation id across the fault | **met** | `FalconHostSpooler.cs:161-200` — `catch (HttpRequestException ex) when (IsServerError(ex))`, re-anchors in-process via the shared `ReanchorAsync`, bounded by `MaxConsecutiveServerErrorReanchors`, and deliberately does not reset `boundaryAids`/`seenAids` (`:174-177`). Test `MidScrollServerError_ReanchorsFromTheWatermark_AndKeepsOneGenerationAcrossTheFault` — **PASSED**. |
| SC8 | A tenant with zero prevention policies still produces a written policy plane object and a ledger entry distinguishing traversed-and-empty from not-traversed | **met** | `FalconTwoPhaseFindingsTests.cs:3801 TenantWithNoPreventionPolicies_WritesThePlaneObject_AndALedgerLineThatIsTraversedNotPending` — **PASSED**. Structural: `WritePolicyEdgePageAsync` runs for every page including an empty one (`FalconHostSpooler.cs:480-485`, comment and code agree); `EmptyUnits` is counted separately from `Rejected` and explicitly refuses to collapse them (`:587-593`). |
| SC9 | Existing Falcon collector unit tests pass, including the two-phase findings tests | **met** (one pre-existing failure) | See §6 for this verifier's own full-suite numbers and the baseRef comparison. |
| SC10 | `assumptions.md` entries are disposed by the verifier with actor and citation | **met** | §2 below; rebuilt for cycle 2 and written back into `assumptions.md`. |

### Findings outside the numbered criteria

- **V2-1 — `execution_notes.md` misreports three dispositions.** `M4` is recorded as "ACCEPTED RISK,
  follow-up required", but the fix code-review-1 asked for **landed**: `FalconHostSpooler.cs:437-455` is
  the resume-skip, comment and all ("RESUME, DON'T RESTART … Without this the stage was one non-resumable
  unit"). `FalconHostSpooler.cs` mtime is `16:21`, after code-reviewer-1.md at `16:06`, and
  `execution_notes.md` was written at `17:17` — i.e. after the code it misdescribes. In the other
  direction, six minors that were **not** disposed in the notes are in fact fixed: m8 (`LedgerVersion`
  check at `FalconPhase1Manifest.cs:330` plus `NotSupportedException` at `:316`), m9
  (`PreLedgerEnrichmentMode` at `:415`, read back at `FalconFindingsFlow.cs:684-686`), m10 (`HashSet`
  at `FalconHostSpooler.cs:429`), m11 (prior-generation adoption at `:382-404`), m13 (the duplicated
  `<summary>` is gone; `IsTransientDefinitionOutage` now carries its own doc), m14's second half
  (`WarnIfNotRelativeToBase` in the policy prune arm, `FalconStagingArea.cs:650`). Only m14's first half
  is genuinely unresolved. **Impact:** the follow-up task for M4 would be written against a false premise,
  and the true residual (no test, no budget signal) would be missed.
- **V2-2 — `collection_status: "unavailable"` and `ComposeFor` have zero production callers.**
  `grep -rn "Unresolved()\|ComposeFor" Collectors/` returns the definitions plus two comments explaining
  why neither emission site uses them. Both real sites do the lookup themselves and fall back to `Empty()`:
  `FalconPolicyEnricher.cs:154-163` (assets) and `FalconFrozenKeyList.cs:118-129` (findings). The reason is
  sound and stated in both files — `FalconAidRejectionReason.RequestFailed` currently covers "a successful
  2xx merely omitted this id", so routing it to `unavailable` would relabel the ordinary unmanaged host —
  and the test suite is honest about it, pinning the current behaviour as a "KNOWN GAP (M3)" at
  `FalconPolicyEnrichmentTests.cs:925-935`. What is **not** sound is that `execution_notes.md` calls
  B1+M3 "FIXED", and that the two tripwires cited as proof — `M3_RejectedAidAndUnmanagedAid_EmitDifferentCollectionStatus`
  (`:1491`) and `B1_TwoRefusedAidsInOneBatch_…`'s status assertion (`:1562`) — call `ComposeFor` directly.
  They pass over code no production path reaches, which is exactly what `orchestration_plan.md`'s synthesis
  rule forbade ("its tripwires must drive the real production path, not a stand-in"). B1's *bisection* half
  is genuinely fixed and genuinely covered (`F2_…`, `:1386`, `:1431`); it is only the emission half that is not.
- **V2-3 — the A10 repair introduced a cross-flow definition-source divergence, and doc 06 contradicts
  itself about it.** Findings now sources definitions from `GET /policy/combined/prevention/v1`
  (`FalconHostSpooler.cs:422` → `FalconPolicyEnricher.cs:294` → `FalconPreventionPolicyClient.cs:86,105`).
  The **assets flow never sweeps**: `FalconAssetsScrollRunner.cs:315` → `EnrichAsync` →
  `HydrateDefinitionsAsync` → `FetchDefinitionsAsync` → `BuildPreventionPolicyUrl`
  (`FalconPreventionPolicyClient.cs:186`, the by-id endpoint). Doc 06 §6.4 states the sweep is the source
  and the by-id call is "the fallback" without qualification — false for `CollectAssets` — and in the same
  paragraph asserts that "any field the sweep carries and the ID-keyed lookup does not now reaches you
  automatically". §6.1 simultaneously promises the two flows emit a canonically identical `device_policies`.
  Those two statements cannot both hold. R5 cannot catch it and says so: `FalconCollectorTests.cs:2292-2297`
  deliberately serves the same body from both endpoints so the comparison tests composition, and the comment
  names this as "the second drift channel this test now has to cover". So the published cross-flow guarantee
  now rests on an unverified vendor fact (that the two endpoints return the same per-policy object) where
  before it rested on nothing. Not a criterion breach — the criterion and the constraint are about the
  envelope *vocabulary*, which is unchanged, and `definition` was always a verbatim vendor object — but it
  is the most consequential thing the repair rounds introduced and it is undocumented as a risk.
- **V2-4 — the M4 resume-skip and the m11 adoption path have no tests.** `grep` over the test project for
  the skip (`TryReadPolicyEdgePageAsync`-driven resume, `ResumedUnits`) and for
  `TryFindCompletedPolicyGenerationAsync` returns nothing. `PolicyStageFailureAfterTheFreeze_…` does not
  exercise either: it has one staged page and fails on page 0, so no edge object exists when the second leg
  runs (the test asserts exactly that at `:3450`). Code-review-1 asked for the test in the same paragraph as
  the fix ("kill the stage mid-loop, resume, assert the device endpoint is called only for the uncovered
  pages"). Two new resume-path branches, both on the critical path before Spotlight, both uncovered.
- **V2-5 (minor) — `state.json` is stale.** Every one of the ten steps is `"pending"`, every worker is
  `"pending"`, `verification.status` is `"not_started"`, `verifierRun`/`codeReviewerRun` are `false`, and
  `lastUpdated` is `2026-08-27T08:45:00Z` — before any code was written. The contract's Execution Rules
  require "update `state.json` step statuses as they change". Bookkeeping only, but it is the file a resumed
  session reads first.
- **V2-6 (minor) — resumed-leg counter fidelity in the supplemental stage.** `PolicyStageCounters.Record`
  recovers the edge stage's counters exactly from a skipped page (`FalconHostSpooler.cs:568-594`), but
  `unavailableDefinitions` (`:429`) is populated only by `HydrateDefinitionsAsync` calls made *this* leg
  (`:476`). A resumed leg that skips every page reports `unresolved: 0` for the definitions ledger entry even
  if prior legs recorded definition outages. Same shape as the m10 defect, one level up. Also: on such a leg
  `WritePolicyDefinitionsAsync` (`:495`) persists only the sweep's set, so the definitions object can come
  back *poorer* than a prior leg's — which the M2 fix then degrades honestly to `partial`, so it is fidelity,
  not correctness.
- **V2-7 (minor, not this task's defect) — the raised snippet budget cannot currently be filled.**
  `FalconHttpFailureClassifier.MaxFormattableBodyChars` was raised 4096 → 16000 "sized to the policy path's
  error-body budget", but the two skipped tests (§7) record that the upstream reader that would supply those
  16000 chars still stops after one network chunk. The policy path routes around it with its own full-body
  reader, so the task's own defense is unaffected; other Falcon calls still get a short snippet.

---

## 2. Assumption Disposition (rebuilt for cycle 2)

15 assumptions, A1–A15 in file order. **Never-tested is the default.** No status moved because the work
completed or because tests passed. Statuses that changed since cycle 1 are marked **MOVED**. These statuses
are terminal.

| id | status | citation | actor |
|---|---|---|---|
| A1 — `POST /devices/entities/devices/v2` accepts ~1000 ids per request | **NEVER-TESTED** | No live vendor call. Every rejection and bisection test drives a fabricated `SessionStub` (`FalconPolicyEnrichmentTests.cs:909-912`, `:1386`, `:1431`). The code encodes no batch bound (`FalconDevicePolicyClient.cs:94-103` sizes only the *search budget* from the batch), so F2's bisection makes the bound irrelevant rather than verifying it. | verifier-2, 2026-08-27 |
| A2 — A Falcon 404 arrives as an HTTP 404 status | **NEVER-TESTED** | No vendor evidence obtained. Both shapes are coded and each is pinned by a fixture only. Attribution keys on body `errors[].id` (`FalconDevicePolicyClient.cs:882`) and requested-minus-returned (`:322`), never on status alone (`:306`), so the assumption no longer changes any outcome. | verifier-2, 2026-08-27 |
| A3 — Policy edge keys resolve against Exposure Analytics' asset match key | **NEVER-TESTED** | The `verify:` query (`select count(*) from cybi.asset_policy ap join cybi.asset a on a.value = ap.asset_match_key`) was not run; no database was touched and no downstream artifact produced. Previously REFUTED, and the restructure moves the emission site, so the risk is live and unmeasured. | verifier-2, 2026-08-27 |
| A4 — `rules[].value` reaches Postgres as native JSON | **NEVER-TESTED** | The `verify:` query (`select distinct jsonb_typeof(value) from cybi.security_policy_rule`) was not run. **The exposure grew since cycle 1:** definitions now travel a second new path as well — the sweep's `JsonObject` → `FalconStagedPolicyDefinitions` NDJSON codec → `RehydrateDefinitions` (`FalconStagedHostPage.cs:336-412`) — on the exact field that broke before. Unmeasured downstream. | verifier-2, 2026-08-27 |
| A5 — Per-batch and per-stage memory cost is known well enough to trade against | **NEVER-TESTED** | No memory figure exists in any artifact; wall-clock is not memory. Three new allocations, none measured: the per-page ~1000-AID assignment batch, the run-scoped AID→edge dictionary, and (new since cycle 1) the sweep's whole-tenant definition list requested at `limit=5000` (`FalconPreventionPolicyClient.cs:41`). | verifier-2, 2026-08-27 |
| A6 — Clamping a retry ladder's configuration inputs bounds how long the ladder can stall the pipeline | **NEVER-TESTED** | No clamp was added or exercised. This task added classifier arms (`FalconFlowExceptionClassifier.cs`), a different mechanism; the assumption's subject does not appear in the diff. | verifier-2, 2026-08-27 |
| A7 — The findings flow shares the assets flow's checkpoint-writer paths | **REJECTED** | `verify:` grep re-run by this verifier: `OnTerminalSnapshotWithoutPublishedPage` call sites are `Flows/Assets/FalconAssetsScrollRunner.cs:339`, `:365` and `Flows/Assets/FalconAssetsCheckpointWriter.cs:114` — all three under `Flows/Assets/`. The only other hit, `Flows/Findings/FalconFindingsFlow.cs:494`, is a doc-comment reference. Not shared. | verifier-2, 2026-08-27 |
| A8 — Emission order does not affect identity | **NEVER-TESTED** | No ordering-determinism test for the plane artifacts. **Exposure grew since cycle 1:** the sweep is now the primary definition source and deliberately pins no `sort` (`FalconPreventionPolicyClient.cs:30-34`), so vendor array order is now an input to the definitions object. Mitigating structure, not evidence: `CaptureSweptDefinition` dedups id-keyed (`:142-157`), both plane artifacts are read back into a `Dictionary`, and neither is published — so order cannot reach a consumer. That is an argument. | verifier-2, 2026-08-27 |
| A9 — `device_policies.prevention.settings_hash` exists and is populated | **NEVER-TESTED** | `verify:` grep re-run: the parser reads it (`crowdstrike_policy_projection.py:266,361`), which is downstream *expectation*, not vendor confirmation. No live response inspected. The edge carries the assignment verbatim, so the field is neither extracted nor asserted — exactly as unverified as at baseRef, as `decisions.md` predicted. | verifier-2, 2026-08-27 |
| A10 — `GET /policy/combined/prevention/v1` returns `precedence` and an `is_default` indicator per policy | **NEVER-TESTED — MOVED (now reachable)** | Cycle 1 found the endpoint uncalled. It is now wired and reachable: `FalconHostSpooler.cs:422` → `FalconPolicyEnricher.cs:294` → `FalconPreventionPolicyClient.cs:86` → `FalconUrls.BuildPreventionPolicySweepUrl` (`:105`), with `RunPolicyStageAsync` called unconditionally for a pending edge stage (`FalconFindingsFlow.cs:705-713`). The **per-policy field claim is still unexercised**: `grep -rn "precedence\|is_default\|platform_default"` over the whole test project returns one unrelated comment and no assertion; the definition is stored verbatim and neither field is projected. Docs record the gap honestly (07 §7.6) and the code emits a one-shot diagnostic (`FalconPolicyEnricher.cs:357`) so the next real run answers it. Reachability caveat: under the shipped default `EnablePreventionPolicyEnrichment = false`, `RunPolicyStageAsync` takes the early return at `FalconHostSpooler.cs:363` and the sweep never runs — see §5. | verifier-2, 2026-08-27 |
| A11 — ~12 distinct prevention policies is representative for this tenant | **NEVER-TESTED — MOVED (load-bearing again)** | No tenant measurement was taken. Cycle 1 called this "no longer load-bearing" because the sweep had not landed. It has: the sweep is sized single-page-or-report-incomplete at `SweepPageSize = 5000` (`FalconPreventionPolicyClient.cs:27-41`), and a second page sets `SinglePage: false`, which degrades the definitions ledger unit to not-covered (`FalconHostSpooler.cs:506`). The claim is a sizing input again, and still unmeasured — though the consequence of being wrong is now a reported degradation rather than silence. | verifier-2, 2026-08-27 |
| A12 — Writing the manifest before policy work does not create a resumable state where spotlight reads host pages a later policy stage would have altered | **VALIDATED** | Immutability holds in fact: one writer (`FalconStagingArea.cs:193`), one caller (`FalconHostSpooler.cs:232`) above the manifest write (`:289`), and the staged line shape carries no policy property (`FalconStagedHostPage.cs:15,38-41`). The resumable state is guarded, not merely safe: `FalconFindingsFlow.cs:216` throws if Phase 2 is entered with a non-traversed edge stage, and `FalconFrozenKeyList.cs:101-106` throws rather than emitting hosts as policy-free when an edge object the ledger claims is missing. Tests `FalconTwoPhaseFindingsTests.cs:590` and `:3394` **PASSED**. | verifier-2, 2026-08-27 |
| A13 — The mid-scroll 500 re-anchor is lossless | **VALIDATED (fixture scope)** | `MidScrollServerError_ReanchorsFromTheWatermark_AndKeepsOneGenerationAcrossTheFault` **PASSED**; the dedup sets are provably not reset across the arm (`FalconHostSpooler.cs:174-177`, and `ReanchorAsync` touches neither). Scope of the evidence: one fault in a fixture; not a live 97k-host scroll, and not the multi-fault path bounded at `MaxConsecutiveServerErrorReanchors`. | verifier-2, 2026-08-27 |
| A14 — The staged ledger fits inside the manifest read ceiling | **VALIDATED (stated null-return mechanism REJECTED)** | Fit: `R1_LedgerStaysUnderControlArtifactCeilingAt20kPages` (`FalconTwoPhaseFindingsTests.cs:3193`) **PASSED** on shipped `IngestionOptions` defaults (`IngestionOptions.cs:38,92` re-checked: 1 MiB). O(1) confirmed by field enumeration: `FalconStageLedgerEntry` is eight scalars, and the stage entries are the only new members besides `ledgerVersion` (int) and `policyGenerationId` (string). Stated mechanism rejected: an over-ceiling read **throws** (`GuardedObjectStore.cs:260,283`) and `FalconStagingArea.cs:383-393` converts it into a `FalconStagedPlaneCorruptException` whose message names the actual cause and the knob to raise, so null now only ever means absent; a 75%-of-ceiling warning was added at `:295-312`. | verifier-2, 2026-08-27 |
| A15 — Classifying `DataPipelineException` as non-retryable is safe | **REJECTED** | Not safe as stated. `verify:` grep re-run: `GuardedObjectStore` raises the type from five sites (`:139`, `:260`, `:283`, `:330`, `:374`), of which `:374-379` is a read-slot acquisition timeout and transient. The landed arm discriminates by cause on `"Ingestion__MaxConcurrentReads="` (`FalconFlowExceptionClassifier.cs:204`), verified to appear at that one site only, and `R4_…` **PASSED** driving a real `GuardedObjectStore` down both paths. Residual, named in the code: an unidentifiable future cause defaults to non-retryable. | verifier-2, 2026-08-27 |

**Summary:** VALIDATED 3 (A12, A13, A14 — A14's fit validated, its stated mechanism separately rejected) ·
REJECTED 2 (A7, A15) · NEVER-TESTED 10 (A1, A2, A3, A4, A5, A6, A8, A9, A10, A11). Two of the never-tested
rows MOVED in substance without moving in status (A10 became reachable, A11 became load-bearing again), and
two grew their exposure (A4, A8). Ten never-tested out of fifteen is the correct answer for a task that made
no live vendor call and touched no database; it is not a deficiency in the work.

---

## 3. Attention Item Disposition

R-ids verified against **this** task's artifacts only. The colliding `R2_RetriedFetch_*`, `R3_DegreeOne_*`,
`R4_Build_*`, `R4_TransportRetryLadder_*`, `R4_WorstLegalLadder_*` and `R5_StagedPageReadDrop_*` tests are in
`FalconCollectorConfigurationBuilderTests.cs` and `FalconSpotlightConcurrencyTests.cs`, belong to
`2026-08-26_1001_falcon-spotlight-transport-retry`, and are **not** credited here. `handled` below means the
named artifact exists, was run, and the guard was read.

| id | final disposition | evidence |
|---|---|---|
| R1 — staged ledger grows past the 1 MB manifest read ceiling | **handled** | `FalconTwoPhaseFindingsTests.cs:3193 R1_LedgerStaysUnderControlArtifactCeilingAt20kPages` exists and **PASSED** in this verifier's full run. Guard read: `FalconStageLedgerEntry` is eight scalars with no collection; the failure mode the item named (over-ceiling read → null → silent re-spool) is closed at source, because `FalconStagingArea.cs:383-393` converts the `DataPipelineException` into a `FalconStagedPlaneCorruptException` naming the `Ingestion__MaxControlArtifactBytes` knob (and reasoning, in the message, that a re-spool would write an equally oversized manifest and never terminate) — which the classifier then marks non-retryable via the arm ordered deliberately above the `DataPipelineException` arm (`FalconFlowExceptionClassifier.cs:89-94`), and `:295-312` warns at 75%. |
| R2 — policy artifacts accumulate forever under every run prefix | **handled** | `FalconTwoPhaseFindingsTests.cs:3286 R2_AbandonedPolicyGenerationsArePruned` exists and **PASSED**. Guard read: `FalconStagingArea.DeleteAbandonedPolicyGenerationsAsync` lists `PolicyAreaPrefix` and keys on `TryGetPolicyGenerationId`, called from the only place that knows the live id (`FalconHostSpooler.cs:410-412`), with the reason for that placement documented at `FalconStagingArea.cs:620-626`. `WarnIfNotRelativeToBase` is now applied in this arm too (`:650`), closing m14's second half — the silent-leak case this item was about. |
| R3 — unscrubbed vendor body reaches logs, or the call loses its retry pipeline | **handled** | `FalconPolicyEnrichmentTests.cs:1227 R3_RejectionPathScrubsBodyAndAppliesPolicies` and `:1284 R3_PropagatedFailureScrubsTheExceptionItRaises` exist and **PASSED**. Guards read at their sites: `LogRedaction.Scrub` on the degraded-response log, the error log (`FalconDevicePolicyClient.cs:463-470`) and the exception's message/`Url`/`BodySnippet` before publication (`:477`). Note for completeness: the *sweep* added by the A10 repair goes through `AdapterHttpClient.GetStreamAsync` (`FalconPreventionPolicyClient.cs:103-108`), so it keeps scrubbing and the Polly pipeline by construction rather than by re-application. |
| R4 — a transient read-slot timeout is classified terminal | **handled** | `FalconFlowExceptionClassifierTests.cs:177 R4_DataPipelineExceptionArmDiscriminatesCeilingFromReadSlot` exists and **PASSED**. Guard read and marker independently re-verified against `GuardedObjectStore.cs:374-379` — one throw site of five carries `Ingestion__MaxConcurrentReads=`. Proportionality judgement unchanged from cycle 1 and I agree with it: the type is `sealed`, carries no inner exception, and the token is a public configuration key rather than prose, so there is provably nothing else to key on; the coupling breaks the build on a reword. |
| R5 — the two flows stop emitting a canonically identical host envelope | **handled, with a new residual the test names itself** | `FalconCollectorTests.cs:2251 R5_BothFlowsEmitCanonicallyIdenticalDevicePolicies` exists and **PASSED**. It drives both flows end to end over one route table, asserts `sweepCalls == 1` so the findings path really composes from the sweep, guards against vacuity by asserting the fully-resolved shape first, and compares `GetRawText()` including property order. **Residual, introduced after cycle 1 and stated in the test's own comment (`:2292-2297`):** the two flows now also differ in *where the definition came from*, and the fixture closes that channel by serving one body from both endpoints. The composition is proven identical; the source parity is a vendor fact nobody has checked. See finding V2-3. |

---

## 4. Decision Drift

| # | Decision | Disposition |
|---|---|---|
| D1 | Manifest written after the Discover scroll, not after enrichment; becomes a per-stage ledger | **landed as decided.** `FalconHostSpooler.cs:270-289`; ledger types `FalconPhase1Manifest.cs:69-131`, `LedgerVersion` at `:218,226`. |
| D2 | `bool? PolicyEnrichmentEnabled` + `HasCompatiblePolicyContract` replaced by staged terminal state plus coverage counters | **landed as decided, with one deliberate partial reversal.** The successors are `IsPreLedger` (`FalconPhase1Manifest.cs:398`) and `EdgesContradictMode`. The reversal is m9's fix: `policyEnrichmentEnabled` is retained as `LegacyPolicyEnrichmentEnabled` (`:223`) and surfaced as `PreLedgerEnrichmentMode` (`:415`) for **read-back only**, so the diagnostic for a pre-ledger artifact can state the mode instead of calling it unknowable. Correct — the field is a fact in the artifact, and refusing to read it was the defect. |
| D3 | Policy work moves out of `SpoolAsync` into a post-freeze stage; `ApplyPolicyContractAsync` becomes the normal path | **landed as decided** (renamed). `FalconFindingsFlow.EnsurePolicyPlaneAsync` → `FalconHostSpooler.RunPolicyStageAsync`, and `:707-710` states it is the normal path for all three arrival states rather than a migration branch. |
| D4 | Policy output is a plane: definitions once, per-host edges separately | **landed as decided.** |
| D5 | Per-host edge carries `policy_id`, `applied`, `settings_hash`, `collection_status`, `definition_status` | **changed during execution.** The edge carries the verbatim vendor `assignment` object plus `definitionStatus` (`FalconStagedHostPage.cs:201`). Reason recorded and sound: extracting fields would rebuild `prevention.assignment` from parts and break the identity R5 pins. Better than decided — one fewer transformation. |
| D6 | Per-policy record carries name, platform, enabled, description, settings, rules, precedence, `is_default` | **changed during execution, and the reason is now recorded.** Still no field projection — the definitions object stores `{"id","definition"}` verbatim. Since the A10 repair, `precedence`/`is_default` are no longer *unobtainable*: the sweep that would carry them is called, and the doc explains why they are not promoted to named fields (the parser's existing null carries a comment refusing to guess `is_default` from a name, and a new guess would be worse than the null — doc 06 §6.4, doc 07 §7.6). Cycle 1's "no reason recorded" no longer applies. |
| D7 | Definitions come from `GET /policy/combined/prevention/v1`, host-independent, own `pgen` generation | **landed as decided — cycle 1's "partially abandoned" is now superseded.** Both halves are in: the `pgen` prefix, and the source (`FalconPreventionPolicyClient.cs:86,105`, reached from `FalconHostSpooler.cs:422`). `BuildPreventionPolicySweepUrl` is no longer dead code. Two things landed beyond the decision: the by-id endpoint is retained as a per-policy fallback, and the `pgen` folder now *earns* its separation via `TryFindCompletedPolicyGenerationAsync` adoption (`FalconHostSpooler.cs:382-404`), which was m11. New drift the decision did not anticipate: the assets flow was left on the by-id source, which is V2-3. |
| D8 | Assignments batched at frozen-page size (~1000 AIDs), not at `aidBatchSize` | **landed as decided.** `FalconHostSpooler.cs:457-468`; test `PolicyStage_MakesOneAssignmentCallPerStagedPage_NotPerAidBatch` (`FalconTwoPhaseFindingsTests.cs:3637`) **PASSED**. |
| D9 | Spotlight gates on traversed, not succeeded | **landed as decided.** Asserted rather than assumed at the call site (`FalconFindingsFlow.cs:216`); the definitions sweep is excluded from the gate, and a failed sweep degrades to `unitsCovered: 0` without blocking (`FalconHostSpooler.cs:500-510`). |
| D10 | Proceeding on unverified ~1000-id bound; mitigation (degrade to a smaller batch) required regardless | **landed as decided — cycle 1's F2 is closed.** Bisection with a budget scaled from the batch (`FalconDevicePolicyClient.cs:94-103,150`, `MaxIsolationsFunded = 4`), `VendorRejected` (isolated singleton) stays distinguishable from `RequestFailed` (abandoned at the floor), and the remainder is reported unresolved rather than as "no policy". Tests `F2_…` (`:1386`, `:1431`) **PASSED**. Still no bound encoded anywhere. |
| D11 | Proceeding on unverified `settings_hash` presence | **landed as decided.** A9 stays NEVER-TESTED, as predicted. |
| D12 | Defense keys on body `errors[]` and requested-vs-returned, not HTTP status alone | **landed as decided.** `FalconDevicePolicyClient.cs:306,322,882`; no prose parsed. |

---

## 5. Things checked hard

**1. Does `WriteManifestAsync` still precede ALL policy work in `SpoolAsync`? — YES.**
`SpoolAsync` (`FalconHostSpooler.cs:99-296`) was read line by line. It contains no policy call of any kind.
`_policyEnricher` appears at exactly three places in the whole file below line 300 — `:74` (field), `:85`
(constructor assignment), `:91` (doc comment) — and every use is inside `RunPolicyStageAsync` at `:363` and
below. `WriteManifestAsync` is `:289`, followed only by a log line and `return manifest`. The two `catch` arms
inside the loop (`:148` cursor-expired, `:161` server-error) both route to `ReanchorAsync`, which fetches
Discover pages only. The `finally` at `:263-268` only observes a pending prefetch and initiates no request.
Call graph: `ResolveFrozenKeyListAsync` → `SpoolAsync` (`FalconFindingsFlow.cs:619`) → **returns** →
`EnsurePolicyPlaneAsync` (`:622`) → `RunPolicyStageAsync` (`:712`). The comment at `:286-288` is not the
evidence; the call graph is. And the order is pinned at the store level, so a future edit that reorders it
fails a test rather than a review (`FalconTwoPhaseFindingsTests.cs:590`).
*Boundary, not a breach:* `FalconAccessProber.ProbePreventionPolicyAsync` hits policy endpoints at init-time
validation, before any generation exists. The invariant is about the freeze, and it holds.

**2. Is the emitted `device_policies` still byte-identical to baseRef for equivalent input? — YES, and the
`collection_status: "unavailable"` concern is moot: that value never reaches the wire.**
This is the strongest result of the pass, and it is stronger than `execution_notes.md` claims.
- `git diff b14d6d64 -- FalconDevicePoliciesEnvelope.cs` is a **pure addition**. `BuildEnvelope`,
  `ForAssignment`, `Empty`, `Disabled`, `ToWireValue` and every wire constant are untouched. `Compose` funnels
  every branch back through `ForAssignment`, so it cannot produce a shape `ForAssignment` cannot.
- **`Unresolved()` — the only producer of `collection_status: "unavailable"` — has no production caller.**
  `grep -rn "Unresolved()\|ComposeFor\|StatusUnavailable" Collectors/` returns the definitions
  (`FalconDevicePoliciesEnvelope.cs:52,67,174,191,198`) and two comments explaining why neither emission site
  uses them. `Unresolved()` is reached only from `ComposeFor`; `ComposeFor` is reached from nothing. So the
  new value appears in **neither** the correct nor the incorrect case — it appears nowhere. The
  adapters-only scope decision is preserved outright, not conditionally.
- **Where it *would* have appeared, baseRef was indeed wrong.** Traced to be sure the design intent was
  sound: an AID the vendor answered for but that carries no `device_policies.prevention` gets
  `FalconPolicyEdge.None()` (`FalconDevicePolicyClient.cs:842`) → `Compose` sees a null assignment →
  `Empty()` → `complete`. Only a *declined or omitted* AID would have taken the `unavailable` arm, and at
  baseRef those hit `assignments.TryGetValue` → miss → `Empty()`, i.e. a false `complete`
  (`git show b14d6d64:…/FalconPolicyEnricher.cs:95-101`). The intent was correct; the emission sites simply
  did not adopt it. See V2-2.
- **Parser tolerance, checked at source rather than taken from the notes.**
  `crowdstrike_policy_projection.py:271` filters `_external_id` (= `prevention.assignment.policy_id`) not-null.
  Every `Unresolved()` envelope carries `prevention: null`, so such a row would drop out exactly as `disabled`
  does; and `grep` for status literals across `libs/packages/parsers/crowdstrike/` finds no enum or whitelist
  validating `collection_status` — it is carried as a plain `StringType`. So the value would have been safe
  even if it did ship.
- **Cross-checked against an unmodified emitter.** `git diff --stat b14d6d64 -- Flows/Assets/` is empty, and
  R5 asserts the findings envelope equals the assets envelope byte-for-byte including property order over a
  fully-resolved payload.
- **One correction to verifier-1.** Its evidence #2 — "the definition body's source is unchanged … definitions
  still come from `/policy/entities/prevention/v1`" — is **no longer true for the findings flow** after the
  A10 repair. The conclusion survives on evidence #1 and #3, but the reasoning does not, and the change it
  reflects is V2-3.

**3. Are `Flows/Assets/*` and the two pre-existing operator changes intact? — YES, all three.**
`git diff --stat b14d6d64 -- …/Flows/Assets/` is empty and `git status --short` lists nothing under
`Flows/Assets/`. `FalconCollectorConfiguration.cs:179` — `EnablePreventionPolicyEnrichment … = false`, present
in the diff. `Collectors/Directory.Build.props` — `CollectorVersion 6.3.3 → 6.3.4`, present. Both were
re-read from `git diff`, not from a grep of the file.
*Caveat carried forward from cycle 1 and still true:* the assets flow's **files** are unchanged but its **code
path** changed, because `FalconPolicyEnricher.EnrichAsync` now composes via `Compose` (`:167`). Points 2 and
V2-3 cover the two consequences.

**4. Does anything reintroduce what `FalconPhase1Manifest`'s "NO ORDINAL DERIVATION LIVES HERE ANY MORE" block
forbids? — NO.**
The block is preserved at `:237-273` and **extended** with a paragraph that names the new hazard and rules it
out: "A stage entry counts UNITS COVERED. It is never read as a position, nothing walks it back into a
coordinate, and Phase 2's resume point is still read from the checkpoint and nowhere else." Checked against the
code, not the comment: `grep -rn "OutputPageFor|BatchesPerPage|LastPossibleOutputPage|PositionAfter|pageHostCounts"`
over the entire collector returns only prose — the block itself and two lines in
`FalconDocs/CollectorDocs/06-resume-live-state-authority.md`. No symbol reappears. `Units`/`UnitsCovered` are
consumed only by `IsTraversed`, `Finished` and `ToString`; `IsSpotlightUnblocked` is a boolean whose only
consumer is a guard (`FalconFindingsFlow.cs:216`), not an arithmetic. The M4 resume-skip is the closest new
thing to a position, and it deliberately avoids being one: it derives resumability from *the presence of the
edge object under its deterministic key* (`FalconHostSpooler.cs:437-448`), persisting no new coordinate — the
right side of this line, and the code says so.

**5. Is the definitions sweep actually reachable in production? — YES, but only with the flag on. State this
plainly, because the answer has two halves.**
- *Wiring:* fully reachable, no dead link.
  `FalconFindingsFlow.cs:705` calls `RunPolicyStageAsync` unconditionally for any generation whose edge stage
  is not traversed → `FalconHostSpooler.cs:419-422` → `FalconPolicyEnricher.cs:294` →
  `FalconPreventionPolicyClient.cs:86` → `FalconUrls.BuildPreventionPolicySweepUrl` (`:105`). No `#if`, no
  feature flag of its own, no condition that is never true. R5 proves it fires end to end through the real
  collector (`sweepCalls.Should().Be(1)`), which is the check A10 failed in cycle 1.
- *Two conditions under which it does not run, both intended:*
  (a) **the shipped default.** `EnablePreventionPolicyEnrichment` defaults to `false` (the operator's
  production unblock), so `RunPolicyStageAsync` takes the early return at `FalconHostSpooler.cs:363-376`,
  stamps `Off()` ledger entries and calls nothing. **Under shipped defaults the sweep never executes in
  production.** This is out of scope to change (`task.md` non-goals) and is correctly out of scope, but it
  means A10 will not be answered by a default-configuration production run, and the diagnostic log line the
  docs point at will not fire either.
  (b) **adoption.** When `TryFindCompletedPolicyGenerationAsync` finds this run's own surviving `pgen`, the
  sweep is skipped and the definitions are rehydrated instead (`:419-422`) — the saving that makes the
  separate plane earn its keep. Correctly scoped to the run's own prefix, so it cannot adopt another run's
  stale set. Untested, per V2-4.

**6. My opinion on the M4 acceptance, since you asked me to scrutinise it.**
**The acceptance is moot and should be struck, not upheld.** The fix landed — `FalconHostSpooler.cs:437-455`
is verbatim the shape code-review-1 proposed, including the safety argument about the mode-flip case — and it
landed *before* `execution_notes.md` recorded the acceptance. So the disposition is not a judgement I need to
agree or disagree with; it is a factual error in the notes.

On the merits of what remains: I would **not** block. Two of M4's three halves are closed. The stage is now
resumable and each leg is strictly cheaper than the last, which is the part that made non-convergence possible.
What is still open is the third half — the stage books no forward progress against the deferred-recovery
budget, calling only `ReportHeartbeat` (`:489`) — and code-review-1 itself said the skip "is the part that
matters". I agree, and I would leave the budget signal to its own contract.

But I would insist on two things before this is called done, because they are cheap and because the current
state is worse than either a clean fix or a clean deferral:
1. **Correct the notes.** An accepted risk that has silently been fixed is the worst of the three states: the
   follow-up task gets written against a false premise, and the real residual (no budget signal, no test) is
   invisible.
2. **Add the test.** A resume-path branch with no coverage is one refactor away from silently reverting, and
   the revert would restore exactly the ~98-reads-and-~98-vendor-calls-per-leg shape on the 97k-host tenant.
   Code-review-1 named the test in the same paragraph as the fix; the fix was taken and the test was not.

---

## 6. Test evidence, measured by this verifier

Full unfiltered `FalconCollector` test project, `--no-build` after a clean build (0 warnings, 0 errors):

```
<<<SUITE>>>
```

Baseline comparison for the single failure was not re-derived here: `execution_notes.md` and
`review/verifier-1.md` independently reported the same throwaway-worktree result at baseRef `b14d6d64`
(`FalconTwoPhaseFindingsTests` 43 passed / 1 failed / 44 total), with a byte-identical error message, and
`git diff b14d6d64 -- '*FalconTwoPhaseFindingsTests.cs'` contains no hunk touching that test. Two independent
prior confirmations plus an untouched test body is sufficient; **the failure is pre-existing and not
attributable to this task.**

---

## 7. The two skipped tests

Both are in `StreamedResponseBodyReaderTests.cs` (`:55`, `:69`), skipped with the same reason string:
*"IntegrationInfra 1.2.0-preview.0 owns this reader and still stops after one network chunk; re-enable when
the package carries fb4c636f-equivalent behavior."*

- `:56 ReadFirstCharsAsync_WithChunkedStream_ReadsUpToTheFullBudget_NotJustTheFirstChunk`
- `:70 ReadFirstCharsAsync_WhenBodyExceedsBudget_TruncatesToExactlyTheBudget`

**Do they conceal something this task should have covered? No — but they are closer to this task than they
look, and one thing follows from them.**

They are about exactly this task's central problem. The first test's fixture is described in its own comment as
"the shape of a CrowdStrike batch rejection, whose `errors` list follows a large `resources` array" — i.e. the
reason a status-only or snippet-only defense fails. The skip records that the upstream reader cannot deliver
its requested budget over a chunked response.

This task's answer was to stop depending on that reader for the path that matters:
`FalconDevicePolicyClient` reads the **full** body itself via `IHttpSession.StreamResponseAsync` and parses
`resources` and `errors` in one forward pass, with the reasoning written down at `:32` and `:521` and pinned by
`Rejection_400WithPopulatedResourcesAndErrors_…` (whose remarks at `FalconPolicyEnrichmentTests.cs:943-947`
name the truncation problem explicitly). So the rejection semantics that SC5 covers do **not** run through the
skipped code, and the skips hide no gap in this task's deliverables.

What does follow is V2-7: W1 raised `FalconHttpFailureClassifier.MaxFormattableBodyChars` from 4096 to 16000
"sized to the policy path's error-body budget", and these two skips are the record that the upstream reader
still cannot fill that budget over a chunked response. The bound is harmless — a short snippet parses fine, it
is only *less* informative — and it is not this task's defect. But the comment justifying the new number rests
on a budget the package does not currently honour, and the two skipped tests are the only place that is
written down.

---

## 8. Findings to act on, in order

1. **V2-1 — correct `execution_notes.md`.** Strike the M4 acceptance (the fix landed at
   `FalconHostSpooler.cs:437-455`), restate the true residual (no budget signal, no test), correct
   "B1 + M3 — FIXED" to name the emission-site gap the test suite already documents, and record the six
   undisposed minors that are in fact fixed (m8, m9, m10, m11, m13, m14b) plus the one that is not (m14a).
2. **V2-4 — add the two missing tests.** Kill the policy stage mid-loop, resume, assert the device endpoint
   is called only for uncovered pages and the ledger counters are exact. Separately, cover
   `TryFindCompletedPolicyGenerationAsync` adoption: assert the sweep is not re-paid and the definitions
   ledger unit is still reported covered.
3. **V2-3 — resolve the cross-flow definition source.** Either point the assets flow at the sweep too, or
   correct doc 06 §6.4 to say the sweep is the findings-flow source and the by-id call is the assets-flow
   source, and qualify §6.1's identity guarantee accordingly. Leaving §6.4's "any field the sweep carries
   and the ID-keyed lookup does not now reaches you automatically" next to §6.1's "canonically identical"
   is a contradiction a consumer will eventually act on.
4. **V2-2 — decide `ComposeFor`'s fate.** Either give `FalconAidRejectionReason` a third value that separates
   "a 2xx omitted this id" from "the request failed" and route both emission sites through `ComposeFor`, or
   delete `ComposeFor`/`Unresolved`/`StatusUnavailable` and keep the ledger counters as the sole record. An
   unreachable state with two passing tests over it is how the next engineer concludes `unavailable` ships.
5. **V2-5 — update `state.json`.** It still reports every step and worker as pending.
6. **Pre-existing, not attributable, still worth its own task.**
   `ResumedLegThatPublishesNothing_LeavesTheCoordinateUnchanged_SoTheNoProgressBudgetAccumulates` fails at
   baseRef. `execution_notes.md` already records the substantive hypothesis (no checkpoint snapshot carries
   `_resilience.recovery.lastProgressCoordinate`), which — if it holds in production and not only in the
   fixture — means a repeatedly-barren run never trips its backstop. Same family as `L-dae004f6`, and it is
   adjacent to M4's open third half.
