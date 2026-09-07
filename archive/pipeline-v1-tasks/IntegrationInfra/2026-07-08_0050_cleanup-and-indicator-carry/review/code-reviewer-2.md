# Code Review — Consolidate duplicated `TryExtractMetadataObject`

## Scope & Calibration

- **Change type:** shared library code (single-assembly `.NET 8` class library).
- **Risk level:** Low. Pure refactor — collapses two byte-identical private helpers into one `internal` method, no behavioral change to the parsing logic itself.
- **Execution path:** RUN-envelope parsing at flow entry (`ParseOrThrow`) and best-effort metadata promotion (`ResolveForFlow`). Runs once per flow invocation — a warm/cold entry path, not a hot loop.
- **Files reviewed:**
  - `src/IntegrationInfra/Job/AdapterRunEnvelopeParser.cs` (retained home, `private` → `internal`, XML doc added)
  - `src/IntegrationInfra/Conducting/AdapterPlatformEventFactory.cs` (removed local copy, now calls the shared method)

## Assessment

**No blocking or material findings.** This is a clean, minimal deduplication done the right way. Verified:

- **Behavioral equivalence — confirmed.** The removed copy in `AdapterPlatformEventFactory` was byte-for-byte identical to the retained `AdapterRunEnvelopeParser.TryExtractMetadataObject` (same branch order, same `ValueKind` checks, same `out metadata = default` on the false path). There is zero behavioral drift at either call site.
- **Default/undefined `JsonElement` inputs — safe.** The helper's first statement, `root.TryGetProperty(...)`, throws `InvalidOperationException` when `root.ValueKind != Object`. Both call sites guard *before* calling: `AdapterPlatformEventFactory` returns early for `Undefined`/`Null`/non-`Object` payloads (lines 40–48), and `AdapterRunEnvelopeParser` gates on `doc.RootElement.ValueKind == JsonValueKind.Object` (line 63). So the shared method is never reached with a non-object root today. This matches the pre-refactor behavior exactly — the throw-on-non-object property was already present in both copies.
- **Visibility choice — correct.** `internal` is the right call: both types live in the same assembly but different namespaces (`.Job` and `.Conducting`), so `private` couldn't be shared and `public` would needlessly widen the surface. It does not leak into the package's public API.
- **Dependency direction — sound and acyclic.** `Conducting → Job` is an already-documented edge in the concern DAG (`Conducting/README.md` line 81–84 lists `Job (AdapterTopics)` as a dependency; "no concern depends on Conducting"). This change reuses that established direction rather than introducing a new one. Confirmed no reverse edge: `Job/` contains no code reference to `Conducting` (the only mention is a comment). Build passes with 0 errors.
- **Naming / comments — good.** `TryExtractMetadataObject` follows the `Try*`/`out` convention. The added XML doc accurately describes both accepted shapes and names the single-home intent; the updated inline comment in `AdapterPlatformEventFactory` correctly points to the new owner.

## Findings

### Minor — `Try*` helper can throw on a non-object root (Try-pattern contract)

- **Problem:** By convention a `Try*` method signals failure via its `bool` return, not by throwing. `TryExtractMetadataObject` will throw `InvalidOperationException` if called with a `root` whose `ValueKind` is not `Object`, because `JsonElement.TryGetProperty` throws on non-object elements.
- **Impact:** None today — both existing call sites guard the `ValueKind` upstream. The concern is latent: now that this is a shared `internal` method (a wider contract than the former `private` copies), a future third caller that trusts the `Try*` name and passes an unguarded/`default` `JsonElement` would get an exception instead of `false`. Low severity, purely forward-looking.
- **Recommended fix (optional, local patch):** make the helper self-contained by adding a guard at the top so it honors the `Try*` contract regardless of caller:
  ```csharp
  if (root.ValueKind != JsonValueKind.Object)
  {
      metadata = default;
      return false;
  }
  ```
  This lets the two call-site `ValueKind` guards remain as-is (they serve other purposes) while making the shared method robust on its own. Not required for this change to merge — defer if the team prefers to keep the diff strictly a move.
- **Refactor vs patch:** local patch, ~4 lines.

### Observation — pre-existing "Legacy" wording in comments

The `// Legacy: { "metadata": {...} }` comments describe a wire/payload shape and predate this change; they are comments, not identifiers, so they do not conflict with the naming convention against "Legacy" *identifiers*. No action.

## Verdict

Safe to merge as-is. The one Minor finding is a defer-able robustness improvement on the newly-shared helper, not a defect in current behavior.
