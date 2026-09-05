# Execution Notes

- 2026-09-05T18:51:52Z — Contract created. No implementation has started.
- 2026-09-05T18:56:00Z — Internal recon completed; seven disjoint sets require decomposed execution.
- 2026-09-05T19:00:58Z — Provider CLI research completed and A2 validated.
- 2026-09-05T19:03:00Z — Orchestration plan completed; phase-zero shared contract freeze started.

## W0

- Created the .NET 8 solution, project graph, deterministic build defaults, and repository-scoped NuGet configuration.
- Froze provider-neutral identifiers, governance models, commands, causal events, state/reducer boundary, serialized single-writer service boundary, context manifest, and process-adapter contracts.
- `dotnet build AILedger.sln --no-restore` passed with zero warnings and errors.

## W1

- Copied all six canonical skill directories from Git object bytes at `5bdb62717f8da0b33075b704b519a9e80a4e8490` into `cognitive/skills/`.
- Created `cognitive/manifest.json`; the eight skill files and governing `RULES.md` match the source commit and manifest SHA-256 inventory exactly.

## W4

- Implemented a shell-free asynchronous process runner with redirected stdin/stdout/stderr and timeout cancellation.
- Implemented version/capability probes, fail-closed Codex and Claude command construction, exact-session resume, JSONL normalization, terminal success checks, and stderr secret redaction.
- `dotnet build src/AILedger.Providers/AILedger.Providers.csproj --no-restore` passed with zero warnings and errors.

## W2

- Implemented the governed task reducer, command handler, operator-only authority assignment, lifecycle transitions, causal invalidation, work/run invariants, and deterministic role-aware context assembly.
- `dotnet build src/AILedger.Core/AILedger.Core.csproj --no-restore` passed with zero warnings and errors.

## W3

- Implemented safe workspace resolution, per-task in-process and cross-process mutation locks, append-only JSONL, authoritative replay, stale-state recovery, atomic projections, history streaming, and Markdown views.
- `dotnet build src/AILedger.Storage/AILedger.Storage.csproj --no-restore` passed with zero warnings and errors.
- Combined W0-W4 solution build passed with zero warnings and errors.

## W5

- Implemented dependency-free CLI parsing and composition for task, truth, work, run, context, and provider operations.
- Added safe role defaults, verified cognitive-manifest loading, exact-session launch/resume, and run lifecycle recording.
- CLI build and state-command smoke flow passed.

## W6

- Added 44 independent xUnit tests covering core, storage, providers, CLI, and two-lead end-to-end behavior.
- R1-R5 production-path tests are present and pass.
- `dotnet test AILedger.sln --no-restore` passed: 44 passed, 0 failed.

## W7

- Added the root README, architecture reference, and operator guide against the implemented command and persistence surfaces.
- Independently verified the CLI help surface and all eight initial skill snapshot hashes.

## Integrated verification

- Exercised task open, actor attachment, truth mutation, role-aware context build, status, and history through the compiled CLI against a temporary task root.
- Authenticated Codex CLI `0.150.0-alpha.8` new-session run `CR1` and exact-session resume `CR2` completed with session `01a0730b-a289-7422-930f-45b16b22defc` and output `LEDGER_SMOKE_OK`.
- Authenticated Claude Code `2.1.260` new-session run `CL4` and exact-session resume `CL5` completed with session `fae34c2f-f571-48d2-9eac-f80f0e735fa0` and output `LEDGER_SMOKE_OK`.
- The Claude smoke exposed ambient MCP and skill discovery. The adapter was hardened with `--strict-mcp-config` and `--disable-slash-commands`; the repeated live stream reported no MCP servers, slash commands, or skills while the governed context still executed successfully.
- The final `dotnet test AILedger.sln --no-restore -m:1` run passed: 44 passed, 0 failed. `git diff --check` passed.

## Verifier repair 1

- Mapped the `Verifier` role to the verifier protocol embedded in `task-orchestrator` and added production-path core and CLI context tests.
- Copied the governing `RULES.md` verbatim from source commit `5bdb62717f8da0b33075b704b519a9e80a4e8490`, added it to the hash manifest, and stopped labeling manifest metadata as behavioral rules.
- Made any conflicting provider session identity permanently fail the run and added a mixed-session Claude protocol test.
- Replaced shallow Codex probing with explicit `exec --help` and `exec resume --help` capability probes and test assertions.
- Repaired the attention-item table schema required by the orchestration methodology.
- `dotnet test AILedger.sln --no-restore -m:1` passed after repairs: 47 passed, 0 failed; `git diff --check` passed.
- A focused CLI verifier-context smoke emitted `rules/governing-rules`, `skill/task-orchestrator`, and its orchestration rubric, with no unrelated skill.

## Code-review repair 1

- Replaced direct event-log append with a flushed temporary-history batch and atomic rename commit point; added event identity/order validation.
- Made derived-state writes best-effort after commit and made reads repair every Markdown projection independently of `state.json` freshness.
- Snapshot history under the mutation lease and release it before yielding to consumers.
- Wired Ctrl+C/SIGTERM cancellation and used a fresh ten-second cleanup token to close interrupted provider runs as `Cancelled`.
- Required claim-resolution evidence to directionally support validation or refute rejection.
- Bounded process lines, per-stream output, and combined retained provider output; overflow terminates the child and returns a protocol error.
- Added production-path recovery, sequence, history-lock, cancellation, evidence-direction, adapter-output, and real runaway-process tests.
- `dotnet test AILedger.sln --no-restore -m:1` passed after repairs: 56 passed, 0 failed; `git diff --check` passed.

## Verifier repair 3

- Moved successful provider terminal persistence to the same fresh, bounded completion path used for failure compensation; added cancellation-after-success coverage.
- Enforced non-empty actor/correlation, meaningful timestamps, deterministic event identities, and prior same-task causation both before commit and during replay; added corruption and poison-write tests.
- Made all non-fatal derived-writer failures non-ambiguous after the event-log commit and added non-I/O failure coverage.
- Corrected provider-output documentation units from bytes/MiB to characters.
- `dotnet test AILedger.sln --no-restore -m:1` passed after this repair set: 63 passed, 0 failed; `git diff --check` passed.
- Claude auto-updated during verifier pass 4. Authenticated Claude Code `2.1.261` launch `CL6` and exact-session resume `CL7` both completed with session `487b780d-c150-4c6b-aaef-7552aa592d1e` and output `LEDGER_SMOKE_OK`; the stream reported no MCP servers, skills, or slash commands.

## Code-review repair 2

- Rejected numeric/undefined enum values in CLI parsing, core command handling, and persisted JSON replay.
- Enforced operator-role authority for privileged capability assignment and governed scope creation at the Core boundary, not only in CLI defaults.
- Separated provider stdout/stderr probe buffers and made version precedence deterministic under concurrent callbacks.
- Made terminal provider failures return CLI exit code 3 only after their failure status is durably recorded.
- Bounded cross-process lock acquisition to 30 seconds and guaranteed in-process semaphore release if file-lock disposal fails.
- Added CLI, core, replay, authorization, concurrency-probe, and provider-exit tests.
- `dotnet test AILedger.sln --no-restore -m:1` passed after this repair set: 73 passed, 0 failed; `git diff --check` passed.

## Code-review repair 3

- Bound provider working and additional directory grants to existing, canonicalized directories inside the selected operator-defined work scope; rejected sibling-prefix and symbolic-link escapes, and restricted unscoped launches to operators.
- Prevented rejected/superseded claims from acquiring later decision/work dependents or starting work, and preserved `Blocked`/`Stale` work status when an already-running provider completes.
- Made process failure cleanup cancel, kill, reap the child, and observe every active I/O task before rethrowing; adapter results now snapshot event collections.
- Applied the 30-second acquisition deadline to both the in-process semaphore and file-lock phases.
- Added scope-escape, temporal dependency, invalidation-preservation, and real process callback-quiescence tests.
- `dotnet test AILedger.sln --no-restore -m:1` passed after this repair set: 77 passed, 0 failed; `git diff --check` passed.

## Verifier repair 6

- Canonicalized every CLI resource scope at work-item creation and persisted it as an absolute, symbolic-link-resolved directory rather than rebinding relative text during a later provider launch.
- Required absolute scopes at the Core command boundary so non-CLI callers cannot persist location-ambiguous grants; provider launch now rejects legacy relative scopes.
- Added Core and production CLI regression tests for the invariant and updated the operator documentation.
- `dotnet test AILedger.sln --no-restore -m:1 --disable-build-servers` passed after this repair set: 79 passed, 0 failed; `git diff --check` passed.

## Code-review repair 4

- Moved the default Ledger root to platform-local application data and rejected provider directories that contain the authoritative Ledger root. Documented v0.1's cooperative single-OS-principal trust model: actor IDs are audited attribution, not caller authentication or tamper resistance.
- Enforced work-owner/run-actor authorization in Core for both direct commands and provider-mediated runs.
- Replaced ambient child environment inheritance with a small operational allowlist, redacted explicit secrets from retained stdout JSON/final output/stderr, and strengthened provider terminal protocol validation.
- Made process cleanup always attempt kill and bounded reap, preserve the initiating failure when cleanup succeeds, and surface an explicit cleanup failure if the process remains alive.
- Enforced a bounded v0.1 ledger of 1,000 events and 16 MiB so full-history replacement/replay cannot be presented as unbounded storage.
- Added command-specific option allowlists so safety-relevant typos fail before service creation or mutation.
- Added Core, CLI, storage, protocol, redaction, environment-isolation, and cleanup tests. `dotnet test AILedger.sln --no-restore -m:1 --disable-build-servers` passed: 98 passed, 0 failed; `git diff --check` passed.
- A fresh authenticated provider smoke was requested because the child environment changed. The host approval gate rejected transmission of the generated governed context without explicit user authorization, so post-change live Codex/Claude evidence remains pending.
