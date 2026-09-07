# Constraints

- Validation-only change in `ProfileLoader.Validate`; no runtime/engine behavior change.
- `ValidatePagination` adds required-field rules ONLY for cursor (next_token_at), cursor_watermark (next_token_at + watermark_field), next_url (next_token_at). `none`/`offset`/`page_number` add nothing.
- Extend the reserved `__`-guard to include `AccumulateList` keys alongside Capture/CaptureList.
- All 5 shipped integrations/*.yaml + every existing test profile MUST still validate (no regression).
- Generic engine, no vendor identity; net8.0; CollectorBase.slnx; the 82 passing tests stay green.
