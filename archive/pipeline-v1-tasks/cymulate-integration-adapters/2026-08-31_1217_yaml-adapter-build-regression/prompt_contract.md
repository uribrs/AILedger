# Objective

Determine when the YamlAdapter compile breakage occurred and identify the pull request responsible.

# Context

- Current adapter build fails because `ConnectionValidator` references `OperationConfig.HandshakeFor`.
- The pinned and freshly restored `Cymulate.Integration.Yaml.Engine` `1.0.0-preview.19` binary lacks that member.
- Freshly fetched engine `origin/dev` contains the member in commit `ef407cf`.

# Constraints

- Perform a read-only regression investigation.
- Preserve working trees and user changes.
- Do not infer PR causality from commit subjects alone; verify ancestry and before/after behavior.
- Separate source merge timing from package publication timing.

# Required Work

1. Identify the exact adapter commit where compilation first fails.
2. Map that commit to its merged PR and parent merge.
3. Identify when the engine contract was added relative to engine package `preview.19`.
4. Verify whether earlier related adapter PRs built successfully.
5. Report the causal sequence and corrective release ordering.

# Success Criteria

- Name the first broken commit and responsible PR with evidence.
- Name the last passing relevant commit or parent.
- Explain whether the regression was caused by adapter code, engine source, package publication, or their ordering.
- Reproduce the relevant pass/fail boundary without modifying product source.
