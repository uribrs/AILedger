# External dependencies

Everything this ledger needs that is not a NuGet package or a file in this repository. Collected
here so a move to another machine, another account or another network is one list rather than an
archaeology exercise.

Nothing in this folder is required to run `ailedger` itself. The kernel is one dotnet global tool
with no external service. Each entry below says what stops working without it.

## Required

### .NET SDK 8.0

The kernel targets `net8.0`. `sh scripts/install.sh` packs and installs `ailedger` as a global tool
into `$HOME/.dotnet/tools`, which must be on `PATH`.

Without it: nothing builds and nothing installs.

## Required for provider runs

### `codex` and `claude` CLIs, installed and authenticated

`provider launch` runs one of them inside a governed run. A verifier must come from a different
provider than the work it verifies, so in practice both are needed rather than either.

    codex     /Users/user/.local/bin/codex, codex-cli 0.155.0-alpha.9.2
    claude    Claude Code 2.1.280

Codex authentication is read from `CODEX_HOME`; the launcher builds a temporary `CODEX_HOME` per run
and deletes it at run end, linking authentication without exposing the operator's full configuration.

Without them: `provider launch` refuses, and no work item can be completed, because completion
requires a working run and a verifier run.

## Guarded C# navigation for governed Codex and Claude runs

### Roslyn CodeLens MCP 2.18.1 and .NET 10

Install with `sh scripts/install-roslyn.sh`. The pinned tool lives in
`~/.ailedger/tools/roslyn/2.18.1`; .NET 10 must be installed separately. This requirement
is additional to the kernel's .NET 8 target. NuGet access is needed for installation,
and the analyzed solution's package dependencies must be restored for reliable results.

Both adapters supply an explicit eleven-tool navigation allowlist in temporary configuration.
The stdio bridge starts Roslyn empty and checks on-demand solution selections against navigation
directories. Scoped workers can load their repository's solution without receiving broader
provider write grants. No interactive MCP registration is needed. Both providers also receive
a PreToolUse hook blocking covered C# shell/Grep searches without a recorded Roslyn failure.
Codex's app-server hook metadata API must support scoped trust; unsupported versions fail
preflight. Hooks were execution-tested on the CLI versions listed above using local mock APIs.

Non-C# searches and targeted source reads retain CLI access. C# content searches require Roslyn;
an observed bridge failure permits one scoped CLI fallback for ten minutes. Missing Roslyn is
a dependency failure, not an unrestricted opt-out. Unsupported standalone-project layouts need
a solution for semantic navigation. The kernel's non-navigation commands do not require Roslyn.
CLI navigation uses shell tools such as `rg`; Git is needed for the repository workflow,
and the Codex adapter requires a git checkout.

See [Roslyn navigation](../docs/roslyn-navigation.md) for supported layouts, overrides,
refresh requirements, hook coverage limits, timeouts and fallback rules. This integration has been tested;
token savings have not been established. Graphify and Serena trials are not production
dependencies and are not wired into provider launches.

## Package dependencies

NuGet package versions are declared in the individual `.csproj` files under `src/`,
`tests/` and `tools/GovernedTests/`; `dotnet restore` resolves their transitive dependencies.
The external tools listed here supplement those package declarations.

## Required only for semantic memory (not yet activated)

The index at `src/AILedger.Memory` is dormant. Nothing in the kernel reads it, and a machine without
these gets tag-and-recency recall, which is what every task has used to date.

### Ollama, running locally

    endpoint    http://127.0.0.1:11434/   loopback only, enforced in code
    used at     index rebuild, and once per recall to embed the query

The adapter refuses a non-loopback endpoint and requires `--confirm-local`, so document text never
leaves the machine.

Without it: recall falls back to tag-and-recency and governed work continues.

### An embedding model

    mxbai-embed-large:latest    669 MB    1024 dimensions    ~512 token context

The model's build digest is stored alongside every vector, because vectors from different models are
not comparable. **Changing the model requires a full rebuild**, and the digest is what makes that
detectable rather than silently wrong.

    ollama pull mxbai-embed-large

#### Known failure on this network

`ollama pull` fails with a digest mismatch:

    Error: digest mismatch, file must be downloaded again:
    want sha256:819c...  got sha256:cf31...

Cause: a transparent network proxy ignores Ollama's HTTP `Range` requests and returns the entire
file with `200 OK`. Ollama treats those responses as partial chunks, assembles invalid bytes, and
correctly rejects the resulting SHA-256. This matches an open Ollama bug and is **not** a corrupt
mirror — it reproduced on two different models, twice each, for two different operators.

Workarounds, in order of preference:

1. Switch network or VPN so the transparent proxy is not in the path.
2. Bypass the multipart path: download the blob sequentially, verify its expected SHA-256 by hand,
   and install it. This is what unblocked us on 2026-09-09.

Diagnosed and fixed by codex; the record is `2026-09-09_1944-ollama-digest-mismatch`.

## Repository indexing boundary

The kernel does not require a persistent repository-content index. Optional Roslyn navigation
loads a solution for the provider session; its workspace must be refreshed after edits or
checkout changes. This supplements CLI navigation and is separate from the optional ledger
semantic-memory service described above.
