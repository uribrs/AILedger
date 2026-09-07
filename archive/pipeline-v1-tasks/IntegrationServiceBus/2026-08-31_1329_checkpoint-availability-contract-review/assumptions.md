# Assumptions

* A1 — OPEN — ISB can answer checkpoint availability from its authoritative checkpoint store using identifiers already available to Admin or cymulate-integrations.
* A2 — OPEN — Removing `ForceResume` does not make ordinary replay of a failed correlation id accidentally resume when the caller intended a fresh run.
* A3 — OPEN — The Admin failed-collection row has enough stable identity to query ISB without trusting Mongo for checkpoint existence.
* A4 — OPEN — cymulate-integrations can revive the existing Mongo instance without needing to decide or communicate resume-versus-fresh semantics.
* A5 — OPEN — The proposed `/GetCheckpoint` response can be a narrow availability contract and need not expose checkpoint contents or collector-specific state.
* A6 — OPEN — Current branch implementations across the three repositories represent one coherent feature attempt despite cymulate-integrations having been falsely merged.

## Prior Art

* A7 — OPEN — A plain retry on the same correlation id is harmless when a retained checkpoint exists. source: lessons.md#L-e9b2e21e (verifier, 2026-08-30)
* A8 — OPEN — Adding an optional typed field to an inbound wire message is additive and cannot break existing producers. source: lessons.md#L-645966cc (verifier, 2026-08-30)
* A9 — OPEN — A collector's resume decision consumes only adapter state and never ISB flat checkpoint columns. source: lessons.md#L-6f1353b8 (verifier, 2026-08-27)
* A10 — OPEN — Readability judgment lives entirely in the collector and state lives entirely in ISB, so the three-repo decomposition is clean. source: lessons.md#L-f80a1c15 (verifier, 2026-08-27)
