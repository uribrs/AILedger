# Falcon Spotlight — in-flow transport retry, then defer

Falcon's `CollectFindings` Phase 2 must absorb a mid-stream transport failure (TLS EOF) with
in-process retry inside its own fetch loop, and on exhaustion defer through the resilience layer
instead of terminating the run.

## What breaks today

A TLS read failure on the Spotlight response **body** — after HTTP 200 headers were already
returned — ends the run terminally.

- `6a85ca2038f164746a562020`, prod-eu-west, 2026-08-20T16:35:25Z. Died after 25h and 7 resumes,
  ~110M items, page ~4504, on `"Received an unexpected EOF or 0 bytes from the transport stream."`
  Published `collectors.done Status: "failed", TotalAssets: 0` over an intact resumable checkpoint
  at page 3920. No host redelivery followed (zero log lines after 16:35:30 through 2026-08-22).
- `6a85c6f28c0514d2b461b96e`, 2026-08-20T19:35:34Z — identical.
- 14 days of production: 2 EOF fatalities, both Falcon, out of 13 failed collections total.

## Why it is terminal

1. Http.Package's Polly pipeline wraps `SendAsync` only. `TrueStreamingTransportService.cs:370`
   uses `HttpCompletionOption.ResponseHeadersRead` and hands the body to the caller as
   `StreamedResponse.ContentStream`. A mid-body drop is structurally outside the retry pipeline.
2. The exception reaches IntegrationInfra's `AdapterResilienceStrategy` chain, where
   `RetryableTransportFailurePolicy` sits at position 4 — ahead of Falcon's `MappedFailurePolicy`
   at position 6. Falcon is the only collector that does not pass `transientTransportBackoff`;
   with a null plan that policy returns `PublishFailure("ADAPTER_EXCEPTION", IsRetryable: true)`
   and ends the run. Falcon's own classifier never sees the exception.

Infra is out of scope, so Falcon must absorb the fault before it escapes the flow.

## Shape of the fix

Follow the TenableIo precedent: retry the unit of work in-process inside the flow's own fetch loop,
using Infra's already-public, vendor-agnostic `HttpTransportFailureClassifier`. On exhaustion,
**defer — never skip**.
