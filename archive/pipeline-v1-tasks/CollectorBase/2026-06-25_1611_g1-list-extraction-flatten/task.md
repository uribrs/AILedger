# Task: List-extraction flatten (capture_list / accumulate_list)

Complete the earlier records-path flatten fix on the correct axis: ALL list-extractions flatten across a
repeated (array) parent; only single-node navigation (`At`/`StringAt`) stays literal.

## Confirmed defect (B1, 2026-06-25 repo review — established)
`capture_list` (`CollectorExecutor/Execution/CollectorExecutorStepHelpers.cs:176`) and `accumulate_list`
(`:191`) use the non-flattening `JsonNav.ListAt`. A captured list whose path crosses a repeated element →
empty → the `for_each` over `capture.<key>` never runs → 0 emitted end-to-end. Same object-only-nav primitive
already fixed for `records_path`/ids (the `JsonNav.ListAtFlattened` resolver exists). Manifests at
`integrations/qualys.yaml` (host_ids/qids) and `integrations/defender-vm.yaml` (recommendation_ids).

## Fix
Route the two list-capture sites through `JsonNav.ListAtFlattened`, keeping their post-processing (capture_list
builds the for_each list; accumulate_list appends + dedupes). `At`/`StringAt` (single-node nav: cursor /
next_token_at / watermark / capture-scalar / next_url) stay UNCHANGED.

## Nits (document, no behavior change)
- `ListAtFlattened` exact-literal-key check runs at every object level vs root-only in `At`.
- `ResponseMapper.Map` flattens array-of-arrays one level only (bounded by design).
