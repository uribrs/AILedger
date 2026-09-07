# Assumptions

- **A1 (VALIDATED)** — The three repos live under /Users/user/Dev/, not /Users/slava/work/ as
  CLAUDE.md states. Validated by filesystem check 2026-07-13.
- **A2 (VALIDATED)** — "Backend sends a run message" = the platform backend publishing a
  trigger-flow `AdapterRunMessage` to ISB's RMQ input queues (or HTTP /events/publish). No
  Magic-side router is involved anywhere (it does not exist — ADR-0001 open item).
- **A3 (VALIDATED)** — The dispatch payload carries the vendor YAML inline (`payload["yaml"]`),
  inlined HOST-side by ISB from S3 `{env}/yaml-files/{name}.yaml`
  (ProcessEventCommandHandler.TryPrepareYamlEngineDispatchAsync); the adapter also has its own
  S3 fallback loader.
- **A4 (VALIDATED)** — tenable.io has both a YAML definition (magic `integrations/tenable.yaml`)
  and a native adapter (`TenableIoCollector` in adapters repo). Routing: `Adapters:UseYaml` flag
  + S3 YAML hit → YamlEngine; miss → silent fallback to native (loop-guarded).
- **A5 (VALIDATED)** — Snapshot recorded: magic master@1d1c5c2, ISB dev@6a9fcc0,
  adapters dev@86b2029 (all fetched ≤1 day before assessment).
