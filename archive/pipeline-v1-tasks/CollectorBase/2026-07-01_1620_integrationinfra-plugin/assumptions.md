# Assumptions

- A1 — Every Shared TYPE used by the consumers exists in IntegrationInfra. The 11 consumer namespaces all map to carried concerns, but a specific type within one may be missing (e.g. a Shared.Helpers/Glossary/Converters/Models type not carried). Status: OPEN — confirmed at build; if the build surfaces an unresolved type IntegrationInfra genuinely lacks, STOP + surface (new carry decision, per D-no-workaround).
- A2 — Strategies needs no direct package reference (it references CollectorExecutor, so it inherits Cymulate.IntegrationInfra transitively). Status: OPEN — confirm at build; add a direct ref only if the build demands.
- A3 — Cymulate.Integration.Sdk 3.2.0 is available on the org feed AND does not break CollectorBase compilation (3.1.8→3.2.0 is a minor bump). Status: OPEN — restore/build-driven; surface any breakage.
- A4 — The local feed (../IntegrationInfra/artifacts) resolves Cymulate.IntegrationInfra 1.0.0-preview.2 while the inherited org feed stays active for the transitive Cymulate.* deps. Status: OPEN — restore-driven; if restore can't see both, fix the nuget.config (still no <clear/>).
- A5 — The rewrite is purely mechanical (namespace + type identifiers); no consumer logic needs to change to compile against IntegrationInfra. Status: OPEN — confirmed at build; if a consumer relied on a Shared API that changed shape (not just name), STOP + surface.
