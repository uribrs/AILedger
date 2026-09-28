# Task 9 installation receipt

Installed and verified **2.0.172  from 6ebb55a** on 2026-09-29, from verified
source commit `6ebb55a7d1403fc66f677be68676a46ac6937336`. This documentation-only follow-up does not
require reinstalling; the installed implementation identity stays `2.0.172 from 6ebb55a`.

- Previous identity: `2.0.170  from 540d6e6`.
- Package SHA-256: `647327dfccb34a8777e0cb31b60202d80eb2f0a1f4fa22f7690d3f7e8bf42027`.
- Verified package: `/tmp/ailedger-task9/packages/AILedger.Cli.2.0.172.nupkg`; identical to `/Users/user/.dotnet/tools/.store/ailedger.cli/2.0.172/ailedger.cli/2.0.172/ailedger.cli.2.0.172.nupkg`.
- Executable: `/Users/user/.local/bin/ailedger` → `/Users/user/.dotnet/tools/ailedger`.
- Executable SHA-256: `6fb7a6701c1b9a079e55c28a1f280e9ddbe43de8c831aa48bf28121de1673840`.
- All 10 managed package payload files matched installed bytes.
- Durable rollback package: `/Users/user/.local/share/ailedger/rollback/ailedger.cli.2.0.170.nupkg`.
- Rollback SHA-256: `2a806f7f30182c22cb275f7bdda45d0fea72dd0f18ea9b7a8dd30a06edbe4cc3`.

The exact commit was packed externally in Release configuration, installed separately
under `/tmp/ailedger-task9/candidate`, then tested through its actual executable before
the global tool was updated. Candidate and global runs of both the claim-dispositions
probe and existing task-8 artifact probe passed. All mutations used new disposable
fixtures; no provider/model launch occurred. The complete commands, build identity,
fixture paths, checks and log hashes are in the [JSON receipt](task-9-installation.json).

The [validation record](task-9-validation.md) contains the full suite, repository runner,
frozen historical hash/report/cost results and failure history. The [contract](../structured-claim-dispositions-v1.md)
explains authority, scope, atomicity, receipts, recovery and remaining CLI work.

**Implementation:** verified and committed. **Installation:** verified.
**Client observation/acceptance:** pending Uri's ordinary task for tasks 7–9.
Tasks 1–6 remain accepted. No work on tasks 10–14 was started.

Tomorrow's task should exercise several already-reasoned judgments by an actor with
ResolveClaim authority. Inspect the rationale and cited evidence for judgment quality,
receipt status/dependency changes for fidelity, retry/refusal recovery, remaining CLI
friction and once-per-run usage. A mechanically accepted judgment is not proof of truth.
No scripted package check substitutes for this client observation.
