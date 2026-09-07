# Code review — retire subset mapping (passthrough + envelope only)

Scope: the regions named in the brief, reviewed in isolation. The core change is sound and
internally consistent — `ResponseMapper.Map` is a clean passthrough, the envelope path is unchanged
and still composes, and the shipped `integrations/*.yaml` are clean. Findings below are ranked.
There are **no blockers in the engine itself**; the one notable correctness issue is a stale
companion file (`Runner/DefaultProfiles.cs`) that now silently misrepresents what the engine does.

---

## Major

### M1. `Runner/DefaultProfiles.cs` still declares removed `fields:` / `correlation_keys:` / `source_type:` — now a silent lie
`Runner/DefaultProfiles.cs:22,36-37,39,53-54` (the built-in default Falcon profile).

The loader runs with `IgnoreUnmatchedProperties()` (`CollectorExecutor/Profile/Profile.cs:170`), so
these keys parse without error and are **silently dropped**. The problem is semantic, not a crash:
the `fields:` blocks claim a 5-field / 5-field projection that no longer happens. Anyone running
`Runner` with the default profile (the documented happy path in `CLAUDE.md` / README) now publishes
the **full hydrated record**, not the `{device_id, hostname, platform, last_seen}` subset the YAML
advertises. This is exactly the behavior change the migration intends — but the default profile
shipped with the live-run host still reads as if projection were in force, which is actively
misleading for the first thing a new user runs.

This is the only place in non-test source that still carries the retired vocabulary (verified:
`grep` over `CollectorExecutor/ Strategies/ Runner/` for `.Fields | SourceType | CorrelationKeys |
"mapped"` returns nothing else).

Fix: delete `source_type:`, `fields:`, and `correlation_keys:` from both streams in
`DefaultProfiles.cs`, leaving `mapping: { records_path: "resources" }` (verbatim default), matching
the cleaned `integrations/crowdstrike-falcon.yaml`.

---

## Minor

### m1. Test inline profiles carry retired keys throughout — they pass only because the loader ignores them
`Tests/CollectorExecutor.Test/CollectorExecutorTests.cs` — `fields: { id: id }` /
`correlation_keys: [id]` / `source_type: demo` appear in ~30 inline profile constants (e.g. lines
1706-1710, 1723-1724, 1737-1738, 1795-1796, 2102-2123, 2142-2143, 2155-2164, …).

These do nothing now (the records are passthrough regardless), so the tests still pass — but they
keep the retired grammar alive as copy-paste templates and undercut the "projection is gone" story:
a reader can't tell from these profiles that `fields:` is inert. Not a correctness bug (the asserts
that matter check `result.Data["records"]` counts, not projected shape), but it leaves the change
half-finished. Recommend a sweep to drop `fields:`/`correlation_keys:`/`source_type:` from the test
profiles. Lower priority than M1 because tests aren't user-facing.

Note: the **deliberately rewritten** emit tests (`Emit_Verbatim_*`, `Emit_TypedWrapper_*`,
`Emit_SourceTypePrefix_*`, `Emit_DefaultMode_PassesFullRecordThrough` at lines 1438-1566) and their
profiles (`VerbatimProfile`, `TypedWrapperProfile`, `SourceTypePrefixProfile`, `DefaultModeProfile`)
are clean and use only the new vocabulary — those are correct and assert the right things (full key
set survives, nested object intact, envelope wins on collision). The Falcon findings test change at
1584-1587 (`Assert.Contains("\"aid\"", page)`) correctly proves the raw `aid` field now survives
passthrough.

### m2. `IRecordMapper` / `ResolveMapper` / `DotPathMapper` are now pure ceremony around a 4-line passthrough
`CollectorExecutor/Seams/Seams.cs:81-85,100-101`, `Strategies/Mapping/DotPathMapper.cs:10-16`,
`CollectorExecutor/Execution/CollectorExecutorRunPreparer.cs:54`.

`DotPathMapper.Map` is a one-line delegate to `ResponseMapper.Map`. The mapper name is **hardcoded**
`registry.ResolveMapper("dotpath")` (Preparer:54) — it is not selectable from YAML, and there is no
second implementation. So the whole seam (interface + registry slot + register call + resolve call +
the `mapper` parameter threaded through five method signatures in the runner) resolves a single fixed
implementation that forwards to a static method. The runner could call `ResponseMapper.Map(...)`
directly and drop `IRecordMapper`, `ResolveMapper`/`RegisterMapper`, `DotPathMapper`, and the
`mapper` parameter entirely.

I'd flag this as over-engineered for the now-trivial operation — but it's a judgment call, not a
defect: the seam predates this change and the registry pattern is the codebase's stated extension
philosophy (`CLAUDE.md`: "a vendor quirk is a named strategy"). If a future per-vendor record shape
("flatten arrays", "unwrap single-element envelopes") is realistically foreseeable, keeping the seam
is cheap insurance. If not, this is dead abstraction the simplification should have collected.
Recommend the operator decide; if no second mapper is on the roadmap, inline it. (Note the analogous
`IPageSizeStrategy` is also hardcoded `"static"` at Preparer:55 — same shape, out of scope here.)

### m3. Validation gap: `records_path` pointing at a non-array/non-object is silently accepted and yields zero records
`CollectorExecutor/Interpreter/Interpreter.cs:92-99` + `ValidateMapping`
(`Profile/Profile.cs:266-283`).

`ValidateMapping` only checks that `records_path` is non-empty; it cannot (and shouldn't, statically)
check the runtime shape. At runtime `Map` iterates `JsonNav.ListAt(response, RecordsPath)` and keeps
only elements that are `JsonObject` (`if (element is JsonObject src)`). Consequences worth noting:
- A `records_path` resolving to a **scalar** or an **array of scalars** (e.g. an id list) yields an
  empty record set — `ListAt` returns the scalar(s), the `is JsonObject` filter drops them, nothing
  is emitted, and the stream reports success with 0 records. This is silent under-collection, not an
  error. Pre-change the projection mapper would also have produced nothing, so this is not a
  regression — but the passthrough framing makes it more surprising ("publish what you get" quietly
  drops non-objects). At minimum worth a one-line comment at Interpreter.cs:96 stating scalars are
  intentionally skipped; ideally a debug log when `ListAt` returned N>0 but all were non-objects, to
  make a mis-pointed `records_path` observable (mirrors the existing empty-`for_each` warning at
  Runner.cs:373-375).
- This is inherent to a declarative engine and arguably acceptable; flagging as a known gap, not a
  required fix.

### m4. `envelope_fields` ordering relative to the record is load-bearing but undocumented in validation
`CollectorExecutor/Execution/CollectorExecutorStepHelpers.cs:237-244`.

`ApplyEnvelope` writes `sourceType`, then `metadata` (envelope_fields), then record properties
**only if not already present** (`if (!output.ContainsKey(property.Key))`). So envelope fields win
over colliding record keys — which is the intended/tested behavior
(`Emit_SourceTypePrefix_EnvelopeWinsOnKeyCollision`, test:1498). Correct and unchanged. No fix; noting
that the "envelope wins" semantics live only in code + one test, not in `ValidateMapping`, so it's
trusted-by-test, not enforced. Fine.

---

## Nits

### n1. `MappingSpec` carries nothing vestigial — confirmed clean
`CollectorExecutor/Profile/Profile.cs:150-164`. `Fields` is gone; every remaining member
(`RecordsPath`, `EmitMode`, `TypeValue`, `WrapperKey`, `DataKey`, `EnvelopeFields`) is consumed by
`ApplyEnvelope` or the watermark/map path. `WrapperKey="type"` / `DataKey="data"` defaults are used
(StepHelpers:233) and exercised by `Emit_TypedWrapper_*`. Not dead.

### n2. `ResponseMapper.ExtractIds` is still live
`CollectorExecutor/Interpreter/Interpreter.cs:81-86` is called from the hydrate path
(`CollectorExecutorRunner.cs:241`). Not orphaned by this change.

### n3. `ValidateMapping` rules are complete and consistent for the new vocabulary
`Profile/Profile.cs:266-283`. Verified the matrix:
- default → `verbatim` (line 268) ✓
- valid set `{verbatim, typed_wrapper, source_type_prefix}` enforced (269-270) ✓
- `type_value` rejected under `verbatim` (272-273); required under `typed_wrapper`/`source_type_prefix`
  when emitting (280-281) ✓
- `envelope_fields` rejected unless `source_type_prefix` (274-275) ✓
- `emitNone` (control step) correctly skips the `records_path`/`type_value` requirements but still
  runs the negative-misuse rejects (277-282) ✓ — a control step can't emit, so type_value/envelope_fields
  being meaningless there is still caught by the mode-level checks above the `if (!emitNone)`.

No unreachable branch. One consistency observation, not a bug: the negative checks (272-275) use the
normalized `mode` local but the error messages interpolate the raw `m.EmitMode` (270) — fine, raw is
more useful in the message; the verbatim/envelope rejects don't interpolate the value so no mismatch.

### n4. Watermark/pagination behavior under always-full records — correct
`CollectorExecutorRunner.cs:276-280`. The watermark read does `rec[step.Pagination.WatermarkField!]`
on the **bare** record, and the envelope is applied **after** (285-286), so the watermark still reads
the raw top-level field. Pre-change with projection, the field had to be in the projected `fields:`
set or the watermark read would have missed it; now the full record always carries it. This is a
**strict improvement** (no silent watermark stall from a field omitted from a projection) and the
comment at 276-277 correctly explains it. `recordsForPaging` (271) counts mapped objects, so a page
of non-object elements (see m3) would count 0 and could prematurely stop `empty_page` pagination —
but no shipped profile points `records_path` at non-objects, and the existing comment at
Interpreter.cs:53-56 acknowledges this. No action.

### n5. `Seams.cs` `IRecordMapper` doc comment updated correctly
`CollectorExecutor/Seams/Seams.cs:79-85` now reads "Passthrough — no field projection" — consistent
with the implementation. Good.

---

## Summary of recommended actions
1. **M1 (do):** strip `source_type:`/`fields:`/`correlation_keys:` from `Runner/DefaultProfiles.cs` —
   the default live-run profile currently advertises a projection the engine no longer performs.
2. **m1 (should):** sweep the same three retired keys out of the test inline profiles to finish the
   retirement and stop propagating dead grammar.
3. **m2 (decide):** the `IRecordMapper`/registry/`DotPathMapper` seam is now a fixed one-line
   passthrough with no YAML selector and no second impl — inline it unless a per-vendor mapper is on
   the roadmap.
4. **m3 (optional):** add a debug log (or comment) when `records_path` resolves to N>0 non-object
   elements, so a mis-pointed path that silently emits nothing is observable.
