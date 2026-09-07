# Export parity note — native record shape → YAML that reproduces it

Engine: `MappingSpec.emit_mode` selects the emit shape; `ResponseMapper.Map`
(`CollectorExecutor/Interpreter/Interpreter.cs:95-118`) emits the FULL raw element for any non-`mapped`
mode; `CollectorExecutorRunner.ApplyEnvelope` (`Execution/CollectorExecutorRunner.cs:874-901`) wraps it
(typed_wrapper / source_type_prefix) using the page token scope, applied after the watermark read and
before slicing (`:452-465`).

## Tenable — verbatim (full record, no envelope)
Native `TenableIoAssetsChunkProcessor.BuildChunkRecordsAsync`
(`Collectors/TenableIoCollector/Flows/Assets/TenableIoAssetsChunkProcessor.cs:30-44`) +
`TenableIoFindingsChunkProcessor` — "dumb passthrough: no filtering, no enrichment, no remapping";
`NormalizedUtf8Json.SerializeToSingleLine(owned.RootElement)` emits each object VERBATIM (no wrapper, no
`sourceType`, no correlation keys).

YAML (`integrations/tenable-io.yaml` findings+assets inner chunk fetch):
```yaml
mapping: { records_path: "$", emit_mode: verbatim }
```
→ each chunk element emitted untouched. Verified: `Emit_Verbatim_PassesFullRecordThrough_NoDiscriminator`.

## Defender — typed_wrapper + source_type_prefix
Native `DefenderVmRecordFormatter` (`Collectors/DefenderVmCollector/Processing/DefenderVmRecordFormatter.cs`):

| Native method | Shape | line | YAML emit_mode + type_value |
|---|---|---|---|
| `FormatMachine` / `FormatSoftware` → `Wrap(type, record)` | `{"type":"machine"\|"software","data":{<record>}}` | :17-21, :38-49 | `typed_wrapper`, `type_value: machine`/`software` (assets stream) |
| `FormatVulnerability` → `AddSourceType("inventory")` | `{"sourceType":"inventory", <record>}` | :23-24, :51-90 | `source_type_prefix`, `type_value: inventory` |
| `FormatVulnerabilityChange` → `"delta"` | `{"sourceType":"delta", <record>}` | :26-27 | `source_type_prefix`, `type_value: delta` |
| `FormatRecommendation` → `"recommendationCatalog"` | `{"sourceType":"recommendationCatalog", <record>}` | :29-30 | `source_type_prefix`, `type_value: recommendationCatalog` |
| `FormatRecommendationVulnerability` → `AddMetadata("recommendationScopedVulnerability", recRef)` | `{"sourceType":"recommendationScopedVulnerability","recommendationReference":<id>, <record>}` | :32-33, :56-92 | `source_type_prefix`, `type_value: recommendationScopedVulnerability`, `envelope_fields: { recommendationReference: "{{rec_id}}" }` |

`AddMetadata` drops any pre-existing `sourceType`/`recommendationReference` before re-adding (:74-86); our
`ApplyEnvelope` reproduces this via `if (!o.ContainsKey(prop.Key))`. Verified:
`Emit_TypedWrapper_WrapsFullRecordUnderData`, `Emit_SourceTypePrefix_FlattensRecordWithTemplatedMetadata`.

### Defender collection scope (completed)
`integrations/defender-vm.yaml` now matches native `DefenderVmUrlBuilder.cs` stages/endpoints:
- machines `api/machines?$top&$filter=lastSeen ge {{input.base_date}}` (:32-39); software `api/software?$top` (:41-44).
- vulnerabilities `api/machines/SoftwareVulnerabilitiesByMachine?pageSize` (:46-49); changes
  `api/machines/SoftwareVulnerabilityChangesByMachine?sinceTime={{input.base_date}}&pageSize` (:71-79).
- recommendations `api/recommendations` (:51-54) → `capture_list recommendation_ids` → `for_each` recVulns
  `api/recommendations/{{rec_id}}/vulnerabilities?$top` (:61-66).
- vulns/changes set `allow_offset_fallback: false` — native `IsNextLinkPaginationEligible=false`
  (`DefenderVmFindingsFlow.cs:205-206`) paginates those by `@odata.nextLink` ONLY.

### Parity gaps (documented, not coded — computed date-logic, would be runner control flow)
- Native `GetAssetsBaseDate` remaps `base == now-365d ⇒ now-180d` (`DefenderVmUrlBuilder.cs:11-19`). Not
  templatable; the run supplies the effective `lastSeen` floor via `--base-date` (`{{input.base_date}}`).
- Native vuln-changes `sinceTime` floors to `max(base, now-14d)` (`:78-88`). Same — `{{input.base_date}}` used raw.
- Defender query params: native uses `pageSize=` for the two machine-export endpoints and `$top`/`$skip`
  for the OData ones; we mirror that. The `$filter`/`sinceTime` values are URL-encoded by `BuildUrl`.

## Deferred vendors (NOT yet native-exact — still on `mapped` subset mode)
- `integrations/cortex-xdr.yaml`
- `integrations/crowdstrike-falcon.yaml`
- `integrations/qualys.yaml`
These keep `{sourceType, <fields subset>, <correlationKeys>}` and their existing tests. Converting each
requires reading its native formatter; out of scope for this task.
