# YamlCollector End-to-End Readiness Assessment

Assess whether the YamlCollector execution path is correct and ready, by tracing the full
run flow for a concrete use case (tenable.io) across three repos:

- **cymulate-magic-integration** (`/Users/user/Dev/cymulate-magic-integration`) — source of vendor
  YAML definitions (yaml-as-instructions for the engine) + canonical engine copy
  (`Platform.Integrations.Sdk`) + ADRs 0001/0002/0003.
- **IntegrationServiceBus** (`/Users/user/Dev/IntegrationServiceBus`) — receives backend run
  messages, dispatches work to adapters.
- **cymulate-integration-adapters** (`/Users/user/Dev/cymulate-integration-adapters`) — home of
  `YamlCollectorAdapter` and the second engine copy (`Cymulate.Integration.Yaml.Engine`).

## Flow to trace (tenable.io)

- **A.** Backend sends a run message for tenable.io — what is the message, where does it land?
- **B.** What ISB does with it and how work reaches the adapters layer (dispatch mechanism, payload).
- **C.** YamlCollector activation: adapter selection, YAML payload resolution, credential
  resolution, checkpoint/resume — everything before collection starts.
- **D.** Collection execution: auth, pagination/export polling, record batching, S3 upload,
  events, completion/error paths.

## Deliverables

1. End-to-end understanding write-up.
2. Correctness + readiness assessment: gaps, bugs, missing pieces, drift between the two
   engine copies, alignment vs ADR-0001/0002/0003.
3. Detailed flow diagram of the full path.

Read-only analysis — no production code changes.
