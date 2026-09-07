# Constraints

- Keep the YAML grammar: additive fields on the existing `merge_into` block only; no new syntax family.
- Additive / no regression: every capability opt-in; flag-off (field absent) MUST be byte-identical to today's behavior.
- Existing consumers pass unchanged: qualys.yaml, insightvm-cloud.yaml (merge_into), the 93 `strategy: cursor` YAMLs.
- Align to native exactly: mirror the algorithm including the `floor(n/cap)+1` off-by-one (exact-cap hosts get a trailing empty terminal chunk) and the zero-finding single record.
- Generic, not Falcon-special: each is a reusable merge capability keyed off config, no vendor branching.
- Do NOT reinvent Shared.Datapipeline.Egress: chunk framing is category-(c) inner-record reshaping that lives ABOVE egress; egress consumes finished records as a stream and needs ZERO change. Do not add record-splitting to egress.
- There is NO 50 MiB egress fail-fast (myth); chunk framing's justification is byte-parity + bounded per-record memory only.
- All four land at the single choke point: Workflow/MergeEnrichmentSink.cs (post-plan pass) + Workflow/MergeEnrichment.cs (EmbedValue/EmbedGroup, ApplyEmbed/ApplyGrouped) + Models/WorkflowConfig.cs + Loader + Schema. No IntegrationEngine page-loop change.
- ADR-0003 dual-home: after each engine schema change, re-sync byte-identical to cymulate-magic-integration/schemas/integration.schema.json.
- dotnet gates: adapters engine test project + YamlCollector adapter test project green; magic-integration catalog/schema tests green with `-p:TreatWarningsAsErrors=false` (user-level CodeArtifact NuGet source trips NU1507; never edit the user's global NuGet.Config).
- Continue on `falcon-strategy-parity` in both repos; do not commit/push unless asked.
- Never print credential values in the live run.
