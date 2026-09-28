# Task 7 validation — structured alternatives

Date: 2026-09-28. Scope: the respecified task 7 only. Starting checkpoint:
`c5bc88b`, on `codex/structured-findings-contract`; the requested worktree was clean.
Tasks 1–6 and their accepted evidence were preserved. No governed development records,
agent dispatch, global instruction/skill changes, historical task migration, merge,
publication or billable episode were performed.

## Delivered boundary

See the [contract](../structured-alternatives-v1.md) and its request/response schemas.
The new typed service calls the existing handler on candidate state under the existing
mutation lease, then uses the canonical group append/flush path. Local keys map to
host-generated alternative IDs in a durable receipt. Existing decision/lesson rules
and outer-only whitespace normalization remain. Unknown outcomes, conflicts and
current-grant receipt authorization follow the findings-v1 recovery semantics.

Codex and Claude receive the exact additional tool grant through the trusted relay.
Both ordinary and assurance briefings supply direct alternatives recording guidance.
Only new default researcher/worker/verifier/reviewer assignments gain RecordAlternative.
Historical assignments require an explicit operator reassignment; neither command
admission nor replay gains a role shortcut or a new historical prerequisite. Existing
findings schemas and canonical receipt behavior are unchanged.

The shared measurement join and bounded journal reader retain original findings
semantics. Alternatives have separate canonical receipts/journals and an opt-in report.
Run ownership is checked before reporting their receipts as canonical commitments.
Default reports and cost populations remain unchanged. Missing observations and
unsupported boundaries are stated in the contract rather than counted as zero.

## Behavioral coverage

94 added cases cover strict typed/wire bounds and Unicode, normalization and canonical
bytes, exact multiline/quoted text, generated IDs, atomic late-reference/lesson refusal,
concurrent writes/retries, operation-key separation, restart/replay, archive retries,
capacity, cancellation, torn tails, failed writes/flushes, receipt corruption, projection
and telemetry failures, all author defaults, actual grants, explicit upgrades/revocations,
no added approval powers, run/cause identity, spoofed input and trusted relay reconnects.

Provider configuration tests retain navigation/resume variants and assert both exact
tool grants with no wildcard. Relay tests run with both Codex and Claude identities,
using real local IPC, storage and the kernel, with scripted provider behavior. No claim
of a successful real-client episode is made. The existing completion-failure service
decorator forwards the new recorder interface so its original completion-recovery
assertions still test the intended boundary.

Measurement cases include missing/truncated/corrupt journals, failed collectors and
lost responses, one canonical commit across retries, once-per-run usage/session joins,
a forged nonexistent run, and a read-only CLI comparison proving the new opt-in section
leaves all default report fields unchanged. Findings event/call counts exclude
recognized alternatives; unidentifiable shared protocol frames remain unclassified.

## Actual validation

| Check | Result |
|---|---|
| Final standard external `dotnet test AILedger.sln` | 1,995 main + 99 Memory passed; zero failures/skips |
| Final repository full runner | 1,995 main + 99 Memory passed; zero failures/skips/runner errors |
| Frozen fixture hashes | 11 unchanged before and after implementation |
| Read-only historical probe | Four report cases and four provider-cost samples matched |
| Builds | Zero compiler warnings/errors |
| Git whitespace | Clean |

Earlier results are retained, not relabeled as passing:

- Initial test compilation needed a missing `System.Text` import in the new measurement
  fixture. Production solution compilation had already passed.
- The first targeted run passed 301 and failed one recalled-lesson fixture. The live
  service reloads recalled lessons from its store; the fixture now constructs a genuine
  historical opening through the real handler, as the established findings test does.
  No lesson rule was weakened.
- The next targeted run passed 315 and failed the new report CLI test, revealing the
  missing boolean flag registration. That production omission was fixed. The next
  targeted run passed all 317 cases; the additional forged-run case passed in full runs.
- The first full standard run passed 1,992 main cases and failed two existing findings
  telemetry assertions (one missing application row and one collection-failure stderr
  diagnostic); all 99 Memory cases passed. The new disk-heavy fixture classes now use
  one nonparallel collection to avoid adding unrelated I/O contention to the 250 ms
  best-effort collectors. Production deadlines and all assertions are unchanged;
  explicit concurrent-writer tests remain concurrent. Final standard and runner results
  are recorded separately above. The underlying collectors remain best effort, not a
  guarantee that every attempt can be observed under arbitrary host load.

Final review also removed duplicate envelope parsing from historical reads: one shared
metadata inspection feeds the two typed validators. This is not an arbitrary-command
framework or a change to domain event shape. A final provider-guidance wording change
clarifies that only a missing capability calls for explicit reassignment; the repository
runner includes that wording. Reference/domain refusals retain their own correction path.

## Reproduction and retained logs

All compilation outputs are external. No baseline capture mode was used.

```sh
dotnet test AILedger.sln --artifacts-path /tmp/ailedger-task7-standard -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false --verbosity minimal --logger trx --results-directory /tmp/ailedger-task7-results-final
UseSharedCompilation=false sh scripts/test-governed.sh all
python3 tools/FindingsBaseline/verify-fixtures.py
dotnet build tools/FindingsBaseline/FindingsBaseline.csproj --artifacts-path /tmp/ailedger-task7-baseline -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false
dotnet /tmp/ailedger-task7-baseline/bin/FindingsBaseline/debug/FindingsBaseline.dll tests/Fixtures/structured-findings-v1
```

Temporary artifacts may expire:

- Initial/full logs: `/tmp/ailedger-task7-standard.log`, `/tmp/ailedger-task7-standard-final.log`.
- Original and final TRX files: `/tmp/ailedger-task7-results/`, `/tmp/ailedger-task7-results-final/`.
- Runner: `/tmp/ailedger-task7-runner.log`; detailed logs/artifacts:
  `/private/var/folders/v3/pncbbv7x45jgp4bnd81fjnl80000gn/T/ailedger-tests.akUnHO/`.
- Final baseline: `/tmp/ailedger-task7-baseline-final.log` and
  `/tmp/ailedger-task7-baseline-build-final.log`.

## Installation and client acceptance

Implementation verification is complete when the final checks above pass. The user's
request separately authorizes installing the exact scoped commit after verification.
The initially installed identity was **2.0.163 from cd3f2d9**. Its preserved package is
`/tmp/ailedger-task7-installation/rollback/ailedger.cli.2.0.163.nupkg`, SHA-256
`450d7e9d44cff18c2e14ad3262aa049424becc9a471d72f480f9fc4b34ba02ca`.

Installation is recorded in a separate follow-up receipt after the clean commit has
been externally packed and the package checked. It does not count as client-trial
acceptance. Uri's ordinary-task trial and its actual text/provenance, friction and
usage review remain pending. Tasks 8–14 remain unstarted.

Validation log SHA-256 values:

- Initial standard: `c9d658700aa01a662eef86d8ae399cfdc50ac8bad3b5fd72e49d3867e7f8390b`.
- Final standard: `abd9c0a051f0b8e8e96dc806405c852888f60636e7c14e40e8a37279d3f91275`.
- Full runner: `7bf77987d92b2cec11e327bb36a1c3c89b9d7703daaf4817d3e67358d03a3ad1`.
