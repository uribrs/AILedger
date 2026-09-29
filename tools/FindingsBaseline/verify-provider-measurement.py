#!/usr/bin/env python3
"""Read existing task-4 episodes through task-5 reports. Never launch a provider.

CLI GetState repairs disposable projections, so report only against temporary copies. Originals
are hashed before/after. This compares recorded observations, not a new live-provider trial.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("--cli", type=Path, required=True)
parser.add_argument("episodes", type=Path, nargs="+")
args = parser.parse_args()

def hashes(root):
    return {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in root.rglob("*") if p.is_file()}

for episode in args.episodes:
    before = hashes(episode)
    accepted = json.loads((episode / "acceptance.json").read_text())
    assert accepted["accepted"]
    task = accepted["receipt"]["task_id"]
    with tempfile.TemporaryDirectory(prefix="ailedger-task5-observation-") as temp:
        ledger = Path(temp) / "ledger"
        shutil.copytree(episode / "ledger", ledger)
        command = ["dotnet", str(args.cli.resolve()), "retrospective", "build", "--root", str(ledger), "--task", task]
        original = json.loads(subprocess.check_output(command, text=True))
        report = json.loads(subprocess.check_output(command + ["--findings"], text=True))
        findings = report.pop("findings")
        assert report == original, "Existing retrospective fields changed"
        assert findings["committedTransactions"] == 1
        assert findings["granularEvents"] == 2
        assert findings["observedToolAttempts"] == 3
        assert findings["observedApplicationAttempts"] == 3
        assert not findings["coverageGaps"], findings["coverageGaps"]
        receipt, = findings["transactions"]
        assert receipt["transactionId"] == accepted["receipt"]["transaction_id"]
        assert receipt["eventIds"] == accepted["receipt"]["event_ids"]
        run, = findings["runs"]
        completion = run["completion"]
        expected = dict(accepted["run"])
        expected.pop("eventType", None)
        assert completion == expected, (completion, expected)
        raw = json.loads((ledger / task / "runs" / "R1.json").read_text())
        assert raw["providerSessionId"] == completion["providerSessionId"] == accepted["session"]
        assert raw["truncatedLines"] == completion["truncatedLines"]
        assert raw["endedAtTheLaunchTimeout"] == completion["endedAtTheLaunchTimeout"]
        for field in ["turns", "outputTokens", "tokensInUncached", "tokensInCacheWrite", "tokensInCacheRead", "millisecondsToFirstLedgerWrite"]:
            measure = report["cost"][field]
            assert measure.get("total") == completion.get(field)
            assert measure["runsMeasured"] == int(completion.get(field) is not None)
        calls = [x for x in findings["attempts"] if x["kind"] == "transport" and x["observation"]["messageKind"] == "tool_call"]
        assert len(calls) == 3
        for row in calls:
            assert row["joinedProviderSessionId"] == accepted["session"]
            assert row["observation"].get("providerSessionId") is None
            assert row["joinedApplicationAttemptId"]
        successes = [x for x in calls if x["observation"]["outcome"] == "committed"]
        assert len(successes) == 2
        assert {x["observation"]["replayed"] for x in successes} == {False, True}
        assert {x["canonicalTransactionId"] for x in successes} == {receipt["transactionId"]}
        denied, = [x for x in calls if x["observation"].get("code") == "kernel_refused"]
        assert denied["observation"]["commitState"] == "not_committed"
        assert "canonicalTransactionId" not in denied
        assert len((ledger / task / "refusals.jsonl").read_text().splitlines()) == 1
        assert hashlib.sha256((ledger / task / "events.jsonl").read_bytes()).hexdigest() == before[f"ledger/{task}/events.jsonl"]
        print(f"{accepted['provider']}: 3 tool/application attempts, 1 transaction, 2 events, 1 unchanged completion/cost; session joined, transport session absent")
    assert before == hashes(episode), "Original episode changed"
