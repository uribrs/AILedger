#!/usr/bin/env python3
"""One short line per governed event, for a Monitor watch.

Every event is emitted, not only the good ones: a run that ends failed, cancelled or on a
protocol error is exactly what silence would otherwise hide, and an agent recording claims is
the sign that it is working rather than hung.
"""
import json, sys

def label(d):
    t = d.get("eventType", "?")
    bits = [v for k, v in d.items()
            if k != "eventType" and isinstance(v, (str, int)) and len(str(v)) < 44]
    return f"{t} {' '.join(str(b) for b in bits[:4])}".strip()

for line in sys.stdin:
    line = line.strip()
    if not line:
        continue
    try:
        e = json.loads(line)
    except ValueError:
        continue
    d = e.get("data") or {}
    kind = d.get("eventType", "")
    # Milestones only. A claim or an evidence record per line is the sign an agent is alive, but
    # one notification per record costs a turn and buries the events that change what happens next.
    # Run lifecycle covers liveness anyway, and it covers failure: failed, cancelled and
    # protocolError all arrive here, so silence still cannot hide a crash.
    if not (kind.startswith(("run.", "stage.", "escalation.", "artifact.", "lesson."))
            or kind in {"work.completed", "work.blocked", "work.abandoned",
                        "decision.resolved", "claim.resolved", "challenge.disposed"}):
        continue
    version = e.get("eventId", ":").split(":")[-1].lstrip("0") or "0"
    print(f"v{version} {e.get('actorId','?')} — {label(d)}", flush=True)
