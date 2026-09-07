# Decisions

- cursor guard compares the returned token to `ctx.Cursor` (the cursor that produced this page); equal+non-empty → stop. Parity with cursor_watermark/next_url non-advance guards.
- ProbeAsync resolves the request-bearing step generically (step.Request if path present, else step.Step.Request) — no kind-specific branching beyond that fallback.
- Doc removals are grep-justified; `hydrate` stays (real).
- All three are independent; batched only for efficiency.
