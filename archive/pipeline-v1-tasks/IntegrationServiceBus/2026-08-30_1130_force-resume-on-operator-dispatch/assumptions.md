# Assumptions

All OPEN.

## Prior Art

Tags: `integration-service-bus`, `checkpoint-resume`, `persisted-state`. The two immediately
preceding passes on this branch minted fourteen rows; the four bearing on this one are seeded.

- OPEN — Excluding a status from a sweep's SELECT is enough to stop that sweep dispatching such a
  row. Refuted: select-then-claim needs the guard on the claim.
  source: lessons.md#L-9db99c35 (verifier, 2026-08-27)
- OPEN — A terminal checkpoint row is inert. Refuted: the lock revives it by design.
  source: lessons.md#L-bb0d01f6 (verifier, 2026-08-27)
- OPEN — The Postgres raw SQL added for retention is correct. Untested: Docker unavailable.
  source: lessons.md#L-a561ce63 (verifier, 2026-08-27)
- OPEN — A feature's scope can be priced by the number of predicates it changes. Drifted: each pass
  found the replaced code was silently doing more than it appeared to.
  source: lessons.md#L-dc61d2c4 (verifier, 2026-08-27)

## Task assumptions

- A1 OPEN — No collector re-checks staleness inside `ResumeAsync` or the load path it uses, so
  bypassing `CanResumeFrom` is sufficient to force a resume. Evidence today covers Falcon and
  CloudGuard only.
- A2 OPEN — `PlatformEvent.Metadata` survives the whole path from `TriggerFlowMapper` to
  `ExecuteWithResumeAsync` without being rebuilt, filtered, or dropped.
- A3 OPEN — A marker set on an operator dispatch cannot reach an automatic sweep dispatch, including
  via `platform_event_json` rehydration.
- A4 OPEN — `AdapterRunMessage` is ISB-owned and a new optional field breaks no producer or consumer.
- A5 OPEN — With the marker absent, every existing path behaves byte-for-byte as today.
- A6 OPEN — A forced resume that throws or returns failure is reported as a failure and does not
  silently restart the collection.
- A7 OPEN — Forcing the resume does not bypass anything else the gate was incidentally protecting —
  i.e. `CanResumeFrom` has no side effect the resume path depends on.
- A8 OPEN — The marker is observable in logs, so an overridden resume is explicable after the fact.

---

# Disposition (verifier-2 plus the live-database run) — terminal

Citations in `review/verifier-2.md`. Rows marked *(database)* were settled by the first real Postgres
run, which happened after that verifier finished.

| id | status | actor |
|----|--------|-------|
| A1 — no collector re-checks staleness inside `ResumeAsync` or its load path | NEVER-TESTED — no collector implementation in this repo; inference only, accepted in `decisions.md` | verifier |
| A2 — the marker rides `PlatformEvent.Metadata` | NEVER-TESTED (superseded before execution — `Metadata` serializes into `platform_event_json`, so it rides `ProcessEventCommand` instead) | verifier |
| A3 — the marker cannot reach an automatic sweep dispatch | **VALIDATED** — real round trip through the real sweep, killed by mutation | verifier |
| A4 — a new optional field on `AdapterRunMessage` breaks no producer or consumer | **VALIDATED** after repair. Cycle 1 rejected it: a malformed value failed deserialization of the whole message and settled the delivery as Retry. The bound property was removed. | verifier |
| A5 — with the marker absent every path behaves byte-for-byte as today | **REJECTED** as literally stated — a changed log template and one extra deserialize on the unmarked trigger-flow path. Inert. | verifier |
| A6 — a forced resume that fails is reported as a failure, never a silent restart | **VALIDATED** | verifier |
| A7 — `CanResumeFrom` has no side effect the resume path depends on | NEVER-TESTED — no collector in this repo to observe | verifier |
| A8 — the marker is observable in logs | **VALIDATED** | verifier |
| Prior art — a sweep's SELECT exclusion is enough to stop dispatch | VALIDATED as refuted; the claim guard is the load-bearing half | verifier |
| Prior art — a terminal row is inert | VALIDATED as refuted; the lock revives it by design | verifier |
| Prior art — the Postgres raw SQL added for retention is correct | **REJECTED (database)** — `MarkFailedAsync` threw `42883: operator does not exist: text[] - text` on every call. The retention feature was inoperative against real Postgres. Fixed; 47/47 now. | verifier |
| Prior art — a feature's scope can be priced by the predicates it changes | VALIDATED as refuted, again | verifier |

## Must not be re-assumed without new evidence

- That raw SQL reads correctly therefore parses correctly. Three verifier passes and three blind
  reviews read the strip expression; one reviewer raised operator precedence and it was dismissed by
  inspection. Only the database disproved it.
- That a reported "Docker unavailable" means no container runtime exists. It was installed and stopped.
- That the force marker could ride any field of `PlatformEvent` (A2).
- That a collector does not re-check staleness inside its resume path (A1, A7) — still inference.
