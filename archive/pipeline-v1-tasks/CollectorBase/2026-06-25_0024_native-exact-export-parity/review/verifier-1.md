# Verifier report — native-exact export parity

Verdict: **PASS** (all 5 Success Criteria met). Build clean; 56/56 tests green (52 prior + 4 new).
Two cosmetic, non-blocking observations recorded below. No must-fix findings.

## Build & test
- `dotnet build CollectorBase.slnx` → 0 Error(s).
- `dotnet test CollectorBase.slnx` → `Passed! - Failed: 0, Passed: 56, Skipped: 0, Total: 56`.
- New tests present & passing: `Emit_Verbatim_PassesFullRecordThrough_NoDiscriminator`,
  `Emit_TypedWrapper_WrapsFullRecordUnderData`, `Emit_SourceTypePrefix_FlattensRecordWithTemplatedMetadata`,
  `Emit_Mapped_DefaultMode_StillProjectsSubset`.

## Per-criterion

### SC1 — build clean — PASS
0 errors (background build, exit 0).

### SC2 — all tests pass incl. 4 new — PASS
- (a) verbatim FULL record / no injection: `Interpreter.cs:98-106` — for `verbatim|typed_wrapper|source_type_prefix`
  the mapper does `output.Add((JsonObject)src.DeepClone())` and `continue`s BEFORE any `sourceType` /
  field / correlation injection. Test asserts emitted record has exactly `{id,name,nested,score}`, no
  `sourceType`, nested object intact (`CollectorExecutorTests.cs:1443-1447`). PASS.
- (b) typed_wrapper `{type,data}`: `CollectorExecutorRunner.ApplyEnvelope:897-900` builds
  `{ WrapperKey: typeVal, DataKey: rec }`. Test asserts exactly `{data,type}` keys, `type=machine`,
  data carries the full record (`:1461-1465`). PASS.
- (c) source_type_prefix with templated metadata: `ApplyEnvelope:904-909`. Test drives a real `for_each`
  (`over_values:[R-1]`, `as: rec_id`, `envelope_fields:{recommendationReference:"{{rec_id}}"}`) and asserts
  the emitted record's `recommendationReference == "R-1"` (`:1480-1484`). **Not tautological** — it proves the
  for_each item token actually flows into the envelope template render end-to-end.
- (d) mapped unchanged: default `EmitMode="mapped"` (`Profile.cs:164`); mapper's non-passthrough branch
  (`Interpreter.cs:107-117`) is the original logic. Test asserts `{sourceType, id}` only; `name/nested/score`
  NOT passed through (`:1498-1502`). PASS.

### SC3 — Tenable verbatim + Defender shapes & completed scope — PASS
- `integrations/tenable-io.yaml`: both streams' chunk fetch use `emit_mode: verbatim`, `records_path:"$"`
  (`:55-57`, `:91-93`). Matches native TenableIo*ChunkProcessor "dumb passthrough — no wrapper/sourceType/
  correlation."
- `integrations/defender-vm.yaml` shapes match `DefenderVmRecordFormatter.cs` exactly:
  - machines/software → `typed_wrapper` type machine/software (`:34,:42`) ↔ `Wrap("type"/"data")` (`Formatter:34-45`).
  - vulns→inventory, changes→delta, recs→recommendationCatalog, recVulns→recommendationScopedVulnerability
    with `envelope_fields.recommendationReference:"{{rec_id}}"` (`:56,:66,:74,:90-94`) ↔
    `AddSourceType`/`AddMetadata` (`Formatter:22-32, 50-85`).
  - `ApplyEnvelope` flatten-skip (`:906-908 if (!o.ContainsKey)`) reproduces native dropping a pre-existing
    `sourceType`/`recommendationReference` before re-adding (`Formatter:66-80`).
- Completed scope verified against `DefenderVmUrlBuilder.cs`: machines `$top+$filter=lastSeen ge <base>`
  (Builder:21-28), software `$top` (30-33), vulns `pageSize=` (35-39), changes `sinceTime+pageSize=`
  (59-68), recommendations + per-rec `/vulnerabilities?$top` (41-57). vuln-changes stage present;
  recommendations→capture_list→for_each recVulns present. All native endpoints/stages reproduced.

### SC4 — parity note with citations + deferred vendors — PASS
`parity-note.md` maps every native formatter method → YAML emit_mode+type_value with file:line, documents
the two date-logic gaps, and explicitly lists cortex-xdr / crowdstrike-falcon / qualys as deferred (still on
`mapped`). Deferred profiles confirmed untouched (mtime Jun 18 vs Jun 25 for the two changed).

### SC5 — no regression — PASS
- poll_and_drain (9 tests), resume (4), reserved `__` guard, ingress all present & green.
- `mapped` default path byte-identical to prior; only the new modes branch early.

## Skeptical checks
1. verbatim drops/leaks nothing — confirmed (`Interpreter.cs:102-106`, DeepClone + continue before injection).
2. typed_wrapper EXACTLY `{type,data}`; source_type_prefix `{sourceType,<meta>,...record}` with the
   skip-existing rule matching native `AddMetadata`. Templated `{{rec_id}}` genuinely resolved from for_each
   scope (`Runner:395` injects extraTokens into `ctx`; `BuildItemTokens:616` sets `[step.As]`). Test non-tautological.
3. mapped default unchanged; prior 52 pass unmodified.
4. YAML vs native verified file:line. Base-date gaps (180d remap `Builder:11-19`, 14d lookback `Builder:70-76`)
   are genuinely computed date-logic, NOT templatable — honestly documented, correctly out-of-scope per the
   "resist inner-platform creep" execution rule. No hidden correctness bug.
5. `allow_offset_fallback:false` suppresses ONLY the offset fallback; the `@odata.nextLink` branch
   (`NextUrlPaginationStrategy.cs:22-24`) runs first and is unaffected — a legit multipage nextLink response
   still paginates fully. Mirrors native `ResolveNextPageUrl` (Builder:78-104) + `AllowsSyntheticSkipPagination`
   false for exactly Vulnerabilities+VulnerabilityChanges (FindingsFlow:204-206). Correct; no early data loss.
6. Ordering correct: envelope applied AFTER watermark read (Runner:454-458) and BEFORE slice/publish
   (`:463-464`, slice `:472`). No reparent throw: mapper hands ApplyEnvelope detached DeepClones; typed_wrapper
   assigns `rec` (already detached) as a child once; source_type_prefix DeepClones each prop. No double-parent.
7. No regression in poll-and-drain / bus / resume / page-counter / ingress / `__`-guard.

## Observations (non-blocking, cosmetic — not must-fix)
- **O1 (low):** `ApplyEnvelope` skip-existing uses `o.ContainsKey` which is case-SENSITIVE, whereas native
  `AddMetadata` skips `sourceType`/`recommendationReference` case-INSENSITIVELY (`Formatter:68,74` OrdinalIgnoreCase).
  A raw record carrying a differently-cased `SourceType` would be kept by us but dropped by native. Not a
  realistic concern for these vendor payloads (Defender emits canonical lowercase); flagging for completeness.
- **O2 (low/cosmetic):** defender software/recs/per-rec-vuln stages template `$skip={{offset}}` in the
  page-1 query; native software/vulns URLs omit `$skip` on the first request and inject it only via the
  nextLink/BuildSkipUrl fallback. Page-1 `$skip=0` is a no-op; subsequent pages are driven by `@odata.nextLink`
  exactly like native. Functional parity holds; only the first-request query string differs cosmetically.

- **Validation gap (informational):** emit_mode/type_value validation runs in `ValidateFetchStep` (Profile.cs:261-279),
  which covers steps-based streams (both Tenable & Defender use steps) but NOT the single-fetch sugar path
  (`:245-252`). No shipped single-fetch profile uses the new modes, so no live impact; would matter only if a
  future single-fetch profile sets `emit_mode` directly.
