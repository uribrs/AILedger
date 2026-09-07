# Code Review — Emit-shape mechanism (native-export parity)

Scope: `MappingSpec` emit-shape fields + validation, `ResponseMapper.Map` passthrough, `ApplyEnvelope`
+ call site, `NextUrlPaginationStrategy.AllowOffsetFallback`, the two YAML profiles, and the `Emit_*`
tests. Reviewed in isolation against the actual code.

Overall: the change is small, well-targeted, and idiomatic. The hot-path concerns flagged for scrutiny
(reparenting safety, clone correctness, watermark-vs-envelope ordering, per-page templating) are all
handled correctly. Findings below are mostly validation-completeness and test-strength gaps; no blockers.

---

## Blocker
None.

---

## Major

### M1. `type_value` / `envelope_fields` / `wrapper_key` / `data_key` are silently ignored on `mapped` and `verbatim` — no validation error
`CollectorExecutor/Profile/Profile.cs:261-279` (`ValidateFetchStep`)

Validation only checks the *positive* requirement (typed_wrapper/source_type_prefix **require**
type_value, line 274). It never rejects the *negative* misuse: a profile that sets `emit_mode: verbatim`
but also `type_value:` / `envelope_fields:`, or sets `envelope_fields:` under `mapped`. Those fields are
silently dropped at runtime (`ResponseMapper.Map` passthrough ignores them for verbatim;
`ApplyEnvelope` is never called for verbatim/mapped). For a declarative YAML contract whose whole point
is "the profile selects and parameterizes invariants," a silently-ignored knob is a latent
authoring-error trap — the operator believes a discriminator is being emitted when it is not.

This is the kind of inner-platform foot-gun the contract doc explicitly guards against (a field that
appears to do something but doesn't, depending on a sibling field's value).

Fix: in `ValidateFetchStep`, after computing `mode`, reject the contradictions:
```csharp
if (mode is "mapped" or "verbatim")
{
    if (!string.IsNullOrWhiteSpace(st.Mapping.TypeValue))
        errors.Add($"{at}.mapping.type_value is not valid for emit_mode '{mode}' (only typed_wrapper/source_type_prefix)");
    if (st.Mapping.EnvelopeFields is { Count: > 0 })
        errors.Add($"{at}.mapping.envelope_fields is only valid for emit_mode 'source_type_prefix'");
}
if (mode == "typed_wrapper" && st.Mapping.EnvelopeFields is { Count: > 0 })
    errors.Add($"{at}.mapping.envelope_fields is only valid for emit_mode 'source_type_prefix' (typed_wrapper has no extra metadata slot)");
```
`wrapper_key`/`data_key` default to non-null ("type"/"data") so they can't be cleanly distinguished from
"unset" — leave those un-validated (low value, would require nullable fields). The type_value /
envelope_fields checks are the ones that matter.

### M2. The single-fetch (sugar) path is not validated for emit modes at all
`CollectorExecutor/Profile/Profile.cs:245-252`

`ValidateFetchStep` (with all the emit_mode validation) is only invoked on the `steps`-based path. The
single-fetch sugar branch (else, lines 245-252) validates `request.path`, `records_path`, and
correlation_keys directly — it never looks at `s.Mapping.EmitMode`. So a single-fetch profile with
`emit_mode: typed_wrapper` and **no** `type_value` passes validation, then `ApplyEnvelope` runs with
`typeVal = ""` (line 890) and silently emits `{"type":"", "data":...}`. Same for an `emit_mode` typo on
the sugar path: it is not caught by line 265's whitelist; the typo falls through `Map`'s
`passthrough` check (which only matches the three valid passthrough names) and degrades to `mapped`
behavior — but then `correlation_keys` *is* required on the sugar path (line 249), so it would emit a
mapped record with sourceType under a mode the operator thinks is passthrough. Either way the sugar
path diverges from the steps path.

Note: both shipped profiles (defender-vm, tenable-io) use `steps`, so this is not currently exercised
— but the sugar path is a documented first-class form and `ResolveSteps` (runner:923) turns the sugar
into a fetch step that *will* honor EmitMode at runtime. The validation and the runtime disagree about
which path enforces the contract.

Fix: route the sugar branch through the same emit-mode checks. Simplest: have the sugar branch
construct the same effective `MappingSpec`/`StepSpec` shape and call a shared validator, or duplicate
the three emit_mode lines (whitelist + type_value requirement) into the else branch. Given the
duplication already present between the two branches, extracting a `ValidateMapping(MappingSpec, emit,
correlationKeys, at, errors)` helper called from both is the cleaner move.

---

## Minor

### m1. `source_type_prefix` collision precedence is correct but undertested and subtly surprising
`CollectorExecutor/Execution/CollectorExecutorRunner.cs:904-908`

The "metadata/discriminator wins over record keys" precedence (`if (!o.ContainsKey(prop.Key))`) is the
intended native `DefenderVmRecordFormatter` behavior and is implemented correctly: `sourceType` and each
envelope field are written first, then record props are added only if not already present — so a record
whose own `sourceType` or `recommendationReference` key would otherwise clobber the discriminator is
dropped in favor of the envelope value. Good.

But this means a record field can be *silently discarded* under `source_type_prefix` if its key collides
with `sourceType` or an `envelope_fields` key. That is the right call for parity, but it is invisible.
At minimum it deserves a one-line `_logger.LogDebug` when a collision drops a record key, and it is
**not covered by any test** (see m4). Comment on 902-903 documents the intent — keep it.

### m2. `verbatim` clone is correct, but non-object elements are silently skipped with no signal
`CollectorExecutor/Interpreter/Interpreter.cs:99-106`

`(JsonObject)src.DeepClone()` is correct: `DeepClone()` on a `JsonObject` returns a `JsonNode` whose
runtime type is `JsonObject`, the cast is safe, and the clone detaches it from the response tree so the
later reparent in `ApplyEnvelope` (typed_wrapper) cannot throw "node already has a parent." Null handling
is fine — `ListAt` never yields nulls into the `is not JsonObject` guard meaningfully (a JSON `null`
element is a `JsonNode?` null, caught by `element is not JsonObject src`).

The gap: line 102 `if (element is not JsonObject src) continue;` silently drops any array element that
isn't an object (a scalar or nested array at `records_path`). For the mapped path that was always true;
for passthrough/verbatim it means a vendor that returns e.g. `["id1","id2"]` at `records_path` emits
**nothing** with no warning. Low likelihood for the shipped profiles (both point at object arrays), but
a "verbatim" mode invites pointing `records_path` at arbitrary shapes. Consider a debug log of the
skipped-count, or at least a doc note in yaml-contract.md that passthrough requires object elements.

### m3. `Emit_SourceTypePrefix` test does not prove key-set exactness (extra keys could leak undetected)
`Tests/CollectorExecutor.Test/CollectorExecutorTests.cs:1479-1485`

The typed_wrapper and verbatim tests assert exact key-set equality
(`Assert.Equal(new[]{...}, r.Select(kv => kv.Key).OrderBy(...))`, lines 1444, 1461, 1464) — strong.
The source_type_prefix test only asserts presence of `sourceType`, `recommendationReference`, `id`,
`name`, `nested.k`. It does **not** assert the full key set, so a regression that leaked an extra key,
dropped `score`, or failed to flatten a field would pass. Add:
```csharp
Assert.Equal(new[]{ "id","name","nested","recommendationReference","score","sourceType" },
    r.Select(kv => kv.Key).OrderBy(k => k).ToArray());
```
This also incidentally locks in the metadata-first ordering claim in the comment (line 1480) only as
"present," not "first" — JSON object key order from the mock is preserved through serialization, so you
*can* assert order with `r.Select(kv=>kv.Key).First() == "sourceType"` if ordering parity matters.

### m4. No test for the discriminator/metadata-vs-record key collision (m1's precedence)
`Tests/CollectorExecutor.Test/CollectorExecutorTests.cs` (`/emit/recs` mock at 392-398)

The mock records (`id/name/nested/score`) never contain a key that collides with `sourceType` or
`recommendationReference`, so the `if (!o.ContainsKey(prop.Key))` branch at runner:907 — the most subtle
line in `ApplyEnvelope` — is never exercised in the "skip" direction. Add a record whose own field is
`sourceType` or `recommendationReference` and assert the envelope value wins (record value dropped).
This is the behavior most likely to silently break on a refactor.

### m5. `AllowOffsetFallback` default = true preserves back-compat but is the riskier default
`CollectorExecutor/Profile/Profile.cs:142`, `Strategies/Pagination/NextUrlPaginationStrategy.cs:29-35`

Defaulting to `true` keeps existing `next_url` profiles (any pre-existing OData profile) behaving as
before — correct back-compat call. The naming is clear and the inline comment on both the field (142)
and the strategy (26-31) explains the failure mode well (offset fallback on a non-`$skip` URL refetches
the same page forever). The infinite-loop clamp at strategy:33 (`Math.Max(1, spec.PageSize)`) is a good
defensive touch.

One observation, not a defect: when `AllowOffsetFallback=false` and there's no next-URL, the strategy
returns `HasMore:false` but still increments `NextPage`/keeps `NextOffset` (line 30). Harmless (the loop
breaks on `!HasMore`), but the page/offset advance is dead — `new Paginator.Step(..., NextOffset:
ctx.Offset, NextPage: ctx.Page, HasMore: false)` would read more honestly. Nit-level.

The runner also has an independent next-URL-didn't-advance guard (runner:492-496) — a belt-and-suspenders
cycle breaker. Good defense in depth; worth knowing the two interact (the `allow_offset_fallback:false`
case can't reach that guard since it returns no NextUrl).

### m6. Templating is rendered once per page (correct) — confirm no per-record waste
`CollectorExecutor/Execution/CollectorExecutorRunner.cs:888-893`

`ApplyEnvelope` renders `TypeValue` and each `EnvelopeFields` value **once** at the top (lines 890-893),
then reuses the resolved strings in the per-record loop — not re-rendered per record. Correct and
efficient. No issue; flagging only because it was called out for scrutiny.

The one subtlety: `meta` values are rendered to plain strings and assigned as `o[k] = v` (string
JsonValue). If a vendor ever needed a non-string envelope value (number/bool), this forces string — but
that matches native `AddMetadata` (string metadata) and both current uses (`recommendationReference`) are
strings. Fine as-is; not worth generalizing (resist the inner-platform pull).

---

## Nits

### n1. Watermark read under passthrough — verified correct, no change
`CollectorExecutor/Execution/CollectorExecutorRunner.cs:454-465`

The watermark loop (454-458) reads `rec[WatermarkField]` on the bare mapped/cloned record **before**
`ApplyEnvelope` runs (463-464). In passthrough modes the record is the full raw element, so the
top-level watermark field is present and read correctly; the typed_wrapper nesting (which would bury the
field under `data`) happens only afterward. Ordering is right and the comment (460-462) documents exactly
why. No action.

### n2. Duplicated request-building block across fetch / poll_until / poll_and_drain
`CollectorExecutor/Execution/CollectorExecutorRunner.cs:405-410, 653-657, 781-785`

The `BuildUrl` + `HttpMethod` + `StringContent` body-template trio is copy-pasted three times. Pre-existing,
not introduced by this change, and extraction would touch unrelated code paths — out of scope here, but
worth a cleanup ticket.

### n3. `MappingSpec` XML-doc-style comment block is good; keep it in sync with validation
`CollectorExecutor/Profile/Profile.cs:159-168`

The mode descriptions (159-168) are the clearest part of the change. If M1/M2 add negative validation,
add a one-liner there noting that type_value/envelope_fields are rejected on the modes that ignore them,
so the doc and the validator can't drift.

---

## Verification performed
- Confirmed C# operator precedence on `Profile.cs:274` (`mode is "typed_wrapper" or "source_type_prefix"
  && string.IsNullOrWhiteSpace(...)`): compiled a standalone repro — `is`-pattern `or` binds within the
  `is` operand, `&&` binds looser, so it parses as `(mode is (... or ...)) && blank`. The guard is
  correct; type_value is required only for the two passthrough-with-discriminator modes.
- Confirmed `SliceByBytes` (runner:1295-1313) serializes each record to bytes immediately, so the
  `ApplyEnvelope` reparent happens once, before serialization — no double-reparent / detached-node risk.
- Confirmed `emit_mode` typo IS caught on the steps path (Profile.cs:265 whitelist) but NOT on the
  single-fetch sugar path (M2).
