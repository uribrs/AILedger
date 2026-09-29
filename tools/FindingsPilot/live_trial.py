"""Prepare a bounded source-inspection trial; --execute explicitly consumes provider usage."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess

SOURCES = ["tests/Support/RepositoryLayout.cs", "tests/Support/RepositoryLayout.props",
           "tests/AILedger.Tests/Support/TestEnvironment.cs", "tests/AILedger.Tests/Support/ContextBrief.cs",
           "src/AILedger.Cli/CognitiveArtifactLoader.cs", "src/AILedger.Providers/Adapters/CodexAgentAdapter.cs"]
GOAL = """This is an explicitly authorized, bounded task-6 live trial in a disposable ledger.
Inspect only the supplied read-only source snapshots under sources/. Determine how the tests locate
their source checkout when build artifacts are external, and identify one material remaining limitation.
Do not modify source or implement changes. Use the supplied record_findings MCP tool for all findings.
Do not dispatch agents, invoke CLI writes, install anything, change grants, or bypass expected refusals.

First inspect RepositoryLayout.cs and RepositoryLayout.props. BEFORE inspecting the remaining files,
record one source-backed observation as request_id task6-early with one finding key first and one
evidence key first-evidence supporting {"finding":"first"}. Preserve concrete file/line citations.
Then inspect the remaining four files and prepare one distinct source-backed observation about the
cognitive-root setting or another material limitation. Use request_id task6-later, finding key later,
and evidence key later-evidence. For the FIRST attempt of this second batch ONLY, deliberately use
supports:[{"claim_id":"task6-deliberately-missing"}]. Expect kernel_refused and no canonical prefix.
This tests the guardrail; do not bypass it. Correct only that reference to {"finding":"later"} and
resubmit task6-later. After the corrected call succeeds, retry that EXACT corrected body and key once;
expect the same transaction/IDs and replayed:true. This is an intentional replay, not a simulated lost response.

Stop after these four tool attempts. In your final answer report both observations, citations,
transaction IDs, expected refusal, replay result, and remaining uncertainty. Use small early batches;
keep observations distinct from inference. No further investigation or repairs are authorized.
"""


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def prepare(args, repo, output):
    if output.is_relative_to(repo) or output.exists():
        raise ValueError("Use a new output directory outside the checkout")
    output.mkdir(parents=True)
    work = output / "work"
    (work / "sources").mkdir(parents=True)
    subprocess.run(["git", "init", "-q", str(work)], check=True)
    hashes = {}
    for name in SOURCES:
        destination = work / "sources" / Path(name).name
        shutil.copyfile(repo / name, destination)
        hashes[name] = digest(destination)
    (output / "goal.txt").write_text(GOAL)
    manifest = {"provider": args.provider, "cli": str(args.cli.resolve()), "cli_sha256": digest(args.cli),
                "source_commit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=repo, text=True).strip(),
                "source_sha256": hashes, "goal_sha256": digest(output / "goal.txt"), "timeout_seconds": 240,
                "status": "prepared-not-executed", "billable_runs_authorized_by_preparation": 0}
    (output / "trial-plan.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print("Prepared; no provider started. Plan: " + str(output / "trial-plan.json"))


def execute(args, repo, output):
    plan = json.loads((output / "trial-plan.json").read_text())
    assert plan["provider"] == args.provider and plan["cli"] == str(args.cli.resolve())
    assert plan["cli_sha256"] == digest(args.cli) and plan["goal_sha256"] == digest(output / "goal.txt")
    for name, expected in plan["source_sha256"].items():
        assert digest(output / "work/sources" / Path(name).name) == expected
    executable = shutil.which(args.provider)
    if not executable:
        raise RuntimeError("Provider executable not found")
    ledger = output / "ledger"
    ledger.mkdir()  # Existing execution is refused, never implicitly repeated.
    cli = ["dotnet", str(args.cli)]
    common = ["--root", str(ledger), "--task", "live", "--actor", "operator"]
    def call(arguments, label):
        result = subprocess.run(cli + arguments + common, text=True, capture_output=True, timeout=300)
        (output / (label + ".out")).write_text(result.stdout)
        (output / (label + ".err")).write_text(result.stderr)
        if result.returncode:
            raise RuntimeError(label + " failed; inspect saved output. No automatic provider retry.")
        return result
    call(["task", "open", "--title", "Disposable task 6 bounded source trial", "--goal", GOAL], "open")
    call(["actor", "attach", "--target", "researcher", "--role", "researcher", "--capability", "build-context",
          "--capability", "add-claim", "--capability", "add-evidence"], "role")
    call(["stage", "transition", "--stage", "research", "--without-prerequisites", "Disposable task-6 trial fixture only"], "stage")
    cognitive = str(repo / "cognitive")
    call(["context", "build", "--cognitive-root", cognitive, "--output", str(output / "operator-manifest.json")], "brief")
    result = call(["provider", "launch", "--subject", "researcher", "--run", "R1", "--provider", args.provider,
                  "--executable", executable, "--working-directory", str(output / "work"),
                  "--cognitive-root", cognitive, "--timeout-seconds", "240"], "launch")
    launch = json.loads(result.stdout)
    events = [json.loads(line) for line in (ledger / "live/events.jsonl").read_text().splitlines()]
    claims = [e for e in events if e["data"]["eventType"] == "claim.added"]
    evidence = [e for e in events if e["data"]["eventType"] == "evidence.added"]
    completions = [e for e in events if e["data"]["eventType"] == "run.completed"]
    assert launch["status"] == "completed" and len(claims) == len(evidence) == 2 and len(completions) == 1
    rows = [json.loads(line) for file in (ledger / "live/telemetry").glob("findings-transport-*.jsonl")
            for line in file.read_text().splitlines()]
    calls = sorted([r for r in rows if r["message_kind"] == "tool_call"], key=lambda r: r["started_at"])
    assert len(calls) == 4
    assert [c["request_id"] for c in calls] == ["task6-early", "task6-later", "task6-later", "task6-later"]
    assert calls[1]["code"] == "kernel_refused" and calls[1]["commit_state"] == "not_committed"
    assert [calls[i]["replayed"] for i in (0, 2, 3)] == [False, False, True]
    assert calls[2]["transaction_id"] == calls[3]["transaction_id"]
    assert all(e["actorId"] == "researcher" and e["correlationId"] == "R1" for e in claims + evidence)
    for name, expected in plan["source_sha256"].items():
        assert digest(output / "work/sources" / Path(name).name) == expected, "Source snapshot changed"
    report = json.loads(call(["retrospective", "build", "--findings"], "retrospective").stdout)
    assert report["findings"]["committedTransactions"] == 2 and report["findings"]["granularEvents"] == 4
    assert report["findings"]["observedToolAttempts"] == report["findings"]["observedApplicationAttempts"] == 4
    assert len(report["findings"]["runs"]) == 1
    (output / "acceptance.json").write_text(json.dumps({"mechanical_acceptance": True,
        "semantic_review_required": True, "source_sha256": plan["source_sha256"],
        "claims": claims, "evidence": evidence, "completion": completions[0],
        "limitations": ["No loss injection in live run: retry is deliberate after observed success.",
                        "Mechanical checks do not establish truth of model observations."]}, indent=2) + "\n")
    print("Live mechanical checks passed; source-grounding/early-read-order review still required.")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("provider", choices=["codex", "claude"])
    parser.add_argument("--cli", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--execute", action="store_true", help="Requires explicit user authorization for one billable episode")
    args = parser.parse_args()
    if not args.cli.is_absolute():
        parser.error("CLI assembly path must be absolute")
    repo = Path(__file__).resolve().parents[2]
    output = args.output.resolve()
    if args.execute:
        execute(args, repo, output)
    else:
        prepare(args, repo, output)
