# Task 11 validation — bounded handoff preparation

2026-09-29. Implemented directly in the requested worktree on
`codex/structured-findings-contract`, initially clean at
`31215b7f93b39134331267156c75a0dff2845406`. The installed CLI was independently checked as
`2.0.174 from dbeafbd`, not assumed to match the checkout. Its package SHA-256 was
`e27e22e3abc9b54e4d620445fc0a0b1eb797952411e759c059a7e7816c8f5877`;
the executable shim hash was `686fe72305c58146170f3cba0cbdeb89e426422d5a5e506b8ebecbf987bb9c87`.
That exact package was preserved for rollback before installation.

## Verified scope

The [contract](../bounded-handoffs-v1.md) defines the read-only preparation API/CLI, explicit
curation inventory, versioned inputs, package identity, bounded authored result and authority
boundary. No recording schema, event/replay rule, default capability, grant, dependency,
assurance, host dispatch or provider integration changed. The new CLI commands are explicitly
classified read-only and call the existing inspection service. No arbitrary source path is opened.

22 added test cases cover exact data/digests and source fidelity, omission retrieval without
writes, material/input-inventory/coverage/ref/digest validation, budgets, invalid/null JSON,
unknown/spoofed fields, denied/stale reads, Unicode chunk reconstruction, task-8 receipt identity,
bounded result checks, and four frozen/current slices. Existing task-10 tests retain role/run and
assurance-isolation coverage. The command inventory test now explicitly names the three intended
additions and checks their read classification; this was not a frozen-measurement recapture.

| Check | Result |
|---|---|
| Focused handoff cases | 22 passed |
| Full `dotnet test AILedger.sln` | 2,204 main + 99 Memory passed; zero failures/skips |
| Repository runner | 2,204 main + 99 Memory passed; zero failures/skips |
| Existing frozen fixture hashes | 11 unchanged |
| Existing frozen historical reports | Four cases matched |
| Existing frozen provider costs | Four samples matched |
| Four new frozen input hashes/cutoffs and material-loss tests | Passed |
| Development executable package/retrieval/spoof/no-write probe | Four cases passed |
| Original source histories after capture and testing | All four SHA-256 values unchanged |
| Build and whitespace | No compiler warnings/errors; clean diff check |

Initial compilation exposed a Core-to-Storage serializer dependency, help-string indentation and
one missing test namespace. They were corrected before focused verification. The first full run
passed 2,203 main cases and all 99 Memory cases but failed the intentionally explicit CLI inventory
because it lacked the new commands. Its inventory was updated and read-only assertions added;
the final full run above passes. No historical expected output was regenerated to make a failure
pass. The separate live-provider measurement script requires real episode arguments and was not
used as a trial; frozen provider-cost compatibility was checked by FindingsBaseline.

## Frozen knowledge checks and observations

The new [manifest](../../tests/Fixtures/bounded-handoffs-v1/manifest.json) retains exact source,
event cutoff/time and hashes. Axonius stops before RV1; Falcon stops after RN1/RY1 and before RS1;
S3 stops before synthesis. Later events never enter preparation. The current task is the actual
unfinished cursor re-anchor task at version 39, with separately pinned source dependencies at 404.
Current dependency material is not inserted into any historical arm.

The [evaluator](../../tests/Fixtures/bounded-handoffs-v1/evaluation.json) checks retained no-dedup
rationale, environment/test limitations, shipping/working-tree identities, rejected host premises,
storage/engine contradictions, keyed-store counterexamples, proposed versus accepted decisions
and open uncertainty. Dropping each declared required material input is refused. Withheld later
claim IDs are absent. These are deterministic loss diagnostics, not agent trials or semantic proof.

Development probe observations (package bytes include inventory, escaped record JSON and metadata;
read counts below cover preparation, excluding the separately reported initial curation index):

| Slice | Version | Included/visible | Omitted | Package bytes | Included content bytes | Inspection/retrieval calls |
|---|---:|---:|---:|---:|---:|---:|
| axonius | 58 | 20/36 | 16 | 68,707 | 18,677 | 3/20 |
| falcon | 78 | 47/66 | 19 | 112,595 | 30,591 | 4/47 |
| s3 | 219 | 143/168 | 25 | 346,625 | 142,062 | 7/143 |
| current-reanchor | 39 | 25/28 | 3 | 104,719 | 58,104 | 2/25 |

Each executable case also deliberately retrieves one omitted stage record, checks its digest, and
checks that every disposable source file remains unchanged. It performs three calls (one typed
read plus two chunk reads). Unique omitted-content bytes are 116/148/133/117 for Axonius/Falcon/S3/
current respectively; returned content totals are 232/296/266/234 because it intentionally reads
each record twice. These are probe observations, not observed cognitive needs. Client additional
reads, tokens, cost and elapsed delivery remain unknown. Shorter selected content is not called
success; the S3 package remains large and explicitly exposes that cost.

## Reproduction and limits

All builds and test fixtures live outside the checkout:

```sh
dotnet test AILedger.sln --settings /tmp/ailedger-task11/serial.runsettings --artifacts-path /tmp/ailedger-task11/verified -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false --verbosity minimal
UseSharedCompilation=false sh scripts/test-governed.sh all
python3 tools/FindingsBaseline/verify-fixtures.py
dotnet build tools/FindingsBaseline/FindingsBaseline.csproj --artifacts-path /tmp/ailedger-task11/baseline -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false
dotnet /tmp/ailedger-task11/baseline/bin/FindingsBaseline/debug/FindingsBaseline.dll tests/Fixtures/structured-findings-v1
python3 tools/HandoffProbe/run.py /absolute/path/to/ailedger /tmp/ailedger-task11-examples
```

Runsettings use `<RunSettings><xUnit><MaxParallelThreads>1</MaxParallelThreads><ParallelizeTestCollections>false</ParallelizeTestCollections></xUnit></RunSettings>`.
The repository runner executes actual xUnit tests with its existing external-build procedure.
Original sources are read only for capture; all new package preparation uses disposable copies.
Existing suite fixtures may simulate providers/processes; no billable model episode was launched.

Source snapshots retain recorded product candidate identities, not complete frozen product
repositories. Actual recipient access to those versions and semantic sufficiency remain trial
checks. A declared materiality flag is curator judgment. History/protected-context gaps remain
visible; no retrieval route expands authority. Result validation checks authored report shape,
not the truth of a verdict or authenticity of copied output metadata. No task-12 host enforces
trial time/spend or recovers partial output. The [trial](trial.md) names these limitations.

Implementation verification is complete. The exact clean implementation commit is packed and
installed separately; the installation receipt follows without requiring a reinstall for its
subsequent documentation commit. Client acceptance is pending. Tasks 7–10 client trials remain
pending. Tasks 12–14 are unstarted. No governed development task/events, agent dispatch, global
skills/instructions/configuration change, historical migration, merge or publication occurred.

External final log hashes (scratch paths may expire):

- `full-final.log`: `6912ff0d481eb383a97e1afd6c72e0a6a0befa52e56609af25c4f9c18c0cab91`.
- `runner.log`: `da29420862ee27f2906b3bc634204ee9e4e2035d910a21ba522c415a9755a02d`.
- `focused.log`: `d81de857405e9de7038d35108ddbefeadff1b20b43766369fee02f621f280811`.
- `baseline.log`: `1549f160669268c1f6416f029c065ffd1783ad2cd122e8a576e5c195963ba56f`.
- `probe.log`: `9331b75951ab72e70f05b8f1f809ddc324113f93f308801c16ee703a6b7faa22`.
