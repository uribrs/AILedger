# Decisions

- Build the dossier's first implementation slice rather than the complete migration roadmap.
- Use AILedger 1.x skills as immutable seed inputs for version one.
- Model roles and capabilities independently from provider names.
- Use a single authoritative writer for task state and event history.
- Include real provider process adapters, with testable dry-run boundaries, in the first version.
- Keep one governed orchestration run per work item.
- Proceeding on unverified: local Codex and Claude runtimes provide stable non-interactive invocation surfaces. If wrong: ship the adapter contract and report the unavailable runtime integration as a blocker rather than fabricating support.
- Proceeding on unverified: file-backed persistence is adequate behind one writer. If wrong: use a local transactional store while retaining Markdown projections.
- Provider adapters invoke direct child processes with argument arrays and redirected streams; they consume JSONL, persist exact provider session IDs, resume only by those IDs, and never use shell command strings, `--last`, or `--continue`.
- The initial unattended permission mapping is fail-closed and workspace-bounded: Codex uses `--sandbox workspace-write`; Claude uses `acceptEdits`, no permission-prompt host, and a required native Bash sandbox with unsandboxed fallback disabled. Dangerous bypass flags are excluded.
- Claude provider runs exclude ambient MCP servers and disable ambient slash-command skills; Ledger injects only the hash-verified, role-selected cognitive snapshot through the governed context manifest.
