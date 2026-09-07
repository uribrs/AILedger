# Code Review — Falcon Prevention Policy enrichment

Reviewed: uncommitted working-tree changes on `feature/falcon-prevention-policy-enrichment`
(CrowdStrike Falcon collector + its unit test project; new `Flows/Policies/` folder).

**Change type:** feature logic inside a production collector, on the hot path of both collection flows
(assets page loop, findings aid-batch loop) and in the init/access-probe path.
**Risk level:** High — network-bound, retry-interacting, checkpoint-adjacent, per-page/per-batch execution,
changes the egress record shape and the assets flow's memory profile.
**Depth applied:** high (failure semantics, retry interaction, DOM/streaming, memory, observability).

## Verification performed

- `dotnet build` of `Cymulate.Integration.Adapters.Collectors.FalconCollector.csproj`: **succeeded, 0 warnings**
  (forced recompile of the new files, not an up-to-date no-op).
- `dotnet test --filter FalconPolicyEnrichmentTests|FalconAccessProberTests|FalconCollectorConfigurationBuilderTests`
  → **60 passed**.
- `dotnet test --filter FalconCorrelatedFindingsTests` → **27 passed, 1 skipped** (pre-existing skip).
- `dotnet test --filter FalconCollectorTests` → **26 passed**.
- Full suite deliberately not run (simulation classes hang in this environment).

## Overall assessment

Good work, and better than most enrichment bolt-ons. The decomposition is right: one enricher shared by
both flows so the two outputs cannot drift, thin single-purpose vendor clients, a run-scoped definition cache,
and a single place where the wire vocabulary is spelled. Three design decisions are genuinely well made:

- **The cache never stores a transient outcome.** `StoreNotFound` only on a confirmed 2xx omission, so an
  outage can never harden into a permanent `not_found` — this is the mistake most caches of this shape make.
- **Deep-cloning both the assignment and the cached definition per host** is correct and non-obvious
  (`JsonNode` has a single parent); the test asserting distinct instances with identical content is the right test.
- **The assignment/definition failure asymmetry** is defensible and documented: the assignment *is* the
  relationship, so failing it fails the unit; the definition is supplemental, so it degrades honestly.

Cancellation flows, `ConfigureAwait(false)` is consistent, streamed documents are disposed per row, no
unbounded parallelism, no shared mutable state across threads, AIDs deduplicated through a `HashSet`, and
one vendor request per bounded unit rather than per host. The test suite is unusually thorough for this repo
and would catch most content/shape regressions.

The material concerns are all about **failure classification** and about **what the new DOM materialization
does that the old streaming path did not**. Nothing here is dangerous enough to call a Blocker; two findings
are worth resolving before this reaches a large tenant.

---

## Findings

### 1. Major — every non-401/403 HTTP failure is treated as a transient outage, and outages are never negative-cached

`Flows/Policies/FalconPolicyEnricher.cs:137` and `:241`

**Problem.** `IsTransientDefinitionOutage` returns true for *any* `HttpRequestException` whose status is not
401/403 — including 400, 404, 405, 414 and 422, i.e. exactly the class of failure that means "our request is
wrong" and will fail identically forever. Combined with the deliberate decision not to cache an unavailable
outcome (`HydrateDefinitionsAsync` adds to `unavailable` and `continue`s without touching `_cache`), a
*persistent* definition-endpoint failure produces, for every bounded unit for the rest of the run:

- one doomed definition request per 100-ID chunk,
- a full Polly retry cycle inside it (Falcon uses `SessionRetryDefaults.CreateTransient()`),
- one `LogWarning` — and a silent `partial` / `unavailable` envelope on every host.

**Impact.** Two compounding effects. (a) Correctness of classification: a deterministic 400 (say a policy ID
the URL builder mangles, or a URL that exceeds a gateway limit at 100 IDs) is reported to the operator as a
vendor outage and silently degrades every record in the run. (b) Runtime: on a 1,000-page assets run or a
10,000-batch findings run, the retry-exhaustion cost is paid once per unit instead of once. The repo's own
retry boundary doc says the Resilience layer must not retry deterministic failures; this is that rule
inverted — it retries them and then hides them.

Note the negative-caching decision is right *as a policy for outages* (the test
`UnavailableDefinition_IsNeverCached_AndIsRetriedByTheNextUnit` encodes the intent correctly). The gap is
that there is no bound on how many times the run pays for it.

**Recommended fix (local patch, two small edits).**
1. Narrow the degrade predicate to genuinely transient conditions: `StatusCode is null` (transport/timeout),
   `>= 500`, or `429`. Everything else propagates like 401/403 does.
2. Add a run-scoped trip in the enricher: after N (2–3) consecutive chunk failures, stop issuing definition
   requests for the rest of the run and mark subsequent assignments `unavailable` directly, logging once at
   warning level. This keeps the honest `partial` envelope while removing the per-unit retry tax.

Not a refactor; both changes live inside `FalconPolicyEnricher`.

---

### 2. Major (PLAUSIBLE — depends on unverified vendor behavior) — the assignment lookup is fail-closed for the whole unit, and the assets flow can feed it fabricated AIDs

`Flows/Policies/FalconPolicyEnricher.cs:67`, `Flows/Policies/FalconDevicePolicyClient.cs:52`,
`Flows/Findings/Hosts/AidExtractor.cs:416` (new `JsonObject` overload) → `TryExtractAidFromCombinedHostId`

**Problem.** Any non-2xx from `POST /devices/entities/devices/v2` aborts the page (assets) or the batch
(findings) and, via existing recovery, the run. That is the intended asymmetry — but the assets flow now
derives its join keys with `AidExtractor`, whose own comment says the `<cid>_<suffix>` heuristic can yield a
non-AID for **unmanaged** Discover entities ("For unmanaged entities it can be `<cid>_<hash>` which is not a
Spotlight AID"); the only guard is "32 hex chars". Discover deliberately includes unmanaged assets, so a page
can consist entirely of IDs that resolve to no device entity.

If CrowdStrike answers such a request with 404 or 400 rather than `200 {"resources":[]}`, an unmanaged-heavy
assets page that previously published cleanly now hard-fails the run, repeatedly, with no way for the
operator to distinguish it from a real outage. I could not verify the endpoint's behavior for a request where
no ID resolves, so I am labelling this a plausible risk, not a confirmed defect — but the code has no defence
either way, and **no test covers 404/400 on the assignment lookup** (only 500 and 401/403).

**Impact if it manifests:** total assets-collection failure for tenants with unmanaged-heavy Discover
inventory — the exact tenants where Discover is most valuable. Fail-closed is the right default for a 5xx;
it is the wrong default for "none of these IDs exist".

**Recommended fix (local patch).** Treat `404` on the assignment lookup as the successful "no assignments"
outcome (return an empty dictionary), keeping 5xx/401/403 fatal; and add a test for it. Optionally log the
count of AIDs that came from the `id`-suffix heuristic so this is diagnosable from logs. Verifying the live
response for an all-unknown-ID batch against the lab tenant before merge would settle whether this is real.

---

### 3. Minor — DOM materialization is stricter than the streaming path it replaced: a `null` row now fails the page

`Flows/Assets/FalconAssetsPageParser.cs:103`

**Problem.** `JsonNode.Parse(item.GetRawText()) ?? throw new InvalidOperationException(...)`. `JsonNode.Parse`
returns `null` for the JSON literal `null`, so a `"resources": [null]` element now throws and takes down the
page and the run. The previous `NormalizedUtf8Json.SerializeToSingleLine` path emitted it verbatim as a
record. This directly contradicts the method's own doc comment ("a non-object row is still emitted verbatim
exactly as the streaming implementation emitted it") — the claim holds for numbers/strings/arrays but not for
`null`.

Related, same mechanism, unverified: `JsonObject` materialization (forced here by `AidExtractor.ExtractAid`
and `Attach` indexing) is believed to reject duplicate property names, which the streaming writer passed
through. I did not confirm .NET 8's exact duplicate-key behavior; flagging it only because both cases share
one root cause — the new path *parses* what the old path *copied*.

**Impact.** Low likelihood (Falcon has not been observed to emit null resources), hard failure if it happens.

**Recommended fix.** Skip a `null` row (it carries no data and no AID) or emit `JsonValue.Create((object?)null)`,
and correct the doc comment either way.

---

### 4. Minor — `JsonNode.Parse(x.GetRawText())` is the expensive detach path, used on the hottest loop in the change

`Flows/Assets/FalconAssetsPageParser.cs:103`, `Flows/Policies/FalconDevicePolicyClient.cs:106`,
`Flows/Policies/FalconPreventionPolicyClient.cs:102`

**Problem.** `GetRawText()` allocates a UTF-16 string of the entire record (2 bytes per byte of payload),
which `JsonNode.Parse` then re-parses into a fresh `JsonDocument`. The assets parser runs this for every
surviving row of every page for the whole run (up to 1,000 rows/page). A Discover host record is fat, so
this is a per-page transient of tens of MB of pure copy overhead in a codebase that already carries a
four-tier heap defense in egress specifically because heap pressure here matters.

`JsonObject.Create(item.Clone())` detaches without the string hop — `JsonElement.Clone()` is documented to
survive its owning document's disposal, which is the only property the code actually needs here. For the
device client it also avoids reparsing the assignment.

**Impact.** Allocation churn / GC pressure only; no correctness effect. Peak page footprint is genuinely
bounded (assets page clamped to 1,000 in `FalconCollectorConfigurationBuilder.cs:150`, aid batch to 5,000),
so the materialization decision itself is a reasonable tradeoff for the join — this is about how it is done,
not whether.

**Recommended fix.** Swap the three `JsonNode.Parse(...GetRawText())` sites for `JsonObject.Create(el.Clone())`
(keeping the non-object fallback in the assets parser). Local, mechanical, individually testable.

---

### 5. Minor — the capability switch does not roll back the change it would need to roll back

`Flows/Assets/FalconAssetsScrollRunner.cs:269-278`, `Flows/Policies/FalconPolicyEnricher.cs:61-65`

**Problem.** With `EnablePreventionPolicyEnrichment = false`, the assets flow still materializes the entire
page into a `JsonNode` list instead of streaming it to egress, and still rewrites every record's
`device_policies` (to a `disabled` envelope). The flag suppresses only the vendor calls.

**Impact.** If this ships and the assets flow shows heap or throughput trouble under a real tenant, flipping
the flag will not restore the previous behavior — the operator's rollback lever does not reach the change
most likely to need rolling back. The `disabled` envelope is a deliberate and good design choice
(distinguishing "off" from "no policy"), so the record-shape half is intentional; the memory-profile half
probably is not.

**Recommended fix.** Either document explicitly that the flag is a vendor-call switch and not a behavioral
rollback (one line in `FalconCollectorConfiguration.cs`), or keep the streaming path when disabled. The
former is sufficient if the bounded footprint is accepted.

---

### 6. Minor — the envelope squats on the vendor's own `device_policies` property name, with a different shape, and overwrites it

`Flows/Policies/FalconDevicePoliciesEnvelope.cs:30`, `:81`

**Problem.** `device_policies` is a real CrowdStrike field on the device entity, and it holds sibling policy
families (`sensor_update`, `firewall`, `device_control`, …) — the tests' own fixtures show them. `Attach`
unconditionally overwrites whatever is at that key with a collector-owned object of a completely different
shape (`schema_version`/`collection_status`/`prevention`). The overwrite is asserted as desired behavior
(`ExistingDevicePoliciesProperty_IsReplaced_NotDuplicated`), so it is intentional — but the *intent* was
"never emit two properties", and the side effect is "silently destroy vendor data if the vendor ever supplies
this field".

**Impact.** None today (Discover host records are not known to carry `device_policies`). It becomes a silent
data-loss bug the day Discover adds the field, and it permanently forecloses collecting a second policy
family under the same key without a schema break.

**Recommended fix.** Prefer a namespaced key the vendor will never occupy (e.g. `cym_device_policies`), or
keep the key and nest under it non-destructively. If the current name is a downstream contract already, note
that constraint in the envelope's doc comment so the next person does not have to rediscover it.

---

### 7. Nit — the policy probe re-issues the Discover request the hosts probe just made

`Processing/Validation/FalconAccessProber.cs:589` → `:633`, both building the identical URL from `:661`

`ProbeHostsAsync` fetches one Discover host and discards it; `ProbePreventionPolicyAsync` immediately fetches
the same URL again to read an AID off it. Two identical requests per init, on every invocation including
every resume. Cold path, trivial cost — but the fix is equally trivial: have the hosts probe return the AID
it already streamed.

---

### 8. Nit — dangling doc reference

`UnitTests/.../FalconPolicyEnrichmentTests.cs:20` — `<see cref="FalconPolicyFlowTests"/>` names a type that
does not exist (flow wiring actually lives in `FalconCollectorTests` / `FalconCorrelatedFindingsTests`, as
`05-testing.md` correctly states). It compiles clean here only because doc-comment warnings are off.

---

## Observations (no action required)

- `FalconAssetsScrollRunner.ToUtf8Records` needs `await Task.CompletedTask` to satisfy the async iterator —
  an unavoidable wart given the publisher takes `IAsyncEnumerable`. Fine as written.
- Enrichment issues its vendor requests while the assets page response is still inside `await using`. Harmless:
  `TopLevelJsonArrayStreamReader` reads through to root completion (it exposes `AfterToken`/`Total` afterwards),
  so the connection is already back in the pool.
- `FalconPolicyEnricher` holds unsynchronized mutable state (`_cache`). Correct today — both call sites are
  sequential loops — but it is one `Parallel.ForEachAsync` away from being wrong, and nothing says so. A single
  "not thread-safe; one instance per run, sequential units" line in the class doc would age well.
- Adding a device round-trip per findings aid-batch lengthens per-page join work, which slightly raises the
  odds of hitting the Discover after-token's 120s life. Already handled by the existing re-anchor path; expect
  marginally more `re-anchoring from last_seen watermark` warnings, not failures.
- Serialization switched from `NormalizedUtf8Json.SerializeToSingleLine` to `Encoding.UTF8.GetBytes(node.ToJsonString())`.
  This matches `FalconCorrelatedRecord.cs:73` exactly, so it is the right house pattern, and both paths use the
  default HTML-escaping encoder — no wire-format drift. `NormalizedUtf8Json` remains in use by six other
  collectors, so nothing became dead code.
- `FalconCollector.cs:324` — the `afterCreateAsync` lambda converted to `async` with correct `ConfigureAwait`;
  gating on `_configuration?.EnablePreventionPolicyEnrichment == true` is safe because config is built before
  session init in the shared pipeline.

## Test quality

Strong, and materially better than the neighbouring baseline. The suite pins the things that actually break:
one request per bounded unit, AID dedup, request-body/URL construction, prevention-only projection,
verbatim preservation of unknown vendor fields and nested `prevention_settings`, all five envelope shapes,
derived `collection_status`, cache dedup across units, outage-never-cached, per-host cloning, and the
fail-vs-degrade split at both endpoints. Flow-level tests correctly assert the operational invariants
(no publish on assignment failure, no Spotlight work done, page published on definition failure, host never
dropped). The existing-stub updates were applied consistently rather than by loosening assertions.

Three gaps, in priority order:

1. **No coverage of deterministic 4xx on the definitions endpoint** (finding 1) or **404 on the assignment
   lookup** (finding 2). Both are the paths where the current classification is most likely wrong, and both
   are cheap to add to the existing `SessionStub` harness.
2. `Policy_SameSourceHost_IsCanonicallyEqualAcrossAssetsAndFindings` re-parses both sides through
   `JsonNode.Parse(...).ToJsonString()` before comparing — that normalization is precisely the difference the
   test claims to prove absent. It still catches property-set and ordering drift (its real value), but it
   cannot catch a byte-level serialization divergence. Compare the raw published lines instead.
3. Nothing asserts the `partial`/`unavailable` case is *logged* — with silent degradation as the designed
   behavior, the warning is the only operator signal, and it is untested.

## Summary

| Severity | Count |
|---|---|
| Blocker | 0 |
| Major | 2 (one plausible-pending-vendor-verification) |
| Minor | 4 |
| Nit | 2 |

Merge-worthy after findings 1 and 2 are addressed or consciously accepted with the vendor behavior in
finding 2 verified against a live tenant. Findings 3–6 are safely deferrable; 7–8 are housekeeping.
