# Assumptions

## Pre-task context (VALIDATED in prior tasks)

- VALIDATED: `DataPipeline/` umbrella with `Ingress/`, `Egress/`, `Json/` is in
  place. Source: prior task
  `2026-05-17_1500_shared-data-pipeline-restructure`.
- VALIDATED: `IngressStream.ReadNdjsonLinesAsync(Stream, CancellationToken)` and
  `IngressStream.Skip(IAsyncEnumerable<ReadOnlyMemory<byte>>, long, CancellationToken)`
  exist with the documented semantics (lenient, no JSON validation, no
  buffering beyond current row). Source: prior task
  `2026-05-17_1700_cortex-xdr-ingress-streaming`.
- VALIDATED (risk-accepted, decision carried forward): `cve_id` uniqueness in
  `va_cves` is not vendor-confirmed; if duplicates exist, upstream dedup is the
  fallback. Source: same prior task. This task inherits the same risk profile
  for the `endpoint_id` sort key on `va_endpoints`.

## Task decisions captured in `decisions.md` (VALIDATED on entry)

- VALIDATED: Add `va_endpoints` as third XQL stage; flow becomes
  `FindingsCves → FindingsEndpoints → Assets`. Source: operator decision.
- VALIDATED: Exact `va_endpoints` XQL query string (fields, sort, 50k limit).
  Source: operator decision.
- VALIDATED: `va_endpoints` rows publish to `findings_*.json`. Upstream routes
  by `sourceType`, not by file name. Source: operator decision.
- VALIDATED: `sourceType` field name (camelCase), three lowercase values
  (`"va_cves"`, `"va_endpoints"`, `"endpoint"`), stamped as first property,
  applied retroactively to all three streams in this task. Source: operator
  decision.
- VALIDATED: Field collision policy is fail-fast (`InvalidOperationException`).
  Differs from `DefenderVmRecordFormatter`'s silent-skip; surface in
  `decisions.md`. Source: operator decision.
- VALIDATED: `cves` field on va_endpoints rows passes through as raw bytes; no
  array/object/string normalization in the adapter. Source: operator decision.
- VALIDATED: Dataset-availability probe before va_endpoints stage; missing
  Host-Insights add-on degrades gracefully (skip stage, do not fail flow).
  Source: operator decision.
- VALIDATED: `checkpointVersion` bumps to `"3"`; add `NextVaEndpointIndex`
  (`long`); reject v2 as legacy with a clear log message. Source: operator
  decision.
- VALIDATED: Reuse `IngressStream.ReadNdjsonLinesAsync` + `Skip` for the new
  stream. No new buffering. Source: operator decision.
- VALIDATED: Trust XQL's `| sort asc endpoint_id` for determinism; no
  SHA-256 tie-breaker. Source: operator decision (mirrors va_cves risk
  acceptance).
- VALIDATED: One PR, multiple commits permitted at executor's discretion.
  Source: operator decision.

## OPEN — surfaced by the operator for explicit tracking

- **OPEN: Upstream consumer tolerance for additive `sourceType` field on
  Cortex findings/assets streams.** Default assumption: upstream schemas are
  tolerant of additive properties on existing record shapes (sourceType slips
  in as one more property). If the upstream actually applies strict-schema
  validation that would reject the new field, the operator must coordinate
  out-of-band before merging. Stop-condition: if executor or verifier
  discovers concrete evidence of strict-schema rejection, surface as a blocker
  and pause the work rather than working around it silently.
  - Why this is OPEN: upstream coordination is out of band and out of scope per
    the operator's framing. The producer side ships independently; the
    assumption is recorded so it is not forgotten.

## OPEN — implementation-detail decisions left to the executor (NOT blocking)

- **OPEN: Exact file location of `CortexXdrRecordFormatter`.** Default:
  `src/Cymulate.Integration.Adapters/Collectors/CortexXdrCollector/Processing/CortexXdrRecordFormatter.cs`,
  mirroring `DefenderVmRecordFormatter`. Executor may relocate if a sibling
  fits existing conventions better, but the type stays Cortex-local (not
  Shared) because the collision policy diverges from Defender's.
- **OPEN: Whether to plumb the new probe through
  `CortexXdrXqlClient` (preferred — already owns XQL HTTP + control-response
  parsing) or a sibling helper.** Default: extend `CortexXdrXqlClient`. Tests
  for the probe live alongside `CortexXdrXqlClient` tests if any; otherwise
  fold into `CortexXdrFindingsFlowTests`.
- **OPEN: Test-project layout for the new test cases.** Default: extend the
  existing `CortexXdrFindingsFlowTests` and add a small
  `CortexXdrCheckpointHelperTests` test for v2 rejection if one does not
  already exist. Stay consistent with the existing test directory.
- **OPEN: Stage name string casing.** Default in `decisions.md`:
  `"findingsCves"`, `"findingsEndpoints"`, `"assets"` (lowerCamel). Existing
  string is `"findings"` (lowercase). The rename ripples through tests; if
  the rename causes downstream string-comparison breakage outside the Cortex
  collector + tests, surface as a blocker rather than working around.
- **OPEN: How to expose collision-detection in
  `CortexXdrRecordFormatter`.** Default: walk the parsed `JsonElement` object
  properties looking for any property with name equal to `"sourceType"`
  (ordinal byte-level comparison, case-sensitive). On match, throw
  `InvalidOperationException` with a message naming the source type that was
  about to be stamped.

## OPEN — risk surface to track during execution

- **OPEN: Whether the `xql/get_datasets` POST is rate-limited or otherwise
  expensive enough that probing on every run is a concern.** Mitigations if it
  is: probe-once-per-process cache, or call only on the very first non-resume
  page. Default: probe every run; cheap relative to the XQL queries that
  follow. Re-evaluate only if a concrete signal arrives.
- **OPEN: Whether `xql/get_datasets` could be paged and require multiple
  calls.** The on-prem reference issues a single call with empty
  `{"request":{}}` body and parses the returned `JArray`. Default: same single
  call here; failure modes (missing root, empty array) treat va_endpoints as
  absent and skip the stage gracefully.
