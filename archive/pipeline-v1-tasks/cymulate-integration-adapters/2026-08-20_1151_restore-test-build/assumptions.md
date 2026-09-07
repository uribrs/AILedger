# Assumptions

- A1 — VALIDATED — The build failures are reproducible from the clean worktree at commit `a7b5121b548201fc8da9690a20ee7cde0419f80c`. actor: verifier; citation: `research/internal-recon.md` Compiler evidence records the pre-edit focused and solution builds producing 56 Defender-test errors; `state.json` records the verified base revision.
- A2 — VALIDATED — The failures are compile-time regressions rather than missing private dependencies or machine-only configuration. actor: verifier; citation: the base-to-worktree diff restores only `Defender.Test.csproj:20-22`; verifier runs of focused `dotnet test --no-restore` (27/27) and solution `dotnet build --no-restore` (0 warnings/errors) exited 0 without restore or dependency changes.
- A3 — VALIDATED — The smallest correct repair preserves the intended behavior and assertions introduced by the pulled development changes. actor: verifier; citation: `git diff a7b5121b548201fc8da9690a20ee7cde0419f80c` is one four-line project-reference addition; the verifier focused run passed 27/27 and `git diff --check` passed.

## Prior Art

No relevant prior art found for tags: `cymulate-integration-adapters`, `dotnet-tests`, `compile-failure`, `falcon-prevention-policy`. A Falcon batching lesson matched repository/product tags but was unrelated to compilation and was not seeded.
