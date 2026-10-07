# Isolated Roslyn compatibility and governed recon trial — 2026-10-08

## Result and scope

A real governed Codex recon completed and filed its own valid InternalRecon after two
kernel integration defects were corrected. Roslyn loaded the adapters snapshot with
78 projects, no skipped projects and no unresolved-reference warning. Definitions,
method source, callers and references were exercised. This establishes feasibility of
an isolated container worker, not a production backend, native-platform equivalence,
successful implementation or acceptance of the original cursor-strategy task.

The original task `2026-10-07_2126-yaml-falcon-cursor-strategies` and source checkout
were not mutated or relaunched. The installed tool remains 2.0.192. Trial launches used
the separately built candidate CLI and an explicit experimental Roslyn override.

## Why the container experiment was used

The current upstream [NamedPipeUtil](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Compilers/Shared/NamedPipeUtil.cs)
still combines `/tmp` with the pipe name on Unix. The
[BuildHostProcessManager](https://raw.githubusercontent.com/dotnet/roslyn/main/src/Workspaces/MSBuild/Core/MSBuild/BuildHostProcessManager.cs)
generates a GUID pipe name; neither inspected path offers an environment override for
the directory. The installed RoslynCodeLens 2.18.1 carries Roslyn Workspaces 5.9.0.
A native fix would require a coordinated upstream/client/BuildHost rebuild and packaging
maintenance, rather than a local configuration change. No binary patch or broad host
Unix-socket permission was introduced.

The prototype owns Roslyn/MSBuild in a non-root container with private writable `/tmp`
and source workspace, no network, no capabilities, `no-new-privileges`, read-only root,
256-PID limit, 3 GiB memory and two CPUs. Host source snapshots and an approved package
cache are mounted read-only; no ledger, credentials directory or Docker socket is mounted.
The provider talks through a scoped authenticated loopback relay behind the existing
AILedger navigation bridge. Docker remains owned by the trusted host.

Docker here is Colima, not Docker Desktop. Its VM does not see host `/private/tmp`.
Disposable snapshots were placed in the repository's ignored `artifacts` directory,
outside project source globs. Source snapshot omissions are explicit: tracked build and
source files only, no hidden state, symlinks, credentials or general content files.

The first image could not contact NuGet. Building from the already installed Roslyn
nupkg worked offline. A subsequent real-solution load reported `ready` while all 78
projects had unresolved references. Adding the matching .NET 8 framework packs from
the already cached official SDK image and restoring against the read-only package cache
resolved that failure. No private-feed credential was passed through.

The source checkout was adapters commit `3199df628771672274003864bd52e76c8d78dbcc`.
The prepared snapshot digest recorded by both workers was
`c8bd5631585c7ee59c8cf46b2c0ab584ff46af15e56e7367e84c2f98ab7fceb4`.

Pinned image inputs:

- .NET 10: `sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317`.
- .NET 8 packs/runtime: `sha256:78235e09001f52b6592c458ac010775ebac6725422e80cd0c1650590f67b2743`.
- Built experimental image: `sha256:6be1f5798bb5c7154625d7ead51633848eea81b65d6f2dedd15206037936ce55`.

## Defects found by actual governed use

1. **Nested shell sandbox:** the kernel already applies inherited macOS confinement,
   while Codex `workspace-write` tried to apply a second sandbox to shell commands.
   R1's harmless rule-file discovery failed at `sandbox_apply` with EPERM. Codex now
   selects `danger-full-access` only when the request carries the kernel's outer
   isolation. Requests without outer isolation retain `workspace-write`. The outer
   profile, path grants, configuration protections and hook trust check are unchanged.
   R2 successfully executed `pwd`, filename-only discovery and a targeted source read.
   The filename discovery's exit 1 meant no matching rule file in the source-only copy.

2. **Recon authoring gap:** `cognitive_handoff` advertised InternalRecon through a
   field named `markdown`, but the kernel requires a strict versioned JSON document.
   The agent also lacked a current template after its findings changed the claim hash.
   Added a read-only `recon_template` operation to the existing authenticated handoff.
   It returns a fresh, intentionally incomplete template to an active task-wide lead
   at Research/Design, with an `observed` result and no ledger events. It preserves null
   assessment domains for the producer to classify. Tool guidance now explains filling
   the report, consulting lessons and serializing the complete document into the existing
   artifact field. The host does not classify claims, waive consultation or accept stale
   hashes. Artifact command/replay rules remain unchanged.

## Governed observations

Temporary ledger root: `/tmp/ailedger-governed-roslyn-trial/.ailedger/tasks`.
Task: `2026-10-07_2258-roslyn-container-recon`. Subject `recon`, PlanningLead; Codex
`0.155.0-alpha.16.3`; ordinary fresh provider launches with 900-second limits, no work
items or implementation assurance binding.

| Run | Outcome | Wall time | Uncached input | Cached input | Output |
| --- | --- | ---: | ---: | ---: | ---: |
| R1 | Navigation worked; artifact refused; blocked routing recorded | 178.5 s | 97,255 | 1,367,040 | 4,493 |
| R2 | Navigation, shell and producer-owned recon filing succeeded | 120.2 s | 94,034 | 744,832 | 3,447 |

These are measured token counts, not prices or evidence of improved productivity.
R2's Roslyn solution load took 13.52 seconds. The preceding direct container load took
15.21 seconds. No native comparison under equivalent isolation was available.

R2 produced artifact
`host-d193f123485edb36f4348fdd03c5a8cd6bf980accdb0488ef627162b9ee65107`, event 34,
and a Proceed routing assessment explicitly limited to returning the bounded recon.
The record contains five claims, ten evidence records, three alternatives and one
InternalRecon. Claims remain open; the task remains Research. No stage transition,
implementation, independent assurance, acceptance or original-task completion is claimed.
After run completion, read-only inspection confirmed that the artifact still matched the
current claim-set hash and classified all five claims as internal.

Both worker containers exited and were removed. R2's cleanup receipt confirms exit 0,
container removal and unchanged source snapshot. The earlier R1 cleanup check initially
reported unknown removal because it matched Docker's error text case-sensitively;
independent Docker inspection showed no container, and the prototype check was corrected.
No probe containers remain. The stopped container from the failed initial image build
was removed as well.

## Validation

- Eight prototype boundary tests passed: scope, prefix sibling, symlinks, unsupported
  tools, stale inputs, session reuse and returned path translation.
- Real fixture definitions/references passed; a modified snapshot was refused, a fresh
  worker observed the edit, and a deliberately paused container was removed in 5.16 s.
- Container probes confirmed host source/cache/root writes were denied and the host
  ledger and Docker socket were absent.
- Candidate CLI build passed with zero warnings/errors.
- Focused handoff/provider/assurance/inspection suite: 269 passed, one failed out of 270.
  The failure was the existing inspection telemetry count under concurrent load
  (27 rows versus 36 for Claude); the new template/confinement tests passed.
- An initial default full suite was interrupted after unusually prolonged execution;
  before interruption it reported cancellation-order and inspection-telemetry failures.
  It has no valid final total. Four isolated cases covering those two failures passed.
- The three isolated assurance stdio cases also passed after the serialized run reported
  a telemetry-unavailable line on stderr for its operator case.
- Final serialized suite: 2,714 passed, one failed out of 2,715. The main suite
  contributed 2,615 passes and one failure in 16 m 48 s; memory tests added 99 passes.
  The failure was the existing
  `AssuranceBoundaryTests.ActualStdioClientDiscoversScopedToolsCallsThemAndCannotSpoofIdentity`
  operator case: its empty-stderr assertion encountered a telemetry-unavailable record.
  All three cases of that test passed in isolation, as noted above.

Transport journals intentionally bound telemetry collection to 250 ms, including gate
wait, and report unavailable collection on stderr. These timing-sensitive failures were
retained; assertions and unrelated telemetry behavior were not weakened to make the
experiment appear green.

Local detailed evidence: `/tmp/ailedger-governed-roslyn-trial/`,
`/tmp/ailedger-roslyn-container-smoke-final/`, and `/tmp/ailedger-roslyn-*.log`.
These temporary records may be removed by host cleanup; the outcome and exact identifiers
are preserved here, while the original test ledger remains authoritative for the trial.

## Adoption decision

An optional isolated worker is technically viable for this tested snapshot. It should
not become a mandatory runtime from this evidence. Before normal launches can use it,
the existing provider host must own snapshot/dependency preparation, worker lifecycle,
refresh after edits, path mapping, bounded diagnostics, image provisioning and cleanup.
The current script rejects snapshot changes and requires a fresh worker; it does not
silently serve old code. Native Roslyn still has the original MSBuild IPC incompatibility.

The reproducible prototype is in [`scripts/roslyn-container-probe`](../scripts/roslyn-container-probe/README.md).
