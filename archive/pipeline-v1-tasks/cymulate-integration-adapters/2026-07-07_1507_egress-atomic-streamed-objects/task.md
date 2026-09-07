# Egress: Atomic Streamed Objects (Universal)

Make "one publish call = one atomic object, streamed, size-unbounded" the universal law of `Shared/DataPipeline/Egress` — for every collector, no opt-in flag — replacing 50MiB object-splitting and the fail-fast single-record rule.

## The law

- Small calls: single PUT, **byte-identical behavior to today** (path, naming, content). Well-behaved pre-splitting callers cannot tell the difference.
- Growing calls: escalate to ISB multipart (`Initiate/UploadPart/Complete/Abort`); the object materializes on `Complete` or never exists.
- `MaxBytesPerBatch` retreats to in-flight memory/buffering discipline (part sizing / flush thresholds inside the four-tier heap defense — tiers unchanged).
- Fail-fast "single record must fit" is deleted; replaced by observability (size on the `Hash=` completion log, loud warning past a per-record soft threshold, default 24MB).
- Abort the multipart on failure, cancellation, and dispose-without-complete (death/teardown).
- `BatchScopedStorage` (batch_NNNNNN scoping + announcements) stays the only opt-in, orthogonal; interplay verified.

## Cleanup mandate (user explicit)

Remove all dead code and never-trodden paths this law creates: egress object-rotation branches, fail-fast checks, ignored max-byte publisher params, collector-side code whose *sole* purpose was pre-splitting output into ≤budget objects. Judgment rule: collector-side buffering that bounds collector RAM is not dead — only object-size-rule servitude is. When ambiguous, keep and document.

## Out of scope

ISB repo changes (publisher verified adequate); Tenable/Falcon correlated feature branches (inherit on merge); Spark line discipline / record slicing (deferred collector-side follow-up); exposure-analytics.

## Why now (measured)

Production Tenable run: max single record 17.8MB, fattest single finding 13.6MB, densest asset 842 findings — the fail-fast gate is one dense+fat asset away from killing a run. S3 gives ≈48GiB per object at 5MiB parts; no Cloudflare in the upload path (direct S3).
