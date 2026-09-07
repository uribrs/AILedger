# Verifier-1 — G4 Profile Validation (independent, adversarial)

Ran the suite myself; read the implementation and all shipped profiles; traced each negative test
to a real validation path. Evidence below.

## Criterion 1 — Build clean + suite green (≥86, 0 failed)
**PASS.** `dotnet test CollectorBase.slnx` → `Failed: 0, Passed: 86, Skipped: 0, Total: 86`.
Build compiled (Strategies.dll + test dll emitted, no warnings surfaced in tail). Prior baseline was
82; +4 new = 86.

## Criterion 2 — No shipped profile rejected by the new rules
**PASS.** Spot-confirmed the now-required fields are present in every shipped profile that uses a
gated strategy:
- `integrations/crowdstrike-falcon.yaml` — `cursor_watermark` in BOTH streams. assets (L28-32) sets
  `next_token_at: meta.pagination.after` + `watermark_field: last_seen_timestamp`; findings step
  (L56-61) sets `next_token_at` + `watermark_field: updated_timestamp`. Satisfied.
- `integrations/defender-vm.yaml` — six `next_url` blocks (L33,41,55,65,73,88), each with
  `next_token_at: "@odata.nextLink"`. Satisfied.
- `integrations/qualys.yaml` — two `next_url` blocks (L27,45), each with
  `next_token_at: HOST_LIST_OUTPUT.RESPONSE.WARNING.URL`. Satisfied.
- `integrations/cortex-xdr.yaml` — only `offset` / `none` (positional, no requirement). Unaffected.
- `integrations/tenable-io.yaml` — only `none`. Unaffected.
In-suite test profiles also clear the bar: both cursor_watermark fixtures (CollectorExecutorTests.cs
L3181, L3207) set next_token_at + watermark_field; all next_url fixtures (L2802, L2895, L3282) set
next_token_at. The 86 green tests are the load-bearing evidence; the manual read confirms no shipped
profile is on the edge.

## Criterion 3 — Negative tests force a REAL validation error; positive actually loads
**PASS.**
- `Validate_AccumulateListReservedKey_Rejected` (L1308): `accumulate_list: { __drained_1: "a.b" }`
  inside a steps/fetch. The new `__`-guard at Profile.cs L215-219 now concats `AccumulateList.Keys`,
  so this hits the "reserved" branch. Asserts `Contains("reserved")`. The step is otherwise valid
  (request.path, pagination none, records_path present) so no other error masks it.
- `Validate_CursorWatermark_MissingFields_Rejected` (L1330): single-fetch sugar with valid
  request.path + records_path, only `strategy: cursor_watermark` and nothing else → exactly the two
  new errors fire. Asserts both `next_token_at is required for cursor_watermark` AND
  `watermark_field is required for cursor_watermark`. Both strings match Profile.cs L296/L298
  verbatim. This is the strongest test — it proves the watermark_field branch independently.
- `Validate_NextUrl_MissingNextToken_Rejected` (L1349): sugar, valid path+records_path,
  `strategy: next_url` only → asserts `next_token_at is required for next_url`, matches L302.
- `Validate_ValidWatermarkAndNextUrl_Load` (L1366): loads two profiles that satisfy the rules and
  asserts `.Vendor == "demo"` — confirms the validator does not over-reject and the parse succeeds.
All four use direct `ProfileLoader.Load(...)`, not the adapter, so they isolate validation. No risk
of an unrelated failure (auth/HTTP) being mistaken for the validation error.

## Criterion 4 — Validation-only + `__`-guard extended to accumulate_list
**PASS.** Diff is confined to `Profile.cs`: (a) L217 adds
`.Concat(st.AccumulateList?.Keys ?? Enumerable.Empty<string>())` to the existing reserved-key sweep —
extension only, Capture/CaptureList coverage unchanged; (b) the two old inline cursor-only checks are
replaced by one `ValidatePagination` helper (L286-305) called from both the sugar path (L264) and
`ValidateFetchStep` (L278). No engine/runner/strategy/checkpoint code touched. `cursor` rule is
preserved identically; `cursor_watermark` and `next_url` are added; `none`/`offset`/`page_number`
remain unguarded (correctly — positional). No runtime/dispatch behavior change.

## Task note (success criterion 4)
**PASS.** `execution_notes.md` documents the `__`-guard extension (B-M1) and includes the
strategy→required-field table (cursor / cursor_watermark / next_url / positional). Matches the code.

## Adversarial checks performed
- Confirmed negative tests are not passing for the wrong reason (each fixture is otherwise valid; the
  only triggered error is the intended one — verified by reading the validator order).
- Confirmed the watermark_field assertion is genuinely exercised (separate `Contains`), not folded
  into the next_token_at check.
- Swept all 5 shipped profiles, not just the two named, for any gated strategy.
- Verified in-suite fixtures (the real regression surface behind the 86 count) satisfy the new rules.

## VERDICT: PASS
All 4 success criteria met. Build clean, 86/86 (0 failed), no shipped or in-suite profile regressed,
negative tests provably hit the new validation paths, positive test loads, change is validation-only,
and the task note documents both the guard extension and the requirement table.
