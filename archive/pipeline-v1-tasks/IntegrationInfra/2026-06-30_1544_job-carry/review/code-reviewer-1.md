# Code Review — Job Concern (RUN envelope parse + hydrate)

**Reviewer:** Independent senior-engineer code review (isolated)
**Stack:** C# / .NET 8, `System.Text.Json`, xUnit
**Scope reviewed:**
- `src/IntegrationInfra/Job/**` (AdapterRun* models + parsers, RunPayloadCredentialHydrator, AdapterTriggerParsing, AdapterTopics, AdapterTimeDefaults)
- `src/IntegrationInfra/Envelopes/Common/AdapterRunMetadata.cs`
- `tests/IntegrationInfra.Job.Tests/**`

**Change type:** shared library code (parsing + credential/config hydration on the inbound message path).
**Risk level:** Medium-High. This is the deserialization boundary for every inbound RUN message and the path that moves credentials/endpoint into collector config. Serialization boundaries and credential handling raise the bar even though individual methods are small.

## Accepted context (not re-litigated)
- Code relocated largely verbatim from a battle-tested source; pre-existing style preserved.
- `RunPayloadCredentialHydrator`'s untyped `Dictionary<string,string>` output **and** its best-effort catch-all swallowing are a deliberately-retained contract. I do not flag these as defects (risks noted where relevant only).
- Job -> Emission dependency for a shared default constant is known/flagged elsewhere.

---

## Severity summary
- **Blocker:** none
- **Major:** none
- **Minor:** 4
- **Nit:** 3
- **Observation:** 4

No correctness defect rises to merge-blocking. No credential-exposure defect found (see Observation O1 — handling is sound for what it does). The findings below are robustness/consistency improvements and test-coverage gaps.

---

## Minor

### M1 — Inconsistent case-sensitivity between the two action-parse paths
**File:** `AdapterRunActionParser.cs:12-15, 32-49` vs `AdapterRunEnvelopeParser.cs:10-13`
**Problem:** `TryParse` first attempts the start-envelope shape via `AdapterRunEnvelopeParser.TryParseStartEnvelope`, which uses `StrictJson` (`PropertyNameCaseInsensitive = false`). If that fails, it falls back to a flat deserialize using `FlatJsonOptions` (`PropertyNameCaseInsensitive = true`). So the same `credentials`/`product` keys are matched case-sensitively in the envelope path but case-insensitively in the flat path. The metadata-object path in `ParseOrThrow` (line 67) also uses `StrictJson`.
**Impact:** A wire payload with, say, `"Credentials"` capitalized parses under the flat fallback but not under the envelope shape. Behavior depends on which branch wins. For a "shared contract all collectors consume," this asymmetry is a latent surprise — the wire contract is effectively "case-sensitive unless you happen to hit the flat path."
**Recommended fix:** Pick one policy deliberately and document it. If the wire format is canonical camelCase (it is — every `JsonPropertyName` is camelCase), make the flat path `StrictJson` too. Local patch (swap the options instance), not a refactor.
**Required now?** No — defer, but record the decision. It is pre-existing carried behavior; raising it because it sits on the deserialization boundary.

### M2 — `ReadStringFromExtensionData` has a redundant exact lookup feeding an O(keys × ext) nested scan
**File:** `RunPayloadCredentialHydrator.cs:85-122`
**Problem:** For each candidate key the method does (a) an exact `ext.TryGetValue(key, ...)` and then (b) a full `foreach` over `ext` doing an `OrdinalIgnoreCase` compare. The case-insensitive inner loop already subsumes the exact lookup — `kvp.Key == key` is always caught by `OrdinalIgnoreCase`. The `EndpointKeys` array also lists both `apiEndpoint`/`ApiEndpoint`, `baseUrl`/`BaseUrl`, etc., which is itself redundant once the comparison is case-insensitive.
**Impact:** Correctness is fine. It is dead/duplicated work — `EndpointKeys.Length (10) × ext.Count` per call, plus a redundant probe. `ext` is small (vendor extension keys), so this is a cold/small-N path; not a performance concern, purely a clarity/maintainability one. The doubled key list invites someone to "add the lowercase variant too" forever.
**Recommended fix:** Drop the exact `TryGetValue`, halve `EndpointKeys` to the canonical forms, and keep the single `OrdinalIgnoreCase` scan; or build the dictionary lookups once with an `OrdinalIgnoreCase` comparer. Local patch.
**Required now?** No. Carried verbatim; note as cleanup for the planned reshape seam.

### M3 — `GetAny` returns `current` untrimmed while fallbacks are trimmed
**File:** `AdapterTriggerParsing.cs:36-52`
**Problem:** When `current` is non-blank it is returned as-is (line 39-41), but values pulled from the dictionary are `.Trim()`-ed (line 47). So `GetAny(dict, "  present  ", ...)` yields `"  present  "` while a dictionary hit yields `"present"`. Asymmetric normalization for the same logical output.
**Impact:** Downstream code that treats the result as a flow name / id may carry leading/trailing whitespace only on the "already present" branch. Low blast radius but the kind of inconsistency that produces a one-off "why does this id have a space" bug.
**Recommended fix:** `return current.Trim();` on the early-return branch (mirrors `GetAnyNullable`/`NormalizeFlowName` which both trim). Local patch.
**Required now?** No.

### M4 — `TryParseBool` first delegates to `bool.TryParse`, making subsequent `"true"/"false"` arms unreachable, and silently maps unknowns to null
**File:** `AdapterTriggerParsing.cs:86-116`
**Problem:** `bool.TryParse` already handles `"true"/"false"` (case-insensitive, with surrounding whitespace), so the explicit `"true"`/`"false"` comparisons at lines 102 and 110 are dead branches. Not a bug, but it signals the author may not realize `bool.TryParse`'s coverage, and the dead arms add noise. Separately, any unrecognized non-empty value returns `null` (treated identically to "absent"), which is the intended tri-state but is easy to misread at call sites that only check `== true`.
**Impact:** Maintainability/readability; no incorrect result for current inputs.
**Recommended fix:** Remove the redundant `"true"`/`"false"` string arms. Keep `1/0/yes/no/y/n`. Optionally add an XML doc line stating "unrecognized -> null". Local patch.
**Required now?** No.

---

## Nit

### N1 — `IsJsonObjectLike` is a brittle structural pre-check
**File:** `AdapterRunActionParser.cs:52-55`. Checking only `StartsWith("{")` / `EndsWith("}")` will accept `"{garbage"` ... `"}"` and reject valid-but-whitespace-wrapped-after-trim edge inputs only if trim missed them (trim is applied, so fine). It is a cheap guard before the real parse, and the real parse catches malformed input anyway, so the guard adds little. Harmless; leave it.

### N2 — `AdapterRunEnvelopeParser.cs:2-3` and `AdapterRunActionParser.cs:2` import their own namespace (`using Cymulate.IntegrationInfra.Job;`) inside files already in that namespace
Redundant `using` self-import (also `AdapterTriggerParsing.cs:2`). Compiler ignores it. Cleanup only.

### N3 — `StrictJson` / `FlatJsonOptions` are duplicated singletons across two parser files
Each parser declares its own `JsonSerializerOptions`. Fine for now; if a third parser appears, hoist to one shared options holder to avoid drift in policy (ties into M1).

---

## Observations (no action required)

### O1 — Credential handling is sound; no secret exposure found
The hydrator never logs credential values. Plain-JSON creds are expanded into `config`; non-JSON blobs go to `_encryptedCredentials` for later decrypt. Error messages in `AdapterRunEnvelopeParser.ParseOrThrow` (lines 79-82, 128-130) include only `CorrelationId` and structural hints — **not** payload contents — so a malformed-JSON throw will not leak credentials into logs/exception messages. This is the correct choice and worth preserving explicitly if anyone later "improves" the error message to include the raw payload. Recommend a code comment on those throws warning against echoing `payload`.

### O2 — `JsonValueKind.Number => prop.Value.ToString()` round-trips raw JSON token text
**File:** `RunPayloadCredentialHydrator.cs:65`. `JsonElement.ToString()` on a Number returns the raw token (e.g. `1e3`, `1.0`, `007` is not valid JSON so won't occur). For credential/config values this is acceptable since they are almost always strings; numeric creds are unusual. The `_ => prop.Value.ToString()` default arm (line 69) would stringify a nested object/array as its raw JSON — a nested object under `credentials` would land in `config` as a JSON blob string. Unlikely given the contract, but if it ever happens the value is opaque. Behavior is defensible and matches "verbatim relocation"; flagging only so it's a known property.

### O3 — `ParseOrThrow` swallows the metadata-shape failure into the flat path correctly, but `JsonException` is the only caught type
**File:** `AdapterRunEnvelopeParser.cs:62-83`. `JsonDocument.Parse` / `Deserialize` throw `JsonException` on malformed JSON — caught and rewrapped well. Note that `TryParseStartEnvelope` (line 55) uses a bare `catch` (line 34) so it never propagates; the outer `try` mainly guards the second (`JsonDocument.Parse`) path. The `required`-member contract on `AdapterRunStartPayload.Metadata` / `AdapterRunStartEnvelope.Payload` causes STJ to throw when absent, which the bare catch converts to `false` — that is exactly why the `"{\"payload\":{}}"` test case returns false. Control flow matches intent. No change.

### O4 — `AdapterTimeDefaults.ResolveBaseDateUtcOrDefault` lookback math is correct; one theoretical edge
**File:** `AdapterTimeDefaults.cs:17-27`. `now.ToUniversalTime().Subtract(DefaultLookback)` then re-normalized. Two notes: (1) if a caller passes a `nowUtc` already Utc, `ToUniversalTime()` is a no-op — fine; if they pass `Unspecified`/`Local`, `ToUniversalTime()` interprets it as local, which is the documented .NET behavior but a caller-side foot-gun (the param is named `nowUtc`, so callers should pass Utc). (2) `now - 365 days` could only equal `DateTime.MinValue` for absurd inputs (`now` near year 1), so the sentinel collision is not a practical concern. The double-normalize (subtract from an already-Utc value, then `NormalizeUtcOrMinValue` again) is harmless. No change.

---

## Test quality and coverage

**Strengths:** The three test files are focused, fast, deterministic (injected `Now` in time tests), and the hydrator tests are explicitly framed as *characterization* tests pinning the verbatim contract — appropriate given the accepted constraint. `TryParseBool` and `TryParseUtcDateTime` cover happy + invalid + empty. Envelope theory covers null/empty/whitespace/malformed/missing-metadata.

**Gaps worth closing (Minor, additive — no production change):**

1. **`AdapterRunEnvelopeParser.ParseOrThrow` is entirely untested.** This is the primary public entry point (resolve PlatformEvent -> envelope) and the most logic-dense method in the concern: the start-envelope path, the legacy `{metadata}` path, the wrapped `{payload:{metadata}}` path, the metadata-dictionary fallback (all-six-keys gate at lines 102-107), the `JsonException` rewrap, and the final "missing envelope" throw. None are exercised. Tests skip it presumably because `PlatformEvent` is an SDK type to construct, but that is exactly the seam most likely to regress. Recommend at least: one success per shape, the dictionary-fallback success, the `ValidateOrThrow` missing-required-field throw, and the malformed-JSON `InvalidOperationException` wrap.
2. **`ValidateOrThrow` missing-required-key paths untested** (each `Require` call). A payload missing `clientID` etc. should throw with the field name; no test asserts this.
3. **Hydrator endpoint precedence untested:** the `!config.ContainsKey(endpointConfigKey)` guard (line 37) means an existing endpoint is *not* overwritten. No test pins this "don't clobber" behavior, nor the custom `endpointConfigKey` (e.g. `"baseUrl"`) parameter, nor the case-insensitive endpoint-key match in `ReadStringFromExtensionData` (M2). Given endpoint precedence affects which host credentials are sent to, pin it.
4. **Hydrator number/bool/null `JsonValueKind` arms untested** (lines 65-68). The `switch` is verbatim contract; a characterization test with `{"port":443,"enabled":true,"x":null}` would lock O2's behavior.
5. **`AdapterRunActionParser.TryParse` has no tests at all** — neither the flat-fallback path nor the case-insensitivity (M1) nor the `IsJsonObjectLike` reject.
6. **`NormalizeFlowName(null)` not tested** — line 19 (`value ?? string.Empty`) guards null but the signature is non-nullable `string`; a test documenting the null tolerance (or tightening the signature to `string?`) would clarify intent.

None of these block merge; they are the difference between "carried code" and "carried code we can refactor safely behind the planned parse->typed-job seam." Prioritize #1 (ParseOrThrow) — it is the riskiest untested surface.

---

## Bottom line
Implementation is coherent, idiomatic .NET 8, and safe to merge. No blocker/major. Credential handling and error-message hygiene are correct (O1). The substantive follow-ups are: decide the case-sensitivity policy (M1), simplify the hydrator endpoint lookup (M2), and — most important — add coverage for `ParseOrThrow`/`ValidateOrThrow` and the hydrator endpoint-precedence/value-kind arms before the planned reshape touches this code.
