# Lessons — falcon-spotlight-fetch-concurrency (2026-08-18)

## A3 — the CrowdStrike per-CID rate limit
belief:  CrowdStrike Spotlight gives us ~6,000 requests/minute of headroom to spend on concurrency.
counter: It is a token bucket — 100 req/s sustained, 6,000 burst capacity — scoped PER CUSTOMER ACCOUNT (CID)
         and pooled across every API endpoint and every API client. It is the customer's budget, shared with
         their SIEM/SOAR/scripts and any other Cymulate collector on the same CID, and its occupancy is
         invisible to us while rate-limit headers go unread. 6,000/min is the burst, not the sustained rate.
source:  FalconDocs/OfficialDocs/crowdstrike-auth.pdf §1.8 p42-43 (researcher, 2026-08-18) via
         research/crowdstrike-spotlight-throughput.md
verify:  grep -rn "X-RateLimit-Remaining" src/Cymulate.Integration.Adapters/Collectors/FalconCollector/
         (no hits = still blind to the shared pool; the ceiling is still un-measurable)
do not:  re-derive a concurrency degree as a fraction of a vendor rate number without first establishing whose
         budget it is and whether we can observe its occupancy.

## A8 — a sibling collector is not automatically a reference implementation
belief:  TenableIo already had an ordered parallel-collect/serial-publish pipeline we could port and measure.
counter: It has no such pipeline. All three Parallel.ForEachAsync sites are UNORDERED bounded fan-outs over
         order-independent sub-item work, sitting inside an already-sequential one-chunk-at-a-time foreach.
         There was nothing to port, nothing to measure against, and no Channel<> usage in either checkout.
source:  research/internal-recon.md question 5 (recon, 2026-08-18); TenableIoVulnPhase.cs:429,
         TenableIoAssetSpoolPhase.cs:323, TenableIoAssetSpine.cs:282, sequential pump at TenableIoVulnPhase.cs:215-224
verify:  grep -rn "Channel<" src/Cymulate.Integration.Adapters/Collectors/ | wc -l   (0 = still no such pattern)
do not:  plan around "collector X already does this" without opening X and checking the parallelism is the
         SHAPE you need, not merely present.

## A4a — cursor expiry may be undetectable on the Spotlight leg
belief:  An expired Spotlight `after` token surfaces as an HTTP 404, which FalconHttpFailureClassifier detects
         and the scroller re-anchors from.
counter: NEVER-TESTED, and the vendor doc contradicts the premise: spotlight.pdf p19 says this 404 "displays
         with a 200 OK header and the 404 code under `errors` in the response body". If accurate,
         FalconHttpFailureClassifier.cs:22's `StatusCode == NotFound` condition can never be true on this leg,
         FalconCursorExpiredException is never thrown, the re-anchor never fires, and a batch truncates
         silently. stats.SpotlightReanchors has no recorded occurrence anywhere in the repo.
source:  FalconDocs/OfficialDocs/spotlight.pdf p19, verified against the page image (researcher, 2026-08-18)
verify:  mint an `after` token, wait 130s, reuse it, record the status line AND the body — one request pair
do not:  treat SpotlightReanchors == 0 as evidence that cursors are not expiring, in either direction.

## A6 / A7 — what concurrency did NOT get measured
belief:  Peak memory is bounded at degree x one materialised batch, ~35-50 MB each; and no consumer of the
         published objects depends on wall-clock spacing between them.
counter: Both NEVER-TESTED. Nothing observed RSS at any degree. The 35-50 MB is a TARGET in the config comment,
         while the same paragraph's observed figure at AidBatchSize 10 was 90-125 MB — so degree 4 could be
         ~360-500 MB of live records and the clamp ceiling of 16 ~1.4-2 GB, under the 8Gi container limit but
         capable of crossing the 70%-of-4Gi HPA memory trigger. Nothing addressed spacing at all, and
         concurrency compresses it by design.
source:  review/verifier-1.md §2 (verifier, 2026-08-18); FalconCollectorConfiguration.cs:95-98
verify:  observe container RSS during a heavy-tenant run at degree 4 and compare against the 2.8Gi HPA trigger
do not:  quote the memory formula as a measurement; it is a derivation, and its per-batch input is a target.
