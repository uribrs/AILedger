# AILedger 4.0

AILedger 4.0 is a local, governed task kernel for coordinating AI-assisted work. It preserves the existing AILedger skill methodology as a verified cognitive snapshot, while moving durable truth, authority, lifecycle, causal invalidation, context isolation, and provider-run bookkeeping into deterministic .NET mechanisms.

The release generations describe the system's evolution:

- **1.0:** the original skills-based methodology.
- **2.0:** the original kernel with a pure CLI interface.
- **3.0:** separated interfaces and triggering.
- **4.0:** the current kernel, including structured agent interactions, governed assurance and native host services.

The current release is **4.0.7**. `Directory.Build.props` declares the version used by builds and
`scripts/install.sh`; advance that version for the next release. Reinstalling a release keeps its
number. `ailedger version` also reports the source commit, and build metadata retains the build time
and dirty-tree marker. Historical logs retain the version labels recorded when they were written.

The operator remains the authority. Codex and Claude provide cognition through provider adapters; neither provider owns task truth or may silently expand its role. The append-only task event log is authoritative, and generated JSON/Markdown files make the current state easy for tools and people to inspect. Closing a task mints durable lessons from governed findings; later tasks recall those lessons into their own opening history and context.

The operator CLI and trusted provider-supplied structured tools share the governed kernel. An operator invokes each context build, provider launch, resume, and lifecycle transition. It does not automatically wake Codex or Claude when an event is appended.

## Quick start

Requirements:

- .NET 8 SDK
- For live provider runs, an installed and authenticated `codex` or `claude` CLI with the capabilities described in [Provider operation](docs/operator-guide.md#provider-operation)
- Nothing else. The kernel is one dotnet global tool and depends on no external service.

Guarded C# navigation for governed Codex and Claude runs uses Roslyn CodeLens MCP 2.18.1 and .NET 10.
See [setup and fallback behavior](docs/roslyn-navigation.md); other-language navigation uses
authorized CLI tools.

The semantic memory index at `src/AILedger.Memory` is **dormant and optional**. Activating it would
add a local Ollama service and a 669 MB embedding model, and `ollama pull` currently fails on this
network for a reason worth knowing before you try. Every dependency, what stops working without it,
and that failure and its workaround are in
[ExternalDependencies](ExternalDependencies/README.md) — kept as one list so a move to another
machine is not an archaeology exercise.

Build and test:

```bash
dotnet restore AILedger.sln --artifacts-path /tmp/ailedger-build
dotnet build AILedger.sln --artifacts-path /tmp/ailedger-build --no-restore
dotnet test AILedger.sln --artifacts-path /tmp/ailedger-build --no-restore
```

Run the CLI from the repository root:

```bash
dotnet run --project src/AILedger.Cli -- --help
```

Open a task and attach two leads:

```bash
dotnet run --project src/AILedger.Cli -- task open \
  --task demo-1 --actor operator --title "Provider design" --goal "Produce a governed implementation"

dotnet run --project src/AILedger.Cli -- actor attach \
  --task demo-1 --actor operator --target codex-lead --role planning-lead

dotnet run --project src/AILedger.Cli -- actor attach \
  --task demo-1 --actor operator --target claude-lead --role implementation-lead

dotnet run --project src/AILedger.Cli -- status --task demo-1
```

By default, the CLI walks upward to find a `.ailedger` directory, using its `tasks` directory and the adjacent `<home>/lessons` store. Without a discovered home it falls back to platform-local application data under `AILedger`. Explicit `--root PATH` and `--lesson-root PATH` override those locations. Provider scopes inside the authoritative ledger root are refused; a workspace containing the ledger is supported for self-hosting.

The kernel is a cooperative single-user tool: actor IDs are audited attribution, not authenticated identities against other processes running as the same OS account. Each local task is capped at 10,000 events and a 64 MiB event log while persistence uses atomic full-history replacement and replay. See the architecture and operator guides for the exact trust and scaling boundaries.

## Agent interactions

Trusted launches supply seven structured recording and inspection tools. Use the [interaction guide](docs/operator-guide.md#structured-agent-interactions) for tool selection, retries and CLI fallbacks. The bounded [task-13 assurance path](docs/handoff-assurance-v1.md) is the default for supported implementation work and adds five explicitly granted tools; acceptance is separate from governed completion. The [coverage map](docs/structured-inspection-v1.md) identifies remaining CLI dependencies. Task 12 remains opt-in, offline and read-only.

## Project shape

- `cognitive/` — hash-verified snapshot of the six original AILedger skills and governing rules
- `src/AILedger.Core/` — commands, events, governance, lifecycle, invalidation, and context assembly
- `src/AILedger.Storage/` — single-writer file persistence, replay, recovery, and readable projections
- `src/AILedger.Providers/` — defensive Codex and Claude process adapters
- `src/AILedger.Cli/` — explicit operator command surface
- `tests/AILedger.Tests/` — core, storage, provider, CLI, concurrency, and two-lead behavior

The original design dossiers remain at the repository root as design inputs. The implemented behavior is documented here and in:

- [Architecture and governance](docs/architecture.md)
- [Operator guide and CLI reference](docs/operator-guide.md)

## Verification status

Task 13 delivery recorded 2,285 main tests and 99 memory tests passing in the repository runner, plus installed scripted assurance/inspection/artifact/episode probes and frozen measurement checks. See the [validation record](docs/handoff-assurance-v1/validation.md). Tasks 7–13 client trials remain pending; scripted verification does not establish improved judgment, delivery time or cost. After child-environment isolation was introduced, authenticated, non-destructive new-session and exact-session-resume smoke tests passed on 2026-09-05 with Codex CLI `0.150.0-alpha.8` (`CR4`/`CR5`) and Claude Code `2.1.261` (`CL8`/`CL9`). Other CLI versions remain guarded by runtime capability probes rather than assumed compatible.
