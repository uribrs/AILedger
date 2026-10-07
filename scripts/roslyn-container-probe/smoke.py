#!/usr/bin/env python3
"""Real-container semantic, refresh and forced-cleanup checks; no model calls."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile
import time
from worker import Worker


def request(identifier, method, params):
    return {"jsonrpc": "2.0", "id": identifier, "method": method, "params": params}


def initialize(worker):
    worker.exchange(request(1, "initialize", {"protocolVersion": "2024-11-05",
        "capabilities": {}, "clientInfo": {"name": "smoke", "version": "1"}}))
    worker.exchange({"jsonrpc": "2.0", "method": "notifications/initialized"})


def call(worker, identifier, name, arguments):
    result = worker.exchange(request(identifier, "tools/call", {"name": name, "arguments": arguments}))
    assert "error" not in result and not result["result"].get("isError"), result
    return result["result"]["content"][0]["text"]


def check(worker, solution, symbol):
    initialize(worker)
    loaded = call(worker, 2, "load_solution", {"path": str(solution), "background": False})
    assert "unresolved" not in loaded and "Loaded 1 project(s)" in loaded, loaded
    listing = json.loads(call(worker, 3, "list_solutions", {}))["items"]
    assert len(listing) == 1 and listing[0]["projectCount"] == 1
    assert not listing[0]["skippedProjects"]
    definitions = json.loads(call(worker, 4, "go_to_definition", {"symbol": symbol}))["items"]
    assert any(x["fullName"] == symbol for x in definitions), definitions
    references = json.loads(call(worker, 5, "find_references", {"symbol": symbol, "limit": 10}))["items"]
    assert references, references


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--image", required=True)
    parser.add_argument("--packages", required=True, type=Path)
    parser.add_argument("--shared-parent", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=False)
    results = {}
    with tempfile.TemporaryDirectory(prefix="roslyn-smoke-", dir=args.shared_parent) as directory:
        snapshot = Path(directory).resolve()
        solution = snapshot / "Fixture.slnx"
        solution.write_text('<Solution><Project Path="Fixture.csproj" /></Solution>')
        (snapshot / "Fixture.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk">'
            '<PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>')
        source = snapshot / "Example.cs"
        original = 'namespace Fixture; public class BeforeEdit { } public class Consumer { public BeforeEdit Use() => new BeforeEdit(); }'
        source.write_text(original)

        def start(name):
            output = args.output / name
            output.mkdir()
            return Worker(argparse.Namespace(snapshot=snapshot, packages=args.packages.resolve(),
                output=output, image=args.image, docker="/opt/homebrew/bin/docker"))

        worker = start("initial")
        try:
            check(worker, solution, "Fixture.BeforeEdit")
            source.write_text(original.replace("BeforeEdit", "AfterEdit"))
            try:
                call(worker, 6, "rebuild_solution", {})
                raise AssertionError("Changed snapshot was silently accepted")
            except ValueError as error:
                assert "Snapshot changed" in str(error)
                results["staleSnapshotRefused"] = True
            finally:
                source.write_text(original)
        finally:
            worker.close()
        results["initialSemantics"] = True
        source.write_text(original.replace("BeforeEdit", "AfterEdit"))
        worker = start("refreshed")
        try:
            check(worker, solution, "Fixture.AfterEdit")
        finally:
            worker.close()
        results["freshWorkerObservesEdit"] = True
        worker = start("forced-cleanup")
        try:
            initialize(worker)
            subprocess.run([worker.args.docker, "pause", worker.name], check=True,
                           stdout=subprocess.DEVNULL, timeout=10)
        finally:
            started = time.monotonic()
            worker.close()
            results["forcedCleanupSeconds"] = round(time.monotonic() - started, 2)
        for name in ("initial", "refreshed", "forced-cleanup"):
            receipt = json.loads((args.output / name / "cleanup.json").read_text())
            assert receipt["containerRemoved"] and receipt["snapshotUnchanged"], receipt
        results["allContainersRemoved"] = True
    (args.output / "result.json").write_text(json.dumps(results, indent=2))
    print(json.dumps(results), flush=True)


if __name__ == "__main__":
    main()
