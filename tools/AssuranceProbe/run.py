#!/usr/bin/env python3
"""Task-13 mechanical tool/candidate/recovery probe. No models, no governed histories."""
import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import sys
import time
from datetime import datetime, timezone, timedelta


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def write(path, data):
    Path(path).write_text(json.dumps(data, indent=2) + "\n")


class Client:
    def __init__(self, cli, policy, store, principal, transcript):
        self.commands = cli + ["assurance", "serve", "--authority", str(policy), "--store", str(store),
                               "--principal", principal, "--session", principal + "-session"]
        self.process = subprocess.Popen(self.commands, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                        stderr=subprocess.PIPE, text=True, start_new_session=True)
        self.next_id = 1
        self.transcript = transcript
        self.rpc("initialize", {"protocolVersion": "2025-11-25", "capabilities": {},
                                 "clientInfo": {"name": "task13-script", "version": "1"}})
        self.send({"jsonrpc": "2.0", "method": "notifications/initialized"})

    def send(self, body):
        self.process.stdin.write(json.dumps(body) + "\n")
        self.process.stdin.flush()

    def rpc(self, method, params):
        rid = self.next_id
        self.next_id += 1
        self.send({"jsonrpc": "2.0", "id": rid, "method": method, "params": params})
        line = self.process.stdout.readline()
        if not line:
            raise RuntimeError("MCP host ended: " + self.process.stderr.read())
        result = json.loads(line)
        assert result["id"] == rid, result
        self.transcript.append({"method": method, "request": params, "response": result})
        return result["result"]

    def call(self, operation, body, expected=None):
        response = self.rpc("tools/call", {"name": operation, "arguments": body})["structuredContent"]
        if expected is None:
            assert response["error"] is None, response
        else:
            assert response["error"]["code"] == expected, response
        return response

    def close(self):
        self.process.stdin.close()
        self.process.wait(timeout=20)
        error = self.process.stderr.read()
        assert self.process.returncode == 0, error


def run(cli, root):
    root.mkdir(parents=True, exist_ok=False)
    candidate = root / "candidate"
    candidate.mkdir()
    for area in ("a", "b", "c"):
        (candidate / (area + ".txt")).write_text("1\n")
        (candidate / (area + ".requirements")).write_text("The output is the integer one followed by a newline.\n")
        (candidate / (area + ".source")).write_text("Source contract: preserve the exact output.\n")
    # Host-approved, argument-vector invocation; the tool never accepts this program text.
    check = root / "check.sh"
    check.write_text("#!/bin/sh\nset -eu\ntest \"$(cat a.txt)\" = 1\nprintf 'observed output: '\ncat a.txt\n")
    os.chmod(check, 0o700)
    slow = root / "slow.sh"
    slow.write_text("#!/bin/sh\nsleep 30\ntest \"$(cat a.txt)\" = 1\n")
    os.chmod(slow, 0o700)
    expires = (datetime.now(timezone.utc) + timedelta(hours=2)).isoformat()
    policy = {"schema_version": 1, "case_id": "task13-scripted-example", "candidate_root": str(candidate),
              "implementer": "external-implementer", "areas": [], "principals": [], "checks": []}
    for area in ("a", "b", "c"):
        policy["areas"].append({"id": area, "candidate_paths": [area + ".txt"],
          "requirement_paths": [area + ".requirements"], "source_paths": [area + ".source"],
          "depends_on_areas": ["a"] if area == "b" else [],
          "criteria": [{"id": "output", "description": "Exact one-line output", "check_id": "unit"}]})
    for principal, role in (("reviewer", "review"), ("verifier", "verification"), ("operator", "acceptance")):
        policy["principals"].append({"id": principal, "role": role, "enabled": True,
                                      "expires_at": expires, "areas": ["a", "b", "c"]})
    policy["checks"] = [{"id": "unit", "executable": str(check), "executable_sha256": digest(check),
                          "arguments": [], "timeout_seconds": 10}]
    authority = root / "authority.json"
    write(authority, policy)
    transcript = []
    clients = {p: Client(cli, authority, root / "store", p, transcript) for p in ("reviewer", "verifier", "operator")}
    def inspect(p="operator", area="a"):
        return clients[p].call("inspect_assurance", {"schema_version": 1, "area_id": area})["data"]
    def report(p, area="a", status="complete", check_status="pass", previous=None, key="report"):
        snapshot = inspect(p, area)["snapshot"]
        common = {"schema_version": 1, "area_id": area, "expected_binding": snapshot["binding_sha256"]}
        paths = [i["path"] for i in snapshot["inputs"]]
        read_body = dict(common, request_id=key + "-read", paths=paths)
        read = clients[p].call("read_assurance", read_body)
        replay = clients[p].call("read_assurance", read_body)
        assert replay["replayed"] and replay["receipt"] == read["receipt"]
        tests = []
        if p == "verifier" and check_status == "pass":
            result = clients[p].call("run_assurance_checks", dict(common, request_id=key + "-check", check_ids=["unit"]))
            tests.append(result["receipt"]["id"])
        body = dict(common, request_id=key, status=status, summary="Scripted inspection of captured content; no model judgment.",
                    inspected_paths=paths, read_receipts=[read["receipt"]["id"]],
                    checks=[{"criterion_id": "output", "status": check_status, "evidence": "Captured exact input and host check where applicable.", "test_receipts": tests}],
                    findings=[], uncertainty=[], supersedes=previous)
        result = clients[p].call("record_assurance", body)
        return result, body
    for p, expected in (("reviewer", 3), ("verifier", 4), ("operator", 2)):
        assert len(clients[p].rpc("tools/list", {})["tools"]) == expected
    partial, body = report("reviewer", status="partial", check_status="unknown", key="review-checkpoint")
    write(root / "partial.json", partial)
    # Disconnect after an acknowledged partial; the later host retains its stable identity.
    clients["reviewer"].close()
    clients["reviewer"] = Client(cli, authority, root / "store", "reviewer", transcript)
    retry = clients["reviewer"].call("record_assurance", body)
    assert retry["replayed"] and retry["receipt"] == partial["receipt"]
    write(root / "recovered-partial.json", retry)
    review, _ = report("reviewer", previous=partial["receipt"]["id"], key="review-final")
    verify, _ = report("verifier", key="verify-final")
    for area in ("b", "c"):
        report("reviewer", area=area, key="review-" + area)
    decision = {"schema_version": 1, "request_id": "accept-a", "area_id": "a",
                "expected_binding": inspect()["snapshot"]["binding_sha256"],
                "reports": [review["receipt"]["id"], verify["receipt"]["id"]],
                "rationale": "Explicit scripted fixture acceptance; not client acceptance or semantic proof.", "dispositions": []}
    accepted = clients["operator"].call("accept_assurance", decision)
    write(root / "accepted.json", accepted)
    assert inspect()["acceptance"] == "accepted"
    (candidate / "a.txt").write_text("2\n")
    changed = inspect()
    assert changed["acceptance"] == "not_accepted"
    assert any("candidate changed: a.txt" in h["reasons"] for h in changed["history"])
    assert any("relied-on area changed: a" in h["reasons"] for h in inspect(area="b")["history"])
    assert all(h["status"] == "current" for h in inspect(area="c")["history"])
    write(root / "changed.json", changed)
    clients["operator"].call("accept_assurance", dict(decision, request_id="stale-accept"), "stale_candidate")
    # The repair deliberately adds an extra newline; the check below does not detect it.
    (candidate / "a.txt").write_text("1\n\n")
    repaired_review, _ = report("reviewer", key="review-repaired", previous=review["receipt"]["id"])
    repaired_verify, _ = report("verifier", key="verify-repaired", previous=verify["receipt"]["id"])
    # Shell command substitution drops trailing newlines; the scripted test alone is insufficient
    # for semantic acceptance. Keep repair unaccepted to expose the client inspection obligation.
    write(root / "repaired-unaccepted.json", inspect())
    for client in clients.values():
        client.close()
    # A separate disposable case demonstrates actual hard host/process-group interruption.
    policy["case_id"] = "task13-interruption"
    policy["checks"][0].update(executable=str(slow), executable_sha256=digest(slow), timeout_seconds=60)
    write(authority, policy)
    clients = {p: Client(cli, authority, root / "store", p, transcript) for p in ("verifier", "operator")}
    interruption_partial, interruption_body = report("verifier", status="partial", check_status="unknown", key="before-interruption")
    binding = inspect()["snapshot"]["binding_sha256"]
    request = {"schema_version": 1, "request_id": "interrupted-check", "area_id": "a", "expected_binding": binding, "check_ids": ["unit"]}
    victim = clients["verifier"]
    victim.send({"jsonrpc": "2.0", "id": 100, "method": "tools/call", "params": {"name": "run_assurance_checks", "arguments": request}})
    deadline = time.monotonic() + 20
    while time.monotonic() < deadline:
        starts = [p for p in (root / "store" / "episodes-v1").glob("*/*.json") if 'assurance_check_started' in p.read_text() and 'interrupted-check' in p.read_text()]
        if starts:
            break
        time.sleep(0.05)
    assert starts, "No durable check start observed"
    os.killpg(victim.process.pid, signal.SIGKILL)
    victim.process.wait(timeout=10)
    clients["verifier"] = Client(cli, authority, root / "store", "verifier", transcript)
    clients["verifier"].call("run_assurance_checks", request, "inspection_interrupted")
    write(root / "interrupted.json", inspect())
    reconcile = subprocess.run(cli + ["assurance", "reconcile", "--authority", str(authority), "--store", str(root / "store"),
                       "--principal", "operator", "--session", "operator-session", "--request-id", "interrupted-check",
                       "--check-principal", "verifier", "--confirm-provider-stopped"], capture_output=True, text=True, check=True)
    write(root / "reconciled.json", json.loads(reconcile.stdout))
    preserved = clients["verifier"].call("record_assurance", interruption_body)
    assert preserved["receipt"] == interruption_partial["receipt"] and preserved["replayed"]
    assert inspect()["acceptance"] == "not_accepted"
    write(root / "after-reconciliation.json", inspect())
    for client in clients.values():
        client.close()
    write(root / "transcript.json", transcript)
    identity = subprocess.check_output(cli + ["version"], text=True).strip()
    result = {"identity": identity, "result": "passed", "real_provider_episodes": 0, "client_acceptance": "pending",
              "checks": ["scoped discovery and actual typed calls", "host identity", "read receipt replay", "independent explicit acceptance",
                         "candidate/source/requirement binding", "affected and unaffected areas", "stale refusal",
                         "partial restart", "hard host/process-group interruption", "unknown reconciliation", "partial receipt preservation"],
              "limitation": "Mechanical fixture only; scripted repair remains unaccepted and illustrates test relevance limits."}
    write(root / "report.json", result)
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit("Usage: run.py /absolute/ailedger-or-CLI.dll /absolute/new-output-directory")
    cli = ["dotnet", sys.argv[1]] if sys.argv[1].endswith(".dll") else [sys.argv[1]]
    run(cli, Path(sys.argv[2]).resolve())
