# Decisions

- **D1 — Release by nulling master-list slots after `AddAsync`, not by
  restructuring to streaming pages.** Smallest change that satisfies the
  byte-identical output constraint; streaming (`IAsyncEnumerable` pages) is the
  better long-term shape but is a separate, larger change.

- **D2 — Release at `AddAsync` time, not after flush.** Flushes are byte-cap
  driven and do not align with batch iterations (real files held 7–148 rows), so
  the caller cannot observe when one happened.

- **D3 — Do not mutate a shared object as the primary fix.** Projecting the row
  into a new object was considered and rejected here only because it would
  change the emission path, which the byte-identical constraint forbids. It
  remains the better shape if that constraint is ever relaxed.

- **D4 — Scope held to the memory fix.** The 404 retry policy, silent-drop
  paths, payload deduplication, GC configuration, and fan-out redesign are
  deferred so the retention fix can be attributed cleanly against a validation
  rerun. Bundling would add variables to that measurement.

- **D7 — `CybiBatchUploader` does not retain batch rows; no uploader-side change
  needed.** Rows are enumerated once into an NDJSON file and the upload streams
  from disk (A1, VALIDATED). Treat this as a stable rule for other collectors
  using the same accumulator pattern.

- **D8 — Do not detach assets from their parse page to reach the literal
  `O(one batch)`.** It would touch the fetch path
  (`fetchPagedAssetsToMemoryAsync`) and break the clean attribution D4 exists to
  protect. The `JToken.Parent` root caps retention at one page (~100 assets),
  which is constant and sufficient. Recorded as a known, bounded overshoot rather
  than fixed here.

- **D5 — No Jira ticket; branch `fix/insightvm-collector-heap-retention`.**
  User's choice; diverges from the repo's `CA-xxxxx` commit convention.

- **D6 — Falcon `SupportsBatchScopedUpload` rides in this branch as a separate
  commit.** User's explicit decision. Designer dissent recorded: it is a
  different collector and a different feature, and it belongs to the already-
  tracked task `2026-07-19_1321_cybi-batch-scoped-upload` (see
  `~/codex-state/tasks/AgentService/`), whose `task.md` covers exactly this
  batch-scoped-upload work. Keeping it as its own commit preserves the option to
  re-home it later.
