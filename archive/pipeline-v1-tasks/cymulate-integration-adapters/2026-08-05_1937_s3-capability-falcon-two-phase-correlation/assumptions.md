# Assumptions

Every entry is OPEN. The contract-designer holds no evidence and may not write any other status.
A transition out of OPEN requires an **actor** (`researcher` / `executor` / `verifier`) and a
**citation** (vendor doc URL, `file:line`, test name, log line, correlation ID, or `research/*.md`).

## Prior Art

Tags searched: `falcon`, `crowdstrike`, `s3`, `aws`, `cymulate-integration-adapters`,
`IntegrationServiceBus`, `IntegrationInfra`, `correlation`, `batching`, `checkpoint-resume`,
`dedupe`, `definition-sourcing`, `sdk-client`.

Four relevant hits in `~/codex-state/lessons.md`. Three pointed-at task directories read (hard cap).

- **PA1 — OPEN** — Falcon batch bounds cannot be assumed from a number that "looks safe": 250 AIDs
  returned HTTP 400 on `POST /devices/entities/devices/v2` while a single-AID probe on the same
  session returned 200, and the 400 body still carried populated `resources`, favouring a rejected
  ID over a hard cap. Bears directly on lowering `AidBatchSize`.
  source: lessons.md#2026-07-26-falcon-batching (executor, 2026-07-26)
- **PA2 — OPEN** — Falcon entity IDs are stable across runs; only array order differs.
  `remediation.entities` is sorted canonically at emission specifically to fix parser `entities[0]`
  nondeterminism. Byte-compatibility depends on preserving that sort.
  source: lessons.md#2026-07-02-falcon-entity-ids (executor, 2026-07-02)
- **PA3 — OPEN** — YAML definitions reach the engine inline via the ISB dispatch payload *or* are
  self-downloaded from S3 by name. S3 is not the only definition path, so consolidating fetchers must
  not assume it is.
  source: lessons.md#2026-07-29-definition-sourcing (executor, 2026-07-29)
- **PA4 — OPEN** — `Cymulate.Integration.Client` and the retired SDK drifted in **both** directions;
  Client is not a superset. Adding a contract to Client may require a matching ISB-side change, and
  the drift must be checked bidirectionally.
  source: lessons.md#2026-08-02-package-drift (executor, 2026-08-02)

## Highest-risk assumption

- **A1 — VALIDATED (conditionally)** — A middle-ground folder inside the run's `storageUrl` is
  invisible to the parser's input discovery **only if its name does not begin with `assets` or
  `findings`**. Discovery is an undelimited boto3 string-prefix match, so it is a flat match across
  the whole subtree; Spark never sees a directory. `_staging/` is excluded; `assets_staging/` would
  be ingested.
  source: `libs/packages/utilities/helpers.py:216,239-243,281-296`,
  `libs/packages/utilities/input_resolver.py:44-60,140`,
  `research/parser-input-discovery-a1.md` (researcher, 2026-08-05)
- **A1a — OPEN** — Derived risk: the exclusion is a naming coincidence, undocumented and unenforced in
  the resolver. Any future staging name sharing the `assets`/`findings` prefix silently regresses to
  ingesting intermediate data. Mitigated only by placing staging outside the read prefix, or by a
  CI-enforced reserved-prefix convention. **Operator decision pending at the Phase A gate.**
- **A1b — OPEN** — In-process test coverage of the S3 discovery branch requires `moto`, which is not
  currently a dependency of `cymulate-integration-parsers`. The local-glob branch is assertable
  without it.

## Capability and platform

- **A2 — OPEN** — The Run message's `storageUrl` is present on every collector run and parseable by
  `S3UrlParser.ParseObject`. (Observed on one prod object's metadata; not verified as invariant.)
- **A3 — OPEN** — The IRSA policy grants the ISB pod **read** and **delete** on the data bucket, not
  only write. Delete is a distinct IAM action and the design depends on it.
- **A4 — OPEN** — Deleting the middle-ground folder is safe because nothing downstream reads it.
- **A5 — OPEN** — Publishes are Complete-or-never-exists, so a crashed write leaves no object and
  deterministic-key overwrite semantics stay clean.
- **A6 — OPEN** — `Emission`'s `MemoryPressureOptions` is applicable to read-side guarding without
  modification.

## Falcon

- **A7 — OPEN** — Phase 1 Discover completes fast enough at ~100K hosts that no replay occurs within
  it. (At 5,479 hosts the standalone assets flow wrote in ~8s; 100K is extrapolation.)
- **A8 — OPEN** — A frozen key list of ~100K aids (~3.2 MB raw) needs no paging in the chosen
  persistence.
- **A9a — VALIDATED** — There is no documented minimum for the number of values in an FQL `aid:[...]`
  list, and nothing marks a small N as syntactically pathological. The only documented FQL cap is
  "max 20 properties per statement" (distinct filter fields, not array length); this filter uses 3.
  source: `research/spotlight-batch-and-schema-limits.md` (researcher, 2026-08-05)
- **A9b — OPEN** — That the increased request count stays inside the rate-limit budget.
  CrowdStrike publishes **no** endpoint-specific rate limit. The ~6,000 req/min per-CID figure is
  **community consensus, not official**, and is shared across all endpoints and clients. Budget math
  for 50k aids: N=250 → 200 req/sweep, N=50 → 1,000, N=10 → 5,000 (near-exhausts the unofficial pool
  with no headroom). Must be settled by reading `X-RateLimit-Limit` from a live response on the real
  CID, not by trusting the community figure.
  source: `research/spotlight-batch-and-schema-limits.md` (researcher, 2026-08-05)
- **A9c — OPEN** — That no undocumented Spotlight-specific cap on `aid:[...]` length exists. No
  documented max for array length or filter/URL length was found in the API reference, FQL guide,
  falconpy or crimson-falcon. N=250 is empirically working but doc-backed in neither direction. PA1
  stands: this vendor's documented and observed caps have diverged before.
- **A10 — OPEN** — Host blocks measured at 3,377 B mean / 1,533 B per-entry-deflated are
  representative beyond the 200-record sample and the single tenant measured.
- **A11 — OPEN** — Envelope byte-compatibility is verifiable via the existing egress page-hash
  mechanism (streaming hash over the logical object's bytes, logged on the publish-completion line).

## Correlation shape

- **A12 — OPEN** — In-process correlation is sufficient for this build, and designing the call site
  to be transport-agnostic is enough to keep out-of-process available later.
- **A13 — OPEN** — The five capability primitives (frozen key list; budgeted entity store with
  remove-on-use and residue drain; durable per-key prior state; cached side inputs; lazy list +
  batch delete) cover Qualys, Tenable.io, Defender VM and Falcon without a sixth being discovered
  mid-build.

## Parser

- **A19 — OPEN** — The freshest-wins fix only helps when the two copies of a finding carry **different**
  `updated_timestamp`. On a tie the window falls through to `created_timestamp`, then to record order —
  and record order in production is lane order, i.e. **stale-first wins again**. The fix therefore
  depends on the vendor bumping `updated_timestamp` whenever a finding's content changes. That holds
  for the case this targets (a status change from open to closed *is* an update), and when nothing
  changed the copies are identical so the winner is irrelevant. It fails only if CrowdStrike mutates a
  finding without bumping the field — possible, unverified.
  Same tiebreak limitation the asset spine already has at
  `CrowdstrikeAssetsFindingsCorrelated.py:194`; not newly introduced here.
  Discovered because the original replay test hardcoded one `updated_timestamp` for both copies
  (`test_crowdstrike_live_edge_replay.py:56`), so it could not have exercised the ordering at all until
  W4 parameterised it.
  source: `CrowdstrikeAssetsFindingsCorrelated.py:116-125` (executor, 2026-08-05)

- **A14 — OPEN (narrowed)** — `updated_timestamp` present on every Spotlight finding. No explicit
  "always non-null" statement exists in CrowdStrike docs. Documented-adjacent support only: the FQL
  guide treats it as always range-filterable, and Azure Monitor's independent
  `CrowdStrikeVulnerabilities` table types it as a non-nullable `datetime`. Not a hard guarantee.
  Mitigation adopted: order by `updated_timestamp` with a `created_timestamp` fallback (documented as
  identical at creation), so a null cannot make the dedupe non-deterministic.
  source: `research/spotlight-batch-and-schema-limits.md` (researcher, 2026-08-05)
- **A17 — OPEN** — Spotlight finding `id` is a usable dedupe key. Official-adjacent evidence (falconpy
  maintainer, GitHub discussion #798): "a vulnerability is defined as the combination of Host, Product
  and CVE IDs" — so effectively unique per (aid, product, cve) within a tenant, not an ambiguous key.
  Not a formal schema guarantee. Verify against a live large pull; be ready to compose a key from
  (aid, cve.id) if `id` ever collides.
  source: `research/spotlight-batch-and-schema-limits.md` (researcher, 2026-08-05)
- **A18 — OPEN** — Cutting the Spotlight page `limit` is **not** a substitute for lowering
  `AidBatchSize`. The research recommended cutting `limit` first if smaller objects are needed; on my
  reading that conflates HTTP response buffering with published-object size. One output object is
  published per aid-batch and contains every envelope for those N hosts, so its size scales with N and
  findings-per-asset, not with page size. `limit` bounds only the transient response buffer. To be
  confirmed by the W3 worker against the emit path before the batch size is finalised.
- **A15 — OPEN** — Replacing `dropDuplicates(["id"])` with a `row_number()` window costs
  approximately the same shuffle and does not regress Glue runtime at ~200M finding rows.
- **A16 — OPEN** — The parser's split-mode and correlated-mode detection is unaffected by the new
  middle-ground folder.
