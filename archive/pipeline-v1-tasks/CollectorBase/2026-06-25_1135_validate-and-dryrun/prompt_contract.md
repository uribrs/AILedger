Role:
You are a senior .NET engineer replacing two faked capabilities in the CollectorExecutor SDK adapter with
real behavior, reusing the existing Shared substrate and the engine's own preparer/session/runner.

Goal:
Make `ValidateConfigurationAsync` a real live connection test, and honor `isDryRun` as a capped end-to-end
smoke run. Remove both stubs. No regression to the rest of the engine.

Context:
- Repo: /Users/user/Dev/Uri/localprojects/CollectorBase (net8.0; CollectorBase.slnx).
- Adapter: CollectorExecutor/CollectorExecutorAdapter.cs — `SetConfiguration` (:95, empty),
  `ValidateConfigurationAsync` (:100, returns Success unconditionally), the direct capability methods
  (:131/134, throw NotSupportedException), and the bus-entrypoint delegates (:160-240): `SetConfiguration`
  (:164, no-op), `ValidateConfigurationAsync` (:165, Success stub), `CollectAssetsAsync`/`CollectFindingsAsync`
  (:172/173, discard the request arg → `RunAndCountAsync`).
- Runner + helpers (Execution/): `CollectorExecutorRunner.RunAsync`, `RunAndCountAsync`,
  `CollectorExecutorRunPreparer` (parse/validate/resolve), `CollectorExecutorSessionFactory` (auth→SessionSpec),
  `CollectorExecutorRunInputBuilder` (config/creds/inputs + RUN-envelope hydration incl. decryption).
- Reference: YamlCollector `YamlCollectorAdapter.cs:95-137` (SetConfiguration retains `_lastConfiguration`;
  ValidateConfigurationAsync fails closed on no yaml, else probes). Native `DefenderVmCollector.cs:95,172` +
  `DefenderVmCollectorFlowRunner.cs` (isDryRun rides CollectorFlowRunRequest; dry-run = real-but-tiny run).

Constraints:
- See constraints.md (authoritative). Real probe (auth + 1 capped request, no egress, fail-closed on no yaml);
  dry-run honored end-to-end (capped 1-page real run, no checkpoint poison); reuse Shared; runner stays thin;
  no regression of passthrough/envelope/poll-and-drain/conformance/reserved-__-guard/pagination; net8.0; async.

Success Criteria:
1. `dotnet build CollectorBase.slnx` clean.
2. `dotnet test CollectorBase.slnx` all pass + new in-process-harness tests:
   (a) `ValidateConfigurationAsync` → Failure on bad creds / 401 probe; Success on good creds.
   (b) Fail-closed: no yaml supplied → Failure (not Success).
   (c) dry-run → a capped run (auth + first page only; demonstrably fewer requests/records than the full run)
       that reports success and publishes the tiny sample.
   (d) dry-run does NOT corrupt a subsequent real run's checkpoint/output.
3. No stub remains: `ValidateConfigurationAsync` actually probes; `isDryRun` is honored through to the runner.
4. No regression: full suite green; spot-check passthrough emit + poll-and-drain + bus/resume/page-counter wiring.

Execution Rules:
- Resolve OPEN assumptions A2–A7 against the SDK/code during execution: exact bus-delegate signature +
  IsDryRun property; whether validate comes via the adapter method, the delegate, or both (wire both);
  how the validate path (config dict only, no PlatformEvent) reuses the preparer/input-builder (incl.
  credential decryption); the cleanest dry-run cap-point in the step loop; dry-run checkpoint policy;
  probe target (first request vs first emitting request).
- If the SDK provides NO way to obtain the yaml/creds at validate time (SetConfiguration not called), STOP and
  surface it — a real connection test is impossible and the honest fallback is documented, not a fake Success.
- Keep the runner thin (no vendor branches). Reuse Shared. Async-only.

Output Format:
- Code: CollectorExecutor/CollectorExecutorAdapter.cs, Execution/* (runner + helpers as needed),
  Tests/CollectorExecutor.Test. execution_notes.md updated; state.json steps in sync.

Stop Conditions:
- All success criteria met and verified.
- The SDK cannot supply yaml/creds at validate time (surface; document fallback rather than fake Success).
- An OPEN assumption resolves against a constraint (surface).
