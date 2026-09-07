# Execution Notes — failure-policy seam (reactive resilience)

DONE + VERIFIED (build green; tests 6/6):
- New SEAM: CollectorExecutor/Resilience (IFailureStrategy + FailureContext + FailureResolution/FailureAction)
  = the executor's EXPRESSION layer. Shared/Resilience stays the infra. Strategy impls live in
  Strategies/Resilience (the strategies home).
- Strategies: DefaultFailureStrategy ("default") reproduces the runner's prior status→outcome mapping
  (behavior-preserving). CursorExpiryFailureStrategy ("cursor_expiry") is REACTIVE: on a vendor outcome
  whose status/body signals cursor expiry AND a watermark floor exists → ResetAndContinue; else falls
  through to default.
- Registry extended (failure strategies); profile selects via top-level `resilience.failure_strategy`
  (default "default"). Fail-closed resolution.
- Runner: per-page inner try routes a caught AdapterHttpRequestFailedException through the resolved
  failure strategy → acts on the decision vocabulary: ResetAndContinue (drop cursor, continue from
  watermark), Defer (PartialResult), Fail (today's mapping + partial-success invariant). Outer try keeps
  ServerSuggestedRetryDelayException (defer) + cancellation unchanged.
- cursor_watermark is now REACTIVE, not blind: a real expired-cursor error triggers the reset (the
  depth-cap remains the optional proactive complement).

TESTS:
- CursorExpiry_ReactiveReset_FromVendorError_CollectsAll: vendor returns 403 "expired cursor" on the
  aged-cursor request; the policy resets to watermark; all 4 records collected; asserts BOTH the failed
  after= request AND the post-reset wm= request fired (reactive, not a counter).
- 5 prior tests stay green (behavior preserved: default failure mapping == old inline mapping).

VALIDATES the user's direction: quirks now REACT to observed vendor behavior (expressed as named
failure strategies) instead of being blindly applied; a vendor's policy = a registered strategy its
YAML names; zero runner/orchestrator edits.

HONEST SCOPE / next refinement: this is the executor's failure-EXPRESSION layer. It does NOT yet
DELEGATE to Shared's AdapterResilienceStrategy / IAdapterFailurePolicy chain (the infra) — strategies
currently classify directly. Wiring them to delegate to the Shared chain (ordered policy composition)
+ landing the progress-anchored RecoveryBudget behind DeferUnbudgeted is the next segment. The seam +
decision vocabulary are the integration point for that.

FILES: CollectorExecutor/Resilience/FailureSeam.cs; Strategies/Resilience/{Default,CursorExpiry}FailureStrategy.cs
(+register); Seams/Composition registry (+failure); Profile (resilience.failure_strategy);
Execution/CollectorExecutorRunner.cs (resolve + per-page reactive routing); Tests (+reactive test/mock/profile).
