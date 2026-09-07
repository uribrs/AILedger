Role:
You are a .NET engineer tightening declarative-profile load-time validation.

Goal:
Reject two malformed/colliding profile shapes at load: (1) an `accumulate_list` key with the reserved `__`
prefix; (2) `cursor_watermark` missing `next_token_at`/`watermark_field` and `next_url` missing `next_token_at`
— without rejecting any currently-valid profile.

Context:
- `Profile.cs:215` reserved-`__` guard covers Capture + CaptureList, not AccumulateList.
- `next_token_at` enforced only for `cursor` (`:262` sugar, `:277` ValidateFetchStep).
- Shipped profiles already set the required fields where used.

Constraints:
- See constraints.md. Validation-only; shared `ValidatePagination` helper; all shipped + test profiles stay valid;
  no vendor identity; net8.0/CollectorBase.slnx; 82 prior tests green.

Success Criteria:
1. `dotnet build CollectorBase.slnx` clean.
2. `dotnet test CollectorBase.slnx` — 82 prior pass + new: (a) accumulate_list `__`-key → validation error;
   (b) cursor_watermark missing next_token_at OR watermark_field → error; (c) next_url missing next_token_at →
   error; (d) valid cursor_watermark + next_url profiles still load.
3. All shipped + test profiles still validate (full suite green).
4. note.md / execution_notes: the `__`-guard extension + the cursor/cursor_watermark/next_url requirement table.

Execution Rules:
- Pin A1/A2/A3; VERIFY shipped profiles still validate (don't just assume). Add the guard term + the shared
  helper; replace both inline cursor checks. Respect constraints.

Output Format:
- Profile.cs change + tests in Tests/CollectorExecutor.Test; note + execution_notes in the task dir.

Stop Conditions:
- A new rule would reject a shipped profile — stop and reconcile (the rule or the profile is wrong).
- Goal achieved and full suite green.
