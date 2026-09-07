# Code review — flatten-aware records_path resolver

Scope reviewed: `CollectorExecutor/Interpreter/Interpreter.cs` (new `JsonNav.ListAtFlattened` /
`FlattenInto` / `AddTerminal`; `ResponseMapper.Map`/`ExtractIds` switched to the flattening resolver;
`Map` array-element emit branch) and the new `Flatten_*` tests + `/qualys/detections` mock +
`QualysDetectionsProfile` in `Tests/CollectorExecutor.Test/CollectorExecutorTests.cs`.

Build: clean (0 errors). Tests: full suite 75/75 pass; the 5 `Flatten_*` tests pass.

## Verdict
Clean. No BLOCKER or MAJOR issues. The change is correct, narrowly scoped, and the invariant it was
supposed to preserve (non-flattening `At`/`StringAt`/`ListAt`) is genuinely preserved. A couple of
MINOR/NIT observations below; none require a change.

## Correctness

- `At` / `StringAt` / `ListAt` are byte-for-byte unchanged (Interpreter.cs:22-66). The flattening logic
  lives entirely in the new `ListAtFlattened`/`FlattenInto`/`AddTerminal`. Confirmed every non-records
  caller still uses the single-node resolvers: pagination cursor/next-url/watermark
  (`Strategies/Pagination/*.cs` → `StringAt`), capture/capture_list/accumulate_list and fail_if
  (`Execution/CollectorExecutorStepHelpers.cs:164,176,191,208` → `At`/`ListAt`), poll_until status +
  drain (`Execution/CollectorExecutorRunner.cs:643,654` → `StringAt`/`ListAt`). Only `Map` and
  `ExtractIds` (Interpreter.cs:155,168) call the flattening variant. The records-only contract holds.

- Flatten across a repeated parent is correct. For the Qualys shape
  `...HOST_LIST.HOST.DETECTION_LIST.DETECTION`, `XmlResponse.ToJson` makes `HOST` a `JsonArray` (2
  siblings) and `DETECTION` an array under each host. `FlattenInto` hits the array at `HOST`, re-applies
  the remaining path `DETECTION_LIST.DETECTION` to each element (Interpreter.cs:93-98), and `AddTerminal`
  spreads each terminal `DETECTION` array one level. Result 4 records for 2×2, 3 for 2+1. The
  single-host case (`HOST` is an object, not an array) still yields the detections because the object
  branch handles it — single-vs-array tolerance preserved. Both verified by `Flatten_*` tests and the
  end-to-end `/qualys/detections` adapter run.

- No over-collection / no duplication. Each descent strictly consumes path or descends into distinct
  array elements; nothing is visited twice. No under-collection in the shipped shapes.

## Termination / safety
- Recursion is genuinely bounded. Two recursion sites: (1) array fan-out (Interpreter.cs:96) recurses
  with the **same** path but onto strictly-smaller subtrees (array items), and JSON is a finite acyclic
  tree (`System.Text.Json.Nodes` cannot represent a cycle), so this terminates; (2) dot-descent
  (Interpreter.cs:120) recurses with a strictly-shorter `rest`. Depth is bounded by (path segments) ×
  (JSON nesting depth) — both finite. The doc comment's "bounded by remaining segments" is slightly
  imprecise (array fan-out keeps the same path) but the real bound holds. No stack-overflow risk on
  realistic vendor payloads; a pathologically deep *attacker-controlled* JSON tree could deepen the
  stack, but that is a pre-existing property of every recursive nav here and out of scope.

## MINOR

1. `FlattenInto` runs the exact-literal-key shortcut at **every** object level (Interpreter.cs:104),
   whereas `At` only does it at the root (Interpreter.cs:30). A divergence is only observable if a
   *nested* object contains a property whose name equals the entire remaining dotted path (e.g. a nested
   `{"a.b": ...}` reached while resolving `...a.b`). For the records-only use case this is harmless and
   arguably more consistent than `At`; no shipped profile hits it. Worth a one-line comment noting the
   recursive shortcut is intentional, or leave as-is.

2. `Map` emit policy is "object → record; one extra array level → its objects; scalar skipped"
   (Interpreter.cs:173-178). The "one more level" for array-of-arrays is a deliberate, bounded choice,
   not arbitrary: `AddTerminal` already spreads the terminal array one level, so this second level only
   triggers for a genuine array-of-arrays at the record position. It will **not** recurse N levels — a
   3-deep nesting would emit nothing for the inner arrays. That is fine as a passthrough policy (records
   are objects), but if a vendor ever legitimately nests records two array-levels deep it would silently
   drop them. No current consumer needs it; flag only so the limit is a known decision.

## NIT
- `ExtractIds` and `Map` each call `ListAtFlattened` which allocates an intermediate `List<JsonNode?>`
  then re-iterates. For large pages this is one extra list + one extra pass versus streaming via a
  callback. Negligible at realistic page sizes (bounded, page-by-page by design) — not worth the
  added complexity of a visitor.

## Test quality
- Tests are real, not trivial. `Flatten_RecordsPathCrossingRepeatedParent_FlattensNested` drives the
  actual `XmlResponse.ToJson` → `ResponseMapper.Map` path and asserts 3 vs 2 (multi-host vs single-host),
  locking both the flatten and the single-vs-array tolerance.
  `Flatten_ArrayOfArraysAtRecordPosition_EmitsInnerObjects` exercises the new `Map` array branch.
  `Flatten_IsRecordsOnly_SharedNavStillSingleNode` is a meaningful regression-lock: it asserts
  `ListAtFlattened(node,"hosts.id")` flattens to 2 while `At`/`StringAt("hosts.id")` return null — i.e.
  the cursor/next-token/watermark callers are explicitly proven NOT to flatten.
  `Flatten_QualysDetections_AcrossMultipleHosts_EmitsAll` is a full adapter run (auth → XML → flatten →
  publish) asserting 4 records, so it covers the real production path, not just the helper.
- The regression-lock is meaningful and would catch an accidental switch of `At`/`StringAt` to the
  flattening resolver.

One small coverage gap (NIT): no test asserts a `Map` *array-of-arrays with 3+ nesting levels* is
dropped (the documented limit from MINOR #2). Add only if that limit is contractual.
