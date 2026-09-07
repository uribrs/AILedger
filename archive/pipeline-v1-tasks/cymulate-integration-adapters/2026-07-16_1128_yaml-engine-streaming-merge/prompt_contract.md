# Prompt Contract

Role:
You are a senior .NET engineer reworking the workflow-merge subsystem of Cymulate's YAML
integration engine (`src/Cymulate.Integration.Adapters/Collectors/YamlCollector/Cymulate.Integration.Yaml.Engine`).

Goal:
Replace merge_into's held-replay execution with a streaming enrichment-sink decorator
(per-page enrich + publish + cursor checkpoint → resume forward progress), lift the
one-merge-per-target bound (chaining), and add `mode: collect` (aggregate embed) — on
branch feature/yaml-engine-declarative-enrichment, building on the uncommitted
predecessor work.

Context:
- Predecessor (complete): ai/active/2026-07-15_1746_yaml-engine-declarative-enrichment —
  read its decisions.md + execution_notes.md for shipped v1 semantics.
- Current v1 shape to REPLACE: WorkflowRunner holds a merge target's records
  (heldByStage), replays in chunks after the target completes; resume is discarded for
  merge workflows (forced-fresh branch at top of RunAsync).
- Machinery to REUSE verbatim: key extraction (record + array anchors), CanonicalKey,
  batched {{keys}} fetch, BoundedKeyCache (cross-page), embed/unmatched logic, loader
  validation scaffolding, MergeIntoConfig.
- Resume mechanics reference: IsbExecutionSink.SetPaginationState (cursor/state BEFORE
  AdvancePage invariant); YamlCollectorCheckpointState (cursor/offset/nextPage);
  paginator state objects (PaginationState.CursorValue etc.).
- See decisions.md (binding design), constraints.md (binding limits), assumptions.md
  (B2/B3/B5 need executor verification).

Constraints:
- See constraints.md — binding and complete.

Success Criteria:
1. A merge-target stage enriches and publishes PER PAGE while fetching; no held stream
   exists anywhere in the codebase afterward.
2. WorkflowCheckpoint carries the target's pagination cursor; a resumed merge workflow
   with a paginated target re-enters AT the cursor: completed pages are not re-fetched,
   and the boundary page is neither lost nor duplicated (explicit test).
3. Non-paginated targets and fingerprint mismatches restart fresh (documented, tested).
4. Two merges on one target stack in declaration order; the later merge observes the
   earlier merge's enrichment (explicit test: record-level HOST-details-style merge +
   array-anchor KB-style merge applied together — generic fixture names).
5. `mode: collect` with `on:` as list: all distinct matches for a record's key-set are
   collected into one record-level array named by `as:`, deduped by canonical source
   key; `unmatched: drop` removes records with empty collections; single-string `on:`
   embed mode unchanged (regression-tested).
6. Loader validation: lifts one-merge-per-target; still rejects forward/unknown/topicless
   targets, merge stages with topics, unparseable `on:` (string or list), mixed source
   keys in a list, missing `as:`.
7. Schema: minimal targeted additions for `mode` and `on` string-or-list; synthetic yaml
   using chaining + collect loads through the schema-validating loader.
8. All prior behavioral assertions from the 544-test suite survive (reshaped where the
   execution model changed, never weakened); full engine suite green; engine + adapter
   projects build clean.

Execution Rules:
- Read current file state first; the branch tip includes all predecessor work.
- Do not assume missing data; decisions.md settles design choices; verify B2/B3/B5.
- Respect constraints strictly; no yaml, no version bumps, no vendor names.
- Update state.json step statuses and execution_notes.md as steps complete.

Output Format:
- Code + tests on the branch (no commit).
- execution_notes.md: per-step delta summary, deviations, risks.
- state.json updated.

Stop Conditions:
- All success criteria demonstrably met.
- A constraint conflict or a falsified assumption (B2/B3/B5) that changes the design —
  stop and surface before proceeding.
