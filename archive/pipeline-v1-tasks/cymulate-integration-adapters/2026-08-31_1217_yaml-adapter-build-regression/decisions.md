# Decisions

- Compare the suspected adapter commit and its first parent using isolated temporary worktrees or equivalent immutable Git evidence.
- Correlate adapter PR merges with engine commit and package-version history.
- Proceeding on unverified: PR #352 is the breaking merge. If wrong: inspect all commits between the last known passing merge and current `dev`.
- Do not implement or publish a fix during this investigation.
