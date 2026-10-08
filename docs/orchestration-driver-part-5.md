# Orchestration driver — Part 5: governed coordination, ownership and recovery

Status: implemented for the bounded macOS deployment below. Validation is recorded at the end.
Parent: [scope and delivery parts](orchestration-driver-scope.md). Parts 1–4 remain in place.
**This is not ready for unattended real work. Parts 5 and 6 together are the supported live-use milestone.**

Development used the requested existing worktree, temporary ledgers, fake adapters and controlled
local subprocesses, outside the development kernel workflow. No real provider, live ledger,
global installation, branch change, commit, merge or publication was used.

## Reviewable increments and owning components

| Increment | Principal files | Responsibility |
| --- | --- | --- |
| 5A | `TrustedDriverIntake`, `DriverPreparation`, Core `PreparationAuthority` and `PreparationSelectionRules`, cognitive handoffs | Trusted request provenance, admitted routine decisions, bounded staffing/work preparation and current prerequisites |
| 5B | Storage `Coordination/`, `DriverContinuity`, `DurableOrchestrationDriver`, shared `DispatchCheckpoint` and `DispatchRecovery` | One owner, epochs, locked mutation fencing, durable dispatch intent and exact receipt reconciliation |
| 5C | `OrchestrationDriver.Recovery`, `TrustedExecutionRecovery`, existing router/dispatch seams | Retained diagnostic context, brief refresh, bounded investigation, interrupted-work inspection and no-progress stops |

Core remains independent of CLI and providers. The driver still uses the Part 3 dispatch service;
there is no second provider launcher, arbitrary command dispatcher or acceptance path. New
preparation events have command-time and replay validation; historical events acquire no new gate.
The two permissive Part 1 completion characterizations remain unchanged.

## Trusted intake and decision ownership

`TrustedDriverIntake.CreateAsync` and `orchestrate intake` are trusted host entry points. They
preserve the original request as both task goal and operator-authored `UserRequest`, and record
original constraints with intake provenance. Intake request identity, original body and resulting
events commit together. An exact retry reconciles interrupted multi-command intake; a changed body
cannot reuse its key. Model-authored summaries and statements of approval cannot call this endpoint
from the confined coordinator, replace the request, add constraints or grant exceptions.

The full lifecycle fixture starts with this intake and a trusted profile policy, with no work,
roles, stage waivers or brief waivers prepared by the fixture. It runs discovery, recon/lessons,
design, scope, automatic bounded preparation, implementation, independent verification and blind
review, and stops at `AwaitingAcceptance`. The external-research and closeout paths from Part 4
continue through the same live handoff and kernel admission services.

| Decision | Owning path and retained basis |
| --- | --- |
| Intent and assumptions | Discovery lead sees original request/constraints; records assumptions and evidence through bound findings tools |
| Current behavior and unknowns | Lead recon, Researcher where required, current claim dispositions and role-filtered lesson consultations; existing stage gates apply |
| Design, scope and criteria | Current governing artifacts and explicit planning decision; validated current dependencies and cited evidence/artifacts precede routine resolution |
| Roles and work profiles | Trusted preauthorization sets exact role/capability and single-directory work templates; completed Scope lead selects profiles by admitted decision and current plan/request references |
| Findings and replanning | Existing bounded Findings role and backward stage edges; generic Proceed cannot dispose a blocked verifier judgment, override producer blockers, unresolved challenges, adverse verifier assumptions or unresolved attention items |
| Refusal diagnosis | Typed attempted action, observed stage/version, dispatch identity and owning diagnostic reach a bounded Findings run with explicit investigation instructions |
| Closeout | Existing lead synthesis/lesson marking and archive admission; work must already be terminal through a separate authorized path |

Two new cognitive operations close the demonstrated preparation gap:

- `decision_resolution`: decision ID, accepted/superseded status, expected task version,
  evidence IDs and current artifact IDs. Only a PlanningLead with the actual `ResolveDecision`
  capability can resolve its own proposed routine decision. Current validated dependencies,
  evidence/artifacts and absence of open challenges/escalations are checked. The existing decision
  command performs admission. Original request, producer/run binding and references remain in the
  atomic event receipt. This operation grants no escalation, constraint, waiver or acceptance power.
- `preparation_selection`: accepted decision, current plan and original request artifact IDs,
  selected profile IDs and producer evidence. Core records one immutable `PreparationSelected`
  per active Scope planning run. It captures current constraint and dependency-evidence identities;
  changed prerequisites require reassessment. This is the narrow new contract missing from Part 4.

`PreparationAuthority` is separate from restricted routine orchestration. It admits only exact
preauthorized role assignments and work templates, with the same live delegating authority and
kernel admission. It cannot replace an existing actor assignment, assign an operator, broaden a
scope, waive a brief or complete work. Work creation derives claim dependencies from the admitted
decision. Unknown profiles, missing decisions, stale dependencies, changed constraints/evidence,
changed producer authority and conflicting existing work refuse preparation. The driver revalidates
selected work before dispatch. A unique admitted profile supplies the selected work without another
user command; multiple items still require explicit single-item selection.

Part 4's legacy prepared-task entry remains available for fixtures without a preparation policy.
It reuses existing artifact/stage, work, role, provider and task-13 admission. It does **not** establish
that trusted intake or Part 5 planning selection was performed; use the intake/preparation path for
that coverage. Bundles, concurrent workers and arbitrary scope creation remain unsupported.

A routing assessment is still attributed judgment, not proof of correctness. The driver uses the
owning verifier-document observation API for adverse structured dispositions; it does not parse
free-form assessment rationales or NextActionContract text into instructions. External ledger
changes after the producer's run begins refuse delayed consequential handoffs. Each new cognitive
write also carries a task-version condition checked under storage's mutation lock. Candidate
capture, independent verification and blind-review pairing retain their Part 4 boundaries.

Scoped worker routing reports have one narrow concurrency exception: additive claims, evidence,
alternatives and outcome/routing reports attributed to another worker on disjoint work may pass
without invalidating the briefing. Evidence linked to the reporting worker's claim dependencies,
operator writes and governing changes still refuse. Task-wide planning/decision/preparation checks
remain conservative. Recording a worker report neither accepts it nor grants a downstream action.

## Enforceable deployment and setup

The supported profile is **`macos-confined-cognitive-coordinator-v1`**, on a local macOS filesystem
with `sandbox-exec`. The deterministic .NET host is trusted composition. All model-controlled
coordination occurs in its confined cognitive children and their authenticated, bounded relay.
There is no model-controlled process outside that boundary in this profile.

The profile reuses Part 2 process isolation, including fork/exec inheritance, hidden authoritative
ledger/lesson/configuration/store paths, protected executables and scoped writes. Loading the CLI
or host assemblies in the child does not restore filesystem authority. The real subprocess test
records a proposal through the relay while attempts to read/write the ledger and invoke privileged
CLI role assignment are refused. Existing symlink, alternate-host, process and provider confinement
tests remain in the regression selection.

An owner-level conversational shell, including a desktop conversation that can invoke privileged
host APIs directly, is **unsupported for this coordinator-authority guarantee**. A prompt or role
label does not confine it. Linux/Windows, remote/network filesystems, untrusted adapters/executables,
and unauthenticated privileged local brokers are not supported deployment profiles. Live provider
credential/cache compatibility remains untested; failures must not widen isolation automatically.

Trusted setup example (temporary/local fixture paths shown):

```sh
dotnet src/AILedger.Cli/bin/Debug/net8.0/AILedger.Cli.dll orchestrate intake \
  --root /absolute/fixture/tasks --lesson-root /absolute/fixture/lessons \
  --task example --actor operator --request-file /absolute/protected/request.json

dotnet src/AILedger.Cli/bin/Debug/net8.0/AILedger.Cli.dll orchestrate run \
  --root /absolute/fixture/tasks --lesson-root /absolute/fixture/lessons \
  --task example --actor operator --profiles /absolute/protected/profiles.json \
  --working-directory /absolute/repository --cognitive-root /absolute/cognitive \
  --assurance-authority /absolute/protected/authority.json \
  --assurance-store /absolute/protected/assurance-store
```

These commands describe setup, not authorization for provider spend. `request.json` is strict JSON,
at most 128 KiB, with `schema_version: 1`, `request_id`, `title`, `original_request`, `constraints`
and `tags`. The last two are arrays of strings. Preserve original user text; do not populate this
trusted file from a model's claim that approval was given.

The existing strict profile schema remains version 1 and adds optional `preparation`. Its form is:

```json
{
  "schema_version": 1,
  "agents": [
    { "work": "discovery", "subject": "lead", "provider": "codex" },
    { "work": "recon", "subject": "lead", "provider": "codex" },
    { "work": "design", "subject": "lead", "provider": "codex" },
    { "work": "scope", "subject": "lead", "provider": "codex" }
  ],
  "preparation": {
    "roles": [
      { "actor": "lead", "role": "PlanningLead", "capabilities": [
        "BuildContext", "AddClaim", "ResolveClaim", "AddEvidence", "RecordArtifact",
        "RecordAlternative", "ProposeDecision", "ResolveDecision", "RaiseEscalation"
      ] }
    ],
    "work": [
      { "profile": "bounded-change", "work": "W1", "title": "Bounded change",
        "owner": "worker", "scope": "/absolute/repository/area", "base_ref": "trusted-existing-base" }
    ]
  }
}
```

This excerpt shows the new shape; complete setup must also supply preauthorized Worker, Verifier,
CodeReviewer and any Researcher/Findings/Closeout roles and agent profiles needed for the task.
The verifier's provider must differ from the worker's. `base_ref` is optional when no Git baseline
applies; when supplied, it is trusted setup data, not invented by the driver. Only one directory per
work template is supported. Planning briefs receive the bounded profile choices, not protected
configuration or authority objects. The whole profile file remains protected from model writes.

Task-13 setup still follows [the assurance contract](handoff-assurance-v1.md). Declare all material
inputs and scoped grants; missing configuration, incompatible blind-review contexts and unsupported
input closures are explicit blockers. No provider child receives acceptance tools.

`--owner-lease-seconds` is a trusted host option, 1–3600 seconds, default 120. Renewal occurs every
third of the lease period. `--timeout-seconds` remains the provider timeout. Neither duration grants
acceptance, permission to retry an uncertain process, or permission to dismiss unresolved work.

## Persistence, ownership and compatibility

Storage owns `coordination-v1.json` beside the task event log. Schema 1 stores a monotonically
increasing ownership epoch, owner identity, expiry, revision and versioned execution payload.
Acquisition, renewal, journal compare-and-swap and bound ledger mutations use the existing task
mutation lock. A new owner can take over only an expired/released lease. A stale epoch cannot write
ledger commands, bound child records, retained provider results or journal updates. Renewal failure
cancels the current driver; it does not infer provider termination.

Journal replacements use a private temporary file, write-through/flush and atomic replacement;
Unix files are created with user-only permissions. Unknown schema versions, corrupt state, oversized
journals and revision conflicts fail closed. The payload limit is 4 MiB and the dispatch bound is
256 intents; exhausting either preserves unresolved work. There is no automatic compaction or
history replacement. Tests establish local process-crash behavior, not power-loss durability or
network-filesystem guarantees.

Each intent retains the stable original run/dispatch identity, request, task stage, producer
assignment and input/result basis. Shared dispatch checkpoints retain independently observed
retention/closure results. Provider output stays in its existing sidecar; the journal holds execution
references and receipts, not a second judgment/acceptance ledger. No launcher completion credential
is written to the journal. Terminal command observations are explicitly redacted.

Cognitive receipts use optional, schema-versioned `HostRequest` metadata committed atomically with
the existing command event group. It retains the original authenticated binding, request key and
body. Storage checks current agent authority before replaying a receipt, rejects conflicting content
and rejects duplicate/corrupt receipt groups. Exact retries recover the original event and assigned
record identities across lost replies, new session objects and process restart. Expired, closed,
revoked or reassigned sessions cannot turn historical receipts into fresh authority. Services without
atomic receipt support retain the conservative unknown-write freeze.

Old event histories without this metadata replay unchanged. New preparation events require a
binary that understands them; older binaries are not compatible readers of those new events.
There is no migration that rewrites historical judgments or tightens historical completion rules.

## Recovery and operating behavior

| Interruption | Supported behavior |
| --- | --- |
| Before persisted intent | Reobserve under the current owner and propose normally |
| Intent persisted, no ledger run | Fenced storage establishes absence; retry the original dispatch identity, never a replacement run to hide uncertainty |
| After run creation / during provider or task-13 execution | Preserve active/unknown state; do not relaunch or repeat checks because a lease expired |
| After result retention but before acknowledged closure | Preserve result and redacted terminal observation; only the original live launcher can use its process-local completion credential |
| After ledger closure / before driver acknowledgement | Reconcile the exact run and retained result, check authority/basis, and consume the original receipt without another provider call |
| Cognitive mutation committed, reply lost | Retry original binding/key/body; atomic event receipt returns original IDs |
| Changed evidence, authority, selection or candidate | Refuse stale consumption or require fresh applicable work; no revived grants or stale acceptance |
| Unchanged refusal or repeated judgment | Keep diagnostic/evidence, refresh brief, investigate once per unresolved basis, and stop without repeating identical dispatch |
| Failed/cancelled working run | Preserve physical-candidate uncertainty; a configured Findings role can investigate and explicitly request repair/replan; generic Proceed cannot reuse old assurance |
| Cancellation, capacity or budget boundary | Pause with work unresolved; never create acceptance or work completion |

For a hard host crash, first establish whether the original provider/check still exists. A lease
is not a process supervisor receipt. Use the existing trusted process termination and orphan
Failed/Cancelled closure path if the original launcher is gone; never forge Completed or a launch
credential. Record operator-authored evidence with source type `process-termination`, citation
`<task>/<run>`, and a summary of the observed provider-tree/check termination, after the run started.
Then explicitly acknowledge it through the trusted host:

```sh
dotnet src/AILedger.Cli/bin/Debug/net8.0/AILedger.Cli.dll orchestrate reconcile-stopped \
  --root /absolute/fixture/tasks --task example --actor operator \
  --run ORIGINAL_RUN --termination-evidence TERMINATION_EVIDENCE
```

`TrustedExecutionRecovery.ConfirmStoppedAsync` takes exclusive ownership, checks operator provenance,
the exact original intent/run identity and existing Failed/Cancelled closure, then records a failed
execution receipt referencing that evidence. It preserves missing result retention as missing; it
neither synthesizes a provider result nor restores a lost credential. Active or successful runs and
agent-authored termination claims are refused. The next driver may investigate/replan from the changed
basis instead of being permanently blocked by the old pending intent. This exceptional operation is
not available to the routine coordinator or cognitive children.

An interrupted task-13 check additionally requires its owning assurance reconciliation
before any explicitly authorized new check key. The driver has no automatic check replay API.

The automatic-approval reviewer rejected a proposed credential-persistence design during development.
The delivered alternative keeps completion credentials process-local, redacts terminal observations
and refuses automatic closure when that authority is lost. This is a deliberate recovery boundary,
not a silently waived prerequisite.

Known stale-version refusals refresh current state/brief before reconsideration. Opaque refusals
with a configured Findings role include the actual attempted action, owning diagnostic and current
context. The lead must investigate whether the prescribed procedure was followed before alleging a
kernel defect. A changed authoritative basis can support a fresh admitted action; an unchanged
Proceed label cannot repair a missing prerequisite. Routine engineering problems do not create
business-decision escalations automatically.

## Validation and Part 6 handoff

New tests cover trusted intake/proposal separation; admitted routine decisions; missing decisions,
unknown profiles, stale/contradictory findings; intact original constraints; preparation without
operator fallback; exclusive owners and version races; stable pending dispatch identities; lost
cognitive replies and exact/conflicting retries; expiry/revocation; external changes before delayed
judgment; unchanged refusals; real confined coordinator bypass attempts; and built-CLI fake-provider
host death/takeover without relaunch. The full intake-to-review test contains no stage or brief waiver.

The focused selection also includes existing task-13 configuration, independent-provider, candidate,
blind-review isolation, cancellation, confinement and dispatch tests. Full-suite results include the
existing interrupted-check and historical-replay tests. No inspection collector deadline or assertion
was relaxed, and no test was skipped to conceal a failure.

Validation on macOS, with local fake providers and temporary ledgers:

| Run | Result |
| --- | --- |
| Focused orchestration/dispatch/authority/confinement/assurance regression selection | **228 passed**, 0 failed/skipped |
| Final default `dotnet test --nologo` | **2,608 passed**, 0 failed/skipped (2,509 core/application tests + 99 memory tests; main suite 5m24s) |
| Additional serialized `dotnet test --no-build --no-restore --nologo -- xUnit.ParallelizeTestCollections=false` | **2,608 passed**, 0 failed/skipped (2,509 + 99; main suite 6m53s) |
| `git diff --check`; relative documentation links | Clean; valid |

The focused filter selects Orchestration, Dispatch, TrustedAuthority, ProviderIsolation,
ConfinedProviderLaunch, ProviderFindingsSession, AssuranceProvider, CliCommandCatalog and the owned
archived-sibling lesson-recall regression. It includes the real confined coordinator and built-CLI
host crash/takeover fixtures. The final default run additionally includes the built-CLI
`reconcile-stopped` continuation added after the focused run. No real provider was used.

The first default run during development had **2,599 passed and 2 failed** (2,601 then-current tests).
One was the new owner-race fixture's 300 ms lease expiring during parallel fixture setup; the test now
uses an injected `TimeProvider`, while the separate subprocess crash fixture still exercises real
lease expiry. The other was the known
`InspectionMcpTests.TrustedProviderRelaySupportsInspectionRetrievalAndReadinessWithRevocation(claude)`:
all response assertions passed, but **34 of 36** telemetry rows were retained (Part 4 recorded 22/36).
The unchanged diagnostic rerun passed alongside the correction tests (18/18). The collector's existing
250 ms deadline remains a plausible contributor, not a proven cause. The collector and its assertions
were not changed. The later default pass does not erase this observed intermittent failure.

The reassignment regression initially expected no follow-up dispatch; the driver correctly refused
the old planning selection and launched bounded Findings investigation. Its final assertion verifies
that one investigation occurs and no second review runs. This changed only the new test's expectation
of the authorized recovery path.

Part 6 still owns requirements-aware independent review association, explicit task-13 acceptance,
complete finding disposition, acceptance applicability and correct automatic work completion.
`AwaitingAcceptance` remains a hard boundary. No routing judgment, terminal run, receipt or successful
process substitutes for that bridge. Legacy prepared fixtures and local fake-provider success do not
establish production-provider compatibility or unattended real-work readiness.
