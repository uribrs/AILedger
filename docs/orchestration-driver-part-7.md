# Orchestration driver — Part 7: behavioral integration, telemetry and adoption

## Delivered boundary

This candidate extends the existing process runner, adapters, shared dispatch/completion, durable
coordination and retrospective owners. It preserves the Parts 1–6 work in the main checkout on
`codex/orchestration-review`. Development used the user's explicit direct-work exemption: no
new development ledger, governed development dispatch or live-ledger mutation. The user subsequently
authorized committing, pushing and installing the branch; Parts 1–7 shipped as `2.0.191` from
`3a41b22`. The startup correction below follows that installation and the next real launch report.

Implemented behavior and repeatable fixture coverage are distinct from live adoption. The bounded
single-member lifecycle can reach archive after independent verification, blind review, separate
requirements-aware review and explicit acceptance. Authenticated real-provider lifecycle operation
is **not established**: the bounded Claude startup probe below failed before protocol output.
No productivity, human-attention or delivered-quality improvement has been measured.

## The two historical Claude failures

Read-only sources were the event log and coordination journal for
`2026-10-07_1902-orchestration-part7`, plus the protected setup under
`~/.local/share/ailedger/orchestration-part7/2026-10-07_1902`. Candidate replay and retrospective
inspection used copies of the histories under `/tmp/part7-historical-replay`, never edits to the originals.

| Run | Durable observation |
| --- | --- |
| `driver-a3a3c8a17bbe42229218f66c193e3ee4` | Discovery, `claude-lead`, Failed, approximately 18 seconds, ProviderFault / Broken pipe |
| `driver-425374e9c44540f689bbd498d53b83a2` | Findings recovery, `claude-synthesis`, Failed, approximately 17 seconds, ProviderFault / Broken pipe |

Both have recorded start/completion, `retention=notAttempted`, zero cognitive receipts, and no
retained provider result. The second invocation was Findings recovery, not repeated Discovery.
The copied history replays at version 36, including the later diagnostic evidence. There is no
active run recorded, work item or plan. That does **not** demonstrate process-tree termination.
No historical provider PID, actual child exit, stderr or effective process permissions can be
recovered from these records. The second invocation's reported outside-shell-sandbox origin is
not proof of its effective child permissions. Current process-name inspection cannot identify
an original child; no process was terminated or stop evidence invented.

The configured launcher used the installed CLI, with working directory and cognitive root in the
original `~/.codex/worktrees/orchestration-driver/AILedger` checkout. The profiles selected Claude
for Discovery and Findings. Executable resolution walks host PATH unless explicitly configured;
the current `~/.local/bin/claude` resolves to `~/.local/share/claude/versions/2.1.293`, consistent
with the recorded `2.1.293 (Claude Code)` version, but the original resolved path was not retained.
The main checkout is a separate copy and was the only source tree edited here.

The launch path is shared dispatch → `ClaudeAgentAdapter` → `SystemProcessRunner` →
`ProviderProcessIsolation` → `sandbox-exec` → Claude. It uses an argument vector without a shell,
a redirected brief on stdin, concurrent stdout/stderr readers, an allowlisted ambient environment
plus explicit launch values, inline MCP settings and the existing authenticated relay. Claude's
own shell sandbox remains enabled, fail-closed, with no unsandboxed fallback. Operator configuration
and authentication locations remain protected. No authentication contents were printed or changed.
The capability/version probes run before the confined execution and cannot establish that execution,
remote settings, authentication or MCP startup works.

### Established masking defect

A controlled real child writes `startup refused` to stderr and exits 37 while the host writes
4 MiB of input. Before the fix, the regression failed with `IOException: Broken pipe` from
`WriteInputAsync`. The fail-fast handler cancelled readers and rethrew; the adapter did not catch
that exception. Already captured diagnostics never became an `AgentRunResult`, so dispatch closed
the ledger run as Failed with no result retention. This explains **how the useful diagnostic was
lost**, not why the original Claude processes exited.

The runner now gives exit/readers at most one second to finish after an input-write failure,
still respecting caller cancellation and the launch deadline. It then uses the existing bounded
kill/reap/I/O cleanup. A typed observation retains the original input error, observed exit if any,
whether the child had already exited, cleanup completion, output completeness, and separate drain
or cleanup diagnostics. A concurrent output-limit failure remains visible rather than replacing or
being hidden by the input error. Existing stream/retention limits remain in force.

The adapter redacts diagnostics and returns the partial result. Shared dispatch retains it before
closure through its existing owner. Startup, transport, cleanup uncertainty, ledger completion and
result-retention outcomes remain separate. A compact stderr excerpt reaches the driver diagnostic;
the full bounded retained result stays in `runs/<run>.json`. Capability/version failures also retain
a bounded diagnostic in the exception reported by dispatch.

`cleanupConfirmed` means the runner completed its existing child-reap/I/O cleanup; it is **not**
a trusted attestation that every descendant or check stopped. `exitedBeforeCleanup` distinguishes
an observed earlier child exit from an exit after cleanup. Unknown exits remain nullable; the legacy
result exit sentinel `-1` is not a measured exit. Unconfirmed cleanup takes precedence over ordinary
cancellation in driver reporting and remains UnknownOutcome.

### Bounded real-provider observations

Two isolated Claude adapter probes used the built candidate, a temporary workspace, a 20-second
execution deadline and the existing macOS confinement. They had no live task or MCP endpoint and
requested no tool use or file changes. The product sandbox and grants were not weakened.

| Probe | Actual observation | What it establishes |
| --- | --- | --- |
| Inside the desktop shell sandbox | Claude version/help probes passed; confined execution exited 71 with `sandbox-exec: sandbox_apply: Operation not permitted`, no protocol events | Current nested-sandbox setup is incompatible; this does not diagnose the historical launches |
| Host-permitted execution, product confinement unchanged | Claude 2.1.293 exited 1 after **17.86 seconds**, no protocol events; stderr: “Your organization requires remote managed settings to load, but they could not be loaded.” It suggested reauthentication, checking connectivity or contacting the administrator | Actual current startup reached Claude and failed on required remote managed-settings loading; authenticated execution and MCP/governed interaction were not demonstrated |
| Codex version/exec/resume help | `codex-cli 0.155.0-alpha.16.3`; required adapter tokens present, including strict config and sandbox; shell reported inability to create PATH aliases | Capability visibility only; no real Codex model execution, authentication or governed interaction claim |

The managed-settings failure is consistent with the historical 17–18 second timing, but cannot be
proved to be either original child's cause. Its own message does not distinguish authentication,
network connectivity or organization-policy availability. There was no unconfined retry or
credential/grant alteration. Provider token usage and USD cost were absent and remain **unknown**,
not zero. No provider model returned a successful result. No full Part 7 orchestration was launched.

### Follow-up: macOS startup compatibility

The subsequent task `2026-10-07_2126-yaml-falcon-cursor-strategies` retained both failed recon
results: Codex `XRA1` exited 1 during in-process app-server initialization; Claude `XRA2` exited 1
with the mandatory managed-settings error. Both had zero protocol events. The new stderr retention
worked, but that did not make provider startup compatible.

Read-only macOS denial logs for 2026-10-07 21:28:37–21:29:18 UTC identify product confinement
defects, rather than establishing the reported outer shell sandbox as the sole cause:

- Codex was denied writes to its temporary home's `installation_id` and `state_5.sqlite-shm`.
  Hook-trust discovery starts an app-server before execution, creating those runtime files. The
  adapter then incorrectly marked every existing home file read-only. It now captures the generated
  configuration paths before discovery, so runtime state stays writable and configuration stays
  protected. Both a configuration symlink's target and its directory entry are protected, preventing
  replacement of the linked credential path without copying credentials.
- Both providers were denied access to `/private/var/run/mDNSResponder`. Permitting outbound TCP/UDP
  does not permit this macOS DNS socket. The profile now permits that exact resolver socket; arbitrary
  local sockets remain denied. The same unauthenticated HTTPS probe against `api.anthropic.com` failed
  DNS resolution under installed `2.0.191` (curl exit 6) and reached HTTP 404 under the corrected
  product profile (curl exit 0). No token or model request was used.

Validation: 23 targeted tests and a broader 238-test provider/assurance-boundary selection passed,
with no skips. New real-process regressions exercise runtime write/locking, configuration protection,
credential-link replacement denial, resolver access and denial of an unrelated listening host socket.
The initial new socket fixture exceeded macOS's path-length limit; shortening its name fixed the
fixture without changing the product boundary. A separate `claude auth status --json` probe inside
the corrected profile returned exit 0, `loggedIn: true` and empty stderr; credentials stayed protected
and were not printed. These checks do not establish authenticated model execution or managed-settings
loading during a model run. No recon was retried, no live task was changed and no outer
sandbox permission rule was added. An outer host that refuses nested sandbox creation remains a
separate prerequisite; this correction does not grant permission to bypass that refusal.

## Recovery behavior

A persisted infrastructure failure now returns its original typed failure across restart instead
of becoming an opaque prerequisite refusal that launches Findings using the same broken provider.
An admitted failed launch remains stopped even if unrelated diagnostic evidence changes the task
basis. Older `ProviderFault` receipts with no retained output also stop honestly. A lost final
checkpoint can recover the startup/transport classification from its retained result without
relaunching the child. Unknown start/completion/handoff writes retain their existing reconciliation
boundary. No diagnostic prose is parsed as authority.

Trusted `orchestrate reconcile-stopped` remains the existing path after **actual** process/check
termination has been established and independently recorded by the operator. It preserves Failed or
Cancelled and makes no successful provider result, acceptance or repaired configuration. After that
reconciliation and correction of the actual infrastructure issue, an explicit restart can use the
existing routing path. A pre-admission unavailable executable has no child to terminate; correct
trusted setup and record the investigation/corrected prerequisite through the normal trusted path
before attempting a new basis. Repeating an unchanged command is not a recovery strategy.

No acceptance, independence, resource scope, replay, checkpoint, owner-epoch or required-check gate
was weakened. Historical receipts do not supply current authority or acceptance. Process termination
still cannot be inferred from Failed, an expired lease, missing output or unchanged candidate bytes.

## Behavioral and deployment coverage

The implementation extends and runs the existing assembled tests instead of creating another router
or acceptance system. Tests assert durable records, admitted actions and absence of forbidden actions.

| Scenario | Evidence and boundary |
| --- | --- |
| Trusted intake, recon, planning, bounded preparation, implementation, verification/checks, blind review, separate requirements review, explicit acceptance, Learn/archive | `GovernedCoordinationTests`, `LifecycleAssurance`, `AcceptanceCompletionTests`; complete and repair/replan fixtures, no automatic acceptance |
| Planning-ready setup without assurance; alleged user approval in a proposal | Added full-lifecycle cases stop before worker dispatch or work creation respectively; an acceptance-principal name or model prose cannot grant authority |
| Altered original request, unsupported scope, duplicate/changed body, forged identity/approval | `DurableCoordinationTests`, `CognitiveHandoffTests`, `TrustedAuthorityTests`, `CoordinatorDeploymentTests`; original request and existing grants preserved |
| Stale/ignored recon and external prerequisites, conflicting or missing decisions, adverse findings versus Proceed | Existing recon/preparation/routing tests and `GovernedCoordinationTests`; no dependent forward admission; recorded contradiction is not erased by successful transport |
| False/disputed findings, repeated and introduced defects, repair impact and dependencies | `FindingAdjudicationTests`, `RepairContinuationTests`, assembled repeated/introduced closeout cases; original finding identities survive and dispositions need independent evidence |
| Missing/failed/unknown checks, NEVER-TESTED, stale candidate, invalid/missing acceptance | `AssuranceOwnershipTests`, handoff-assurance boundary tests and `AcceptanceCompletionTests`; no residual-risk or operator fallback turns them into pass |
| Crash, lost replies, duplicates, concurrent owners, delayed judgments, stale epochs, ownership loss, interrupted checks | `CoordinatorCrashProcessTests`, `DurableCoordinationTests`, `AssuranceOwnershipTests`; real CLI/process boundary plus controlled clocks/gates and original identities |
| Actual candidate CLI, both actual adapters, unavailable providers and startup failure | New `ProviderStartupIntegrationTests`: executable fixtures implement required help and Codex hook-trust handshake, then fail inside production confinement; retained exit/stderr and one launch across restart, including changed diagnostic basis |
| Input-pipe failure, output ceilings, cancellation/timeout and cleanup uncertainty | New `ProviderStartupTests` and dispatch regressions, existing process/timeout suites; real child pipes plus controlled cleanup failure fixtures |
| Historical replay | Existing legal-history regressions and read-only candidate status/retrospective over copies of the actual 36-event failed task |

A scripted agent supplying an evidenced judgment is not proof of model judgment quality. The kernel
can enforce recorded conflicts, applicability and authority; these tests do not demonstrate automatic
semantic detection of every contradiction in free prose. Supported deployment remains macOS confined
single-member work with bounded complete declared input closures. Automatic multi-member scheduling,
unbounded repository builds, undeclared dependencies and other OS confinement profiles remain outside
this demonstrated profile. Python 3 is used only by the deterministic fake-provider test executable.

## Telemetry and adoption observations

Use the existing retrospective entry point:

```sh
dotnet src/AILedger.Cli/bin/Debug/net8.0/AILedger.Cli.dll retrospective build \
  --root /absolute/task-store --task TASK --orchestration
```

This optional extension reads existing events, coordination intents/stop, refusal journal and retained
provider results. It creates no telemetry store, owner lease or authority. Default retrospective
populations remain unchanged. It reports snapshot task version and coordination revision/epoch;
a detected task change during reading is disclosed. It is diagnostic observation, not atomic
current admission. Missing/corrupt journals remain absent/unavailable, never measured zero.

- Dispatches preserve run identity, cognitive role, phase, retention and typed failure cause. Recovery
  observations preserve their source run where originally recorded and the recorded result/judgment.
- Repeated-dispatch observations require the same recorded stage/work/basis, not merely two calls.
  Historical Discovery followed by Findings is reported as recovery, not duplicate Discovery.
- Repair dispatches and later ledger findings are traceable temporal observations. They do not prove
  causation or classify repeated defects. Task-13 findings/check/disposition receipts remain in their
  existing assurance journal and transport telemetry; they are not silently counted as ledger claims.
- Stage transitions show event ID, actor and correlation. Neither an operator actor nor a correlation
  label proves a human manually routed the task. User corrections/manual routing and human attention
  time remain unknown without attributed observations. Record actual human observations and their
  source references through the existing `retrospective record` artifact path; do not infer them from
  wall-clock gaps or model claims.
- Escalations show recorded kind, status and provenance. Whether a purported business decision was
  genuinely necessary, or routine engineering was unnecessarily escalated, needs semantic evaluation.
- New refusal rows carry the owning typed cause; old rows remain `unclassified`, and opaque rules stay
  `Opaque`. No classifier parses refusal prose. Missing/partial telemetry is explicitly disclosed.
- Completion observations distinguish a historical acceptance association from its absence. Neither
  proves current applicability; missed or invalid acceptance cannot be counted from absence alone.
  The owning assurance admission still decides whether a current decision is applicable.
- Usage retains per-run provider/model and known token buckets. Claude's explicitly reported terminal
  `total_cost_usd` is read through `RunCostReader`; negative, malformed, absent or unsupported values
  stay unknown. Reported USD is not verified billing. No tokens are converted to estimated dollars.

The historical copied task reports two ProviderFault dispatches, one failed Findings recovery, no
retained results and no measured provider usage. This is actual failure evidence, not adoption success.
Fewer dispatches after the fix demonstrate bounded failure handling in fixtures, not productivity.
No human-time, user-correction or delivery-quality comparison was performed. Existing earlier telemetry
failures remain documented in Parts 2–6; no unrelated collector deadlines or assertions were weakened.

## Operator setup and troubleshooting

A **planning-ready** configuration provides trusted intake, protected profiles, current actors,
scoped planning resources and a compatible provider. It can plan and select preauthorized work.
It is not an implementation-ready assurance configuration.

Before implementation, configure the protected task-13 authority and canonical store, explicit
single-member association, complete candidate/requirements/source/dependency inputs, pinned required
checks, implementer and independent verifier providers, blind-review role, separate requirements-aware
reviewer and external acceptor. Configure a sufficient owner lease as described in
[Part 6C+D](orchestration-driver-part-6-cd.md). `--acceptance-principal` only names an observer of the
external explicit decision. It supplies neither policy/store nor acceptance. The driver refuses missing
assurance before worker dispatch; setup stays on the operator configuration surface.

When launch fails:

1. Read the driver's returned failure kind and compact diagnostic, then its retained `runs/<run>.json`
   if retention succeeded. Compare provider status, exit, process observation, stderr, ledger closure
   and retention independently. `notAttempted` on an older fault means the output may be unrecoverable.
2. Check the exact configured executable/version, cwd, protected policy/config paths, environment
   allowlist, capability probes and confined startup separately. Version/help and network reachability
   alone do not validate authentication, organization settings, MCP handshake or governed interaction.
3. For `sandbox_apply` failure, the current outer sandbox cannot establish the nested product profile.
   Use an authorized compatible host; do not disable product confinement or broaden grants. For the
   observed Claude managed-settings error, first use a build with the DNS correction above and
   distinguish product confinement from outer-host restrictions. Authentication and organization-policy
   failures may still require the operator/admin path. No authentication or organization-policy change
   was performed in this task.
4. Do not restart an uncertain execution. Establish process-tree/check termination first, close an
   orphan through its existing trusted path if needed, record actual `process-termination` evidence,
   then use `orchestrate reconcile-stopped` with the original run/evidence identities. Failure status
   and historical receipts do not replace those observations.
5. Resume only with corrected prerequisites and current authority. `AwaitingAcceptance` is an explicit
   pending decision/evidence boundary. Exit 0 means Archived; stopped/pending is 4, unknown is 5,
   cancellation is 130. Neither a provider exit 0 nor a generic Proceed means accepted work.

## Validation record

All automated provider executions used deterministic fixtures or controlled local subprocesses.
The isolated real-provider observations are listed separately above. Test commands ran with host
permission for MSBuild IPC and subprocess confinement; an initial desktop-sandbox test invocation
could not bind MSBuild IPC sockets and did not run tests.

| Check | Actual result |
| --- | --- |
| Before-fix early-exit regression | **0 passed, 1 failed**; original `IOException: Broken pipe`, missing adapter result |
| Initial process/dispatch/timeout selection | **35 passed**, 0 failed/skipped |
| Built CLI/provider/telemetry selection after fixture and flag corrections | **19 passed**, 0 failed/skipped |
| Integrated orchestration/dispatch/providers/assurance/replay selection | **578 passed**, 0 failed/skipped; 4m01s |
| Final targeted process/lost-checkpoint/planning/approval selection | **37 passed**, 0 failed/skipped; 48s |
| Final telemetry/refusal-journal selection | **23 passed**, 0 failed/skipped; includes malformed observation/result records and backward-compatible refusal rows |
| First default full suite | **2,698 passed, 1 failed**, 0 skipped; the existing exact-field assertion omitted the newly added typed refusal cause |
| Actual historical copied task | Candidate status and `retrospective build --orchestration` exited 0; version 36 and both original failure identities preserved |
| Default full `dotnet test --nologo` | **2,703 passed, 1 failed**, 0 skipped: main 2,604/1 + memory 99/0; main 12m16s. Sole failure: unchanged findings application-attempt telemetry retained 1/2 rows |
| Unchanged isolated findings-telemetry diagnostic | **1 passed**, 0 failed/skipped; 285 ms. Does not erase the default failure |
| Additional serialized full suite | **2,703 passed, 1 failed**, 0 skipped: main 2,604/1 + memory 99/0; main 16m17s. Sole failure: assurance reviewer test received the telemetry-unavailable stderr fallback |
| Isolated assurance variants plus before-fix redaction regression | **3 passed, 1 failed**, 0 skipped: all three unchanged assurance variants passed; the new redaction test reproduced a partial secret left at the diagnostic cutoff |
| Final redaction/provider arguments/startup/built-CLI/isolated assurance selection | **22 passed**, 0 failed/skipped; 8s. Runs after the diagnostic redaction-order correction |

Development failures were kept distinct: initial new fixtures had namespace/platform/raw-string
compilation errors; a first runnable selection had **36 passed / 4 failed** because its oversized
request exceeded the actual manifest budget before reaching the intended startup boundary. Reducing
fixture padding preserved substantial pipe input without changing product limits. A later selection
had **15 passed / 4 failed**: three exposed missing CLI boolean registration for the new measurement
flag, and one fake Codex lacked the required hook-trust handshake. The owning CLI flag was wired;
the fake provider implemented that existing protocol rather than bypassing hook trust. Subsequent
19-test, 578-test and final 37-test selections passed. No failure was erased by weakening a gate.

The first default full run exposed the refusal-journal schema assertion: the additive `cause` field
was intentional, but its exact-field fixture still listed the old schema. The fixture now checks
the additional field against the owning exception's type and checks that old rows retain a null
cause. A compilation failure during that test update identified its manual row reader, which was
updated too. Review also added malformed telemetry schema/result guards; the optional retrospective
reports unavailable observations instead of throwing or inventing zero usage. All 23 final targeted
telemetry/refusal tests passed before the final default and serialized runs.

The final default run also exposed
`FindingsMeasurementTests.UnknownFlushCanJoinLaterCanonicalReceiptWithoutRewritingItsOutcome`:
its canonical transaction count was one as expected, but only **one of two** application-attempt
observations was present. That test and `FindingsAttempt.CaptureAsync` were unchanged. The collector's
existing 250 ms write/flush deadline and swallowed best-effort collection errors make load a plausible
contributor, not an established cause for this occurrence. Earlier Parts 5/6 losses concerned other
telemetry tests; they are analogous observations, not proof of this failure's cause. No assertion,
collector deadline or unrelated logging implementation was changed. The default failure remains a
failure regardless of any subsequent isolated or serialized result.

The additional serialized run exposed a separate telemetry-path failure in
`AssuranceBoundaryTests.ActualStdioClientDiscoversScopedToolsCallsThemAndCannotSpoofIdentity`
for the reviewer. Scoped-tool discovery, reads/replay, identity-spoof rejection and revocation
assertions passed; the final empty-stderr assertion received a `transport_attempt_id` diagnostic.
That shape is the existing transport collector's `collection_status=unavailable` fallback. Its
250 ms collection deadline is a plausible contributor, but the fallback does not retain the
exception, so the exact underlying write failure remains unknown. Neither that test nor the
collector/fallback path was changed by Part 7, and the assertion remains intact.

Final review found a diagnostic-only redaction ordering defect: shortening a capability-probe error
before redacting an explicit environment secret could leave a partial secret that no longer matched.
The new regression first failed with that partial value, then the helper was changed to redact the
complete diagnostic before applying its bound. Both full-suite results above precede this final
localized correction; subsequent targeted provider/built-CLI/assurance checks validate it. They do
not turn either full run green, and no further broad run was used to chase an intermittent pass.

Required final commands:

```sh
dotnet test --nologo
dotnet test --no-build --no-restore --nologo -- xUnit.ParallelizeTestCollections=false
```

The bounded implementation and tests do not close the real-use milestone. Concrete remaining limits
are unresolved original stderr/child-permission attribution, current Claude managed-settings startup
failure, no successful real authenticated governed interaction, unsupported configurations described
above, and unmeasured adoption benefits. The installed supervisor does not contain this candidate's
fixes until a separately authorized release.

The existing tool project was also packed locally with `dotnet pack -c Debug --no-build --no-restore`
to `/tmp/part7-package/AILedger.Cli.2.0.0.nupkg`, then extracted without installing it. Its CLI,
Core, Storage and Providers DLLs match the tested build byte-for-byte. The extracted CLI's `version`,
`orchestrate coverage` and copied-history `retrospective build --orchestration` all exit 0. This
candidate retains the project's development version `2.0.0`; that version/commit alone does not
identify the uncommitted changes. Package SHA256:
`13ca3d72e2efaad9016f3279af7e18ea83e572a8bd3f70803f54dac1792d72c1`.
The package emitted the existing missing-readme advisory. Nothing was installed globally or released.
