# Orchestration Plan

## Complexity Decision
- Path: decompose
- Rationale: three separable workstreams with clean file partitions (fixes / paginator / merge feature);
  merge_into alone is substantial. File-touch partitioning, phase-bounded testing (full suite only at
  integration phase). Schema edits centralized in main thread to avoid three-way conflicts.

## Research Decisions
- None needed. All external behavior verified against native collectors this session (see decisions.md).
- A3 resolved inline: no deployed yaml uses workflow `capture:` with regex; error-rule `body_regex`
  matches happen at classification time (pre-conversion) and are unaffected → A3 VALIDATED.

## Worker Plan
- W1 — scope: three fixes + their tests.
  files: Mapping/ResponseMapper.cs, Mapping/MappingTransformConfig (if needed for $self/except),
  Mapping/JsonPathHelper.cs (ExtractArray object→1-element coercion), IntegrationEngine.cs
  (ONLY the lastResponseBody assignment move), engine tests Mapping/* + one Engine/ test.
  dependencies: none.
- W2 — scope: body_cursor pagination strategy + tests.
  files: Pagination/BodyCursorPaginator.cs (new), Models/PaginationConfig.cs, Pagination/PaginatorFactory.cs,
  engine tests Pagination/*. NO schema edits.
  dependencies: none (parallel with W1).
- W3 — scope: merge_into per-page enrichment + loader validation + tests.
  files: Models/WorkflowConfig.cs, Workflow/WorkflowRunner.cs, Loader/YamlIntegrationLoader.cs (validation),
  Templating (only if {{keys}} scope needs it), engine tests Workflow/*. NO schema edits.
  Semantics: a stage with merge_into suppresses the TARGET stage's publish (loader-derived); the merge
  stage replays the target's records page-wise: extract keys at `on:` anchor → fetch uncached keys via
  its operation ({{keys}}, batch_size) → embed under `as:` → publish that page → checkpoint.
  dependencies: after W1+W2 land (shares WorkflowRunner file with nobody, but sequenced to keep the
  tree stable and let W3 build on the coercion fix in its tests).

## Synthesis Approach
Main thread: integration.schema.json additions for all three constructs, validator wiring gaps,
solution build, full engine test-project run (phase boundary), reconcile execution notes.

## Verification Obligations
- Every Success Criterion in prompt_contract.md.
- Stream-through stages behaviorally unchanged (existing workflow tests must pass untouched).
- No vendor names in new engine code (grep).
- No yaml files touched (git status).
- Tests use synthetic XML modeled on the captured fixtures — real captures are NOT committed.
