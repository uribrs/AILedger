"""EMPIRICAL PROBE (temporary): what does the correlated Falcon parser do with an
AID-less record? Not a permanent test — prints actual output for a written report."""
from __future__ import annotations

import json
import uuid

import pytest

from parsers import build_default_parser_options
from parsers.deprecated.crowdstrike.crowdstrikeAssetsFindings import CrowdstrikeAssetsFindingsParser


# ---------------------------------------------------------------- builders
def _write_ndjson(path, rows):
    path.write_text("".join(f"{json.dumps(row)}\n" for row in rows), encoding="utf-8")


def _build_parser(base_path, spark_session, logger, *, input_mode="hydrated"):
    env_vars = {
        "ZIP_FILE_KEY": str(base_path),
        "FLOW_NAME": "CrowdStrike Assets and Findings",
        "INTEGRATION_SETTING_ID": str(uuid.uuid4()),
        "INTEGRATION_SETTING_FLOW_ID": str(uuid.uuid4()),
        "CLIENT_ID": str(uuid.uuid4()),
        "INSTANCE_ID": str(uuid.uuid4()),
        "CLIENT_INTEGRATION_ID": str(uuid.uuid4()),
    }
    options = build_default_parser_options(
        base_file_path=str(base_path), env_vars=env_vars, logger=logger,
        spark_session=spark_session, glue_context=None, is_glue_env=False,
    )
    options["input_mode"] = input_mode
    return CrowdstrikeAssetsFindingsParser(options, connection_manager=None)


def _managed_host(*, aid, hostname, discover_id=None):
    return {
        "id": discover_id or f"cid_{uuid.uuid4().hex}",
        "cid": "cid-test",
        "aid": aid,
        "entity_type": "managed",
        "hostname": hostname,
        "platform_name": "Windows",
        "os_version": "Windows 11",
        "first_seen_timestamp": "2026-01-01T00:00:00Z",
        "last_seen_timestamp": "2026-05-01T00:00:00Z",
        "current_local_ip": "10.0.0.10",
        "fqdn": f"{hostname}.example.local",
        "site_name": "Default-First-Site-Name",
        "tags": ["Production"],
        "agent_version": "7.0.0",
        "reduced_functionality_mode": "No",
        "form_factor": "Virtual machine",
        "product_type_desc": "Workstation",
        "system_serial_number": "SN-123",
        "mac_addresses": ["00-0C-29-C7-0E-96"],
        "device_policies": _policy_envelope("policy-win"),
    }


def _unenriched_host(*, discover_id, hostname, ip, entity_type="unmanaged", aid_value="__absent__"):
    """A Discover asset with no sensor: verbatim, unenriched, no usable aid,
    device_policies collection_status=complete with prevention: null."""
    host = {
        "id": discover_id,
        "cid": "cid-test",
        "entity_type": entity_type,
        "hostname": hostname,
        "platform_name": "Windows",
        "os_version": "Windows 10",
        "first_seen_timestamp": "2026-02-01T00:00:00Z",
        "last_seen_timestamp": "2026-05-02T00:00:00Z",
        "current_local_ip": ip,
        "device_policies": {
            "schema_version": 1,
            "collection_status": "complete",
            "prevention": None,
        },
    }
    if aid_value != "__absent__":
        host["aid"] = aid_value
    return host


def _policy_envelope(policy_id):
    return {
        "schema_version": 1,
        "collection_status": "complete",
        "prevention": {
            "assignment": {
                "policy_type": "prevention", "policy_id": policy_id,
                "applied": True, "settings_hash": "settings-hash",
            },
            "definition_status": "resolved",
            "definition": {
                "id": policy_id, "name": "Test prevention policy",
                "platform_name": "Windows", "enabled": True,
                "description": "Policy fixture", "groups": [],
                "ioa_rule_groups": [], "prevention_settings": [],
            },
        },
    }


def _finding(*, aid, cve):
    return {
        "id": f"{aid}_{uuid.uuid4().hex}",
        "cid": "cid-test",
        "aid": aid,
        "vulnerability_id": cve,
        "vulnerability_metadata_id": "CS-V16-0000001",
        "confidence": "confirmed",
        "created_timestamp": "2025-06-03T12:22:44Z",
        "updated_timestamp": "2025-10-04T03:10:50Z",
        "status": "open",
        "cve": {"id": cve, "base_score": 7.5, "severity": "HIGH",
                "exprt_rating": "LOW", "description": f"Test vuln {cve}"},
        "remediation": {"entities": [{"id": "rem-1", "title": "Update X",
                                      "action": "Apply the vendor update"}]},
    }


def _record(*, host, findings, aid="__from_host__", chunk=0, is_last=True):
    rec = {"chunk": chunk, "isLastChunk": is_last,
           "findingsInChunk": len(findings), "host": host, "findings": findings}
    if aid == "__from_host__":
        aid = host.get("aid")
    if aid != "__absent__":
        rec["aid"] = aid
    return rec


def _dump(label, assets_df, findings_df, policies_df):
    print(f"\n{'='*78}\n### {label}\n{'='*78}")
    print(f"assets rows = {assets_df.count()}")
    cols = [c for c in ("id", "aid", "value", "type", "os_type", "ip_address", "fqdn")
            if c in assets_df.columns]
    for r in assets_df.select(*cols).collect():
        print("  ASSET", {c: r[c] for c in cols})
    print(f"findings rows = {findings_df.count()}")
    fcols = [c for c in ("asset_id", "cve_id", "name", "status", "severity")
             if c in findings_df.columns]
    for r in findings_df.select(*fcols).collect():
        print("  FIND ", {c: r[c] for c in fcols})
    if policies_df is None:
        print("policies_df = None")
    else:
        print(f"policies rows = {policies_df.count()}")
        for r in policies_df.collect():
            print("  POLICY external_id=", r["external_id"], " edges=", r["policy_edges"])


# ------------------------------------------------------- probe 1: ASSETS lane
def test_probe_assets_lane_real_fixture(spark_session, logger, capsys):
    """Does the separate CollectAssets lane already emit AID-less assets, and how
    does it identify them?"""
    import os
    from pathlib import Path
    from parsers import PARSERS, SPECS

    repo = Path(__file__).resolve().parent.parent
    base = repo / "test_files" / "crowdstrike" / "assets"
    raw = [json.loads(l) for l in (base / "assets.json").read_text().splitlines() if l.strip()]
    print(f"\nfixture rows={len(raw)}  with-aid={sum(1 for r in raw if r.get('aid'))}  "
          f"aid-less={sum(1 for r in raw if not r.get('aid'))}")

    integ = SPECS["crowdstrike-assets"]["integration"]
    env_vars = {
        "ZIP_FILE_KEY": str(base), "FLOW_NAME": integ.get("flow_name", ""),
        "INTEGRATION_SETTING_ID": integ.get("integration_setting_id", ""),
        "INTEGRATION_SETTING_FLOW_ID": integ.get("integration_setting_flow_id", ""),
        "CLIENT_ID": uuid.uuid4().hex[:24], "INSTANCE_ID": str(uuid.uuid4()),
        "CLIENT_INTEGRATION_ID": str(uuid.uuid4()),
    }
    for k, v in env_vars.items():
        os.environ[k] = v
    config = {
        "assets_file_path": str(base / "assets.json"),
        "findings_file_path": str(base / "assets.json"),
        "env_vars": env_vars, "logger": logger, "sparkSession": spark_session,
        "glueContext": None, "is_glue_env": False,
    }
    parser = PARSERS["crowdstrike-assets"](config, None)
    out = parser.run()
    assets_df = out[0]
    policies_df = out[2] if len(out) == 3 else None
    print(f"ASSETS LANE emitted rows = {assets_df.count()}")
    print("ASSETS LANE final output columns:", assets_df.columns)
    print("'aid' in final asset output columns? ->", "aid" in assets_df.columns)
    rows = assets_df.select("id", "value", "type", "fqdn").collect()
    print("first 5 emitted:", [r.asDict() for r in rows[:5]])
    print("distinct values count:", len({r["value"] for r in rows}), "of", len(rows))
    print("policies:", None if policies_df is None else policies_df.count())
    if policies_df is not None and policies_df.count():
        for r in policies_df.limit(3).collect():
            print("  POLICY", r["external_id"], (r["policy_edges"] or "")[:400])


# ------------------------------------------- probe 2: correlated AID-less variants
@pytest.mark.parametrize("variant", ["null", "empty", "absent"])
def test_probe_correlated_aidless(variant, tmp_path, spark_session, logger):
    aid_value = {"null": None, "empty": "", "absent": "__absent__"}[variant]

    managed = [
        _record(host=_managed_host(aid="aid-1", hostname="HOST-1"),
                findings=[_finding(aid="aid-1", cve="CVE-2026-0001"),
                          _finding(aid="aid-1", cve="CVE-2026-0002")]),
        _record(host=_managed_host(aid="aid-2", hostname="HOST-2"),
                findings=[_finding(aid="aid-2", cve="CVE-2026-0003")]),
    ]
    unenriched = [
        _record(aid=aid_value,
                host=_unenriched_host(discover_id=f"disc-{i}", hostname=f"UNMANAGED-{i}",
                                      ip=f"10.10.1.{i}", aid_value=aid_value),
                findings=[])
        for i in (11, 12, 13)
    ]
    _write_ndjson(tmp_path / "findings.json", managed + unenriched)

    parser = _build_parser(tmp_path, spark_session, logger)
    try:
        _out = parser.run(); assets_df, findings_df = _out[0], _out[1]
    except Exception as exc:
        print(f"\n### VARIANT {variant}: EXCEPTION {type(exc).__name__}: {exc}")
        raise
    _dump(f"VARIANT aid={variant}  (2 managed + 3 unenriched hosts in one lane)",
          assets_df, findings_df, parser.policies_df)


# ------------------------------- probe 3: batch that is ENTIRELY AID-less
def test_probe_correlated_all_aidless_absent_key(tmp_path, spark_session, logger):
    rows = [
        _record(aid="__absent__",
                host=_unenriched_host(discover_id=f"disc-{i}", hostname=f"UNMANAGED-{i}",
                                      ip=f"10.10.1.{i}"),
                findings=[])
        for i in (11, 12, 13)
    ]
    _write_ndjson(tmp_path / "findings.json", rows)
    parser = _build_parser(tmp_path, spark_session, logger)
    try:
        _out = parser.run(); assets_df, findings_df = _out[0], _out[1]
    except Exception as exc:
        print(f"\n### ALL-AIDLESS (aid key absent everywhere): "
              f"EXCEPTION {type(exc).__name__}: {exc}")
        return
    _dump("ALL-AIDLESS (aid key absent everywhere)", assets_df, findings_df, parser.policies_df)


def test_probe_correlated_all_aidless_null(tmp_path, spark_session, logger):
    rows = [
        _record(aid=None,
                host=_unenriched_host(discover_id=f"disc-{i}", hostname=f"UNMANAGED-{i}",
                                      ip=f"10.10.1.{i}", aid_value=None),
                findings=[])
        for i in (11, 12, 13)
    ]
    _write_ndjson(tmp_path / "findings.json", rows)
    parser = _build_parser(tmp_path, spark_session, logger)
    try:
        _out = parser.run(); assets_df, findings_df = _out[0], _out[1]
    except Exception as exc:
        print(f"\n### ALL-AIDLESS (aid null everywhere): EXCEPTION {type(exc).__name__}: {exc}")
        return
    _dump("ALL-AIDLESS (aid null everywhere)", assets_df, findings_df, parser.policies_df)


# ---------------- probe 4: identity — what asset_match_key would an AID-less row get
def test_probe_asset_match_key_identity(tmp_path, spark_session, logger):
    """Compare, for a managed host, the asset output's `aid` column against the
    policy edge's asset_match_key. These must agree for a join to resolve."""
    _write_ndjson(tmp_path / "findings.json", [
        _record(host=_managed_host(aid="aid-1", hostname="HOST-1", discover_id="DISCOVER-ID-1"),
                findings=[_finding(aid="aid-1", cve="CVE-2026-0001")]),
        _record(aid=None,
                host=_unenriched_host(discover_id="DISCOVER-ID-2", hostname="UNMANAGED-2",
                                      ip="10.10.1.99", aid_value=None),
                findings=[]),
    ])
    parser = _build_parser(tmp_path, spark_session, logger)
    parser.pre_process()
    print("\n--- asset spine columns:", parser.assets_source_df.columns)
    print("--- spine aid/id/hostname rows:")
    for r in parser.assets_source_df.select("aid", "id", "hostname", "current_local_ip").collect():
        print("   ", r.asDict())
    pol = parser.policies_df
    print("--- policies_df:", None if pol is None else pol.count())
    if pol is not None:
        for r in pol.collect():
            print("    external_id=", r["external_id"], "edges=", r["policy_edges"])
    parser.process()
    print("--- assets_df AFTER process(), BEFORE post_process(): columns=", parser.assets_df.columns)
    for r in parser.assets_df.select("id", "aid", "value").collect():
        print("    intermediate:", r.asDict())
    parser.post_process()
    print("--- FINAL asset output columns:", parser.assets_df.columns)
    print("--- 'aid' present in FINAL asset output? ->", "aid" in parser.assets_df.columns)
    for r in parser.assets_df.select("id", "value", "type").collect():
        print("    final:", r.asDict())


# ------------- probe 5: realistic scale (lab tenant shape: 49 managed / 249 not)
def test_probe_scale_lab_tenant(tmp_path, spark_session, logger):
    managed = [
        _record(host=_managed_host(aid=f"aid-{i}", hostname=f"HOST-{i}"),
                findings=[_finding(aid=f"aid-{i}", cve=f"CVE-2026-{i:04d}")])
        for i in range(49)
    ]
    unenriched = [
        _record(aid=None,
                host=_unenriched_host(discover_id=f"disc-{i}", hostname=f"UNMANAGED-{i}",
                                      ip=f"10.10.{i // 256}.{i % 256}", aid_value=None),
                findings=[])
        for i in range(249)
    ]
    _write_ndjson(tmp_path / "findings.json", managed + unenriched)
    parser = _build_parser(tmp_path, spark_session, logger)
    out = parser.run()
    assets_df, findings_df = out[0], out[1]
    rows = assets_df.select("value").collect()
    values = [r["value"] for r in rows]
    unmanaged_out = [v for v in values if v and v.startswith("unmanaged-")]
    managed_out = [v for v in values if v and v.startswith("host-")]
    print(f"\n### SCALE: input 49 managed + 249 unenriched (aid=null)")
    print(f"    assets emitted           = {assets_df.count()}")
    print(f"    managed assets emitted   = {len(managed_out)} of 49")
    print(f"    unenriched assets emitted= {len(unmanaged_out)} of 249  -> {unmanaged_out}")
    print(f"    findings emitted         = {findings_df.count()} (expect 49)")


# ------------- probe 6: determinism of the single collapsed survivor
@pytest.mark.parametrize("run_no", [1, 2, 3])
def test_probe_collapse_survivor_determinism(run_no, tmp_path, spark_session, logger):
    rows = [
        _record(aid=None,
                host=_unenriched_host(discover_id=f"disc-{i}", hostname=f"UNMANAGED-{i}",
                                      ip=f"10.10.1.{i}", aid_value=None),
                findings=[])
        for i in range(20)
    ]
    rows.append(_record(host=_managed_host(aid="aid-keep", hostname="HOST-KEEP"),
                        findings=[_finding(aid="aid-keep", cve="CVE-2026-0001")]))
    _write_ndjson(tmp_path / "findings.json", rows)
    parser = _build_parser(tmp_path, spark_session, logger)
    out = parser.run()
    survivors = sorted(r["value"] for r in out[0].select("value").collect())
    print(f"\n### DETERMINISM run {run_no}: survivors = {survivors}")


# ------------- probe 7: mixed aid falsy forms in one batch
def test_probe_mixed_null_and_empty_aid(tmp_path, spark_session, logger):
    rows = [
        _record(host=_managed_host(aid="aid-1", hostname="HOST-1"),
                findings=[_finding(aid="aid-1", cve="CVE-2026-0001")]),
        _record(aid=None, host=_unenriched_host(discover_id="d1", hostname="NULLAID-1",
                                                ip="10.1.1.1", aid_value=None), findings=[]),
        _record(aid=None, host=_unenriched_host(discover_id="d2", hostname="NULLAID-2",
                                                ip="10.1.1.2", aid_value=None), findings=[]),
        _record(aid="", host=_unenriched_host(discover_id="d3", hostname="EMPTYAID-1",
                                              ip="10.1.1.3", aid_value=""), findings=[]),
        _record(aid="", host=_unenriched_host(discover_id="d4", hostname="EMPTYAID-2",
                                              ip="10.1.1.4", aid_value=""), findings=[]),
    ]
    _write_ndjson(tmp_path / "findings.json", rows)
    parser = _build_parser(tmp_path, spark_session, logger)
    out = parser.run()
    print("\n### MIXED null + empty aid: input 1 managed + 2 null-aid + 2 empty-aid")
    print("    assets emitted:", sorted(r["value"] for r in out[0].select("value").collect()))
    print("    findings emitted:", out[1].count())


# ------------- probe 8: does lane ORDER decide which single AID-less asset survives?
@pytest.mark.parametrize("order", ["asc", "desc"])
def test_probe_collapse_survivor_depends_on_lane_order(order, tmp_path, spark_session, logger):
    rows = [
        _record(aid=None,
                host=_unenriched_host(discover_id=f"disc-{i}", hostname=f"UNMANAGED-{i}",
                                      ip=f"10.10.1.{i}", aid_value=None),
                findings=[])
        for i in range(20)
    ]
    if order == "desc":
        rows = list(reversed(rows))
    _write_ndjson(tmp_path / "findings.json", rows)
    parser = _build_parser(tmp_path, spark_session, logger)
    out = parser.run()
    survivors = sorted(r["value"] for r in out[0].select("value").collect())
    print(f"\n### LANE ORDER {order}: 20 AID-less hosts in -> survivors = {survivors}")


# ---- probe 9: does a UNIQUE derived per-asset aid (Discover combined-id suffix) survive?
def test_probe_unique_derived_aid_per_unenriched_asset(tmp_path, spark_session, logger):
    """Candidate remedy: the collector emits AidExtractor.ExtractAid's derived value
    (the <cid>_<hash> suffix) as the record-level aid for unenriched assets, so every
    such record carries a UNIQUE key instead of null/empty."""
    rows = [
        _record(host=_managed_host(aid="aid-1", hostname="HOST-1"),
                findings=[_finding(aid="aid-1", cve="CVE-2026-0001")]),
    ]
    rows += [
        _record(aid=f"derived-{i:032x}",
                host=_unenriched_host(discover_id=f"cid-test_derived-{i:032x}",
                                      hostname=f"UNMANAGED-{i}",
                                      ip=f"10.10.1.{i}", aid_value=None),
                findings=[])
        for i in range(20)
    ]
    _write_ndjson(tmp_path / "findings.json", rows)
    parser = _build_parser(tmp_path, spark_session, logger)
    out = parser.run()
    assets_df, findings_df = out[0], out[1]
    vals = sorted(r["value"] for r in assets_df.select("value").collect())
    print(f"\n### UNIQUE DERIVED AID: input 1 managed + 20 unenriched")
    print(f"    assets emitted = {assets_df.count()} (expect 21)")
    print(f"    findings       = {findings_df.count()} (expect 1)")
    print(f"    values         = {vals}")
    pol = out[2] if len(out) == 3 else None
    if pol is not None:
        print(f"    policies       = {pol.count()}")
        for r in pol.collect():
            print("      edges:", r["policy_edges"])
