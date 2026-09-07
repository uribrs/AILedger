# Decisions

- Static taxonomy is the governing doctrine: pure-function KEEP / explicit-args KEEP / hidden-dependency PROHIBITED. — codifies the operator's toolshed paradigm; makes the kill-list precise.
- One emitter type replaces both statics (engine + naming façade merged into one public instance surface). — the façade's only added value is the naming gate + paths; two types would preserve an accidental split. Executor may split back into two instance types ONLY if the merged surface turns unwieldy; record why.
- Emitter name: behavior-named (`NdjsonBatchEmitter` suggested); no "Publisher2"/"New"/lineage names.
- Options resolved ONCE at `Create(IServiceProvider?)`; explicit ctor for tests/direct composition. — kills per-call service location while preserving sourcing behavior.
- Static entrypoints deleted outright, no compatibility wrappers. — zero consumers; additive later beats breaking later. (Operator-approved.)
- Progress/execution context remains a method parameter. — per-call data, not a construction dependency; keeps the emitter reusable across runs.
- ISink is NOT introduced; the seam note is resolved-or-narrowed per the README's own framing. — the DIP debt dies with the statics; a sink abstraction waits for a real second sink.
- Part 1 and Part 2 land as separate commits so the doctrine diff is reviewable standalone.
