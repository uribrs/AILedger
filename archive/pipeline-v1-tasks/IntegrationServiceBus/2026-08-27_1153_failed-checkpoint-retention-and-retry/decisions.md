# Decisions

- Recon is scoped to four repos: IntegrationServiceBus, cymulate-integration-adapters,
  IntegrationInfra, and whichever backend repo evidence shows owns the collection trigger.
- The service-hub constraint is accepted as given and shapes what recon looks for: ISB-side
  findings are about state retention and request servicing, not about judgment.
- Recon reports the per-repo obligation split even when a repo's change is out of ISB's
  control, because the sequencing cost is the operator's decision to make.
- Proceeding on unverified: the three-repo split (state in ISB, readability in the collector,
  decision in the backend) is the right decomposition. If wrong: the recon report's
  obligation split misallocates work, though the cited facts remain valid.
- Proceeding on unverified: a backend repo that owns the collection trigger is present on
  this machine. If wrong: the trigger-owner question is answered from ISB's inbound message
  surface only, and the retry request path stays an open design question.
