"""Non-billable package probe: disposable fixture only, no provider/model launch.

Usage: python3 run.py /absolute/path/to/ailedger
"""
import hashlib
import itertools
import json
import pathlib
import subprocess
import sys
import tempfile

exe = sys.argv[1]
root = pathlib.Path(tempfile.mkdtemp(prefix="ailedger-dispositions-package-"))
ledger = root / "tasks"
lessons = root / "lessons"
lessons.mkdir()
task = "dispositions-package-check"
common = ["--root", str(ledger), "--task", task, "--actor", "operator"]

def cli(*args):
    result = subprocess.run([exe, *args], capture_output=True, text=True, timeout=30)
    if result.returncode:
        raise RuntimeError(f"{args[:2]}: {result.stderr}")
    return result.stdout

def command(*args):
    return cli(*args, *common)

identity = cli("version").strip()
command("task", "open", "--lesson-root", str(lessons), "--title", "Package check", "--goal", "Non-billable dispositions verification")
command("actor", "attach", "--target", "planner", "--role", "planning-lead")
command("run", "start", "--run", "R", "--subject", "planner", "--provider", "codex")
config = dict(task_workspace_root=str(ledger), task_id=task, actor_id="planner", run_id="R", correlation_id="R",
              diagnostics_directory=str(ledger / task / "telemetry"), allow_record_findings=True,
              allow_record_alternatives=True, allow_submit_artifact=True, allow_record_claim_dispositions=True)
config_path = root / "host.json"
events = ledger / task / "events.jsonl"

def send(process, value):
    process.stdin.write(json.dumps(value) + "\n")
    process.stdin.flush()

def start():
    config_path.write_text(json.dumps(config))
    process = subprocess.Popen([exe, "findings", "serve", str(config_path)], stdin=subprocess.PIPE,
                               stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    send(process, dict(jsonrpc="2.0", id=1, method="initialize", params=dict(protocolVersion="2025-11-25", capabilities={}, clientInfo=dict(name="package-check", version="1"))))
    assert json.loads(process.stdout.readline())["result"]["protocolVersion"] == "2025-11-25"
    send(process, dict(jsonrpc="2.0", method="notifications/initialized"))
    return process

rpc_ids = itertools.count(3)

def call(process, name, body):
    number = next(rpc_ids)
    send(process, dict(jsonrpc="2.0", id=number, method="tools/call", params=dict(name=name, arguments=body)))
    result = json.loads(process.stdout.readline())["result"]
    assert json.loads(result["content"][0]["text"]) == result["structuredContent"]
    return result["structuredContent"]

def finish(process):
    process.stdin.close()
    process.wait(timeout=30)
    assert process.returncode == 0 and not process.stdout.read() and not process.stderr.read()


process = start()
send(process, dict(jsonrpc="2.0", id=2, method="tools/list"))
assert {tool["name"] for tool in json.loads(process.stdout.readline())["result"]["tools"]} == {
    "record_findings", "record_alternatives", "submit_artifact", "record_claim_dispositions"}
findings = dict(schema_version=1, request_id="observations", findings=[dict(key="c1", statement="Observed claim"), dict(key="c2", statement="Refuted claim")],
                evidence=[dict(key="e1", source_type="test-run", citation="disposable probe", summary="Support", supports=[dict(finding="c1")], refutes=[]),
                          dict(key="e2", source_type="test-run", citation="disposable probe", summary="Refutation", supports=[], refutes=[dict(finding="c2")])])
recorded = call(process, "record_findings", findings)["receipt"]
claims = [m["claim_id"] for m in recorded["findings"]]
evidence = [m["evidence_id"] for m in recorded["evidence"]]
assert all(json.loads(line)["data"]["claim"]["status"] == "open" for line in events.read_text().splitlines() if json.loads(line)["data"]["eventType"] == "claim.added")
command("decision", "propose", "--id", "D", "--statement", "Dependent decision", "--rationale", "Fixture", "--depends-on", claims[1])
rationale = '  Explicit judgment $HOME | `quoted` "x"\nשלום 😀  '
request = dict(schema_version=1, request_id="judgments", dispositions=[
    dict(key="j1", claim=dict(claim_id=claims[0]), expected_status="open", status="validated", rationale=rationale, evidence=[dict(evidence_id=evidence[0])]),
    dict(key="j2", claim=dict(claim_id=claims[1]), expected_status="open", status="rejected", rationale="Refuting evidence establishes the rejection", evidence=[dict(evidence_id=evidence[1])])])
before = events.read_bytes()
bad = dict(request, dispositions=[request["dispositions"][0], dict(request["dispositions"][1], evidence=[dict(evidence_id=evidence[0])])])
refused = call(process, "record_claim_dispositions", bad)
assert refused["error"]["code"] == "kernel_refused" and refused["error"]["item_path"] == "dispositions[1]"
assert refused["error"]["commit_state"] == "not_committed" and before == events.read_bytes()
first = call(process, "record_claim_dispositions", request)
assert first["status"] == "committed" and not first["replayed"]
receipt = first["receipt"]
assert receipt["actor_id"] == "planner" and receipt["run_id"] == "R" and receipt["correlation_id"] == "R"
assert receipt["dispositions"][0]["rationale"] == rationale
assert receipt["dispositions"][1]["dependencies"][0]["id"] == "D"
assert receipt["dispositions"][1]["dependencies"][0]["status"] == "invalidated"
assert len(receipt["event_ids"]) == 3
finish(process)
command("run", "complete", "--run", "R", "--status", "completed", "--session", "fixture-session")
process = start()
before = events.read_bytes()
retry = call(process, "record_claim_dispositions", request)
assert retry["replayed"] and retry["receipt"] == receipt and retry["attempt_id"] != first["attempt_id"]
assert events.read_bytes() == before
conflict = call(process, "record_claim_dispositions", dict(request, dispositions=[dict(request["dispositions"][0], rationale="Changed"), request["dispositions"][1]]))
assert conflict["error"]["code"] == "idempotency_conflict" and "receipt" not in conflict
finish(process)
report = json.loads(cli("retrospective", "build", "--root", str(ledger), "--task", task, "--dispositions", "--findings", "--alternatives", "--artifacts"))
assert report["claimDispositions"]["committedTransactions"] == 1 and report["claimDispositions"]["granularEvents"] == 3
assert report["claimDispositions"]["observedToolAttempts"] == 4
assert report["findings"]["committedTransactions"] == 1 and report["alternatives"]["committedTransactions"] == 0
assert report["artifactSubmissions"]["committedTransactions"] == 0
assert len(report["claimDispositions"]["runs"]) == 1
assert report["cost"]["outputTokens"]["runsMeasured"] == 0
command("actor", "attach", "--target", "planner", "--role", "planning-lead", "--capability", "add-claim")
process = start()
denied = call(process, "record_claim_dispositions", request)
assert denied["error"]["code"] == "kernel_refused" and "receipt" not in denied
spoof = call(process, "record_claim_dispositions", dict(request, actor_id="operator"))
assert spoof["error"]["code"] == "invalid_request"
finish(process)
print(json.dumps(dict(identity=identity, fixture=str(root), result="passed", client_trial="not performed", billable_episode=False,
    checks=["four exact tools", "evidence leaves claims open", "atomic late direction refusal", "explicit validation and rejection",
            "durable verbatim rationale", "trusted actor/run", "dependency invalidation receipt", "process restart/completed run retry",
            "key conflict", "separate attempt/request/event populations", "once-per-run join with unmeasured usage", "authority revocation", "spoof refusal"]), indent=2))
