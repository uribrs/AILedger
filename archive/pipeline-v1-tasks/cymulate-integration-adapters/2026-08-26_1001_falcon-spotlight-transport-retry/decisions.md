# Decisions

- **Fix Falcon-side, not Infra-side.** Infra already handles transport failures generically in two
  places and both terminate; correcting that is major surgery this incident does not justify.
- **Mimic TenableIo's `TenableIoChunkRetry`**, not its exhaustion policy. Same in-flow retry, opposite
  failure semantics: TenableIo skips a chunk and escalates on a threshold, Falcon must defer.
- **Retry lives inside `FalconSpotlightBatchPump.FetchAsync`** (`Flows/Findings/Correlated/FalconSpotlightBatchPump.cs:280`)
  — the pure, materialising fetch, upstream of any publish or page mint.
- **Retry predicate** = `HttpTransportFailureClassifier.IsRetryableTransportFailure(ex) || .IsCircuitBreakerException(ex)`.
- **Exhaustion throws a new Falcon exception under `Exceptions/`**, classified retryable by
  `Processing/FalconFlowExceptionClassifier.cs`, routed by
  `FalconResilienceStrategyFactory.CreateRetryableDecision` to the SAME unbudgeted flat 5-minute
  `RequestDeferredRecovery` the 5xx branch uses, under its own `Reason` (`falcon-transport-failure`)
  for observability. Justification: 102 `falcon-server-error` deferrals in 14 days were all absorbed
  without killing a run, on the same endpoint and edge.
- **Accepted tradeoff:** a backing-off batch keeps its semaphore slot (held from before the scroll
  until after publish, by design), so at degree 6 with k batches retrying, effective concurrency is
  6-k. Deliberate. Must be stated in a code comment and in the docs, not left to be rediscovered.
- **Proceeding on unverified:** the EOF originates on the Spotlight response-body read inside
  `FetchAsync`. Inferred from the concurrent teardown of that stream 17ms before the publish; the
  throw site itself was never logged with a stack. If wrong — if it originates in S3 emission or
  elsewhere in the consumer — retrying `FetchAsync` does not fix the observed failure. See A6.
- **Proceeding on unverified:** deferral from Phase 2 reaches
  `OnTerminalSnapshotWithoutPublishedPage` (`FalconFindingsFlow.cs:442`) and does not `AdvancePage`.
  Read from source; never exercised for a transport fault. If wrong, a deferred transport failure
  mints a page over an unpublished batch. See A7.

## Mid-execution (orchestrator, 2026-08-26, under operator leave to mitigate found holes)

- **The wrapper must carry NO inner exception.** `RetryableTransportFailurePolicy` (chain position 4, ahead of
  Falcon's classifier at 6) calls `HttpTransportFailureClassifier.IsRetryableTransportFailure`, which walks
  `InnerException` **ten levels deep** and matches any `IOException`/`HttpRequestException` carrying a transport
  marker (`HttpTransportFailureClassifier.cs:15-25`). Throwing the new type with the EOF as its inner would
  therefore have been claimed by that policy and published terminally — the classifier arm would never have run
  and the change would have shipped as a **no-op**. The original's type and message are flattened into the
  message and the original is logged with its stack at the throw site instead. Guarded by a dedicated regression
  test, because the safety is invisible in the code.
- **The wrap is the fix; the retry is the optimisation.** The wrap therefore happens even at
  `SpotlightTransportMaxRetries = 0`. Otherwise setting that knob to 0 would not disable retrying, it would
  silently restore the outage.
- **R5 upgraded from `accept` to a partial mitigation.** It was accepted on the assumption that covering the
  staged-page read and the multipart publish meant *retrying* them. It does not: wrapping alone converts a
  terminal failure into a resumable deferral. A single catch on the Phase 2 consumer loop
  (`FalconFindingsFlow.cs`, after the existing `OperationCanceledException` catch) now re-throws any retryable
  transport failure as the Falcon type. Re-throw only, no retry — re-reading a staged page mid-enumeration is a
  different problem from re-issuing a self-contained scroll, and the deferral already redoes the whole
  unpublished batch correctly.
- **Type renamed `FalconSpotlightTransportFailureException` -> `FalconTransportFailureException`.** With two
  throw sites (Spotlight scroll, and Phase 2 staged-page/publish) the "Spotlight" qualifier had stopped being
  true. Renamed while the workers were still mid-flight rather than shipping a misleading name.
- **Degree 1's memory-incident rollback is now weaker, and says so.** Covering degree 1 means it materialises a
  batch like every other degree, so it no longer restores the pre-concurrency memory profile. The pump's
  class-level doc claimed it did; that paragraph is being rewritten rather than softened.

## OPEN — to revisit after this change lands (operator, 2026-08-26)

**Was covering degree 1 worth the memory-incident rollback?** The degree-1 branch in
`FalconSpotlightBatchPump.RunAsync` is fully reversible — one branch, and `PumpSequentiallyAsync` reverts to
handing the publisher the lazy scroll.

What has changed since the decision was taken, and why it may change the answer: **the R5 flow-level guard did
not exist when degree 1 was decided.** At that moment the choice was framed as "cover degree 1, or leave it
dying terminally on an EOF". That framing is now wrong. The catch on the Phase 2 consumer loop
(`FalconFindingsFlow.cs`) wraps *any* retryable transport failure escaping that loop — including one from a
lazy degree-1 scroll — so degree 1 would **defer, not die**, even with the branch reverted. It would lose the
~2-second in-flow recovery and pay a 5-minute deferral instead; it would not lose the outage fix.

So the trade to weigh is no longer "protection vs memory". It is "sub-second recovery vs the lean rollback
profile", on a path that is not the default (degree 6 is).

Must be established before reverting — do not revert on the argument above alone:
1. Whether a mid-emission failure on the lazy path can leave a PARTIAL published object under the batch's key.
   In-flow retry was ruled out there because the publisher has already consumed records; the same fact makes
   the deferral safe only if the failed emission leaves nothing behind (multipart aborted, no completed
   object). If a partial object can survive, resume republishes over it and the lazy path is unsafe at any
   recovery grade — which would settle the question the other way.
2. How often degree 1 is actually configured in production, and whether the memory-incident rollback has ever
   been exercised.
3. The real memory delta: recon cites 90-125 MB per materialised batch at AidBatchSize 10, against partial
   per-host accumulators on the lazy path.

## RESOLVED — exhaustion behaviour (operator, 2026-08-26)

**A transport failure is treated exactly as a 5xx: unbudgeted, flat 5 minutes, unlimited.** No skip tier, no
deferral budget. Ruled after the three-tier alternative (retry -> defer -> skip-the-aids-after-N) was costed:
skipping and carrying on needs a per-batch deferral count in the checkpoint and a coordinate that advances past
a dead batch without publishing, which is roughly the size of the original change, for a backstop expected
never to fire. Patch if it turns out to be needed.

The governing constraint, in the operator's words: *"we're already deferring — we mustn't fail on this."* A run
holding an intact checkpoint waits; it does not die. That the wait is unbounded is the accepted consequence,
and it is the same consequence Falcon already accepts for every 5xx and every open circuit.

No code change followed this ruling — `FalconResilienceStrategyFactory` already routes both conditions through
the same `CreateUnbudgetedFlatRecoveryBackoff()` plan with `UseRecoveryBudget: false`, differing only in the
`Reason` label that keeps them separable in logs.

This closes the item raised earlier in this file as an open concern about unbounded deferral.
