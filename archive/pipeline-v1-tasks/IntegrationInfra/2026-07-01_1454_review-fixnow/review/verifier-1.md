# Verifier-1 Report — review fix-now (S1 / C4 / C1)

Date: 2026-07-01
Branch: fix/review-fixnow
Verdict: **SATISFIED**

Method: inspected actual source + tests (not the notes), ran full `dotnet build IntegrationInfra.slnx`
and `dotnet test IntegrationInfra.slnx`, and grep-audited scope + assumption dispositions against source.

---

## SC1 — S1 HTTP failure-exception secret leak — **PASS**

Evidence (`src/IntegrationInfra/Conversation/AdapterHttpClient.cs`, `ThrowFailure` lines 152–198):
- Scrubbing happens at the raise site: `scrubbedBody = LogRedaction.Scrub(bodySnippet)` and
  `scrubbedUrl = LogRedaction.Scrub(request.RequestUri)` (lines 176–177), computed AFTER the classifier call.
- Message is composed from `scrubbedBody`/`scrubbedUrl` (lines 180–188); the exception is constructed with
  `url: scrubbedUrl` and `bodySnippet: scrubbedBody` (lines 193, 195). All three surfaces (Message, Url,
  BodySnippet) redacted.
- Classifier receives RAW: `_failureClassifier?.ClassifyFailure(request, response, bodySnippet ?? "", context)`
  (line 166) — before scrubbing, using the raw `bodySnippet`. Correct.
- LogError (lines 159–164) scrubs its own args inline and is NOT double-scrubbed by the new code (the new
  scrubbed locals are declared later, line 176+). No double-scrub.
- Test `AdapterHttpClientRedactionTests` is NON-tautological: drives a real failing response via stub
  `HttpMessageHandler`, body `{"client_secret":"bodysecret123",...}`, URL `?access_token=urlsecret456`.
  Verified against `LogRedaction` regexes: `client_secret:"..."` matches `KeyValuePairColon`, `access_token=...`
  matches `KeyValuePairEquals` — both replaced with `***REDACTED***`. Test asserts raw secrets absent from
  Message/BodySnippet/Url AND the `***REDACTED***` marker present. Proves masking, not identity.

## SC2 — C4 DateTimeUtc.NormalizeUtcOrMinValue — **PASS**

Evidence (`src/IntegrationInfra/Kernel/Time/DateTimeUtc.cs`):
- MinValue sentinel short-circuit preserved (returns `DateTime.MinValue` unchanged).
- Kind-switch correct: `Utc => value` (unchanged); `Local => value.ToUniversalTime()` (offset applied,
  fixing the old `SpecifyKind(Utc)` bug that dropped the offset); `_ (Unspecified) => SpecifyKind(value, Utc)`
  (relabel to UTC, no clock shift).
- XML doc explicitly states the Unspecified = assume-UTC contract and the no-clock-shift behavior.
- Test `DateTimeUtcTests` (4 facts) is machine-TZ-independent: Local case asserts
  `result == local.ToUniversalTime()` (degenerates to equality on a UTC machine, still holds) — does not
  hardcode an offset. Unspecified asserts `Kind==Utc` and `Ticks` unchanged (no shift). Utc unchanged. MinValue
  preserved.

## SC3 — C1 Conducting callback guards + terminal-publish token — **PASS**

Evidence:
- Runner (`AdapterBusEntrypointRunner.cs`): all 3 host-callback sites guarded via `SafeInvokeHostCallback`
  (try/catch → LogWarning → continue): OnCancelled (line 139), OnUnhandledException (line 175), and the
  last-word OnUnhandledException (line 266). Zero raw `.Invoke` left. Publish is never gated on callback success.
- Bus publisher (`AdapterBusFailurePublisher.cs`): all 3 OnClassifiedFlowException sites guarded via
  `SafeInvokeClassifiedCallback` (lines 39, 86, 110). Zero raw `.Invoke` left.
- Terminal-publish token: all 5 `PublishErrorAndFailureCompletionAsync` / partial-success publish arg tokens in
  the Bus publisher now pass `CancellationToken.None` (lines 25, 49, 77, 96, 120). The runner's terminal
  publishes at lines 257 & 278 were already `None` (confirming the Bus publisher was the sole live-token
  offender). Cancellable setup/collect calls (lines 45–129) correctly retain `cancellationToken`.
- Test `AdapterBusEntrypointRunnerInvariantTests.ThrowingHostCallback_DoesNotSuppressTheFailurePublish`: flow
  throws + `OnUnhandledException` throws; `RunAsync` returns normally (throwing callback does NOT propagate) and
  `ErrorRequest` publish is verified `Times.AtLeastOnce`. Proves both halves.
- The two pre-existing invariant tests (PartialSuccessWins, Cancellation_IsNackNotFailure) remain green
  (Conducting suite: 31 passed).

## SC4 — Full build clean + all 7 test projects pass + diff scoped — **PASS**

- `dotnet build IntegrationInfra.slnx`: **0 Errors**, 32 warnings — all NU1507/NU1900 (package-source /
  vuln-feed), pre-existing and accepted per constraints.
- `dotnet test IntegrationInfra.slnx`: all 7 projects pass, 0 failed / 0 skipped —
  FaultGovernance 20, Kernel 37, Emission 15, Conversation 11, Reporting 15, Job 31, Conducting 31 (160 total).
- `git diff main --name-only` modified source = exactly the 4 named files
  (AdapterHttpClient.cs, DateTimeUtc.cs, AdapterBusEntrypointRunner.cs, AdapterBusFailurePublisher.cs) plus
  touched/new test files (AdapterBusEntrypointRunnerInvariantTests.cs modified; DateTimeUtcTests.cs and
  AdapterHttpClientRedactionTests.cs new). `AdapterHttpRequestFailedException.cs` NOT touched (ctor change was
  avoidable — scrubbing done at call site). Within contract allowance.

## SC5 — Scope discipline — **PASS**

- No out-of-scope source touched. `CollectorResumeFailurePublisher.cs` UNTOUCHED and confirmed already correct
  (uses `CancellationToken.None` at lines 34 & 50; invokes no host callbacks).
- C2 (Bus/Resume + NDJSON dedup), C3 (broad test pass), M7 (UnknownFlow deny-list), TLS cert-bypass handler,
  RunPayloadCredentialHydrator catch, and resume-leg M3/M12 semantics: all unchanged — not present in the diff.

## Assumption dispositions — all TRUE against source

- **A1 (no BodySnippet/Url consumers)** — CONFIRMED. `grep .BodySnippet|.Url` across `src/` returns zero hits
  outside AdapterHttpClient.cs. Scrubbing the stored properties breaks no downstream consumer.
- **A2 (all changed Bus publishes are terminal last-words)** — CONFIRMED. All 5 are error/failure-completion or
  partial-success terminal publishes; none is a legitimately-cancellable operation.
- **A4 (resume leg has no callbacks)** — CONFIRMED. No `.Invoke` of OnUnhandled/OnCancelled/OnClassified in the
  `Conducting/Collectors/` subtree; the 3 files mentioning those names are the definition builder/source/delegate
  (declaration/wiring only), not the resume publish path. CollectorResumeFailurePublisher already uses `None`.

---

## Final Verdict: **SATISFIED**

All 5 Success Criteria PASS. Build clean (0 errors), all 160 tests green, diff limited to the named files +
touched tests, exception type and resume publisher untouched, no out-of-scope drift, and all assumption
dispositions (A1/A2/A4) hold against source. No FAIL, no PARTIAL. No behavior change beyond the three fixes.
