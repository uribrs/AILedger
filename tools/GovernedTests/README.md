# Governed .NET tests

From any working directory, run `sh /path/to/AILedger/scripts/test-governed.sh`.
The script builds the current checkout into a fresh directory under `TMPDIR`, then runs both
test assemblies without VSTest's socket. It prints the output directory and keeps complete logs
there. NuGet vulnerability auditing is disabled for this local test build; restore/build failures
still stop execution. No source-tree build outputs or default permission grants are changed.

The runner uses xUnit 2.5.3's discovery and execution framework, matching these test projects.
xUnit handles theories, fixtures, async lifetime, output helpers and skips. Collections run
sequentially. Each assembly runs in a separate process with its original dependency manifest,
so native libraries and the existing refusal-journal child-process probe keep their runtime setup.

To run one suite or a targeted selection:

```sh
sh scripts/test-governed.sh AILedger.Tests ClaimValidationTests
```

The default is `all`; otherwise select `AILedger.Tests` or `AILedger.Memory.Tests`. An optional
second argument filters test display names by a case-insensitive substring. Each selected assembly
must discover tests; an empty selection, framework error or test failure returns nonzero. The script
stops at the first failed suite. The smaller memory suite runs first to expose its application-data
permission requirement early. That suite currently includes a test that writes to platform
application-data storage; a sandbox denying it will fail the test. This runner does not grant access
or reinterpret its xUnit v3-only dynamic-skip exception as a pass under xUnit v2.

To reuse existing build outputs, keep the working directory in the repository and use absolute
paths to the saved outputs (replace `/tmp/run` with the printed scratch path):

```sh
dotnet exec --depsfile /tmp/run/artifacts/bin/AILedger.Tests/debug/AILedger.Tests.deps.json \
  --runtimeconfig /tmp/run/artifacts/bin/AILedger.Tests/debug/AILedger.Tests.runtimeconfig.json \
  /tmp/run/artifacts/bin/AILedger.Tests/debug/GovernedTests.dll \
  /tmp/run/artifacts/bin/AILedger.Tests/debug/AILedger.Tests.dll ClaimValidationTests
```

Discovery is bounded to two minutes and execution to ten minutes per assembly. A timeout is a
runner error, never a passing result. If the command is denied, record the denial and stop that
operation; do not rewrite it to evade approval or construct another test host.

Run `sh tools/GovernedTests/check.sh` to check the runner and its launch-briefing integration.
Run `python3 tools/GovernedTests/check_script.py` for command orchestration, fail-fast behavior,
path quoting and output-containment checks using a fake `dotnet` executable (no build required).
`Fixtures` is an intentional runner probe assembly, not part of the product suite. `LifecycleCases`
must pass with seven executed cases including one skip. `FailingCases`, `CleanupCases`, and an
unmatched filter must return nonzero. In particular, fixture cleanup failure must fail the runner
even when the individual test passed.
