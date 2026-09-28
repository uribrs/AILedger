# Task 4 validation: provider adoption

Date: 2026-09-28. Worktree: `/Users/user/.codex/worktrees/structured-findings-contract/AILedger`.
Branch: `codex/structured-findings-contract`. Base: task 3, `57f9dcb`.

Task 4 is implemented and both real provider paths met the disposable recording acceptance.
See [host ownership and limitations](task-4-provider-adoption.md). Development did not invoke the
kernel workflow, open a governed development task, dispatch development agents, install the global
tool, merge or publish. Ledger mutations and provider launches below belong only to isolated
acceptance fixtures. All builds are outside the checkout.

## Real provider acceptance

The externally built CLI's production `provider launch` ran both clients with their normal adapter
permissions and sandbox settings. No mocked provider configuration stands in for these results.
The operator dispatched a researcher with BuildContext, AddClaim and AddEvidence, through a real
manifest/run. The agent wrote a spoofed host.json in its writable workspace, then submitted ordinary
findings/evidence prose with quotes, Hebrew, emoji, newlines, `rg`, `Grep`, C# terms, `$()`, pipes and
backticks. It retried the exact body/key. The trusted probe waited for that replay, revoked
AddEvidence through the disposable ledger's operator, and signalled the agent to submit another
batch. Both providers received `kernel_refused` at `evidence[0]`, with no claim prefix.

| Observed | Claude | Codex |
|---|---|---|
| Client | 2.1.283 | 0.155.0-alpha.9.2 |
| Actual session | `89f132d5-b3cf-4feb-8988-a1cb23251069` | `01a0e81c-06b3-7432-ae47-3201b3ec6ef3` |
| Receipt transaction | `d7185b311ce34862992d61c8a3941f42` | `69d6251bd101469cba3ae423e9cd04ab` |
| Tool outcomes | commit, identical replay, kernel refusal | commit, identical replay, kernel refusal |
| Canonical findings events | one claim + one evidence | one claim + one evidence |
| Actor / run correlation | researcher / R1 | researcher / R1 |
| Connection session observation | null | null |
| Provider completion records | 1 | 1 |
| Turns | 8 | absent |
| Output tokens | 2,256 | 729 |
| Uncached / cache-write / cache-read input | 14 / 54,994 / 318,958 | 20,829 / 0 / 164,224 |
| First ledger write | 33,882 ms | 18,899 ms |
| Manifest artifacts | 8 | 8 |
| Launch outcome / timeout / truncated lines | completed / false / 0 | completed / false / 0 |

Each canonical receipt retains the original version 9, commit timestamp, event IDs and mappings.
The two prose fields match the prepared payload exactly. Canonical state contains no denied prefix
or duplicate findings, and terminal usage matches the one run-completed event without multiplication
by the three tool calls or two granular events. The model's narrative is not the evidence: Claude
called its two successful responses “two commits”; the canonical log proves one commit plus replay.

Both real clients completed MCP initialization/discovery and used the frozen structured response
successfully; interoperability is demonstrated by actual recording/replay/refusal. The current
transport journal does not retain successful initialize frames or the selected protocol-version
string, so the exact negotiated version is **unobserved**, not guessed from client version. Claude
also produced one protocol-rejected observation without application entry; the journal does not
identify that method. It did not prevent discovery or any of the three calls. This is not a claim of
support for every MCP method. No task-3 protocol/parsing rule was relaxed.

Claude's terminal stream records one unrelated Bash permission denial while polling the marker;
it then read the already-created file with its file tool. No findings call required shell permission
or search-hook fallback, and no permission was widened to avoid that denial. Both clients accepted
the exact configured MCP grant. Roslyn was present in these production launch configurations;
independence with navigation absent is covered by real adapter-argument tests, not asserted from
these episodes alone.

The first fixture attempts failed admission before provider execution because the test actor's
explicit capability list omitted BuildContext. Fresh fixtures added that normal briefing capability;
this was a test setup correction, not a kernel waiver or policy change. Stage prerequisites were
explicitly waived only in the disposable acceptance fixtures, as in existing integration tests.

The live trials used external builds of the task-4 working tree before its commit. The existing
assembly stamp therefore names base `57f9dcb`; it is not evidence that those binaries contain only
task-3 source. No build-stamp semantics were changed in this task.

Artifacts (temporary; they may expire): `/tmp/ailedger-task4-live-{claude,codex}-2/` contains the
canonical ledger, transport/refusal/application journals, provider result sidecar, launch logs and
acceptance.json. Earlier failed admissions remain in the corresponding directories without `-2`.
The reproducible [probe](../../tools/FindingsProviderProbe/README.md) includes canonical/observation
assertions; its verification logic was also run read-only over these completed episode logs (apart
from writing acceptance.json). SHA-256 of the final canonical logs:

- Claude: `20137de635e2e9689b34d832564634789f271aecb428a8c76695f010a477484d`.
- Codex: `364c83581206403530d8aa382558835523e2af007b40c31dd84182077b3d25d8`.

## Automated coverage and results

Nine new cases cover real host/relay authentication, inability to bind the occupied listener,
file spoofing, subject/run/cause attribution, reconnect receipt recovery, revoked host grants,
revoked kernel capability, identity fields rejected by the strict parser, exact provider tool grants
with navigation present/absent and resume, and production-launch manifest/usage/first-write capture.
The model output is scripted in automated tests; the MCP relay, recorder, kernel and storage are real.
The original task-2/task-3 findings tests remain in place. Existing Claude navigation tests now parse
the inline JSON configuration. The completion-failure service decorator forwards IFindingsRecorder
so its existing assertions still exercise completion retry/stranding, rather than failing early at
endpoint composition. None of those assertions was weakened.

- Focused `Findings` filter: **190 passed, 0 failed, 0 skipped, 0 runner errors** (includes the original
  168 findings cases, nine new cases and other existing tests whose names contain Findings).
- Final repository-aware full suite: **main 1,867 passed; Memory 99 passed; zero failures, skips or runner errors**.
  Detailed logs: `/private/var/folders/v3/pncbbv7x45jgp4bnd81fjnl80000gn/T/ailedger-tests.xsgQXv`.
  The existing timing-sensitive R8 fixture passed on this run and the final standard run; its race is unchanged.
- Final standard `dotnet test`: **main 1,625 passed / 242 failed; Memory 97 passed / 2 failed**.
  This is a failed run. Its named failure set is task 3's existing external-output discovery failures
  plus the new production-launch findings test, which likewise cannot locate the cognitive root.
  The repository-aware run supplies that context; no original task-2/task-3 findings case failed.
- Frozen hash checker: **all 11 hashes unchanged** before and after production implementation.
- Read-only report probe: **four historical report cases and four provider-cost samples matched**.
- Builds outside the checkout: **zero warnings/errors** on successful builds.
- `git diff --check`: clean.

An initial new test omitted StartRunCommand's required nullable session argument; it was corrected.
An initial configuration assertion searched for the word `ailedger` anywhere in a file and matched
a build path; it now checks the specific findings MCP section. The first full run identified four
completion-failure wrapper cases missing IFindingsRecorder forwarding; that test decorator was
updated as described above. These were failures and are not counted as passing runs.

Reproduction:

```sh
UseSharedCompilation=false sh scripts/test-governed.sh all
dotnet test AILedger.sln --artifacts-path /tmp/ailedger-task4-standard-tests -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false --verbosity minimal
python3 tools/FindingsBaseline/verify-fixtures.py
dotnet build tools/FindingsBaseline/FindingsBaseline.csproj --artifacts-path /tmp/ailedger-task4-baseline -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false
dotnet /tmp/ailedger-task4-baseline/bin/FindingsBaseline/debug/FindingsBaseline.dll tests/Fixtures/structured-findings-v1
```

Final logs: `/tmp/ailedger-task4-full-final.log`, `/tmp/ailedger-task4-standard-final.log`,
`/tmp/ailedger-task4-focused.log`, `/tmp/ailedger-task4-baseline.log`.

## Remaining limits

- Session identity is honestly missing in immutable transport bindings; actual sessions remain in
  provider run records. Task 5 must join these without manufacturing observations or usage.
- Loopback IPC must be available to the trusted launcher and provider MCP subprocess. It is not an
  HTTP MCP endpoint and is not an OS-level sandbox against direct ledger/host process authority.
- The standalone file host retains its trusted-configuration responsibility. Provider composition
  uses no such file. Existing CLI authorization/validation is preserved, not replaced with new OS ACLs.
- Task-2 durability remains process-crash/retry recovery, not power-loss/distributed durability.
  General storage readers/writers must be task 2 or newer; task-1 storage rejects singleton markers.
- Existing standard VSTest external-output repository discovery failures and the timing-sensitive
  `ReconsiderationConsultantTests.R8_ConsultationRequiresARecordedSubjectRole` fixture remain visible.
- No report integration, performance conclusion, YAML, broader batch command, workflow retirement,
  or task 5–8 implementation is included. The next assignment is [task 5](../handoffs/structured-findings-task-5.md).
