# Assumptions

Every entry starts OPEN. A transition out of OPEN requires an actor (researcher, executor, verifier)
and a citation (file:line, test name, log line, correlation ID, vendor doc, or research file).

## Prior Art

Recall tags: `cymulate-integration-adapters`, `IntegrationServiceBus`, `falcon`, `checkpoint-resume`.
Ledger: `~/codex-state/lessons.md`.

- A-P1 — OPEN — Contract types shared between IntegrationServiceBus and the adapters may have drifted
  in both directions rather than one, so a type added in IntegrationInfra is not automatically visible
  to both consumers.
  source: lessons.md#2026-08-02-sdk-client (executor, 2026-08-02)
- A-P2 — OPEN — A namespace-level shim cannot bridge a renamed contract package, so the vocabulary
  must be added in a package both consumers already reference rather than forwarded from a new one.
  source: lessons.md#2026-08-02-type-forwarding (executor, 2026-08-02)
- A-P3 — OPEN — Not every collector has a checkpoint write at all, so "all collectors gain
  flush-on-yield" may be false for some.
  source: lessons.md#2026-04-29-checkpoint-resume (researcher, 2026-06-12)

## Task assumptions

- A1 — OPEN — The four existing outcome vocabularies (`MessageDisposition`, `FlowExceptionHandling`,
  `AdapterResultStatus.PartialWaitRequired`, and unclassified cancellation) can be mapped onto one
  enum without losing information any caller currently depends on.
- A2 — OPEN — `FlowExceptionHandling` is defined in IntegrationInfra and not in a package forked
  per-consumer, so adding to it reaches both IntegrationServiceBus and the adapters.
- A3 — OPEN — The Postgres upsert in `CheckpointRepository` can report which clause rejected the write
  (page order, claim ownership, stop request) without a second query.
- A4 — OPEN — A worker that has lost its claim can be stopped mid-run through the existing
  cancellation path, without a new shutdown mechanism.
- A5 — OPEN — The checkpoint row's state blob carries a readable version marker for every collector,
  not only Falcon, so a compatibility check before claiming can be generic.
- A6 — OPEN — `CheckpointRecoveryHandler` can reach a version-compatibility check through
  `IServiceScopeFactory` without a new dependency being injected into it.
- A7 — OPEN — Flushing a partial unit is possible for Falcon (a partial aid-batch can be published as
  a smaller object). It may not be possible for collectors whose unit is an atomic vendor export.
- A8 — OPEN — Removing the host-owned second position number does not break object naming for
  collectors other than Falcon.
- A9 — OPEN — The 11 Falcon test compile errors can be migrated to the v4 two-field position without
  changing production code.
- A10 — OPEN — The uncommitted partial-completion change in `FalconCollector.cs` is correct as
  written; it has never been executed.
- A11 — OPEN — Measuring resume waste requires the collector to report where it actually stopped,
  which no collector records today.
