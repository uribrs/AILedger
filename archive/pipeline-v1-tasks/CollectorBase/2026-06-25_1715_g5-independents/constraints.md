# Constraints

- B-M2 cursor guard must stop ONLY a non-advancing cursor (token == current ctx.Cursor); a normal advancing cursor (token differs each page) must be unaffected.
- ProbeAsync change is request-target resolution only; fetch-first profiles must behave exactly as before (step.Request still used when its path is non-empty).
- Doc-drift is docs-only; remove/annotate a knob ONLY with a grep showing zero code consumers. `hydrate` is real (HydrateSpec consumed) — keep it.
- Generic engine, no vendor identity; net8.0; CollectorBase.slnx.
- No regression of the 86 passing tests (esp. the existing cursor/cursor_watermark/next_url pagination tests).
