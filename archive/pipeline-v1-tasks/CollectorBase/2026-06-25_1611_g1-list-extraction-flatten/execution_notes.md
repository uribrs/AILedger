# Execution Notes — Group 1: list-extraction flatten (B1)

## Outcome
Fixed. `dotnet build` clean; `dotnet test CollectorBase.slnx` → **77/77** (75 prior + 2 new). The capture-driven
`for_each` flow now runs when the captured list path crosses a repeated parent; was silently 0.

## What changed
- `CollectorExecutor/Execution/CollectorExecutorStepHelpers.cs`:
  - `capture_list` (`:176`) and `accumulate_list` (`:191`) now resolve via `JsonNav.ListAtFlattened` (was the
    non-flattening `ListAt`). Post-processing unchanged: capture_list → `RunContext.CaptureLists[key]` list;
    accumulate_list → append + dedupe (`!accumulated.Contains`).
  - One-line comment at each site noting it's a list-extraction (flattens; single-node nav stays on At/StringAt).
- `Tests/CollectorExecutor.Test/CollectorExecutorTests.cs`:
  - `CaptureList_CrossingRepeatedParent_DrivesForEach_AndAccumulateDedupes` — JSON `groups[].items[].id` capture
    (crosses repeated `groups`) → for_each → detail; `blocks[].qs[].qid` accumulate (crosses repeated `blocks`,
    SHARED overlap) → for_each → kb. 6 records; SHARED kb-fetched once (dedupe).
  - `CaptureList_CrossingRepeatedParent_XmlHostList_DrivesForEach` — the qualys.yaml findings shape: capture
    host_ids at `…HOST_LIST.HOST.ID` (crosses repeated HOST) → for_each → detections → 4 (was 0).
  - New mock endpoints: `/grp/list`, `/grp/detail`, `/grp/kb` (JSON nested), `/qxml/hostlist` (multi-host XML).
  - These use the capture→for_each shape the prior flatten test omitted — the gap that let B1 escape.

## Corrected axis (the fix's principle)
ALL list-extractions flatten across a repeated parent: `records_path`, hydrate ids (prior pass), and now
`capture_list` + `accumulate_list`. ONLY single-node nav (`At`/`StringAt`: cursor / next_token_at / watermark /
capture-scalar / next_url) stays literal. `drain_path` left on `ListAt` (A1).

## Assumption resolutions
A1 leave drain_path · A2 document the two nits (no behavior change) · A3 single-node nav unchanged. See assumptions.md.

## No-regression
Full suite green (77/77), incl. cursor/cursor_watermark/next_url, capture-scalar/for_each, XML single-vs-array,
envelope/passthrough, records-path flatten.

## Commands
- `dotnet build CollectorBase.slnx` → clean. `dotnet test CollectorBase.slnx` → 77/77.
