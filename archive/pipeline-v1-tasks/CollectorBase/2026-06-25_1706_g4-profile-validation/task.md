# Task: Profile validation fail-closed

Tighten `ProfileLoader.Validate` (`CollectorExecutor/Profile/Profile.cs`) so two malformed/colliding profiles
are rejected at load.

## Items (established)
- B-M1: the reserved `__`-prefix capture-key guard (`Profile.cs:215`) covers `Capture` + `CaptureList` but not
  `AccumulateList` → `accumulate_list: { __drained_1: ... }` collides with the engine-internal `__drained_<n>`.
  Fix: add `AccumulateList` keys to the guard.
- MINOR: `next_token_at` enforced only for strategy `cursor` (`:262` sugar, `:277` ValidateFetchStep). `next_url`
  needs `next_token_at`; `cursor_watermark` needs `next_token_at` + `watermark_field`. Fix: a shared
  `ValidatePagination` helper called from both sites.

## Out of scope
Validation only — no runtime behavior change. `none`/`offset`/`page_number` keep no extra required fields.
All shipped + test profiles must still validate.
