# Prompt Contract — Falcon Prevention Policy Enrichment

## Role

You are a senior .NET engineer extending the existing FalconCollector in
`cymulate-integration-adapters`.

## Goal

Implement CrowdStrike Prevention Policy enrichment for every Discover host
published by `CollectAssets` and correlated `CollectFindings`.

The complete implementation specification is:

`ai/active/2026-07-26_1655_falcon-prevention-policy-enrichment/source_docs/FALCON_PREVENTION_POLICY_IMPLEMENTATION_PLAN.md`

Follow it as part of this contract. `source_docs/FALCON_POLICY_COLLECTION_PLAN.md`
is superseded research and must not define delivery scope.

## Context

- Branch `feature/falcon-prevention-policy-enrichment` is already checked out and
  current with `dev`. Do not create branches, stage, commit, or push.
- The three authoritative/superseded policy documents are copied into
  `source_docs/` in this task directory for durable reference.
- The plan's §6 component placement and §12 required tests are binding.

## Required behavior

- Use `POST /devices/entities/devices/v2` to join Discover `aid` to device
  `device_id` and obtain `device_policies.prevention`.
- Hydrate deduplicated assignment IDs through
  `GET /policy/entities/prevention/v1` using repeated `ids` parameters and
  chunks of at most 100.
- Add the exact versioned `device_policies` envelope specified by the plan.
- Preserve raw assignment and definition JSON, including unknown future fields
  and nested `prevention_settings`; keep `rule_groups` as IDs.
- Enrich one asset page and one existing findings AID batch at a time.
- Make the enriched host contract canonically identical across both flows.
- Reuse the current session, OAuth, retry/rate-limit/circuit-breaker,
  publishing, checkpoint, and recovery components.
- Add a default-enabled configuration switch and extend staged access probing.

## Placement and wiring (operator-mandated)

- Falcon policy logic lives in a dedicated policy space:
  `FalconCollector/Flows/Policies/`. Names may follow the nearest existing
  Falcon convention; the folder is not optional.
- Policy collection is **not** a separate flow and is **not** duplicated per
  flow. Implement **one shared enrichment component** under `Flows/Policies/`,
  reused by both flows exactly as asset inventory is already gathered for
  findings.
- Each flow owns only the choice of its bounded unit and the call site:
  - `CollectAssets`: one materialized Discover page, enriched before publish.
  - `CollectFindings`: the existing `freshHosts` AID batch, enriched before
    `HostFindingsAccumulator` construction and before Spotlight.
- Policies are therefore gathered on every run of both `CollectAssets` and
  `CollectFindings`. Neither flow may skip enrichment when the switch is on.
- Responsibilities stay small and separate: device client (batch request +
  parse), prevention client (definition request + parse), enricher (join,
  dedup, cache, envelope), flows (unit selection, invoke, publish, checkpoint).
- Extend `FalconUrls` for both endpoints. No new `HttpClient`, token manager,
  retry loop, or attempt counter.

## Constraints

- Prevention family only.
- No standalone policy flow, output, or parser entity.
- No rule-group recursion.
- No alert/detection/incident work.
- No production finding-shape changes except the enriched `host`.
- No generic multi-family policy abstraction or route catalog.
- No Shared infrastructure changes unless existing mechanisms are first proven
  unable to express the behavior.
- No checkpoint format change or version bump unless persisted traversal state
  actually changes; if it does, stop and justify before changing it.
- Idiomatic C# matching neighboring Falcon code: small single-responsibility
  methods and classes, no long procedural methods, no speculative abstraction,
  no new framework. This is collection plus enrichment only.
- Preserve all user worktree changes and never expose credentials.

## Failure contract

- Successful lookup, no assignment / unmatched / `resources: null` / empty:
  publish `complete` with `prevention: null`. Never drop the host.
- Assignment transient exhaustion: publish nothing for the affected page/batch,
  do not checkpoint it, throw into existing Falcon recovery; in Findings, do not
  call Spotlight for that batch.
- Definition transient exhaustion: retain the assignment, publish
  `partial`/`unavailable`, checkpoint normally.
- Definition 2xx omitting a requested ID: `complete` / `not_found`.
- 401/403 on a required endpoint, or malformed vendor contract: fail; never
  silently degrade to empty or partial.
- Cache only resolved definitions and confirmed 2xx not-found results;
  run-scoped, never checkpointed.
- Do not add a policy-specific recovery budget or attempt counter.

## Success criteria

- Every emitted Discover host in both flows has exactly one `device_policies`
  envelope; existing `device_policies` is replaced, never duplicated.
- No-policy hosts are retained.
- Assignment and definition failures follow the asymmetric contract above.
- Policy lookup is bounded and deduplicated — one assignment call per page /
  per AID batch, never per host, finding, or chunk.
- The shared enricher is the single implementation used by both flows; the same
  source host is canonically equal in Assets output and in the Findings `host`.
- Existing Assets and Findings traversal, output naming, prefetch, watermark,
  cursor recovery, segmentation, publication, and checkpoint invariants are
  preserved.
- Focused client/envelope/cache/flow/access/recovery tests pass, covering the
  plan's §12 list.
- Targeted Falcon build/test and repo-local final collector review pass.
- Touched documentation (including the relevant `ai/skills` collector skill) and
  task state are current.

## Execution rules

- Read `state.json`, then the task Markdown files, `AGENTS.md`, `ai/README.md`,
  the referenced repo-local collector skills, and current Falcon code/tests
  before editing.
- Inspect the worktree first; preserve user changes.
- Never run the full FalconCollector test suite — filter to change-relevant test
  classes.
- Implement and verify; do not return another plan.
- Update `state.json` and `execution_notes.md` while executing.
- Do not stage, commit, or push.
- Ask only when local code/docs/probe evidence cannot resolve a
  contract-breaking ambiguity.

## Output format

Report:

1. implemented behavior;
2. changed files;
3. exact build/test/review commands and results;
4. remaining risks or assumptions.

## Stop conditions

- Stop successfully only after implementation and required verification pass.
- Stop blocked only when proceeding would require a contract-breaking guess, a
  persisted-checkpoint shape change, or unavailable authority/credentials that
  cannot be safely avoided.
