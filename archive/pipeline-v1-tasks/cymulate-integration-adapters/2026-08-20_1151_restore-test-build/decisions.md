# Decisions

- Reproduce using the repository's normal .NET build/test entry points before editing.
- Diagnose compiler output to identify the narrowest affected project set.
- Proceeding on unverified: failures are locally repairable source/test inconsistencies. If wrong: stop with the concrete dependency or environment blocker.
- Run an independent verifier and isolated code review after implementation, as required by the workflow.
