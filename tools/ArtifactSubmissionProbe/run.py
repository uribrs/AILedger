"""Non-billable package check. All records belong to a new disposable fixture.

Usage: python3 run.py /absolute/path/to/ailedger /absolute/path/to/cognitive
No provider launch or model request is performed.
"""
import hashlib
import itertools
import json
import pathlib
import subprocess
import sys
import tempfile

exe, cognitive = sys.argv[1:3]
root = pathlib.Path(tempfile.mkdtemp(prefix="ailedger-artifact-package-"))
ledger = root / "tasks"
lessons = root / "lessons"
lessons.mkdir()
task = "artifact-package-check"
common = ["--root", str(ledger), "--task", task, "--actor", "operator"]

def cli(*args, body=None):
    result = subprocess.run([exe, *args], input=body, capture_output=True, text=True, timeout=30)
    if result.returncode:
        raise RuntimeError(f"{args[:2]}: {result.stderr}")
    return result.stdout

def command(*args, body=None):
    return cli(*args, *common, body=body)

def stage(value):
    command("stage", "transition", "--stage", value, "--without-prerequisites", "Disposable package fixture stage arrangement")

identity = cli("version").strip()
command("task", "open", "--lesson-root", str(lessons), "--title", "Package check", "--goal", "Non-billable artifact package verification")
command("context", "build", "--cognitive-root", cognitive, "--output", str(root / "manifest.json"))
command("actor", "attach", "--target", "verifier", "--role", "verifier")
command("actor", "attach", "--target", "reviewer", "--role", "code-reviewer")
stage("research")
stage("design")
command("run", "start", "--run", "RP", "--provider", "codex")
command("artifact", "record", "--id", "P", "--kind", "prompt-contract", "--title", "Fixture contract", "--run", "RP", "--body-stdin", body="Disposable fixture contract")
command("run", "complete", "--run", "RP", "--status", "completed", "--session", "fixture-planner")
stage("scope")
command("run", "start", "--run", "RO", "--provider", "codex")
command("artifact", "record", "--id", "O", "--kind", "orchestration-plan", "--title", "Fixture plan", "--run", "RO", "--body-stdin", body="No material attention items")
command("run", "complete", "--run", "RO", "--status", "completed", "--session", "fixture-plan")
stage("ready")
command("work", "add", "--id", "W", "--title", "Fixture work", "--owner", "operator")
stage("execution")
stage("verification")
command("run", "start", "--run", "V", "--subject", "verifier", "--work", "W", "--provider", "codex")

config = dict(task_workspace_root=str(ledger), task_id=task, actor_id="verifier", run_id="V", correlation_id="V",
              diagnostics_directory=str(ledger / task / "telemetry"), allow_record_findings=True,
              allow_record_alternatives=True, allow_submit_artifact=True)
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

content = "# Verification\n\n| id | status | name | citation | actor |\n| --- | --- | --- | --- | --- |\n\n| id | final disposition | name | evidence |\n| --- | --- | --- | --- |\n\n  Literal $HOME | `quotes` שלום 😀\r\n"
request = dict(schema_version=1, request_id="output-1", kind="verifier-output", title="Verification", content=content)
process = start()
send(process, dict(jsonrpc="2.0", id=2, method="tools/list"))
assert [tool["name"] for tool in json.loads(process.stdout.readline())["result"]["tools"]] == ["record_findings", "record_alternatives", "submit_artifact"]
before = events.read_bytes()
rejected = call(process, "submit_artifact", dict(request, expected_content_sha256="0" * 64))
assert rejected["error"]["code"] == "content_identity_mismatch" and before == events.read_bytes()
first = call(process, "submit_artifact", request)
assert first["status"] == "committed" and not first["replayed"]
receipt = first["receipt"]
assert receipt["artifact"]["content_sha256"] == hashlib.sha256(content.encode()).hexdigest()
assert receipt["artifact"]["covered_work_item_ids"] == ["W"] and receipt["actor_id"] == "verifier" and receipt["run_id"] == "V"
event = json.loads(events.read_text().splitlines()[-1])
assert event["data"]["artifact"]["content"] == content
finish(process)
command("run", "complete", "--run", "V", "--status", "completed", "--session", "fixture-verifier")
process = start()
before = events.read_bytes()
retry = call(process, "submit_artifact", request)
assert retry["replayed"] and retry["receipt"] == receipt and events.read_bytes() == before
conflict = call(process, "submit_artifact", dict(request, content=content + "changed"))
assert conflict["error"]["code"] == "idempotency_conflict" and "receipt" not in conflict
finish(process)
stage("review")
command("run", "start", "--run", "R", "--subject", "reviewer", "--work", "W", "--provider", "claude")
config.update(actor_id="reviewer", run_id="R", correlation_id="R")
process = start()
review = call(process, "submit_artifact", dict(request, kind="code-review-output", content="Independent review output", title="Review"))
assert review["status"] == "committed" and review["receipt"]["run_id"] == "R"
finish(process)
report = json.loads(cli("retrospective", "build", "--root", str(ledger), "--task", task, "--artifacts", "--findings", "--alternatives"))
assert report["artifactSubmissions"]["committedTransactions"] == 2
assert report["findings"]["committedTransactions"] == 0 and report["alternatives"]["committedTransactions"] == 0
command("actor", "attach", "--target", "reviewer", "--role", "code-reviewer", "--capability", "build-context")
process = start()
denied = call(process, "submit_artifact", dict(request, kind="code-review-output", content="Independent review output", title="Review"))
assert denied["error"]["code"] == "kernel_refused" and "receipt" not in denied
finish(process)
summary = dict(identity=identity, fixture=str(root), result="passed", client_trial="not performed", billable_episode=False,
               checks=["three exact tools", "content mismatch no mutation", "inline content fidelity and SHA-256",
                       "trusted author/run/scope", "restart receipt after run completion", "key conflict",
                       "both artifact kinds", "separate measurement populations", "capability revocation"])
(root / "verification.json").write_text(json.dumps(summary, indent=2) + "\n")
print(json.dumps(summary, indent=2))
