# Task: IntegrationInfra extraction — planning

Plan the extraction of the former `Shared` library (freshly copied, standalone, to
`/Users/user/Dev/Uri/localprojects/IntegrationsInfra` — 169 `.cs` across 15 top-level areas:
Contracts, Converters, DataPipeline, DependencyInjection, Diagnostics, Events, Exceptions, Glossary,
Helpers, Models, Orchestration, Recovery, Resilience, Session, Time) into a properly-structured
**IntegrationInfra**.

**Hard success criterion:** a SINGLE capability surface ("the capability bank") that adapters plug
into, importing ZERO mechanics namespaces. If adapters would still reach into many internal
namespaces afterward, the effort has no value and should not proceed.

This run is **planning only**: produce the plan + assessment, gated for user sign-off. No structural
edits, renames, rewiring, or fat-removal. Read-only analysis of the real tree is expected.
