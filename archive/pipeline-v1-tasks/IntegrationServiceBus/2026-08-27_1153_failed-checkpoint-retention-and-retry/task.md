# Failed-checkpoint retention and operator-initiated retry

## Task

Recon only. Collect and cite what exists today across every repo that a future
implementation of this feature would touch. No implementation, no user questions.

## Feature being reconnoitred

1. When a collector run ends with a reported failure, its adapter checkpoint is retained
   rather than deleted, carrying a one-week TTL.
2. A sweep compares each retained checkpoint's clock against that TTL and deletes it when
   the week is up.
3. A backend/operator-initiated request can name a retained checkpoint and have the run
   proceed from it — taking the resume path, not a fresh relaunch.

## Architectural constraint (operator, authoritative)

ISB is a **service hub, not a decision hub**. It holds checkpoint state and services a
request to resume a specific checkpoint. It does not judge retryability, does not decide
whether a resume is advisable, and does not need to know why a collector failed. The
collector owns its state blob and therefore owns the readability judgment; the backend or
operator owns the decision to retry.

## Recon must establish, with citations

- ISB checkpoint lifecycle: where a failed run's checkpoint is deleted, what the sweep and
  cleanup jobs select on, and where the resume path is entered.
- The adapters repo: every collector's resume-state shape, how uniform the checkpoint-helper
  pattern is, and what decline taxonomy already exists.
- IntegrationInfra: the shared staleness threshold, every call site, and what a per-request
  parameterization would have to touch.
- Which repo owns the collection trigger today, and what message or API a retry would use.
- The wire-contract surface (`AdapterDoneMessage` in `Cymulate.Integration.Client`) and its
  consumers, with the cross-repo release cost of changing it.
- The minimal ISB-side change consistent with the service-hub constraint.
