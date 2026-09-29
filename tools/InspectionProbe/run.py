"""Non-billable installed executable probe. Every ledger is disposable; no provider launch.
Usage: python3 tools/InspectionProbe/run.py /absolute/path/to/ailedger
"""
import hashlib
import itertools
import json
import pathlib
import subprocess
import sys
import tempfile

exe = sys.argv[1]
root = pathlib.Path(tempfile.mkdtemp(prefix="ailedger-inspection-package-"))
ledger = root / "tasks"
lessons = root / "lessons"
lessons.mkdir()
task = "inspection-package-check"
common = ["--root", str(ledger), "--task", task, "--actor", "operator"]

def cli(*args):
    result = subprocess.run([exe, *args], capture_output=True, text=True, timeout=30)
    if result.returncode:
        raise RuntimeError(f"{args[:2]}: {result.stderr}")
    return result.stdout

def command(*args):
    return cli(*args, *common)

identity = cli("version").strip()
command("task", "open", "--lesson-root", str(lessons), "--title", "Package check", "--goal", "Non-billable inspection verification")
command("actor", "attach", "--target", "planner", "--role", "planning-lead")
command("run", "start", "--run", "R", "--subject", "planner", "--provider", "codex")
config = dict(task_workspace_root=str(ledger), task_id=task, actor_id="planner", run_id="R", correlation_id="R",
              diagnostics_directory=str(ledger / task / "telemetry"), allow_record_findings=True,
              allow_record_alternatives=True, allow_submit_artifact=True, allow_record_claim_dispositions=True,
              allow_inspect=True)
config_path = root / "host.json"
events = ledger / task / "events.jsonl"

def send(process, value):
    process.stdin.write(json.dumps(value) + "\n")
    process.stdin.flush()

def start():
    config_path.write_text(json.dumps(config))
    process = subprocess.Popen([exe, "findings", "serve", str(config_path)], stdin=subprocess.PIPE,
                               stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    send(process, dict(jsonrpc="2.0", id=1, method="initialize", params=dict(protocolVersion="2025-11-25", capabilities={}, clientInfo=dict(name="inspection-probe", version="1"))))
    assert json.loads(process.stdout.readline())["result"]["protocolVersion"] == "2025-11-25"
    send(process, dict(jsonrpc="2.0", method="notifications/initialized"))
    return process

rpc_ids = itertools.count(3)

def call(process, name, body):
    send(process, dict(jsonrpc="2.0", id=next(rpc_ids), method="tools/call", params=dict(name=name, arguments=body)))
    response = json.loads(process.stdout.readline())
    assert "result" in response, response
    result = response["result"]
    assert json.loads(result["content"][0]["text"]) == result["structuredContent"]
    return result["structuredContent"]

def finish(process):
    process.stdin.close()
    process.wait(timeout=30)
    assert process.returncode == 0 and not process.stdout.read() and not process.stderr.read()

def inspect(process):
    result = call(process, "inspect_task", dict(schema_version=1, limit=32))
    assert result["status"] == "ok", result
    assert result["snapshot"]["actor_id"] == "planner" and result["snapshot"]["run_id"] == "R"
    return result

process = start()
send(process, dict(jsonrpc="2.0", id=2, method="tools/list"))
tools = json.loads(process.stdout.readline())["result"]["tools"]
assert {tool["name"] for tool in tools} == {"record_findings", "record_alternatives", "submit_artifact", "record_claim_dispositions", "inspect_task", "retrieve_context", "check_readiness"}
assert all(tool["annotations"]["readOnlyHint"] for tool in tools if tool["name"] in {"inspect_task", "retrieve_context", "check_readiness"})
page = inspect(process)
body = dict(schema_version=1, request_id="findings", findings=[dict(key="c", statement='Observed $HOME `literal` "quoted" שלום 😀', consequence_if_wrong="Must revisit")],
            evidence=[dict(key="e", source_type="test-run", citation="fixture://source", summary="Supports", supports=[dict(finding="c")], refutes=[])])
ready = dict(schema_version=1, action="record_findings", expected_version=page["snapshot"]["ledger_version"], proposal=body)
before = events.read_bytes()
assert call(process, "check_readiness", ready)["status"] == "ready"
assert before == events.read_bytes()
receipt = call(process, "record_findings", body)["receipt"]
page = inspect(process)
assert call(process, "check_readiness", ready)["diagnostic"]["code"] == "stale_snapshot"
claim_ref = next(r for r in page["records"] if r["kind"] == "Claim")
retrieved = call(process, "retrieve_context", claim_ref["retrieve"])
assert retrieved["data"]["consequenceIfWrong"] == "Must revisit"
query = dict(claim_ref["retrieve"], length=31)
chunks = []
while True:
    chunk = call(process, "retrieve_context", query)
    assert chunk["status"] == "ok", chunk
    chunks.append(chunk["json_chunk"])
    if chunk["next_offset"] is None:
        break
    query["offset"] = chunk["next_offset"]
raw = "".join(chunks)
assert hashlib.sha256(raw.encode()).hexdigest() == claim_ref["sha256"]
assert json.loads(raw) == retrieved["data"]
receipt_ref = next(r for r in page["records"] if r["kind"] == "FindingsReceipt")
assert call(process, "retrieve_context", receipt_ref["retrieve"])["data"]["transactionId"] == receipt["transaction_id"]
# Reads leave deliberately damaged derived views alone.
projection = ledger / task / "task.md"
projection.write_text("DAMAGED PROJECTION")
state_path = ledger / task / "state.json"
state_before = state_path.read_bytes()
before = events.read_bytes()
inspect(process)
assert projection.read_text() == "DAMAGED PROJECTION" and state_path.read_bytes() == state_before and events.read_bytes() == before
assert call(process, "check_readiness", dict(schema_version=1, action="dispatch_run", expected_version=page["snapshot"]["ledger_version"]))["status"] == "unsupported"
judgment = dict(schema_version=1, request_id="judgment", dispositions=[dict(key="j", claim=dict(claim_id=receipt["findings"][0]["claim_id"]), expected_status="open", status="validated", rationale="Observed evidence supports this", evidence=[dict(evidence_id=receipt["evidence"][0]["evidence_id"])])])
assert call(process, "check_readiness", dict(schema_version=1, action="record_claim_dispositions", expected_version=page["snapshot"]["ledger_version"], proposal=judgment))["status"] == "ready"
command("claim", "resolve", "--id", receipt["findings"][0]["claim_id"], "--status", "validated", "--evidence", receipt["evidence"][0]["evidence_id"])
assert call(process, "record_claim_dispositions", judgment)["error"]["code"] == "state_conflict"
assert call(process, "inspect_task", dict(schema_version=1, actor_id="operator"))["diagnostic"]["code"] == "invalid_request"
config["allow_inspect"] = False
config_path.write_text(json.dumps(config))
assert call(process, "inspect_task", dict(schema_version=1))["diagnostic"]["code"] == "authorization_denied"
finish(process)
report = json.loads(cli("retrospective", "build", "--root", str(ledger), "--task", task, "--findings", "--dispositions"))
assert report["findings"]["observedToolAttempts"] == 1
assert report["findings"]["committedTransactions"] == 1
assert report["claimDispositions"]["observedToolAttempts"] == 1
assert report["claimDispositions"]["committedTransactions"] == 0
rows = [json.loads(line) for path in (ledger / task / "telemetry").glob("inspection-transport-*.jsonl") for line in path.read_text().splitlines()]
assert len(rows) > 10 and all(row["population"] == "inspection" and row["run_id"] == "R" for row in rows)
assert all(not row["durable_request"] and row["transaction_id"] is None and row["provider_usage"] is None for row in rows)
print(json.dumps(dict(identity=identity, fixture=str(root), result="passed", client_trial="pending", billable_episode=False,
    checks=["seven narrow tools", "trusted actor/run", "typed retrieval", "exact chunk digest", "own receipt",
            "no projection repair or canonical writes", "ready/stale/unsupported separation", "execution-time revalidation",
            "spoof refusal", "grant revocation", "inspection and mutation measurement separation", "unknown provider usage"]), indent=2))
