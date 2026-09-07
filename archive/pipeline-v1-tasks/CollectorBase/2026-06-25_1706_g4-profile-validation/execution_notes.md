# Execution Notes — Group 4: profile validation fail-closed

## Outcome
`dotnet build` clean; `dotnet test CollectorBase.slnx` → **86/86** (82 prior + 4 new). Validation-only; no shipped
or test profile regressed.

## What changed (CollectorExecutor/Profile/Profile.cs)
- B-M1: the reserved `__`-prefix capture-key guard now also covers `AccumulateList` keys (was Capture +
  CaptureList only) → `accumulate_list: { __drained_1: ... }` is rejected at load (collision with the engine's
  internal `__drained_<n>`).
- MINOR: new shared `ValidatePagination(PaginationSpec, at, errors)` replaces the two inline cursor-only checks
  (sugar path + ValidateFetchStep). Required-field table:
  | strategy | required |
  | cursor | next_token_at |
  | cursor_watermark | next_token_at + watermark_field |
  | next_url | next_token_at |
  | none / offset / page_number | (none — positional) |

## Tests (Tests/CollectorExecutor.Test) — direct ProfileLoader.Load assertions
- `Validate_AccumulateListReservedKey_Rejected`, `Validate_CursorWatermark_MissingFields_Rejected`,
  `Validate_NextUrl_MissingNextToken_Rejected`, `Validate_ValidWatermarkAndNextUrl_Load`.

## No-regression (A1 verified)
All 82 prior tests still pass — the existing cursor_watermark (falcon) / next_url (defender, qualys) test
profiles + shipped integrations satisfy the new rules. A2 confirmed: none/offset/page_number add no requirement.

## Post-review repair (code-reviewer-1)
- MINOR FIXED: `ValidatePagination` now lowercases `pagination.Strategy` before the switch, matching the
  registry's case-insensitive name resolution — a mixed-case `strategy: Cursor` can no longer skip the
  required-field check while still resolving+running. (NIT on negative-test opposite-arm assertions left as-is.)

## Commands
- `dotnet build CollectorBase.slnx` → clean. `dotnet test CollectorBase.slnx` → 86/86.
