# Task: CollectorExecutor remediation — 3 audit findings

Fix exactly three findings from the conformance audit
(`ai/active/2026-06-24_1410_collector-executor-conformance-audit/`). Code-bearing.

1. **Robustness**
   - 1a. Non-string cursor/next-token crash — `JsonValue.GetValue<string?>()` throws on a numeric/bool
     token in `CursorPaginationStrategy`, `CursorWatermarkStrategy`, `NextUrlPaginationStrategy`; the
     exception escapes the page-loop catch filter and bypasses partial-success. Make token extraction
     tolerant (string or coerced numeric/bool).
   - 1b. Zero/negative `page_size` or `hydrate.batch_size` → infinite loop/hang. Clamp effective sizes to ≥ 1.
2. **De-duplicate** the parse+validate prefix shared by `Preflight` and `RunAsync` (one source of truth;
   behavior-preserving).
3. **Delete the dead `FetchSignal`/`FetchDecision`/`RunContext.Signal` vocabulary** and fix the misleading
   Seams comments that present it as the live mechanism (the real reset is the returned `Paginator.Step`).

## Out of scope
Other audit findings (sparse-page truncation, string-only watermark, `request.headers` unapplied,
18-arg method signatures) — explicitly deferred. No new abstractions beyond the item-2 extraction.
Conformance from the prior task (interfaces, bus routing, resume, per-target page counter, ingress) must
remain intact.
