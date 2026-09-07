# Flatten-aware records_path — fix note

## Root cause
Records extraction went through `JsonNav.At` (object-only dot-nav: returns null the moment a segment lands on a
`JsonArray`) and `ResponseMapper.Map` (kept only `JsonObject` elements). So a `records_path` crossing a REPEATED
element — which `XmlResponse.ToJson` correctly turns into a `JsonArray` — resolved to null → 0 records, silently.
Proven before the fix: multi-host detections 0 of 3; single-host 2 of 2 (single worked only because one `<HOST>`
stays an object).

## Fix
New records-only resolver `JsonNav.ListAtFlattened(root, path)` (Interpreter.cs):
- Walks the dotted path. At each step: an ARRAY → apply the SAME remaining path to each item and concatenate
  (the jq `.HOST[].DETECTION_LIST.DETECTION` sense); an OBJECT → descend by key (with the same exact-literal-key
  preference `At` has, for `@odata.nextLink`-style keys); a missing key → nothing for that branch.
- Terminal (`AddTerminal`): an array yields its items (one level — the normal "records is an array" case + the
  flattened items of a repeated parent); a single object/value yields itself (preserves XML single-vs-array
  tolerance).
- `ResponseMapper.Map` now iterates `ListAtFlattened` and applies the emit policy: object → record; a terminal
  array (array-of-arrays at the record position) → its object members (one more level); scalar → skipped.
  Passthrough + DeepClone unchanged — flatten only changes WHICH nodes are records, never their content.
- `ResponseMapper.ExtractIds` also routed to `ListAtFlattened` (A3).

## Why At/StringAt were left untouched
`At`/`StringAt` back cursor / next_token_at / watermark / capture / next_url navigation, which resolve a SINGLE
node and must NOT flatten (flattening a cursor path would corrupt pagination/captures). The fix is a SEPARATE
resolver consumed only by records extraction (`Map` + `ExtractIds`); `At`/`StringAt` are byte-for-byte unchanged.
Other `ListAt` callers (capture_list, accumulate_list, poll_and_drain drain_path) also keep the old
non-flattening `ListAt`.

## Bounded recursion
`FlattenInto` recurses two ways: object descent (remaining path strictly shortens) and array fan-out (SAME path,
but on a strictly-smaller child subtree). The array branch keeps the path unchanged, so the bound is NOT
"segment count" alone — termination rests on the combination: the path never GROWS, and JSON nodes form an
acyclic tree (System.Text.Json cannot self-reference), so the array branch always recurses on a strictly-smaller
subtree. Both recursion forms therefore strictly decrease a well-founded measure; it always terminates, even on
adversarial input. (Doc comment on `ListAtFlattened` corrected to state this precisely — the earlier
"bounded by remaining segments" wording was imprecise for the array branch.)

## Decisions pinned
A1 implicit flatten (no YAML token; qualys.yaml:47 unchanged) · A2 emit objects + one-more-level for
array-of-arrays + skip scalars · A3 ExtractIds shares the resolver · A4 single-object tolerance preserved.

## Result
`dotnet test CollectorBase.slnx` → 75/75 (71 prior + 4 new). Harness test proves qualys.yaml:47-shape findings
emit 4 across 2 hosts (was 0). No regression.
