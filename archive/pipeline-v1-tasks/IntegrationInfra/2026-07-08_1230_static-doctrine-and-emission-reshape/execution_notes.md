# Execution Notes

Branch: `reshape/emission-instance-emitter` (off `dev` @ c6a30c8 — NOTE: initial branch was cut from a
stale local dev missing the pins merge; caught immediately, fast-forwarded to origin/dev before any work).

## Part 1 — Doctrine (commit 8a8cfa7)
- DESIGN.md "Static taxonomy (normative)": pure-function KEEP / explicit-args KEEP / hidden-dependency
  PROHIBITED, with the one-question test (can a reader see every dependency at the call site?).
- Deferred-seams list: static-publisher DIP seam marked resolved; ISink explicitly gated on a real second sink.

## Part 2 — Reshape (commit 2ec35c3)
- OPEN assumptions resolved pre-code: MultipartUploadOptions was resolved inside BOTH session ctors
  (line 58 each) — the last hidden locator; sessions' handoff is a plain ctor, extended with a
  MultipartUploadOptions parameter (null-guarded).
- `NdjsonBatchEmitter` (public sealed, Emission root): merges engine + naming façade per decisions.md.
  - Explicit-options ctor: validates ThrottlingOptions + computes effective buffering + enforces both
    5 MiB guards AT CONSTRUCTION (was per publish call; same exception type + message text — recorded
    guard-timing choice: construction).
  - `Create(IServiceProvider services)` — non-null (DEVIATION from contract's `IServiceProvider?`
    suggestion: the existing Resolve(services) methods ThrowIfNull; a null-services path never existed,
    so nullable would have invented new behavior).
  - Publish surface preserved 1:1 (4 named page methods, 2 generic page methods, 2 batch methods);
    context/progress stay method params. Pure statics kept per taxonomy: BuildMandatoryTargetPath,
    AsAsyncEnumerable ×2, NormalizeJsonPath (internal; sessions repointed).
- Deleted: ResultsBatchPublisher.cs, AdapterNdjsonPublisher.cs (git rm, no wrappers).
- ThrottlingAdapterExecutionContext: doc cref updated only (type untouched).
- Grep gates: zero `Resolve(*.Services)` in Emission static bodies; zero public static publish entrypoints.

## Test churn (mechanical only — verified)
- AtomicStreamedObjectsTests + BatchScopedStorageTests: call sites → `NdjsonBatchEmitter.Create(ctx.Services).Publish…`
  (per-call Create mirrors the old per-call resolution semantics and exercises the resolution chain
  through the existing fake Services). Four sites with inline `ContextWith(...)` args hoisted to locals.
- Assertion audit: `git diff tests/ | rg Assert` → zero assertion lines changed. 237/237 green.
- EXECUTION INCIDENT (self-inflicted, repaired): a malformed no-op regex in the second transform script
  deleted the four remaining `await ResultsBatchPublisher.PublishUtf8Async(` lines instead of matching
  nothing. Caught by the suspicious "replaced 0 + grep clean" combination; all four sites repaired by
  hand; full diff re-audited. Lesson recorded: never leave a "placeholder" regex sub in a transform.

## Docs
- Emission/README.md: reshape marked done in charter; deferred list updated; Stable Output Rules
  re-pointed at the emitter. Historical carry-record lines (what moved in from Shared) intentionally
  keep the old names.
- README.Publishing.md: entry-points section rewritten (single emitter surface); path references updated.
- ARCHITECTURE.md consumer-families paragraph updated.

## Verification
- Build 0 errors; full suite 237/237 across all 7 projects, twice (post-reshape, post-docs).

## Post-review repairs (code-reviewer-1 minors, commit follows verifier gitignore fix)
- Create() XML remarks: provider-affinity caveat (options freeze from construction provider; sink rides the call context).
- NdjsonBatchEmitterReuseTests: single emitter, sequential findings+assets pages + 8 concurrent publishes; pins the reuse/thread-safety contract and serves as the consumption template. Suite: 238/238.
