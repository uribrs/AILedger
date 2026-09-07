# Assumptions

- **A1** — The bus calls `SetConfiguration(Dictionary<string,string>)` with the inline `yaml` + (encrypted)
  credentials before `ValidateConfigurationAsync`, same as YamlCollector (YamlCollectorAdapter.cs:95-137).
  Status: VALIDATED against YamlCollector source; our adapter already has the `SetConfiguration` hook (:95, empty).

- **A2** — `ValidateConfigurationAsync` is reachable via the adapter method AND/OR the bus-entrypoint delegate.
  Status: OPEN — wire BOTH; confirm which the platform actually calls during execution.

- **A3** — Credentials in `SetConfiguration` may be encrypted; the existing RUN-envelope hydrator
  (RunPayloadCredentialHydrator / CollectorExecutorRunInputBuilder) decrypts them. The probe path must reuse
  that same hydration. Status: OPEN — confirm the validate path can reuse the input builder/preparer (it
  takes a PlatformEvent today; validate has only the config dict — may need a small adapter to build inputs
  from the config dict).

- **A4** — The bus `CollectAssetsAsync`/`CollectFindingsAsync` delegates receive a request object carrying
  `IsDryRun` (native CollectorFlowRunRequest.IsDryRun); our delegates discard it (:172/173 `_`). Status: OPEN
  — confirm the exact SDK delegate signature + the property name, and how `RunAndCountAsync` exposes it.

- **A5** — A dry-run cap = page_size→1 + stop after the first emitting page (per stream; first emitting step
  in a multi-step flow), still publishing the tiny sample (native does $top=1 and publishes). Status: OPEN —
  confirm the cleanest cap-point in the step loop without a vendor branch.

- **A6** — A dry-run should not write a resumable checkpoint (or writes nothing meaningful), so a later real
  run starts fresh. Status: OPEN — decide persist-nothing vs a flagged/ignored checkpoint during execution.

- **A7** — The probe request should be capped + non-publishing; for a multi-step flow it issues only the
  FIRST request (auth + 1 GET), not the whole flow. Status: OPEN — confirm a control/`emit:none` first step
  (e.g. Tenable export POST) is an acceptable probe, or whether to probe the first EMITTING request.
