# Task: Falcon Spotlight Staged Collection And Planned Yield

Refactor the Falcon **findings** flow so Spotlight collection runs in value-ordered status
stages and applies planned, host-scheduled waits to reduce vendor pressure — building on the
already-landed internal assets-stage (`Stage = assets/findings`) work.

## Scope

1. **Staged Spotlight collection.** After the internal assets stage, run Spotlight findings in
   ordered status lanes: `open` → `reopen` → `closed`. Each lane composes its `status:'...'`
   clause with the existing suppression / timestamp / month-segment / AID-scoped / sort / facet
   filter behavior. `expired` is out of scope for v1.

2. **Planned yields (host-scheduled, unbudgeted).**
   - `10m` wait between status stages (after `open`, after `reopen`; **not** after `closed`).
   - `5m` wait after each completed `1,000,000` published Spotlight findings (cumulative across
     all lanes in one run); **never** after final completion / final page / final lane.
   - These are *planned* yields, not failures: no failure/completion events; they must **not**
     consume the 5xx recovery budget.

3. **Cursor TTL safety (120s).** Any resume from a checkpoint older than `120s`, or any
   scheduled delay over `120s`, must resume **without** Falcon `after` cursors — re-anchoring
   each cleared cursor from its matching watermark/floor. This is a *new* gate, separate from
   the existing 23h checkpoint-staleness threshold.

4. **Remove** the previously proposed fixed Discover→Spotlight auth delay. No fixed delay
   between internal assets completion and the first Spotlight lane.

5. **Preserve** existing failure behavior: 5xx → session retries → Falcon scheduled recovery
   `5m/15m/30m` → fallback; cursor-404 and Spotlight-5xx-with-`after` → watermark fallback.

## Out of scope
- Standalone `FalconAssetsFlow` (unchanged).
- `expired` status lane.
- Making `spotlightCursorTtl` user-tunable (internal constant for v1).
- The findings vulnerability record shape / `findings_*.json` content.
