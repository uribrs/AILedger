# Assumptions

- A1 (VALIDATED): Native duplicate-key/left-join/cache semantics are the correct reference — verified in QualysKnowledgeBaseEnricher, QualysFindingsXmlParser, InsightVmCloudVulnerabilityDefinitionsFetcher on 2026-07-15.
- A2 (VALIDATED): Workflow subsystem (stages/capture/for_each/poll) exists and is wired via YamlOperationRunner:92; merge_into composes with it.
- A3 (VALIDATED — no yaml uses workflow capture regex; error rules match pre-conversion, unaffected): No shipped yaml relies on regex-capturing the RAW (pre-conversion) XML body — lastResponseBody fix changes captures to post-conversion JSON. Risk: low (capture is JSON-path-broken on XML today, so no working consumer can exist). Reject the fix only if a grep of deployed yamls shows regex captures on XML vendors.
- A4 (OPEN): Coercing object-at-records_path to a 1-element array does not surface unwanted records in existing yamls. Risk: low — strictly more permissive; behavior change is the intent. Release-note it.
- A5 (OPEN): `{{keys}}` comma-join covers current keyed-fetch vendors (Qualys ids=, IVMC search body). If a vendor needs a JSON-array body, the existing hydrate-style `{{ids}}` body injection pattern is the fallback; do not build new templating beyond comma-join in this task.
- A6 (VALIDATED): Session-captured real Qualys XML fixtures exist at /private/tmp/claude-501/-Users-user-Dev-cymulate-integration-adapters/471d249c-bb45-4535-a11f-4e332495ca84/scratchpad/qualys-real-{hosts,detections}.xml — copy into test fixtures (sanitize: no credentials present in bodies; verify before commit).
