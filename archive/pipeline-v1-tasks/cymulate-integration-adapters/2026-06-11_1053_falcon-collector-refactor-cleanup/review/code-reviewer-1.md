# Code Review — Falcon Collector Refactor / Cleanup

**Scope:** Uncommitted working-tree changes + new untracked files under
`src/Cymulate.Integration.Adapters/Collectors/FalconCollector`.

**Change type:** Shared-library / collector pipeline code (resumable paginated collector).
**Risk level:** High — persistence/checkpoint serialization, pagination state, recovery decisions, a producer/consumer channel. Operationally severe surfaces despite being "just a refactor".

**Method:** Read the full diff (3540 lines), every new untracked file in full, and cross-checked each extracted helper against the inline code it replaced. Built the project (`dotnet build` — 0 warnings, 0 errors), which validates type/positional parity across all changed signatures.

## Verdict

This is a clean, behavior-preserving refactor. I found **no Blocker and no Major correctness defects.** The checkpoint serializer/deserializer split is byte-faithful, the record/DTO threading is correct, the alias blocks do not change any value/default/null-handling, and the extracted JSON/pagination/event helpers match the originals exactly (culture, date-parse styles, null/whitespace handling, try/catch swallowing). Findings below are Minor/Nit/Observation only.

---

## Findings

### Minor

**M1 — `AssetsSegmentScrollState` reaches "up" to a sibling flow's static helper (`FalconAssetsFlow.BuildAssetsFilter`).**
`Flows/Assets/AssetsSegmentScrollState.cs:86` calls `FalconAssetsFlow.BuildAssetsFilter(...)`, which forced that method from `private` to `internal` (diff `FalconAssetsFlow.cs`, line ~1179). The state object now depends on the flow class it is supposed to be a plain state-bag for — a small circular-ish coupling. It is correct and compiles, but the filter-building logic (`BuildAssetsFilter`) is arguably the thing that should live in `SharedFlows/` (next to `FalconCursorPagination`) and be consumed by both. 
*Impact:* maintainability only; no runtime risk. 
*Fix (optional, deferrable):* move `BuildAssetsFilter` into a shared helper, or pass the rebuilt URL/floor into `ResetScrollToWatermark` from the flow rather than having the state-bag rebuild it. Local patch, not required now.

**M2 — `ResetScrollToWatermark` moved onto the state object but still takes 5 collaborators as parameters.**
`AssetsSegmentScrollState.cs:64-97` takes `config`, `segment`, `limit`, `logger`, `ex`. This is a method that mostly operates on flow inputs and writes three state fields. The split is defensible (it is the same code, relocated) but the method straddles the state/flow boundary. 
*Impact:* readability; no behavior change (verified line-by-line vs. the original local function — identical, including the `WatermarkFloorUtc!.Value` usages and the `effectiveBaseDateUtc` precedence). 
*Fix:* none required; noted for the boundary heuristic only.

### Nit

**N1 — Null-forgiving `WatermarkFloorUtc!.Value` / `watermarkFloorUtc!.Value` relies on a non-local invariant.**
`AssetsSegmentScrollState.cs:78-79,94` and `AssetIdsFetcher.cs:1230,1239` (new diff lines) dereference the watermark with `!` because `FalconCursorPagination.ShouldResetForDepthCap(...)` (and, for assets, the reactive-404 guard) guarantees `HasValue` first. I verified `ShouldResetForDepthCap` (`FalconCursorPagination.cs:27-34`) does require `watermarkFloorUtc.HasValue`, so the `!` is sound today. It is a latent foot-gun if the predicate is ever changed to not require the watermark. 
*Fix:* optional `Debug.Assert(WatermarkFloorUtc.HasValue)` or an explicit guard-throw documenting the precondition. Cosmetic.

**N2 — `FindingsAidScopedResumeState.PendingAids` is a mutable `List<string>` on an otherwise immutable record, and is shared by reference.**
`Dtos/FindingsDtos/FindingsAidScopedResumeState.cs:21`. The producer emits the batch list and `FalconFindingsFlow.ApplyAidState` (`FalconFindingsFlow.cs` diff ~1765) assigns it directly to `_pendingAids` (reference share), exactly as the prior inline code did (`_pendingAids = batch.Aids`). No defect — the consumer replaces `_pendingAids` with a fresh list after each batch and the producer does not retain/mutate the emitted list. Worth a one-line note that the list is handed off (not copied). Behavior-identical to before; flagging only because a mutable collection on a `record` invites accidental aliasing in future edits.

### Observation (no action)

**O1 — Checkpoint serializer/deserializer split is faithful.** Keys in `FalconCheckpointKeys` match the original string literals 1:1 (`flow`, `page`, `afterToken`, … `collectedAids`, `spotlight*`). Save/Load agree on every key. The de-duplicated `TryParseBaseFields` preserves the exact validation order and identical `LogWarning` templates for both flows; the findings-only layering (aidBatchSize required, stage/assets-stage/AID/spotlight optional) matches. Backward-compat preserved verbatim: legacy `collectedAids` fallback, the 1,000,000-char size guard (refuse-to-resume), and the asymmetric error handling (`pendingAids`/`collectedAids` parse failure ⇒ `return false`; all other list parse failures ⇒ swallow + continue). `lastWatermarkIds`/`assetsStageWatermarkIds`/`spotlightCompletedLanes` best-effort deserialize preserved.

**O2 — Extracted JSON readers preserve semantics.** `FalconJson.ReadString`/`TryReadUtcDateTime`/`TryReadAnyId` (`Flows/SharedFlows/FalconJson.cs`) keep InvariantCulture, `AssumeUniversal | AdjustToUniversal`, the `JsonValueKind.String` guard, the `ValueKind != Object` early-out, and whitespace→null normalization. `TryReadAnyId` keeps the `device_id ?? aid ?? id` precedence. The two pre-existing copies (assets flow `TryReadUtcTimestamp`, spotlight `TryReadUtcDateTime`, AID fetcher `TryReadUtcDateTime`) are consolidated without drift.

**O3 — `FalconFindingsProgressSnapshot.TryDecodeCursorSortTimestampUtc` moved verbatim**, including the base64url normalization, the `%4` padding, the `s[0]` epoch seconds-vs-ms split at `1_000_000_000_000`, and the catch-all → null. This is diagnostics-only (log fields), so even a behavioral slip here would be low-severity; none found.

**O4 — Record/DTO field threading verified correct.** `SpotlightVulnerabilitiesRunRequest`/`Result`, `FindingsAidScopedResumeState`, `FindingsSpotlightYieldState`, `SpotlightLaneRunContext`, `ExtractedFields` are all populated with **named** arguments at every call site, so the positional-ordering hazard called out in the task does not materialize. The alias blocks at the top of `RunAsync` and `RunSpotlightLaneAsync` (e.g. `FindingsFlowRunConfig config = ctx.Config;`) are pure local rebinds — bodies unchanged, no value/default/null re-interpretation.

**O5 — Recovery handler extraction is a pure move.** `FalconCollectorRecoveryHandlers` reproduces `RecoverFreshAsync`/`RecoverResumeAssetsAsync`/`RecoverResumeFindingsAsync` line-for-line (planned-yield short-circuit, durable-progress gate, cursor-expired re-anchor, 401/5xx `ApplyCursorTtlForResume(fromScheduledWait: true)`, decline messages). It carries only the injected logger — no smuggled instance state. The old private `ApplyCheckpointState` is now `FalconProgressState.Apply` (identical `foreach SetState`).

**O6 — Config builder split (`ResolveCredentialsDictionary` / `ExtractFields` / `ApplyOptionalOverrides` / `BuildSessionSpec`) preserves order and semantics**: encrypted-credentials merge (case-insensitive, copy non-`credentials` keys then overlay decrypted fields), the early `(null, null)` partial-config return moved to the orchestrating method but fires on the same condition, `Math.Clamp` ranges on the paging knobs unchanged, override application order unchanged. The `using var doc = JsonDocument.Parse(...)` is disposed once per path.

**O7 — Channel/producer concurrency is sound.** `FalconAidBatchProducer` keeps the bounded(1), `SingleReader`/`SingleWriter` channel; `CompleteWriter()` → `TryComplete()` is idempotent; the consumer still completes the writer in `finally` and awaits `producer.Completion` to observe faults (verified `FalconFindingsFlow.cs` diff ~2062-2066). No new shared mutable state introduced; the producer task and consumer remain a single-producer/single-consumer pair. Stream disposal (`await using streamed`) in `ProcessPageAsync` happens exactly once per page (the `await using` wraps the whole parse/publish block, as before).

---

## Summary

No merge-blocking issues. The refactor is faithful at the level that matters for this collector (checkpoint wire format, resume/recovery decisions, pagination watermark math, producer/consumer lifecycle). The Minor items (M1/M2) are coupling/altitude observations on the assets-scroll extraction and can be addressed opportunistically; everything else is a Nit/Observation. Build is clean.

**Certainty:** High on the checkpoint serialization, DTO threading, recovery handlers, JSON helpers, and config builder (read in full against their originals; build green). Medium on the deep findings-lane control-flow equivalence (large method, verified by structured diff reading + build, not by executing the resume/yield state machine).
