# Code Review — Falcon Spotlight in-flow transport retry

**Reviewer:** code-reviewer-1 (independent pass)
**Scope:** uncommitted changes on `fix/falcon-spotlight-transport-retry` vs `77ec50cb`, plus the untracked
`Collectors/FalconCollector/Exceptions/FalconTransportFailureException.cs`.

## Calibration

| Axis | Classification |
|---|---|
| Change type | Feature logic inside a long-running collector; touches retry, concurrency, checkpoint routing and failure classification |
| Risk level | **High** — retry loops, bounded concurrency, cancellation, deferral/idempotency semantics, 25h-scale runs |
| Depth applied | Failure semantics, concurrency, resource lifetime across retry, scale/memory behaviour, observability, test strength |

**Tests run** (filtered per constraint): `FalconSpotlightConcurrencyTests`, `FalconResilienceStrategyTests`,
`FalconFlowExceptionClassifierTests`, `FalconCollectorConfigurationBuilderTests` — **85 passed, 0 failed, 19s.**

## Verdict

The core mechanism is sound and the hard parts are right. The three things most likely to be wrong in a change
like this — retry safety of shared state, stream disposal across a re-issue, and cancellation being mistaken for
a retryable fault — are all correct, and I verified each against source rather than against the comments.

One finding is worth fixing before merge: the retry predicate includes circuit-breaker exceptions, and at the
shipped configuration those retries cannot possibly succeed. The rest is small.

---

## Findings

### 1. Retrying an open circuit is guaranteed futile, and violates the change's own stall invariant — **Major**

`FalconSpotlightBatchPump.cs:435-437`

```csharp
private static bool IsRetryableTransportFailure(Exception ex)
    => HttpTransportFailureClassifier.IsRetryableTransportFailure(ex)
       || HttpTransportFailureClassifier.IsCircuitBreakerException(ex);
```

**Problem.** Falcon's circuit breaker default is `BreakDuration = TimeSpan.FromSeconds(300)`
(`FalconCollectorConfiguration.cs:58-61`). The in-flow ladder at the shipped defaults is ~2s + 6s + 18s ≈ **26s
total**. A `BrokenCircuitException` therefore burns the entire retry budget against a circuit that is certain to
still be open on every attempt, then defers anyway. The doc comment above the predicate asserts the opposite:

> "an open circuit that will half-open on its own"

It will not, inside this ladder. Even at the maximum legal ladder (10 × 30s = 300s) it only just reaches the
break duration.

**Why this is more than waste.** `MaxTransportRetryDelay` exists, by its own remarks, so that "the in-flow path
can never be a worse choice than deferring." For circuit-open, the in-flow path is *strictly* worse than
deferring: it adds ~26s of head-of-line publisher stall, holds up to `degree` materialised record sets resident
for that long (per the change's own memory model, ~350–500 MB at degree 6), issues three doomed vendor calls,
and then takes exactly the 5-minute deferral it would have taken immediately. Before this change, an open
circuit in the Spotlight scroll deferred at once on the same flat plan.

**Recommended fix (local patch, one line).** Drop the circuit-breaker clause from the *pump's* predicate:

```csharp
private static bool IsRetryableTransportFailure(Exception ex)
    => HttpTransportFailureClassifier.IsRetryableTransportFailure(ex);
```

An open circuit then escapes raw, `RetryableTransportFailurePolicy` declines it (it returns `null` for circuit
failures while `transientTransportBackoff` is null — verified in the Infra source), and Falcon's classifier
routes it to `FALCON_SERVER_ERROR` / `falcon-server-error` on the same unbudgeted flat 5m plan. That also
restores the alert label that `02-decision-making.md` currently documents as being lost.

Note the contrast with the precedent this change otherwise mirrors: `TenableIoVulnPhase.cs:259` explicitly
*branches* on `IsCircuitBreakerException` and waits `config.CircuitBreakerBreakDuration`. That option is not
open here — 300s in-flow would blow the stall invariant — which is why declining to retry is the right answer,
not a longer delay.

Not a refactor. One clause, plus updating the predicate's summary comment.

---

### 2. Jitter is applied after the cap, so the stated 5-minute bound is not the real bound — **Minor**

`FalconSpotlightBatchPump.cs:485-492`

```csharp
double seconds = Math.Min(baseDelaySeconds * Math.Pow(3, attempt - 1), MaxTransportRetryDelay.TotalSeconds);
return TimeSpan.FromSeconds(seconds + Random.Shared.NextDouble());
```

The remarks on `MaxTransportRetryDelay` state the invariant as "the worst legal configuration stalls at most
`maxRetries × 30s` = 5 minutes, which is the deferral's own wait." With jitter added *after* `Math.Min`, the
worst legal ladder is up to 310s — i.e. marginally longer than the deferral it is justified against. The
invariant is the whole justification for the in-flow path existing, so it is worth having it be exactly true.

Compounding it: `R4_WorstLegalLadder_NeverOutlastsTheDeferralItAvoids` asserts
`≤ 300 + maxLegalRetries` seconds. That is the implementation's actual behaviour restated, not the invariant the
test's own name and docstring claim. The test cannot fail for the reason it exists.

**Fix.** Move the jitter inside the clamp — `Math.Min(base * 3^(n-1) + jitter, 30)` — and tighten the test to
`≤ TimeSpan.FromMinutes(5).TotalSeconds` with no allowance. Local patch; no behavioural cost.

---

### 3. The class advertises a bound it explicitly declines to enforce — **Minor**

`FalconSpotlightBatchPump.cs:112-124` (ctor) and `:350` (`for (int attempt = 1; ; attempt++)`)

The constructor guards `transportMaxRetries >= 0` and comments that this is "an assertion against a future
caller that skips [the builder], not a second clamp." But every stall guarantee the class documents — including
the `MaxTransportRetryDelay` justification — is phrased against "the worst *legal* configuration", where legality
is defined entirely by `FalconCollectorConfigurationBuilder`. A caller passing `int.MaxValue` gets an unbounded
loop at 30s per attempt that never gives up and never lets the publisher advance. The unconditional `for` has no
other exit.

I recognise the tension with the repo's single-authority clamping convention (`FalconFlowRunPreparer.cs:77-84`
is explicit about not re-clamping), and I am not suggesting a second clamp. The cleaner reconciliation is to
make the invariant *local and clamp-independent*: track cumulative delay and bail when it reaches the deferral
window.

```csharp
// invariant: the in-flow ladder never outlasts the deferral it is avoiding
if (totalDelay >= MaxTotalTransportRetryStall) throw ExhaustedTransportRetries(batch, attempt, ex);
```

That makes the documented property true regardless of what any caller passes, and lets `MaxTransportRetryDelay`
go back to being a per-attempt smoothing detail. Deferrable — no current caller can trigger it.

---

### 4. `R5`'s docstring claims an ordering guarantee that cannot be violated — **Minor** (test quality)

`FalconSpotlightConcurrencyTests.cs`, `R5_StagedPageReadDrop_DefersAsFalconTransportFailure_RatherThanTerminating`

> "This test also pins the guard BELOW the `OperationCanceledException` catch — a drop is not a cancellation and
> must not be reported as one."

It does not, and cannot. `HttpTransportFailureClassifier.IsRetryableTransportFailure` returns `false` for
`OperationCanceledException` at the top of the method, so the two catches at `FalconFindingsFlow.cs:357` and
`:365` are disjoint by construction; swapping them changes nothing. The accompanying assertion —
`capture.Errors.Should().NotContain(e => e.ErrorMessage.Contains("cancel", ...))` — is an absence check that
also passes when `capture.Errors` is empty, which on a deferral it plausibly is.

**Fix.** Delete the claim and the assertion, or replace them with something that has teeth (assert the published
error code is `FALCON_TRANSPORT_FAILURE`, which the strategy tests already do properly). Keeping a decorative
assertion under a confident docstring is worse than having neither — the next reader will trust it.

Related, same test: `published.Should().BeInRange(1, hostCount - 1)` is loose for a fault that is fully
deterministic at degree 1 (`ReadFault` fires on read attempt 4, reads are serial). Pinning the exact count would
catch a change in read ordering that the range silently absorbs. And the trailing `_ = result;` is dead — either
assert on `result` or drop the variable.

---

### 5. Pump predicate and flow guard disagree on circuit breakers, correctly, but silently — **Nit**

The pump ORs in `IsCircuitBreakerException` (`:437`); the flow's Phase 2 guard
(`FalconFindingsFlow.cs:365`) does not. Given finding 1 I'd resolve this by making the pump match the flow, but
either way the asymmetry deserves one line of comment: the flow *must not* wrap circuit failures, because
position 4 declines them and letting them through is what preserves the `falcon-server-error` branch. Someone
harmonising these two predicates "for consistency" would steal that branch with no failing test outside
`FalconResilienceStrategyTests`.

---

### 6. Naming and prose — **Nit**

- `ExhaustedTransportRetries` (`:395`) reads as a predicate or a past-tense event but is an exception *factory*.
  `CreateExhaustedTransportRetriesFailure`, or similar, would say what it returns. The
  return-don't-throw shape itself is good — it keeps the `throw` visible at the call site.
- `FetchAsync`'s counter comment (`:359-362`) ends mid-thought: "make a number the doc above forbids becoming a
  position drift further from one." Unparseable; the intent (a fetched-batch count must not be read as a
  position) is worth stating in one clean sentence.

---

## Verified as correct — no action

These are the failure modes I went looking for. Recording them so a later reviewer does not re-derive them.

- **Retry safety of shared state.** Each attempt re-seeds accumulators (`SeedAccumulators` is called inside
  `ScrollBatchAsync`, `:421`) and allocates fresh `stats`/`records`. The only object shared across attempts is
  each host's `JsonObject`, and `FalconCorrelatedRecord.Build` writes `host.DeepClone()` into the record — the
  host node is never re-parented, so a second attempt is legal. The doc comment's claim here is accurate,
  and it is the claim the whole change rests on.
- **Stream and enumerator lifetime across a re-issue.** `FalconSpotlightBatchScroller` wraps each response in
  `await using (streamed.ConfigureAwait(false))`, and the `await foreach` in `ScrollBatchAsync` disposes the
  iterator on the exception path, so the failed attempt's response is released before the next one is issued. No
  connection leak across the ladder.
- **Scroller statelessness per call.** `seenFindingIds`, `updatedFloor`, `scrollFilter` and `after` are all
  locals of `EmitBatchRecordsAsync`, so a re-issue genuinely restarts from `windowStartUtc`. No carry-over.
- **Cancellation is not misclassified as retryable.** `IsRetryableTransportFailure` short-circuits on
  `OperationCanceledException`, so a co-operative stop mid-scroll is never swallowed by the retry catch. The
  `Task.Delay(delay, cancellationToken)` sits inside the `catch`, not the `try`, so its own OCE propagates
  rather than re-entering the loop. Stop latency during a backoff is one token observation, not a full rung.
- **Retry loop termination.** `attempt > _transportMaxRetries` with a monotonically increasing `attempt` and a
  finite budget; `maxRetries = 0` yields exactly one attempt and then the wrap. Both boundaries are tested.
- **Semaphore/channel invariants under the new delay.** The slot is taken before the fetch starts and released
  only after the consumer's `yield return` returns, so `channel depth ≤ slots held ≤ degree` still holds and the
  bounded write still cannot block. A sleeping fetch holds its slot — that is the documented, intended cost. On
  the teardown path a slot is not released, but `producerCts.CancelAsync()` + `DrainAsync` run in `finally` and
  the pump is being torn down, so it is unobservable.
- **The wrap-without-inner-exception invariant.** `RetryableTransportFailurePolicy`'s predicate only message-
  matches chain members that are `IOException`/`HttpRequestException`, and `FalconTransportFailureException`
  derives from plain `Exception` with no inner-exception constructor. `Create_WhenTransportFailureCarriesTransport
  MarkerInMessage_StillReachesTheMappedPolicy` is a real tripwire for a real, invisible regression — that test
  earns its place.
- **Classifier arm ordering.** The new arm sits above the guarded base-`Exception` arm that would otherwise
  shadow it, and `TransportFailureArmDoesNotShadowTheCircuitBreakerArm` asserts both directions.

## Accepted tradeoffs

- **Degree 1 now materialises a whole batch.** This is a real regression in the memory-incident rollback path,
  and the change says so loudly in four places (pump class remarks, `FalconCollectorConfiguration`,
  `FalconFindingsFlow`, `01-collection-strategy.md`) including the correct redirect to `aidBatchSize`. Retry and
  laziness genuinely cannot both hold on a sequence the publisher has already pulled from. Documented,
  deliberate, correctly signposted — no objection.
- **Per-attempt garbage on a re-issue.** A failed attempt discards a partial `List<ReadOnlyMemory<byte>>`.
  Bounded by `aidBatchSize` and rare by construction; not worth engineering around.
- **`CapturingLogger` as an assertion surface.** Normally a smell, and the harness docstring says so itself
  before doing it anyway. For `FetchedBatches` there genuinely is no other observer, and `R2` pairs it with an
  independent published-object count. Reasonable.

## Documentation

Unusually good, and unusually honest — `03-current-concerns.md` names the two dead production runs and
`02-decision-making.md` volunteers that the new label breaks existing `falcon-server-error` alerting. Two
corrections land in the docs as a result of the findings above: the "will half-open on its own" claim in the
pump (finding 1) and the exact-5-minute bound (finding 2).

## Summary

| # | Severity | Area | Fix shape |
|---|---|---|---|
| 1 | Major | Circuit-breaker retries are guaranteed futile; violates the stall invariant | Local patch (one clause) |
| 2 | Minor | Jitter after the cap breaks the stated 5m bound; test restates impl | Local patch |
| 3 | Minor | Documented bound not locally enforced | Local patch, deferrable |
| 4 | Minor | `R5` docstring claims an unverifiable ordering; decorative assertion | Test edit |
| 5 | Nit | Undocumented pump/flow predicate asymmetry | Comment |
| 6 | Nit | Factory naming; one garbled comment | Cosmetic |

Nothing here is dangerous. Finding 1 is the only one I would hold the merge for, and it is a one-line change.
