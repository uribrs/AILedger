# Assumptions

Entries were seeded OPEN by the contract designer. A3 and A4 were disposed by `researcher` on 2026-08-18
against `research/crowdstrike-spotlight-throughput.md`, then **re-evidenced the same day against the
authoritative CrowdStrike documentation checked in at `FalconDocs/OfficialDocs/`** — citations are now vendor
PDF page numbers, not URLs. A3a and A4a were raised by that research. Everything else remains OPEN.

## Prior Art

Tags searched: `cymulate-integration-adapters`, `falcon`, `crowdstrike`/`spotlight`, `concurrency`/`parallel`,
`checkpoint`/`resume`. Matched 4 rows in `lessons.md`; none carried `supersedes:` or `retracts:`, so no
head-filtering was needed and no truncation occurred. Two are relevant and seeded below as A1 and A2. Two were
read and rejected as not relevant: `2026-04-29 AgentService tenable checkpoint-resume` (different repo and
collector) and `2026-06-25 CollectorBase auth sigv4` (unrelated subsystem). The ledger predates the one-line
format, so the entries carry `do not` guidance instead of a `verify:` command — nothing was mechanically
re-runnable to confirm them.

- **A1 — VALIDATED** (verifier, 2026-08-18) — Vendor batch/volume bounds for Falcon must not be taken on faith. A prior run assumed
  `POST /devices/entities/devices/v2` accepted 5,000 IDs and got HTTP 400 at 250 AIDs while a single-AID probe
  on the same session returned 200. Directly relevant: this task's premise is a vendor throughput ceiling
  (see A3), and the same class of belief was already refuted once on this collector.
  source: `lessons.md#2026-07-26-falcon` (executor, 2026-07-26) →
  `tasks/cymulate-integration-adapters/2026-07-26_1655_falcon-prevention-policy-enrichment/`

- **A2 — VALIDATED** (verifier, 2026-08-18) — Emission *ordering* and record *identity* are separate properties on this collector, and
  ordering was previously found unstable while identity was stable. Sorting at emission was adopted to fix
  parser `entities[0]` nondeterminism. Relevant because introducing concurrency is an ordering change: any
  existing sort-at-emission behaviour must survive, and per-batch record order must not become
  completion-order.
  source: `lessons.md#2026-07-02-falcon` (executor, 2026-07-02) →
  `tasks/cymulate-integration-adapters/2026-07-02_1705_falcon-correlated-findings-redesign/`

## Task assumptions

- **A3 — REJECTED as written** (actor: `researcher`, 2026-08-18; **re-evidenced against the authoritative vendor
  documentation checked into this repo**, superseding the web-sourced disposition) — The *number* is right; the
  *mechanism* and the *scope* the assumption implies are not, and both bear on the decision.
  - **Mechanism — a token bucket, not a per-minute quota.** "An average of **100 requests per second per
    customer account**", bucket capacity **6,000 tokens**, refilling continuously at **100 tokens/second**.
    6,000 is the *burst* capacity; 100/s is the sustained rate (100 × 60 = 6,000/min, hence the coincidence that
    made the flat reading look right). Bursts above 100/s are absorbed by the bucket; only sustained excess
    drains it. Reasoning about sustained load from the 6,000 figure alone is the A1 error class.
  - **Scope — per customer account (CID), pooled across every API client and every endpoint.** "Each request
    consumes one token, **regardless of which API endpoint or API client is used**", and `X-RateLimit-Limit` is
    the maximum "that can be made by **all API clients in your customer account** (also called a 'CID')". So it
    is not a Spotlight allowance and not exclusively ours: the pool is shared with every other API consumer on
    the customer's CID. The "two orders of magnitude of headroom" framing is therefore unsound — we can compute
    our own request rate but never the pool's occupancy.
  - **Not Spotlight-specific.** `spotlight.pdf` documents no rate limit of its own; §1.6 lists only
    `429 Too Many Requests — The allowed rate limit has been reached.`
  - **Corrections to the earlier web-sourced disposition**: the unauthenticated/token-request limit is **5 req/s
    per source IP (not per CID), bucket 300** — not the "15/min" a maintainer stated; and the only documented
    backoff guidance is *"monitor the rate limiting headers that are returned with each request, then tune your
    API script or integration so that the rate limiting pool is restored more quickly than your requests deplete
    it"* — CrowdStrike publishes no exponential-backoff instruction.
  - **Practical effect on N**: at `AidBatchSize` 4 and `FindingsApiPageSize` 2500 (~7–12 requests/batch), even
    degree 16 sits under 100 req/s on our own traffic. The vendor limit is not the binding constraint; the
    shared pool and A6's memory bound are.
  citation: `FalconDocs/OfficialDocs/crowdstrike-auth.pdf` §1.8 Rate limiting **p42–43** (algorithm, numbers,
  scope, header table, 429 body) and §1.9.3 **p44** (cross-cloud 308s counted twice toward the limit);
  `FalconDocs/OfficialDocs/spotlight.pdf` §1.6 **p20** (429 row), **p28** (remediation-cache best practice) →
  `research/crowdstrike-spotlight-throughput.md` §A3

- **A3a — VALIDATED** (orchestrator claim, confirmed in source by verifier, 2026-08-18) — A 429 on a
  Spotlight page IS retried by the HTTP path, not surfaced as a batch failure. `SessionRetryDefaults.CreateTransient`
  (`IntegrationInfra/src/IntegrationInfra/Conversation/SessionRetryDefaults.cs:36-54`) does not override
  `RetryStatusCodes`, so the default set applies, and it includes `HttpStatusCode.TooManyRequests`
  (`Cymulate.Http.Package/DefensiveToolkit/Contracts/Options/RetryOptions.cs:13-20`). Retry-After <= 60s is
  absorbed in-process; > 60s externalizes to the host-scheduled wait (`ExternalizeServerSuggestedDelays = true`,
  `MinExternalizedServerSuggestedDelay = 60s`, wired at `FalconCollectorConfiguration.cs:42-43`).
  The safety net for N-concurrent scrolls against a shared, unobservable budget therefore exists.

- **A3a (original text, superseded)** — A 429 returned on a Spotlight page request is retried by the Falcon HTTP path rather than
  surfaced as a batch failure. Not established by research: `FalconFlowExceptionClassifier`'s transient-arm
  comment concerns the object-store contract, not the vendor HTTP client. Raised by the A3 finding — with
  rate-limit header reading out of scope by contract, the 429 retry path is the only safety net for
  N-concurrent scrolls against a shared, unobservable budget.
  Consequence if wrong: a 429 caused by the customer's *other* API consumers fails our batch outright.
  Verify in code before the concurrency degree is raised above the recommended default of 4.

- **A4 — VALIDATED** (actor: `researcher`, 2026-08-18; **re-evidenced against the authoritative vendor
  documentation checked into this repo**) — 120 seconds is vendor-documented, verbatim and repeatedly:
  *"Tokens expire 120 seconds after a call is made."* It appears in the `after` parameter description of **every**
  paginated Spotlight endpoint and in the §1.6 response-code table, and identically in `discover.pdf` for the
  Discover host scroll — so **both** endpoints this flow drives carry the same documented TTL.
  **Retraction of a claim in the earlier disposition**: I previously reported the figure as undocumented for
  Spotlight and flagged the comment at `FalconCursorExpiredException.cs:5` ("Per the Spotlight docs…") as
  unsupported. That was wrong — true of the *public developer portal*, which omits the sentence, but not of the
  customer documentation, which states it plainly. **The code comment is accurate and correctly attributed**, as
  is every 120s reference in `CollectorDocs/01-collection-strategy.md:50,112`, `03-current-concerns.md:23` and
  `06-resume-live-state-authority.md:65`.
  Two findings that still stand, now at Tier-1: (a) a 404 is **not** a clean expiry discriminator — the same row
  lists "an invalid ID is in the request or there are too many records in the response" as the alternative
  cause; (b) keep the structural constraint (never hold a live cursor while blocked on backpressure) rather than
  a 120s timer — it stays correct whatever the TTL.
  citation: `FalconDocs/OfficialDocs/spotlight.pdf` **p21** (`after` on `/spotlight/combined/vulnerabilities/v1`,
  the endpoint this collector scrolls), also **p19, p24, p29, p31, p34**; `FalconDocs/OfficialDocs/discover.pdf`
  **p4, p12** → `research/crowdstrike-spotlight-throughput.md` §A4

- **A4a — NEVER-TESTED** (verifier, 2026-08-18; raised by the A4 re-research, deliberately not fixed inside this task) — An expired
  Spotlight `after` token may not arrive as an HTTP 404 status at all. `spotlight.pdf` **p19** attaches this note
  to the `404 Not found` row (which covers the expired-token arm): *"Note: This message displays with a **200 OK
  header** and the 404 code under `errors` in the response body."* Verified visually against the page image, not
  just extracted text — the note sits in the HTTP-code cell for that row, whose description carries both causes
  joined by "or".
  `FalconHttpFailureClassifier.cs:22` detects cursor expiry with
  `response.StatusCode == HttpStatusCode.NotFound && UrlHasAfterCursor(url)`. If the vendor note is accurate for
  this API, that condition cannot be true for Spotlight, and the re-anchor path it feeds
  (`FalconSpotlightBatchScroller.cs:99`) never fires on this leg.
  Not resolvable from documentation alone — the note could be stale, or the gateway could return a real 404
  status in some deployments. No run data was available to settle it (`stats.SpotlightReanchors` exists but no
  observed occurrence is recorded anywhere in the repo).
  Consequence if the note is accurate: concurrency raises the chance of a producer's cursor ageing out, and a
  re-anchor path that does not fire turns a handled event into a silently truncated batch.
  verify: mint an `after` token on `/spotlight/combined/vulnerabilities/v1`, wait 130s, reuse it, record the
  status line and body. One request pair; produces exactly the failing-run payload A1 demands.
  citation: `FalconDocs/OfficialDocs/spotlight.pdf` §1.6 **p19** against
  `Flows/SharedFlows/FalconHttpFailureClassifier.cs:22` → `research/crowdstrike-spotlight-throughput.md` §Q7

- **A5 — VALIDATED** (verifier, 2026-08-18) — Producers must fully materialise a batch's correlated records before handing them to the
  consumer, because `EmitBatchRecordsAsync` returns a lazy `IAsyncEnumerable` that is currently driven by the
  publisher pulling. If the buffer carried the un-enumerated sequence, the fetch would still happen inside the
  serial publish and the change would deliver no concurrency.
  Consequence if wrong: either no speedup (lazy handoff) or unbounded memory (materialising without a cap).

- **A6 — NEVER-TESTED** (verifier, 2026-08-18) — Peak memory becomes approximately N × one materialised batch, replacing today's bound of
  one batch's partial per-host accumulators. The pod has an 8Gi limit against a ~540MB idle baseline, and the
  HPA scales on memory at 70% of a 4Gi request.
  Consequence if wrong: a large-estate tenant's batches are bigger than assumed, and N × batch crosses the
  HPA threshold or the container limit.

- **A7 — NEVER-TESTED** (verifier, 2026-08-18) — No consumer of the published objects depends on wall-clock spacing between objects, only on
  their order and content. Concurrency compresses the interval between publishes considerably.

- **A8 — REJECTED** (recon, formalised by verifier 2026-08-18) — TenableIo's parallel implementation is running locally and observable, and can supply a
  measured concurrency degree rather than a guessed one. Operator-stated; not yet confirmed runnable in this
  environment.

- **A9 — VALIDATED** (verifier, 2026-08-18) — Degree-of-1 configuration reproduces today's behaviour exactly, making the change safely
  revertible in production without a redeploy. Must be demonstrated by test, not asserted.


## Verifier dispositions — TERMINAL (verifier, 2026-08-18)

Recorded by the verifier pass; full reasoning and the per-criterion verdicts are in
`review/verifier-1.md`. `NEVER-TESTED` is the default status: `VALIDATED`/`REJECTED` appear only where a
specific diff hunk, test, command output or research file moved the assumption. Compiling, or the task
completing, moved nothing.

| id | status | citation | actor |
|----|--------|----------|-------|
| A1 | VALIDATED | Its prediction recurred: `research/crowdstrike-spotlight-throughput.md` §A3 / `crowdstrike-auth.pdf` p42-43 refuted the vendor throughput belief this task rested on. Honoured in the work: `AidBatchSize` untouched (`FalconCollectorConfiguration.cs:122`), degree defaulted conservatively rather than derived from the vendor number. | `researcher` (evidence), `W1` (honoured) |
| A2 | VALIDATED | `FalconSpotlightConcurrencyTests.PublishedObjects_KeepFrozenListOrder_NamingAndContent_AtEveryDegree(1,2,4,8)` with `ReverseSpeedVendor` (completion order = reverse of frozen order); per-object content asserted. Within-batch record identity is not newly at risk — one batch is one sequential scroll (`FalconSpotlightBatchPump.cs:265-280`). | `W2` |
| A3 | REJECTED as written | `crowdstrike-auth.pdf` §1.8 p42-43 via `research/crowdstrike-spotlight-throughput.md` §A3 — token bucket, per-CID, pooled across all clients and endpoints. | `researcher` |
| A3a | VALIDATED | Confirmed in source by the verifier: `SessionRetryDefaults.CreateTransient` (`IntegrationInfra/src/IntegrationInfra/Conversation/SessionRetryDefaults.cs:36-55`) never sets `RetryStatusCodes`; the default set (`Cymulate.Http.Package/DefensiveToolkit/Contracts/Options/RetryOptions.cs:14-21`) includes `TooManyRequests`. No test exercises a 429. | `orchestrator` (claim), `verifier` (confirmed) |
| A4 | VALIDATED | `spotlight.pdf` p21 (and p19/24/29/31/34), `discover.pdf` p4/p12, via `research/crowdstrike-spotlight-throughput.md` §A4. | `researcher` |
| A4a | NEVER-TESTED | No 130s cursor probe was run; `execution_notes.md` says so itself. `Flows/SharedFlows/FalconHttpFailureClassifier.cs` is not in the diff. Residual risk: `SpotlightReanchors` may be a silent zero, so it is not evidence either way about cursor pressure. | — |
| A5 | VALIDATED | `FalconSpotlightBatchPump.FetchAsync:265-280` materialises; the degree-1 branch (`:116-133`) deliberately does not; the predicted consequence is observed in `SpotlightScrollsInFlight_AreExactlyTheConfiguredDegree(2)`/`(4)`. Records are independent arrays (`FalconCorrelatedRecord.cs:73`). | `W1` (impl), `W2` (test) |
| A6 | NEVER-TESTED | Nothing measured memory. The `execution_notes.md` formula is an argument, not a measurement, and its `S = 35-50 MB` is a **target** (`FalconCollectorConfiguration.cs:95-98`, "4 targets ~35-50 MB") while the observation at `AidBatchSize` 10 was 90-125 MB. The count bound (<= degree live record sets) is structurally sound (`FalconSpotlightBatchPump.cs:148,237,180`); the bytes are not. Residual risk: at the observed class rather than the target, degree 4 is ~360-500 MB live and the ceiling of 16 is 1.4-2 GB — under the 8Gi limit but able to cross the 70%-of-4Gi HPA trigger. | — |
| A7 | NEVER-TESTED | Nothing in the diff, the tests or the research addresses downstream sensitivity to publish spacing, which this change compresses by design. | — |
| A8 | REJECTED | `research/internal-recon.md`, Landmines — all three TenableIo `Parallel.ForEachAsync` sites are unordered sub-item fan-outs; the chunk level is a sequential `foreach` (`TenableIoVulnPhase.cs:215-224`). No ordered producer/consumer pipeline exists to observe or measure. | `recon` |
| A9 | VALIDATED | Reachable by configuration (`Build_WithMaxConcurrentSpotlightBatchesBelowOne_ClampsToOne`; clamp at `FalconCollectorConfigurationBuilder.cs:191`) and indistinguishable in output (`PublishedObjects_...(1)`), vendor-side concurrency (`SpotlightScrollsInFlight_...(1)`) and checkpoint shape (`CheckpointShape_IsIdenticalAtEveryDegree...`). Gap: the memory half — that degree 1 still does not materialise — is prose only, untested. | `W1`/`W2` |
