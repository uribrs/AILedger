# Execution Notes

Contract designed 2026-08-27T08:17:56Z. baseRef
`b14d6d64e1f317bd6c5fae71d04f8ccca6c9e481` on branch
`fix/falcon-disable-prevention-policy-enrichment`.

Branch carried two uncommitted operator changes at contract time, both out of
scope and to be preserved: `EnablePreventionPolicyEnrichment` default false,
`CollectorVersion` 6.3.4.

(Appended by execution.)

## W0 — freeze shared spine (main thread, complete)

- NEW `Flows/Policies/FalconPolicyEdge.cs` — `FalconPolicyEdge(Assignment, DefinitionStatus)` with `.PolicyId`/`.None()`; `FalconAidRejectionReason { VendorRejected, RequestFailed }`; `FalconAssignmentOutcome(Edges, Rejected)` with `.Empty()`/`.AllRejected()`. Separating `Rejected` from `Edges` is what makes "skip and count" checkable — a declined AID can never be folded into "no policy".
- `Flows/Policies/FalconDevicePoliciesEnvelope.cs` — added `Compose(edge, definitionLookup)`, the emission-time half of the staged-edge split. Produces the same wire shape as `ForAssignment`, so §6.1's "canonically identical" contract holds and the parser is untouched.
- Deliberately did NOT change `EnrichAsync`'s signature. Recon warned set-D (assets) collapses into set-C if it changes; adding alongside keeps them disjoint.
- FalconCollector csproj builds clean, 0 warnings, 0 errors.

## W2 — classification and validation (complete)

- `Processing/FalconFlowExceptionClassifier.cs` — `DataPipelineException` arm added, splitting by CAUSE not type (R4). Worker established from source that the type is `sealed`, adds no members over `InvalidOperationException`, and every Ingestion throw site passes a message with no inner exception — so the cause is legible only in text. It matches on the `Ingestion__MaxConcurrentReads=` configuration key (public contract, one throw site) rather than prose, documents the repo rule it is bending, and pins it with a test driving a real `GuardedObjectStore` down both paths so a reworded message fails the build. Residual causes default to non-retryable, with the rationale that of the throw sites exactly one is transient.
- `Processing/Validation/FalconAccessProber.cs` — 404 on the prevention probe now reports unverified scope instead of failing init; 401/403 still propagate.
- Tests updated in `FalconFlowExceptionClassifierTests.cs`, `FalconAccessProberTests.cs`.

### Ownership violation, handled

W2 also wrote `Processing/Urls/FalconUrls.cs`, which the plan assigns to W1, while W1 was still running. The added `BuildPreventionPolicySweepUrl(limit, offset)` is sound — bounds-checked against the spec's [1,5000] limit, pins no `sort` because no sort field on that endpoint is confirmed, and carries an explicit note that no prevention-members counterpart exists because of the 10k ceiling. Rather than revert good work, the orchestrator froze it and instructed W1 to skip its deliverable 3 and revert any edit of its own to that file. Recorded because two workers writing one file is exactly the failure the disjoint-set decomposition exists to prevent, and it happened anyway.

## W1 — policy clients (complete, resumed once)

- `Flows/Policies/FalconDevicePolicyClient.cs` — constructor is now `(IHttpSession session, ILogger logger)`; `FetchPreventionAssignmentsAsync` returns `Task<FalconAssignmentOutcome>`. Reads the FULL body via `IHttpSession.StreamResponseAsync` and parses `resources` and `errors` in one forward pass, because `AdapterHttpClient` reads only a truncated snippet and then disposes `RawResponse`.
- R3 re-applied explicitly at four tagged sites: the defensive policy runner, and three scrub sites (log line, degraded-response log, and the exception's message plus Url/BodySnippet before it is published as an error event).
- `Flows/SharedFlows/FalconHttpFailureClassifier.cs` — snippet-length bound adjusted.
- Resumed once: R3's named test was missing on first return. Delivered as `FalconPolicyEnrichmentTests.cs:1081::R3_RejectionPathScrubsBodyAndAppliesPolicies`, asserting the raw credential token is ABSENT (not merely that the redacted marker is present) and that the call observably routed through the policy runner.

## W4 — tests and tripwires (complete)

- Migrated 18 compile errors from the manifest becoming a staged ledger (14 in `FalconTwoPhaseFindingsTests.cs`, 4 in `FalconConcurrencyHarness.cs`).
- Tripwires delivered: `R1_LedgerStaysUnderControlArtifactCeilingAt20kPages`, `R2_AbandonedPolicyGenerationsArePruned`, `R5_BothFlowsEmitCanonicallyIdenticalDevicePolicies`.
- Also present and passing: `PolicyStageFailureAfterTheFreeze_LeavesTheManifestIntact_AndTheNextLegCreatesNoNewGeneration` (the headline fix), `MidScrollServerError_ReanchorsFromTheWatermark_AndKeepsOneGenerationAcrossTheFault`, `StagedHostPageBytes_AreIndependentOfHowManyDistinctPoliciesTheVendorReturns`, `PublishedEnvelope_IsByteCompatibleWithTheCurrentParserContract`.

## Test evidence, measured

- Working tree, five change-relevant classes: **162 passed, 1 failed, 163 total, 14m 8s.**
- `FalconPolicyEnrichmentTests` alone: **51 passed, 0 failed, 128 ms.**
- BASELINE, measured in a throwaway git worktree at baseRef `b14d6d64` (dev HEAD, none of this task's changes): `FalconTwoPhaseFindingsTests` = **43 passed, 1 failed, 44 total, 14m 10s.**
- The single failure, `ResumedLegThatPublishesNothing_LeavesTheCoordinateUnchanged_SoTheNoProgressBudgetAccumulates`, FAILS AT BASEREF TOO. It is pre-existing and not attributable to this task. Runtime is also unchanged (14m 8s vs 14m 10s), so no performance regression.
- Independent finding, worth its own task: that test asserts a barren leg persists `_resilience.recovery.lastProgressCoordinate` byte-identically so the no-progress budget accumulates, and no checkpoint snapshot carries the key at all. If it holds in production and not only in the fixture, a repeatedly-barren run never trips its backstop. Same family as lessons.md#L-dae004f6.
- Two orchestrator measurement errors along the way, recorded so the numbers above are not over-trusted: an aggressive `/Blame:TestTimeout` aborted a merely slow suite twice and was reported as a deadlock, and the resulting partial pass-count was reported as a result. There is no hang; the class simply takes ~14 minutes.

## Process defects observed

- W2 wrote `Processing/Urls/FalconUrls.cs`, assigned to W1, while W1 was running. Intercepted before a duplicate landed; W2's version kept.
- W2 and W3 began coordinating directly on test scaffolding. Shut down; the finding they surfaced (`SeedCompletedPhase1` now has to declare freeze-only vs fully-traversed, a state that could not previously be represented) was routed to W4, the owner.
- R-id collision: the earlier `2026-08-26_1001_falcon-spotlight-transport-retry` task also used R1-R5 in this same test project, so `grep R4_` now matches eight tests across two unrelated tasks. The plan's premise that an R-id makes plan-to-code mapping a grep is broken for this repo.

## Verifier-1 repairs (all landed)

- **F1** — `FalconDocs/CollectorDocs/06-prevention-policy-contract.md` (+82) and `07-prevention-policy-observed-variants.md` (+33) updated. §6.1's "canonically identical" statement is still true and kept; what changed is that findings COMPOSES the envelope at emission rather than carrying the definition through staging.
- **F2** — batch degradation. `BisectionBudget` with `MaxAdditionalRequests = 32`; a non-2xx naming no ids bisects until ids are isolated. `VendorRejected` (isolated singleton) stays distinguishable from `RequestFailed` (abandoned at floor). Test `F2_OneRefusedAidInALargeBatch_CostsOnlyThatAid_NotThePage`.
- **A10 / item 4** — the definitions sweep had a URL builder and no caller; the operator confirmed it should be wired. Now called at `FalconPreventionPolicyClient.cs:105`, paginated, page-bounded. `precedence` and the default indicator flow through VERBATIM rather than being extracted as named fields — deliberately, because the parser's existing null carries a comment refusing to guess `is_default` from the `platform_default` name, and substituting a new guess would be worse than the null.

## Code-review-1 dispositions

- **B0 (blocker) — FIXED.** The branch did not pass its own suite: six failures, five caused by the operator's `EnablePreventionPolicyEnrichment` default flip leaving invalidated tests untouched. Orchestrator error: I had filtered every test run to change-relevant classes, so I never saw four of them. `FalconCollectorConfigurationBuilderTests` renamed to `..._DefaultsToDisabled` and inverted; the four `FalconCorrelatedFindingsTests.Policy_*` tests now enable enrichment EXPLICITLY rather than relying on a shipped default, which is the durable fix. Two new tests pin the disabled contract: `Policy_WhenEnrichmentIsDisabled_EveryHostCarriesAVisibleDisabledEnvelope` and `ProcessAsync_Assets_WhenEnrichmentDisabled_AttachesDisabledEnvelope_AndCallsNoPolicyEndpoint`.
- **B1 + M3 — FIXED.** A declined or unaccounted AID was emitted as `collection_status: "complete"` with `prevention: null`, byte-identical to a host the vendor CONFIRMED has no policy. New `FalconDevicePoliciesEnvelope.Unresolved()` emits `collection_status: "unavailable"`; the word is reused rather than invented, and the `(status, prevention)` pair disambiguates the two senses — `unavailable` + null prevention means no answer, whereas an unavailable DEFINITION is `partial` with a populated block. Parser tolerance confirmed: `crowdstrike_policy_projection.py` filters on `_external_id` non-null, so an `unavailable` row drops out exactly as `disabled` does. Tests `M3_RejectedAidAndUnmanagedAid_EmitDifferentCollectionStatus`, `B1_TwoRefusedAidsInOneBatch_AreBothIsolatedAndTheRestOfThePageSurvives`.
- **M2 — FIXED.** `Compose` converted a `Resolved` staged edge into `definition_status: "not_found"` on a cache miss — a stated negative built on missing evidence, whose commonest cause is a resumed leg whose `pgen` read failed. Now a `Resolved` edge with a missing definition emits `Unavailable`/`partial`; only an edge that itself recorded `NotFound` states a not-found.
- **M5 — FIXED.** `FalconStagedPlaneCorruptException` arm added, `FALCON_STAGED_PLANE_CORRUPT`, non-retryable.
- **m6 — FIXED.** Sweep short-page terminator now counts pre-dedup received rows (a page of all-duplicates previously read as `received=0`, i.e. a short page, terminating early), plus a hard page bound so a vendor that never returns a short page cannot spin forever.
- **m7 — FIXED.** Staged codecs throw `FalconStagedPlaneCorruptException` at four sites instead of coercing a non-object payload to null.
- **m12 — ADDRESSED with stated reasoning** at `FalconStagingArea.cs:187-190`: `FromStream` bypasses the 8 MiB in-memory guard, and the comment records that the edge-only page wins on size rather than on the guard.
- **M4 — FIXED, and my earlier note recording it as an accepted risk was WRONG.** The resume-skip landed at `FalconHostSpooler.cs:437-455` ("RESUME, DON'T RESTART ... Without this the stage was one non-resumable unit"), written after code-reviewer-1.md and before I wrote the note that misdescribed it. I had grepped for symbols that do not exist in the implementation and concluded from their absence that the fix was missing, then recorded an accepted risk and told the operator so. Corrected here; verifier-2 §V2-1 caught it. Residual on M4 is narrower than the original finding: no dedicated test, and no explicit progress signal to the recovery budget.
- **Minors m8, m9, m10, m11, m13 and m14's second half — FIXED but undisposed in my notes until now** (`FalconPhase1Manifest.cs:316,330,415`, `FalconFindingsFlow.cs:684-686`, `FalconHostSpooler.cs:382-404,429`, `FalconStagingArea.cs:650`). Only m14's first half is genuinely open.
