#!/usr/bin/env python3
"""validate-closeout.py — check that pipeline tasks closed out honestly.

The workflow-coordinator chain's weakest link is its last step: lessons get recorded
after the work is delivered, when nobody is watching. This script checks what is on
disk instead of trusting that the step ran.

Usage:
    python3 validate-closeout.py                 # scan ai/active + ai/done under cwd
    python3 validate-closeout.py --task <path>   # check one task directory
    python3 validate-closeout.py --quiet         # print nothing when clean

Exit codes:
    0  nothing to check, or every task closed out cleanly
    1  at least one ERROR (a closed task that lost evidence)

WARNs never fail the run: they flag a task that is about to lose a lesson but is
still legitimately open.
"""

import argparse
import os
import re
import sys
import time
from pathlib import Path

AILEDGER = Path(os.environ.get("AILEDGER_HOME", Path.home() / "Dev" / "AILedger"))
LEDGER = AILEDGER / "lessons.md"

STALE_DAYS = 7
TERMINAL_STATUS = {"closed", "complete", "completed", "done", "archived"}
LESSON_BEARING = {"REJECTED", "NEVER-TESTED"}
ALL_STATUSES = {"VALIDATED", "REJECTED", "NEVER-TESTED", "OPEN"}
ROW = re.compile(r"^\|(?P<cells>.+)\|\s*$")


def read(path):
    try:
        return path.read_text(encoding="utf-8", errors="replace")
    except OSError:
        return ""


def json_field(text, key):
    """Pull a top-level-ish string field without importing a JSON parser's strictness."""
    match = re.search(rf'"{key}"\s*:\s*"([^"]*)"', text)
    return match.group(1) if match else None


def json_bool(text, key):
    match = re.search(rf'"{key}"\s*:\s*(true|false)', text)
    return match.group(1) == "true" if match else None


def latest_verifier(task):
    files = sorted((task / "review").glob("verifier-*.md")) if (task / "review").is_dir() else []
    numbered = []
    for path in files:
        match = re.fullmatch(r"verifier-(\d+)\.md", path.name)
        if match:
            numbered.append((int(match.group(1)), path))
    return max(numbered, default=(None, None))[1]


def disposition_rows(text):
    """Return [(id, status, citation, actor)] from the first table that looks like a disposition."""
    rows, in_table, cols = [], False, {}
    for line in text.splitlines():
        match = ROW.match(line.strip())
        if not match:
            if in_table and rows:
                break
            in_table = False
            continue
        cells = [c.strip() for c in match.group("cells").split("|")]
        lowered = [c.lower() for c in cells]
        if not in_table:
            # Column-name driven, not shape driven: a real archived table carries
            # "| id | assumption | status | citation | actor |". Requiring citation
            # is what excludes the attention table ("| id | final disposition |
            # evidence |"), whose header may never contain "status" for this reason.
            if ("status" in lowered and "citation" in lowered
                    and any(c.startswith("id") for c in lowered)):
                in_table = True
                cols = {name: i for i, name in enumerate(lowered)}
            continue
        if all(set(c) <= set("-: ") for c in cells):
            continue

        def pick(name):
            idx = next((i for key, i in cols.items() if key.startswith(name)), None)
            return cells[idx] if idx is not None and idx < len(cells) else ""

        status = pick("status").upper().replace("NEVER TESTED", "NEVER-TESTED")
        status = next((s for s in ALL_STATUSES if s in status), status)
        rows.append((pick("id"), status, pick("citation"), pick("actor")))
    return rows


def empty(cell):
    return cell.strip() in {"", "-", "—", "–", "n/a", "N/A", "none"}


def idle_days(task):
    newest = max((p.stat().st_mtime for p in task.rglob("*") if p.is_file()), default=0)
    return (time.time() - newest) / 86400 if newest else 0.0


def check_task(task, ledger_text, retrospective=False):
    """Return (errors, warns) for one task directory."""
    errors, warns = [], []
    name = task.name
    state = read(task / "state.json")
    status = (json_field(state, "status") or "unknown").lower()
    archived = "ai/done/" in str(task).replace(os.sep, "/")
    terminal = status in TERMINAL_STATUS or archived

    # The observed failure in this corpus is not a closed task that lost evidence — it is a
    # task that was finished in spirit and never closed, so step 5 was never reachable.
    # Staleness is a retrospective finding, never a gate: a session cannot be held responsible
    # for a task abandoned three months ago, and a gate that always fails gets switched off.
    idle = idle_days(task)
    if not terminal and idle >= STALE_DAYS:
        (errors if retrospective else warns).append(
            f"{name}: status {status!r} and idle {idle:.0f}d — abandoned mid-pipeline. "
            f"Close it or delete it; a task that never closes can never record a lesson")

    verifier = latest_verifier(task)
    if not verifier:
        if terminal:
            errors.append(f"{name}: closed with no review/verifier-*.md — nothing verified it")
        elif json_bool(state, "verifierRun"):
            errors.append(f"{name}: state.json says verifierRun but review/verifier-*.md is missing")
        return errors, warns

    rows = disposition_rows(read(verifier))
    if not rows:
        msg = f"{name}: {verifier.name} has no assumption disposition table"
        (errors if terminal else warns).append(msg)
        return errors, warns

    for rid, status_cell, citation, actor in rows:
        if status_cell == "OPEN":
            (errors if terminal else warns).append(
                f"{name}: assumption {rid} is still OPEN in {verifier.name} — OPEN is not a terminal status")
        elif status_cell in {"VALIDATED", "REJECTED"} and (empty(citation) or empty(actor)):
            (errors if terminal else warns).append(
                f"{name}: assumption {rid} is {status_cell} without "
                f"{'a citation' if empty(citation) else 'an actor'} — that is NEVER-TESTED wearing a verdict")
        elif status_cell not in ALL_STATUSES:
            warns.append(f"{name}: assumption {rid} has unrecognized status {status_cell!r}")

    bearing = [r for r in rows if r[1] in LESSON_BEARING]
    if bearing and name not in ledger_text:
        detail = ", ".join(f"{r[0]}:{r[1]}" for r in bearing[:4])
        msg = (f"{name}: {len(bearing)} lesson-bearing row(s) ({detail}) but no ledger row "
               f"points at this task — step 5 never appended")
        (errors if terminal else warns).append(msg)

    if terminal:
        plan = read(task / "orchestration_plan.md")
        if plan and "## File Ownership" not in plan:
            warns.append(f"{name}: orchestration_plan.md has no File Ownership section (1.4.0 rule)")

    return errors, warns


def find_tasks(root):
    found = []
    for bucket in ("ai/active", "ai/done"):
        base = root / bucket
        if base.is_dir():
            found += [p for p in sorted(base.iterdir()) if p.is_dir() and (p / "state.json").exists()]
    return found


def find_archived(since=None):
    """Scan the global archive: tasks/<repo>/<timestamp>_<slug>/. Used for retrospectives."""
    base = AILEDGER / "tasks"
    if not base.is_dir():
        return []
    found = []
    for repo in sorted(p for p in base.iterdir() if p.is_dir()):
        for task in sorted(p for p in repo.iterdir() if p.is_dir()):
            if since and task.name[:10] < since:
                continue
            found.append(task)
    return found


def main():
    parser = argparse.ArgumentParser(add_help=True)
    parser.add_argument("--task", help="check a single task directory")
    parser.add_argument("--root", default=".", help="repo root to scan (default: cwd)")
    parser.add_argument("--archive", action="store_true",
                        help="audit the global archive instead of cwd (retrospective mode; never gates)")
    parser.add_argument("--since", help="with --archive: only tasks dated on/after YYYY-MM-DD")
    parser.add_argument("--recent", type=float, metavar="DAYS",
                        help="only tasks touched within DAYS — what the Stop hook uses, so an old "
                             "backlog cannot block the session you are in")
    parser.add_argument("--quiet", action="store_true", help="print nothing when clean")
    args = parser.parse_args()

    if args.task:
        tasks = [Path(args.task)]
    elif args.archive:
        tasks = find_archived(args.since)
    else:
        tasks = find_tasks(Path(args.root))
    tasks = [t for t in tasks if t.is_dir()]
    if args.recent is not None:
        tasks = [t for t in tasks if idle_days(t) <= args.recent]
    if not tasks:
        if not args.quiet:
            print("close-out: no pipeline task directories here — nothing to check")
        return 0

    ledger_text = read(LEDGER)
    errors, warns = [], []
    for task in tasks:
        e, w = check_task(task, ledger_text, retrospective=args.archive)
        errors += e
        warns += w

    if errors:
        print(f"CLOSE-OUT FAILED — {len(errors)} error(s) across {len(tasks)} task(s)")
        print("A closed task that lost its evidence cannot be recovered later. Fix these before finishing.\n")
        for line in errors:
            print(f"  ERROR  {line}")
        if warns:
            print()
            for line in warns:
                print(f"  warn   {line}")
        print(f"\nLedger: {LEDGER}")
        return 1

    if warns:
        print(f"close-out: OK, {len(warns)} warning(s) on open task(s)")
        for line in warns:
            print(f"  warn   {line}")
        return 0

    if not args.quiet:
        print(f"close-out: OK — {len(tasks)} task(s) checked, evidence intact")
    return 0


if __name__ == "__main__":
    sys.exit(main())
