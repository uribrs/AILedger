"""Read-only RN1 extraction. Never executes historical shell text or reads a mutable projection."""
import argparse
from collections import Counter
from datetime import datetime
import hashlib
import json
from pathlib import Path
import shlex


def sha(data):
    return hashlib.sha256(data).hexdigest()


def extract(source, output):
    if output.resolve().is_relative_to(source.resolve()):
        raise ValueError("Extraction output must be outside historical source directory")
    sources = {name: (source / name).read_bytes() for name in ("events.jsonl", "runs/RN1.json")}
    history = [json.loads(line) for line in sources["events.jsonl"].splitlines()]
    run = json.loads(sources["runs/RN1.json"])
    calls, results = [], {}
    for event in run["events"]:
        raw = json.loads(event["rawJson"])
        message = raw.get("message", {}) if isinstance(raw, dict) else {}
        if not isinstance(message, dict):
            continue
        for part in message.get("content", []):
            if not isinstance(part, dict):
                continue
            if part.get("type") == "tool_use":
                calls.append({"sequence": event["sequence"], "recorded_at": event["recordedAt"], **part})
            elif part.get("type") == "tool_result":
                results[part["tool_use_id"]] = {"sequence": event["sequence"], "recorded_at": event["recordedAt"], **part}
    filing = [c for c in calls if c.get("name") == "Bash" and
              "RN1" in c["input"].get("command", "") and
              "claim add" in c["input"]["command"]]
    first = filing[0]
    command = first["input"]["command"]
    # This historical input uses two literal citation aliases. Parse only its bounded data lines.
    # shlex tokenizes data; no shell, eval, command substitution, or sourced historical code.
    aliases, findings, evidence = {}, [], []
    for line in command.splitlines():
        if line.startswith(('A="', 'I="')):
            key, value = shlex.split(line)[0].split("=", 1)
            aliases[key] = value
        elif line.startswith("c NC"):
            _, key, statement, consequence = shlex.split(line)
            findings.append({"key": key, "statement": statement, "consequence_if_wrong": consequence})
        elif line.startswith("e NE"):
            _, key, citation, summary, support = shlex.split(line)
            for alias, value in aliases.items():
                citation = citation.replace("$" + alias, value)
            if "$" in citation:
                raise ValueError("Unrecognized historical citation expansion")
            evidence.append({"key": key, "source_type": "source-read", "citation": citation,
                             "summary": summary, "supports": [{"finding": support}], "refutes": []})
    prepared = {"schema_version": 1, "request_id": "rn1-prepared", "findings": findings, "evidence": evidence}
    assert len(findings) == len(evidence) == 20
    assert {f["key"] for f in findings} == {f"NC{i}" for i in range(1, 21)}
    selected = [(i, e) for i, e in enumerate(history, 1) if e.get("correlationId") == "RN1"
                and e["data"]["eventType"] in ("claim.added", "evidence.added")]
    assert len(selected) == 40
    recorded = {"schema_version": 1, "request_id": "rn1-recorded", "findings": [], "evidence": []}
    for _, event in selected:
        data = event["data"]
        if data["eventType"] == "claim.added":
            c = data["claim"]
            recorded["findings"].append({"key": c["id"], "statement": c["statement"],
                                         "consequence_if_wrong": c.get("consequenceIfWrong")})
        else:
            e = data["evidence"]
            recorded["evidence"].append({"key": e["id"], "source_type": e["sourceType"],
                "citation": e["citation"], "summary": e["summary"],
                "supports": [{"finding": c} for c in e["supports"]],
                "refutes": [{"finding": c} for c in e["refutes"]]})
    differences = []
    for kind in ("findings", "evidence"):
        actual = {item["key"]: item for item in recorded[kind]}
        for item in prepared[kind]:
            for field, value in item.items():
                if value != actual[item["key"]][field]:
                    differences.append({"kind": kind, "key": item["key"], "field": field,
                                        "prepared": value, "recorded": actual[item["key"]][field]})
    denied = [c for c in filing if results[c["id"]].get("is_error")]
    successful = [c for c in filing if not results[c["id"]].get("is_error")]
    assert len(denied) == 3 and len(successful) == 11
    window_end = results[successful[-1]["id"]]["recorded_at"]
    seconds = (datetime.strptime(window_end, "%Y-%m-%dT%H:%M:%S.%f%z") - datetime.strptime(first["recorded_at"], "%Y-%m-%dT%H:%M:%S.%f%z")).total_seconds()
    artifact_values = {"prepared.json": prepared, "recorded.json": recorded,
                       "canonical-events.json": [e for _, e in selected], "text-differences.json": differences}
    output.mkdir(parents=True, exist_ok=False)
    hashes = {}
    for name, value in artifact_values.items():
        data = (json.dumps(value, ensure_ascii=False, indent=2) + "\n").encode()
        (output / name).write_bytes(data)
        hashes[name] = sha(data)
    (output / "prepared-command.txt").write_text(command)
    hashes["prepared-command.txt"] = sha((output / "prepared-command.txt").read_bytes())
    manifest = {"schema_version": 1, "source_directory": str(source.resolve()),
        "source_hashes": {name: sha(data) for name, data in sources.items()},
        "historical_task": selected[0][1]["taskId"], "historical_run": "RN1",
        "historical_actor": selected[0][1]["actorId"], "prepared_sequence": first["sequence"],
        "canonical_event_lines": [i for i, _ in selected], "sha256": hashes,
        "source_unchanged": all((source / name).read_bytes() == data for name, data in sources.items()),
        "historical_filing": {"started_at": first["recorded_at"], "ended_at": window_end,
            "elapsed_seconds": seconds, "denied_calls": len(denied), "successful_calls": len(successful),
            "attempts": [{"tool_use_id": c["id"], "sequence": c["sequence"], "started_at": c["recorded_at"],
                "result_sequence": results[c["id"]]["sequence"], "ended_at": results[c["id"]]["recorded_at"],
                "denied": bool(results[c["id"]].get("is_error")),
                "denial": results[c["id"]].get("content") if results[c["id"]].get("is_error") else None}
                for c in filing]},
        "changed_fields": dict(Counter(d["field"] for d in differences)),
        "limitations": ["Prepared strings are tokenized from the first denied script, never executed.",
            "Only the two recorded citation aliases are expanded; source and accepted text are both retained.",
            "Text differences do not by themselves establish semantic loss or why wording changed.",
            "Historical elapsed time includes model scheduling and retries; local MCP timing is not a causal speedup estimate."]}
    assert manifest["source_unchanged"]
    (output / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n")
    print(json.dumps({"records": len(selected), "historical_seconds": seconds,
                      "changed_fields": manifest["changed_fields"], "source_unchanged": True}))


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("source", type=Path)
    parser.add_argument("output", type=Path, help="New extraction directory; existing outputs are refused")
    args = parser.parse_args()
    extract(args.source, args.output)
