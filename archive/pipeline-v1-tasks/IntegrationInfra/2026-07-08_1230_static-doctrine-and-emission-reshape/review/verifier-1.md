# Verifier-1: Static-Taxonomy Doctrine + Emission Publisher Reshape

## Verdict

**PASS.** The work satisfies the original intent (codify the static taxonomy, then kill the Emission
service-locating statics under the invariant net, behavior-identical) and every Success Criterion.
Behavior equivalence is confirmed at line level against the deleted originals; the net passes
(237/237) with arrange/act-only test churn and zero assertion changes. The disclosed regex incident
left no collateral damage. Two low-severity, behavior-neutral findings noted below; neither blocks.

## Criterion table

| # | Success Criterion | Result | Evidence |
|---|---|---|---|
| 1 | DESIGN.md "Static taxonomy" section (3 classes, rulings, examples) + deferred-seams updated + no contradicting text | PASS | commit 8a8cfa7: all three classes with example lists matching task.md, rulings, the "can a reader see every dependency at the call site?" test; deferred-seams: static-publisher DIP marked **resolved**, ISink gated on a real second sink, OCP split to its own bullet |
| 2 | No public static publish entrypoints; no service-provider resolution in Emission static bodies | PASS | grep `public static .*(Publish...)` in Emission → NONE; grep `.Resolve(*.Services)` in Emission → NONE |
| 3 | Instance emitter: explicit-options ctor + `Create(IServiceProvider?)` preserving DI→IConfig→env→default; full publish surface (string/utf8, page+generic, naming gate, BuildMandatoryTargetPath); ctx/progress as method params | PASS (1 deviation) | NdjsonBatchEmitter.cs: 4-arg explicit ctor + `Create(IServiceProvider)` resolving 4 options once; 4 named page + 2 generic page + 2 batch methods; static `BuildMandatoryTargetPath`; ctx/progress are method params. **Deviation:** `Create` takes non-nullable `IServiceProvider` (contract/decisions say `IServiceProvider?`) — justified & recorded (below) |
| 4 | Startup guards preserved: same exception type + equivalent timing (choice recorded) | PASS | Both `InvalidOperationException` guards (MaxBytesPerBatch ≥ 5 MiB; effective maxBufferedBytes ≥ 5 MiB) with byte-identical message text, moved per-call→construction; choice recorded in execution_notes |
| 5 | `dotnet build IntegrationInfra.slnx` 0 errors; full suite green; test diffs arrange/act-only (assertions byte-identical) | PASS | build 0 errors (19 pre-existing warnings, none in Emission); 237/237 across 7 projects; zero `Assert.`/`.Should(` lines added or removed; Assert counts identical (Atomic 63/63, BatchScoped 26/26) |
| 6 | Emission README + README.Publishing reflect new surface; ISink resolved-or-narrowed | PASS | README.md reshape-wave-1 + Deferred sections updated; README.Publishing "Main Entry Points" rewritten to the single `NdjsonBatchEmitter` surface; ISink narrowed to "gated on a genuine second sink" |
| 7 | execution_notes logs per-step incl. MultipartUploadOptions site + sessions' handoff shape; state.json updated; archive mirrored | PASS | execution_notes records both session ctors resolved MultipartUploadOptions (line 58 each), now a null-guarded ctor param; state.json currentPhase=verification; archive present at ~/codex-state/tasks/IntegrationInfra/2026-07-08_1230_... |
| + | Two-commit structure (doctrine before reshape) | PASS | 8a8cfa7 (DESIGN.md only) parent of 2ec35c3 (reshape) |
| + | No version bump | PASS | no csproj / Directory.Build.props touched in the diff |
| + | DAG unchanged (Emission usings Kernel/Envelopes.Common/SDK only) | PASS | emitter usings identical to deleted ResultsBatchPublisher; no new using directions |

## Behavior-identical audit (the crux)

Read NdjsonBatchEmitter.cs side-by-side against `c6a30c8:ResultsBatchPublisher.cs` and
`c6a30c8:AdapterNdjsonPublisher.cs`. The publish loops are **line-for-line identical**:

- Arg null/whitespace guards, `StartPublishActivity("string"/"utf8", ...)` tags, the debug-log
  template + fields, the `await foreach` body, whitespace-skip, `NormalizeToSingleLine`/
  `JsonException`→`DataPipelineException($"invalid JSON record: ...")`, the utf8 newline-branch vs
  `JsonDocument.Parse` validity check, `recordBytes + 1`, `FinalizeAsync`, the **commit-incomplete
  guard** (same message), the **zero-record path** (`LogDebug` + `MarkPublishCompleted(0,0)` +
  `PublishResult.Ok()`), the success `LogInformation` + `MarkPublishCompleted` + `PublishResult.Ok(...)`,
  and the `catch when (ex is not OperationCanceledException)` → `MarkFailure(activity,"egress",ex)`.
- Telemetry mode tags remain the strings `"string"`/`"utf8"` (not type names) — unchanged.
- Naming gate (`BuildMandatoryTargetPath`) and the 4 findings/assets page wrappers are byte-identical
  logic; `AsAsyncEnumerable` ×2 and `NormalizeJsonPath` kept as pure/internal statics per the taxonomy.

**The only substantive move:** option **resolution** (`Throttling/Buffering/MemoryPressure.Resolve`)
and the two 5 MiB **guards** relocate from per-publish-call to `Create()`/ctor; `MultipartUploadOptions`
relocates from inside both session ctors to a null-guarded session ctor param supplied by the emitter.
Resolution is deterministic from the same `IServiceProvider`, so once-at-construction yields identical
values to per-call — behavior-identical by construction. `NdjsonOptions` is now built by a private
`BuildNdjsonOptions(targetPath)` helper with the same six fields.

## Regex-incident audit

execution_notes discloses a transform script that deleted four `await ResultsBatchPublisher.PublishUtf8Async(`
lines before hand-repair. Audited `git diff c6a30c8...HEAD -- tests/` in full:

- Every hunk is a coherent arrange/act transform (`ResultsBatchPublisher`/`AdapterNdjsonPublisher.X(` →
  `NdjsonBatchEmitter.Create(ctx.Services).X(`); four sites hoisted an inline `ContextWith(...)` to a
  local so `.Services` can be reused — mechanically correct.
- **No collateral damage:** no orphaned/mangled lines, no dropped `await`, no duplicated locals, no
  stray deletions. All repaired PublishUtf8Async sites present and well-formed.
- **Assertion audit:** zero `Assert.`/`.Should(` lines in the +/- set; Assert counts identical
  base↔HEAD (AtomicStreamedObjectsTests 63/63, BatchScopedStorageTests 26/26). Suite green confirms.

## Findings by severity

### Low
- **Stray `.gitignore` line.** The reshape commit (2ec35c3) adds a junk `ca` entry (no trailing newline)
  after `ai/`. Not a valid ignore pattern — an accidental keystroke, outside the declared scope fence.
  Harmless but should be dropped before merge.
- **`Create` signature deviates from the literal contract.** Contract Success Criterion and decisions.md
  say `Create(IServiceProvider?)`; the implementation is non-nullable `Create(IServiceProvider)` with
  `ArgumentNullException.ThrowIfNull`. Executor's rationale (recorded in execution_notes): the underlying
  `*.Resolve(services)` methods already `ThrowIfNull`, so a null-services path never existed and nullable
  would invent new behavior. Defensible and behavior-preserving; flagged only because it departs from the
  literally-stated signature. No action required unless the operator wants the literal `?`.

### Informational (no action)
- **Resolution ordering.** `Create()` resolves all four options before the ctor validates throttling; the
  originals interleaved resolve→validate per call. Observable only if a non-throttling `Resolve` threw
  while throttling was already invalid — not a real or pinned scenario. Behavior-identical in practice.
- **Guard-timing unobservable by the net.** No test pins the below-5-MiB guard (all use 6/50/52 MiB), so
  the per-call→construction move is invisible to the suite — consistent with the contract's allowance.
  A caller that constructs an emitter and never publishes would now fault at construction; acceptable and
  recorded.

## Evidence

- Build: `dotnet build IntegrationInfra.slnx` → 0 Errors, 19 Warnings (all pre-existing: NU1507 package
  source, CS1574 crefs in Conducting/Conversation/FaultGovernance — none in Emission).
- Tests: `dotnet test --no-build` → Kernel 69, Conducting 16, FaultGovernance 27, Job 50, Reporting 15,
  Conversation 11, Emission 49 = **237/237, 0 failed, 0 skipped**.
- Grep gates: no public static publish entrypoints in Emission; no `.Resolve(*.Services)` in Emission;
  remaining `ResultsBatchPublisher`/`AdapterNdjsonPublisher` string hits are README historical
  carry-record lines only (legitimate per verification brief).
- Commits: 8a8cfa7 (doctrine, DESIGN.md only) → 2ec35c3 (reshape) on `reshape/emission-instance-emitter`,
  diff base c6a30c8 (dev head). Working tree clean.
