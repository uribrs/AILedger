# Verifier-1 — records_path flatten fix

Independent adversarial verification. Files inspected directly; tests run by the verifier.

## Criterion 1 — build clean
PASS. `dotnet test CollectorBase.slnx` built all projects (CollectorExecutor, Strategies, Infrastructure,
Test) with no build errors before running. Implicitly clean.

## Criterion 2 — tests (71 prior + new) green
PASS. Verifier-run command:
`dotnet test CollectorBase.slnx | tail` →
`Passed! - Failed: 0, Passed: 75, Skipped: 0, Total: 75`.
75 = 71 prior + 4 new. 0 failures. Meets the >=75 / 0-fail bar.

New tests present and correct (Tests/CollectorExecutor.Test/CollectorExecutorTests.cs):
- (a) `Flatten_RecordsPathCrossingRepeatedParent_FlattensNested` (1034): multi-host → 3, single-host → 2. PASS.
- (b) single-host = 2 asserted in same test (1049). PASS.
- (c) `Flatten_ArrayOfArraysAtRecordPosition_EmitsInnerObjects` (1052): `data` = [[a,b],[c]] → 3. PASS.
- (d) `Flatten_IsRecordsOnly_SharedNavStillSingleNode` (1059): genuine regression lock — same input
  `{"hosts":[{id:h1},{id:h2}]}`, `ListAtFlattened("hosts.id")`==2 (flattens) while `At`==null AND
  `StringAt`==null (object-only nav dies on array), plus positive control `StringAt("a.b")=="v"`. Not
  trivial — asserts both new and unchanged behavior on identical input. PASS.

## Criterion 3 — qualys.yaml:47 shape across multiple hosts (harness)
PASS. `Flatten_QualysDetections_AcrossMultipleHosts_EmitsAll` (1071) drives the REAL adapter
(`adapter.ProcessAsync` → CollectorExecutorAdapter → real session/egress; FakeHttpClientFactory only at
the HTTP transport seam). Mock `/qualys/detections` (CollectorExecutorTests.cs:252-268) returns 2 hosts ×
2 detections as XML; test asserts `records == 4`. `QualysDetectionsProfile` (2500-2515) uses
`records_path: HOST_LIST_OUTPUT.RESPONSE.HOST_LIST.HOST.DETECTION_LIST.DETECTION` — byte-identical to
integrations/qualys.yaml:47. This is a true end-to-end exercise, not a unit call.

## Criterion 4 — no regression
PASS. Full suite green (75/75). The three non-records `ListAt` callers were NOT rerouted — confirmed by
grep: `CollectorExecutorStepHelpers.cs:176` (capture_list/CaptureLists), `:191` (accumulate_list),
`CollectorExecutorRunner.cs:654` (drainPath) all still call `JsonNav.ListAt` (non-flattening). Only
`ResponseMapper.Map` (Interpreter.cs:168) and `ExtractIds` (:155) use `ListAtFlattened`.

## Criterion 5 — flatten-note.md
PASS. Present in task dir. Covers root cause, the records-only resolver (ListAtFlattened/FlattenInto/
AddTerminal), why At/StringAt left untouched (they back cursor/next_token/watermark/capture/next_url
single-node nav), A1/A2/A3/A4 resolutions, and a bounded-recursion argument.

## At / StringAt unchanged
CONFIRMED. Interpreter.cs:22-50 — `At` is still object-only dot-nav returning a single node (null on
array), `StringAt` still coerces a single JsonValue. Flatten is isolated in `ListAtFlattened` (76) /
`FlattenInto` (83) / `AddTerminal` (126), a separate code path. No edit to At/StringAt behavior.

## Over/under-collection and recursion analysis
- Over-collection: the exact-literal-key check (`FlattenInto`, Interpreter.cs:104) early-returns, so a
  path either matches a literal key OR is dot-split — never both. Mirrors `At`'s OData `@odata.nextLink`
  handling. No double-counting. SAFE.
- Multi-host trace: HOST is JsonArray(2) → fan-out → each host descends `DETECTION_LIST.DETECTION` →
  arrays of 2/2 → 4. Single-host: HOST is a lone object (XmlResponse emits object for count==1, array for
  count>1, verified in XmlResponse.cs:64-78) → descends to DETECTION array(2) → 2. Matches assertions.
- Under-collection: missing key (line 115) and scalar-mid-path (line 101) yield nothing for that branch
  without throwing — a host lacking DETECTION_LIST contributes 0, no crash. CORRECT.
- Array-of-arrays: AddTerminal adds inner arrays one level; Map (173-178) expands a terminal JsonArray's
  object members one more level. Scalars skipped. Matches A2.
- Recursion: two modes — array fan-out (line 96) keeps path, recurses on strictly smaller subtrees;
  object descent (line 120) strictly shortens `rest`. No path-growth; tree is finite (parsed document).
  Terminates; cannot loop on self-reference. Bounded-recursion claim HOLDS. No realistic stack risk
  (depth bounded by document depth, which is finite XML/JSON).

## Risks / nits
- None blocking. Minor: deep document nesting would deepen recursion, but real vendor payloads are
  shallow and bounded; not a concern.

VERDICT: PASS
