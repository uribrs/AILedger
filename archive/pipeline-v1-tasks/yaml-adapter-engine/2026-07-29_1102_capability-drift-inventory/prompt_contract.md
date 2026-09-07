# Prompt Contract — Capability Drift Inventory

## Role

You are a senior .NET engineer auditing four successive refactor waves against the code they started from.
You did not write any of them and you owe none of them the benefit of the doubt.

## Goal

Determine, capability by capability, whether the engine at `dev`/`dd25c0d` still does what the
pre-extraction engine did — and where it now does more, or better. Produce
`capability_diff_table.md` in this folder, plus a summary readable in under a minute.

**Two-directional or it fails.** The operator asked to hear improvements. A table of only losses is as
wrong as a table of only wins.

## Context

Repo `/Users/user/Dev/yaml-adapter-engine`, branch `dev` at `dd25c0d`, tree clean. 183 `.cs` files in
`src/`, 735 tests passing.

**Baseline, measured — treat as fact, do not re-derive.** The pre-extraction engine is 65 authored `.cs`
files, 11,364 lines, in a pre-concept layout (`Models/` 13, `Auth/` 11, `Pagination/` 10, `Mapping/` 8,
`Workflow/` 5, `Validation/` 4, `Retry/` 4, `Templating/` 2, `Loader/` 2, 6 root files). It exists twice:

- `/Users/user/Dev/cymulate-integration-adapters/src/.../YamlCollector/Cymulate.Integration.Yaml.Engine`
- `5e5ca52` in this repo — the verbatim phase-1 move

**64 of 65 files are byte-identical between them.** Use `git show 5e5ca52:<path>`; it is scriptable and
carries its own `.slnx`, `csproj` and props, so it **builds in an isolated worktree**. The one divergent
file, `Auth/OAuth2ClientCredentialsAuthenticator.cs`, is a finding — see below.

**Why this task exists.** Extraction, sink-inversion, rule-compliance and the SRP decomposition each
verified themselves against their immediate predecessor. None compared against the original. This repo
already has one confirmed compounding drift: **D18** — removing the NDJSON spill orphaned the
streaming-ingest fast path and took `tenable.io` chunk collection from O(1) to O(chunk) memory. It was
reported as a clean win, found late by reading the original, and is still open.

**Seed finding, to confirm rather than assume.** The vendored OAuth authenticator has two capabilities
HEAD lacks: `(placement?.Prefix ?? "Bearer").TrimEnd()`, and `ExtraFields` resolution in the order
`static_value` → `credential_key` → credentials-by-name. HEAD implements only the third — while
`Authentication/Contracts/Models/ExtraFieldDefinition.cs` declares both fields and
`AwsSigV4Authenticator.cs:229-233` honours them. So the contract advertises two keys that one
authenticator honours and another silently ignores. Zero of 279 definitions use them.
**A2 must be resolved before this is called a production gap:** confirm whether the adapter actually
compiles the vendored tree. If not, it is a stale fork, not a gap.

**The real prize is not this finding, it is its shape:** a config key that parses, validates, and is
silently dropped on some path. Hunt for others.

**Read first:** `CLAUDE.md`, `ARCHITECTURE.md`, then `constraints.md`, `assumptions.md`, `decisions.md`
here. **Binding on how you work, not background:**
`../2026-07-28_1258_sink-inversion/retrospective.md` — its eleven checks, and the pattern it names,
*a claim made at a wider scope than what was verified.* Also read
`../2026-07-29_0837_srp-decomposition/open_bugs.md` and the four `review/` files there: they record ~40
mutations that survive the current suite, which is why a green suite proves nothing here.

**Corpus:** `/Users/user/Dev/cymulate-magic-integration/integrations`, 279 definitions. Parse with PyYAML.
The operator designated five as most trusted for semantics and expectation: `tenable.io`, `qualys`,
`defender-vm`, `crowdstrike-falcon`, `insightvm-cloud`. Use those five to judge what matters most, and all
279 to judge what is live at all. Keep the two claims separate.

## Execution Steps

### I0 — Build the baseline. Do this first.

Create a throwaway worktree at `5e5ca52` and build it. Then decide, and record, whether a side-by-side
executable differential is possible (**A4**). Everything downstream depends on the answer:

- **If it builds:** a differential is the standard. Precedent to match — the loader work ran all 279
  definitions through old and new code comparing exception type, *exact* message text and a JSON
  fingerprint: 1,198 outcome lines, zero differences.
- **If it does not build:** say so with the error, fall back to inspection, and **mark every affected row
  as inspection-only** rather than presenting weaker evidence as equivalent.

Remove the worktree when done.

### I1 — Config surface

Enumerate every YAML key the original honoured, from its model declarations rather than by reading prose
(**D2**). For each: does HEAD parse it, and does HEAD *act* on it identically? The failure shape to hunt
is parse-but-ignore. Report the enumeration used and its count, so coverage is measurable.

### I2 — Guards and defensive checks

Enumerate the original's guards: null/empty checks on nullable members, zero-record guards, bounds
checks, already-processed checks, early returns. For each, name what performs that role now — `CLAUDE.md`'s
rule for deletions. The sink-inversion retrospective records two guards deleted under a green suite; those
are the class to expect.

### I3 — Error paths and exact message text

Users hand-author YAML and read rejections, so message text is behaviour. Compare by execution if I0
allows. Watch for messages that still exist but now fire on a different condition, or interpolate
different values — inspection alone cannot see either.

### I4 — Failure behaviour

Retry/defer/externalise decisions, the in-process cap, cursor recovery and re-anchoring, and
**checkpoint field names** — a renamed key silently breaks resume for runs already in flight, so treat the
checkpoint as a serialization contract and diff it key by key.

### I5 — Performance characteristics

Look for shape changes: buffering where there was streaming, materialisation where there was laziness,
O(n) where there was O(1). Report shapes, not magnitudes, unless you measure (**D8**). D18 and
`tenable.io`'s `chunk_size: 1000` are the concrete precedent.

### I6 — Host contract

What does the adapter actually read off `OperationResult` and the other return types? Grep the read-only
adapter before asserting any field matters. The largest single waste on the sink-inversion branch was four
revisions of two fields the host never reads — `TotalRecords` and `Records`. Do not repeat it in reverse by
flagging drift in a field nobody consumes.

### I7 — Improvements

Explicitly requested, so give it real effort rather than a token paragraph: capabilities added, defects
fixed, dependency cycles removed, coverage gained, guards that are now stronger. Cite evidence to the same
standard as the losses.

### I8 — Synthesize

Write `capability_diff_table.md`, then the summary.

## Constraints

Full list in `constraints.md`. The ones that will bite:

- **Read-only.** No `src/` change, no test change, no fix — not even a one-character one (**D9**).
- Both external repos untouched; `git status --porcelain` empty in both at completion.
- Verdicts from a fixed vocabulary: **PRESERVED · IMPROVED · WEAKENED · LOST · CHANGED-DELIBERATELY**.
  The last requires a citation to a commit, CHANGELOG entry or defect-register entry (**D5**).
- Name the search behind every claim. Never write "unreachable", "no caller", "dropped" or "unused"
  without it.
- Never write a number you did not measure.
- State what each side of a comparison measures. A file line count is not a type line span; a declared
  contract field is not an honoured code path.
- **A green suite is not evidence** — ~40 known mutations survive the current 735 tests.
- Never edit `.gitignore`. Never push to `master`.

## Success Criteria

Each names what falsifies it.

1. **`capability_diff_table.md` exists** with the required columns: capability · original (file:line) ·
   HEAD (file:line) · verdict · evidence · evidence strength · live in the 279 · matters to the 5 ·
   severity. Falsified by a missing column or a row with an empty evidence cell.
2. **All seven dimensions covered, both directions each.** Falsified by a dimension reporting only losses
   or only gains without stating that the other direction was checked and empty (**D10**).
3. **Coverage is measurable, not asserted.** Each dimension states the enumeration it worked from and its
   count. Falsified by a dimension whose completeness rests on "I read the file".
4. **A4 resolved explicitly** — differential built, or not, with the error. Falsified by rows whose
   evidence strength is unstated.
5. **A2 resolved** — whether the adapter compiles the vendored tree. Falsified by reporting the OAuth
   finding as a production gap without that check.
6. **Every `CHANGED-DELIBERATELY` carries a citation.** Falsified by one that does not.
7. **Every liveness claim names its corpus command.** Falsified by "no definition uses this" without one.
8. **A1, A2, A3, A4, A5, A6, A7 each VALIDATED or REJECTED** with evidence. A1 and A8 are already
   VALIDATED. Falsified by any left OPEN at completion.
9. **Host-contract claims verified against the adapter**, not inferred from the engine. Falsified by an
   asserted consumer that a grep does not confirm.
10. **No repository modified except this task folder.** Falsified by `git status` anywhere.
11. **Artifacts current at completion** — `execution_notes.md`, `defect_register.md`, `state.json`. Not
    "committed": `ai/active/` is gitignored.
12. **The summary is readable in under a minute and leads with the verdict.** Falsified by a wall of text.

## Execution Rules

- Do not assume missing data. Read the code; do not infer it.
- Before asserting what the original did: open it. `git show 5e5ca52:<path>`.
- Before claiming a capability is unused: run the corpus command and quote it.
- Report both directions honestly. If the refactors came out well, say so plainly — an audit that
  manufactures findings to look thorough is as useless as one that misses them.
- Surface findings in `defect_register.md` as they surface.
- Report state, not narrative.

## Output Format

- `capability_diff_table.md` — the primary artifact.
- `execution_notes.md` — per-dimension outcome, the enumeration used, deviations, surprises.
- `defect_register.md` — findings with status and severity.
- `state.json` — step statuses current at completion.
- Final summary: verdict first, two-directional, under a minute to read. Numbers and file names.

## Stop Conditions

- **A4 rejected** — the baseline will not build. Do not stop the task; downgrade the evidence standard,
  say so, and continue.
- **A2 unresolvable** — cannot determine what the adapter compiles. Report the OAuth finding as
  unadjudicated rather than as a production gap.
- **A3 rejected** — definitions exist outside the 279. Every liveness claim must then be qualified.
- A finding appears that is actively dangerous in production. Stop, report immediately, do not fix.
- The inventory would require modifying any repository.
