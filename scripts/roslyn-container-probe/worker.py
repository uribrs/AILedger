#!/usr/bin/env python3
"""Experimental host-owned Roslyn worker. Never run this inside a provider session.

Only for disposable, pre-sanitized source snapshots. This is not a product backend.
The provider gets a stdio client for a bounded, authenticated navigation endpoint,
never Docker access. The normal AILedger navigation bridge remains in front of it.
"""
import argparse
import hashlib
import hmac
import http.server
import json
import os
from pathlib import Path
import queue
import secrets
import signal
import subprocess
import threading
import time
import uuid

TOOLS = {
    "list_solutions", "search_symbols", "go_to_definition", "find_references",
    "find_callers", "get_overloads", "get_method_source", "find_tests_for_symbol",
    "rebuild_solution", "load_solution", "set_active_solution",
}
MAX_REQUEST = 1024 * 1024
MAX_RESPONSE = 8 * 1024 * 1024


def source_digest(root):
    digest = hashlib.sha256()
    for path in sorted(root.rglob("*")):
        if path.is_symlink():
            raise ValueError("Snapshot must not contain symbolic links")
        if path.is_file():
            digest.update(str(path.relative_to(root)).encode())
            digest.update(b"\0")
            digest.update(path.read_bytes())
    return digest.hexdigest()


def translate(value, before, after):
    # Tool results contain nested JSON serialized as text and prose with paths.
    if isinstance(value, str):
        return value.replace(before + "/", after + "/")
    if isinstance(value, list):
        return [translate(x, before, after) for x in value]
    if isinstance(value, dict):
        return {key: translate(item, before, after) for key, item in value.items()}
    return value


class Worker:
    def __init__(self, args):
        self.args = args
        self.name = "ailedger-roslyn-probe-" + uuid.uuid4().hex[:12]
        self.lock = threading.Lock()
        self.responses = queue.Queue(maxsize=32)
        self.failed = False
        self.initialized = False
        self.digest = source_digest(args.snapshot)
        self.stderr = (args.output / "backend.stderr").open("wb")
        self.audit = (args.output / "calls.jsonl").open("a")
        uid, gid = os.getuid(), os.getgid()
        command = [
            args.docker, "run", "--rm", "--name", self.name,
            "--label", "ailedger.probe=roslyn-container",
            "--network", "none", "--read-only", "--cap-drop", "ALL",
            "--security-opt", "no-new-privileges", "--pids-limit", "256",
            "--memory", "3g", "--cpus", "2", "--user", f"{uid}:{gid}",
            "--tmpfs", "/tmp:rw,nosuid,nodev,size=512m",
            "--tmpfs", f"/workspace:rw,nosuid,nodev,size=1g,uid={uid},gid={gid},mode=700",
            "--mount", f"type=bind,src={args.snapshot},dst=/input,readonly",
            "--mount", f"type=bind,src={args.packages},dst=/packages,readonly",
            "--entrypoint", "sh", "-i", args.image, "-c",
            "set -e; cp -R /input/. /workspace/; exec /opt/roslyn/roslyn-codelens-mcp",
        ]
        self.process = subprocess.Popen(command, stdin=subprocess.PIPE,
                                        stdout=subprocess.PIPE, stderr=self.stderr)
        threading.Thread(target=self.read_responses, daemon=True).start()

    def read_responses(self):
        try:
            while True:
                line = self.process.stdout.readline(MAX_RESPONSE + 1)
                if not line or len(line) > MAX_RESPONSE:
                    raise RuntimeError("Backend EOF or response limit exceeded")
                self.responses.put(json.loads(line), timeout=55)
        except Exception:
            try:
                self.responses.put(None, timeout=1)
            except queue.Full:
                pass

    def validate(self, request):
        method = request.get("method")
        if method not in {"initialize", "notifications/initialized", "ping", "tools/list", "tools/call"}:
            raise ValueError("Unsupported navigation method")
        if method == "initialize" and self.initialized:
            raise ValueError("One client session per worker")
        if method != "initialize" and not self.initialized:
            raise ValueError("Initialize first")
        if method != "tools/call":
            return
        params = request.get("params", {})
        name = params.get("name")
        if name not in TOOLS:
            raise ValueError("Unsupported navigation tool")
        if source_digest(self.args.snapshot) != self.digest:
            raise ValueError("Snapshot changed; start a fresh worker to refresh navigation")
        if name in {"load_solution", "set_active_solution"}:
            key = "path" if name == "load_solution" else "name"
            value = params.get("arguments", {}).get(key)
            if not isinstance(value, str) or not Path(value).is_absolute():
                raise ValueError("Absolute snapshot solution required")
            path = Path(value).resolve(strict=True)
            path.relative_to(self.args.snapshot)
            if not path.is_file() or path.suffix not in {".sln", ".slnx"}:
                raise ValueError("Snapshot solution required")

    def exchange(self, request):
        with self.lock:
            if self.failed:
                raise RuntimeError("Worker unavailable after an earlier failure")
            self.validate(request)
            start = time.monotonic()
            wire = translate(request, str(self.args.snapshot), "/workspace")
            try:
                self.process.stdin.write(json.dumps(wire).encode() + b"\n")
                self.process.stdin.flush()
                response = {}
                if "id" in request:
                    deadline = start + 55
                    while True:
                        response = self.responses.get(timeout=max(0.001, deadline - time.monotonic()))
                        if response is None:
                            raise RuntimeError("Backend exited")
                        if response.get("id") == request["id"]:
                            break
                        if "id" in response or time.monotonic() >= deadline:
                            raise RuntimeError("Unexpected response or timeout")
                self.initialized |= request.get("method") == "initialize" and "result" in response
                if request.get("method") == "tools/list":
                    result = response.get("result", {})
                    result["tools"] = [t for t in result.get("tools", []) if t.get("name") in TOOLS]
                self.audit.write(json.dumps({"method": request.get("method"),
                    "tool": request.get("params", {}).get("name"),
                    "seconds": round(time.monotonic() - start, 3),
                    "response": translate(response, "/workspace", str(self.args.snapshot))}) + "\n")
                self.audit.flush()
                return translate(response, "/workspace", str(self.args.snapshot))
            except Exception:
                self.failed = True
                raise

    def close(self):
        self.process.stdin.close()
        try:
            self.process.wait(timeout=5)
        except subprocess.TimeoutExpired:
            subprocess.run([self.args.docker, "rm", "--force", self.name],
                           check=True, timeout=20, stdout=subprocess.DEVNULL)
            self.process.wait(timeout=10)
        result = subprocess.run([self.args.docker, "inspect", self.name],
                                capture_output=True, timeout=10)
        # A daemon error is not evidence of removal.
        removed = result.returncode != 0 and b"no such object" in result.stderr.lower()
        (self.args.output / "cleanup.json").write_text(json.dumps({
            "container": self.name, "exitCode": self.process.returncode,
            "containerRemoved": removed,
            "snapshotUnchanged": source_digest(self.args.snapshot) == self.digest,
        }, indent=2))
        self.stderr.close()
        self.audit.close()
        if not removed:
            raise RuntimeError("Container removal could not be established")


CLIENT = '''#!/usr/bin/python3
import json, sys, urllib.request
opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
for line in sys.stdin:
    request = json.loads(line)
    data = line.encode()
    message = urllib.request.Request(URL, data=data, headers={
        "Authorization": "Bearer " + TOKEN, "Content-Type": "application/json"})
    with opener.open(message, timeout=58) as response:
        body = response.read(8 * 1024 * 1024 + 1)
        if len(body) > 8 * 1024 * 1024: raise RuntimeError("Response too large")
    if "id" in request:
        print(body.decode(), flush=True)
'''


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--snapshot", required=True, type=Path)
    parser.add_argument("--packages", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--image", required=True, help="Immutable local Docker image ID")
    parser.add_argument("--docker", default="/opt/homebrew/bin/docker")
    args = parser.parse_args()
    args.snapshot, args.packages, args.output = (p.resolve() for p in (args.snapshot, args.packages, args.output))
    if not args.image.startswith("sha256:") or len(args.image) != 71:
        raise ValueError("Pin the built worker by its full sha256 image ID")
    if args.output == args.snapshot or args.snapshot in args.output.parents:
        raise ValueError("Worker output must be outside the source snapshot")
    if any(p.name in {".ailedger", ".git", ".claude", ".codex", ".agents"} for p in args.snapshot.rglob("*")):
        raise ValueError("Only sanitized snapshots may be mounted")
    args.output.mkdir(parents=True, exist_ok=False)
    worker = Worker(args)
    token = secrets.token_urlsafe(32)

    class Handler(http.server.BaseHTTPRequestHandler):
        def log_message(self, *_):
            pass

        def do_POST(self):
            if self.path != "/exchange" or not hmac.compare_digest(self.headers.get("Authorization", ""), "Bearer " + token):
                self.send_error(403)
                return
            try:
                length = int(self.headers.get("Content-Length", "0"))
                if not 0 < length <= MAX_REQUEST:
                    raise ValueError("Request size invalid")
                self.connection.settimeout(60)
                response = worker.exchange(json.loads(self.rfile.read(length)))
                body = json.dumps(response).encode()
                self.send_response(200)
                self.send_header("Content-Length", str(len(body)))
                self.end_headers()
                self.wfile.write(body)
            except Exception as error:
                worker.audit.write(json.dumps({"refusal": type(error).__name__}) + "\n")
                worker.audit.flush()
                self.send_error(502, "Navigation probe refused or failed; inspect host probe diagnostics")

    server = http.server.HTTPServer(("127.0.0.1", 0), Handler)
    url = f"http://127.0.0.1:{server.server_port}/exchange"
    client = args.output / "roslyn-client"
    client.write_text(CLIENT.replace("import json,", f"URL = {url!r}\nTOKEN = {token!r}\nimport json,"))
    client.chmod(0o700)
    (args.output / "ready.json").write_text(json.dumps({
        "executable": str(client), "container": worker.name,
        "snapshotSha256": worker.digest, "image": args.image,
    }, indent=2))
    print(json.dumps({"ready": str(args.output / "ready.json")}), flush=True)
    def stop(*_):
        raise KeyboardInterrupt()

    signal.signal(signal.SIGTERM, stop)
    try:
        server.serve_forever()
    except KeyboardInterrupt:
        pass
    finally:
        server.server_close()
        worker.close()


if __name__ == "__main__":
    main()
