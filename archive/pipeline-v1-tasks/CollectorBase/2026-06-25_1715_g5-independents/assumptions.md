# Assumptions

- A1 — `ctx.Cursor` is the cursor that produced the CURRENT page (runner sets it at Runner.cs:243/251 before
  the step), so comparing the returned token to it detects non-advance. STATUS: OPEN — LEAN true; execution confirms.
- A2 — A normal advancing cursor returns a DIFFERENT token each page, so `token != ctx.Cursor` keeps hasMore
  true and the guard doesn't break normal pagination. STATUS: OPEN — must hold (existing cursor tests stay green).
- A3 — Which doc knobs are dead is determined by grep at execution (StopWhen/stop_when, SortKey/sort_key,
  timeWindow/time_window), not assumed; `hydrate` is consumed and stays. STATUS: OPEN.
- A4 — The first page's cursor is null/empty (no prior cursor), so the guard (token != null) lets page 1→2
  proceed normally; non-advance only triggers from page 2+ when the vendor repeats a non-empty token. STATUS:
  OPEN — LEAN true.
