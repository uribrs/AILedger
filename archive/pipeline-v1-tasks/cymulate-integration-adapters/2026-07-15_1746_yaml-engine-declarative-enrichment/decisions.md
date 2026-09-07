# Decisions

- Merge executor owns join-key extraction per page; source operation receives the page's uncached keys via a `{{keys}}` template scope (comma-joinable) — no separate `from_records` capture construct needed for this task.
- `merge_into` grammar: `target` (earlier topic'd stage name), `on` ("<target key path> = <source key path>", `[]` marks array-element anchor), `as` (embed field), `unmatched: keep|drop` (default keep = left join), `batch_size` (keys per source request, default 100), `cache_size` (default 10000).
- Left-join default mirrors native QualysFindingsFlow/IVMC semantics (verified 2026-07-15).
- Last-wins duplicate keys + OrdinalIgnoreCase string comparison mirror QualysFindingsXmlParser.ParseKnowledgeBaseAsync and IVMC DefinitionsFetcher (verified).
- Bounded evict-oldest cache mirrors QualysKnowledgeBaseEnricher (MaxVulnerabilityCacheEntries=10000, MaxQidsPerRequest=100).
- `body_cursor` is one strategy with two source kinds (json_path | regex) and two application modes (query-param value | follow-URL); URL mode replaces the request URI for the next page.
- `$self` includes the whole record as an object; `except:` drops listed top-level keys to avoid payload duplication when the record is also partially lifted.
- Single-element XML coercion implemented at ExtractArray consumption (object at records_path → treated as 1-element array) rather than changing converter output globally — smaller blast radius; converter output stays stable for mapping paths.
- lastResponseBody fix: assign after XML conversion so workflow captures/poll/cursor extraction all see JSON; raw XML no longer observable by captures (regex captures on raw XML were the only consumer — none exist in shipped yamls; acceptable break, noted in assumptions).
- Dev picked up PR #281 (yaml-egress-streaming) — executor must read current IntegrationEngine state, not session-cached line numbers.
