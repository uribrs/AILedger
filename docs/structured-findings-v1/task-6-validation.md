# Task 6 validation checkpoint — prepared pilot

Date: 2026-09-28. Branch `codex/structured-findings-contract`, starting at `cd3f2d9`.
Initial worktree status was clean. Scope: pilot tools, separate RN1 input artifacts, reports and
handoff/backlog updates. No production implementation, global instructions/skills, historical records
or frozen task-1 expected outputs changed.

**Task 6 is incomplete.** The bounded live trial is prepared but has not executed. Explicit approval
for a new billable provider episode and the subsequent operator delivery decision remain pending.
Do not present the prepared-data trial or old task-4 provider acceptance as task-6 live acceptance.

## Checks and results

| Check | Actual result |
|---|---|
| Final external pilot build | Exit 0; zero warnings/errors |
| Prepared-data MCP pilot | 21 trials passed; exact text/reference/attribution checks and six failure/recovery scenarios |
| Standard external `dotnet test` | Exit 0; main 1,901 passed, Memory 99 passed; zero failures/skips |
| Repository-aware full runner | Exit 0; main 1,901 passed, Memory 99 passed; zero failures/skips/runner errors |
| Frozen hash checker | All 11 hashes unchanged before/after changes |
| Read-only baseline probe | All four historical report cases and four provider-cost samples matched |
| Baseline external build | Exit 0; zero warnings/errors |
| RN1 extraction reproducibility | All six checked-in artifacts reproduce byte-for-byte from the original history |
| Historical input integrity | Original events.jsonl and runs/RN1.json hashes still match the extraction manifest |
| Python harnesses | Both parse/compile; preparation-only live harness ran without starting a provider |
| Git whitespace | `git diff --check` clean |

The pilot is a self-validating console probe, not 21 added xUnit cases; the full suite counts remain
unchanged. It uses real MCP parsing, application/storage and existing retrospective reports through
in-process streams, with runless fixture attribution. It launches no model. See [report](task-6-pilot.md),
[all retained final measurements](task-6-prepared-results.json) and
[tool instructions](../../tools/FindingsPilot/README.md) for populations, limits and commands.

The unchanged independent-clock
`ReconsiderationConsultantTests.R8_ConsultationRequiresARecordedSubjectRole` passed in both full runs.
Its race remains an existing limitation, not a fix delivered here. No unrelated test failed.

## Execution record and reproducible paths

- `/tmp/ailedger-task6-build-final.log`: final tool build; artifacts `/tmp/ailedger-task6-build`.
- `/tmp/ailedger-task6-pilot-final.log`: final measured probe; all scenario ledgers, observations,
  early checkpoints and existing CLI reports retained under `/tmp/ailedger-task6-pilot-final`.
- `/tmp/ailedger-task6-standard.log`: standard full invocation, using `--artifacts-path
  /tmp/ailedger-task6-standard`, `-m:1`, `-p:NuGetAudit=false`, `-p:UseSharedCompilation=false`,
  `--verbosity minimal --logger trx --results-directory /tmp/ailedger-task6-standard/results`.
- `/tmp/ailedger-task6-runner.log`: `UseSharedCompilation=false sh scripts/test-governed.sh all`;
  full build/suite logs under `/private/var/folders/v3/pncbbv7x45jgp4bnd81fjnl80000gn/T/ailedger-tests.O9AqeF`.
- `/tmp/ailedger-task6-baseline-build.log`, `/tmp/ailedger-task6-baseline-probe.log`: external build
  under `/tmp/ailedger-task6-baseline`; direct execution of `FindingsBaseline.dll` against
  `tests/Fixtures/structured-findings-v1`, without capture mode.
- `/tmp/ailedger-task6-rn1-recheck`: independently repeated extraction, matched to the checked-in data.
- `/tmp/ailedger-task6-live-prepared/trial-plan.json`: prepared live trial, **not executed**.

Temporary artifacts may expire. The source/provenance fixtures and final measurement summary are
committed so the input and measured values survive. Reruns produce new disposable outputs; do not
rewrite this record or recapture historical baselines to make a result pass.

Intermediate failures are retained: the first extraction attempt hit Python's variable-fraction
`fromisoformat` parsing limitation before writing an output directory; explicit timestamp parsing
fixed it. The first pilot build failed at four byte comparisons because span overload resolution
crossed an await; the comparison now reads bytes before comparing. The second build and initial
21-trial probe passed. The initial probe measured exchange totals only. The final probe additionally
records the full recording window, uses the existing CLI's full refusal-aware retrospective, and
keeps focused recovery helpers. Initial logs remain `/tmp/ailedger-task6-build.log`,
`/tmp/ailedger-task6-build-2.log`, `/tmp/ailedger-task6-pilot-1.log` and
`/tmp/ailedger-task6-pilot-1/`. They are not substituted for the final results.

The final timed probe overlapped full-suite validation. Its distribution is descriptive under that
load, not an isolated performance benchmark. No causal model-time or whole-workflow speedup is claimed.
The live harness execution path and semantic review still need the authorized live episode.

## Authorization and installation

Development remained outside the kernel workflow: no ai-kernel invocation, governed development
task, development governance write or agent dispatch. All new kernel writes were disposable probe/
test fixtures. No billable provider episode, merge, publication, task-7/8 implementation or migration
was performed.

During this work the user explicitly requested installation for trying a new governed task, superseding
the earlier no-install restriction for that action. A clean archive of **cd3f2d9**, excluding unfinished
pilot tools, was packed outside the checkout at `/tmp/ailedger-install-cd3f2d9.0cUMpp`. The global tool
was updated from **2.0.157** to **2.0.163**. `ailedger version` verified **2.0.163 from cd3f2d9**.
This installation did not authorize a billable task-6 episode or install the unfinished pilot.


## Subsequent checkpoint — authorized live evaluation

The prepared-pilot record above remains unchanged. After the user approved the proposed one-episode
plan with “just proceed”, one live Codex episode completed and passed the original harness and
additional transcript/canonical/measurement checks. See [live validation](task-6-live-validation.md)
and [retained results](task-6-live-results.json) for the actual observations, source review, scope
limits and both launch outcomes. The first sandbox socket failure occurred before the adapter ran;
its failed completion and absent usage remain visible. No billable retry was performed.

The continuation changes only task-6 evidence and documentation/status. All 11 frozen hashes,
retained source/CLI/goal hashes, JSON/provenance checks, local links and Git whitespace checks pass.
The prior full-suite results are retained without rerunning unchanged implementation. The completed
pilot supports the proposed first delivery, but **the operator delivery decision is still pending**.
The episode authorization does not authorize delivery acceptance, additional operations or task 7.
