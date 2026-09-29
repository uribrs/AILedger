# Task-13 installation checkpoint

Historical implementation receipt. The subsequent guidance/help refresh is installed as
**2.0.185 from 907280b**; see its [verification and receipt](../interaction-guidance-refresh.md).

Installed **2.0.180  from d338d3b** from implementation commit
`d338d3b17ec1f2ba43e12823714293b7397070d1` on `codex/structured-findings-contract`.
The separate documentation commit does not change the runtime and was not reinstalled.
Tasks 7–13 client trials remain pending. Task 14 is unstarted.

The [machine-readable receipt](installation.json) records exact pack/install commands, all
11 payload hashes, executable identity, probe commands/log hashes and rollback.

| Identity | Verified value |
|---|---|
| Package SHA-256 | `e20f3e0732ea338e1cf0d4ae20109f742dde05f00672df7d3af98af13c90a722` |
| Installed package | `/Users/user/.dotnet/tools/.store/ailedger.cli/2.0.180/ailedger.cli/2.0.180/ailedger.cli.2.0.180.nupkg` |
| Executable | `/Users/user/.local/bin/ailedger` |
| Executable target | `/Users/user/.dotnet/tools/ailedger` |
| Executable SHA-256 | `762ecf17871189bcf37cb61df6e5ffec560815bfb56633271e814ead60ca3bb7` |

Packed the clean implementation commit externally. Verified every candidate tool payload against
the package, then passed the task-13 assurance, task-10 inspection, task-12 execution/recovery and
task-8 artifact probes. Installed exactly that package, verified the cached package and every installed
payload, and repeated all four probe groups against `/Users/user/.local/bin/ailedger`.
No real/billable model or provider episode ran. These are mechanical results, not client acceptance.

The prior `2.0.178  from 142bab5` package is preserved at
`/Users/user/.local/share/ailedger/rollback/ailedger.cli.2.0.178.nupkg` with SHA-256 `7177ea11e95132cffdc5a7558c226b765d77772725b05922cc223f19e8a72b24`.
Rollback, if required, uses the preserved local package only:

```sh
dotnet tool update --global AILedger.Cli --version 2.0.178 --allow-downgrade --source /Users/user/.local/share/ailedger/rollback
ailedger version
```

Rollback has been prepared, not executed. Existing history and frozen measurement fixtures were
not migrated. See [validation](validation.md), [retained walkthrough](examples/README.md) and
[checkpoint-F client trial](trial.md).
