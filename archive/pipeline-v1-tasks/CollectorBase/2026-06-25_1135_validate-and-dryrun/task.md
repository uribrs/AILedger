# Task — Live config validation + dry-run

Replace two stubs in CollectorExecutor with real behavior, matching native + YamlCollector.

## A. ValidateConfigurationAsync (live connection test)
Today it returns `Success` unconditionally (a stub that lies — bad creds report "valid"); `SetConfiguration`
is empty. Make it a real probe: the bus supplies the inline `yaml` + creds via `SetConfiguration`; retain it,
then in `ValidateConfigurationAsync` build the session (real auth) and issue one capped probe request,
returning a real `Success`/`Failure`. Fail closed when no yaml. Wire both the adapter method and the bus delegate.

## B. dry-run
Today `isDryRun` is dropped (bus delegates discard the request arg; direct capability methods throw). Honor it:
read `IsDryRun` off the request, thread it into `RunAsync`, and cap the run to a one-page smoke sample
(auth → request → map → publish, but `page_size→1` and stop after the first emitting page/step). A dry-run
must not corrupt a subsequent real run's checkpoint/output.

## Scope
Adapter + runner wiring only; reuse Shared session/auth/egress; no vendor branches; passthrough emit and all
prior invariants unchanged.
