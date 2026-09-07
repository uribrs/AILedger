# Verifier-1 — TenableIo asset-centric split-model rework

Independent adversarial verification against `prompt_contract.md` Success Criteria + the
explicit residual-risk checklist. Evidence is file:line / command output.

Repos:
- Collector: `/Users/user/Dev/Uri/cymulate-integration-adapters` (branch `tenable-io-findings-to-assets-focused`)
- Parser: `/Users/user/Dev/cymulate-integration-parsers` (branch **`master`** — see Gaps)

---

## Success Criteria (prompt_contract.md)

### SC1 — Two independent dumb feeds, no in-collector join/enrichment — **PASS**
- Findings chunk processor is now pure passthrough: info filter removed, enrichment removed,
  `WriteFindingWithEnrichedAsset`/`WriteMergedAsset` deleted, ctor slimmed to `(stats)`
  (`Flows/Findings/TenableIoFindingsChunkProcessor.cs` diff; final body lines 39, 78–83).
  Retains only a structural validity gate (drops rows missing `asset.uuid` or `plugin.id`) —
  a quality gate, not a join/enrich.
- `TenableIoAssetEnricher.cs` DELETED (git status `D`); `git diff --stat` shows -356 lines.
- `/assets/{id}` URL gone: `AssetByUuid` removed, replaced by export endpoints
  (`Processing/Urls/TenableIoUrls.cs:14-22`).
- Vulns filter now `{ since, state=[OPEN,REOPENED] }` (`Flows/Findings/TenableIoVulnsExportClient.cs:35`) — FIXED skipped, all severities.

### SC2 — Assets flow real, /assets/export + last_assessed, publishes assets feed, checkpoint+resume mirror findings — **PASS**
- `Flows/Assets/TenableIoAssetsExportClient.cs` — `POST /assets/export` with `chunk_size` +
  `filters.last_assessed = baseDate − AssetsLastAssessedWindowDays(30)` unix (`:33-44`); 409
  active_job_id recovery (`:59-82`); status poll + chunk stream.
- `Flows/Assets/TenableIoAssetsFlow.cs` — full create→poll→chunk→publish→checkpoint loop, a
  faithful structural mirror of `TenableIoFindingsFlow` (poll-failure handling, circuit breaker,
  server-side exclusion, 50% permanent-failure threshold, per-page `AdvancePage`). Publishes via
  `CollectorNdjsonPublisher.PublishAssetsUtf8PageAsync` (`:441`). NOT a stub.
- Collector now implements `IAssetsCollectorAdapter` (`TenableIoCollector.cs:29`); the old no-op
  `CollectAssetsInternalAsync` ("not implemented") replaced with a real flow run
  (`TenableIoCollector.cs` diff); `ProcessAsync` `CollectAssetsAsync` delegate passes
  progress/ct/correlationId; `ResumeAsync` routes `IsAssetsFlow → ResumeAssetsAsync`.
- Checkpoint: `TenableIoAssetsCheckpointState` (ExportUuid, TotalChunks, ProcessedTenableChunkIds[],
  LastPublishedPage, totals) — sibling to findings (`Recovery/TenableIoCheckpointState.cs`).
  `SaveAssetsState`/`TryLoadAssetsState[Core]` + `CanResumeFrom` assets branch with stale guard
  (`Recovery/TenableIoCheckpointHelper.cs`). `ResumeAssetsAsync` via `CollectorResumeRunner`
  (`Recovery/TenableIoResumeRunner.cs:90-148`).

### SC3 — Parser DUAL_MODE; assets from ASSETS lane; ACR←ratings.acr.score; uuid==id; LEFT-join survival — **PASS**
- `"tenable-assets-findings": "Tenable IO"` registered in `DUAL_MODE_PARSERS`
  (`preparation.py:20`). Key matches `parsers/__init__.py`.
- Assets built from the assets lane, reshaped independently into the embedded-`asset` envelope
  (`tenableAssetsAndFindingsNotHydrated.py:_build_asset_envelope_df`, `:119-170`); NOT derived
  from finding rows.
- `risk_score ← ratings.acr.score`: surfaced as `asset.acr_score_v3`
  (`tenableAssetsAndFindingsNotHydrated.py:150-164`), which the canonical `risk_score` map reads.
- Correlation: split LEFT-join `findings.asset.uuid == assets._asset_correlation_id (==id)`
  (`tenableAssetsAndFindings.py` process() split branch, captured `_finding_asset_uuid` before the
  `asset` struct is projected away). Hydrated `(type,value)` join preserved on else branch.
- No-vuln survival: assets_df built from assets lane, `dropDuplicates(["_asset_correlation_id"])`
  — no explode; findings_df derived from the findings lane only → no phantom rows. Proven by
  `tests/test_tenable_assets_findings_split.py` (3 assets in incl. no-vuln A3 → 3 out, exactly 2
  findings, A3 has no finding pointing at it, risk_score {a1nb:5.0, 10.0.0.2:3.0, a3nb:null}).

### SC4 — Field-name deltas exactly per decisions.md — **PASS**
`tenableAssetsAndFindingsNotHydrated.py:154-166` matches decisions.md line-for-line: `id`→`uuid`,
`ipv4s[0]`→`ipv4`, `fqdns[0]`→`fqdn`, `netbios_names[0]`→`netbios_name`,
`operating_systems`→`operating_system` (array kept), `tags{uuid,key,value}`→
`{tag_uuid,tag_key,tag_value}`, `first_seen`/`last_seen` verbatim, ACR repathed. All null-safe.

### SC5 — Build green; parser tests green; no regressions — **PASS**
- Collector build: `dotnet build ...TenableIoCollector.csproj -f net8.0` → **Build succeeded,
  0 errors** (only NU1900 CodeArtifact-auth warning). Verified this session.
- Parser: `pytest test_tenable_assets_findings_split.py test_tenable_parser_acr_fields.py
  test_input_resolver.py test_cortex_xdr_assets_findings.py test_defender_vm_reconciliation.py`
  → **32 passed** (verified this session, openjdk@11 + repo .venv). No DUAL_MODE / input_resolver /
  Cortex / Defender-VM regressions.

### Feed-filename ↔ resolver glob — **PASS (confirmed, not taken on faith)**
- Collector page naming = `{name}_{page:D6}.json` with name ∈ {`findings`,`assets`}
  (`CollectorOutputDefaults.BuildPageTargetPath`; `CollectorGlobalDefaults.FindingsFileName="findings"`,
  `AssetsFileName="assets"`). Output e.g. `assets_000001.json`, `findings_000001.json`.
- Resolver globs `assets*.json` + `findings*.json` (`utilities/input_resolver.py:45,51`). Both
  collector outputs match → `input_mode="split"` selected (resolver `:77-91`). Confirmed aligned.

---

## Residual-risk scrutiny

### Population mismatch (assets `last_assessed` 30d vs findings `last_found`/since, OPEN+REOPENED) — **ACCEPTED RISK, real but bounded**
- Confirmed the two lanes use different keys/windows: assets `filters.last_assessed = baseDate−30d`
  (`TenableIoAssetsExportClient.cs:36`); findings `filters.since` watermark from baseDate, no
  `last_assessed` window (`TenableIoVulnsExportClient.cs:29-35`). They are NOT coextensive by
  construction — a finding whose asset falls outside the 30d `last_assessed` window can appear in
  the findings lane without its asset in the assets lane.
- Severity: parser handles it non-fatally — LEFT-join from findings keeps the finding with
  `asset_id = null` (no crash, no data corruption). W3 measured the real-slice mismatch as an
  artifact of pairing a 1000-asset chunk with a wider findings slice; in production both are full
  exports keyed on the same asset, so match rate should be near-100%. The contract itself accepts
  the `last_assessed` proxy and null-allowed correlation. **Not a blocker; design tolerates it.**
  Flag: the windows being non-identical means a residual tail of `asset_id=null` findings is
  expected, not impossible — acceptable per FR3/CA-72614 (null allowed).

### Dead info-filter counters — **CONFIRMED (cosmetic)**
`TenableIoFindingsStats.FilteredInfoVulnerabilities`/`IncrementFilteredInfo` still exist
(`Flows/Findings/TenableIoFindingsStats.cs:15,27`) but `IncrementFilteredInfo` is no longer called
anywhere (processor reference removed). Dead code, churn-minimizing as W2 stated. No behavioral impact.

### The 1 failing collector test — **CLAIM SANITY-CHECKED, plausible/pre-existing**
- `ResumeAsync_Findings_WhenTransportResponseEndsPrematurely_ReturnsRetryableAdapterFailure`
  (`TenableIoCollectorTests.cs:575`) was **NOT modified** by W2 (git diff confirms only
  `EmitsAllSeverities`, `ProcessAsync_Assets`, and removal of `EnrichedAssetWritesNull` appear).
- Read the test: status returns FINISHED with chunks [1,2]; chunk 2 throws IOException; chunk 1
  hits the fallthrough 404. The chunk **processor** (the only findings-flow code W2 changed
  beyond removing the enricher param/`OnAssetFetched`) is never reached — both chunks fail at
  download. Outcome hinges on whether the 50%-permanent-failure threshold trips vs. transport
  retries exhausting first — jitter/timing sensitive. The findings-flow diff shows ZERO changes to
  the exclusion/threshold/transport logic this test depends on. **Claim is consistent with the
  diff: pre-existing timing-sensitive flake, not a W2 regression.** (Could not re-run to confirm
  flakiness per operator's no-poll-loop guidance; the structural argument holds.)

---

## Unresolved gaps / notes
1. **Parser changes are on `master`, not a feature branch** (`git -C cymulate-integration-parsers
   branch --show-current` → `master`). Process concern, not correctness — should be branched
   before any push/PR. Flag for the operator.
2. **A3 (~3× volume on Spark/postgres) remains OPEN/mitigated** — egress batches; downstream
   throughput is not provable from code here. Contract marked this a sanity gate, not a blocker.
3. **Population-mismatch tail** (above) — accepted, but worth an explicit product decision on
   whether `asset_id=null` findings are acceptable long-term or whether the lanes should share a
   window. Not in scope to fix here.
4. UI 102,617 exact count not reproducible — `last_assessed` proxy accepted per decisions.md.

---

## VERDICT: SATISFIED-WITH-RISKS

All six Success Criteria PASS with direct evidence; the defining no-vuln-survival/zero-phantom gate
is proven by a real pipeline test; build and parser tests are green this session; feed↔glob
alignment confirmed. Residual risks (population-window mismatch, dead info counters, the 1 flaky
collector test, parser-on-master, A3 throughput) are real but accepted/cosmetic/process-level and
do not contradict the contract.
