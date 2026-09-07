# Execution Notes — retire subset mapping (passthrough-only)

## Engine
- `Interpreter/Interpreter.cs` — `ResponseMapper.Map(response, mapping)` now ALWAYS emits the full record
  element at `records_path` (DeepClone). Removed: the mapped branch, `Fields` projection, `sourceType`
  injection, correlation injection, and the `correlationKeys`/`sourceType` params.
- `Seams/Seams.cs` + `Strategies/Mapping/DotPathMapper.cs` — `IRecordMapper.Map` simplified to
  `(response, mapping)` (passthrough). Seam + registry kept.
- `Profile/Profile.cs` — `MappingSpec`: removed `Fields`; `EmitMode` default → `"verbatim"`; valid set
  {verbatim, typed_wrapper, source_type_prefix}. Removed `SourceType` + `CorrelationKeys` from StreamSpec
  and StepSpec. `ValidateMapping(m, emitNone, at, errors)`: default verbatim, removed `mapped` + the
  correlation-keys requirement; kept type_value/envelope_fields rules (incl. the negative-misuse rejects).
- `Execution/CollectorExecutorStepHelpers.cs` — synthetic single-fetch step builder no longer sets
  SourceType/CorrelationKeys. `ApplyEnvelope` unchanged (typed_wrapper/source_type_prefix still work).
- `Execution/CollectorExecutorRunner.cs` — `mapper.Map(...)` call sites updated to `(node, step.Mapping)`;
  removed the dead `sourceType` local; comment fixed (no `mapped`).
- YAML loader uses `IgnoreUnmatchedProperties`, so any leftover `fields:`/`correlation_keys:`/`source_type:`
  in a profile is silently ignored (no load break). Shipped profiles were stripped anyway.

## Profiles (all 5 → passthrough; native shapes verified)
- tenable-io.yaml — verbatim (already).
- defender-vm.yaml — typed_wrapper (machines/software) + source_type_prefix (vulns/changes/recs/recVulns) (already).
- crowdstrike-falcon.yaml — verbatim; stripped fields/correlation_keys/source_type (native FalconAssetsPageParser = SerializeToSingleLine verbatim; no stamp).
- qualys.yaml — verbatim; stripped (native: no sourceType stamp found).
- cortex-xdr.yaml — source_type_prefix: assets type_value=endpoint, findings type_value=va_cves
  (native CortexXdrRecordFormatter stamps these); field-picking dropped per user.

## Tests (Tests/CollectorExecutor.Test) — 58/58 green
- `Emit_Mapped_DefaultMode_StillProjectsSubset` → rewritten as `Emit_DefaultMode_PassesFullRecordThrough`
  (no emit_mode ⇒ full record, nothing injected); `MappedSubsetProfile` → `DefaultModeProfile`.
- `ProcessAsync_Findings_HonorsRetryAfterInProcess_Emits3Records` — dropped the stale injected-`sourceType`
  assertion; the raw `aid` assertion now proves full-record passthrough.
- All envelope/validation/poll-and-drain/conformance tests intact.

## Docs
- `docs/yaml-contract.md` — `mapping` section rewritten to passthrough + envelope (records_path + emit_mode/
  type_value/wrapper_key/data_key/envelope_fields); removed `fields`/`correlationKeys`/`unmappedPolicy`/
  `source_type` stream field; EXCLUDED now lists field transforms; worked sketch updated.
- `docs/architecture.md` — pipeline/diagram lines updated from "map+transforms+stamp+correlationKeys" to
  "full-record passthrough + optional envelope".

## Verification
- Grep: no `"mapped"`, no `.Fields`, no `CorrelationKeys`/`SourceType` in CollectorExecutor/Strategies code
  (only narrative docs, now corrected).
- `dotnet build` clean; `dotnet test` 58/58.

## Post-review repairs (verifier-1 PASS; code-reviewer-1 — no blockers)
- Both passes flagged `Runner/DefaultProfiles.cs` (the built-in Falcon profile a new user runs first) still
  declaring `source_type:`/`fields:`/`correlation_keys:` — it read as if it projected but now emits verbatim.
  FIXED → both mapping blocks reduced to `records_path` only (matches integrations/crowdstrike-falcon.yaml).
- Accepted (not fixed): (a) ~30 test inline-profile fixtures still carry dead `fields:`/`correlation_keys:`/
  `source_type:` keys — inert (loader ignores them; tests assert passthrough/counts), cosmetic-only; (b) the
  IRecordMapper/DotPathMapper seam is now a thin delegate — kept deliberately as the codebase's named-strategy
  extension pattern; (c) a `records_path` pointing at a scalar/array-of-scalars emits zero records silently —
  pre-existing declarative behavior, out of scope (candidate debug-log later).

## Resolved assumptions
- A2: Cortex stamps `endpoint`/`va_cves` → source_type_prefix. A3: Falcon/Qualys native = verbatim (no stamp).
- A4: loader IgnoreUnmatchedProperties tolerates leftover keys (confirmed — profiles with stray keys still load).
- A5: seam signature simplified cleanly (only DotPathMapper + 3 runner call sites). A7: 58/58 (was 58; net 0 — one mapped test repurposed, no count change).
