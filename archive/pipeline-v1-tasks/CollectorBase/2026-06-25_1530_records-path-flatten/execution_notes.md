# Execution Notes — flatten-aware records_path

## Outcome
Fixed. `dotnet build` clean; `dotnet test CollectorBase.slnx` → **75/75** (71 prior + 4 new). The silent
zero-records defect on a `records_path` crossing a repeated element is resolved; no regression.

## What changed
- `CollectorExecutor/Interpreter/Interpreter.cs`:
  - NEW `JsonNav.ListAtFlattened(root, path)` + private `FlattenInto`/`AddTerminal` — records-only,
    flattens across arrays mid-path. `At`/`StringAt`/`ListAt` left UNCHANGED.
  - `ResponseMapper.Map` → uses `ListAtFlattened`; emit policy: object → record; terminal array (array-of-arrays)
    → its object members; scalar skipped. Passthrough/DeepClone unchanged.
  - `ResponseMapper.ExtractIds` → uses `ListAtFlattened` (A3).
- `Tests/CollectorExecutor.Test/CollectorExecutorTests.cs`:
  - `Flatten_RecordsPathCrossingRepeatedParent_FlattensNested` (multi-host 3, single-host 2).
  - `Flatten_ArrayOfArraysAtRecordPosition_EmitsInnerObjects` (3).
  - `Flatten_IsRecordsOnly_SharedNavStillSingleNode` (regression lock: ListAtFlattened flattens; At/StringAt do NOT).
  - `Flatten_QualysDetections_AcrossMultipleHosts_EmitsAll` (harness, qualys.yaml:47 shape → 4 across 2 hosts).
  - New mock endpoint `/qualys/detections` (multi-host XML w/ detections) + `QualysDetectionsProfile`.

## Observed numbers
- Before fix (proven last session): multi-host detections 0/3.
- After fix: multi-host unit 3/3, single-host 2/2, array-of-arrays 3/3, harness qualys-detections 4/4.

## Assumption resolutions
A1 implicit flatten · A2 objects + one-more-level array-of-arrays + skip scalars · A3 ExtractIds shares resolver ·
A4 single-object tolerance preserved · A5 (guardrail) At/StringAt unchanged, separate resolver. See assumptions.md.

## Residual risks
- `qualys.yaml:29`/`:65` and all other profiles use terminal-array records_path → behavior identical (verified by
  the 71 prior tests staying green).
- Other `ListAt` callers (capture_list/accumulate_list/drain_path) intentionally NOT rerouted — they are not
  records and must keep single-level semantics.
- Bounded recursion argued in flatten-note.md (remaining-path length strictly decreases; no path growth).

## Commands
- `dotnet build CollectorBase.slnx` → clean. `dotnet test CollectorBase.slnx` → 75/75.
