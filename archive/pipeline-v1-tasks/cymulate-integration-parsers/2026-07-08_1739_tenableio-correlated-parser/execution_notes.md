# Execution Notes

## 2026-07-08 — correlated lane implementation (contract-driven-execution)

### Design pivot (D7)

The contract's Goal said "new declarative yaml_engine lane". Execution evidence overturned that premise:

- Today's finding `additional_fields` contract is the hand-written **dynamic per-sub-field plugin walk** (every non-mandatory `plugin.*` field flattened under its own name, structs `to_json`'d, arrays kept). The engine's `dynamic` additional_fields mode walks top-level columns and would `to_json` the plugin struct wholesale; an explicit map would freeze ~50 drifting plugin keys. Either way: a parity break.
- The split path already owns everything else the correlated shape needs: `_build_asset_envelope_df` (assets-export → embedded-asset envelope: `id`→`uuid`, `ipv4s[0]`→`ipv4`, `fqdns[0]`→`fqdn`, `netbios_names[0]`→`netbios_name`, `operating_systems`→`operating_system`, `tags{uuid,key,value}`→`tag_*`, `ratings.acr.score`→`acr_score_v3` null-safe), uuid dedup, LEFT-join `findings.asset.uuid == assets.id`.
- The correlated envelope is literally a repackaging of the two split lanes: `host` = raw /assets/export row, `findings[]` = raw /vulns/export rows.

So: derive the two lanes from the envelope and run the split machinery verbatim. Exact parity by construction. Repo precedent for this lineage (NotHydrated handler, CrowdStrike correlated) does the same.

### Changes

- **NEW** `libs/packages/parsers/deprecated/tenable/tenableAssetsAndFindingsCorrelated.py` — envelope sniff (`{host, findings, isLastChunk}` + `host` is struct) and lane derivation: `host.*` → `_build_asset_envelope_df`; `explode(findings)` → findings lane; all-zero-findings batch (Spark infers no element struct) → typed empty lane.
- **EDIT** `tenableAssetsAndFindings.py` — `self._correlated_input` flag; hydrated branch sniffs and dispatches; `is_split` gates generalized to `correlates_by_uuid = is_split or self._correlated_input` (4 gate sites: keep `_asset_correlation_id`, dedup-by-correlation-id, capture `_finding_asset_uuid`, uuid join). Split and legacy hydrated code paths bit-identical.
- **EDIT** `specs/tenable-assets-findings.yaml` — comments document the three input generations and why the parser stays a delegate.
- **NEW** `tests/test_tenable_assets_findings_correlated.py` — 3 tests (multi-chunk dedup + join convergence + no-vuln survival + additional_fields spot-checks; all-zero-findings batch; legacy-hydrated sniff regression).
- **NEW** `test_files/tenable/assets_and_findings/findings_correlated.json` + `.expected_assets.json`/`.expected_findings.json` — sanitized real-data cut (4 envelopes / 3 hosts incl. a fabricated 2-chunk split of a real host, CVE-bearing findings preferred); runs in the generic harness (`test_run_parsers.py`) alongside the legacy fixture.

### Validations

- New unit tests: 3/3 green. Tenable split (3) + ACR (2) regressions: green.
- Harness correlated case: green; golden snapshots show 3 assets (multi-chunk host collapsed to ONE row), 8 finding rows (per-CVE explode), every finding linked to the multi-chunk host's single asset id, `risk_score` populated from real `ratings.acr.score` values (8.0/5.0/4.0).
- Discovered en route (platform behavior, mode-agnostic, NOT changed): shared `post_process` drops `type=vulnerability` findings with empty `cve_ids` — informational plugins never reach downstream, in split mode today just the same.
- Real-data facts recorded: 2,168 envelope files / 17 GB run contains ZERO multi-chunk envelopes (no host >2,000 findings on this tenant); zero-findings sweep envelopes cluster at the run tail; `host.ratings.acr.score` present on all sampled records; finding root keys include `last_fixed`/`recast_*`/`resurfaced_date`/`time_taken_to_fix` (covered by the dynamic root walk).
- Fixture sanitization: deterministic uuid/ip/hostname remaps (192.0.2.x TEST-NET, `*.example.test`), regex sweep over fixture+snapshots found zero remaining customer identifiers.

### Environment notes

- Tests: `JAVA_HOME=/opt/homebrew/opt/openjdk@11/libexec/openjdk.jdk/Contents/Home PYTHONPATH=libs/packages .venv/bin/pytest tests/ -q` (uv not on PATH in this harness; repo venv works).

### Residual risks / open items

- Full-suite run: 183 passed / 10 skipped / 5 errors — all 5 errors are tests/test_wheel_packaging.py and reproduce on a CLEAN tree (local venv lacks the setup.py bdist_wheel tooling); pre-existing environment issue, unrelated to this change.
- The correlated sniff keys on `{host, findings, isLastChunk}` top-level columns; a hypothetical legacy combined file that ALSO had those exact columns would misroute — no such shape exists in the legacy contract (its rows are finding-shaped).
- Real-data at-scale smoke (multi-GB) not run in-repo; the harness fixture is a real-data cut. Optional follow-up if wanted.

## 2026-07-09 — pipeline close (orchestrator)

- Verifier (review/verifier-1.md): PASS-WITH-GAPS. Confirmed on a real production envelope: sniff columns present, `finding.asset.uuid == host.id` holds, `ratings.acr.score` live. MED (stale codex-state mirror) fixed; LOW stray-assets.json and LOW chunk-content-disagreement accepted (cannot occur with the v5 collector; the latter now documented as a collector invariant in the handler docstring).
- Code review (review/code-reviewer-1.md): Approve, no blockers/majors. Regression trace confirmed bit-identity for split and legacy hydrated; multi-file `findings_*.json` production load path traced through `helpers._resolve_file_paths` fallback. Repaired: collector-invariant docstring. Accepted: all-null-host sniff fall-through (pathological; pre-existing failure shape), redundant pre-explode filter (self-documenting idiom).
- Final commit: 1e8a880 (amend of 56d2e45 with the docstring repair). NOT pushed — awaiting operator go-ahead.
- Note: two code-reviewer agent runs stalled on 2026-07-08 evening against an API availability blip (the permission classifier was down at the same time); third run completed normally.
