# Constraints

- Recon only. No product code changes in any repo. No branches, no commits.
- No questions to the user during this phase; record ambiguity as an OPEN assumption instead.
- ISB is a service hub, not a decision hub: it holds checkpoint state and services a resume
  request. Do not design ISB-side retryability judgment, failure classification, or
  advisability logic.
- Retryability judgment belongs to the collector, which owns the `adapter_state` blob.
- The decision to retry belongs to the backend/operator, not to ISB.
- Every claim in the recon report carries a `repo/path:line` citation. Uncited claims are
  labelled speculation explicitly.
- Read-only against all four repos. Do not modify `cymulate-integration-adapters`,
  `IntegrationInfra`, or any backend repo.
- Do not treat the prior-conversation findings as settled; each is an OPEN assumption to be
  re-derived from source during recon.
- Cross-repo cost must be stated wherever a change would require a package release
  (`Cymulate.Integration.Client`, `Cymulate.IntegrationInfra`).
- Report the trigger owner from evidence. If no backend repo present on this machine owns the
  collection trigger, say so rather than nominating a plausible one.
