# Execution Notes — review fix-now (S1 / C4 / C1)

## 2026-07-01 — Execution start (contract-driven-execution, direct)
Branch fix/review-fixnow. Contract read + valid. Beginning READ-FIRST (confirm A1/A2/A4) before edits.

## 2026-07-01 — Execution complete

### Assumption dispositions
- A1 VALIDATED: grep found ZERO consumers of AdapterHttpRequestFailedException.BodySnippet/.Url → safe to scrub all three surfaces (Message + Url + BodySnippet). Scrubbed at the call site; the exception type is UNTOUCHED.
- A2 VALIDATED: every AdapterBusFailurePublisher publish (PublishUnsupportedFlow/Transport/Classified/Failure + the partial-success completion) is a terminal last-word → switched to CancellationToken.None. The runner already used None (257/277) — Bus publisher was the sole live-token offender.
- A3 VALIDATED: AdapterHttpClient driven via the HttpClient ctor + a stub HttpMessageHandler + GetStringAsync.
- A4 RESOLVED (no change): CollectorResumeFailurePublisher ALREADY uses CancellationToken.None (34/50) and invokes NO host callbacks; grep confirms the whole resume subtree invokes no OnUnhandled/OnCancelled/OnClassified callbacks. So C1 is Bus-only; the resume leg has neither problem. (M12 "hook never fires on resume" is consistent — out of scope.)

### Changes
- S1: AdapterHttpClient.ThrowFailure scrubs bodySnippet + url with LogRedaction.Scrub before composing `message` and before the exception's url/bodySnippet. Classifier still gets RAW. LogError not double-scrubbed.
- C4: DateTimeUtc.NormalizeUtcOrMinValue → kind-switch (Utc=>value; Local=>ToUniversalTime(); Unspecified=>SpecifyKind(Utc)); MinValue sentinel kept; XML doc states the Unspecified=assume-UTC contract.
- C1a: guarded host callbacks via SafeInvokeHostCallback (runner: OnCancelled, OnUnhandledException x2) / SafeInvokeClassifiedCallback (AdapterBusFailurePublisher: OnClassifiedFlowException x3) — log at Warning + continue; publish never gated on callback.
- C1b: AdapterBusFailurePublisher terminal publishes → CancellationToken.None (the cancellationToken params are retained for caller-signature stability; intentionally unused).

### Tests (all green)
- Kernel DateTimeUtcTests (4): MinValue / Utc / Local (==ToUniversalTime, TZ-independent) / Unspecified (assume-UTC, no shift).
- Conversation AdapterHttpClientRedactionTests (1): secret in body + URL → thrown exception Message/BodySnippet/Url redacted, secret absent.
- Conducting AdapterBusEntrypointRunnerInvariantTests +1: throwing OnUnhandledException does not propagate + ErrorRequest still published. Existing 2 invariant tests still green.

### Build/test
dotnet test IntegrationInfra.slnx: ALL 7 projects pass — Kernel 37, Conversation 11, Emission 15, FaultGovernance 20, Reporting 15, Job 31, Conducting 31 — 0 failed/0 skipped. Build clean (NU1507/NU1900 only).

### Scope
git diff limited to: AdapterHttpClient.cs, DateTimeUtc.cs, AdapterBusEntrypointRunner.cs, AdapterBusFailurePublisher.cs + 3 test files. AdapterHttpRequestFailedException.cs + CollectorResumeFailurePublisher.cs NOT touched. No C2/C3/M7/cert-bypass/hydrator/resume-M3-M12 drift.

## 2026-07-01 — Review passes + repair
- Verifier-1: SATISFIED (all 5 criteria PASS; 160 tests; scope clean; A1/A2/A4 confirmed against source).
- Code-reviewer-1: 0 critical / 0 major / 2 minor / 2 nit; all four fixes confirmed correct.
  - M1 (Minor) REPAIRED: exception Message interpolated Context={context} unscrubbed. Now scrub context too (null-preserving) into both the Message and the stored Context property; S1 test extended to pass a secret in context and assert it's masked. Rebuild + full slnx test = all 7 green (Conversation still 11; test strengthened in place).
  - M2 (Minor, cosmetic body-label length), N1 (intentional unused cancellationToken param — reviewer: acceptable for signature stability), N2 (cold-path closure alloc) — ACCEPTED, no change.
- Post-repair build clean; dotnet test all 7 projects pass, 0 failed.
