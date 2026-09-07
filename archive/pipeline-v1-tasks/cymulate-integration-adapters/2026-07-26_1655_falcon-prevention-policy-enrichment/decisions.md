# Decisions

1. Deliver Prevention Policy only.
   - This is the policy family relevant to detection/prevention behavior and
     sharply limits endpoint and contract scope.

2. Policies are host metadata, not independent collected entities.
   - The relationship is obtained through the device assignment and has no
     useful collector meaning without its asset.

3. Enrich both current Discover-driven flows at their existing bounded units.
   - Assets: one Discover page.
   - Findings: one existing AID batch before Spotlight.

4. Preserve raw assignment and definition JSON.
   - This provides maximum metadata and tolerates vendor field additions without
     a brittle projection model.

5. Assignment lookup is required; definition hydration is degradable.
   - Unknown assignment would make the relationship itself unknowable.
   - A known assignment remains valuable when its definition endpoint is
     temporarily unavailable.

6. Use explicit empty, partial, and disabled states.
   - Empty is not an error.
   - Partial is not not-found.
   - Disabled is visible rather than masquerading as a host with no policy.

7. Cache only resolved and confirmed not-found definition results.
   - Transient failure must never poison the rest of the run.

8. Keep current recovery ownership.
   - Falcon/Shared progress-aware recovery already owns deferral and exhaustion.
     No policy-specific attempt counter or terminal-budget shortcut is added.

9. Keep findings unchanged except for enriched `host`.
   - Existing production stripping, grouping, chunking, publication, and
     checkpoint behavior are protected contracts.

10. Treat the prior broad plan as research history.
    - It must not reintroduce other families, a generic route catalog, or a
      standalone policy flow into this delivery.

11. Policy enrichment is one shared component, not a flow and not per-flow logic.
    - Both flows already traverse the same Discover host inventory, so a single
      enricher under `Flows/Policies/` keeps the host envelope identical by
      construction instead of by convention.

12. Policy logic gets a dedicated `Flows/Policies/` space.
    - Operator-mandated; also matches plan §6 and keeps vendor policy code
      adjacent to the flows that consume it without polluting either flow.

13. Enrichment runs on every `CollectAssets` and every `CollectFindings` run.
    - Policies are host metadata gathered alongside asset inventory, exactly as
      asset inventory is already gathered for the findings flow.

14. Flows own only the bounded unit and the call site.
    - Assets: one materialized Discover page. Findings: the existing
      `freshHosts` AID batch, before accumulator construction and Spotlight.

15. Source documents are archived in the task directory under `source_docs/`.
    - The repo-root Markdown files are working copies; the task directory holds
      the durable record for future conversations.

