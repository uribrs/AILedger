# Task 11 installation — verified, client trial pending

Installed **2.0.176  from 4a0f117** from clean implementation commit
`4a0f11708cd48dcc657d45f8dd806d88555eca59` on `codex/structured-findings-contract`.
The [complete receipt](installation.json) retains commands, source/package/executable identities,
payload hashes, candidate/installed probe results and verification-log hashes.

- Package SHA-256: `e2ea2ea4bd0d3b6d9f88e13e38f0adafa6d3da314c76e2036cd61a613cc31c9b`.
- Installed package: `/Users/user/.dotnet/tools/.store/ailedger.cli/2.0.176/ailedger.cli/2.0.176/ailedger.cli.2.0.176.nupkg`.
- All **11** packaged tools payload files matched the installed bytes.
- Executable: `/Users/user/.local/bin/ailedger`, resolving to `/Users/user/.dotnet/tools/ailedger`.
- Executable SHA-256: `9afe9e608cd2a19cb3512c3f86fe5d6262bc5242d9dfbcc848ea2578294a57d7`.
- Previous CLI: `2.0.174 from dbeafbd`.
- Durable rollback: `/Users/user/.local/share/ailedger/rollback/ailedger.cli.2.0.174.nupkg`.
- Rollback SHA-256: `e27e22e3abc9b54e4d620445fc0a0b1eb797952411e759c059a7e7816c8f5877`.

The package was built externally from the exact clean verified commit, installed into a separate
candidate tool path, and passed handoff, task-10 inspection and task-8 artifact probes. The same
package then updated the global tool and passed fresh disposable probes through the installed
executable. No model/provider episode was launched. The first candidate artifact probe invocation
omitted its required cognitive-root argument and stopped before executing; correcting that argument
did not change the package. Global update occurred only after all candidate probes passed.

Both full runners had already passed **2,204 main + 99 Memory tests**, with no failures/skips,
and all frozen measurement checks matched. See [validation](validation.md). This documentation
receipt is a separate commit and does not require reinstalling the implementation package.

## Inspectable current-task example

The installed executable prepared this real unfinished cursor re-anchor task snapshot:

- [Readable handoff](examples/current-reanchor-handoff.md).
- [Unchanged JSON envelope](examples/current-reanchor-handoff.json).
- [Installed preparation measurements for all four cases](examples/measurements.json).

Current example package SHA-256: `0749843dae5e19226fd672c75a1ed3679f3b72a7e7e36bbc51dfa75f0057880b`; **104,719 bytes**.
It retains **25/28 records**, omits **3** with versioned retrieval
paths, and includes **1** pinned source-task dependency excerpt. The package retains
accepted D1, proposed RPD1/RPD2 and open C1 distinctly. The original task log is pinned at version 39;
the dependency source at version 404. Original histories were read-only and their hashes remained
unchanged. The snapshot is not a claim that live state will remain unchanged.

This retained example points to a disposable snapshot root that may expire. Regenerate through the
[trial instructions](trial.md) if it is unavailable. Do not relabel the old envelope's ledger identity;
new root/time observations produce a new package digest. Frozen source files and curation remain
committed and inspectable. Historical product repository bytes are not bundled; actual candidate
availability is an explicit prerequisite for any later behavioral verification.

## Rollback and acceptance

To restore the preserved pre-task-11 tool (which keeps task 10 but lacks these handoff commands):

```sh
dotnet tool update --global AILedger.Cli --version 2.0.174 --allow-downgrade --add-source /Users/user/.local/share/ailedger/rollback --ignore-failed-sources
ailedger version
```

Implementation and installation are verified. **Task-11 client acceptance is pending.**
Tasks 7–10 ordinary-client trials also remain pending. The historical simulations and frozen
handoff loss tests demonstrate mechanical preservation, not improved cognition, delivery time or
cost. No task-12 host, dispatcher, automatic follow-ups, task-13 assurance change, global skill or
configuration change, migration, merge or publication occurred. Stop at task 11.
