# Optional C# navigation

Run `sh scripts/install-roslyn.sh` to install Roslyn CodeLens MCP **2.18.1** into
`~/.ailedger/tools/roslyn/2.18.1`. The tool requires .NET 10. Run `sh scripts/install.sh`
after building adapter changes so the installed AILedger uses them.

Governed **Codex** launches add nine navigation tools to their isolated configuration when
the executable exists and there is one `.sln` or `.slnx` containing a C# project in the
working directory or an ancestor within the git checkout. Nested solution folders can be
selected with the launch working directory. Multiple solutions, standalone project files,
missing installs and non-C# solutions retain CLI navigation. There is no recursive workspace
scan or automatic download during a run. Claude currently retains its CLI navigation.

`AILEDGER_ROSLYN_EXECUTABLE` can select an absolute executable path. Set it to an empty
string to disable Roslyn. A value in the launch request's environment takes precedence over
the operator process environment. An override is operator-trusted executable configuration;
the default installation is pinned, while override versions are the operator's responsibility.

Only list-solutions, symbol search/definitions/references/callers, overloads, member source,
test discovery and workspace rebuild are exposed. Refactoring, code execution through tool
actions, analyzer trust, solution switching and the other upstream tools are not enabled.
The allowlisted tools are preapproved for noninteractive use; other tool approvals are unchanged.
Existing sandbox and reviewer isolation controls remain in place. Roslyn loads the selected
solution through MSBuild; use trusted repositories, just as for a local build.

Agents receive concise guidance to prefer Roslyn for targeted C# questions, refresh after
edits or checkout changes, and use CLI search for other languages or failed/suspect queries.
Check for skipped projects; empty references do not prove absence. `find_callers` expects
`Type.Method`, not an overload signature. Transitive test discovery is optional and can be
expensive. A startup failure is non-fatal, with a 30-second timeout; CLI navigation remains
available. Tool calls have a 60-second timeout.

This does not change kernel stages, certify discovery completeness, configure interactive
Codex sessions, or establish token savings. It allows semantic queries to replace repeated
file searches. The allowlist uses Codex's supported
[MCP configuration](https://developers.openai.com/codex/mcp).
