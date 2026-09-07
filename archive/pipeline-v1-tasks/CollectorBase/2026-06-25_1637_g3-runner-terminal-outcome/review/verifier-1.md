# Verifier-1 — Group 3 runner terminal-outcome (A-M1 + A-M2)

Independent, adversarial. Ran the suite myself; inspected http.package DLL metadata to confirm field semantics.

## Criterion 1 — build clean — PASS
`dotnet test CollectorBase.slnx` restored + built all projects with 0 errors (only NU1507 source-mapping warnings, pre-existing/unrelated).

## Criterion 2 — suite ≥81, 0 failed, incl. the two new tests — PASS
- `Passed! - Failed: 0, Passed: 81, Skipped: 0, Total: 81` (ran myself).
- `LongServerSuggestedDelay_Externalized_NotSlept` present (line 1241) and green.
- `PublishFailure_ZeroEmitted_IsTransientFailure` present (line 1257) and green.

## Criterion 3 — no regression, esp. 1s-Retry-After in-process — PASS
- `ProcessAsync_Findings_HonorsRetryAfterInProcess_Emits3Records` (line 729) green: asserts 3 records AND that the page-2 query was sent ≥2× (429 + in-process retry). FalconYaml has `max_in_process_delay_seconds: 60`; mock returns Retry-After **1s** (line 358). 1s ≤ 60s floor → honored in-process. Consistent with the A-M1 mapping; no regression.

## A-M1 mapping correctness — PASS
`CollectorExecutorSessionFactory.cs:33-35`:
- `ExternalizeServerSuggestedDelays = ServerDelayStrategy != "disabled"`
- `MinExternalizedServerSuggestedDelay = TimeSpan.FromSeconds(Max(0, MaxInProcessDelaySeconds))` — the externalize **lower bound** (threshold)
- `MaxServerSuggestedDelay = TimeSpan.FromHours(24)` — clamp ceiling, generous, > threshold

Field semantics independently confirmed against `Cymulate.Http.Package.DefensiveToolkit` 2.0.2 metadata: all three fields exist (`MinExternalizedServerSuggestedDelay`, `MaxServerSuggestedDelay`, `ExternalizeServerSuggestedDelays`), and clamp vs externalize are *distinct* mechanisms (separate telemetry: `RetryServerSuggestedDelayClamped` vs `RetryServerSuggestedDelayExternalized`, plus a `ServerSuggestedDelayGate`/`ServerSuggestedDelayExternalizer`). This corroborates "Min = trigger, Max = ceiling". The earlier mis-map (Max = cap) regressed 4 tests; corrected version is right. Behavioral proof: LongWait (cap 1, suggested 120s) externalizes; Falcon (cap 60, suggested 1s) stays in-process. Both pass.

## Criterion 3b — LongServerSuggestedDelay test is real, not trivial — PASS
- Mock `/longwait/query` returns 429 with `RetryAfter = 120s` (line 263); profile cap = 1s (line 2782).
- Asserts `Status == PartialWaitRequired` AND `sw.Elapsed < 10s`. The timing assertion is the anti-trivial guard: a wrong (in-process-sleep) implementation would block ~120s and fail. Not trivially passing — it exercises the real session pipeline throwing `ServerSuggestedRetryDelayException`, caught at `Runner.cs:204` → `PartialResult`.

## A-M2 — publish-loop catch / ZeroEmitted — PASS (with disclosed limitation, see verdict)
`CollectorExecutorRunner.cs:341-371`. Two failure surfaces handled:
- non-success `DataPublishResult` (line 357-360) → `emitted>0 ? PartialSuccess : TransientFailure(PUBLISH_FAILED)`.
- thrown egress exception (line 365 `catch (Exception ex) when (ex is not OperationCanceledException)`) → same split.
`FailOnNthStreamPublisher(1)` returns a non-success result on the first publish; `PublishFailure_ZeroEmitted_IsTransientFailure` asserts `Status != Success` and `ErrorCode == "PUBLISH_FAILED"`. Execution states this surfaced as `ADAPTER_EXCEPTION` before the fix — the test pins the catch in the right place with the right code. Verified green.

## Criterion 5 — broad catch does not over-swallow — PASS
- `OperationCanceledException` is explicitly excluded → cancellation still propagates to the outer `catch (OperationCanceledException)` (line 211) → `CancelledResult`. Correct.
- The externalization invariant is NOT masked: `ServerSuggestedRetryDelayException` is thrown by `http.Send*` (lines 264/273), which sit **outside** the publish try block; the publish loop only calls `CollectorNdjsonPublisher`. Confirmed the publisher throws only egress/IO/argument exceptions (CollectorNdjsonPublisher.cs), never the server-suggested-delay exception. So `Runner.cs:204` is unreachable-from-publish and intact.
- Residual (minor, not a fail): the broad catch would also reclassify an *unexpected* programming exception inside the small publish-loop body (slice → publish → counter inc) as PUBLISH_FAILED/partial rather than ADAPTER_EXCEPTION. Blast radius is narrow (tiny loop body); acceptable.

## Verdict on the A-M2 coverage limitation — ACCEPTABLE (honest disclosure, not a gap)
The emitted>0 partial branch of the publish catch is not separately harness-triggerable because `ThrottlingOptions.Resolve` won't split a small page into ≥2 slices, so a single step-call can't both emit and then fail a later slice without multi-MB data or a Resolve override. The branch is the identical `emitted>0 ? PartialSuccess : <hard-fail>` shape used at four other terminal exits in the same file (fail_if 280-282, publish-result 357-360, JSON/XML 391-393, HTTP-failure path via `TerminalResult`/`ExecuteDecisionAsync` 779-789), all reachable and covered by other tests. The zero-emitted side is directly tested. This is covered-by-pattern + zero-emitted-tested, explicitly documented in `execution_notes.md:32-38`. The disclosure is accurate (I confirmed the pattern reuse and the byte-budget constraint). Deferring a multi-MB or Resolve-override test is a proportional call, not a defect. Not a FAIL.

## Criterion 4 (task-dir note) — PASS
`execution_notes.md` documents the cap/threshold mapping (incl. the mid-flight correction with the source-level reason), the partial-success-wins alignment, and the test limitation. Note: this was filed in `execution_notes.md`, not a separate note file — content requirement met.

## FINAL VERDICT: PASS
All four success criteria met. 81/81 green including both new tests and the in-process regression guard. A-M1 mapping is correct and independently corroborated against http.package metadata + runtime behavior. A-M2 catch is correctly placed, scoped (cancellation excluded), and does not mask externalization. The single uncovered partial branch is an honestly-disclosed, proportional, covered-by-pattern limitation — accepted.
