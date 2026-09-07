# Prompt Contract

Role:
You are a senior .NET engineer working on Cymulate's YAML integration engine
(`src/Cymulate.Integration.Adapters/Collectors/YamlCollector/Cymulate.Integration.Yaml.Engine`).

Goal:
Implement declarative cross-endpoint enrichment (`merge_into`), body-driven pagination
(`body_cursor`), and three correctness fixes, with schema validation and unit tests —
on branch `feature/yaml-engine-declarative-enrichment` (base dev d5b0c93).

Context:
- Workflow subsystem exists: `Models/WorkflowConfig.cs` (StageConfig: operation/topic/capture/for_each/poll),
  `Workflow/WorkflowRunner.cs`, wired via `Execution/YamlOperationRunner.cs:92`.
- Mapping: `Mapping/ResponseMapper.cs` (`"$"` = raw JSON string; no object self-token),
  `Mapping/JsonPathHelper.cs` (`ExtractArray` returns empty for non-array).
- XML: `XmlToJsonConverter` (root element stripped; single repeated element → object).
- Known bug: `IntegrationEngine` assigns `lastResponseBody` BEFORE XML→JSON conversion
  (was :488 vs :614 pre-PR#281 — re-locate in current code).
- Pagination: `Pagination/` (Offset/Cursor/PageNumber/LinkHeader/Scroll + PaginatorFactory).
- Native reference semantics (do not name vendors in code): per-page enrich with bounded
  cache (10,000, evict-oldest), keyed fetch batches of 100, last-wins duplicates,
  OrdinalIgnoreCase string keys, left-join.
- Real XML fixtures for tests: see assumptions.md A6.

Constraints:
- See constraints.md (binding, complete).

Success Criteria:
- `merge_into` stage enriches a target stage per-page: keys extracted at the `on:` target
  path (array-element anchors `PATH[].KEY` supported), source operation invoked per
  uncached-key batch with `{{keys}}` resolved, matches embedded under `as:` at the anchor
  node, `unmatched: keep|drop` honored, page published after enrichment.
- `body_cursor` paginates via json_path/regex cursor from the body, as query-param value
  or followed next-URL; stops on null/empty cursor or max_pages.
- Captures/poll/body_cursor see the post-XML-conversion body.
- `$self` (with `except:`) emits the record as a nested object in mapping output.
- Object at records_path is consumed as a 1-element array.
- Loader/schema reject: unknown/later/topicless merge target, unparseable `on:`, missing `as:`.
- All existing engine tests pass; new tests cover: join (incl. array anchor, unmatched,
  duplicates, cache bound/eviction, batching), body_cursor (both sources, both modes,
  termination), $self/except, coercion, capture-post-conversion — using the XML fixtures
  where applicable.
- `dotnet build` clean; test run green for the engine test project.

Execution Rules:
- Read current file state before editing (dev moved: PR #281 touched the engine).
- Do not assume missing data; consult decisions.md for settled choices.
- Respect constraints strictly; no yaml files, no version bumps, no vendor names.
- Update state.json step statuses and execution_notes.md as steps complete.

Output Format:
- Code + tests on the branch (no commit unless operator approves separately).
- execution_notes.md: per-step summary, deviations, risks.
- state.json updated (steps, verification).

Stop Conditions:
- Goal achieved (all success criteria demonstrably met).
- A constraint cannot be honored without violating another — stop and surface.
- Current engine code contradicts the contract's structural assumptions — stop and surface.
