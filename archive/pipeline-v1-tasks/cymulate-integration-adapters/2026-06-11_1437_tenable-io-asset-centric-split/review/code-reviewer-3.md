# Code Review — TenableIo CollectFindings assets+findings two-phase split

**Scope reviewed (uncommitted working-tree diff):**
- `TenableIoCollector.cs` — `CollectFindingsInternalAsync` two-phase dispatch, `WriteFindingsPhaseTransitionCheckpoint`, `ResumeAsync` routing, `ResumeAssetsAsync` chaining.
- `Flows/Assets/TenableIoAssetsFlow.cs` — `combinedRun` plumbing + `CombinedRun` checkpoint stamp.
- `Flows/Findings/TenableIoFindingsFlow.cs` — `assetsPhaseComplete`/`Phase`, empty-UUID fresh-export-on-resume.
- `Recovery/TenableIoCheckpointHelper.cs` — `phase`/`combinedRun` (de)serialization, relaxed `lastPublishedPage`/`exportUuid` validation.
- `Recovery/TenableIoCheckpointState.cs` — `Phase`, `CombinedRun` fields.

**Change type:** feature logic on a resumable collector (persistence + recovery boundary).
**Risk level:** High — checkpoint state machine, pagination/idempotency, distributed re-invocation, large streamed exports.

Stack: C# / .NET 8. C# lenses applied.

---

## Findings (ranked by severity)

### 1. [Major — likely risk] Cross-invocation batch co-location is assumed, not guaranteed; assets and findings may land in different batches on a phase-transition resume

`CollectFindingsInternalAsync`'s docstring and the whole design rest on the invariant: *"both lanes use the single progressContext … one egress address / batch dir, so `assets_*.json` and `findings_*.json` co-locate in the SAME batch — which the split parser requires."* That invariant holds **only within one process invocation**. On resume, `CollectorResumeSetup.TryCreateExecutionContext` (`Shared/.../Recovery/CollectorResumeSetup.cs:54`) creates a **fresh** `progressContext` via `request.Context.CreateProgressContext(...)` and then `RestoreProgress(...)` only seeds page/item counters — it does not re-attach to the prior invocation's egress address.

Consequence: when the assets lane completes, the phase-transition checkpoint is persisted (`flow=CollectFindings`, `Phase=FindingsInProgress`), and the process then **fails before the findings lane finishes**, the host re-invokes `ResumeAsync` → `ResumeFindingsAsync`. The findings lane now publishes `findings_*.json` into a **new** progress context / batch, while the already-published `assets_*.json` live in the prior invocation's batch. If the platform consumes/parses batches per invocation or per egress address, the two lanes are no longer co-located and the split parser's premise is violated — assets without findings in batch N, findings without assets in batch N+1.

- **Impact:** potential silent data-association break (assets and their findings split across batches) on any mid-findings failure — exactly the failure path this feature adds.
- **Evidence threshold:** this is a *likely risk*, not confirmed. `CreateProgressContext` / egress addressing is SDK-internal (`Cymulate.Integration.Sdk`) and not in this repo; I could not confirm whether the host keys a batch by correlation/sequence (making resume re-join the same logical batch) or by a fresh per-invocation address. The existing resume test (`ResumeAsync_FindingsPhase_SkipsAssetsExport_AndPublishesFindingsOnly`) asserts the findings file path but runs a single in-process resume and cannot observe cross-invocation batch identity.
- **Recommended fix:** confirm with the SDK/host team whether a resumed `progressContext` re-joins the same logical batch as the failed invocation (same correlation/sequence → same parser input set). If it does not, this design cannot guarantee co-location across a resume and needs an explicit mechanism (e.g. carry the batch/sequence identity in the checkpoint and re-bind on resume, or have the parser associate by correlation rather than batch dir). Requires verification first, then possibly a real fix — not a local patch.

### 2. [Minor] `combinedRun` is hard-coded `true` at the only combined call site, making the parameter's "false" branch unreachable from production

In `CollectFindingsInternalAsync` the assets phase is always invoked with `combinedRun: true` (TenableIoCollector.cs:260), and the findings phase always with `assetsPhaseComplete: true` (line 283). The flows additionally OR-in the resume-state markers (`AssetsFlow.cs:56`, `FindingsFlow.cs:58`). So within `CollectFindings` the parameters are effectively constant; the only path that leaves `combinedRun=false` is the standalone `CollectAssets` entry (`CollectAssetsInternalAsync`, which never passes the arg). That's correct behavior, but the parameter pair plus the resume-state OR is two redundant sources of the same truth.

- **Impact:** maintainability only; a future caller could pass `combinedRun:false` while resuming a `CombinedRun=true` checkpoint and the OR silently corrects it — fine today, but the redundancy obscures the single source of truth (the checkpoint).
- **Recommended fix:** none required. Optionally derive the flow's combined/phase flag solely from `resumeState` on the resume path and from the explicit arg only on the fresh path; not worth a refactor now. Local patch at most.

### 3. [Minor] SDK page counter (`checkpoint.CurrentPage`) and lane-local `LastPublishedPage` diverge across the two phases

Within one combined invocation, `AdvancePage` is called for every assets page, then once with `(0,0)` in `WriteFindingsPhaseTransitionCheckpoint`, then for every findings page — so the SDK-managed `CurrentPage` is the **sum across both lanes**. The persisted `LastPublishedPage` in `AdapterState`, however, is **lane-local** (findings restores its `pageCounter` from `LastPublishedPage`, not from `CurrentPage`). On a findings resume, `RestoreProgress(currentPage: checkpoint.CurrentPage,…)` seeds the SDK counter with the combined total while the flow re-derives its own page numbering from `LastPublishedPage`.

- **Impact:** low. File naming (`findings_<page>.json`) uses the flow-local `pageCounter`, so files are numbered correctly and consistently. The mismatch only affects the SDK's `CurrentPage` watermark, which this collector does not use for file addressing. No correctness bug observed, but the dual page-numbering scheme is a latent trap if anyone later keys output or dedup off `CurrentPage`.
- **Recommended fix:** document the two counters' distinct roles (lane-local vs aggregate) near `AdvancePage` call sites, or assert the relationship. No code change required now.

### 4. [Minor] `lastPublishedPage < 1` → `< 0` relaxation also loosens the legacy `chunkId` fallback for ordinary findings checkpoints

`TenableIoCheckpointHelper.cs:132-134`: the findings loader now accepts `lastPublishedPage == 0` (needed for the phase-transition checkpoint) — correct. But the same relaxation is applied to the legacy `chunkId` fallback. A genuinely old/corrupt findings checkpoint with `chunkId=0` and a non-empty `exportUuid` that previously would have been rejected (`< 1`) now loads with `pageCounter=0` and resumes the existing export from page 0. Practically harmless (it re-numbers pages from 1 and the chunk-id dedup set prevents re-publishing already-processed chunks), but the validation floor was deliberately `>= 1` before and is now `>= 0` for *all* findings checkpoints, not just phase-transition ones.

- **Impact:** very low; dedup via `ProcessedTenableChunkIds` covers the re-numbering. Worth a one-line awareness note.
- **Recommended fix:** acceptable as-is. If stricter, gate the `0` allowance on `Phase == "FindingsInProgress"`. Local patch, optional.

### 5. [Observation] Phase marker is a string literal repeated in 4 places, not a glossary/enum constant

`"FindingsInProgress"` appears as a magic string in `FindingsFlow.cs` (read + write), `TenableIoCollector.cs` (`WriteFindingsPhaseTransitionCheckpoint`), and is documented in `TenableIoCheckpointState.cs`. The repo convention (`CLAUDE.md`, Glossary) is to centralize such canonical strings. A typo in one of the comparison sites would silently disable phase skipping (assets would be re-pulled).

- **Impact:** maintainability / latent correctness; `OrdinalIgnoreCase` comparison mitigates casing drift but not a literal typo.
- **Recommended fix:** promote `"FindingsInProgress"` (and the implicit phase vocabulary) to a `private const` on the checkpoint state type or the glossary, referenced everywhere. Local patch.

### 6. [Observation] No staleness/expiry guard on a phase-transition checkpoint's empty export

A phase-transition checkpoint resumes by creating a **fresh** vulns export — good, no stale-UUID problem there. But `CanResumeFrom` still applies the ~24h staleness gate against `CheckpointCreatedUtc`. If a combined run's assets phase finished, then the findings phase was deferred >24h before resume, `CanResumeFrom` returns false and the run restarts from scratch (re-pulling assets too). That is the intended safety behavior (Tenable exports expire ~24h), and the assets export would itself be stale — so restarting is correct. Noted only to confirm the interaction is sound; no action.

---

## Positives

- Resume routing is correct and the phase semantics are coherent: `flow=CollectFindings` ⇒ assets already published ⇒ findings-only; `flow=CollectAssets` + `CombinedRun` ⇒ finish assets then chain into findings; `flow=CollectAssets` + `!CombinedRun` ⇒ legacy standalone assets. The "any findings-flow resume means assets are done" rule (incl. legacy no-phase checkpoints) is the right default and avoids re-pulling assets.
- Asset re-pull is correctly prevented across the transition (transition checkpoint carries `flow=CollectFindings`, routing to findings-only resume). Findings chunk-level dedup (`ProcessedTenableChunkIds`) prevents duplicate findings on a same-export resume.
- `AdvancePage(0,0)` for the transition snapshot mirrors the established resilience-layer no-op-advance pattern; checkpoint is persisted before any findings work, so a crash in the transition window resumes findings-only.
- Backward compatibility is handled: `combinedRun`/`phase` are additive optional keys; absent keys default to legacy behavior; `SaveFindingsState` omits `phase` when null so old consumers are unaffected.
- Cancellation and stream/resource lifecycle in the flows are unchanged and remain correct (`await using` chunk streams, `OperationCanceledException` rethrown ahead of retry filters).

## Verdict

No confirmed Blocker in the reviewed code. **Finding #1 is the one that must be resolved before merge** — not by changing this code blindly, but by confirming the SDK/host batch-identity behavior on resume. If a resumed `progressContext` does **not** re-join the failed invocation's logical batch, the assets→findings co-location guarantee breaks precisely on the mid-findings failure path, and the design needs an explicit batch-binding mechanism. Everything else is Minor/Observation.
