# Guarded C# navigation

Run `sh scripts/install-roslyn.sh` to install Roslyn CodeLens MCP **2.18.1** into
`~/.ailedger/tools/roslyn/2.18.1`. The tool requires .NET 10. Run `sh scripts/install.sh`
after building adapter changes so the installed AILedger uses them. Wait for active provider
runs to finish before replacing the installed tool.

Governed **Codex and Claude** launches receive eleven navigation tools through an AILedger
stdio bridge. Roslyn starts with no solution loaded. Discover solution filenames, then call
`load_solution` with the absolute `.sln` or `.slnx` path for the target repository. Load multiple
solutions and return to one using `set_active_solution` with its exact absolute path in
`name`. Confirm the active solution and skipped projects with `list_solutions`. The launch
working directory does not choose a solution. After edits or a checkout change, call
`rebuild_solution`. `find_callers` takes `Type.Method`, not an overload signature.

## Scoped runs and permissions

For a work item scoped below a repository root, the launcher supplies that root as separate
navigation metadata. A worker in `integrations/axonius` can load its repository's solution
without gaining a provider write grant to the whole repository. The working directory and
`--add-dir` grants remain unchanged. Task-wide runs use the working directory and explicit
additional-directory grants; they do not inherit unrelated parent repositories.

The bridge canonicalizes paths and symlinks, excludes ledger storage, and rejects selections
outside its navigation directories. A failed selection clears the active solution, preventing
accidental queries against the preceding repository. Only loading/selection/listing, symbol
search, definitions/references/callers, overloads, member source, test discovery and rebuild
are exposed. Refactoring, code execution tools and analyzer trust are excluded, including
direct calls to unlisted tools. These tools are preapproved; other approvals are unchanged.

Roslyn evaluates MSBuild outside the provider sandbox. Navigation directories bound solution
selection, **not** transitive project/package reads or MSBuild side effects. Use trusted
repositories. This is not an enforced read-only filesystem capability. Existing provider
sandbox and reviewer memory/narrative isolation settings remain in place.

## Executable guardrails

Both adapters install a `PreToolUse` command hook invoking `ailedger navigation guard` through
the current host assembly. Covered C# content searches in native `Grep`, `rg`, `grep` and
`git grep` are denied before execution. This includes broad searches over mixed repositories
and content searches that only print matching filenames (`rg -l`, `grep -l`). Use Roslyn's
semantic query tools for C# definitions, symbols, references and callers.

File-name discovery (`rg --files`, `find ... -name`), targeted source reads, builds/tests/Git
and explicit non-C# filters such as `rg -g '*.yaml' ...` remain available. Broad searches
must narrow their file filters if they are intended only for non-C# content. The classifier
conservatively treats unknown directory coverage as potentially C#.

A failed authorized Roslyn load or query records a receipt through the bridge. One receipt
permits **one simple CLI search within that solution directory for ten minutes**. Compound
commands and ambiguous targets cannot consume it. Receipts are claimed atomically, cannot
authorize another repository, and are revoked by another valid query/load attempt in the same
solution directory. A successful call, empty result, invalid arguments, or rejected path does
not earn a receipt. The bridge adds the recorded failure identifier to its error response.
Missing/broken Roslyn startup records a fallback for the launch's source directories; restore
the dependency before continuing semantic work. A self-declared failure does not unlock a search.

`AILEDGER_ROSLYN_EXECUTABLE` overrides the executable with an absolute path; the launch request
takes precedence over the parent environment. Empty/missing/relative overrides are unavailable
dependencies, not switches that disable the guard. Hook/configuration errors deny the call
or fail launch rather than intentionally downgrade to prose. Per-launch settings and receipts
are temporary workflow state and are removed at run cleanup.

Codex uses an isolated home and queries its app-server hook metadata before launch. AILedger
trusts only the exact generated command/hash and rejects unexpected hooks. Repository hooks
are excluded with untrusted project configuration; plugins are disabled. No global hook-trust
bypass is used. Claude merges the hook into its explicit launch settings and retains its strict
MCP allowlist. Both providers need compatible hook support. Validated versions are Codex CLI
`0.155.0-alpha.9.2` and Claude Code `2.1.280`; unsupported Codex hook metadata fails preflight.

Hooks are workflow guardrails, **not a tamperproof shell sandbox**. Arbitrary programs can hide
searches; Codex does not invoke `PreToolUse` again for interactive `write_stdin`; provider-managed
policy can affect hook execution. Agents must not bypass the guard through scripts or interactive
sessions. A hook cannot establish discovery completeness or prove token savings. See the provider
contracts: [Codex hooks](https://learn.chatgpt.com/docs/hooks) and
[Claude hooks](https://code.claude.com/docs/en/hooks).

Startup allows 30 seconds; tool calls allow 60 seconds. The bridge stops waiting on a stalled
backend after 55 seconds and kills its child on connection close. No indexing or download occurs
until a solution is selected. Standalone projects without a solution need a solution before
semantic navigation. This change affects governed launches, not existing interactive sessions,
and does not change kernel stages or reconfigure runs already in progress.
