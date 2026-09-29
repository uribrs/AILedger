# Task 10 installation — verified, client trial pending

Installed **2.0.174  from dbeafbd** from scoped source commit
`dbeafbdf830215ff3e5135ee19e22dc35f8af22b` on `codex/structured-findings-contract`.
The complete [installation receipt](task-10-installation.json) records commands and hashes;
[validation](task-10-validation.md) records the full test and frozen-baseline checks.

The exact clean commit was packed externally, installed into a separate candidate tool path,
and exercised with fresh disposable inspection, disposition and artifact probes. That same
package then updated the global tool. All three probes passed again through the installed
executable; no provider/model launch occurred.

- Package SHA-256: `e27e22e3abc9b54e4d620445fc0a0b1eb797952411e759c059a7e7816c8f5877`.
- Verified installed package: `/Users/user/.dotnet/tools/.store/ailedger.cli/2.0.174/ailedger.cli/2.0.174/ailedger.cli.2.0.174.nupkg`.
- Every packaged tools payload matched the installation byte-for-byte (11 files).
- Executable: `/Users/user/.local/bin/ailedger`, resolving to `/Users/user/.dotnet/tools/ailedger`.
- Executable SHA-256: `686fe72305c58146170f3cba0cbdeb89e426422d5a5e506b8ebecbf987bb9c87`.
- Previous installed identity: `2.0.172  from 6ebb55a`.
- Durable rollback: `/Users/user/.local/share/ailedger/rollback/ailedger.cli.2.0.172.nupkg`.
- Rollback SHA-256: `647327dfccb34a8777e0cb31b60202d80eb2f0a1f4fa22f7690d3f7e8bf42027`.

Rollback restores the prior four recording operations but does not provide task-10 inspection.
No historical tasks or recorded assignments were migrated. The documentation receipt is a
separate commit; it does not require reinstalling the already verified implementation package.

Implementation and installation are verified. **Tasks 7–10 ordinary-client trials remain
pending; none is claimed accepted.** Tasks 1–6 retain their prior acceptance. Tasks 11–14
were not started. The historical task-9 simulation remains a mechanical comparison only.

For the next ordinary task, use the [interaction coverage table](../structured-inspection-v1.md)
to inspect actual retrieval/omission behavior, stale and unknown results, useful blockers,
mutation receipts and every residual CLI step. Check physical assurance-candidate inspection
separately: this endpoint deliberately returns unknown when it has only a recorded identity.
Record provider usage once per actual run. Do not infer better safeguards, judgment, cost or
elapsed delivery from fewer mutation refusals.
