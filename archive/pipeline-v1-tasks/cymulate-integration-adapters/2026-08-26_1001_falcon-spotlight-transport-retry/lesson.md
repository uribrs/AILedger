# Lessons — 2026-08-26_1001_falcon-spotlight-transport-retry (2026-08-26)

## L-a50e6941 — A5
belief:  A vendor Retry-After or a 429 status is observable to Falcon's collector code when a Spotlight request fails.
counter: FalconHttpFailureClassifier constructs AdapterHttpRequestFailedException with six arguments and omits retryAfter, so it is always null on the classified path; and a mid-body stream drop is a bare IOException with no status and no headers, not that type at all. Two of the three rungs copied from TenableIo's delay ladder could never have fired.
source:  FalconHttpFailureClassifier.cs:47-54; AdapterHttpRequestFailedException.cs:28 (verifier, 2026-08-26) (verifier, 2026-08-26)
verify:  grep -rn -i 'retryafter' src/Cymulate.Integration.Adapters/Collectors/FalconCollector/ | grep -v '//'   (no hits = still unavailable)
do not:  do not port a vendor-backpressure delay ladder into a collector without checking that collector can actually observe the signals each rung reads.

## L-dae004f6 — A7
belief:  The Falcon findings flow reaches OnTerminalSnapshotWithoutPublishedPage when a Phase 2 deferral snapshots its position without advancing the page.
counter: That method is declared on FalconAssetsCheckpointWriter and is called only from the ASSETS scroll runner. The findings flow never invokes it; the FalconFindingsFlow reference to it is a doc-comment cross-reference, not a call site. The conclusion it was cited for is true by a different route: the findings path's only AdvancePage is inside checkpointWriter.OnBatchPublished, which runs after a successful publish, and both throw paths escape before it.
source:  Flows/Assets/FalconAssetsCheckpointWriter.cs:114; Flows/Assets/FalconAssetsScrollRunner.cs:339,365; FalconFindingsFlow.cs:329 (recon + verifier, 2026-08-26) (recon, 2026-08-26)
verify:  grep -rn 'OnTerminalSnapshotWithoutPublishedPage' src/Cymulate.Integration.Adapters/Collectors/FalconCollector/   (call sites must all be under Flows/Assets/)
do not:  do not assume the findings flow shares the assets flow's checkpoint-writer paths; cite the call site, not a doc-comment mention.

## L-9fa66041 — R5
belief:  An open circuit breaker should be retried in-flow alongside transport faults, because it will half-open on its own.
counter: Falcon's break duration is 300s and the in-flow ladder is ~26s at the shipped defaults, so every attempt is certain to fail. Retrying it spent the whole stall budget, held up to degree materialised record sets resident, issued doomed vendor calls and then took the deferral anyway - strictly worse than deferring immediately. Claiming it also cost the falcon-server-error label, because chain position 4 declines circuit failures only while transientTransportBackoff is null. The clause was removed after independent code review.
source:  FalconCollectorConfiguration.cs:58-61 (break duration) vs the ladder at FalconSpotlightBatchPump.ComputeTransportRetryDelay (code-reviewer, 2026-08-26) (verifier, 2026-08-26)
verify:  grep -n 'IsCircuitBreakerException' src/Cymulate.Integration.Adapters/Collectors/FalconCollector/Flows/Findings/Correlated/FalconSpotlightBatchPump.cs   (no hit in the retry predicate = still correct)
do not:  do not add a fault to an in-flow retry predicate without comparing its expected clear time against the ladder's total length.

## L-40bca6e4 — R4
belief:  Clamping a retry ladder's configuration inputs bounds how long the ladder can stall the pipeline.
counter: Growth is exponential, so clamping to 10 attempts at a 30s base still permitted a single uncapped delay of 30 x 3^9 which is about 6.8 days, and roughly 10 days total, while holding a semaphore slot and blocking one consumer's publish and checkpoint for the whole run. The config remarks and the clamp test both asserted the clamp WAS the mitigation. Fixed by a per-attempt cap plus a cumulative-stall bound enforced in the loop rather than in the config builder.
source:  review/verifier-1.md finding 2; FalconSpotlightBatchPump.MaxTransportRetryDelay / MaxTotalTransportRetryStall (verifier, 2026-08-26) (verifier, 2026-08-26)
verify:  dotnet test --filter 'FullyQualifiedName~R4_WorstLegalLadder_NeverOutlastsTheDeferralItAvoids'
do not:  do not treat an input clamp as a bound on an exponential output; bound the output where it is produced.

## L-9ad5a354 — A4
belief:  Falcon's per-batch memory cost, and the degree-1 rollback's memory profile, are known well enough to trade against.
counter: Never measured. The only datum remains the 90-125 MB observed at AidBatchSize 10 on 2026-08-18, against a 35-50 MB figure that was a config comment's TARGET. This change WIDENED the exposure: degree 1 now materialises a batch too, so the documented memory-incident rollback no longer restores the pre-concurrency profile.
source:  review/verifier-2.md A4; FalconCollectorConfiguration.cs degree-1 remarks (verifier, 2026-08-26) (verifier, 2026-08-26)
verify:  observe container RSS during a heavy-tenant findings run at degree 1 and at degree 6, and compare against the HPA trigger
do not:  do not quote the degree-1 rollback as restoring the old memory profile, and do not cite the per-batch figure as measured.

## L-d8b7fc3a — A6
belief:  The production EOF that killed correlation 6a85ca2038f164746a562020 originated on the Spotlight response-body read inside FetchAsync.
counter: Never established. The throw site was never logged with a stack; the origin was inferred from the concurrent teardown of that stream 17ms before the error publish. Materially de-risked rather than resolved: the Phase 2 consumer-loop guard now defers any retryable transport fault raised anywhere in that loop, so the outage is fixed under either branch - but only within Phase 2. Phase 1 and the assets flow remain uncovered.
source:  review/verifier-2.md A6; admin-traces-production-integration-service-bus, no stack captured (verifier, 2026-08-26) (verifier, 2026-08-26)
verify:  es_esql over admin-traces-production-integration-service-bus for a FalconTransportFailureException message, which now names the staged page and batch index at the throw site
do not:  do not treat the EOF's origin as known; the fix is origin-agnostic within Phase 2 and absent outside it.
