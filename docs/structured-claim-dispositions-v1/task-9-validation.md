# Task 9 validation — explicit claim dispositions

Date: 2026-09-29. Task 9 only, directly in the requested worktree on
`codex/structured-findings-contract`. Initial status was clean at `ed67325`; there
were no subsequent commits. Tasks 1–8 and their installation/acceptance records
were preserved. Uri explicitly authorized proceeding before tasks 7–8 client trials.

## Delivered and tested

The [contract](../structured-claim-dispositions-v1.md) describes 1–32 atomic explicit
claim validations/rejections with expected status, durable verbatim rationale and
typed existing evidence references. The application service and trusted Codex/Claude
endpoints retain resolution capability, direction checks, provenance, lifecycle and
dependency consequences. Receipts account for every judgment and dependency event.
Formal decision acceptance remains separate. No recorded assignments change.

97 new cases cover request bounds and strict parsing, authority/run ownership,
revocation, stale state, evidence direction, no-prefix refusal, concurrency, operation
key isolation, capacity, status reversal and evidence accumulation. Dependency cases
exercise proposed/accepted decisions and active/completed/manually blocked work,
unrelated dependents, shared dependencies and unchanged run lifecycle. Historical
rationale-free events still replay; only present new rationale is constrained.

Recovery cases cover partial lines, complete prefixes, absent final newlines, failed
flush, cancellation, response loss, restart, reduced limits, changed content/binding,
duplicate keys, corrupted/omitted/null metadata and projection/telemetry failures.
Measurement cases distinguish attempts, durable requests and events, preserve
missing-data boundaries, join provider completion once per run, isolate operation
populations and prove the opt-in report leaves existing report fields unchanged.
Both providers use real local IPC with scripted clients; no model episode was run.

## Final results

| Check | Result |
|---|---|
| Focused operation/provider cases | 103 passed (97 new + 6 existing provider configuration cases) |
| Full `dotnet test AILedger.sln` | 2,147 main + 99 Memory passed; no failures/skips |
| Repository runner | 2,147 main + 99 Memory passed; no failures/skips/runner errors |
| Frozen hashes | All 11 unchanged, before and after |
| Frozen historical reports | Four cases matched |
| Frozen provider costs | Four samples matched |
| Build and whitespace | No compiler warnings/errors; clean whitespace |
| Final development executable probe | Passed; new disposable fixture, no provider launch |

The earlier full run passed 2,138 main + 99 Memory cases before nine final regression
cases and the corresponding malformed-data guard/immutable receipt checks were added.
The complete required checks above were repeated against the final source. No expected
outputs were recaptured. The three earlier operation contracts/schemas and frozen
fixtures have no diff. All builds and runtime fixtures were outside the checkout.

Earlier failures remain visible in `/tmp/ailedger-task9/`: the initial compile caught
an incorrect report-handler variable; the first focused run passed 65 and failed two
(report flag registration and a test fixture offset). The next compile identified two
incorrect test helper arguments/enum names. After correction, 94 cases passed. Final
review added nine more cases; all 103 passed. The package probe initially used the
wrong event discriminator and an unsupported CLI usage option; it now asserts the
actual discriminator and unmeasured usage rather than inventing token observations.
No kernel authority, replay rule or baseline was weakened to obtain a pass.

## Reproduction

```sh
dotnet test AILedger.sln --artifacts-path /tmp/ailedger-task9/final -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false --verbosity minimal
UseSharedCompilation=false sh scripts/test-governed.sh all
python3 tools/FindingsBaseline/verify-fixtures.py
dotnet build tools/FindingsBaseline/FindingsBaseline.csproj --artifacts-path /tmp/ailedger-task9/baseline-final -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false
dotnet /tmp/ailedger-task9/baseline-final/bin/FindingsBaseline/debug/FindingsBaseline.dll tests/Fixtures/structured-findings-v1
python3 tools/ClaimDispositionsProbe/run.py /absolute/path/to/ailedger
```

`tools/ClaimDispositionsProbe/run.py` tests the actual executable's MCP endpoint in
a fresh fixture: findings stay open, a late direction refusal commits nothing,
judgments preserve rationale/identity, dependency invalidation appears in the receipt,
restart and completed-run retry recover exactly, key conflicts and revoked authority
fail closed, and measurement populations stay separate. Usage is explicitly unmeasured
in that probe; application tests verify nonzero usage once per run. Package and global
installation checks are recorded separately after the scoped commit.

## Installation and client status

Initial installed identity was verified as **2.0.170 from 540d6e6**, package SHA-256
`2a806f7f30182c22cb275f7bdda45d0fea72dd0f18ea9b7a8dd30a06edbe4cc3`.
It was copied to `/tmp/ailedger-task9/rollback/ailedger.cli.2.0.170.nupkg` before changes;
installation preserves a durable rollback copy as well. The exact scoped source
commit is packed externally, tested through a separate candidate tool installation,
then installed under Uri's existing authorization. Verify identity, package hash,
managed package payloads, executable target/hash and fresh disposable probes. A
separate installation receipt records actual results; this validation is not itself
an installation claim. The rollback package predates the new endpoint and cannot
serve task-9 retries; retain the task-9 package for restoring that capability.

Implementation verification is complete. Installation and client acceptance are
separate checkpoints. Tasks 7–9 client trials remain pending; tasks 1–6 remain accepted.
No ai-kernel, governed development records, dispatch, global instruction/skill edits,
merge, publication, historical migration, billable episode or tasks 10–14 occurred.

Final log hashes (temporary paths may expire):

- `/tmp/ailedger-task9/focused-final.log`: `7ae297a0c95fbd2b4f3e5a5a57c7c9b9e935a5c614502809637e399a09b75b71`.
- `/tmp/ailedger-task9/full-test-final.log`: `268af652416de230d4041b144307c65ef5f2e83cf9a5b030f177091d08327329`.
- `/tmp/ailedger-task9/runner-final.log`: `c01e43e85c80f0f1ed99aba51a49e3b3b54c76397edbcbb981b7b504a6700f91`.
- `/tmp/ailedger-task9/baseline-build-final.log`: `b0ef7f6134254c5e4cea7d0b7f93176367e206fdf8b1d46e1c6b6ce960eef800`.
- `/tmp/ailedger-task9/baseline-final.log`: `1549f160669268c1f6416f029c065ffd1783ad2cd122e8a576e5c195963ba56f`.
- `/tmp/ailedger-task9/hashes-final.log`: `84a31c5bca17ff72e93c03ddbbfb267b541278a52bc89eb5679f9fe861046b73`.
- `/tmp/ailedger-task9/development-probe-final.log`: `1fa6b8a0afbd801e8c99c4681ce897bdbad3c83d03778c5de9fe2ecc9e631655`.

Runner output:

```text
Build artifacts and logs: /private/var/folders/v3/pncbbv7x45jgp4bnd81fjnl80000gn/T/ailedger-tests.GYPNg6
AILedger.Memory.Tests: total=99, failed=0, skipped=0, errors=0
AILedger.Tests: total=2147, failed=0, skipped=0, errors=0
```
