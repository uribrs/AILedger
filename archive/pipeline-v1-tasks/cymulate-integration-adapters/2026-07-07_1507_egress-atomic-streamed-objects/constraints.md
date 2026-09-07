# Constraints

- Universal law — NO opt-in flag: one publish call = one atomic object for every collector flow.
- Small (≤ threshold) calls remain single PUTs with byte-identical path/naming/content to today; regression law: pre-splitting callers cannot observe any difference.
- Multipart escalation uses the existing `IAdapterDataPublisher` surface only (`Initiate/UploadPart/Complete/Abort`); no ISB repo changes.
- Abort must fire on: mid-stream failure, cancellation, dispose-without-complete (death/teardown). Abort failures are logged, never thrown.
- Checkpoint/progress hooks fire exactly once per call, only after successful Complete (or successful single PUT).
- The four-tier heap defense (throttling → buffering → memory-pressure flush → multipart) keeps its memory semantics; `MaxBytesPerBatch` survives only as buffering/part discipline, never as object size.
- Fail-fast single-record rule deleted — not relaxed, removed, along with every branch that exists only to rotate/split objects at the byte budget.
- Cleanup judgment rule (behaviour over shape): collector-side buffering that bounds collector RAM is NOT dead code; only object-size-rule servitude is. When ambiguous, keep and document why.
- `BatchScopedStorage` untouched and orthogonal; its announcement timing must align with Complete.
- Observability: per-object completion log (existing `Hash=` line) gains object size + record count; per-record soft-threshold WARNING (default 24MB, configurable) — log only, never a gate.
- Spark line discipline / record slicing: OUT OF SCOPE (recorded as deferred collector-side follow-up).
- No changes on feature branches (Tenable/Falcon correlated) or other repos.
- Tests: xUnit + Moq + FluentAssertions; central package management; mocked `IAdapterDataPublisher` for multipart assertions.
- Test-run protocol: `dotnet test` hangs in this harness — build then `dotnet vstest` on `artifacts/bin/ut` dlls, phase-boundary cadence. EXPLICIT USER EXCLUSION: never run ISBLoadTestCollector and DummyCollector test projects. Known-hang suites (ServiceNowCmdb, TenableIo) via vstest only.
- Collector csproj version bumps only where collector code is actually touched by cleanup (patch/minor per magnitude).
- Docs sync required: Egress README (incl. Stable Output Rules), Shared session docs, ai/skills content describing the 50MiB object behavior.
- Ops handoff (outside repo, must be recorded in execution notes): confirm/add S3 `AbortIncompleteMultipartUpload` lifecycle rule on the data bucket.
