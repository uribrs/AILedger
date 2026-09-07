# Task: Shared → Infra parity carry (3 items)

Repo: `/Users/user/Dev/IntegrationInfra`. Branch off `origin/dev` (`ad2699a`).

A full type-level parity audit of the in-production `Shared` library against `IntegrationInfra` found
189/197 types carried and 150/164 file pairs normalized-identical. Three capabilities are missing.
All three are post-fork drift in `Shared`, not lost carry. This task closes them.

## 1. instanceBatchId regression + batch-scoping ergonomics — CRITICAL

`Envelopes/Common/BatchScopedStorage.cs:82` writes the raw batch segment (`batch_000001`) into
`Metadata["instanceBatchId"]`. It must be the deterministic RFC-4122 **v5** UUID over
`{baseUrl}/batch_NNNNNN` under the frozen namespace `b584b489-7c3d-4caf-97eb-49d7ff6d78fb`, exactly as
`Shared.BatchScopedStorage.BuildBatchInstanceId` produces. Upstream ops keys on a UUID; the segment
string breaks the sequence. **The namespace GUID is frozen forever** — changing it re-mints every
batch id ever announced and breaks upstream replay deduplication.

Second half: fold `BeginPage`/`RestoreBase` into `NdjsonBatchEmitter` so a consumer opts in with one
boolean instead of the 14–31 lines of plumbing each of the three current Shared consumers carries
(Qualys 14 / InsightVmCloud 31 / Falcon 16 lines; 4 publish sites each re-implementing the ordering
contract).

## 2. Content hash carry

`NdjsonContentHasher` and `ContentHashHex` were dropped from both batch sessions during the emission
reshape, and `Hash={Hash}` from the two publish-completed log lines. This is load-bearing
observability, not a log ornament: it is how a collector stuck re-uploading byte-identical content is
detected (observed in production on DefenderVm). Restore it.

## 3. SessionAuthRetry carry

`Shared/Session/SessionAuthRetry.cs` has no Infra counterpart. It is the auth-replay helper for
indicator adapters that opt out of shared OAuth2 auto-refresh via `AuthSelection.None()` — Defender
uses it at 3 call sites today; Zscaler and FortiGate are the same shape. The SDK's
`SendWithAuthRetryAsync` is not a substitute.

## Execution shape (operator-mandated)

Main agent implements the shared seam itself first and gates on build+suite green. Then exactly one
subagent per work item, file-touch-partitioned so no two workers share a file and no worker touches a
seam file.
