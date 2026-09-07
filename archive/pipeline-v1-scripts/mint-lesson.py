#!/usr/bin/env python3
"""mint-lesson.py — write ledger rows and their detail. Called by workflow-coordinator step 5.

The row used to be hand-typed by the model. Three consequences, all observed:
the canonical one-line format was never once produced (every row in the ledger is
still the superseded 1.2.0 shape), the `<date>-<first-tag>` id collided the moment
one task refuted two beliefs about the same vendor on the same day, and the id was
not even reproducible — archived citations reference date+first-tag, date+second-tag
and date+both-tags in roughly equal measure.

So the model no longer formats anything. It supplies what only it knows -- the
belief, the counter-evidence, the citation -- and this script assigns the id and
writes every field whose value is mechanical.

Usage:
    mint-lesson.py --task <task-dir> < entries.json
    mint-lesson.py --task <task-dir> --dry-run < entries.json   # print, write nothing
    mint-lesson.py --schema                                     # print the input template

Input is JSON on stdin: one object, or a list of them. Stdin rather than flags
because beliefs and verify commands contain quotes, pipes and section signs, and a
model-authored shell command is exactly where that quoting breaks.

Exit codes:
    0  minted, or every entry was already recorded
    1  validation failed -- nothing written
    2  usage error
"""

import argparse
import hashlib
import json
import re
import shutil
import subprocess
import sys
from datetime import date
from pathlib import Path

ROOT = Path(__file__).resolve().parent
LEDGER = ROOT / "lessons.md"
ARCHIVE = ROOT / "tasks"

CLASSES = ("REFUTED", "UNTESTED", "DRIFTED")
BELIEF_MAX = 200
TAG_RE = re.compile(r"^[a-z0-9][a-z0-9.\-]*$")
ID_RE = re.compile(r"^L-[0-9a-f]{8}$")
ATTENTION_ID_RE = re.compile(r"^R\d+[a-z]?$", re.IGNORECASE)
REQUIRED = ("tags", "class", "belief", "counter", "source", "actor", "verify", "do_not")

SCHEMA = """[
  {
    "tags":    ["vendor-or-subsystem", "failure-class"],
    "class":   "REFUTED | UNTESTED | DRIFTED",
    "belief":  "one line: the claim that turned out to be false, standing alone",
    "counter": "what contradicted it, or what was never produced",
    "source":  "the citation the verifier recorded, e.g. Foo.cs:120 or a doc page",
    "actor":   "researcher | executor | verifier | recon",
    "verify":  "a command that re-confirms or refutes this today, or: none - <why not>",
    "do_not":  "what not to re-assume without which evidence",
    "ref":     "optional: the assumption or attention id this came from, e.g. A3 or R2",
    "title":   "optional: short heading for the detail block",
    "supersedes": null,
    "retracts":   null
  }
]"""


def die(problem, fix, code=1):
    """Errors are read by an agent with nobody watching, so lead with the repair."""
    print(f"error: {problem}\nfix:   {fix}", file=sys.stderr)
    sys.exit(code)


def belief_id(belief):
    """Content-addressed, so re-running step 5 cannot duplicate a row."""
    normal = " ".join(belief.split())
    return "L-" + hashlib.sha1(normal.encode("utf-8")).hexdigest()[:8]


def ledger_text():
    return LEDGER.read_text(encoding="utf-8") if LEDGER.exists() else ""


def known_ids(text):
    return set(re.findall(r"\bL-[0-9a-f]{8}\b", text))


def derive_repo(task):
    """The repo the work happened in.

    Path structure beats git here, and the order matters. Asked about a task that
    has already been synced into this repo's archive, `git rev-parse` answers
    "AILedger" -- the repo it is sitting in, not the repo it is about -- which would
    file the lesson under the wrong vendor and sync the copy to tasks/AILedger/.
    So: the archive layout first, then the ai/ marker, and git only as a fallback
    for a task directory that carries neither.
    """
    task = task.resolve()
    archive = ARCHIVE.resolve()
    if archive in task.parents:
        rel = task.relative_to(archive)
        if len(rel.parts) >= 2:
            return rel.parts[0]
    for parent in task.parents:
        if parent.name in ("active", "done") and parent.parent.name == "ai":
            return parent.parent.parent.name
    try:
        top = subprocess.run(["git", "-C", str(task), "rev-parse", "--show-toplevel"],
                             capture_output=True, text=True, timeout=10)
        if top.returncode == 0 and top.stdout.strip():
            return Path(top.stdout.strip()).name
    except (OSError, subprocess.SubprocessError):
        pass
    return task.parent.name


def latest_verifier(task):
    review = task / "review"
    files = sorted(review.glob("verifier-*.md")) if review.is_dir() else []
    numbered = []
    for path in files:
        match = re.fullmatch(r"verifier-(\d+)\.md", path.name)
        if match:
            numbered.append((int(match.group(1)), path))
    return max(numbered, default=(None, None))[1]


def lesson_bearing_ids(task):
    """Which assumption or attention ids the verifier disposed of in a lesson-bearing way.

    Ids, not a count: one lesson legitimately covers several assumptions (a real
    task grouped "A6 / A7" into one block), so comparing counts false-fires on
    correct work, and a warning that cries wolf is a warning nobody reads.

    Every verifier pass is read for assumptions: a repair pass can show a row
    VALIDATED that an earlier pass rejected, and the lesson still belongs to the
    ledger. Attention items are different: only a latest-pass not-applicable result
    is lesson-bearing. An earlier unresolved row that was repaired is not a lesson.
    Feeds a warning only -- never a rejection.
    """
    review = task / "review"
    if not review.is_dir():
        return {}
    found = {}
    for path in sorted(review.glob("verifier-*.md")):
        for line in path.read_text(encoding="utf-8", errors="replace").splitlines():
            line = line.strip()
            if not line.startswith("|"):
                continue
            cells = [c.strip() for c in line.strip("|").split("|")]
            if len(cells) < 2:
                continue
            status = cells[1].upper()
            if "REJECTED" not in status and "NEVER-TESTED" not in status \
                    and "NEVER TESTED" not in status:
                continue
            rid = cells[0].strip("`* ")
            if re.match(r"^[A-Za-z]\d+[a-z]?$", rid):
                found.setdefault(rid, path.name)

    latest = latest_verifier(task)
    if latest:
        for line in latest.read_text(encoding="utf-8", errors="replace").splitlines():
            line = line.strip()
            if not line.startswith("|"):
                continue
            cells = [c.strip() for c in line.strip("|").split("|")]
            if len(cells) < 2 or cells[1].upper().replace(" ", "-") != "NOT-APPLICABLE":
                continue
            rid = cells[0].strip("`* ")
            if ATTENTION_ID_RE.match(rid):
                found.setdefault(rid, latest.name)
    return found


def validate(entries, task, ledger):
    """Collect every problem before reporting, so one run fixes one round-trip."""
    problems = []
    ids_present = known_ids(ledger)

    if not task.is_dir():
        die(f"no such task directory: {task}",
            "pass --task with the path of the task directory being closed out")
    if not latest_verifier(task):
        die(f"{task.name} has no review/verifier-*.md",
            "an unverified task has nothing to teach. Run the verifier pass first; "
            "a lesson minted without one is worse than no lesson, because recall "
            "will hand it to a future task as evidence")

    for i, e in enumerate(entries):
        where = f"entry {i + 1}"
        if not isinstance(e, dict):
            problems.append((f"{where} is not an object", "each entry must be a JSON object"))
            continue

        for key in REQUIRED:
            if not str(e.get(key) or "").strip() and not (key == "tags" and e.get("tags")):
                problems.append((f"{where} has no {key}",
                                 f"supply {key}; run --schema to see every field"))

        cls = str(e.get("class") or "").strip().upper()
        if cls and cls not in CLASSES:
            # The verifier's disposition table and the ledger use different words for
            # the same outcome. An agent reading the table will reach for the table's
            # vocabulary, so name the translation rather than restating the enum.
            translation = {"REJECTED": "REFUTED", "NEVER-TESTED": "UNTESTED",
                           "NEVER TESTED": "UNTESTED", "DRIFT": "DRIFTED",
                           "ABANDONED": "DRIFTED", "NOT-APPLICABLE": "REFUTED",
                           "NOT APPLICABLE": "REFUTED"}.get(cls)
            if translation:
                problems.append((
                    f"{where} class is {e.get('class')!r}, which is the verifier's word",
                    f"the ledger's word for it is {translation} — the disposition table says "
                    f"{cls}, a ledger row says {translation}"))
            else:
                problems.append((f"{where} class is {e.get('class')!r}",
                                 f"use one of {', '.join(CLASSES)}"))

        tags = e.get("tags") or []
        if isinstance(tags, str):
            tags = [t.strip() for t in tags.split(",") if t.strip()]
        if not 1 <= len(tags) <= 4:
            problems.append((f"{where} has {len(tags)} tags",
                             "use 1-4: first the vendor or subsystem, then the failure class"))
        for t in tags:
            if not TAG_RE.match(str(t)):
                problems.append((f"{where} tag {t!r} is not a slug",
                                 "lowercase letters, digits, dot and hyphen only"))

        belief = " ".join(str(e.get("belief") or "").split())
        if "|" in belief:
            problems.append((f"{where} belief contains a pipe",
                             "the ledger is pipe-delimited; reword without '|'"))
        if len(belief) > BELIEF_MAX:
            problems.append((f"{where} belief is {len(belief)} chars, over the {BELIEF_MAX} cap",
                             "the row is an index entry. Move the detail into 'counter' and "
                             "leave the belief as the bare claim that turned out false"))

        verify = str(e.get("verify") or "").strip()
        if verify and re.match(r"(?i)^none\b", verify) and not re.match(
                r"(?i)^none\s*[-—–:]\s*\S", verify):
            problems.append((f"{where} verify says 'none' with no reason",
                             "write 'none - citation is not mechanically checkable', or a real "
                             "command. Do NOT invent a plausible command to satisfy this check: "
                             "a fabricated verify reads as evidence to every future recall"))

        for field in ("supersedes", "retracts"):
            target = str(e.get(field) or "").strip()
            if not target:
                continue
            if not ID_RE.match(target):
                problems.append((f"{where} {field} is {target!r}",
                                 "an id looks like L-a3f9c1e2; take it from the row you are correcting"))
            elif target not in ids_present:
                problems.append((f"{where} {field} points at {target}, which is not in the ledger",
                                 "grep lessons.md for the row you mean and copy its id verbatim"))

    if problems:
        print(f"error: {len(problems)} problem(s); nothing was written.\n", file=sys.stderr)
        for problem, fix in problems:
            print(f"  - {problem}\n    fix: {fix}", file=sys.stderr)
        sys.exit(1)


def normalize(entries, task, repo, today):
    out = []
    for e in entries:
        tags = e.get("tags")
        if isinstance(tags, str):
            tags = [t.strip() for t in tags.split(",") if t.strip()]
        belief = " ".join(str(e["belief"]).split())
        out.append({
            "id": belief_id(belief),
            "date": today,
            "repo": repo,
            "tags": [str(t) for t in tags],
            "class": str(e["class"]).strip().upper(),
            "belief": belief,
            "counter": str(e["counter"]).strip(),
            "source": str(e["source"]).strip(),
            "actor": str(e["actor"]).strip(),
            "verify": str(e["verify"]).strip(),
            "do_not": str(e["do_not"]).strip(),
            "ref": str(e.get("ref") or "").strip(),
            "title": str(e.get("title") or "").strip(),
            "pointer": task.name,
            "supersedes": str(e.get("supersedes") or "").strip(),
            "retracts": str(e.get("retracts") or "").strip(),
        })
    return out


def row_for(e):
    row = (f"{e['id']} | {e['date']} | {e['repo']} | {','.join(e['tags'])} | "
           f"{e['class']} | {e['belief']} | →{e['pointer']}")
    if e["supersedes"]:
        row += f" | supersedes:{e['supersedes']}"
    if e["retracts"]:
        row += f" | retracts:{e['retracts']}"
    return row


def block_for(e):
    heading = " — ".join(p for p in (e["id"], e["ref"], e["title"]) if p)
    return "\n".join([
        f"## {heading}",
        f"belief:  {e['belief']}",
        f"counter: {e['counter']}",
        f"source:  {e['source']} ({e['actor']}, {e['date']})",
        f"verify:  {e['verify']}",
        f"do not:  {e['do_not']}",
        "",
    ])


def write_lesson_md(task, entries, slug, today):
    path = task / "lesson.md"
    existing = path.read_text(encoding="utf-8") if path.exists() else ""
    parts = [existing.rstrip()] if existing.strip() else [f"# Lessons — {slug} ({today})"]
    for e in entries:
        parts.append("")
        parts.append(block_for(e).rstrip())
    path.write_text("\n".join(parts) + "\n", encoding="utf-8")
    return path


def main():
    ap = argparse.ArgumentParser(add_help=True)
    ap.add_argument("--task", help="the task directory being closed out")
    ap.add_argument("--dry-run", action="store_true", help="print what would be written, write nothing")
    ap.add_argument("--schema", action="store_true", help="print the stdin template and exit")
    args = ap.parse_args()

    if args.schema:
        print(SCHEMA)
        return
    if not args.task:
        die("no --task given", "mint-lesson.py --task <task-dir> < entries.json ; "
                              "run --schema to see the input template", code=2)
    if sys.stdin.isatty():
        die("no JSON on stdin", "pipe the entries in:  mint-lesson.py --task <dir> <<'JSON' ... JSON"
                               "  -- run --schema for the template", code=2)

    raw = sys.stdin.read().strip()
    if not raw:
        die("stdin was empty", "run --schema for the template", code=2)
    try:
        parsed = json.loads(raw)
    except json.JSONDecodeError as exc:
        die(f"stdin is not valid JSON ({exc})",
            "check the heredoc is quoted <<'JSON' so nothing is expanded, and that "
            "every embedded quote is escaped", code=2)

    entries_in = parsed if isinstance(parsed, list) else [parsed]
    if not entries_in:
        die("no entries supplied", "pass at least one entry, or skip step 5 entirely if the "
                                  "verifier found nothing lesson-bearing", code=2)

    task = Path(args.task).resolve()
    ledger = ledger_text()
    validate(entries_in, task, ledger)

    repo = derive_repo(task)
    today = date.today().isoformat()
    entries = normalize(entries_in, task, repo, today)

    already = known_ids(ledger)
    fresh = [e for e in entries if e["id"] not in already]
    skipped = [e for e in entries if e["id"] in already]

    bearing = lesson_bearing_ids(task)
    if bearing:
        cited = set()
        for e in entries:
            cited.update(re.findall(r"[A-Za-z]\d+[a-z]?", e["ref"]))
        missing = sorted(set(bearing) - cited)
        if missing:
            print("warning: no entry references " + ", ".join(
                f"{m} ({bearing[m]})" for m in missing) +
                ". Group it into an existing entry's 'ref' if it is already covered.",
                file=sys.stderr)

    for e in skipped:
        print(f"already recorded: {e['id']}  {e['belief'][:60]}")

    if not fresh:
        print("nothing to mint; every entry is already in the ledger.")
        return

    if args.dry_run:
        print(f"--- would append to {LEDGER} ---")
        for e in fresh:
            print(row_for(e))
        print(f"\n--- would write {task / 'lesson.md'} ---")
        for e in fresh:
            print(block_for(e))
        target = ARCHIVE / repo / task.name
        if target.resolve() == task:
            print(f"--- would skip the archive sync: {task} is already the archive copy ---")
        else:
            print(f"--- would sync {task} -> {target} ---")
        print(f"--- would run build-readme.py ---")
        return

    with LEDGER.open("a", encoding="utf-8") as fh:
        if ledger and not ledger.endswith("\n"):
            fh.write("\n")
        for e in fresh:
            fh.write(row_for(e) + "\n")

    write_lesson_md(task, fresh, task.name, today)

    # The pointer names tasks/<repo>/<slug>, so the copy has to exist for the row to
    # resolve. When --task is already that path, there is nothing to copy: copytree
    # onto itself raises, and it would raise *after* the row was appended, leaving a
    # half-finished close-out behind.
    dest = ARCHIVE / repo / task.name
    synced = dest
    if dest.resolve() == task:
        synced = None
    else:
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copytree(task, dest, dirs_exist_ok=True)

    sys.path.insert(0, str(ROOT))
    import importlib.util
    spec = importlib.util.spec_from_file_location("build_readme", ROOT / "build-readme.py")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    module.build()

    print(f"minted {len(fresh)} row(s) into {LEDGER.name}:")
    for e in fresh:
        print(f"  {e['id']}  {e['class']:8}  {','.join(e['tags'])}")
    print(f"detail:  {task / 'lesson.md'}")
    print(f"archive: {synced if synced else str(task) + '  (already the archive copy)'}")
    print("index:   README.md regenerated")


if __name__ == "__main__":
    main()
