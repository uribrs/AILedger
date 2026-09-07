# Verifier Report — yaml-engine-falcon-correlation-gaps

**Verdict: PASS-WITH-GAPS**

The five engine work items were built with genuine extension-not-modification discipline,
the schema is additive and cross-repo-synced, the new YAML uses only existing idiom shapes,
and the live parity numbers reproduce independently to the digit. The gaps are narrow and
mostly pre-booked: one of 358 published envelopes carries `aid: null` and no `findings` key
(an unmanaged host native drops entirely), which is a literal miss against two of the
"every envelope" success criteria; and a handful of claims rest on evidence I could not
fully re-derive from the artifacts alone (full adapters solution suite, live vendor total,
drift causation). None of these are execution defects against the contract's intent.

## Success-Criteria Coverage

| # | Criterion | Status | Evidence |
|---|-----------|--------|----------|
| 1a | New tests cover each item (flag off = old, flag on = new) | MET | Test diffs present: MergeIntoTests +123, EngineCursorRecoveryTests +49, ResponseMapperEdgeTests +21, FieldTransformRegistryTests +8; W1-W4 reports describe both-way coverage + revert-to-fail checks. |
| 1b | Full engine test project green | MET | Re-ran `dotnet test …Cymulate.Integration.Yaml.Engine.Test` myself: **659/659 passed, 0 failed** (374 ms). |
| 1c | Adapters *solution* test run green | UNVERIFIED | Out of my check scope (check #1 named only the engine project). Engine gate green; wider suite not independently run. Low risk. |
| 2a | falcon.yaml loads through adapters engine loader w/ schema validation on | MET (indirect) | Proven by the live parity run executing falcon end-to-end through the adapters engine harness; also loads clean under the magic catalog test (schema-valid, not among the 2 failures). |
| 2b | magic-integration catalog/schema tests green (`-p:TreatWarningsAsErrors=false`) | MET-as-expected | Re-ran: **7 passed, 1 failed**. The one failure is `CatalogBaselineTests`, and its message names **only** `defender-vm.yaml` and `qualys.yaml` — both `Cannot convert 'body_cursor' to enum PaginationStrategy`. Pre-existing dual-home drift (ADR-0003), not caused by this task; falcon not implicated. |
| 3a | hosts collected == vendor total at run time | MET (per guard) | Execution reports 358/358 in 2 pages (250+108), completeness guard armed, no shortfall. The live vendor `meta.pagination.total` at run time is not independently re-derivable from the saved artifacts; the armed-and-passed guard is the mechanism. |
| 3b | every findings-lane envelope has non-null, non-empty aid | **PARTIAL** | 357/358 have non-null aid. **1 record has `aid: null`** — the unmanaged host whose composite-id suffix is not 32-hex. Booked as a parity delta; native SKIPS this record (0 null-aid, 365 recs). Literal criterion not 100% met. |
| 3c | findings is a JSON array on every envelope ([] allowed) | **PARTIAL** | 357/358 have array findings (0 non-array). The same 1 null-aid record has **no `findings` key at all**. Literal criterion not 100% met for that record. |
| 3d | sum of findings ≈ native 35,651 modulo drift | MET | Independently recomputed: native 35,651, yaml 36,484, delta **+833**, fully accounted by 3e. |
| 3e | per-host counts match native for overlapping aids | MET | Independently recomputed (native chunks summed per aid): **356/357 exact**; single diff `2b171c12…` native=0 → yaml=833 (== the entire +833 total delta). Run mtimes confirm yaml ran ~2.3h AFTER native (12:58 vs 15:14), so a host gaining findings is direction-consistent drift. |
| 4 | Written comparison report with the numbers | MET | `execution_notes.md` "Live parity rerun + comparison" section. |
| 5 | Booked-delta list updated in YAML header (closed vs open) | MET | falcon.yaml header lines 30-54: booked deltas + an explicit "Closed by engine batch of 2026-07-21" block. |

## Checks Performed (independently re-run)

1. **Engine gate** — `dotnet test` on the engine project: **659/659 green**. ✓
2. **Extension discipline** — read the diffs of all touched engine files:
   - `CursorPaginator.cs` / `ScrollPaginator.cs` / Offset / PageNumber / LinkHeader / BodyCursor:
     purely additive field carry-forward (`CurrentOffset`, `RecoveryFloor`, `WatermarkValue`,
     `WatermarkIds`); the only "removed" lines are trailing-comma additions on the last existing
     field. No existing-field behavior change. ✓
   - `MergeEnrichmentSink.cs`: flag-off path is the original `ResolveMatchesAsync` + `Apply` loop,
     now in the `else` branch, structurally unchanged; `group: true` branches to a parallel
     `ResolveGroupedMatchesAsync`/`ApplyGrouped` path. ✓
   - `MergeEnrichment.cs`: the one "removed" line (`Cache = new BoundedKeyCache(…10000)`) is
     re-expressed as `Cache = new BoundedKeyCache(cacheCapacity)` where
     `cacheCapacity = merge.CacheSize > 0 ? merge.CacheSize : 10000` — **identical** expression,
     extracted to a local so the new `GroupCache` can share it (built only when `Group`). Byte-identical
     flag-off. ✓
   - `IntegrationEngine.cs`: `bufferedResponse` starts null and is set only under
     `paginationConfig is { Prefetch: true }`; consume branch is dead flag-off. Completeness block
     is guarded by `ExpectedTotalPath is { Length: > 0 }` AND `naturalExhaustion`; `freshestExpectedTotal`
     stays null flag-off so the block is skipped. `loopEndedViaBreak` is new tracking state that does
     not alter control flow. Un-consumed prefetch disposed in `finally`, never resumed. ✓
   - **Acknowledged default-path change (W3)**: `transform:` alias resolution replaced bare
     `Enum.TryParse` (which silently failed every snake_case alias, leaving Transform at its
     zero-value default and never running) with an alias-aware map shared with the loader validator.
     **Blast radius**: `grep 'transform:' integrations/*.yaml` → **1 hit, and it is
     crowdstrike-falcon.yaml itself** (the intended new consumer). Zero pre-existing consumers ⇒ the
     fix changes behavior for no shipped YAML. Claim confirmed. ✓
3. **Idiom constraint** — falcon.yaml uses only existing idiom shapes: `first_of` list of full
   mapping-value entries; `transform: regex_extract` with `pattern` (existing `transform:{}` shape);
   `merge_into.group: true` (additive boolean); `prefetch`/`expected_total_path` (additive scalar
   pagination fields); `cursor_recovery` (pre-existing block). No new syntax families. ✓
4. **Cross-repo schema sync** — `diff` of the two `integration.schema.json` files: **identical**. ✓
   Engine schema diff is purely additive (new optional `prefetch`, `expected_total_path`, `group`,
   `pattern`, `capture` props; `first_of` oneOf branch; `regex_extract` appended to the transform
   enum). Nothing removed or tightened. ✓
5. **Magic-integration gates** — re-ran (see 2b): schema-validation tests green; CatalogBaselineTests
   fails only on the two pre-existing `body_cursor` files. ✓
6. **Parity evidence** — independently recomputed from the raw NDJSON (native chunks summed per aid):

   | Metric | Native | YAML | Claim | Match |
   |---|---|---|---|---|
   | records | 365 | 358 | — | ✓ |
   | distinct hosts (non-null aid) | 357 | 357 | 357/357 | ✓ |
   | host-set intersection / one-sided | 357 / 0 | — | 357, zero one-sided | ✓ |
   | per-host exact matches | — | 356/357 | 356/357 | ✓ |
   | single diff host | — | `2b171c12…` 0→833 | +833 single-host | ✓ |
   | total findings | 35,651 | 36,484 | ≈ +833 drift | ✓ |
   | null/empty aid records | 0 | 1 | 1 null-aid | ✓ |
   | findings not-array | 0 | 0 | array on all | ✓ |
   | missing findings key | 0 | 1 | (the null-aid rec) | ✓ |

   Every claimed number reproduces exactly.
7. **A5 stretch (cursor_recovery)** — re-enabled on the Spotlight op with
   `expiry_message_contains: ["expired","not found"]`; the YAML explicitly documents
   "authored from typical phrasing — a miss degrades to a hard failure (never worse). Verify against
   live traffic." Degradation-safe and marked verify-live, as A5 required. ✓
8. **Contract drift** — no execution step contradicts constraints.md or decisions.md. The item-1 fix
   is the enabler A5 named; `mode: collect` + `group` is rejected at load per decisions.md; falcon
   spine filter stayed native-identical (bare `last_seen_timestamp:>=`), matching the reversed
   decision.

## Gaps / Risks

1. **[Medium] One `aid: null` / no-`findings`-key envelope violates literal criteria 3b/3c.**
   The unmanaged host with a non-32-hex composite-id suffix falls through `first_of` to null and is
   published as `{aid: null}` with no `findings` key. Native's AidExtractor also fails on it but
   native **drops** the record; the yaml route **emits** it. Documented as a booked delta and claimed
   "parser isolates," but this was *discovered during execution*, not pre-authorized in constraints.md,
   and I did not read the correlated parser to confirm a null-aid / missing-findings-key spine record
   is genuinely a harmless no-op (vs a throw or a phantom asset). This is the one substantive open item.
2. **[Low] Full adapters *solution* suite not independently verified** (only the engine project, per
   check scope). Criterion 1c relies on the worker claim.
3. **[Low] +833 drift attribution is plausible but not provable from artifacts.** It reconciles exactly
   with the total delta and is direction-consistent with the run timing (yaml ran 2.3h later), but a
   single host going 0→833 is a large swing that cannot be independently confirmed as vendor-side drift
   from the saved outputs alone.
4. **[Low, accepted] A5 cursor_recovery expiry substrings unverified against live Falcon 404 bodies.**
   Degradation-safe by design; flagged verify-live in the YAML. No run this session hit an expiry.

## Recommendations

1. Confirm the `CrowdstrikeAssetsFindingsCorrelated` parser tolerates a `{aid: null}` record with no
   `findings` key (build spine from `chunk == 0` — does a null aid produce a phantom asset or a safe
   skip?). If tolerance is not certain, consider a follow-up to let the engine drop records whose
   mapped correlation key resolves empty (native-parity skip), rather than publishing a null-aid
   envelope — but that is a new engine capability, out of this task's scope.
2. Run the full adapters solution test suite once before merge to close criterion 1c.
3. Leave items 3 and 4 as accept-and-monitor; both are explicitly booked and degradation-safe.
