# Orchestration driver — Part 2: trusted orchestration authority

Status: implemented for the macOS process boundary; validation record below.
Parent: [scope and delivery parts](orchestration-driver-scope.md).
Development used this existing worktree, temporary ledgers and fixture processes. No live ledger,
paid provider session, global tool installation, branch switch or commit was involved.

## Trust model

| Caller | Authentication / authority | What it cannot establish |
|---|---|---|
| Local human/operator | An authenticated OS-owner process outside the child sandbox, with access to the local ledger and trusted host composition | An actor string alone does not authenticate a remote human. The CLI is not a remote authentication service. |
| Execution host | Trusted application code owns storage, authority configuration, process creation, completion secret and session endpoints | Provider output cannot select a host principal or supply a completion secret. |
| Future driver | A host-created `RoutineOrchestrationAuthority`, bound to one task, delegating actor, assignment provenance/capabilities and expiry | It cannot create exceptions, change roles/scope, resolve business decisions, or accept/complete work. It is not an agent-facing Operator role. |
| Provider child | A random bearer capability connects to one live host endpoint; the host fixes task, subject, run, capabilities and lifetime | Payload identity fields, role names, correlation IDs, provider session output, readiness and NextActionContract observations cannot widen authority. |
| Tool/model/configuration text | Untrusted input subject to existing strict request parsing | Text claiming user approval is never approval. A child-created host or configuration file has no access to authoritative storage. |

The privileged OS owner remains trusted. This does **not** protect against root, a compromised
kernel, a debugger already controlling the host, malicious trusted application code, or an owner
who rewrites the application/ledger outside the sandbox. Operator CLI access remains supported;
there is no new password flag, environment identity or self-asserted “trusted caller” option.

## Routine orchestration

[RoutineOrchestrationAuthority](../src/AILedger.Core/Authority/RoutineOrchestrationAuthority.cs)
is an in-memory authority object, not a serialized grant or persisted role. Trusted composition
installs its command handler around the existing kernel handler at the storage mutation boundary.
The locked, freshly replayed state is checked before the owning kernel admission rules execute.

The supported routine surface is deliberately small:

- Managed run start with a launcher-secret hash and **without** either brief exception.
- Run completion carrying a launcher secret; the existing kernel verifies the secret, chronology,
  role, required outputs and other completion requirements.
- Stage transitions without prerequisite waivers, current-context recording and coordinator-session
  accounting, all subject to their existing admission rules.

Everything else is denied by default, including role/scope/constraint changes, work creation,
work completion/abandonment, user-decision resolution, exceptional orphan closure, waivers and
technical acceptance. Those remain separately authorized host/human operations. Work completion
is intentionally not delegated before Part 6 supplies the acceptance bridge. This does not fix or
change the existing permissive completion cases characterized in Part 1.

Expiry, explicit revocation, a different task/actor, or a changed delegating assignment invalidates
the routine authority. New command types do not silently acquire authority. The wrapper delegates
all domain rules to `CommandHandler`; it does not infer admission from explanatory text, readiness,
the order of next actions, or a separate workflow-policy implementation.

## Bound agent sessions

[AgentSessionAuthority](../src/AILedger.Core/Authority/AgentSessionAuthority.cs) fixes the assigned
actor, assignment provenance/capability ceiling, task, run, provider and deadline. The production
[ProviderFindingsSession](../src/AILedger.Cli/Findings/ProviderFindingsSession.cs) installs it through
the storage session factory. It checks live authority before tool binding and rechecks mutations
under the storage lock. A trusted role promotion does not promote an already issued agent session.
Run closure, reassignment, expiry or revocation stops subsequent authorized use, including task-13
calls through the provider relay. Standalone human assurance hosts retain their separate live policy.

The existing authenticated relay and strict MCP schemas remain the transport. Session identifiers
reported by a provider are observations, not authentication. Host grants can be reduced, but cannot
expand beyond the connection's initial grants. Supported recording retains host-assigned IDs and
the existing exact-body/request-key receipt semantics. A replayed receipt remains historical fact.

The bound mutation ceiling permits findings/evidence, alternatives, claim dispositions when the
persisted capability permits them, own-run artifact submissions and own-run producer outcomes.
It excludes orchestration, role reassignment, user decisions, waivers and run closure. Existing
inspection/retrieval/readiness still use their role-filtered services. The bound handler does not
grant a role the capability needed to perform an otherwise allowed command.

Task-13 acceptance grants are refused before adapter construction for **all** provider children,
including a child named `operator`. Independently authenticated local `assurance serve` and human
acceptance remain available under the existing task-13 rules. Principal/grant revalidation, provider
independence, blind governed review and the separate requirements-aware assurance context remain.

## Alternative paths and process enforcement

[ProviderProcessIsolation](../src/AILedger.Providers/Process/ProviderProcessIsolation.cs) adds an
outer macOS Seatbelt profile through `sandbox-exec`. Production launches prepare it before provider
resolution; `SystemProcessRunner` applies it to the model process, and the OS inherits it across
exec/fork into shells, alternate CLIs, replacement MCP hosts and navigation processes. Clearing the
environment or copying a CLI does not remove the OS restriction. There is no unconfined fallback
or child-selected exception on unsupported hosts.

The launcher no longer appends the ledger as a writable provider directory. Resource-grant
resolution, including bundle unions and repository ceilings, remains owned by the existing resolver.
The outer profile permits writes only to those selected directories and private launch scratch,
while excluding the authoritative ledger, selected lesson store, task-13 authority/store, running
application directory, cognitive source and operator provider configuration. `.ailedger` subtrees
are excluded even within a writable repository. Writes to `.git`, `.claude`, `.agents` and selected
repository `.codex` configuration are denied; operator Codex state is protected separately from its
source worktrees. The resolved provider executable directory is read-only. Explicitly hidden authority paths cannot be read,
so direct ledger reads cannot bypass role-filtered MCP inspection. Protected ancestors cannot be
renamed to move authority into an allowed path. Paths are canonicalized; OS checks apply to symlink
and hardlink attempts. Provider-created runtime configuration files are protected against rewriting.

Only the existing environment allowlist and explicit launch environment reach the process; private
`TMPDIR`/`TMP`/`TEMP` replace shared scratch for confined launches. The completion token stays in
the launcher. The relay credential authorizes only the bound endpoint and expires with that host.
No additional credential copying/forwarding is introduced.

Private scratch is deleted at process exit. Confined work-item sessions without assurance receive
a durable execution-report path at `<working-directory>/.ailedger-output/run-<escaped-run-id>/execution_notes.md`,
inside their existing write grant. Return that path and record findings through the bound tools;
scratch paths are not retained evidence. This does not recover reports already deleted by older runs.

The default-deny profile permits ordinary file reads outside hidden authority, process execution,
forking, self-signals, limited system services and outbound TCP/UDP needed for provider transport.
It also permits the exact `/private/var/run/mDNSResponder` socket required for macOS DNS resolution.
It does not grant arbitrary process inspection/tracing, signalling other processes, other Unix sockets,
Apple events or external service launch. It is **not** a general network/data-exfiltration sandbox.
Any reachable privileged host endpoint must authenticate its own callers; an unauthenticated local
broker that executes commands as the owner is outside the supported deployment boundary.

Listener binding is not granted, including loopback. VSTest and local HTTP mock servers therefore
require an authorized host verification route. A `local tcp "localhost:*"` bind/inbound rule was
tested on macOS and also admitted wildcard listeners and same-machine connections to the LAN
address; it is not a demonstrated 127.0.0.1/::1-only boundary. Do not widen it automatically.

The built-in version/help probes and Codex hook-trust preparation still run as trusted setup, before
model input. Provider executables and their dependencies must therefore be trusted and protected
from the work being inspected. Custom `IAgentAdapter`/`IProcessRunner` implementations are trusted
host code and must honor the supplied isolation; they are not model-selectable plugins. Configured
task-13 checks likewise remain trusted, pinned, bounded host programs, not an authorization to run
arbitrary candidate code with host privileges.

## Compatibility and limits

- The supported OS enforcement implementation is macOS only. Linux/Windows need an independently
  tested implementation before confined production launch is enabled there. No fake-provider test
  establishes compatibility with a real provider's current sandbox/authentication implementation.
- Paid/live provider tests were not authorized. In particular, authentication refresh, provider
  writes to ambient home/cache paths, nested provider sandboxes and provider-specific services may
  fail under the outer profile. Such failures must stay explicit; do not broaden access silently.
- The OS profile protects specified authority, not every readable file on the machine. Existing
  reviewer manifest filtering and provider memory/configuration controls remain necessary. Review
  isolation is not a claim that the entire working repository contains no narrative.
- Bound sessions use the existing structured surface. Unsupported cognitive operations, including
  governing artifact kinds and lesson consultation, require an explicit trusted host handoff.
  They cannot fall back to unrestricted child CLI writes. The launch briefing states this limit.
  No full lifecycle orchestration capability is claimed here.
- No event schema, historical role assignment or replay-time rule changed. Runtime authentication
  cannot be reconstructed as a new prerequisite for historical events. Previously legal waiver
  histories continue to replay; authenticated humans can still issue recorded exceptions.
- This adds no scheduler, durable grant store, crash-safe router, automatic repair, acceptance bridge
  or production driver. It does not change the Part 1 characterization of acceptance gaps.

## Verification

`TrustedAuthorityTests` exercises trusted routine dispatch/completion through real temporary storage,
kernel stage/reviewer refusals, mismatched identities/tasks, exceptions, expiry/revocation, role
changes, bound recordings and human exception replay. `ProviderIsolationTests` launches actual local
processes and checks allowed writes plus refused CLI impersonation, cleared-environment descendants,
protected reads/writes, links, parent rename and configuration replacement. `ConfinedProviderLaunchTests`
uses the production launcher, a fake provider, a real confined relay and a forged local MCP host.
It proves successful subject-attributed recording and denied operator impersonation on the same boundary.

Existing suites continue to cover provider-independent verification, launcher-owned completion,
receipt recovery, frozen candidates, blind reviewer manifests and task-13 independent acceptance.
Only previous assertions expecting a writable ledger grant were changed to assert its explicit
exclusion. The completion-failure fixture forwards the new binding factory while preserving its
original failure injection and completion assertions.

Validation on 2026-10-07:

| Check | Result |
|---|---|
| Final default `dotnet test --nologo` | 2,511 passed, 1 failed, 0 skipped. The failure is the telemetry count described below. |
| `dotnet test --no-build --no-restore --nologo -- xUnit.ParallelizeTestCollections=false` | **2,512 passed** (2,413 main + 99 memory), 0 failed/skipped; exit 0. |
| Local documentation links and `git diff --check` | Passed. |

The default full run failed
`InspectionMcpTests.TrustedProviderRelaySupportsInspectionRetrievalAndReadinessWithRevocation(codex)`:
all tool-response assertions passed, but 20 of 36 expected diagnostic rows were present. Another
concurrent run also lost one diagnostic row in `FindingsOperationTests.AttemptJournalDistinguishesCommitReplayAndRefusalWithoutCopyingProse`.
Both cases passed unchanged in the focused rerun. Their collectors use existing 250 ms best-effort
deadlines in `FindingsTransportJournal` and `FindingsAttempt`; contention is a plausible explanation,
not a newly proven root cause. No collector code, time limit or assertion was changed to conceal it.
The complete serialized run passed all assertions. It uses the supported [xUnit RunSettings option](https://xunit.net/docs/config-runsettings)
`xUnit.ParallelizeTestCollections=false`; it changes scheduling only, with no test selection or skips.

## Handoff

Part 3 can start. Extract shared launch execution while retaining the prepared isolation plan,
bound recording-service factory, subject-filtered manifests, launcher-held completion secret and
all existing preflight ordering. Keep the driver's narrow service separate from the execution
host's controlled subject-context/assurance preparation. Do not give an agent the host service.

Parts 3/4 must expose supported typed handoffs for cognitive operations not yet supplied over MCP;
missing coverage is explicit, not permission for filesystem/CLI fallback. Parts 4/5 own scheduling,
durable ownership and recovery. Part 6 owns the acceptance bridge and interrupted-candidate issues.
Platform/provider compatibility is required before real adoption in Part 7. None of these limits
requires a business decision before the Part 3 extraction begins.
