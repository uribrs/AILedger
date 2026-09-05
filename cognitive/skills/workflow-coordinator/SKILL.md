---
name: workflow-coordinator
version: 1.7.0
description: Pure routing skill for non-trivial work. Sequences the planning, research, execution, verification, and code-review skills, then records lesson-bearing outcomes to a cross-repo ledger and archives the task so that every non-trivial task flows through the same disciplined pipeline and produces durable artifacts in a single task directory. Use this skill as the entry point whenever a request is non-trivial — implementation beyond a small one-file change, architecture or design decisions, multi-step refactoring, external API or vendor work, research, validation, or any work that should be resumable across conversations.
---

# Workflow Coordinator

## Purpose

Route a non-trivial user task through the right skills in the right order. Produce a persistent task directory that downstream skills (and future conversations) can read.

This skill is **pure routing**. It does not analyze, decompose, research, or judge. Every substantive decision is owned by a downstream skill.

## Core Rule

Use this skill at the start of any non-trivial task. Skip for trivial work (single-line edits, syntax fixes, simple explanations, one-off lookups).

The coordinator's job is bounded to five things:

1. Confirm the machine is set up, and report what is missing.
2. Resolve the task directory path.
3. Invoke the planning and execution skills in the correct order.
4. Confirm the verifier and code-reviewer passes ran.
5. Transcribe the verifier's lesson-bearing rows to the global ledger and archive the task.

If you find yourself making planning judgments inside this skill, stop. Move the judgment to the skill that owns it.

## Host Runtime Mapping

Claude is the bundle's primary runtime and retains its harness-enforced `Stop` hook. When this skill
runs under Codex, preserve the same workflow and translate only the host integration points:

| Concern | Claude | Codex |
|---|---|---|
| Installed skills | `~/.claude/skills/` | `${CODEX_HOME:-$HOME/.codex}/skills/` |
| Ledger and scripts | `~/Dev/AILedger/` | `~/Dev/AILedger/` |
| Close-out enforcement | `Stop` hook invokes `closeout-hook.sh` | Coordinator invokes `validate-closeout.py --task <archived-task>` explicitly before reporting completion |

Do not treat a missing Claude hook as a broken Codex installation. The hook is stronger because the
harness runs it after the model tries to stop; explicit Codex validation preserves the check inside
the workflow but does not claim that same external enforcement.

## The Pipeline

```
workflow-coordinator
  0. Preflight  (ledger + scripts + host close-out mode available? report only what is missing)
  1. Resolve taskPath  (continue existing or create new)
  2. Invoke prompt-contract-designer   (performs prior-art recall; records baseRef)
  3. Invoke task-orchestrator
  4. Confirm verifier ran; confirm code-reviewer ran when code-bearing
  5. Record lessons to the global ledger; archive the task
  6. Report path of artifacts and any unresolved blockers
```

The orchestrator owns everything between step 2 and step 4 — research routing, execution-path choice, worker decomposition, synthesis, verifier, and code-reviewer invocation. The coordinator never reaches into those phases.

Step 5 is transcription, not judgment. The verifier already decided every assumption's status and every decision's fate; the coordinator copies the entries that would change a future task and files the directory away.

## Workflow

### 0. Preflight — check the machine is set up

The chain depends on state and tooling that live outside the skills. On a machine where those are
missing, the failure is silent and late: recall finds nothing, step 5 writes into the void, and nobody
learns that setup was incomplete. Check first, once, cheaply.

For every host:

```bash
ls ~/Dev/AILedger/lessons.md ~/Dev/AILedger/mint-lesson.py ~/Dev/AILedger/build-readme.py \
   ~/Dev/AILedger/validate-closeout.py 2>&1
```

On Claude, also check the harness gate:

```bash
ls ~/Dev/AILedger/closeout-hook.sh 2>&1
grep -c '"Stop"' ~/.claude/settings.json 2>/dev/null
```

On Codex, do not inspect Claude settings; the explicit validator call in step 5 is the close-out
mode supplied by this bundle.

Then act on what is missing:

| Missing | What to do |
|---|---|
| `~/Dev/AILedger/` or `lessons.md` | Create them yourself — `mkdir -p ~/Dev/AILedger/tasks && touch ~/Dev/AILedger/lessons.md`. The chain works from cold; it just has nothing to recall yet. Say one line that you did it. |
| `mint-lesson.py` | Tell the user step 5 cannot record anything — this script writes every ledger row. It ships in the chain's bundle: `cp <bundle>/mint-lesson.py ~/Dev/AILedger/`. Do not write a replacement from memory, and do not fall back to hand-writing a row: hand-written rows are what this script exists to eliminate. |
| `build-readme.py` | Tell the user step 5 cannot regenerate the ledger index, and that the file ships in the chain's bundle: `cp <bundle>/build-readme.py ~/Dev/AILedger/`. Do not write a replacement from memory. |
| `validate-closeout.py` | Same — name the bundle copy. Without it there is no close-out gate. |
| `closeout-hook.sh` on Claude | Same. It is the wrapper the hook calls; without it the hook command resolves to nothing. |
| No `Stop` hook on Claude | Tell the user the close-out gate is not armed, show the settings snippet below, and say it must be pasted by them. |

**Never write `~/.claude/settings.json` yourself.** It is the user's machine configuration, not task
state, and a hook runs a command on every session end — that is the user's decision to make. Print the
snippet and let them paste it:

```json
"hooks": {
  "Stop": [
    {
      "hooks": [
        {
          "type": "command",
          "command": "sh \"$HOME/Dev/AILedger/closeout-hook.sh\"",
          "timeout": 20,
          "statusMessage": "Checking task close-out..."
        }
      ]
    }
  ]
}
```

Print exactly that. Never print the pipeline inline instead: a long escaped one-liner breaks when a
paste wraps, the wrap becomes a literal newline inside a JSON string, and an invalid `settings.json`
silently disables every setting in the file rather than just the hook.

Mention that `/hooks` must be opened once afterwards, or the settings watcher will not pick the hook
up until the next restart.

**When everything is present, say nothing and continue.** This step exists to catch a broken install
once, not to narrate a healthy one on every task. Report only what is missing, with the fix, and then
proceed with the work rather than waiting — an unarmed gate degrades the chain's learning, it does not
block the task in front of you.

### 1. Resolve taskPath

If the request continues an existing task:

- Locate the matching directory under `ai/active/<timestamp>_<task-slug>/`.
- Do not create a new directory for the same work.
- Pass the existing path to the contract-designer so it updates rather than recreates.

If no relevant task directory exists, let `prompt-contract-designer` create one. Do not pre-create directories from the coordinator.

If the request is trivial, do not invoke this pipeline. Answer directly.

### 2. Invoke prompt-contract-designer

Always run the designer for non-trivial work, even when a contract already exists — re-running is idempotent and may add or update assumptions and decisions.

The designer produces or updates:

- `task.md`
- `prompt_contract.md`
- `state.json` (without the `workflow` block — orchestrator owns that; includes `baseRef`)
- `constraints.md`, `assumptions.md`, `decisions.md` (full tier)

The designer also performs prior-art recall against `~/Dev/AILedger/lessons.md` and seeds any hits into `assumptions.md` as OPEN entries with provenance. The coordinator does not perform recall itself and does not evaluate what recall returned.

After this step, the contract must be valid. If the designer reports it could not produce a valid contract, stop and surface the missing information to the user.

### 3. Invoke task-orchestrator

Pass `taskPath` to the orchestrator. The orchestrator reads the contract and:

- Classifies the task, grounds material failure modes against recon, and records their planned handling before choosing an execution path.
- Decides whether research is needed.
- Decides execution path (direct vs decompose).
- Writes `orchestration_plan.md`.
- Invokes `technical-researcher` if needed.
- Invokes `contract-driven-execution` or runs workers, depending on chosen path.
- Runs the verifier subagent, which disposes of every assumption against the diff from `baseRef` and records decision drift.
- Runs the code-reviewer subagent when work is code-bearing.
- Returns the assumption, attention-item, and drift rows for step 5.

The coordinator does not duplicate any of these steps and does not second-guess the orchestrator's decisions.

### 4. Confirm review passes ran

After the orchestrator returns, check `state.json`:

- `workflow.verifierRun` must be `true`.
- `workflow.codeReviewerRun` must be `true` when work is code-bearing (see code-bearing definition in `task-orchestrator`).

If either is false, stop and report. The coordinator does not silently skip these.

Then check the final `review/verifier-N.md` contains an assumption disposition table with a row for every assumption in `assumptions.md`, and that no row is still OPEN. When `orchestration_plan.md` contains Problem Classification attention items, also require the later Attention Item Disposition table to cover every R-id with no `unresolved` row, and every `handled` row to name the artifact it resolved and what resolving it returned. This is a presence check, not a judgment — the coordinator does not evaluate whether evidence is good, only that the required rows exist and are terminal. If rows are missing or non-terminal, stop and report; do not proceed to step 5.

### 5. Record lessons and archive

Mechanical transcription, and `mint-lesson.py` does the writing. No judgment — the verifier already
made every call.

1. Archive first: move `ai/active/<timestamp>_<task-slug>/` to `ai/done/<timestamp>_<task-slug>/`,
   creating `ai/done/` if needed. Update `state.json`: `status: "closed"`,
   `workflow.lessonsRecorded: true`, `workflow.archivedAt: "<timestamp>"`.
2. Read the assumption disposition, attention-item disposition, and decision-drift rows from the final `review/verifier-N.md`.
3. For every assumption with status **REJECTED** or **NEVER-TESTED**, every attention item disposed
   **not-applicable**, and every decision that **drifted or was abandoned**, build one JSON entry —
   then pipe them all to the minter in a **single** call.

   The disposition table and the ledger use different words for the same outcome, so translate:

   | verifier disposition | ledger `class` |
   |---|---|
   | REJECTED | `REFUTED` |
   | NEVER-TESTED | `UNTESTED` |
   | attention item `not-applicable` | `REFUTED` |
   | a decision that drifted or was abandoned | `DRIFTED` |

   Do not pass the table's word through; the script rejects it, though its rejection will tell you
   which word to use.

```bash
python3 ~/Dev/AILedger/mint-lesson.py --task ai/done/<timestamp>_<task-slug> <<'JSON'
[
  { "tags":    ["<vendor-or-subsystem>", "<failure-class>"],
    "class":   "REFUTED",
    "belief":  "<the claim that turned out to be false — one line, standing alone>",
    "counter": "<what contradicted it, or what was never produced>",
    "source":  "<the citation the verifier already recorded>",
    "actor":   "researcher | executor | verifier | recon",
    "verify":  "<a command that re-confirms or refutes this today>",
    "do_not":  "<what not to re-assume without which evidence>",
    "ref":     "<the assumption or attention id this came from, e.g. A3 or R2>" }
]
JSON
```

`--schema` prints the field list; `--dry-run` prints the rows and writes nothing. Run the minter
**after** the move so the archived copy carries the closed state, and pass the `ai/done/` path.

The script owns everything the row's integrity depends on: it assigns the id, stamps the date,
derives the repo and the pointer, appends the row(s), writes `lesson.md` into the task directory,
syncs the task to `~/Dev/AILedger/tasks/<repo>/<slug>/` — which is what the pointer names — and
regenerates the ledger index. **Never hand-write a ledger row.** Hand-writing is why the canonical
row format went three versions without once being produced, and why ids became unreproducible.

What the script will not do for you:

4. **The belief clause must stand alone.** Recall triages on that clause without opening the archive,
   so a clause that only reads correctly next to its counter-line is a defect. Write it as the claim
   that turned out to be false, not as a reference to one. The script enforces the length cap; only
   you can enforce this.
5. `verify` must be a command — a grep, a test filter, a path check — derived from the citation the
   verifier already recorded. A `file.cs:120` citation becomes a check against that path or symbol.
   If the citation yields no command, write `none — citation is not mechanically checkable`. The
   script rejects a bare `none`, and its rejection names that escape: take it rather than inventing a
   plausible command, because a fabricated `verify` reads as evidence to every future recall.
6. `ref` is the assumption or attention id the row came from. Supply it — the script uses it to tell you which
   lesson-bearing dispositions no entry covered, and that undercount is the one failure mode nothing
   else catches. One entry may legitimately cover several ids (`"ref": "A6 A7"`). Keep `ref` as bare ids — the script
extracts them with a letter-plus-digits pattern, so put the descriptive names in `title` instead.
7. When this task **overturns** a prior lesson, set `supersedes` to that row's id; when a prior lesson
   was **invalid when minted**, use `retracts`. Ids look like `L-a3f9c1e2`; the script refuses a
   target that is not in the ledger. Append only — never edit or delete the old row. The correction
   lives in the new row, and the history of a belief is itself evidence.
8. Skip VALIDATED rows; confirmations are noise in an index.

9. On Codex, after minting (or immediately after archival when there were no lesson-bearing rows),
   run the close-out validator against the archived local task and do not report completion if it
   fails:

```bash
python3 ~/Dev/AILedger/validate-closeout.py --task ai/done/<timestamp>_<task-slug>
```

   Claude may run this explicitly as a useful immediate check too, but its `Stop` hook remains the
   final harness-enforced gate.

Do not run this step when the verifier did not run, or when the disposition table is missing from
`verifier-N.md`. The script refuses a task directory with no `review/verifier-*.md` for the same
reason: an unverified task has nothing to teach, and a lesson minted from one is worse than no lesson,
because recall will hand it to a future task as evidence.

### 6. Report

In the final response, include:

- The task directory path.
- Whether the work completed, has unresolved blockers, or has accepted technical risks.
- A short pointer to where the human can read the artifacts (`prompt_contract.md`, `orchestration_plan.md`, `execution_notes.md`, `review/`).

## Task Directory Layout

The coordinator does not write these files itself, but it is the canonical reference for what the layout looks like. All downstream skills must conform.

```text
ai/active/<timestamp>_<task-slug>/          (moves to ai/done/ at step 5)
  state.json
  task.md
  prompt_contract.md
  constraints.md            (full tier)
  assumptions.md            (full tier; includes ## Prior Art and the final disposition)
  decisions.md              (full tier)
  execution_notes.md        (appended during execution)
  orchestration_plan.md     (written by task-orchestrator)
  research/
    <topic-slug>.md         (written by technical-researcher)
  review/
    verifier-N.md           (written by orchestrator's verifier subagent)
    code-reviewer-N.md      (written by orchestrator's code-reviewer subagent)
  lesson.md                 (written by mint-lesson.py at step 5; only when a lesson-bearing row exists)
```

Where:

- `<timestamp>` is `YYYY-MM-DD_HHMM`.
- `<task-slug>` is lowercase, hyphen-separated, concise, and describes the task — not the implementation guess.
- `N` is a numeric suffix that increments when review passes are re-run after repairs (`verifier-1.md`, `verifier-2.md`, etc.). Existing files are not overwritten.

A matching global archive lives at `~/Dev/AILedger/tasks/<repo-name>/<timestamp>_<task-slug>/` and uses the same `<timestamp>_<task-slug>` directory name.

Two locations sit outside any single task:

```text
~/Dev/AILedger/lessons.md      append-only cross-repo ledger, one line per entry; written by mint-lesson.py at step 5, read by the designer's recall
~/Dev/AILedger/mint-lesson.py  assigns each row's id and writes the row, lesson.md and the archive copy
~/Dev/AILedger/README.md       generated index over the ledger and archive; rebuilt by build-readme.py
ai/done/<timestamp>_<task-slug>/   closed tasks; keeps ai/active/ meaning something
```

The ledger is the index into the archive, and it holds no detail of its own — one line per entry, with `lesson.md` in the archived task directory carrying the belief, counter-evidence, citation and verify command. Recall greps the ledger, caps its match set, and follows at most three pointers. Nothing in this pipeline scans task directories in bulk, and nothing reads the whole ledger, because both costs grow with every task ever run.

## state.json — Shared Protocol

`state.json` is the machine-readable source of truth. The contract-designer creates it; the orchestrator extends it with a `workflow` block; every skill updates its own fields on completion.

The base structure (taskId, taskSlug, complexityTier, requiredFiles, validation, etc.) is defined in `prompt-contract-designer/SKILL.md`. This section documents only the `workflow` block extension that the orchestrator adds.

```json
{
  "workflow": {
    "executionPath": "direct" | "decompose",
    "orchestrationPlanFile": "orchestration_plan.md",
    "researchNeeded": false,
    "researchTopics": [
      {
        "topic": "<topic-slug>",
        "file": "research/<topic-slug>.md",
        "status": "pending" | "complete",
        "triggeredBy": "assumption:<assumption-id>" | "classification:<normalized-tag>"
      }
    ],
    "workers": [
      {
        "id": "W1",
        "name": "<descriptive-name>",
        "scopeSummary": "...",
        "status": "pending" | "in_progress" | "complete",
        "outputRef": "execution_notes.md#<descriptive-name>"
      }
    ],
    "skillsRun": [
      {
        "skill": "prompt-contract-designer",
        "completedAt": "YYYY-MM-DDTHH:mm:ssZ",
        "outputs": ["task.md", "prompt_contract.md", "state.json"]
      }
    ],
    "verifierRun": false,
    "codeReviewerRun": false,
    "lessonsRecorded": false,
    "archivedAt": null
  }
}
```

Ownership of fields:

- `executionPath`, `orchestrationPlanFile`, `researchNeeded`, `workers`, `verifierRun`, `codeReviewerRun` — written by `task-orchestrator`.
- `lessonsRecorded`, `archivedAt` — written by the coordinator at step 5.
- `baseRef` (base structure, not in the `workflow` block) — written by `prompt-contract-designer` at contract time; the verifier reads it to scope the diff.
- `researchTopics` entries — appended by `task-orchestrator` when scheduling research, updated to `complete` by `technical-researcher` when output is written.
- `skillsRun` entries — appended by each skill on completion.

If `state.json` conflicts with the markdown files, stop and report. Do not continue until resolved.

`topic` is always the filesystem-safe slug used for `research/<topic>.md`. `triggeredBy` records why the topic exists and may contain a ledger-compatible classification tag; never derive the filename from `triggeredBy`.

## Stop Conditions

Stop and surface the issue when:

- The request is ambiguous in a way that would cause wrong implementation.
- The contract-designer reports a contract cannot be made valid.
- The orchestrator returns without running the verifier.
- The orchestrator returns without running the code-reviewer for code-bearing work.
- The verifier output has no assumption disposition table, assumptions remain OPEN, or a planned attention item is missing or unresolved. Do not archive and do not record lessons — report it.
- `state.json` conflicts with the markdown files.
- Sandbox or approval restrictions block a required write.

Stopping is not failure. Routing past a broken step is.

## What This Skill Does NOT Do

To prevent scope creep, this skill explicitly does not:

- Analyze the task or assess complexity (orchestrator's job).
- Decide whether research is needed (orchestrator's job).
- Decompose into workers or assign worker scope (orchestrator's job).
- Invoke `technical-researcher`, `contract-driven-execution`, or `code-reviewer` directly (the orchestrator invokes them).
- Synthesize worker outputs (orchestrator's job).
- Run or evaluate the verifier or code-reviewer passes (orchestrator's job; coordinator only confirms they ran).
- Write `~/.claude/settings.json`, Codex configuration, or any other machine configuration. Preflight reports and prints; the user decides what the host runs automatically.
- Perform prior-art recall or evaluate what it returned (the designer owns initial recall; the orchestrator owns the classified delta).
- Judge whether an assumption held, an attention item was handled, or a decision drifted (verifier's job; the coordinator transcribes its rows verbatim).
- Write any task files (every file has a specific owner skill). Step 5 moves the directory and stamps `state.json`; the ledger row, `lesson.md` and the archive copy are written by `mint-lesson.py` from the JSON the coordinator hands it. The coordinator authors no task content of its own.

If a future change tempts you to add any of these to the coordinator, push them down into the right skill instead.

## Output

In the final response, report:

- The task directory path.
- The execution path the orchestrator chose, and a one-line rationale.
- Whether research was performed, and what topics.
- Verifier and code-reviewer outcomes (pass / repairs made / accepted risks).
- Any assumption that ended NEVER-TESTED or REJECTED, and any attention item left unresolved. Name these even when the work succeeded — they are the part the operator cannot recover later.
- What was appended to the ledger, and where the task was archived.
- Any unresolved blocker.

Keep the report short. The artifacts in the task directory are the durable record; the response is a pointer.
