# Execution Notes

## W2 — body_cursor (complete)
- PaginationConfig: +BodyCursor enum (alias body_cursor), +cursor_json_path/cursor_regex/follow_url; reuses cursor_field for query-param name.
- BodyCursorPaginator (new): json_path (string|number) else regex group 1 over body text (timeout-guarded); null/empty ⇒ complete; follow_url replaces RequestUri.
- 11/11 tests green (BodyCursorPaginatorTests).
- Deviations accepted: (1) URL validity decided at UpdateState so the loop stops via IsComplete; (2) follow_url requires http/https scheme — bare UriKind.Absolute wrongly accepts Unix paths as file: URIs.
- Handoff to main thread: schema needs body_cursor enum + 3 keys (planned S6).

## W1 — fixes (complete; worker went idle without report, verified by orchestrator)
- IntegrationEngine: lastResponseBody reassigned post XML->JSON conversion (4-line diff); classification still sees raw body.
- ResponseMapper/MappingTransformConfig: $self token + {source: "$self", except: [...]} dict form.
- JsonPathHelper.ExtractArray: object at path → 1-element array.
- Tests green: EngineXmlResponseTests (3), ResponseMapperSelfTests, JsonPathHelperExtractArrayCoercionTests (+ W2's 11) — 24 total.

## W3 — merge_into (complete; crashed mid-run on API error, resumed via mailbox, verified by orchestrator)
- MergeIntoConfig (target/on/as/unmatched/batch_size/page_size/cache_size) on StageConfig.
- WorkflowRunner: held-target publish suppression; per-chunk enrich loop; {{keys}} exposed via input-scope binding (same fall-through as {{item.*}}).
- Loader: ValidateWorkflowMerges — earlier+topic'd target, no topic on merge stage, on-expression parse, v1 bound: at most ONE merge per target (documented simplification).
- MergeIntoTests green; pre-existing WorkflowRunnerTests untouched and green.

## Main thread — integration (S6/S7 complete)
- Schema: body_cursor enum + cursor_json_path/cursor_regex/follow_url; merge_into stage block; mappingValue self-envelope branch ({source: "$self", except: []}) — the strict transform def would have rejected W1's dict form; gap found and closed at integration.
- Full engine suite: 536/536 green. Adapter project builds clean.
- Synthetic yaml exercising body_cursor + $self/except + merge_into loads through schema-validating loader.
- Hygiene: 0 vendor names in changed engine code; 0 yaml files touched.

## Review cycle 1 → repairs
- verifier-1: PASS, 4 low gaps → 3 gap tests added by W3 (numeric canonicalization both directions, empty target stream, follow_url precedence). Full suite 540.
- code-reviewer-1: needs-work. Major#1 merge+resume silent data loss → FIXED: workflows with merge_into discard resume state, run fresh from stage 0 (doc updated, test added). Major#2 cache eviction mid-chunk drops enrichment → FIXED: per-chunk non-evicting match dict; BoundedKeyCache cross-chunk only (test added). Minor#4 follow_url credential forwarding → hardened: host-change WARNING + trust-model doc; engine passes logger through PaginatorFactory (main thread wired IntegrationEngine call site). Minor#7 schema reformat noise → re-applied as 30-line targeted edit.
- Deferred (accepted risks): #3 coercion unconditional for JSON callers (contracted behavior, A4); #5 no negative caching (perf follow-up); #6 {{keys}} comma-join ambiguity (documented limitation, A5).
- Post-repair: build clean, full engine suite 544/544. verifier-2 + code-reviewer-2 in flight.

## Close-out (2026-07-16)
- verifier-2: PASS, no regression. code-reviewer-2: SHIP, no new defects; #1/#2/#4/#7 fixed, #3/#5/#6 skipped as maintainer-deferred.
- Accepted risks (documented): merge workflows redo all work on resume (correctness over resumability, v1); orphan S3 pages only if a rerun emits fewer pages (near-unreachable); per-page host-change warning noise on host-hopping runs; int-only canonicalization test coverage; no mid-chunk partial rollback.
- Final: 544/544 engine tests, adapter + engine build clean, 0 vendor names, 0 yaml touched, uncommitted on feature/yaml-engine-declarative-enrichment.
- Follow-ups (separate tasks): yaml updates (qualys → IVMC → tenable-sc/rapid7-insightvm) with YamlLocalRunner-vs-native parity as acceptance gate; deferred minors #5 negative caching, #6 {{keys}} escaping if a comma-keyed vendor ever appears.
