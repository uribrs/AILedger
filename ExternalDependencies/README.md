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

    codex     /Users/user/.local/bin/codex, codex-cli 0.151.0-alpha.7.2
    claude    Claude Code 2.1.266

Codex authentication is read from `CODEX_HOME`; the launcher builds a temporary `CODEX_HOME` per run
and deletes it at run end, copying credentials in rather than exposing the operator's own home.

Without them: `provider launch` refuses, and no work item can be completed, because completion
requires a working run and a verifier run.

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

## Not a dependency, deliberately

**Repository content is not indexed.** Ledger records are append-only, so an index over them cannot
go stale, and no existing tool searches them semantically. Repository content is mutable, so an
index over it is stale between every commit, and `grep` already answers it exactly and for free.
Recorded on task `2026-09-09_2118-memory-index-activation`.
