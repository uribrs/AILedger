# Verifier-1 — tenableio-correlated-parser (commit 56d2e45, not pushed)

## Verdict: PASS-WITH-GAPS

The correlated collector-v5 envelope lane is implemented correctly and satisfies
the USER's actual intent (behavior parity + alignment of the Tenable.io parser to
the new single-lane envelope shape). Every functional Success Criterion is met,
verified by tests I ran and by real-data inspection. The gaps are non-code:
a stale codex-state mirror (task-state conflict) and two documented/theoretical
edge cases. None block the parser.

### D7 pivot judgment
The pivot (correlated lane inside the hand-written delegate rather than a
declarative yaml_engine lane) is **evidence-backed and correct**. The finding
`additional_fields` contract is a dynamic per-sub-field plugin/root walk
(`tenableAssetsAndFindings.py:548-575`); the declarative engine's `dynamic` mode
would `to_json` the plugin struct wholesale or freeze ~50 drifting keys — either
is a parity downgrade. Deriving the two split lanes from the envelope and reusing
the split machinery verbatim gives exact field parity **by construction**. The
USER pointed at real envelope data and said "time to align the parser" — intent
is parity, not a file layout. Pivot satisfies intent. Same extension pattern as
the existing NotHydrated split handler (repo precedent).

## Success Criteria

| Criterion | Status | Evidence |
|---|---|---|
| One asset per `host.id` across multi-chunk envelopes | MET | `dropDuplicates(["_asset_correlation_id"])` `tenableAssetsAndFindings.py:517-522`; test `test_tenable_assets_findings_correlated.py:99-100` (C1 two chunks → 1 asset); harness fixture host `49c8...d2ec` spans 2 chunks → 1 asset row in `findings_correlated.expected_assets.json` (3 assets total) |
| Every finding carries `asset_id` of its host's surviving asset | MET | LEFT-join `_finding_asset_uuid == _asset_correlation_id` `tenableAssetsAndFindings.py:605-619`; test asserts no null asset_id and C1's 3 findings share one asset id `:113-120`; expected snapshot: 8 finding rows all resolve one asset_id, zero null |
| Zero-vuln assets survive | MET | assets lane built independently of findings; test `:122-125` (C3-NOVULN survives, no finding points at it); all-zero batch test `:139-185`; fixture host `1854...d35` (empty findings) present in expected assets |
| Field parity vs hand-written split baseline | MET-via-construction | correlated derives `host.*` → `_build_asset_envelope_df` (same 11-field envelope incl. `ratings.acr.score`→`acr_score_v3`) and `explode(findings)` → raw vulns lane, then split `process()` runs verbatim; ACR risk_score populated 8.0/5.0/4.0 in expected assets |
| Split mode untouched / byte-equivalent | MET | `NotHydrated` handler untouched; `is_split` generalized to `correlates_by_uuid = is_split or self._correlated_input` (`:454`). When `input_mode=="split"`: `is_split=True` → identical gate values; the 4 gate sites (`:498,517,586,605`) take the same branch. `tests/test_tenable_assets_findings_split.py` green |
| Legacy hydrated untouched | MET | `_correlated_input=False` for legacy → `correlates_by_uuid=False` → the `else` (type,value) dedup + join path (`:523-529,623-634`) unchanged. Legacy harness fixture `findings.json` + golden snapshots pass under `test_run_parsers.py -k tenable` (6 passed). Sniff-regression test `:188-227` proves legacy rows don't trip the sniff |
| A7 resolved from repo evidence | MET | `assumptions.md:9` — legacy hydrated is harness-tested, must keep working; resolution = shape-sniff `{host,findings,isLastChunk}` + `host` is struct; legacy snapshots are the regression proof |
| Spec comments explain routing + no-stamping | MET | `tenable-assets-findings.yaml:1-19` documents three generations + why delegate; no `stamp_asset_id`/`stamped_uuid` anywhere (`grep` clean) |
| New tests over multi-chunk/join/zero-vuln/routing | MET | `test_tenable_assets_findings_correlated.py` 3 tests, 3/3 green; harness fixture cut from real 17GB run |
| Full repo test suite green | MET (with pre-existing noise) | correlated+split: 6 passed; harness tenable: 6 passed. Execution notes: full suite 183 passed / 10 skipped / 5 errors — all 5 = `test_wheel_packaging.py`, pre-existing local-env, reproduce on clean tree (not re-run here; claimed) |
| Task artifacts updated + mirrored to codex-state | PARTIAL | repo `state.json`/`execution_notes.md`/`assumptions.md`/`decisions.md` internally consistent; **codex-state mirror is stale** — see Finding 1 |

## Findings

**1 — MED — codex-state mirror is stale / conflicts with repo task state.**
`constraints.md:11` requires the mirror updated at close and SC requires artifacts
"mirrored to codex-state"; the contract lists "Any conflict between task state
files" as a stop condition.
`/Users/user/codex-state/tasks/cymulate-integration-parsers/2026-07-08_1739_tenableio-correlated-parser/state.json`
still shows `S6 in_progress`, `S7 pending`, and `contract-driven-execution.completedAt: null`
with empty `outputs`, while the repo `state.json` shows both done and full outputs.
`execution_notes.md` in the mirror also DIFFERS (decisions.md/assumptions.md do match).
Not a code defect; reconcile the mirror at pipeline close. Note the repo
`state.json` itself is correctly pre-verifier (`verifierRun:false`).

**2 — LOW — stray `assets.json` alongside correlated `findings_*.json` would misroute to split and misparse.**
The sniff only runs in the hydrated branch (`tenableAssetsAndFindings.py:413-427`).
If a stray `assets*.json` co-existed with correlated envelopes, `input_resolver`
picks `split`, and `NotHydrated` would read the envelope `findings_*.json` as raw
`/vulns/export` rows (`tenableAssetsAndFindingsNotHydrated.py:114-125`) — wrong
shape, no envelope unwrap. Does not occur with the v5 collector (emits findings
lane only); acknowledged in `execution_notes.md:38` for the inverse legacy case.
Theoretical; no guard. Acceptable given the collector contract.

**3 — LOW — multi-chunk dedup picks an arbitrary host row if chunks disagree.**
`dropDuplicates(["_asset_correlation_id"])` (`:522`) keeps a non-deterministic row
among a host's chunk envelopes. Harmless because the collector repeats identical
`host` content per chunk (confirmed in fixture: both chunks of `49c8...d2ec` carry
acr 5.0). If a future collector emitted divergent host fields per chunk, asset
attributes would be non-deterministic. Not a current risk; worth a one-line note.

**4 — INFO (not a gap) — informational/empty-CVE findings are dropped downstream.**
Shared `post_process` drops `type=vulnerability` rows with empty `cve_ids`, so
host1's lone info finding and the "Curl Installed" info row never publish; the
8 expected finding rows are the per-CVE explode of the 3 CVE-bearing plugins on
`d2ec`. This is mode-agnostic (identical in split today) — parity preserved, not
a regression. Documented `execution_notes.md:27`.

## Real-data spot-check (cheap, no Spark)
`.../20260706-102603/collector-run/findings_000573.json` (smallest envelope):
top-level keys `{chunk,findings,findingsInChunk,host,isLastChunk,uuid}` — sniff
columns `{host,findings,isLastChunk}` present; `host` is an object with `id`;
`host.ratings.acr.score = 6.0`; `finding.asset.uuid == host.id`
(`4334be02-833a-4317-b483-5c58ae846e4d`) — the D2 uuid join key holds on real
data. Confirms `is_correlated_envelope` sniff and the correlation key against
production output.

## Edge cases checked
- Mixed batch (some envelopes with findings, some empty): covered — main test has
  C1/C2 (findings) + C3 (empty) in one batch; `_explode_findings` filters
  `size>0` so empty envelopes drop only their (absent) findings.
- All-zero-findings batch (Spark infers no element struct): explicit typed-empty
  lane `tenableAssetsAndFindingsCorrelated.py:81-97`, test `:139-185`.
- `finding.asset` missing uuid: guarded — null `_finding_asset_uuid`
  (`tenableAssetsAndFindings.py:592-596`), finding LEFT-joins to no asset.
- Sniff false-positive on legacy: legacy rows are finding-shaped (no `host`/
  `isLastChunk`), sniff returns False; test `:188-227` green.
- uuid-join case-sensitivity: correlated join is on raw ids
  (`_finding_asset_uuid == _asset_correlation_id`), no `lower()` — Tenable uuids
  are lowercase hex, no case drift; the `lower()` only applies to the legacy
  (type,value) path, untouched.

## Unverified claims
- Full-suite "183 passed / 10 skipped / 5 pre-existing wheel-packaging errors" —
  not re-run (harness known to hang on broad runs); I ran the tenable-scoped
  subsets (all green). The 5 errors' clean-tree reproduction is claimed, not
  re-verified.
- At-scale (multi-GB) parse of the full 2,168-file run not executed (by design —
  17GB); the harness fixture is a sanitized real-data cut. Sniff + uuid-join
  confirmed on one real envelope only.
