# Task 8 validation — coherent artifact submission

Date: 2026-09-28. Scope is task 8 only, starting from clean `798f5a5` on
`codex/structured-findings-contract` in the requested worktree. The later task-7
implementation and installation commits were inspected and preserved. Tasks 1–6
remain accepted. Task 7's client trial remains pending.

## Delivered behavior

The [v1 contract](../structured-artifacts-v1.md) supports one existing VerifierOutput
or CodeReviewOutput, including candidate-bound bundles. Inline content, metadata,
generated identity and registration commit in one canonical event group with a durable
receipt. No external content-storage write needs reconciliation. Trusted Codex/Claude
endpoints call the application service directly. Existing recording capabilities,
stage/producer/document/scope/supersession rules and assurance semantics are retained.
Recording proves neither approval nor task acceptance. No domain event or historical
assignment migration was introduced.

Artifact attempt journals and the opt-in retrospective section reuse existing
measurement joins and preserve once-per-run usage and missing-data boundaries.
Findings-v1 and alternatives-v1 schemas, historical logs, default reports and cost
populations are preserved. Unsupported kinds and remaining CLI work are explicit in
the contract; tasks 9–14 are unstarted.

## Verification coverage

55 new cases exercise verbatim Unicode/quoted/multiline content, expected digest and
byte-size bounds, host identity/scope, unsupported kinds, producer ownership and
capability revocation, unchanged work/run state, document prerequisites, legacy
revision chains, candidate/member replacement links and review pairing. They also
cover concurrent identical requests, conflict, receipt replay after completion and
supersession, torn final writes, failed flushes, cancellation, lost responses,
projection/telemetry failures, null or damaged metadata/content, duplicate receipt
keys, capacity, operation-key isolation, separate measurement populations and
read-only report extension.

Codex and Claude relay cases use real local IPC with scripted clients, not model
episodes. Existing provider configuration cases assert three exact grants with no
wildcard and guidance for the new operation. The existing completion-failure service
decorator forwards the new interface so its prior recovery assertions still exercise
the intended failure.

`tools/ArtifactSubmissionProbe/run.py` is a non-billable package check. It opens a new
disposable fixture, uses the actual executable's MCP server, submits both kinds,
checks content/hash/attribution, replays after restart and run completion, tests
conflict and capability revocation, and inspects measurement. It never launches a
provider. Its development-executable check passed; candidate/global installation
checks are recorded separately after packing the verified commit.

## Results

| Check | Result |
|---|---|
| Final full standard `dotnet test AILedger.sln` | 2,050 main + 99 Memory passed; zero failures/skips |
| Final repository full runner | 2,050 main + 99 Memory passed; zero failures/skips/runner errors |
| Frozen fixture hashes | All 11 unchanged before and after implementation |
| Read-only historical reports | Four report cases matched |
| Frozen provider costs | Four samples matched |
| Builds | Zero compiler warnings/errors |
| Non-billable development executable check | Both kinds and recovery/identity checks passed |
| Git whitespace | Clean |

All builds use external artifact paths. No baseline capture mode or expected-output
recapture was used. Implementation verification is complete; installation and client
trial remain separate checkpoints.

Earlier runs are retained as evidence rather than relabeled:

- Initial build passed with zero compiler warnings/errors.
- The first targeted run had 28 passes and 26 failures: the historical helper left the
  fixture at Discovery, so production stage admission correctly rejected output before
  recovery tests could reach storage. The fixture now records explicit disposable stage
  setup through the real handler. Two new response error codes were added to the new
  schema. No production governance rule was weakened.
- Subsequent focused runs exposed test-only comparisons of separately replayed collection
  instances and escaped JSON spellings, plus an incorrect assumption that the fixture
  had only one run. These now compare content/JSON meaning and the actual run population.
  One added report test initially used the wrong CLI constructor and was corrected to
  the established dependency-injected fixture constructor.
- The focused operation/provider run passed 58 cases. The first full standard run passed
  2,047 main and 99 Memory cases, with no failures/skips.
- Final review added null-artifact/provenance and missing-identity guards to the new
  receipt parser, plus three regression cases. All 15 corruption cases passed. Required
  complete checks were repeated against this final source.
- The initial package-check script reused a JSON-RPC ID before its earlier observation
  finished. It now gives every transport call a unique ID while preserving operation
  retry keys; the rerun passed. This was a scripted-client error, not a client trial.

## Reproduction

```sh
dotnet test AILedger.sln --artifacts-path /tmp/ailedger-task8/build -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false --verbosity minimal --logger trx --results-directory /tmp/ailedger-task8/results-final
UseSharedCompilation=false sh scripts/test-governed.sh all
python3 tools/FindingsBaseline/verify-fixtures.py
dotnet build tools/FindingsBaseline/FindingsBaseline.csproj --artifacts-path /tmp/ailedger-task8/baseline -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false
dotnet /tmp/ailedger-task8/baseline/bin/FindingsBaseline/debug/FindingsBaseline.dll tests/Fixtures/structured-findings-v1
python3 tools/ArtifactSubmissionProbe/run.py /absolute/path/to/ailedger /absolute/path/to/cognitive
```

Temporary build/test/package logs live under `/tmp/ailedger-task8/` and may expire.
The installation record retains package, source and log identities.

## Installation and acceptance boundary

Initial installed identity: **2.0.168 from f966952**. Its original package was preserved
before editing at `/tmp/ailedger-task8/rollback/ailedger.cli.2.0.168.nupkg`, SHA-256
`1f92c5becb3c87b798b6e24cf0e923b9d2c2000c597c359213ecccb7899d3d21`.
The user authorized installation of the scoped verified commit. The separate
[installation receipt](task-8-installation.md) records **2.0.170 from 540d6e6**, its successful
non-billable package checks and the durable rollback copy. Client acceptance remains pending.

No ai-kernel, governed development records, agent dispatch, global instruction/skill
changes, merge, publication, historical-task migration, or billable episode occurred.
All runtime mutations in checks target disposable fixtures. Uri's ordinary-task trial
and inspection of actual content, receipts, friction, retries and usage remain pending.

Final retained log identities (SHA-256):

- `/tmp/ailedger-task8/full-test-final.log`: `e9d3358d6991d54e9db540d86b91c8533dcc2b7a467a4d9c9439ee85b54ba9d2`.
- `/tmp/ailedger-task8/runner.log`: `d245297d0790f3ec6390763f1e33a7d14d83c5487e529c3125ab7ec6978d82f8`.
- `/tmp/ailedger-task8/baseline-build-final.log`: `9a5cf5253eecd9ffc5f05a3c94cdf7980753183e01d423b3850feca68dbdad9a`.
- `/tmp/ailedger-task8/baseline-final.log`: `1549f160669268c1f6416f029c065ffd1783ad2cd122e8a576e5c195963ba56f`.
- `/tmp/ailedger-task8/hashes-final.log`: `84a31c5bca17ff72e93c03ddbbfb267b541278a52bc89eb5679f9fe861046b73`.
- `/tmp/ailedger-task8/development-smoke-2.log`: `d583c3332070a94356ba7ad5cd0e564fe546ab31bf8eae3c7e83c0704866bc00`.

Runner artifacts: /private/var/folders/v3/pncbbv7x45jgp4bnd81fjnl80000gn/T/ailedger-tests.nugxCT
