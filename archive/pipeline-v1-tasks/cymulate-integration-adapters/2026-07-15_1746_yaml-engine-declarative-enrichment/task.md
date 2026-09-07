# Task: YAML Engine Declarative Enrichment

Add vendor-agnostic cross-endpoint enrichment and body-driven pagination to
`Cymulate.Integration.Yaml.Engine`, plus three correctness fixes, so fused
record shapes (parent records enriched from a second endpoint) become
expressible in yaml workflow stages.

Motivating (but never named in code): Qualys `{HOST_DETAILS, DETECTION_LIST[+VULNERABILITY_INFO]}`;
same pattern serves InsightVM Cloud, Tenable.sc, InsightVM on-prem, Entra ID, ServiceNow CMDB.

## In scope
- `merge_into` on workflow stages (per-page join/embed, bounded run-scoped cache).
- `body_cursor` pagination strategy (cursor value or next-URL from response body).
- Fix: captures/poll see post-XML-conversion body.
- Fix: `$self` mapping token with `except:`.
- Fix: single repeated XML element coerces to 1-element array.
- Schema + loader validation + engine unit tests.

## Out of scope
- Any yaml file change (follow-up task).
- Version bump (operator's call).
- Adapter surface / ISB contract / Shared / S3 layout / done-event changes.
- YamlLocalRunner-vs-native parity run (acceptance gate of the follow-up yaml task).
