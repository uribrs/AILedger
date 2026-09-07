#!/usr/bin/env python3
"""Regenerate README.md — the browsable index over the lessons ledger and task archive.

Reads lessons.md and tasks/ and writes README.md. Never modifies the ledger.
Run after any append:  python3 build-readme.py

Parses both ledger formats:
  one-line (1.3.0+)  <date> | <repo> | <tags> | <CLASS> | <belief> | →<slug> [| supersedes:<id>]
  multi-line (1.2.0) ## <date> | <repo> | <tag> | <tag> | <CLASS>  followed by body lines
"""

import re
from collections import defaultdict
from datetime import date
from pathlib import Path

ROOT = Path(__file__).resolve().parent
LEDGER = ROOT / "lessons.md"
TASKS = ROOT / "tasks"
README = ROOT / "README.md"

RECENT = 10
CLASSES = ("REFUTED", "UNTESTED", "DRIFTED")
LEGACY_HEADER = re.compile(r"^##\s+(?:(L-[0-9a-f]{8})\s*\|\s*)?(\d{4}-\d{2}-\d{2})\s*\|\s*(.+)$")
ONE_LINE = re.compile(r"^(\d{4}-\d{2}-\d{2})\s*\|\s*(.+)$")
ID_ROW = re.compile(r"^(L-[0-9a-f]{8})\s*\|\s*(\d{4}-\d{2}-\d{2})\s*\|\s*(.+)$")


def entry_id(entry):
    """The row's id.

    1.5.0 rows carry it explicitly — mint-lesson.py assigns L-<sha1(belief)[:8]>,
    so it is unique, stable, and independent of the tags, which means re-tagging a
    row never breaks a `supersedes:` pointing at it.

    The derived fallback is the pre-1.5.0 rule, kept only so a hand-written row
    still resolves to something. It was replaced because it collided whenever one
    task refuted two beliefs sharing a first tag on the same day, and because the
    agents citing it never reproduced it consistently.
    """
    if entry.get("id"):
        return entry["id"]
    first_tag = entry["tags"][0] if entry["tags"] else "untagged"
    return f"{entry['date']}-{first_tag}"


def _split_tags(raw):
    return [t.strip() for t in raw.replace("|", ",").split(",") if t.strip()]


def parse_ledger():
    """Return (entries, superseded_ids). Tolerates both formats in one file."""
    if not LEDGER.exists():
        return [], set()

    entries, superseded, current = [], set(), None
    in_body = False

    for line in LEDGER.read_text(encoding="utf-8").splitlines():
        legacy = LEGACY_HEADER.match(line)
        if legacy:
            if current:
                entries.append(current)
            fields = [f.strip() for f in legacy.group(3).split("|")]
            cls = fields[-1] if fields and fields[-1] in CLASSES else "?"
            current = {
                "id": legacy.group(1) or "",
                "date": legacy.group(2),
                "repo": fields[0] if fields else "?",
                "tags": fields[1:-1] if cls != "?" else fields[1:],
                "class": cls,
                "belief": "",
                "pointer": "",
                "format": "multi-line",
                "refs": [],
            }
            in_body = True
            continue

        row = None
        if not line.startswith("#"):
            # 1.5.0 rows lead with the assigned id; 1.3.0 rows lead with the date.
            with_id = ID_ROW.match(line)
            if with_id:
                row = (with_id.group(1), with_id.group(2), with_id.group(3))
            else:
                bare = ONE_LINE.match(line)
                if bare:
                    row = ("", bare.group(1), bare.group(2))
        if row and "|" in row[2]:
            rid, rdate, rest = row
            fields = [f.strip() for f in rest.split("|")]
            # <repo> | <tags> | <CLASS> | <belief> | →<slug> [| supersedes:<id>]
            if len(fields) >= 5 and fields[2] in CLASSES:
                if current:
                    entries.append(current)
                    in_body = False
                refs = [f for f in fields[5:] if ":" in f]
                for ref in refs:
                    superseded.add(ref.split(":", 1)[1].strip())
                pointer = fields[4].lstrip("→ ").rstrip("/")
                if not pointer.startswith("tasks/"):
                    pointer = f"tasks/{fields[0]}/{pointer}"
                entries.append({
                    "id": rid,
                    "date": rdate,
                    "repo": fields[0],
                    "tags": _split_tags(fields[1]),
                    "class": fields[2],
                    "belief": fields[3],
                    "pointer": pointer,
                    "format": "one-line",
                    "refs": refs,
                })
                current = None
                continue

        if current is not None and in_body:
            stripped = line.strip()
            if stripped.startswith("→"):
                current["pointer"] = stripped.lstrip("→ ").rstrip("/")
            elif stripped and not current["belief"] and not stripped.startswith(("source:", "do not", "<!--")):
                current["belief"] = stripped

    if current:
        entries.append(current)
    return entries, superseded


def archive_stats():
    """Map repo -> (task count, newest task dir name) from the archive layout."""
    stats = {}
    if not TASKS.is_dir():
        return stats
    for repo in sorted(p for p in TASKS.iterdir() if p.is_dir()):
        slugs = sorted(p.name for p in repo.iterdir() if p.is_dir())
        if slugs:
            stats[repo.name] = (len(slugs), max(slugs))
    return stats


def truncate(text, limit=95):
    return text if len(text) <= limit else text[: limit - 1].rstrip() + "…"


def build():
    entries, superseded = parse_ledger()
    stats = archive_stats()

    for entry in entries:
        entry["superseded"] = entry_id(entry) in superseded

    live = [e for e in entries if not e["superseded"]]
    by_repo = defaultdict(int)
    for entry in live:
        by_repo[entry["repo"]] += 1

    total_tasks = sum(count for count, _ in stats.values())
    dates = sorted(e["date"] for e in entries)
    span = f"{dates[0]} → {dates[-1]}" if dates else "empty"
    classes = defaultdict(int)
    for entry in live:
        classes[entry["class"]] += 1
    breakdown = ", ".join(f"{n} {c}" for c, n in sorted(classes.items(), key=lambda kv: -kv[1]))
    stale = len(entries) - len(live)

    out = [
        "# AI Ledger",
        "",
        "Cross-repo lessons ledger and task archive for the `workflow-coordinator` skill chain.",
        "",
        "> Generated by `build-readme.py` — do not edit by hand. Re-run after any ledger append.",
        "",
        "## At a glance",
        "",
        f"- **{len(live)} live entries** ({breakdown or 'none'}), spanning {span}",
        f"- **{stale} superseded or retracted** — retained in the ledger, skipped by recall",
        f"- **{total_tasks} archived tasks** across {len(stats)} repos",
        "- Ledger: [`lessons.md`](lessons.md) — append-only, one line per entry, the index into `tasks/`",
        "",
        "## By repo",
        "",
        "| Repo | Live entries | Archived tasks | Newest task |",
        "| --- | --- | --- | --- |",
    ]

    for repo in sorted(set(stats) | set(by_repo), key=lambda r: (-by_repo.get(r, 0), r)):
        count, newest = stats.get(repo, (0, "—"))
        link = f"[{newest}](tasks/{repo}/{newest}/)" if count else "—"
        out.append(f"| `{repo}` | {by_repo.get(repo, 0) or '—'} | {count or '—'} | {link} |")

    out += ["", f"## Recent lessons (newest {RECENT})", ""]
    for entry in sorted(live, key=lambda e: e["date"], reverse=True)[:RECENT]:
        tags = ", ".join(f"`{t}`" for t in entry["tags"])
        out.append(f"**{entry['date']} — {entry['repo']}** · {tags} · **{entry['class']}**  ")
        out.append(f"{truncate(entry['belief'])}  ")
        if entry["pointer"]:
            target = Path(entry["pointer"])
            detail = ROOT / entry["pointer"] / "lesson.md"
            link = f"→ [{target.name}]({entry['pointer']}/)"
            if detail.exists():
                link += f" · [lesson.md]({entry['pointer']}/lesson.md)"
            out.append(link)
        out.append("")

    if stale:
        out += ["## Superseded and retracted", "", "Kept for provenance; recall drops these.", ""]
        for entry in sorted((e for e in entries if e["superseded"]), key=lambda e: e["date"], reverse=True):
            out.append(f"- ~~{entry['date']} — {entry['repo']} · {truncate(entry['belief'], 70)}~~")
        out.append("")

    out += [
        "## Layout",
        "",
        "```text",
        "lessons.md                    the ledger — coordinator appends at step 5, designer greps at recall",
        "tasks/<repo>/<ts>_<slug>/     the archive each ledger row points into",
        "  lesson.md                   the row's detail: belief, counter, source, verify command",
        "global/                       operator-level state read by the contract designer",
        "build-readme.py               regenerates this README",
        "```",
        "",
        "## How the chain uses this",
        "",
        "1. `prompt-contract-designer` derives 2–4 tags from a task and greps `lessons.md` for them.",
        "   It drops superseded and retracted rows, keeps the newest 10 matches, and follows **at most 3**",
        "   pointers into `tasks/`. Hits are seeded into `assumptions.md` as `OPEN` — never as fact, since",
        "   an entry is stale evidence about code that may have changed. Where a `verify:` command exists,",
        "   running it beats trusting the entry.",
        "2. `workflow-coordinator` step 5 appends one line per REJECTED / NEVER-TESTED / drifted row from",
        "   the verifier, writes the detail to `lesson.md` in the archive, and regenerates this index.",
        "   Confirmations are never recorded; they are noise in an index.",
        "",
        f"<!-- generated {date.today().isoformat()} -->",
    ]

    README.write_text("\n".join(out) + "\n", encoding="utf-8")
    return len(live), stale, total_tasks


if __name__ == "__main__":
    n_live, n_stale, n_tasks = build()
    print(f"README.md written: {n_live} live entries, {n_stale} superseded/retracted, {n_tasks} archived tasks")
