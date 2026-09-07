# Falcon Adaptive Pressure Strategy Analysis

## Source Inputs

- Task contract: `ai/active/2026-04-30_0858_falcon-adaptive-pressure-strategy/prompt_contract.md`
- CrowdStrike docs:
  - `src/Cymulate.Integration.Adapters/Collectors/FalconCollector/FalconDocs/spotlight.pdf`
  - `src/Cymulate.Integration.Adapters/Collectors/FalconCollector/FalconDocs/discover.pdf`
- Prior recovery decisions:
  - `ai/active/2026-04-30_1022_falcon-cursor-recovery-observability/`
  - `ai/active/2026-04-28_1500_falcon-day-segment-collection/`

## Documented Vendor Constraints

- Spotlight combined vulnerabilities endpoint `/spotlight/combined/vulnerabilities/v1` supports `limit=1-5000`, default `100`, with `after` pagination.
- Discover combined hosts endpoint `/discover/combined/hosts/v1` supports `limit=1-1000`, default `100`, with `after` pagination.
- `after` tokens expire 120 seconds after a call is made.
- CrowdStrike guidance says to reduce returned records with `limit` when API requests time out.
- For maintaining a local vulnerability replica, CrowdStrike recommends chronological sorting by last updated timestamp; `updated_timestamp.asc` remains the safest default.
- Spotlight facets are `host_info`, `remediation`, `cve`, and `evaluation_logic`; this task is not allowed to change facet shape.
- CrowdStrike recommends baseline collection through the combined vulnerabilities endpoint, then differential pulls adjusted to environment volume.

## Current Behavior

- Findings flow builds one Spotlight base URL per run with fixed `limit`, facets, and sort.
- Unfiltered findings query Spotlight directly with `suppression_info.is_suppressed:!'true'` plus `updated_timestamp` floor/ceiling.
- Filtered findings treat user FQL as an asset filter, fetch AIDs from Discover, then query Spotlight with `aid:[...]` batches.
- Spotlight cursor fallback exists for:
  - cursor rejected/expired,
  - HTTP 500 with `after`,
  - repeated non-empty `after` after a non-empty page.
- Fallback drops `after`, rebuilds from `updated_timestamp >= LastWatermark`, and uses boundary IDs to avoid duplicate records at the watermark timestamp.
- Checkpoints persist `AfterToken`, `LastWatermark`, `LastWatermarkIds`, `ApiPageSize`, segment bounds, and `MonthSegmentFloorUtc`.

## Pressure and Correctness Gaps

- Current branch has `FindingsApiPageSize = 1000` as the default in `FalconCollectorConfiguration`, which matches the known-stable baseline.
- `FalconCollectorConfigurationBuilder` still clamps `findingsPageSize` and `findingsApiPageSize` to `1000`, so configured values above `1000` are silently ignored.
- In filtered findings, `VulnerabilitiesApiPageSize` is also passed to the Discover AID pre-pass. If Spotlight page size is raised above `1000`, Discover can receive an invalid `limit`.
- Resume can reuse a checkpoint `AfterToken` while rebuilding the request with the current configured page size. If the configured limit changed, this violates cursor-chain invariants.
- The collector publishes a full page before requesting the next cursor page. Larger pages increase the gap between Falcon cursor calls and raise 120-second cursor-expiry risk.
- Existing fallback recovers from unsafe cursors, but it does not by itself reduce pressure. If the page size caused a timeout/500, retrying with the same limit can repeat the failure.

## Adaptive Strategy

Use a conservative congestion-controller model:

- Stable baseline: `1000`
- Normal adaptive ceiling: `3000`
- Hard configurable maximum: `5000`
- Recovery minimum: `100`
- Scale-up ladder: `1000 -> 1500 -> 2000 -> 3000 -> 5000`
- Scale-down ladder: `5000 -> 3000 -> 2000 -> 1000 -> 500 -> 250 -> 100`

Scale up only when no active cursor chain is being reused:

- new segment/window,
- completed cursor chain,
- or after intentionally dropping `after` and resuming from watermark.

Scale down immediately on pressure:

- timeout,
- `429`,
- repeated `5xx`,
- transport cut/reset/EOF,
- cursor rejected/repeated/expired,
- cursor age approaching the 120-second expiry window,
- excessive parse/publish time or payload size.

When page size changes:

- never continue with the existing `after`;
- drop `after`;
- resume from `updated_timestamp >= LastWatermark`;
- keep boundary IDs for duplicate suppression;
- if no watermark exists, preserve the checkpoint page size until a safe boundary or fail with a resume-not-possible error instead of silently changing the chain.

If repeated pressure happens at `1000` or below:

- prefer splitting the current time window smaller over more retries at the same window size.
- dynamic window splitting is a second-phase feature unless implementation scope allows it cleanly.

## Recommended Implementation Sequence

1. Keep the safe default: `FindingsApiPageSize = 1000`.
2. Fix builder clamp for findings page size to `1..5000`.
3. Decouple filtered findings Discover AID pre-pass limit from Spotlight vulnerability page size; Discover must stay capped at `1000`.
4. Add cursor-limit compatibility handling on resume:
   - if checkpoint `AfterToken` exists and checkpoint `ApiPageSize` differs from the current configured Spotlight page size, continue the active cursor chain with checkpoint `ApiPageSize`;
   - if pressure forces a lower page size and a watermark exists, drop `after`, reduce page size, and resume from watermark;
   - if no watermark exists, do not silently change `limit` while reusing `after`.
5. Add a small local adaptive page-size helper with no external abstraction ceremony:
   - ladder values,
   - next-down calculation,
   - optional next-up calculation,
   - min/max clamping.
6. Implement pressure downshift first:
   - on Spotlight pressure with an active cursor and available watermark, reduce page size, drop `after`, rebuild from watermark, and continue.
   - on Spotlight pressure before a cursor exists, reduce page size and retry the same request without `after`.
   - keep autonomous runtime scale-up disabled until pressure downshift is verified.
   - phase-1 scale-up means configured higher Spotlight page size up to `5000`, with safe resume behavior.
7. Add focused tests for:
   - config accepts `5000` and clamps `5001`,
   - default remains `1000`,
   - Discover AID pre-pass never exceeds `1000`,
   - resume with changed page size does not reuse incompatible `after`,
   - pressure downshift drops `after` and resumes from watermark with lower `limit`,
   - legacy checkpoint loading remains compatible.

## Tradeoffs

- Larger pages reduce request count and can improve large-tenant catch-up time when Falcon is healthy.
- Larger pages increase backend query/serialization pressure, response size, retry cost, parse/publish time, and cursor-expiry risk.
- Slow scale-up avoids unstable tenants getting pushed into `5000` too early, but may underuse capacity for healthy massive tenants.
- Fast scale-down protects Falcon and cursor health, but can duplicate boundary records during watermark fallback and may increase total calls.
- Window splitting is safer for backend pressure caused by query scope, but it increases request count and state complexity.

## Implementation Boundary

This task may implement page-size defaults, clamp fixes, Discover/Spotlight limit decoupling, cursor-limit compatibility, configured scale-up to `5000`, and pressure downshift. Full autonomous runtime scale-up and dynamic time-window splitting are second-phase work.
