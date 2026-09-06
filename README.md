# AILedger 2.0

AILedger 2.0 v0.1 is a local, governed task kernel for coordinating AI-assisted work. It preserves the existing AILedger skill methodology as a verified cognitive snapshot, while moving durable truth, authority, lifecycle, causal invalidation, context isolation, and provider-run bookkeeping into deterministic .NET mechanisms.

The operator remains the authority. Codex and Claude provide cognition through provider adapters; neither provider owns task truth or may silently expand its role. The append-only task event log is authoritative, and generated JSON/Markdown files make the current state easy for tools and people to inspect.

This release is an explicit CLI, not an autonomous dispatcher. An operator invokes each context build, provider launch, resume, and lifecycle transition. It does not automatically wake Codex or Claude when an event is appended.

## Quick start

Requirements:

- .NET 8 SDK
- For live provider runs, an installed and authenticated `codex` or `claude` CLI with the capabilities described in [Provider operation](docs/operator-guide.md#provider-operation)

Build and test:

```bash
dotnet restore AILedger.sln
dotnet build AILedger.sln --no-restore
dotnet test AILedger.sln --no-restore
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

By default task workspaces are written under the platform-local application-data directory at `AILedger/tasks`. Use `--root PATH` on every invocation to select another root. The Ledger root and every provider-writable directory must be fully disjoint; neither may contain the other.

v0.1 is a cooperative single-user tool: actor IDs are audited attribution, not authenticated identities against other processes running as the same OS account. Each local task is capped at 1,000 events and a 16 MiB event log while persistence uses atomic full-history replacement and replay. See the architecture and operator guides for the exact trust and scaling boundaries.

## Project shape

- `cognitive/` — hash-verified snapshot of the six original AILedger skills and governing rules
- `src/AILedger.Core/` — commands, events, governance, lifecycle, invalidation, and context assembly
- `src/AILedger.Storage/` — single-writer file persistence, replay, recovery, and readable projections
- `src/AILedger.Providers/` — defensive Codex and Claude process adapters
- `src/AILedger.Cli/` — explicit operator command surface
- `tests/AILedger.Tests/` — core, storage, provider, CLI, concurrency, and two-lead behavior

The original design dossiers remain at the repository root as design inputs. The implemented v0.1 behavior is documented here and in:

- [Architecture and governance](docs/architecture.md)
- [Operator guide and CLI reference](docs/operator-guide.md)

## Verification status

The recorded implementation run reports 121 automated tests passing. After child-environment isolation was introduced, authenticated, non-destructive new-session and exact-session-resume smoke tests passed on 2026-09-05 with Codex CLI `0.150.0-alpha.8` (`CR4`/`CR5`) and Claude Code `2.1.261` (`CL8`/`CL9`). Other CLI versions remain guarded by runtime capability probes rather than assumed compatible.
