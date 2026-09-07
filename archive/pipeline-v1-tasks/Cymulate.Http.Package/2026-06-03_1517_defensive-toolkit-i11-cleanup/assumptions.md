# Assumptions

- **[VALIDATED]** Interface decision is DOCUMENT-keep-both (user, 2026-06-03) — see decisions.md.
- **[VALIDATED]** `ServerSuggestedDelayExternalizer` already exists and disposes the response before throwing (delivered in the prior hardening task); the extraction builds on it, not around it.
- **[VALIDATED]** Current green baseline is 268/268 tests; behavior preservation means this count holds (or grows only by added refactor-seam tests).
- **[ASSUMPTION — execution detail]** The consolidated gate will most naturally be a small collaborator (e.g. a `ServerSuggestedDelayGate` that owns resolve-once + clamp + invoke-externalizer), or an extension of the existing externalizer. Exact shape is an execution decision for the refactor, bounded by "resolve once, invoke from one place, behavior-preserving." Not blocking.
- **[OPEN — verify during execution]** Confirm the externalization is invoked from exactly the sites identified (RetryPolicy ShouldHandleAsync, post-success in ExecuteAsync, ClassifyResultAsync) and that consolidating to one resolution point does not change which response/decision wins (precedence must be preserved).
