# Assumptions

- A1 — Implicit vs explicit flatten. STATUS: VALIDATED → IMPLICIT. The resolver auto-traverses any array
  encountered mid-path; no YAML token, `integrations/qualys.yaml:47` is unchanged. Matches the natural reading
  of "a records_path resolving through nested arrays" and fixes the profile with no edit.

- A2 — Non-object behavior at the record position. STATUS: VALIDATED. `ResponseMapper.Map` emits each
  `JsonObject`; a terminal element that is itself a `JsonArray` (array-of-arrays at the record position) yields
  its `JsonObject` members (one more level); bare scalars are skipped (no full record to publish). Locked by
  `Flatten_ArrayOfArraysAtRecordPosition_EmitsInnerObjects`.

- A3 — `ExtractIds` shares the resolver. STATUS: VALIDATED → yes. `ExtractIds` now uses `ListAtFlattened` (ids
  may sit under a repeated parent). No hydrate profile relied on the old non-flattening behavior (none cross a
  repeated element); full suite green confirms no regression.

- A4 — Single-vs-array XML tolerance preserved. STATUS: VALIDATED. A path landing on a single object still
  yields one record (single-host → 2 detections); the resolver's `AddTerminal` subsumes the old `ListAt`
  single-object tolerance. Locked by `Flatten_RecordsPathCrossingRepeatedParent_FlattensNested` (single case).

- A5 (guardrail, not open) — `At`/`StringAt` left byte-for-byte unchanged; flatten is a SEPARATE resolver
  (`JsonNav.ListAtFlattened`) consumed only by `Map` + `ExtractIds`. Cursor/next_token/watermark/capture/
  next_url nav unaffected. Locked by `Flatten_IsRecordsOnly_SharedNavStillSingleNode` + all prior pagination/
  capture tests staying green.
