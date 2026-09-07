# Constraints

- Scope is EXACTLY items 1-3. Do NOT fix other audit findings (sparse-page truncation, string-only
  watermark, request.headers-unapplied, 18-arg method sprawl) — explicitly deferred.
- No new abstractions beyond the item-2 shared-validation extraction. Reuse existing patterns.
- Behavior-preserving for item 2: the extraction must not change validation outcomes; all current tests pass.
- 1a fix is tolerant token extraction at the strategy seam (accept string or coerce numeric/bool); do not
  weaken the page-loop catch filter as the primary fix.
- 1b: clamp the EFFECTIVE page size and batch size to >= 1; do not silently change a valid configured size.
- Do NOT regress the conformance work: full interface set, ProcessAsync via AdapterBusEntrypointRunner,
  resume via CollectorResumeRunner, per-emit-target persisted page counter, RUN-envelope ingress.
- net8.0; solution CollectorBase.slnx. Build clean; tests green.
- Before deleting the FetchSignal seam, confirm by search that nothing reads RunContext.Signal / FetchSignal.
