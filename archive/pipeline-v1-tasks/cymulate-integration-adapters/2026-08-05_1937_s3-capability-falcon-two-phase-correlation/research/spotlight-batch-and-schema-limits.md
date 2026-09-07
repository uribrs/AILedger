# Spotlight batch bounds, rate limits, and schema — research findings

Endpoint: `GET /spotlight/combined/vulnerabilities/v1`
Filter form: `suppression_info.is_suppressed:!'true'+status:['open','reopen','closed']+aid:[<N aids>]+updated_timestamp:>='<ts>'`

## Q1a — Max values in `aid:[...]` / max filter or URL length

**ANSWER: Not documented.** No CrowdStrike source states a maximum count for values inside a bracketed FQL list, nor a max filter string or URL length for this endpoint.

- Documented (developer.crowdstrike.com FQL reference): "A FQL statement can have a maximum of **20 properties** defined." — this caps distinct *filter fields* (aid, status, updated_timestamp, …), not the number of values inside one field's array. Your filter uses 3 properties, well under 20. [FQL reference](https://developer.crowdstrike.com/api-reference/falcon-query-language/)
- No page (API reference, falconpy, crimson-falcon, FQL guide) states an array-length or URL-length ceiling for `aid:[...]`.
- **This is exactly the class of limit that diverged from docs before** on `/devices/entities/devices/v2` (400 at N=250 while docs implied no such cap, and the 400 body still carried a populated `resources`, i.e. a rejected-ID response, not a hard-cap rejection). Treat the current N=250 on Spotlight as **empirically working**, not as evidence of a documented ceiling — you have no doc-backed number either way. **Inferred/unknown**, not documented.

## Q1b — Documented minimum / reason small N is pathological

**ANSWER: No documented minimum, and nothing suggests small N is rejected.** FQL brackets accept a list of any size ≥ 1 syntactically; there is no min-length rule in any source reviewed. A single-digit N is not documented as pathological.

- **Inferred risk (not documented):** an N in the single digits multiplies request count by 25-50x relative to N=250 for the same host population, which is a real, quantifiable cost (see 1c) — not a correctness risk.

## Q1c — Rate limits for this endpoint, and the request-count budget

**ANSWER: General account-wide limit only; no endpoint-specific number is published.**

- **Documented (community-sourced, describing the general OAuth2/API gateway behavior, not this endpoint specifically):** default **6,000 requests/minute per customer account (CID)**, sliding window, shared across *all* endpoints/API clients on that CID — not additive per-endpoint. Source: Tines integration guide and multiple community write-ups; CrowdStrike's own developer docs do not publish this number on any page reviewed (the FQL/rate-limit pages only describe the `X-RateLimit-Limit` / `X-RateLimit-Remaining` / `X-RateLimit-RetryAfter` response headers, not the numeric limit itself). **Label: community consensus, not officially documented on developer.crowdstrike.com.**
- **Documented (developer.crowdstrike.com Rate Limits page):** check `X-RateLimit-Limit` / `X-RateLimit-Remaining` per response — the account's actual limit is whatever your CID's headers say, and it is stated to vary. Do not hardcode 6,000; read it from headers at runtime if the design needs a hard budget.
- **Budget math (using the unofficial 6,000/min figure, and assuming Spotlight calls share the pool with everything else this collector does):** lowering N from 250→50 aids multiplies request count 5x for the same host population; N=250→10 multiplies it 25x. For a fleet of e.g. 50,000 aids: N=250 → 200 requests/sweep; N=50 → 1,000 requests/sweep; N=10 → 5,000 requests/sweep (close to exhausting a 6,000/min budget in one sweep, with zero headroom for concurrent collector activity on other endpoints). **This is the strongest documented-adjacent argument against going to single digits.**

## Q1d — Max `limit` (page size)

**ANSWER: Documented — default 100, max 5000.** Source: [Spotlight Vulnerabilities API reference](https://developer.crowdstrike.com/api-reference/collections/spotlight-vulnerabilities/) and falconpy (`limit -- ... (default: 100, max: 5000)`). Your current `limit=2500` is within the documented ceiling and unrelated to the aid-batch-size question — `limit` bounds page size for a given filter, `aid:[N]` bounds how many hosts one filter covers. Lowering N does not require lowering `limit`.

## Q1e — Deep-pagination / offset ceiling, and whether `after` avoids it

**ANSWER: Documented for the general pagination pattern CrowdStrike reuses across combined-query endpoints — `after` is the intended workaround for a ~10k offset ceiling.** The `after` parameter's doc text (verbatim, reused CrowdStrike boilerplate — note it says "indicators," a generic term carried over from other endpoints, not vulnerability-specific phrasing): *"To access more than 10k [records], use the `after` parameter instead of `offset`."* This implies plain `offset`-based paging on `combined/*` endpoints tops out around 10k results, and `after`-token paging is CrowdStrike's documented escape hatch — which the current collector already uses correctly.
**Practical implication for batching:** smaller aid batches also reduce how many pages you must paginate through per batch before needing `after` continuation, which is a secondary (minor) argument for smaller N, but not the primary lever — memory is driven by page size × object size, not aid-batch size, per finding below.

---

## Q2 — Is `updated_timestamp` guaranteed present? Is `id` globally unique?

**`updated_timestamp` presence: documented as a real, always-relevant timestamp field, but no source states a "required/non-null" contract.** The FQL guide describes it as *"UTC date and time of the last update made on a vulnerability"* and confirms it's usable in range filters (`>=`, `<=`), which CrowdStrike would not support reliably on a frequently-null field. Azure Monitor's `CrowdStrikeVulnerabilities` table (an independent, production ingestion pipeline built on this same API) types `UpdatedTimestamp` as **`datetime`** (not `dynamic`/nullable-object), consistent with it always being populated. **Label: strongly documented-adjacent, not an explicit CrowdStrike "always non-null" guarantee.** No source anywhere describes a fallback or a null case.
**Do-not-assume:** a live run should still defensively fall back to `created_timestamp` (documented: identical to `updated_timestamp` at creation) if `updated_timestamp` is ever absent — cheap insurance, no doc claims it's needed, but none rules it out either.

**`id` uniqueness: documented behavior implies scoping by host+CVE(+product), which is effectively globally unique within your tenant's data — not stated as a bare/ambiguous key.** Per the falconpy maintainer (official-adjacent, GitHub discussion #798): *"In Spotlight, a vulnerability is defined as the combination of Host, Product and CVE IDs"* — i.e., one record per (aid, product, CVE) triple, and `aid` is already unique per sensor/asset within a CID. No source states `id` collides across CIDs or across aids for the same CVE. **Label: official-adjacent (falconpy maintainer statement), not a formal schema doc.**
**Do-not-assume:** no source explicitly confirms `id` is a single opaque globally-unique string vs. a client-side composite you must build from `aid+cve.id`. Verify on a live sample whether the API's own `id` field is already unique per row before relying on it as a dedupe key — if it collides, fall back to composing your own key from `(aid, cve.id)`.

---

## Recommended aid-batch size range

**N = 40–80**, defaulting to **~50**.

Reasoning:
- Memory: the current 250-aid batch produces 1.2–2.2 GiB objects → roughly **5–9 MB per aid** of output at `limit=2500`. Target "tens of MB" per object → **N ≈ 5–20 aids** by pure linear scaling. But:
- Request budget: going that low multiplies request count 12–50x, which is the dominant risk given the *undocumented* per-account rate limit (community figure: 6,000/min shared pool) — a fleet-wide sweep at N=10 can approach or exceed that pool by itself, with no headroom for concurrent traffic.
- **N≈50 is the balance point**: ~5x fewer aids per batch than today (→ output objects in the ~120–450 MB range, a large reduction but not yet "tens of MB" — combine with a smaller `limit` per page, e.g. 500–1000, if you need to hit the tens-of-MB target without going below N≈40 on the request-count axis) and only a 5x multiplier on request count, which a 6,000/min pool can absorb for any realistically-sized fleet.
- If tens-of-MB objects are a hard requirement even at N=50, lower `limit` (page size) first — it's cheap (no extra requests-per-record cost beyond more pages) — before pushing N into the single digits, which is where request-count risk (documented-adjacent) dominates.

## Do-not-assume list (verify against a live run)

1. **Aid-array-size ceiling** — no documented max exists; the same vendor showed a docs-vs-reality gap on a *different* endpoint (400 at 250 IDs that was actually a rejected-ID response, not a cap). Confirm N=250 (and whatever new N you pick) doesn't itself trip an undocumented cap on Spotlight specifically, independent of the memory-driven reason to lower it.
2. **The 6,000 req/min figure** — sourced from community/integration-guide writeups, not developer.crowdstrike.com. Read `X-RateLimit-Limit` from a live response for your actual CID before sizing the batch/request budget.
3. **`updated_timestamp` non-null guarantee** — no explicit "always present" statement exists anywhere; test against a real pull across a large, varied host population (stale/decommissioned assets, host-without-sensor rows where `aid` aliases asset ID) before trusting it unconditionally as a sort/dedupe key.
4. **`id` global uniqueness** — sourced from a maintainer forum comment, not a schema doc. Confirm on a live sample that `id` doesn't repeat across rows that should be distinct (or across pages/`after`-token continuations), and be ready to key dedupe on `(aid, cve.id)` instead if it does.
5. **The 10k `after`-vs-`offset` ceiling text is boilerplate** (says "indicators") reused from another endpoint family — confirm it actually governs `combined/vulnerabilities/v1` pagination behavior, though the collector's existing use of `after` already sidesteps the question either way.

## Sources

- [Spotlight Vulnerabilities — API Reference](https://developer.crowdstrike.com/api-reference/collections/spotlight-vulnerabilities/) (limit default/max, filter/after param text)
- [Falcon Query Language — API Reference](https://developer.crowdstrike.com/api-reference/falcon-query-language/) (20-properties-per-statement limit)
- [falconpy `spotlight_vulnerabilities.py` source](https://github.com/CrowdStrike/falconpy/blob/main/src/falconpy/spotlight_vulnerabilities.py) (docstring parameter confirmation)
- [falconpy Discussion #798 — Pulling all Spotlight data](https://github.com/CrowdStrike/falconpy/discussions/798) (id = host+product+CVE, dedupe-by-default sample script)
- [Falcon MCP Spotlight module](https://developer.crowdstrike.com/falcon-mcp/modules/spotlight) (aid field description, pagination.total note)
- [Azure Monitor Logs reference — CrowdStrikeVulnerabilities](https://learn.microsoft.com/en-us/azure/azure-monitor/reference/tables/CrowdStrikeVulnerabilities) (independent schema typing `UpdatedTimestamp` as non-dynamic `datetime`)
- [Axis Security — Checking your CrowdStrike rate limit](https://docs.axissecurity.com/docs/checking-your-crowdstrike-rate-limit) (header names, no numeric limit)
- [Tines — How to connect to the CrowdStrike API](https://www.tines.com/blog/getting-connected-to-the-crowdstrike-api/) (6,000 req/min community figure)
