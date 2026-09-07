# Assumptions

Status vocabulary: OPEN / VALIDATED / REJECTED / NEVER-TESTED.
Anything other than OPEN requires an actor and a citation.

**Verifier-2 (2026-08-26, post-repair cycle): A1-A8 are all CARRIED FORWARD UNCHANGED.** The second cycle
repaired the mitigation, the tests and the prose; it produced no new evidence for or against any assumption.
Re-run: 88 passed / 0 failed (80 + the 8 new R4/R5 cases). Two consequences worth recording against existing
dispositions, neither of which moves a status:
- **A6** — still NEVER-TESTED and still no stack for the production throw site. What changed is bookkeeping:
  the residual uncovered surface (Phase 1 Discover scroll + staged-page spool, and the whole assets flow) is
  now written into `Collectors/FalconCollector/FalconDocs/CollectorDocs/03-current-concerns.md:52-58` instead
  of living only in a review file.
- **A3** — the per-attempt ceiling added this cycle (`MaxTransportRetryDelay = 30s`,
  `FalconSpotlightBatchPump.cs:93`) is a head-of-line-blocking bound, NOT a rate-limit one. Do not cite it as
  evidence for the token-bucket model.
Full reasoning in `review/verifier-2.md` SS3.

**Dispositions below are TERMINAL, written by verifier-1 on 2026-08-26 against what LANDED**
(`git diff 77ec50cb` + the new `Exceptions/FalconTransportFailureException.cs`, and the verifier's own
filtered test run: 80 passed / 0 failed). Full reasoning in `review/verifier-1.md` §3. NEVER-TESTED is the
default: it means no diff hunk, test, log line or research file spoke to the claim — not that the claim is
false.

## Prior Art

Tags searched: `cymulate-integration-adapters`, `falcon`, `spotlight`, `transport`/`eof`/`stream`,
`resilience`/`retry`/`recovery`. Matched 6 rows; none superseded or retracted; 4 judged relevant and
seeded below. Followed 1 task pointer of the 3 permitted:
`tasks/cymulate-integration-adapters/2026-08-18_0829_falcon-spotlight-fetch-concurrency/` — the task
that built the very pump this work modifies.

Dropped as not relevant to this path: `L-16fed2de` (devices/v2 batch bound), `L-c9239f9c`
(Discover entity-id derivation).

## Final disposition (terminal — written by the verifier pass, 2026-08-26)

| id | assumption | status | citation | actor |
|---|---|---|---|---|
| A1 | `TenableIoChunkRetry` is the right SHAPE to copy | VALIDATED | `FalconSpotlightBatchPump.cs:336-366`; holds under injected drops at degrees 1, 2 and 4 — `R3_DegreeOne_TransportDropRetriesAndPublishesEachBatchOnce`, `R2_RetriedFetch_CountsBatchOnceNotPerAttempt`, `TransportDropUnderAFanOut_IsRetried_AndEveryBatchPublishesOnceInOrder`, all passing | verifier |
| A2 | An expired Spotlight `after` token surfaces as 404 inside a 200 body | NEVER-TESTED | — nothing in this task exercised the 404-in-200 path | — |
| A3 | Spotlight's rate limit is a shared per-customer token bucket | NEVER-TESTED | — nothing measured; the conservative ladder is consistent with it, not evidence for it | — |
| A4 | Peak memory ≈ degree × one materialised batch | NEVER-TESTED | — still unmeasured, and this change WIDENED it: degree 1 now materialises too | — |
| A5 | `Retry-After` / a 429 status is observable on the Falcon Spotlight path | REJECTED | `FalconHttpFailureClassifier.cs:47-54` omits `retryAfter`; a mid-body drop is a bare `IOException` with no status | verifier |
| A6 | The production EOF originated on the Spotlight body read inside `FetchAsync` | NEVER-TESTED | — throw site never captured with a stack; inferred only from a concurrent teardown 17ms earlier | — |
| A7 | A Phase 2 deferral reaches `OnTerminalSnapshotWithoutPublishedPage` and does not `AdvancePage` | REJECTED | Mechanism refuted — that method is assets-flow (`Flows/Assets/FalconAssetsCheckpointWriter.cs:114`, called only from `FalconAssetsScrollRunner.cs:339,365`). Conclusion holds by another route: the findings path's only `AdvancePage` is inside `OnBatchPublished` (`FalconFindingsFlow.cs:329`), which both throw paths bypass | recon |
| A8 | `FetchAsync` is idempotent across attempts | VALIDATED | `ScrollBatchAsync` (`FalconSpotlightBatchPump.cs:396-414`) allocates fresh stats/records and re-seeds accumulators per attempt; `FalconCorrelatedRecord.cs:69` (`host.DeepClone()`) untouched, confirmed absent from `git diff --stat` | verifier |

Prose detail for each row follows; `review/verifier-1.md` §3 and `review/verifier-2.md` §3 carry the full
reasoning and the decision-drift table.

- **A1 — VALIDATED** (verifier) — the shape landed at `FalconSpotlightBatchPump.cs:336-366` (attempt body
  extracted to `ScrollBatchAsync`) and holds under injected mid-body drops at degrees 1, 2 and 4:
  `R3_DegreeOne_TransportDropRetriesAndPublishesEachBatchOnce`, `R2_RetriedFetch_CountsBatchOnceNotPerAttempt`,
  `TransportDropUnderAFanOut_IsRetried_AndEveryBatchPublishesOnceInOrder` — all PASSED.
  Original: `TenableIoChunkRetry` is the right SHAPE to copy: a per-unit in-flow retry helper,
  not merely "another collector that does concurrency". The prior lesson is aimed squarely at this
  plan's premise. source: lessons.md#L-98cb5412 (recon, 2026-08-18)
- **A2 — NEVER-TESTED** — nothing in this task exercised the 404-in-200 path. The documentation half of the
  obligation WAS met (`01-collection-strategy.md:149` scopes the new bullet to "response stream dies mid-body"
  and claims nothing about cursor expiry), but that is doc hygiene, not evidence.
  Original: An expired Spotlight `after` token surfaces as HTTP 404 *inside a 200 body*, so
  `FalconHttpFailureClassifier.cs:22` may never fire and a batch can truncate silently. A transport
  retry does not cover this and must not be documented as if it does. source: lessons.md#L-882455c1
  (researcher, 2026-08-18)
- **A3 — NEVER-TESTED** — nothing measured. The conservative default ladder is *consistent with* A3 but is
  not evidence for it; do not cite the shipped defaults as confirmation of the bucket model.
  Original: Spotlight's rate limit is a token bucket (100 req/s sustained, 6,000 burst) scoped
  per CUSTOMER ACCOUNT and pooled across every endpoint and client. Retries spend the customer's
  budget, and occupancy is invisible to us. Bears directly on how aggressive the delay ladder may be.
  source: lessons.md#L-5e32d960 (researcher, 2026-08-18)
- **A4 — NEVER-TESTED** — still unmeasured, and this change WIDENED the exposure: degree 1 now materialises a
  batch too (`FalconSpotlightBatchPump.cs:157`, `FalconCollectorConfiguration.cs:222-225`), so the documented
  memory-incident rollback no longer restores the pre-concurrency profile. The 2026-08-18 figure of 90-125 MB
  remains the only datum.
  Original: Peak memory ≈ degree × one materialised batch is unmeasured; the observed figure at
  AidBatchSize 10 was 90–125 MB, not the 35–50 MB target. A backing-off batch holds its slot longer,
  extending residency. source: lessons.md#L-85a32cda (verifier, 2026-08-18)

## This task

- **A5 — REJECTED** (verifier) — contradicted by source: `FalconHttpFailureClassifier.cs:47-54` builds
  `AdapterHttpRequestFailedException` with NO `retryAfter` argument, and `grep -rn -i retryafter
  Collectors/FalconCollector/` returns only the new pump comment; a mid-body drop is a bare `IOException` with
  no status and no headers. **Do not re-assume** a `Retry-After` or 429 rung could fire at the `FetchAsync`
  seam without new evidence — omitting both rungs from the ladder was correct. Scope note: the *session* layer
  separately honours `Retry-After` headers via `DefaultInProcessServerDelayThreshold`; different layer,
  untouched, and not evidence for A5.
  Original: `Retry-After` / a 429 status is actually observable on the Falcon Spotlight path.
  `grep -rn "X-RateLimit-Remaining"` and `grep -rn "RetryAfter"` over `Collectors/FalconCollector/`
  both return nothing, so those rungs of the TenableIo delay ladder may be dead code here. Determine
  what Falcon can actually see before copying the ladder wholesale.
- **A6 — NEVER-TESTED** — no new log evidence; the throw site was never captured with a stack and nothing in
  the diff or the test run speaks to where the production EOF arose. **Do not re-assume the throw site is
  known.** Materially de-risked, not resolved: the R5 guard (`FalconFindingsFlow.cs:365-386`) defers ANY
  retryable transport fault raised inside the Phase 2 consumer loop, so the outage is fixed under either
  branch of A6 provided the fault is inside Phase 2. Phase 1 spool and the assets flow remain uncovered.
  Original: The EOF originates on the Spotlight response-body read inside `FetchAsync`.
  Inferred from the concurrent teardown of that stream 17 ms before the error publish; the throw site
  was never logged with a stack in `admin-traces-production-integration-service-bus`. If it
  originates elsewhere (S3 emission, the consumer), retrying `FetchAsync` does not fix the observed
  failure.
- **A7 — REJECTED as to MECHANISM; conclusion separately CONFIRMED** (recon: mechanism; verifier:
  conclusion) — `OnTerminalSnapshotWithoutPublishedPage` is declared at
  `Flows/Assets/FalconAssetsCheckpointWriter.cs:114` and called only from `FalconAssetsScrollRunner.cs:339,365`
  — the ASSETS flow. `FalconFindingsFlow.cs:471` mentions it in a doc-comment cross-reference only; nothing on
  the findings path invokes it. The conclusion holds by a different route: the findings path's only
  `AdvancePage` is inside `checkpointWriter.OnBatchPublished` (`FalconFindingsFlow.cs:329`), reached only after
  a successful publish at `:309`, and both throw paths escape before it. **Do not re-assume** findings shares
  the assets writer's terminal-snapshot path.
  Original: A deferral raised from Phase 2 reaches
  `OnTerminalSnapshotWithoutPublishedPage` (`FalconFindingsFlow.cs:442`) and does not `AdvancePage`.
  Read from source; never exercised for a transport fault. If wrong, a deferred transport failure
  mints a page over an unpublished batch.
- **A8 — VALIDATED** (W1 source, verifier test) — `ScrollBatchAsync` (`FalconSpotlightBatchPump.cs:396-414`)
  allocates a fresh `BatchEmitStats` and `List<>` per attempt and re-seeds from the frozen `StagedAidBatch`;
  `FalconCorrelatedRecord.cs:69` (`host.DeepClone()`) is untouched (`git diff --stat` over that file is empty);
  `R3_DegreeOne_TransportDropRetriesAndPublishesEachBatchOnce` asserts each retried aid's object carries its
  finding exactly once — PASSED.
  Original: `FetchAsync` is idempotent across attempts: fresh `BatchEmitStats`, fresh `List<>`,
  `SeedAccumulators(batch)` rebuilt from the frozen `StagedAidBatch`, and `FalconSpotlightBatchScroller`
  holding only readonly deps. Read from source; not yet proven by a test that fails a fetch and
  re-runs it.
