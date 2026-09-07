# Code Review — fix/review-fixnow

Independent code-quality review of the uncommitted changes on branch `fix/review-fixnow`.
Stack: C# / .NET 8. Scope: only the four edited source files + associated tests.

## Calibration

- **Change type:** feature/shared-library logic (HTTP client, terminal bus publish path) + one Kernel time utility + tests.
- **Risk level:** Medium–High. The redaction fix touches a security boundary (secret leakage into logs/published error events); the callback-guard and `CancellationToken.None` changes touch the terminal failure-delivery path (last-word semantics, message ACK/NACK).
- **Execution path:** failure/exception paths — cold in the happy case but operationally critical when they run.

## Verification performed

- `dotnet build` — succeeds, 0 errors, 32 warnings (all NuGet infra: NU1507/NU1900; **none** attributable to these edits — in particular no unused-parameter warnings).
- Ran the three affected test suites: DateTimeUtc (4), Redaction (1), Conducting invariants (3) — all pass.
- Traced downstream: confirmed `AdapterHttpRequestFailedException.Message` flows into `AdapterResult.FailureResult(...)` → `LogFinalOutcome` (AdapterBusEntrypointRunner.cs:306-311) and error-event publishing, which substantiates the redaction fix's stated rationale (the message/properties genuinely bypass the direct `LogError` scrub).
- Confirmed the three `AdapterBusFailurePublisher` methods that retain a now-unused `cancellationToken` are called from `AdapterBusLegacyFlowExecutor` / `AdapterBusStrategyFlowExecutor`.

---

## Findings

### Critical
None.

### Major
None.

### Minor

**M1 — `AdapterHttpClient.ThrowFailure`: `Context={context}` remains unscrubbed in the exception message (and `Context` property).**
`AdapterHttpClient.cs:188,194` — The fix scrubs body and URL but the exception `Message` still interpolates `Context={context ?? string.Empty}` raw, and `context` is stored raw on the `Context` property.
- Impact: Low in practice. `context` is a caller-supplied operation label (`GetStringAsync(url, ct, string? context = null)`, AdapterHttpClient.cs:57), not derived from response bodies, URLs, or headers, so it is not a typical secret carrier. But it is attacker/caller-influenced free text that lands in the same logged/published message the rest of the fix is hardening.
- Recommendation: For defense-in-depth and consistency, scrub `context` too before interpolating into `message` (the `Context` property itself is not currently logged downstream, so scrubbing the message occurrence is sufficient). Local patch, not a refactor. Acceptable to defer if `context` is contractually a static literal at all call sites.

**M2 — Body char-count label is now approximate after scrubbing.**
`AdapterHttpClient.cs:182` — `Math.Min(_maxErrorSnippetChars, scrubbedBody.Length)` computes the "first N chars" label against the *scrubbed* length. Redaction replaces secrets with `***REDACTED***` (13 chars), which can lengthen the string, so the displayed count can exceed the raw snippet's real char count.
- Impact: Cosmetic only (diagnostic label wording); no correctness or security effect. The upstream read already bounds the raw body to `_maxErrorSnippetChars` (AdapterHttpClient.cs:111-116).
- Recommendation: Leave as-is, or compute the label from `Math.Min(_maxErrorSnippetChars, (bodySnippet ?? "").Length)` if the exact count matters. Nit-adjacent.

### Nit

**N1 — Unused `cancellationToken` parameters left on the three terminal publisher methods.**
`AdapterBusFailurePublisher.cs:13,36,61,108` — `PublishUnsupportedFlowAsync`, `PublishTransportFailureAsync`, `PublishClassifiedFailureAsync`, `PublishFailureAsync` now pass `CancellationToken.None` internally and never use their `cancellationToken` parameter.
- Impact: None functional. No compiler/analyzer warning is produced (verified: IDE0060 not enabled). The parameter keeps the call-site signatures in the two executors stable.
- Assessment: **Acceptable to keep.** Removing them is a churn-vs-clarity tradeoff. If kept, a one-line `// intentionally ignored: terminal publish is non-cancellable` at each site (or a `_ = cancellationToken;` discard) would document intent and pre-empt "dead parameter" review noise. Not merge-blocking either way.

**N2 — `SafeInvokeHostCallback` allocates a closure per failure.**
`AdapterBusEntrypointRunner.cs:136,172,266` — the `() => definition.OnCancelled?.Invoke(...)` lambda captures locals and allocates. This is on a cold failure/cancellation path, so the allocation is irrelevant. No action.

---

## Assessment of the specific concerns raised

**Redaction fix (AdapterHttpClient.cs):**
- Covers the message and both stored properties (`Url`, `BodySnippet`) that actually get logged/published. Verified the leak surface claim downstream — correct and necessary.
- Correctly preserves raw snippet for the classifier (`_failureClassifier?.ClassifyFailure(... bodySnippet ...)`, line 166), which the comment documents; the classifier does not log. Good.
- `LogRedaction.Scrub` handles the test inputs (`client_secret` in JSON body via `KeyValuePairColon`; `access_token` in URL query via `KeyValuePairEquals`), fails-closed on regex timeout, and is null-safe. Does not over-scrub anything needed.
- Residual surface: `Context` (M1) — minor. If the classifier returns a `classified` exception (line 167-169), that exception's message is out of scope of this fix and unchanged by it — not a regression.
- Nullability: clean — `bodySnippet ?? string.Empty` and `RequestUri?.ToString() ?? string.Empty` guard both inputs; `Scrub` never returns null.

**DateTimeUtc kind-switch (DateTimeUtc.cs):**
- All four cases correct: `MinValue` sentinel preserved (early return before the switch); `Utc` returned as-is; `Local` → `ToUniversalTime()` (this is the real bug fix — the old `SpecifyKind(Utc).ToUniversalTime()` relabelled Local as UTC first, *dropping the offset*, so a genuine local time was never converted); `Unspecified` → `SpecifyKind(Utc)` with no clock shift (assumed already-UTC). Semantics match the updated XML doc. No regression.

**Callback-guard pattern (both files):**
- try/catch is correctly placed *before* the mandatory publish in every branch, `catch (Exception)` is the right breadth for an untrusted host callback, and it logs at Warning with the exception. A throwing callback can no longer suppress the terminal publish — confirmed by the new invariant test.
- Nothing remaining can suppress the publish via the callback route. (The publish itself could still throw, but that is pre-existing behavior and out of scope.)
- `SafeInvokeHostCallback` correctly threads the `?.Invoke` null-conditional inside the guarded lambda, so a null callback is a no-op and never enters the catch.

**CancellationToken.None change (AdapterBusFailurePublisher.cs + AdapterBusEntrypointRunner.cs:257,278):**
- Applied uniformly to terminal error/completion and partial-success publishes — consistent with the documented "last words must be delivered regardless of cancellation" invariant. No terminal publish here should remain cancellable, so the change is correct.
- The `OnCancelled` path (AdapterBusEntrypointRunner.cs:136) deliberately does *not* publish (message is NACKed/requeued), so it is untouched by the None change — correct.
- Unused parameters: see N1 — acceptable.

**Test quality:**
- `DateTimeUtcTests` — asserts real `.Kind` and value/ticks; the Local test compares against `local.ToUniversalTime()` rather than a hardcoded offset, so it is TZ-independent and degenerates safely on a UTC machine. Non-tautological and deterministic. Good.
- `AdapterHttpClientRedactionTests` — asserts the secret is absent from `Message`, `BodySnippet`, `Url` AND that the redaction marker is present in the properties (so it can't pass by scrubbing everything to empty). Exercises the real code path via a stub `HttpMessageHandler`. Strong.
- `ThrowingHostCallback_DoesNotSuppressTheFailurePublish` — asserts the throwing callback does not propagate (implicit: no exception escapes `RunAsync`) and that `ErrorRequest` is still published `AtLeastOnce`. Asserts the real invariant, deterministic. Good.
- Minor gap (not blocking): no test asserts the Warning log is emitted when a callback throws, and no test covers the `AdapterBusFailurePublisher.SafeInvokeClassifiedCallback` path directly (only the runner's `OnUnhandledException` path). The logging is diagnostic and the pattern is identical, so coverage is proportionate.

---

## Summary

No blocking or major issues. The four fixes are correct, idiomatic, and well-scoped:
- Redaction covers all logged/published surfaces; leak-surface claim verified downstream. One minor residual (`Context`, M1).
- DateTimeUtc switch fixes a real Local-offset bug; all kinds correct.
- Callback guard robustly prevents host callbacks from suppressing the terminal publish.
- `CancellationToken.None` correctly enforces last-word delivery; the resulting unused parameters are acceptable (N1).

Tests are non-tautological, deterministic, TZ-independent, and assert the real properties. No regression risk identified.

**Severity counts:** Critical 0 · Major 0 · Minor 2 (M1, M2) · Nit 2 (N1, N2).
