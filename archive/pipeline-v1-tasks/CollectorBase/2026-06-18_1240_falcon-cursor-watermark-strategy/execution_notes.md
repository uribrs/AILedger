# Execution Notes — cursor_watermark (first hardened strategy)

DONE + VERIFIED (build green; tests 5/5):
- `CursorWatermarkStrategy` (Strategies/Pagination): follows the cursor; on `max_pages_per_scroll`,
  drops the cursor and emits FetchSignal=ResetToWatermark, resuming from the watermark floor. Registered
  as the default "cursor_watermark" paginator — selected purely by YAML.
- Decision vocabulary is now LIVE (was dead per the prior review): the runner honors the reset by
  rendering {{watermark}} (empty → populated after a reset), the strategy decides, the runner extracts
  the watermark (max of pagination.watermark_field across mapped records) and persists it in the
  checkpoint (CheckpointState.Watermark) + seeds it on resume.
- Profile additions (narrow): pagination.max_pages_per_scroll (int) + reuse watermark_field +
  {{watermark}} token. RunContext.ScrollDepth added.
- Test: ThingsProfile (cursor_watermark, cap=1, watermark_field=updated). Mock returns 4 things with
  increasing 'updated'; the wm filter drives continuation. Asserts: all 4 collected, no dup/loss, and a
  post-reset request carries `wm=2026-01-02 & after=` (proves ResetToWatermark went live + cursor dropped).

PROVES (addresses prior review M2): the registry indirection is now genuine — cursor_watermark is a
DISTINCT strategy, not the re-switching shim. A vendor's hardened quirk = a registered strategy its
YAML names, with zero runner/orchestrator edits.

BEHAVIOR-PRESERVING: the 4 prior tests (assets=5, findings=3, resume, open/closed) stay green; existing
profiles (strategy: cursor) are unaffected.

KNOWN REFINEMENT (real Falcon, later): boundary-ID handling for non-unique timestamps (here timestamps
are strictly increasing, so `updated > watermark` is exact). Falcon adds boundary IDs to avoid
boundary dup/loss — a refinement of this strategy, not a new seam.

FILES: Strategies/Pagination/CursorWatermarkStrategy.cs (+register in StrategyRegistryDefaults);
CollectorExecutor/Profile (MaxPagesPerScroll), Seams (ScrollDepth), Checkpointing (Watermark),
Execution/CollectorExecutorRunner.cs ({{watermark}} + extraction + checkpoint persist/seed);
Tests (ThingsProfile + reset test + mock /things/query).
