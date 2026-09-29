# Task 8 installation — client trial pending

Date: 2026-09-28. Installation was explicitly authorized by the implementation request.

- **Implementation:** verified and committed as `540d6e603da18d8388c2c93212739b3ac4a33d05`.
- **Installation:** `2.0.170  from 540d6e6`, confirmed from `/Users/user/.local/bin/ailedger`.
- **Client trial/acceptance:** pending. Task 7's client trial also remains pending.
- **Billable episodes:** none launched.

Supported scope is one inline VerifierOutput or CodeReviewOutput document, including
existing assurance bundles, through `submit_artifact` on trusted Codex and Claude endpoints.
Other artifact kinds and external-reference ingestion remain outside this slice. See the
[contract](../structured-artifacts-v1.md) and [validation](task-8-validation.md) for the
exact boundary, unchanged authority/assurance rules and remaining CLI operations.

The clean verified implementation commit was packed in Release outside the checkout with
explicit package and informational versions. Its source identity is **540d6e6**. The
[machine-readable receipt](task-8-installation.json) retains the build/update commands,
package/executable identities, fixture results and log hashes.

Package SHA-256: `2a806f7f30182c22cb275f7bdda45d0fea72dd0f18ea9b7a8dd30a06edbe4cc3`.
The global tool's stored package matches that hash. The executable resolves to
`/Users/user/.dotnet/tools/ailedger`; its hash is retained in the JSON receipt.

The previous **2.0.168 from f966952** package was preserved before editing, verified
before updating, and copied to durable rollback storage:
`/Users/user/.local/share/ailedger/rollback/ailedger.cli.2.0.168.nupkg`.
Its SHA-256 is `1f92c5becb3c87b798b6e24cf0e923b9d2c2000c597c359213ecccb7899d3d21`. The initial external scratch copy is also retained
at `/tmp/ailedger-task8/rollback/ailedger.cli.2.0.168.nupkg`. No global instructions or skills were changed.

Both the candidate tool-path installation and final global executable passed the
non-billable [package check](../../tools/ArtifactSubmissionProbe/run.py): three exact
advertised tools, content mismatch rejection without mutation, content fidelity/digest,
trusted author/run/scope, receipt replay after restart and run completion, key conflict,
both artifact kinds, separate measurement populations, and explicit recording-capability
revocation. The check uses disposable fixtures and scripted local MCP clients. It does
not count as a successful actual-agent client trial or evidence of improved delivery cost.

The pack command emitted the existing missing-package-readme advisory. Packing and both
installations succeeded. This documentation follow-up changes no executable source and
is not repacked; the installed implementation identity remains **540d6e6 / 2.0.170**.
No merge, publication, historical-task migration or work on tasks 9–14 occurred.

## Ordinary task for Uri

> Verify and review the artifact-submission change on `codex/structured-findings-contract`.
> Have the verifier and code reviewer submit their reports through `submit_artifact`,
> then show me the reports and receipts.

This is a proposed ordinary task for Uri to run, not an episode launched during implementation.
After it runs, inspect actual content fidelity, attribution, accepted version/dependency links,
refusals/retries, remaining CLI work, manual intervention, elapsed time and once-per-run usage.
Retain missing observations and failures. Do not infer acceptance from installation or test counts.
