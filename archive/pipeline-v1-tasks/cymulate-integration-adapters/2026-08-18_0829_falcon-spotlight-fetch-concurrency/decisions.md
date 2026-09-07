# Decisions

- **Fan-out sits around the batch loop, not inside `FalconSpotlightBatchScroller`.** Resolved by code, not
  preference: `EmitBatchRecordsAsync` is a cursor chain — each page's request needs the previous page's
  `after` token, with `updatedFloor` and a `seenFindingIds` set carried across iterations and a re-anchor path
  on cursor expiry. It cannot be parallelised internally without breaking the scroll. The parallel unit is
  therefore one whole `StagedAidBatch` = one independent scroll.

- **Producers materialise, consumer publishes.** Each worker runs a batch's scroll to completion and hands a
  finished record set to a bounded buffer; one consumer publishes. Forced by `EmitBatchRecordsAsync` being
  lazy and currently pull-driven by the publisher — a lazy handoff would leave the fetch inside the serial
  section and deliver no concurrency (A5).

- **The buffer is bounded and small.** Bounding is what keeps A6's N × batch memory claim true. Backpressure
  is the mechanism that stops producers outrunning the publisher.

- **Publish stays serial; checkpoint stays v4.** Follows the house contract already written down in
  `TenableIoCorrelatedBatchPublisher.cs:23-26` and restated in `FalconFindingsFlow`. Parallel publish would
  require Infra changes to `AdvancePage` and `BatchScopedStorage`'s shared metadata entry. Permitted by the
  operator, not justified by this task's goal.

- **Concurrency degree is configuration, clamped like the other scalars.** Operator's explicit call. Enables
  production tuning and a degree-of-1 rollback without redeploy.

- **`AidBatchSize` stays 4.** Operator's call, and consistent with A1 — the last time a Falcon batch bound was
  raised on assumption it returned HTTP 400.

- ~~Proceeding on unverified: Spotlight allows ~6000 rpm (A3).~~ **Resolved 2026-08-18 by `researcher` against
  `FalconDocs/OfficialDocs/crowdstrike-auth.pdf` p42–43, and the premise changed.** It is a **token bucket**:
  100 req/s sustained **per CID**, capacity 6,000 tokens, refill 100/s — 6,000 is the *burst*, not a per-minute
  quota. Pooled across every API client and endpoint ("Each request consumes one token, regardless of which API
  endpoint or API client is used"), so it is neither Spotlight-specific nor exclusively ours. See the sizing
  rule below.

- ~~Proceeding on unverified: producers blocked on backpressure will not routinely exceed the ~120s `after`
  token lifetime (A4).~~ **Resolved 2026-08-18 by `researcher`: 120s is vendor-documented**, verbatim
  ("Tokens expire 120 seconds after a call is made") in `FalconDocs/OfficialDocs/spotlight.pdf` p21 for the
  exact endpoint this collector scrolls — and in five further places there, plus `discover.pdf` p4/p12 for the
  Discover leg. The in-code comment claiming a Spotlight-docs citation is correct. The "never hold a live cursor
  while blocked on backpressure" constraint stays as the mitigation because it holds whatever the TTL is; do not
  implement a 120s timer.

- **Size the concurrency degree from our pipeline's economics, not from the vendor budget.** Stable rule from
  the A3 research, now on vendor evidence: the pool is **per-CID, shared with the customer's other API
  consumers**, and with rate-limit headers out of scope its occupancy is invisible to us — so any degree derived
  as "x% of the limit" is arithmetic over an unknown. No concurrency limit is documented anywhere in the three
  vendor PDFs (verified negative across `crowdstrike-auth.pdf` §1.8, `spotlight.pdf` §1.6/§1.2.6, and all of
  `discover.pdf`). On our own traffic (~7–12 requests/batch at `AidBatchSize` 4) even degree 16 sits under the
  documented 100 req/s, so the vendor is **not** the binding constraint — memory (A6) and the shared pool are.
  Recommended default **4**, clamp ceiling **16**: 4 is a real 4× step off serial, keeps A6's bound at
  4 × one materialised batch, and sits close enough to 1 that the degree-1 rollback is a small delta. Raising it
  above 4 requires measurement — zero 429s over a full run at 4 on the heaviest tenant, measured peak RSS
  against the 4Gi HPA threshold, and observed `X-RateLimit-Remaining` (which needs the deferred header work).

- **Two vendor traps recorded for the deferred header task, so they are not rediscovered.**
  `X-RateLimit-RetryAfter` is a **UTC epoch timestamp**, not delta-seconds, and there is no standard
  `Retry-After` in the documented 429 (`crowdstrike-auth.pdf` p43). Cross-cloud 308 redirects are **counted
  twice** toward the limit, so a misconfigured `ApiEndpoint` region silently halves the effective budget —
  N times over under concurrency (`crowdstrike-auth.pdf` p44).

- **Cancellation semantics must be stated explicitly before implementation, not discovered.** Today
  `OperationCanceledException` yields via `RecordCooperativeYield` with at most one batch abandoned. With N in
  flight the design must define what happens to the other N-1 — the contract requires that resume re-does
  them and never skips them, since the checkpoint records only what the consumer published.

- **Deferred, not silently dropped:** vendor rate-limit header reading (would make the degree data-driven and
  would settle A3), the `BatchScopedStorage` → emitter-flag alignment Tenable already made, and a test pinning
  the User-Agent `ConfigureClient` hook. Each is out of scope here and worth its own task.
