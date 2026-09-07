# Research — CrowdStrike Spotlight throughput: rate limits and pagination cursor lifetime

- **Topic slug**: `crowdstrike-spotlight-throughput`
- **Triggering assumptions**: A3 (rate limit), A4 (`after` token lifetime)
- **Task type**: capability exploration in support of an implementation decision (choosing a concurrency degree N)
- **Researcher**: `researcher` (technical-researcher skill), 2026-08-18
- **Revision 2** — rewritten against the authoritative CrowdStrike documentation checked into this repo. The
  first revision was built from open-web sources; where the two disagree, the vendor PDFs win and the
  corrections are called out explicitly below.

---

## Sources

**Primary — authoritative vendor documentation, in-repo** at
`src/Cymulate.Integration.Adapters/Collectors/FalconCollector/FalconDocs/OfficialDocs/`:

| Document | Pages | What it settles |
|---|---|---|
| `crowdstrike-auth.pdf` — *CrowdStrike OAuth2-Based APIs*, dated 04/30/2026 | 46 | §1.8 **Rate limiting** (p42–43): the algorithm, the numbers, the scope, the headers, the 429 body. §1.9.3 Redirects and rate limiting (p44). |
| `spotlight.pdf` — *Vulnerability Management APIs*, dated 04/30/2026 | 37 | §1.6 **HTTP response codes** (p19–20). `after` parameter descriptions (p21, p24, p29, p31, p34). §1.7.3.1 remediation-cache best practice (p28). §1.2.6 Recommended best practices (p4). |
| `discover.pdf` — *Asset Management APIs* | 55 | `after` parameter descriptions (p4, p12). |

**Secondary — this repo's own collector docs** (`FalconDocs/CollectorDocs/`), used to cross-check what the
codebase already believes.

Open-web sources from revision 1 are retained only where they add nothing the PDFs settle, and are marked as
superseded where they do. No claim below rests on a community source.

---

## A3 — Rate limits

### Q1. The documented rate limit

**Answer: documented precisely, and it is a token bucket — not a flat per-minute quota.**

`crowdstrike-auth.pdf` p42, §Rate limiting, verbatim:

> "All requests to CrowdStrike APIs are subject to a rate limit. The rate limit is calculated by a token bucket
> algorithm that allows a certain number of requests per second. The bucket has a maximum capacity and refills
> over time. Rate limits apply to API requests differently depending on whether the request contains a valid
> bearer token.
>
> **Requests with a valid bearer token**: For requests containing a valid bearer token, the rate limit allows an
> average of **100 requests per second per customer account**.
> - The bucket has a standard capacity of **6,000 tokens**. When the bucket is full, up to 6,000 requests can be
>   made.
> - Each request consumes one token, **regardless of which API endpoint or API client is used**.
> - The bucket refills continuously at **100 tokens per second** until it reaches its maximum capacity.
>
> **Requests without a valid bearer token**: … an average of **5 requests per second per source IP address (not
> per CID)**. Note: This includes requests to get an auth token…
> - The bucket has a standard capacity of **300 tokens**… refills continuously at 5 tokens per second."

**The operator's "~6000 requests/minute" is right, but for a reason worth getting straight.** 6,000 is the
*bucket capacity* — the maximum burst from a full bucket. The *sustained* rate is 100 req/s, which is 6,000 per
minute, so the two coincide numerically. `X-RateLimit-Limit` is described (p42) as "Maximum number of requests
per minute", which is where the per-minute framing comes from. The practical difference matters for a
concurrency change: **a short burst above 100 req/s is absorbed by the bucket, and only a sustained rate above
100 req/s drains it.** A flat "6,000/min" model would have said nothing about burst behaviour; the bucket model
says bursts are explicitly fine.

Spotlight-specific: `spotlight.pdf` documents **no** Spotlight-specific rate limit. Its §1.6 HTTP response codes
table (p20) lists `429 Too Many Requests — The allowed rate limit has been reached.` and nothing further. The
only rate-limit-adjacent guidance in the whole document concerns a *different* endpoint (p28, §Get remediation
details by ID): *"CrowdStrike recommends maintaining a local cache for joining and look up purposes, and
refreshing this cache infrequently… excessive calls to the remediation API can cause you to hit your API rate
limit."* This collector takes remediation as a **facet** on the combined endpoint rather than calling the
remediation API per ID, so that warning does not bite here.

*Correction to revision 1*: I reported a psfalcon maintainer's "15/min" for token requests. The vendor document
says **5 requests per second per source IP, bucket capacity 300** for unauthenticated requests (which includes
token requests). Use the documented figure — and note the scope difference: that one is **per source IP, not per
CID**, which matters if several collector pods share an egress address.

### Q2. Scope — per client_id, per CID, per endpoint, or global?

**Answer: per customer account (CID), pooled across every API client and every endpoint. Now Tier-1 confirmed —
this was the key finding of revision 1, and the vendor document states it outright, three times.**

- p42: *"an average of 100 requests per second **per customer account**"* and *"Each request consumes one token,
  **regardless of which API endpoint or API client is used**."*
- p42, header table, `X-RateLimit-Limit`: *"Maximum number of requests per minute that can be made by **all API
  clients in your customer account** (also called a 'CID')"*.
- p42, header table, `X-RateLimit-Remaining`: *"Number of requests that remain in **your customer account's**
  rate limiting pool. Remember that making an API request removes one request from your customer account's API
  rate limiting pool; the pool automatically replenishes requests over time."*

**Consequences for this task:**

1. N concurrent scrolls on one session **share one budget**. There is no per-client or per-endpoint
   sub-allowance to spend.
2. **The budget is not ours alone.** It is the customer's CID-wide pool, shared with every other API client they
   have registered — their SIEM connector, their SOAR, their own scripts, and any other Cymulate collector
   pointed at the same tenant. We can compute our own request rate; we cannot see the pool's occupancy.
3. A second Cymulate collector running concurrently against the same CID competes with this one. That is a
   deployment-level concern this task does not control, but it is a reason to keep N modest.

### Q3. Rate-limit response headers

**Answer: three headers, exactly as named below. `crowdstrike-auth.pdf` p42–43, header table.**

| Header | Type | Description (verbatim) | Included in responses |
|---|---|---|---|
| `X-RateLimit-Limit` | int | "Maximum number of requests per minute that can be made by all API clients in your customer account (also called a 'CID')" | **Every response** |
| `X-RateLimit-Remaining` | int | "Number of requests that remain in your customer account's rate limiting pool…" | **Every response** |
| `X-RateLimit-RetryAfter` | **Date/time (UTC epoch timestamp)** | "The next time when your customer account's rate limit pool will have at least one request available. This header is only included if you exceed your customer account's rate limit (in other words, after `X-RateLimit-Remaining` would go below 0)." | **Only when rate limit is exceeded** |

**Trap, now vendor-confirmed**: `X-RateLimit-RetryAfter` is an **absolute UTC epoch timestamp**, not
RFC-standard `Retry-After` delta-seconds. The worked example on p43 shows `x-ratelimit-retryafter: 1555640820`
— an epoch second. Code treating it as "seconds to wait" would sleep for roughly 49 years. There is **no**
standard `Retry-After` header in the documented 429 response.

Header names are rendered `X-RateLimit-…` in the table and lowercase in the wire example; HTTP header names are
case-insensitive, so match accordingly.

Header reading is out of scope for this task by contract. This table is reference material for the follow-up
already recorded in `decisions.md`, not work to do now.

### Q4. Documented behaviour on exceeding the limit

**Answer: HTTP 429 with the three headers and a structured error body. `crowdstrike-auth.pdf` p42–43.**

> "If you exceed your rate limit, the response to any further request returns a HTTP 429: Too Many Requests
> error, along with current rate limit status as HTTP headers." (p42)

Worked example, p43 §Rate limited response:

```
HTTP/2 429
content-type: application/json
x-content-type-options: nosniff
x-ratelimit-limit: 6000
x-ratelimit-remaining: 0
x-ratelimit-retryafter: 1555640820
content-length: 224
date: Fri, 19 Apr 2019 02:26:34 GMT

{
    "meta": { "query_time": 0.000875986, "powered_by": "crowdstrike-api-gateway", "trace_id": "…" },
    "errors": [ { "code": 429, "message": "API rate limit exceeded." } ]
}
```

**Documented backoff expectation** — p43, §Best practices for rate limiting, verbatim and complete:

> "As a best practice, we recommend that you monitor the rate limiting headers that are returned with each
> request, then tune your API script or integration so that the rate limiting pool is restored more quickly than
> your requests deplete it."

That is the entire published guidance. CrowdStrike prescribes **proactive pacing from the headers**, and
publishes **no** backoff algorithm, no retry count, and no exponential-backoff instruction. Revision 1
attributed exponential-backoff advice to a CrowdStrike maintainer on GitHub — that remains a maintainer's
opinion, not documentation, and is superseded by the paragraph above.

**One further documented cost, easy to miss** — p44, §Redirects and rate limiting:

> "Because the HTTP 308 requires an additional round trip, redirected requests are counted twice toward the rate
> limit. To avoid rate limit errors, we recommend caching the Location and making all subsequent requests
> directly to this URL."

Cross-cloud redirects (US-1/US-2/EU-1) therefore double the token cost of every affected request. Worth checking
that the collector's configured `ApiEndpoint` is the tenant's correct cloud — a misconfigured region silently
halves the effective budget, and would do so N times over under concurrency.

### Q5. Concurrency limits distinct from rate limits

**Answer: not stated in any of the three documents. This is a searched negative finding, not an unexamined gap.**

Checked across all 37 + 46 + 55 pages for `concurren*`, `parallel`, `simultaneous`, `at a time`: **zero matches
in all three documents.** Specifically:

- `crowdstrike-auth.pdf` §1.8 Rate limiting and §1.8.1 Best practices for rate limiting (p42–43) — the section
  that would carry it — describes only the token bucket. No connection cap, no in-flight-request cap.
- `spotlight.pdf` §1.6 HTTP response codes (p19–20) and §1.2.6 Recommended best practices (p4) — no concurrency
  guidance of any kind.
- `discover.pdf` — no rate-limit or concurrency section at all; its only relevant content is the `after`
  parameter description.

**Interpretation**: CrowdStrike governs load by request rate, not by connection count. There is no documented
ceiling on simultaneous requests, and a burst capacity of 6,000 tokens implies bursts are an expected mode of
use. Absence of a published limit is not proof none is enforced — but these are the sections where one would be
published, and it is not there.

---

## A4 — Pagination cursor lifetime

### Q6. Documented lifetime of the Spotlight `after` token

**Answer: 120 seconds. Vendor-documented, repeatedly and unambiguously. The code comment is correct.**

`spotlight.pdf` states it in **six** places — once in the HTTP response codes table, and once in the `after`
parameter description of every paginated endpoint:

| Page | Context |
|---|---|
| p19 | §1.6 HTTP response codes, the `404 Not found` row |
| p21 | `GET /spotlight/combined/vulnerabilities/v1` — the endpoint this collector scrolls |
| p24 | `GET /spotlight/entities/evaluation-logic/v1` |
| p29 | evaluation-logic IDs by timestamp |
| p31 | Windows patch details |
| p34 | deprecated `GET /spotlight/queries/vulnerabilities/v1` |

Verbatim, p21, the `after` parameter on `/spotlight/combined/vulnerabilities/v1`:

> "`after` — query — string — Token used with the `limit` parameter to manage pagination of results. On your
> first request, don't provide an `after` token. On subsequent requests, provide the `after` token from the
> previous response to continue from that place in the results. **Tokens expire 120 seconds after a call is
> made.**"

`discover.pdf` p4 and p12 carry the identical sentence for the Discover host scroll this flow also drives. So
**both** endpoints in the correlated findings flow are governed by the same documented 120-second TTL.

**Correction to revision 1, stated plainly**: I previously reported that CrowdStrike does not document a
lifetime for the Spotlight `after` token, and flagged the comment at `FalconCursorExpiredException.cs:5`
("Per the Spotlight docs the `after` token expires 120 seconds after a call") as an unsupported claim. That was
wrong. It is true of the *public developer portal*, whose parameter description omits the sentence; it is not
true of the customer documentation, which states it explicitly. **The code comment is accurate and correctly
attributed.** The same applies to every 120s reference in `CollectorDocs/01-collection-strategy.md:50,112`,
`03-current-concerns.md:23`, and `06-resume-live-state-authority.md:65` — all vendor-supported.

**What this licenses for the task**: 120 seconds is a documented vendor figure, not an in-house observation, so
the constraint in `constraints.md` ("a producer must not hold a live cursor while blocked on backpressure") is
grounded rather than precautionary. It remains the right *shape* of mitigation — a structural guarantee that a
cursor is never held across a block stays correct whatever the TTL — but it now defends against a published
limit rather than a guess.

### Q7. Error shape on `after` token expiry

**Answer: error code 404 — but delivered inside the body of an HTTP 200 response. This contradicts how the
collector detects it, and is the highest-value finding in this document.**

`spotlight.pdf` p19, §1.6 HTTP response codes, the `404 Not found` row, verbatim and complete:

> **HTTP code**: `404 Not found`
> *Note: This message displays with a **200 OK header** and the 404 code under `errors` in the response body.*
>
> **Description**: "An invalid ID is in the request or there are too many records in the response. Verify the
> IDs and resend the request.
> **or**
> Your `after` pagination token has expired. Tokens expire 120 seconds after a call is made.
> Tip: Reduce the number of records in your response by including the `limit` parameter in your request."

I verified the table layout visually (page image, not just extracted text): the note sits in the *HTTP code*
cell immediately under the `404 Not found` label, and the Description cell for that same row carries **both**
causes joined by "or". The note therefore qualifies the row as a whole, including the expired-token arm.

**Two consequences.**

**(a) A 404 is not a clean expiry discriminator — now Tier-1.** The same 404 means *either* an expired `after`
token *or* an invalid ID / too many records. This collector runs `limit=2500`
(`FalconCollectorConfiguration.FindingsApiPageSize`) against a documented max of 5000 (p21), so the oversize arm
is unlikely but not impossible.

**(b) The collector's cursor-expiry detection keys on the HTTP status line, which per this document is `200`.**

```
Flows/SharedFlows/FalconHttpFailureClassifier.cs:22
    if (response.StatusCode == HttpStatusCode.NotFound && UrlHasAfterCursor(url))
```

If the vendor note is accurate for the vulnerability management API, that condition **cannot be true** for a
Spotlight expired-token response, and the re-anchor path it feeds — `FalconSpotlightBatchScroller.cs:99`,
`catch (FalconCursorExpiredException) when (…)` — never fires on this leg. The scroll would instead see a 200
whose body carries a 404 error and no `after` token.

**I am flagging this as a conflict to settle, not declaring the code broken.** Three things could be true: the
note is accurate and the Spotlight re-anchor path is effectively dead; the note is stale, or applies only to the
"invalid ID" arm despite its placement; or the gateway returns a genuine 404 status line in some deployments. I
found no operational evidence either way — `stats.SpotlightReanchors` exists but no run data was available to
me, and the collector docs describe re-anchoring as designed behaviour without citing an observed occurrence.

Note the asymmetry that makes this worth checking rather than dismissing: `discover.pdf` has **no** HTTP
response codes section and no such note, and the Discover scroll is the leg where the collector's proactive
500-page cursor cap (`MaxPagesPerCursorScroll`) already exists because expiry-adjacent failures *were* observed.
The 200-wrapping note is specific to the vulnerability management API.

**Cheapest way to settle it**: one probe against a real CID — mint an `after` token on
`/spotlight/combined/vulnerabilities/v1`, wait 130 seconds, reuse it, record the status line and the body. A
single request pair, and it produces exactly the kind of failing-run payload the prior-art lesson demands.

This is **out of scope for the concurrency task** and must not be fixed inside it — but it bears on the task
directly, because concurrency raises the chance of a producer's cursor ageing out, and a re-anchor path that
does not fire converts that from a handled event into a silently truncated batch.

### Q8. Recommended re-anchor strategy for expired cursors

**Answer: none published. The vendor's only remedy is prevention — reduce the page size.**

`spotlight.pdf` p19–20 gives one instruction for the 404 condition: *"Tip: Reduce the number of records in your
response by including the `limit` parameter in your request."* There is no re-anchor guidance, no
resume-from-position mechanism, and no alternative pagination mode documented for these endpoints.

`spotlight.pdf` p4, §1.2.6 Recommended best practices, does however endorse the traversal shape this collector
already uses:

> "To ensure the completeness of returned vulnerabilities, by default, vulnerability management API responses
> are sorted chronologically based on the last updated timestamp. This ensures the completeness of returned
> vulnerabilities. Alternative sort parameters should only be applied for use cases other than maintaining a
> replica of vulnerability data."

The collector pins `sort=updated_timestamp.asc` (`FalconUrls.BuildCorrelatedSpotlightBaseUrl`), which is both
the documented completeness guarantee **and** the axis its watermark re-anchor depends on. The in-house
re-anchor strategy (drop cursor, strengthen the timestamp floor to the watermark, dedupe the boundary with a
per-batch seen-id set) is more capable than anything CrowdStrike publishes; no external source validates it, and
none contradicts it.

---

## Cross-reference against the task

**Request-rate arithmetic** (my computation from repo constants — the only non-vendor-sourced numbers here).
One scroll page is one request at `FindingsApiPageSize = 2500`, scoped to `AidBatchSize = 4` AIDs. The
configuration's own note records the heaviest observed tenant at 40–70k findings per batch when `AidBatchSize`
was 10, scaling to roughly 16–28k at 4, i.e. **~7–12 requests per batch**. Sustained rate at degree N with page
round-trip `t` seconds ≈ `N / t` requests per second.

Against a documented **100 req/s sustained, 6,000-token burst bucket**:

| Degree | `t = 0.5s` (aggressive) | `t = 2s` (realistic) | Share of 100 req/s |
|---|---|---|---|
| 1 (today) | 2 req/s | 0.5 req/s | 0.5–2% |
| 4 | 8 req/s | 2 req/s | 2–8% |
| 16 | 32 req/s | 8 req/s | 8–32% |

Even degree 16 stays under the sustained refill rate on these figures, and any burst is absorbed by a bucket
sixty times larger than one second's allowance. **The vendor rate limit is not the binding constraint on N for
our traffic in isolation.** What remains binding is (a) the pool is the customer's, shared with consumers we
cannot see, and (b) memory — A6's `N × one materialised batch` — which the collector has already been tuned hard
against (`AidBatchSize` 250 → 50 → 10 → 4, each step driven by oversized objects).

**Agreement with this repo's existing docs, and one correction to make there.**
`CollectorDocs/03-current-concerns.md:173` records: *"The cost is request count, against an unofficial ~6,000
req/min per-CID pool that CrowdStrike does not document and we have not verified. … Before lowering further,
read `X-RateLimit-Limit` from a live response on the real CID."* The **per-CID scoping in that note is correct
and now Tier-1 confirmed**; the **"does not document" clause is wrong** — `crowdstrike-auth.pdf` §1.8 documents
it fully, and that document is checked in a few directories away. Worth correcting so the next reader does not
re-research this. (Not corrected by me: it is outside this task's file ownership.)

**Tension with the contract, flagged not re-litigated.** Rate-limit header reading is out of scope by contract.
The vendor's *only* published best practice is to monitor those headers and pace against them (p43). So the
collector will run N-concurrent while ignoring the one control CrowdStrike documents. That is the operator's
call and I am not reopening it; it is recorded here because it is the reason the recommended degree is
conservative rather than sized to the ceiling — and because `X-RateLimit-Limit` and `X-RateLimit-Remaining` are
returned on **every** response, so the data already arrives on every request the collector makes and is simply
being discarded.

**Relation to prior art (A1).** A1 concerns a batch bound on `POST /devices/entities/devices/v2`; nothing here
bears on it. What this research does confirm in A1's spirit: the Spotlight bounds that *are* documented are
`limit` max **5000** on `/spotlight/combined/vulnerabilities/v1` (p21) and max **400** on the entities and
deprecated queries endpoints (p31, p34). This collector runs 2500 — inside the documented bound, which is where
A1 says to stay. It also supplies a fresh example of A1's failure mode: the "6,000 per minute" figure everyone
repeats is a *bucket capacity*, and reasoning about sustained load from it without reading the algorithm would
have been the same class of error.

---

## Recommendation

**Defensible default concurrency degree: 4. Defensible configured clamp ceiling: 16.**

The vendor evidence *permits* more than revision 1 assumed — 100 req/s sustained with a 6,000-token burst bucket
puts even degree 16 inside the sustained rate on our own traffic. The recommendation stays at 4 anyway, and the
reasons have shifted from "the vendor might stop us" to reasons we control:

1. **The pool is the customer's, not ours** (p42, Tier-1). Our share is unknown and unmeasured. This is now a
   documented fact rather than an inference.
2. **We are choosing not to read the one control CrowdStrike publishes.** With `X-RateLimit-Remaining` ignored
   by contract, the collector cannot follow the vendor's stated best practice, and a 429 is the first signal it
   will get. Running further from the ceiling is the correct compensation for running blind.
3. **Memory, not the vendor, is the near-term binding constraint.** A6 (`N × one materialised batch`) is still
   OPEN, against a collector whose batch size was cut 250 → 4 specifically because objects were too large. At
   degree 16 the memory arithmetic dominates long before the rate limit does.
4. **4 is a real step change** — 4× on a request-bound phase — while keeping the blast radius of a wrong
   assumption small, and staying close enough to 1 that the degree-1 rollback is a small behavioural delta.
5. **A3a is unresolved**: whether a 429 on a Spotlight page is retried rather than surfaced as a batch failure
   was not established. Until it is, the conservative degree is the safe one.

Clamp at 16: comfortably above any degree currently justifiable, comfortably below anything that would threaten
the documented 100 req/s, and it stops a config typo putting 200 scrolls in flight.

**What would have to be measured to raise it above 4** — now cheaper than revision 1 suggested, because
`X-RateLimit-Limit` and `X-RateLimit-Remaining` arrive on **every response** (p42), so a single logged sample
answers the occupancy question:

- `X-RateLimit-Remaining` sampled on a real tenant at the degree under test — the one measurement that converts
  the shared-pool unknown into a known. Needs the deferred header work.
- Zero 429s over a full run at degree 4 on the heaviest tenant.
- Measured peak RSS at degree 4 against the 4Gi HPA threshold and 8Gi limit (A6).
- Measured page round-trip `t`. The table above assumes it.

**Sequencing**: implement at default 4, ship, measure, then revisit. Separately and outside this task, settle the
Q7 conflict with the 130-second cursor probe.

---

## What must NOT be assumed

1. **Do not assume "6,000 per minute" describes the mechanism.** It is a token bucket: 100 req/s sustained,
   6,000-token capacity, 100 tokens/s refill (`crowdstrike-auth.pdf` p42). Bursts above 100 req/s are absorbed;
   only sustained excess drains the pool. Reasoning about sustained load from the 6,000 figure alone is exactly
   the error class A1 records.
2. **Do not assume the budget is ours.** It is per-CID and pooled across all API clients and all endpoints
   (p42). Our headroom is 6,000 minus the customer's other consumers — unmeasured, and invisible without the
   headers.
3. **Do not assume `X-RateLimit-RetryAfter` is delta-seconds.** It is a UTC epoch timestamp (p43). Treating it
   as seconds sleeps for decades. There is no standard `Retry-After` header in the documented 429.
4. **Do not assume a concurrency limit exists.** Not stated in any of the three documents — verified negative
   across `crowdstrike-auth.pdf` §1.8 (p42–43), `spotlight.pdf` §1.6 (p19–20) and §1.2.6 (p4), and all of
   `discover.pdf`. Absence of a published limit is not proof none is enforced.
5. **Do not assume an expired `after` token arrives as an HTTP 404.** `spotlight.pdf` p19 says it arrives with a
   **200 OK header** and the 404 under `errors` in the body. `FalconHttpFailureClassifier.cs:22` tests the
   status line. Settle with the 130-second probe before relying on the Spotlight re-anchor path.
6. **Do not assume a 404 means cursor expiry.** The same row lists invalid ID / too many records as the
   alternative cause (p19). Tier-1, not inference.
7. **Do not assume 120s is negotiable or endpoint-specific.** Documented six times in `spotlight.pdf` and twice
   in `discover.pdf`, covering both endpoints this flow drives.
8. **Do not assume redirected requests cost one token.** Cross-cloud 308s count **twice** (p44). Confirm the
   tenant's configured cloud is correct before scaling N.
9. **Do not assume the unauthenticated limit is 15/min.** It is 5 req/s per **source IP** (not per CID), bucket
   300 (p42) — and the per-IP scope matters if pods share an egress address.
10. **Do not assume the 429 path is safe until it is read** (A3a, still OPEN).

---

## Limitations

- **No operational evidence.** No run was made, no `X-RateLimit-*` header captured, no 429 or expired-cursor
  response observed. Everything about *our* request rate is arithmetic over repo constants. A single logged
  header sample from a live CID would outrank that arithmetic and should be preferred as soon as it exists.
- **The Q7 conflict is unresolved.** The vendor document and the collector's classifier disagree about how an
  expired Spotlight cursor arrives. I could not settle it from documentation or from available run data, and I
  have deliberately not proposed a code change on the strength of a document alone.
- **Document currency.** `crowdstrike-auth.pdf` and `spotlight.pdf` are both dated 04/30/2026. They are current
  as of this task; a later revision could change any figure here.
