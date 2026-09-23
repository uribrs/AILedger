# Optional C# navigation

Run `sh scripts/install-roslyn.sh` to install Roslyn CodeLens MCP **2.18.1** into
`~/.ailedger/tools/roslyn/2.18.1`. The tool requires .NET 10. Run `sh scripts/install.sh`
after building adapter changes so the installed AILedger uses them. Wait for active provider
runs to finish before replacing the installed tool.

Governed **Codex** launches add eleven navigation tools to their isolated configuration when
the executable exists. A small AILedger stdio bridge starts Roslyn with no solution loaded.
The agent identifies the target repository and language with a narrow CLI search, then calls
`load_solution` with the absolute `.sln` or `.slnx` path for C# work. It can load multiple
solutions and return to one using `set_active_solution` with the exact absolute path in
its `name` argument. Confirm the active path, loaded projects and skipped projects with
`list_solutions`. The launch working directory no longer chooses a solution.

Task-wide research runs can launch from the ledger's repository and use explicit `--add-dir`
grants for other repositories; no work item is needed just to select a C# solution. The bridge
accepts solutions inside the launch working directory or its additional directory grants,
excluding the authoritative ledger storage directory itself. It resolves directory and file
symlinks before checking containment. Loading waits for completion; after any failed selection,
semantic calls are blocked until a successful load or selection, preventing accidental queries
against the preceding repository.

No workspace scan, indexing or download happens until a solution is selected. Standalone
project files, missing installs and non-C# work retain CLI navigation. Claude currently retains
its CLI navigation: this bridge is wired into the Codex provider, not interactive MCP settings.

`AILEDGER_ROSLYN_EXECUTABLE` can select an absolute executable path. Set it to an empty
string to disable Roslyn. A value in the launch request's environment takes precedence over
the operator process environment. An override is operator-trusted executable configuration;
the default installation is pinned, while override versions are the operator's responsibility.

Only solution loading/selection/listing, symbol search/definitions/references/callers, overloads,
member source, test discovery and workspace rebuild are exposed. Refactoring, code execution
through tool actions, analyzer trust and the other upstream tools are not enabled. The bridge
rejects direct calls to tools outside the allowlist as well as filtering tool discovery.
The allowlisted tools are preapproved for noninteractive use; other tool approvals are unchanged.
Existing sandbox and reviewer isolation controls remain in place. Roslyn loads the selected
solution through MSBuild; use trusted repositories, just as for a local build. The directory
check bounds solution selection; it is not a filesystem sandbox for transitive project/package
reads or MSBuild imports. Per-launch bridge settings are trusted launch configuration.

Agents receive concise guidance to prefer Roslyn for targeted C# questions, refresh after
edits or checkout changes, and use CLI search for other languages or failed/suspect queries.
Check for skipped projects; empty references do not prove absence. `find_callers` expects
`Type.Method`, not an overload signature. Transitive test discovery is optional and can be
expensive. A startup failure is non-fatal, with a 30-second timeout; CLI navigation remains
available. Tool calls have a 60-second timeout; the bridge stops a stalled backend after
55 seconds and terminates its child process when the connection closes.

This does not change kernel stages, certify discovery completeness, configure interactive
Codex sessions, or establish token savings. It allows semantic queries to replace repeated
file searches. The allowlist uses Codex's supported
[MCP configuration](https://developers.openai.com/codex/mcp).
