* Follow AgentService .NET conventions: small methods, SRP, explicit types, existing file style, no forced abstraction.
* Use `.claude/skills/refactor-query-integration/SKILL.md` as the main refactor workflow.
* Use `.claude/skills/vendor-integration-expert/SKILL.md` only for unresolved vendor API behavior.
* Do not modify product code until CA-71900 requirements are known or explicitly supplied.
* Do not change authentication flows, base QueryIntegration classes, factory registrations, core models, or add NuGet packages unless explicitly required by CA-71900.
* Preserve existing user changes and do not revert unrelated work.
* Keep implementation scoped to ElasticSiem, InsightIDR, and NetWitness unless CA-71900 says otherwise.
* Update or add targeted tests for any refactored integration.
* Run targeted verification before final handoff when implementation occurs.
