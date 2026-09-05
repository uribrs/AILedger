# Operator guide

## Setup

Install the .NET 8 SDK, then from the repository root run:

```bash
dotnet restore AILedger.sln
dotnet build AILedger.sln --no-restore
dotnet test AILedger.sln --no-restore
```

Use the CLI through the project:

```bash
dotnet run --project src/AILedger.Cli -- --help
```

Examples below omit `--root`. Its default is the platform-local application-data directory under `AILedger/tasks`; when using another root, pass the same `--root PATH` to every command. Keep that root outside every provider work scope.

## Cognitive snapshot and manifest verification

`cognitive/manifest.json` identifies the source repository commit and SHA-256 of every copied skill and governing-rules artifact. `context build`, `provider launch`, and `provider resume` load that manifest and verify every Markdown artifact before use. A missing file, a path escaping the cognitive root, or a hash mismatch fails closed.

The CLI discovers `cognitive/` by walking upward from the current directory or executable directory. Override it with `--cognitive-root PATH` or `AILEDGER_COGNITIVE_ROOT`.

For an independent macOS hash check from the repository root:

```bash
jq -r '.files[] | [.path, .sha256] | @tsv' cognitive/manifest.json \
  | while IFS=$'\t' read -r artifact expected; do
      artifact_path="cognitive/$artifact"
      test -f "$artifact_path" || artifact_path="cognitive/skills/$artifact"
      actual=$(shasum -a 256 "$artifact_path" | awk '{print $1}')
      test "$actual" = "$expected" || exit 1
    done
```

Treat snapshot edits as versioned methodology changes: edit deliberately, update the manifest hash inventory and provenance, and run the full test suite. Do not quietly patch copied skill text to make a context build pass.

## Governed task example

Open a task. The opening actor becomes its operator:

```bash
dotnet run --project src/AILedger.Cli -- task open \
  --task task-123 --actor operator --title "Adapter change" --goal "Implement and verify the change"
```

Attach actors using safe defaults:

```bash
dotnet run --project src/AILedger.Cli -- actor attach \
  --task task-123 --actor operator --target codex-plan --role planning-lead

dotnet run --project src/AILedger.Cli -- actor attach \
  --task task-123 --actor operator --target claude-impl --role implementation-lead
```

If any `--capability` is supplied, the repeated values replace the role defaults rather than extending them. Prefer defaults unless intentionally creating a narrower role.

Record truth and scoped work:

```bash
dotnet run --project src/AILedger.Cli -- claim add \
  --task task-123 --actor codex-plan --id C1 \
  --statement "The provider emits structured terminal output" \
  --consequence "Run success cannot be established"

dotnet run --project src/AILedger.Cli -- evidence add \
  --task task-123 --actor claude-impl --id E1 --source-type local-probe \
  --citation "provider --help" --summary "Structured output is supported" --supports C1

dotnet run --project src/AILedger.Cli -- claim resolve \
  --task task-123 --actor codex-plan --id C1 --status validated --evidence E1

dotnet run --project src/AILedger.Cli -- work add \
  --task task-123 --actor operator --id W1 --title "Implement adapter" \
  --owner claude-impl --depends-on C1 --scope src/AILedger.Providers
```

Build and inspect the exact context intended for an actor:

```bash
dotnet run --project src/AILedger.Cli -- context build \
  --task task-123 --actor claude-impl --work W1 --output /tmp/task-123-claude-context.json
```

Inspect state and causal history:

```bash
dotnet run --project src/AILedger.Cli -- status --task task-123
dotnet run --project src/AILedger.Cli -- history --task task-123
```

`task status` and `task history` are aliases for the last two commands. List-valued options such as `--evidence`, `--supports`, `--depends-on`, `--scope`, and `--add-dir` are repeated once per value.

## Command reference

The implemented command surface is:

```text
task open          --task ID --actor ID --title TEXT --goal TEXT
status             --task ID
history            --task ID
actor attach       --task ID --actor OPERATOR --target ID --role ROLE [--capability CAP]
context build      --task ID --actor ID [--work ID] [--cognitive-root PATH] [--output FILE]
claim add          --task ID --actor ID --id ID --statement TEXT [--consequence TEXT]
claim resolve      --task ID --actor ID --id ID --status STATUS [--evidence ID]
evidence add       --task ID --actor ID --id ID --source-type TYPE --citation TEXT --summary TEXT
                   [--supports CLAIM] [--refutes CLAIM]
decision propose   --task ID --actor ID --id ID --statement TEXT --rationale TEXT
                   [--depends-on CLAIM] [--supersedes DECISION]
decision resolve   --task ID --actor ID --id ID --status accepted|superseded
challenge raise    --task ID --actor ID --id ID --target-type TYPE --target-id ID --reason TEXT
                   [--evidence ID]
challenge dispose  --task ID --actor ID --id ID --status supported|rejected|withdrawn
work add           --task ID --actor ID --id ID --title TEXT [--owner ID]
                   [--depends-on CLAIM] [--scope PATH]
run start          --task ID --actor ID --run ID [--work ID] --provider NAME [--session ID]
run complete       --task ID --actor ID --run ID --status STATUS [--session ID]
stage transition   --task ID --actor ID --stage STAGE
provider launch    --task ID --actor ID --run ID --provider codex|claude [provider options]
provider resume    --task ID --actor ID --run ID --provider codex|claude --session EXACT_ID [provider options]
```

Provider options are `--work ID`, `--executable PATH`, `--working-directory PATH`, `--model NAME`, `--timeout-seconds N`, repeated `--add-dir PATH`, `--cognitive-root PATH`, and `--output-schema VALUE`. Global `--root PATH`, optional `--cause EVENT_ID`, and optional `--correlation ID` may appear with commands. Enum values are case-insensitive and accept hyphenated forms such as `planning-lead`.

## Role defaults

- `Operator`: every capability.
- `PlanningLead`: add/resolve claims, add evidence, propose decisions, raise challenges, manage runs, request transitions, and build context.
- `ImplementationLead`: add claims/evidence, propose decisions, raise challenges, manage runs, request transitions, and build context.
- `Researcher`, `Worker`, `Verifier`, `CodeReviewer`: add claims/evidence, raise challenges, and build context.

Only a suitably authorized actor can resolve decisions or challenges or create governed work. The initial operator has those capabilities.

## Provider operation

The provider executable must be on `PATH`, or supplied as an absolute/relative `--executable PATH` that resolves to a file. The CLI probes `--version` and `--help` before every launch and fails before execution if required capabilities are absent.

The provider must already be authenticated through its local configuration or OS credential store. Provider child processes receive a small operating environment allowlist rather than all of AILedger's ambient variables; unrelated API keys, CI variables, and proxy credentials are not inherited. v0.1's CLI does not expose explicit provider environment injection, so an installation that authenticates only through an environment variable needs a trusted wrapper or a future secret-input mechanism. The implementation was smoke-tested with authenticated new sessions and exact-session resumes using Codex CLI `0.150.0-alpha.8` and Claude Code `2.1.261` on 2026-09-05, before the environment allowlist was introduced. Repeat those non-destructive checks after this change or after upgrading either CLI; capability probes reject missing flags but cannot prove protocol compatibility by themselves.

Launch one actor on one governed work item:

```bash
dotnet run --project src/AILedger.Cli -- provider launch \
  --task task-123 --actor claude-impl --run R1 --work W1 --provider claude \
  --timeout-seconds 1800
```

The result JSON contains the provider session ID. A successfully completed run also completes its work item, so create a new continuation item before resuming that exact provider session:

```bash
dotnet run --project src/AILedger.Cli -- work add \
  --task task-123 --actor operator --id W2 --title "Continue adapter work" \
  --owner claude-impl --depends-on C1 --scope src/AILedger.Providers

dotnet run --project src/AILedger.Cli -- provider resume \
  --task task-123 --actor claude-impl --run R2 --work W2 --provider claude \
  --session EXACT_ID_FROM_R1
```

Do not guess, use a provider's "latest" session, or reuse a run ID. A resume without `--session` is rejected. Codex discovers the new session from its stream; Claude receives a preassigned session ID. Both resume paths require the stream's session identity to match.

Each provider command records the run as active before launching. A setup/adapter error normally records it as failed; a completed process records `Completed`, `Failed`, `Cancelled`, or `ProtocolError` according to its exit and JSONL protocol. Ctrl+C/SIGTERM cancellation uses a fresh bounded cleanup token to record `Cancelled` after stopping the child. The default timeout is 1,800 seconds. A host crash or forced termination that prevents the completion write can still leave an active run behind. Inspect `status`, establish what happened to the provider process, then close it explicitly with `run complete --status failed|cancelled --session ID` using an actor with `ManageRuns`. There is no background orphan-run recovery.

Provider commands return process exit code `0` only for a `Completed` run. A durably recorded `Failed`, `Cancelled`, or `ProtocolError` result is printed as JSON and returns exit code `3`; usage errors return `2`, other handled operational errors return `1`, and caller cancellation returns `130`.

Provider output is bounded: a line over 1,048,576 characters, either stream over 8,388,608 characters, or combined retained output over 8,388,608 characters terminates the child and produces a protocol error. This protects the Ledger process but means exceptionally verbose provider runs must emit smaller results or persist bulky artifacts in the governed workspace.

Explicit invocation secrets are redacted from retained stdout JSON, final output, and stderr. Redaction is a last-resort output safeguard, not a substitute for avoiding secrets in prompts or provider responses.

For a work item, only one pending/active run is allowed. To involve Codex and Claude concurrently, create disjoint work items with explicit owners and directory scopes. At work creation, the CLI resolves every scope from that invocation's directory, follows symbolic links, and stores a canonical absolute path; later launches never reinterpret it against their own current directory. The provider working directory defaults to the first stored scope; an explicit `--working-directory` and every `--add-dir` must exist and resolve inside one of the work item's scopes, including after symbolic-link resolution. A non-operator may launch only work it owns. Only an operator may launch without a work item. v0.1 does not detect overlap between different work items, so the operator must still make those scopes disjoint where concurrency matters.

Codex is launched with strict configuration and `workspace-write` sandboxing. Claude is launched non-interactively with sandboxing required, unsandboxed commands denied, permission prompts disabled, ambient MCP servers excluded, and ambient slash-command skills disabled. Ledger supplies the verified role skills in its context manifest instead. These are provider controls, not independent host enforcement by Ledger.

AILedger v0.1 assumes one cooperative operating-system principal. `--actor` is audited attribution, not authenticated identity, and event files are not cryptographically protected from another process running as that user. Do not place the Ledger root inside a provider scope. If providers or sibling local processes are adversarial, run them behind an isolation boundary and do not rely on v0.1 role checks as a security perimeter.

## Persistence and recovery

Each safe task ID becomes one directory:

```text
<ledger-root>/<task-id>/
├── events.jsonl       authoritative append-only history
├── state.json         materialized machine-readable state
├── task.md            roles, work, runs, and open challenges
├── assumptions.md     claims and evidence references
├── decisions.md       decision status, rationale, and dependencies
└── .writer.lock       per-task mutation lock
```

Task IDs must be safe single directory names; path traversal is rejected, and mutation rejects a task directory that is a symbolic link/reparse point. Writers are serialized in-process and across processes. Each event batch is committed by writing and flushing the complete prior history plus new records to a temporary file, then atomically replacing `events.jsonl`; the history remains logically append-only. This avoids exposing a torn final JSON line after interrupted writes, at the cost of rewriting the local log for each command in v0.1.

Cross-process lock acquisition is bounded to 30 seconds so persistent filesystem failures do not masquerade as contention forever.

To keep this deliberately simple full-replay/full-replacement store within predictable operating bounds, v0.1 rejects a mutation that would take a task past 1,000 events or a 16 MiB event log. Archive or migrate the task before that point. These are enforced limits, not tuning guidance; a later snapshot-plus-tail store can lift them without pretending the current O(n) per-command storage path scales indefinitely.

On `status`, the event log is replayed and event identity/order is validated. If `state.json` is missing, malformed as text, or differs from replayed state, AILedger replaces it. All Markdown projections are regenerated on reads, so a missing or partially updated view heals even when `state.json` is current. Derived-view write failure after the event-log commit does not turn a committed command into an ambiguous failure; the next read retries repair. Corrupt or blank event-log lines fail closed with a line number; never hand-edit `events.jsonl`.

Back up or copy the entire task directory while no writer holds the task lock. Recovery begins with `events.jsonl`; `state.json` and Markdown files are disposable projections. There is no database, remote replication, archive mover, or TTL cleanup in v0.1.

## Lifecycle

The stage sequence supports controlled backward loops:

```text
Discovery → Research → Design → Scope → Ready → Execution → Verification → Review → Learn → Archive
                                      Execution ↔ research/design/scope
                                      Verification ↔ execution/repair
                                      Repair ↔ execution/verification
                                      Review ↔ repair
                                      Learn ↔ review
```

Use `stage transition`; transitions outside the implemented graph fail. Entering `Execution` requires at least one work item. Entering `Archive` requires no active/pending run and no open challenge. Archive is currently only a state transition, not cold storage or deletion.
