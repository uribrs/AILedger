# Assumptions

* A1 — OPEN — The adapter registry is reachable from the application layer, so the evaluator can resolve an adapter by `platform_type` and `category` without a new Infrastructure reference.
* A2 — OPEN — `CanResumeFrom` is cheap and side-effect free, so calling it for every correlation id in a batch is affordable inside an HTTP request.
* A3 — OPEN — Resolving one adapter instance per `(platform_type, category)` group and reusing it across the batch's rows is safe.
* A4 — OPEN — A fresh run always mints a new instance document, hence a new correlationId, hence no checkpoint — so removing `forceResume` cannot make an intended fresh run resume by accident.
* A5 — OPEN — `startedAt: { $gt: new Date() }` separates the recurrence successor from a revived resume in every case the cancel sweep sees.
* A6 — OPEN — Admin can obtain every correlationId it needs for a batch call from the row array it already builds (`buildInstanceRow[0]`), with no extra Mongo read.
* A7 — OPEN — Suppressing the retention-clock bump on the decline path does not strand a row that legitimately needs its clock refreshed by an unrelated write.
* A8 — OPEN — Adding an index on `correlation_id` is safe on a live table at production row counts.
* A9 — OPEN — The three-repo change can ship in any order, because the endpoint is additive and `forceResume` was never present on cymulate-integrations `master`.

## Prior Art

Tags: `integration-service-bus`, `checkpoint-resume`, `cross-repo-contract`, `persisted-state`.
Matched 33 rows on the core tags, triaged the newest 10. Followed 3 task pointers.

* A10 — OPEN — No collector re-checks staleness inside `ResumeAsync`, so bypassing `CanResumeFrom` is sufficient to force a resume. source: lessons.md#L-7448468a (verifier, 2026-08-30)
* A11 — OPEN — A plain retry on the same correlation id is harmless when a retained checkpoint exists. source: lessons.md#L-e9b2e21e (verifier, 2026-08-30)
* A12 — OPEN — Adding an optional typed field to an inbound wire message is additive and cannot break existing producers. source: lessons.md#L-645966cc (verifier, 2026-08-30)
* A13 — OPEN — Raw SQL that reads correctly to reviewers parses correctly in Postgres. source: lessons.md#L-a8ddf024 (verifier, 2026-08-30)
* A14 — OPEN — A missing `/var/run/docker.sock` means no container runtime is available on the machine. source: lessons.md#L-0f0a6065 (verifier, 2026-08-30)
* A15 — OPEN — A checkpoint row marked terminal is inert, so its `updated_at_utc` is a stable failure timestamp. source: lessons.md#L-bb0d01f6 (verifier, 2026-08-27)
* A16 — OPEN — Readability judgment lives entirely in the collector and state lives entirely in ISB, so the three-repo decomposition is clean. source: lessons.md#L-f80a1c15 (verifier, 2026-08-27)
* A17 — OPEN — Cymulate.Integration.Client has drifted from ISB source in both directions. source: lessons.md#L-b1b66b36 (verifier, 2026-08-27)
* A18 — OPEN — Excluding a status from a recovery sweep's SELECT is enough to stop that sweep dispatching such a row. source: lessons.md#L-9db99c35 (verifier, 2026-08-27)
* A19 — OPEN — An in-memory repository used as the test double behaves equivalently to the Postgres one for lifecycle questions. source: lessons.md#L-71758bac (verifier, 2026-08-27)

### Recall re-run at contract time

Two recalled rows were re-verified rather than taken on faith, because they bear directly on the design:

* `L-6f1353b8` (a collector's `CanResumeFrom` consumes only `adapter_state` and never ISB's flat columns) — **re-confirmed as refuted**. `YamlAdapter.cs:453-462` reads `checkpoint.CreatedAtUtc` against `_maxCheckpointAge`; `DummyCollector.cs:252-254` reads `CurrentPage` and `ProcessedFindings`. Promoted into `constraints.md` as a design input, not left as an assumption.
* `L-70bf7cf8` (the collector checkpoint-helper pattern is uniform) — **re-confirmed as refuted**. 26 `CanResumeFrom` implementations against 16 files referencing `IsCheckpointStale`. Promoted into `constraints.md`.
