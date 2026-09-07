Role:
You are a principal .NET engineer adding the first hardened, registry-resolved pagination strategy
to the CollectorExecutor seam architecture, lifted from FalconCollector.

Goal:
Add a `cursor_watermark` pagination strategy (Falcon's depth-cap → watermark-reset behavior) as a
registered IPaginationStrategy the YAML selects by name — making the decision vocabulary LIVE
(ResetToWatermark) and proving the registry indirection is a genuinely distinct strategy.

Context:
- Repo: CollectorBase. Pagination/mapper/pagesize are registry-driven seams; RunContext + FetchSignal
  exist but are not yet consumed.
- Falcon cursor ages out (~120s) during deep scrolls; the hardened behavior caps pages-per-scroll and,
  on the cap, drops the cursor and resumes from a persisted watermark floor (a record timestamp field)
  via a filter — no restart, no loss.

Constraints:
- EXISTING profiles unaffected (the 4 current tests stay green) — cursor_watermark is opt-in by name.
- Runner stays the composition root; the strategy decides reset, the runner honors it via RunContext
  (no vendor-identity branching). Watermark extraction (from mapped records) is the runner's job;
  the reset DECISION is the strategy's.
- Watermark persisted in the checkpoint so resume rebuilds from it. Narrow YAML: add only
  `max_pages_per_scroll` + reuse existing `watermark_field`; expose `{{watermark}}` template token.
- net8.0; build on Shared; no engine copy.

Success Criteria:
- `cursor_watermark` registered as a default strategy; selected purely by YAML `strategy: cursor_watermark`.
- Decision vocabulary is LIVE: on `max_pages_per_scroll`, the strategy sets FetchSignal=ResetToWatermark,
  the runner drops the cursor and continues using the watermark filter; the watermark is checkpointed.
- A test proves the reset: with a small cap, a multi-page stream resets to the watermark and collects
  ALL records with NO loss/dup, and the post-reset request carries the watermark (observable).
- Existing 4 tests stay green; build succeeds.

Execution Rules:
- Behavior-preserving for existing strategies; only cursor_watermark adds behavior.
- Verify by build + tests, not inspection.

Output Format:
- Code in CollectorBase (Strategies + minimal runner/Profile/Checkpoint/Seams additions) + a new test.
- Update this segment's state.json + execution_notes.md.

Stop Conditions:
- Stop when cursor_watermark works (reset test green) and existing tests stay green.
- Surface if the reset cannot be expressed without vendor-identity branching or an existing-behavior change.
