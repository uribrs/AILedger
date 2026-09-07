# Constraints

- No vendor names or vendor-conditional logic in engine code.
- Grammar backward-compatible: every merge_into yaml valid against the predecessor build stays valid and means the same (single-string `on:` = embed mode).
- Stream-through stages (no merge attached) byte-identical to current behavior.
- No changes to: adapter public surface, ISB host contract, Shared/*, S3 layout, done-event shape, csproj files.
- No yaml files created or modified. No version bump.
- Held-replay code paths REMOVED, not left dormant (no dual execution shapes).
- Enrichment cache is never persisted; rebuilds lazily after resume.
- Resume granularity = target pagination granularity; non-paginated (single-page) targets restart fresh — documented in code, not warned per-run.
- Checkpoint ordering must follow the existing invariant: cursor/state written BEFORE page advance so the persisted snapshot always points at the NEXT unit of work (see IsbExecutionSink.SetPaginationState).
- Chaining applies in declaration order; a later merge sees earlier enrichment on the page it processes.
- collect-mode dedupes by canonical source key; `unmatched: drop` in collect mode removes records with an empty collected array.
- Carried defaults: canonical case-insensitive string keys; duplicate source keys last-wins + logged; cross-page cache bounded 10000 evict-oldest; batch_size 100; page publication via existing per-topic sinks/counters.
- Schema diff minimal and targeted (no reformatting).
- Full engine test suite green at completion (behavioral assertions from the 544-suite preserved; merge tests may be reshaped to streaming, not weakened).
- C# style: small methods, mirror neighboring engine idiom, no speculative abstractions.
