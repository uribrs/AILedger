# Decisions

## Carried in from prior art (do not re-litigate)

- Record schema is `{aid, chunk, isLastChunk, findingsInChunk, host, findings[]}` with a ~2000
  findings-per-record chunk cap; `apps` / `suppression_info` / `host_info` stripped at emission;
  `remediation.entities` sorted canonically at emission; zero-finding hosts still emit one envelope.
  (`tasks/cymulate-integration-adapters/2026-07-02_1705_falcon-correlated-findings-redesign`)
- `CollectFindings` publishes correlated `findings_*.json` only; the standalone `CollectAssets` flow
  is unchanged. (same task)
- Egress page hash — streaming hash over the logical object's bytes, logged on the publish-completion
  line — exists for all collectors and is the byte-compatibility instrument. (same task)
- Enrichment happens at existing bounded units: one Discover page for assets, one aid-batch before
  Spotlight for findings.
  (`tasks/cymulate-integration-adapters/2026-07-26_1655_falcon-prevention-policy-enrichment`)
- Physical absorption keeps Client's types out of the Infra assembly; two packages, one repo.
  (`IntegrationInfra/ai/active/2026-07-08_1720_sdk-physical-absorption`)

## Deliberate supersession of prior art

- The 2026-07-02 redesign decided *"checkpoints carry watermarks + aid-batch position only (never
  cursors); both scrolls re-anchor on 120s expiry"*. **This task supersedes the Discover half of
  that.** A watermark over a mutable field is the defect. Phase 2 walks a frozen key list; the
  watermark stops being the Discover cursor. The Spotlight-side `updated_timestamp` floor and
  per-batch seen-id dedupe are retained.
  Justification: the watermark design is what allows relocation, measured at 29.2%.

## Taken for this task

- Task directory lives in **cymulate-integration-adapters** and is canonical. The other three repos
  carry a short reference, not a copy. Falcon is the first consumer and the repo has the established
  `ai/active/` convention.
- The new contract is **not** named `IAdapterCapability` — that name is a marker interface for
  capability discovery. It is named as a functional sibling of `IAdapterDataPublisher`.
- `cymulate-integration-parsers` is in scope and branched from **master**; it has no `dev` branch.
  (Operator decision.)
- `tests/test_crowdstrike_live_edge_replay.py` stays **untracked** in the parsers repo.
  (Operator decision.)
- Correlation defaults to **in-process** for this build. The call site is written transport-agnostic
  so out-of-process stays available. Recorded rather than resolved because it materially changes
  failure handling for durable per-key prior state.
- Topologies are C# strategy classes with typed options records. A YAML descriptor is **deferred**
  until this is in production. Rationale: four collectors and three topologies do not justify a
  config language, and a second declarative interpreter beside the existing engine is a known trap.
- `AidBatchSize` default becomes **50**, config-overridable. Reasoning: cuts published object size ~5×
  (from 1.2–2.2 GiB to roughly 240–440 MB) and accumulator peak proportionally, at a 5× request-count
  multiplier that an unofficial ~6,000 req/min per-CID pool absorbs comfortably. Pure memory scaling
  would argue for N≈5–20, but that multiplies requests 12–50× against a ceiling we have **not
  verified** (A9b), so it is not a defensible default. Overridable so it can be retuned once
  `X-RateLimit-Limit` is read from a live response on the real CID.
- Findings dedupe orders by `updated_timestamp` **with a `created_timestamp` fallback**, because
  `updated_timestamp` is documented-adjacent rather than guaranteed non-null (A14). Cheap insurance
  against a null making the ordering non-deterministic.
- **Staging lives INSIDE the run prefix** at `.../{instanceId}/_staging/`, per operator decision
  ("inside the storage url, whatever's most reliable"). Reliability is bought with two enforced
  guards rather than convention, because the parser's exclusion of that folder is a naming coincidence
  in a different repo (A1, A1a):
  1. adapters-side unit test asserting the staging path constant cannot match `^(assets|findings)`
  2. parsers-side resolver test asserting a `_`-prefixed segment under the read prefix is never
     resolved
  A leading `_` is thereby reserved for non-parser artifacts.
- Deletion is garbage collection. The checkpoint remains the cursor; correctness never depends on a
  delete having happened.
- ~~Scope + pseudo code for all four repos is produced and reviewed by the operator **before** any
  implementation.~~ **SUPERSEDED by operator instruction, 2026-08-05T19:5x** ("we've scoped and
  planned enough / do the work / we'll adjust when a shape's emerged"). The Phase A review gate is
  waived and implementation proceeds directly.
  This is a deliberate operator decision, **not** decision drift — it must not be transcribed to the
  lessons ledger, because "the operator changed their mind about a review gate" teaches a future task
  nothing.
- The two gating investigations (P0, P1) still ran and completed before implementation, so the
  evidence the gate existed to produce was obtained regardless. Only the review pause was dropped.
- The object-store contract is authored by the **orchestrator in the main thread**, not by a worker.
  It is the cross-repo coupling seam; fixing it centrally lets all four repos proceed in parallel
  instead of serialising behind the Infra worker, and prevents three workers from each inventing a
  slightly different surface.
- Delete is a **separate interface** (`IAdapterObjectPruner`), not a method on the store. A deployment
  without delete permission simply does not register the pruner. This makes the permission gate
  structural rather than a runtime flag that can be forgotten.
- `moto` is **not** added to the parsers repo. The staging-exclusion guard is asserted on the local
  glob branch (no new dependency) and, more usefully, on the **producing** side in the adapters repo
  where a bad staging name originates. The S3 discovery branch stays unasserted in-process and that
  gap is recorded rather than hidden.

## Proceeding on unverified

- `Proceeding on unverified: a middle-ground folder inside storageUrl is invisible to the parser's
  input discovery. If wrong: staged intermediate assets are ingested as final parser input and
  corrupt customer output.` Settle this first — it gates Phase 1's write path.
- `Proceeding on unverified: the IRSA policy already grants read and delete on the data bucket. If
  wrong: the capability works in tests and fails in the cluster at the first delete.`
- `Proceeding on unverified: Phase 1 Discover stays fast enough at 100K hosts to be replay-free. If
  wrong: the defect reappears inside Phase 1 at large tenants, and the frozen list is built from
  duplicated reads — mitigated because the list is keyed, so duplicates collapse.`
- `Proceeding on unverified: lowering AidBatchSize does not breach a Spotlight minimum or the
  rate-limit budget. If wrong: more requests than the vendor tolerates, and the run slows or 429s.`
