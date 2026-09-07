# Task: Fix the three "fix-now" review findings (S1 / C4 / C1)

Fix three verified, pre-existing latent bugs surfaced by the 3-pass full-repo review
(`ai/reviews/full-repo-2026-07-01/SYNTHESIS.md`). The operator has authorized these behavior changes; the
verbatim-preservation rule is lifted for these three only. Each fix ships with a test. Nothing else changes.

## S1 — Secret leak (security), Conversation
`AdapterHttpClient.ThrowFailure` scrubs its own `LogError` line but builds `AdapterHttpRequestFailedException`
from the raw body snippet + URL (in `message`, and the stored `Url`/`BodySnippet`). That exception is later
logged (`AdapterBusEntrypointRunner.LogFinalOutcome`) and published as an `ErrorRequest`, so a
`client_secret`/bearer in a vendor error body or URL query reaches logs + the platform. Scrub at the raise site.

## C4 — DateTimeUtc Local→UTC (correctness), Kernel
`DateTimeUtc.NormalizeUtcOrMinValue` uses `SpecifyKind(value, Utc).ToUniversalTime()` — relabels a `Local`
value as UTC without shifting (offset dropped); the chained call is a self-cancelling no-op. Replace with a
kind-switch. Keep the `DateTime.MinValue` sentinel.

## C1 — Failure-path robustness (Conducting)
(a) Vendor host callbacks are invoked immediately before the mandatory last-word failure publish with no
try/catch — a throwing callback propagates past the runner's `finally` and suppresses the failure completion.
Guard them (log + continue). (b) `AdapterBusFailurePublisher` forwards the live cancellation token for
terminal error/completion publishes; a mid-flow cancel can drop the failure completion. Use
`CancellationToken.None` to match the runner's documented last-word invariant.

Out of scope: C2 (dedup reshape), C3 (broad test pass), M7 (ratified design), cert-bypass, hydrator catch,
and the deeper resume-leg semantics (M3 retryable-rethrow, M12 hook-fires). Full detail in `prompt_contract.md`.
