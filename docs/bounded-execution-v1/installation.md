# Task-12 installation checkpoint

Installed **2.0.178  from 142bab5** from implementation commit
`142bab5126e6e3e02b7aaba619b0532dcea2f421` on branch `codex/structured-findings-contract`.
The subsequent receipt/example documentation commit does not change the installed runtime.
Implementation, installation and client acceptance are separate checkpoints; tasks 7–12 client
trials remain pending. Task 13 is untouched.

The [machine-readable receipt](installation.json) preserves exact commands, all 11 payload hashes,
candidate and installed probe results, prior identity and verification log hashes.

| Identity | Verified value |
|---|---|
| Package SHA-256 | `7177ea11e95132cffdc5a7558c226b765d77772725b05922cc223f19e8a72b24` |
| Installed package | `/Users/user/.dotnet/tools/.store/ailedger.cli/2.0.178/ailedger.cli/2.0.178/ailedger.cli.2.0.178.nupkg` |
| Executable | `/Users/user/.local/bin/ailedger` |
| Executable target | `/Users/user/.dotnet/tools/ailedger` |
| Executable SHA-256 | `8db2262db3d2ff4e16e852526774aa64c85182b90bbbd133a014bcfa354990c6` |

Packed the clean implementation commit outside the checkout. Installed the candidate into an
external tool directory, compared its 11 payload files with the package, and passed all seven
scripted execution/recovery cases plus the existing task-8 artifact and task-10 inspection probes.
Installed that same package globally, then repeated payload/package/executable identity checks
and all three probe groups against the installed executable. No real/billable provider ran.

The [validation record](validation.md) reports 2,236 main and 99 Memory tests passing in both the
full .NET suite and final isolated repository runner; frozen hashes/reports/costs remained unchanged.
It also records the earlier overlapping-run test failure and successful isolated reruns.
The [retained recovery example](examples/recovery.md) preserves actual interrupted, reconciled and
resumed inspections. See the [client trial](trial.md) for precise reproduction and remaining limits.

## Preserved rollback

Prior installed identity: **2.0.176  from 4a0f117**. Its original package was copied before
installation to `/Users/user/.local/share/ailedger/rollback/ailedger.cli.2.0.176.nupkg` and verified with SHA-256
`e2ea2ea4bd0d3b6d9f88e13e38f0adafa6d3da314c76e2036cd61a613cc31c9b`. To restore that exact prior package if needed:

```sh
dotnet tool update --global AILedger.Cli --version 2.0.176 --allow-downgrade \
  --add-source /Users/user/.local/share/ailedger/rollback --ignore-failed-sources
/Users/user/.local/bin/ailedger version
```

Expected restored identity: `2.0.176  from 4a0f117`. Rollback has been preserved, not performed.
The runtime is deliberately limited to macOS offline read-only episodes; successful process and
authored output do not provide independent assurance for implementation changes or acceptance.
