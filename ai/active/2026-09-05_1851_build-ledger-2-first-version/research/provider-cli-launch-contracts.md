# Provider CLI Launch Contracts

## Research frame

- **Task type:** implementation.
- **Decision supported:** freeze the provider-neutral `AgentLaunchRequest` / `AgentRunResult` contracts and the Codex/Claude command construction used by the first Ledger 2 runtime.
- **Targets tested:** Codex CLI `0.150.0-alpha.8` at `/Applications/ChatGPT.app/Contents/Resources/codex`; Claude Code `2.1.260` at `/Users/user/.local/bin/claude`.
- **Documentation status:** public official docs available for both products.
- **Instruction limitation:** the installed and source copies of `technical-researcher` do not contain the referenced `references/task-emphasis.md`, `research-standards.md`, or `output-contract.md`. This report therefore follows the main `SKILL.md` requirements and uses a decision-oriented structure, but cannot reproduce the missing exact output template.

## Decision

Implement both adapters as direct child-process invocations using `ProcessStartInfo.ArgumentList`, redirected stdin/stdout/stderr, and an explicit working directory. Never construct a shell command string. Use each provider's newline-delimited event stream as the runtime protocol, persist the provider-issued session identity, and resume only by that exact identity.

The supported version-pinned commands are:

```text
# Codex — new
codex exec --json --color never --strict-config --sandbox workspace-write --cd <workspace> -

# Codex — resume
codex exec --strict-config --sandbox workspace-write --cd <workspace> resume --json <thread-id> -

# Claude — new
claude -p <short-bootstrap-prompt> --output-format stream-json --verbose
  --forward-subagent-text --session-id <ledger-generated-uuid>
  --permission-mode acceptEdits --permission-prompts none
  --settings <sandbox-settings-json>

# Claude — resume
claude -p <short-bootstrap-prompt> --resume <session-id>
  --output-format stream-json --verbose --forward-subagent-text
  --permission-mode acceptEdits --permission-prompts none
  --settings <sandbox-settings-json>
```

`<sandbox-settings-json>` should be passed as one argument, not through a shell:

```json
{
  "sandbox": {
    "enabled": true,
    "failIfUnavailable": true,
    "autoAllowBashIfSandboxed": true,
    "allowUnsandboxedCommands": false,
    "excludedCommands": []
  }
}
```

Send the complete assembled context on stdin. For Codex, `-` explicitly makes stdin the prompt. For Claude, keep only a short invariant instruction in the argument list (for example, “Use the complete Ledger context supplied on stdin”) and pipe the manifest/rendered context; official non-interactive documentation supports piped stdin and documents a 10 MB cap. Fail before launch if the encoded Claude input exceeds that limit rather than silently truncating it. [OpenAI non-interactive mode](https://learn.chatgpt.com/docs/non-interactive-mode) [Claude programmatic mode](https://code.claude.com/docs/en/headless)

## Evidence and cross-check

### Codex launch, events, and resume

**Confirmed, high confidence.** OpenAI documents `codex exec` as the stable scripted/CI surface. `--cd` selects the workspace, `--json` emits JSONL, `--sandbox workspace-write` supplies the bounded execution policy, `--output-schema` optionally constrains the final answer, and a prompt of `-` reads stdin. It documents `codex exec resume [SESSION_ID]` with an optional follow-up prompt. [OpenAI CLI reference](https://learn.chatgpt.com/docs/developer-commands?surface=cli)

**Confirmed, high confidence.** The JSONL stream begins with a `thread.started` event containing `thread_id`, followed by turn/item events and a terminal `turn.completed`, `turn.failed`, or `error`. The adapter can therefore acquire the durable provider session identity without scraping local Codex state. [OpenAI non-interactive mode](https://learn.chatgpt.com/docs/non-interactive-mode)

The local `codex exec --help` and `codex exec resume --help` outputs agree on stdin prompts, `--json`, `--output-schema`, explicit session resume, and session persistence unless `--ephemeral` is supplied. Local parsing also established two version-specific details:

- parent options placed before `resume` are accepted, so the adapter can retain `--strict-config`, `--sandbox workspace-write`, and `--cd` while using resume's own `--json` flag;
- `--approve-for-me` cannot be combined with `--sandbox workspace-write` (the CLI exits 2 with a mutual-exclusion error).

The first version should therefore use the documented `workspace-write` sandbox and never add `--dangerously-bypass-approvals-and-sandbox`. Treat `--approve-for-me` as a separate, future opt-in policy after its contract is documented; do not silently substitute it. OpenAI explicitly limits the bypass flag to isolated runners. [OpenAI CLI reference](https://learn.chatgpt.com/docs/developer-commands?surface=cli)

### Claude launch, events, session identity, and resume

**Confirmed, high confidence.** Anthropic documents `claude -p` as its non-interactive surface, `stream-json` as newline-delimited events, and the final `result` message as containing final text, cost, and session metadata. `--session-id` accepts a caller-provided UUID, while `--resume <session-id>` resumes an exact conversation. Claude `2.1.223+` can locate that ID across projects, although Ledger should still run from the governed workspace for deterministic project context. [Claude CLI reference](https://code.claude.com/docs/en/cli-usage) [Claude programmatic mode](https://code.claude.com/docs/en/headless) [Claude sessions](https://code.claude.com/docs/en/sessions)

**Confirmed, high confidence.** `--forward-subagent-text` with `stream-json` exposes main, child, and nested-subagent messages using `parent_tool_use_id`, which is needed to observe the existing pipeline's delegation rather than reducing the run to one opaque final message. The local `2.1.260` help exposes this flag. [Claude programmatic mode](https://code.claude.com/docs/en/headless)

The non-destructive local resume probe with a nonexistent UUID exited 1 and still emitted a valid JSON `result` with `subtype: error_during_execution`, `is_error: true`, the requested `session_id`, zero usage, and an `errors` array. The adapter must therefore determine success from both process exit code and terminal event semantics; a parseable result is not itself success.

### Permission behavior

**Confirmed, high confidence.** Claude's `acceptEdits` mode permits in-workspace edits, while the native Bash sandbox confines command writes to the working directory and controls network access. `sandbox.failIfUnavailable: true` prevents an unnoticed fallback to unsandboxed commands, and `allowUnsandboxedCommands: false` disables the escape hatch. `--permission-prompts none` (available since 2.1.259 and present locally) denies any action that still requires a human rather than hanging an unattended process. [Claude permission modes](https://code.claude.com/docs/en/permission-modes) [Claude sandboxing](https://code.claude.com/docs/en/sandboxing) [Claude CLI reference](https://code.claude.com/docs/en/cli-usage)

Do not use `--dangerously-skip-permissions`. Anthropic reserves it for an externally isolated container or VM, whereas this first version launches directly on the operator's machine. Also do not use `--bare` by default: although Anthropic recommends it for deterministic scripts, it deliberately skips normal skill/config discovery and requires API-key-style authentication instead of local OAuth/keychain authentication, conflicting with the requested locally installed cognitive pipeline. [Claude programmatic mode](https://code.claude.com/docs/en/headless)

## Contracts to freeze

### `AgentLaunchRequest`

Provider-neutral required fields:

- `RunId`, `TaskId`, `ActorId`, `WorkItemId` (when applicable), and launch-vs-resume mode.
- Absolute `ExecutablePath` and absolute `WorkingDirectory`.
- Complete `Prompt`/rendered context as stdin payload; never a shell-escaped command.
- Optional exact `ProviderSessionId`; required for resume and forbidden for a new Codex run. A new Claude run may receive a Ledger-generated UUID.
- Permission profile selected from a closed Ledger enum, initially only `WorkspaceGoverned`; adapters own its provider-specific mapping.
- Optional model and optional final-output JSON Schema. For Codex pass a schema file path via `--output-schema`; for Claude pass the schema text via `--json-schema`. In both cases, continue consuming the event stream and treat schema validation failure as a failed run.
- Optional additional writable/readable directories, cancellation/timeout, and a redacted environment allowlist. Do not accept arbitrary provider arguments in the core contract.

Provider-specific command arguments, sandbox settings, flags, and version checks remain inside provider adapters.

### `AgentRunResult`

Persist at least:

- Ledger run/provider/actor identifiers and the acquired `ProviderSessionId` (`thread_id` for Codex, `session_id` for Claude).
- Start/end timestamps, process exit code, and `Completed`, `Failed`, `Cancelled`, or `ProtocolError` status.
- Final assistant message or structured final output, if present.
- Parsed provider events in arrival order (or durable references to them), plus captured stderr with credential/path redaction.
- Usage/cost metadata when emitted, permission denials, and normalized failure details.
- Effective command contract (executable, non-secret arguments, working directory, adapter version), whether this was a resume, and the detected provider CLI version.

Success requires a zero exit code **and** a successful terminal protocol event. A missing terminal event, malformed JSONL, mismatched session identity, or schema failure is a protocol/run failure even when the process exits zero.

## Implement now

1. Add startup capability probes that execute `<binary> --version` and the relevant `--help` surfaces, then require the tested minimum flags. Store the version in the run record. A future CLI change should fail closed with a clear unsupported-contract error.
2. Use `ArgumentList`, redirected streams, and asynchronous concurrent stdout/stderr draining to avoid deadlocks. Feed stdin, close it, and parse JSONL incrementally.
3. On new Codex runs, obtain and persist `thread.started.thread_id` before accepting later events. On new Claude runs, generate/persist the UUID before process launch and verify every terminal/event session ID matches it.
4. Resume only an explicit saved identity; never use Codex `--last` or Claude `--continue`, because both are directory/history selection mechanisms and can attach the wrong governed run under concurrency.
5. Keep provider transcript stores non-authoritative. Ledger events and state remain the source of governance truth.

## Verify first / accepted limitations

- **Live authenticated smoke tests remain required.** The help and nonexistent-session probes validate parsing and invocation surfaces without spending tokens or mutating a repository, but they do not prove the installed accounts can complete a real turn, use tools, or resume after completion.
- **Codex `0.150.0-alpha.8` is prerelease.** Official documentation describes the current stable `exec` contract, and local help agrees on the selected flags, but capability probing and version capture are mandatory because prerelease behavior can move.
- **Provider event schemas are only partially enumerated in public docs.** Parse the small required envelope defensively, retain unknown events, and ignore unknown fields. Do not deserialize into a closed union that breaks on a new item/event type.
- **Claude configuration is layered.** Explicit sandbox keys should be passed on every run, but managed settings can still impose stricter policy. Surface denials; never attempt to weaken managed policy.

## Most defensible next action

Freeze the neutral request/result fields above, implement the exact version-pinned commands behind a fakeable process runner, and add parser/argument tests from captured representative JSONL. Then perform one operator-authorized, minimal live launch-plus-resume smoke test per provider before claiming runtime integration complete.
