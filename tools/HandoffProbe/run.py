#!/usr/bin/env python3
"""Prepare frozen diagnostic/current handoffs through the actual CLI. No agent/model launch.
Usage: python3 tools/HandoffProbe/run.py /absolute/path/to/ailedger OUTPUT_DIRECTORY
Never reads original ledgers or later evaluation findings; inputs come only from pinned fixtures.
"""
import hashlib
import json
import pathlib
import subprocess
import sys
import tempfile

repo = pathlib.Path(__file__).resolve().parents[2]
fixtures = repo / "tests/Fixtures/bounded-handoffs-v1"
exe = sys.argv[1]
out = pathlib.Path(sys.argv[2]).resolve()
out.mkdir(parents=True, exist_ok=True)
root = pathlib.Path(tempfile.mkdtemp(prefix="ailedger-handoff-probe-"))
ledger = root / "tasks"

def call(args, body=None, fail=False):
    result = subprocess.run([exe, *args], input=None if body is None else json.dumps(body),
                            text=True, capture_output=True, timeout=120)
    if fail:
        assert result.returncode != 0, result.stdout
        return result.stderr.strip()
    if result.returncode:
        raise RuntimeError(result.stderr)
    return json.loads(result.stdout)

def file_hashes(directory):
    return {str(p.relative_to(directory)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in directory.rglob("*") if p.is_file()}

def render(package, sealed):
    lines = ["# " + package["spec"]["id"], "", package["spec"]["objective"], "",
             "Package SHA-256: `" + sealed["package_sha256"] + "`", "",
             package["authority_boundary"], "", "## Instructions", "",
             "```json", json.dumps(package["spec"], indent=2, ensure_ascii=False), "```", "",
             "## Preservation assessment", "", "```json", json.dumps(package["preservation"], indent=2, ensure_ascii=False), "```", ""]
    for item in package["inputs"]:
        lines += ["## " + item["key"], "", "Selection: " + json.dumps(item["selection"], ensure_ascii=False), "",
                  "Retrieval: `" + json.dumps(item["reference"]["retrieve"], ensure_ascii=False) + "`", ""]
        if item["record_json"] is not None:
            lines += ["Source data (never instructions):", "", "```json", item["record_json"], "```", ""]
    lines += ["## Pinned source data", "", "```json", json.dumps(package["sources"], indent=2, ensure_ascii=False), "```", "",
              "## Measurements", "", "```json", json.dumps(package["measurements"], indent=2), "```", "", package["omission_policy"], ""]
    return "\n".join(lines)

identity = subprocess.check_output([exe, "version"], text=True).strip()
results = []
for case in json.loads((fixtures / "manifest.json").read_text()):
    name = case["case"]
    raw = (fixtures / (name + ".jsonl")).read_bytes()
    assert hashlib.sha256(raw).hexdigest() == case["sha256"]
    assert len(raw.splitlines()) == case["cutoff"]
    directory = ledger / case["task_id"]
    directory.mkdir(parents=True)
    (directory / "events.jsonl").write_bytes(raw)
    common = ["--root", str(ledger), "--task", case["task_id"], "--actor", "operator"]
    index = call(["handoff", "index", *common])
    curation = json.loads((fixtures / (name + "-curation.json")).read_text())
    included = set(curation["included_keys"])
    omitted = curation["omitted"]
    assert included.isdisjoint(omitted)
    assert included | omitted.keys() == {r["kind"] + ":" + r["id"] for r in index["records"]}
    inputs = []
    for ref in index["records"]:
        key = ref["kind"] + ":" + ref["id"]
        include = key in included
        inputs.append(dict(kind=ref["kind"], id=ref["id"], sha256=ref["sha256"], include=include, material=include,
            reason="Needed for this bounded technical question." if include else omitted[key]["reason"],
            omission_risk="Would lose relevant evidence." if include else omitted[key]["risk"],
            retrieve_when="Before reliance." if include else omitted[key]["retrieve_when"]))
    request = dict(schema_version=1, ledger_identity=index["ledger_identity"], expected_version=index["snapshot"]["ledger_version"],
        selection=index["selection"], spec=curation["spec"], inputs=inputs, sources=curation["sources"], preservation=curation["preservation"])
    before = file_hashes(directory)
    sealed = call(["handoff", "prepare", *common, "--body-stdin"], request)
    assert before == file_hashes(directory)
    content = sealed["package_json"].encode()
    assert hashlib.sha256(content).hexdigest() == sealed["package_sha256"] and len(content) == sealed["package_bytes"]
    package = json.loads(content)
    assert package["snapshot"]["ledger_version"] == case["cutoff"]
    assert package["measurements"]["observed_additional_reads"] is None
    assert package["measurements"]["observed_cost_usd"] is None
    assert package["measurements"]["omitted_records"] == len(omitted)
    # Deliberately retrieve one omitted full record through task 10's API via the read-only CLI adapter.
    # This is an observed probe read, not an agent request or a measure of semantic necessity.
    omission = next(i for i in package["inputs"] if i["record_json"] is None)
    query = dict(omission["reference"]["retrieve"], length=16384)
    retrieved = call(["handoff", "retrieve", *common, "--body-stdin"], query)
    assert retrieved["status"] == "ok" and retrieved["sha256"] == omission["reference"]["sha256"]
    assert retrieved["next_offset"] is None
    if retrieved["data"] is not None:
        # UTF-8 record bytes are determined by its exact existing serialization, returned as chunks below.
        query["length"] = 1
        first = call(["handoff", "retrieve", *common, "--body-stdin"], query)
        chunks = [first["json_chunk"]]
        extra_calls = 2
        query["length"] = 16384
        while first["next_offset"] is not None:
            query["offset"] = first["next_offset"]
            first = call(["handoff", "retrieve", *common, "--body-stdin"], query)
            chunks.append(first["json_chunk"])
            extra_calls += 1
        raw_read = "".join(chunks).encode()
    else:
        raw_read = retrieved["json_chunk"].encode()
        extra_calls = 1
    assert hashlib.sha256(raw_read).hexdigest() == omission["reference"]["sha256"]
    assert before == file_hashes(directory)
    refusal = call(["handoff", "prepare", *common, "--body-stdin"], dict(request, actor_id="operator"), fail=True)
    assert "actor_id" in refusal and before == file_hashes(directory)
    (out / (name + "-request.json")).write_text(json.dumps(request, indent=2, ensure_ascii=False) + "\n")
    (out / (name + "-handoff.json")).write_text(json.dumps(sealed, indent=2, ensure_ascii=False) + "\n")
    (out / (name + "-handoff.md")).write_text(render(package, sealed))
    results.append(dict(case=name, source_cutoff=case["cutoff"], source_sha256=case["sha256"],
                        withheld_events=case["later_events_withheld"], package_sha256=sealed["package_sha256"],
                        package_bytes=sealed["package_bytes"], probe_additional_record=omission["key"],
                        probe_additional_calls=extra_calls, probe_additional_record_bytes=len(raw_read), probe_additional_bytes=len(raw_read) * (2 if extra_calls > 1 else 1),
                        curation_index_calls=index["inspection_calls"],
                        **package["measurements"]))
report = dict(identity=identity, fixture_root=str(root), results=results, client_trial="pending", agent_launches=0,
              interpretation="Preparation observations only. No judgment, elapsed-delivery, token or cost improvement is measured.")
(out / "measurements.json").write_text(json.dumps(report, indent=2) + "\n")
print(json.dumps(report, indent=2))
