# Task: Flatten-aware records_path

Fix a confirmed latent defect in the CollectorExecutor POC: the engine cannot extract records when
`records_path` crosses a REPEATED (array) element, so it silently emits zero records.

## Confirmed defect (proven via the real code path)
- `JsonNav.At` (`CollectorExecutor/Interpreter/Interpreter.cs:22-40`) does object-only dot-nav; landing on a
  `JsonArray` → next segment returns null.
- `ResponseMapper.Map` (`Interpreter.cs:92-99`) keeps only `JsonObject` elements; non-objects skipped.
- `JsonNav.ListAt` (`Interpreter.cs:57-66`) returns one array level; no array-of-arrays flatten.
- NET: a `records_path` crossing a repeated element (XML repeated siblings → `JsonArray`, `XmlResponse.cs:65-78`)
  → null → 0 records, silently.
- PROVEN: multi-host Qualys-shaped XML → detections **0 of 3**; single-host → 2 of 2 (single works by accident).

## In-repo impact (no prod/no client — POC)
- `integrations/qualys.yaml:47` (findings, `HOST_LIST_OUTPUT.RESPONSE.HOST_LIST.HOST.DETECTION_LIST.DETECTION`)
  crosses the repeated `HOST` → 0 findings on any >1-host response.
- `qualys.yaml:29` (assets) and `:65` (KB vulns) are terminal arrays → fine. All other profiles use
  terminal-array `records_path` → none regress.

## Fix
A flatten-aware records resolver (new method) that traverses arrays mid-path and flattens
(jq `.HOST[].DETECTION_LIST.DETECTION` semantics). Consumed by `ResponseMapper.Map` (and `ExtractIds`).
**Scoped to records extraction only** — `JsonNav.At`/`StringAt` (cursor/next_token/watermark/capture/next_url
nav) must stay single-node and must NOT flatten.
