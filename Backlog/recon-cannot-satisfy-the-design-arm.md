recon cannot satisfy the design arm, so internal-only tasks fake a researcher run

The methodology has two ways of establishing ground before design, and they are deliberately
different things:

- **outward** — `technical-researcher`, which says so three times: its description is *research
  external technical targets*, line 11 repeats it, and line 13 is explicit — *treat repository
  context as supplementary only. Do not turn this skill into codebase analysis.*
- **inward** — the **internal recon pass**, owned by `task-orchestrator` (line 31, for code-bearing
  work; line 53: *map the internal ground the plan and the worker briefs are written from*).

Recon is required, and the requirement is checked — `task-orchestrator` puts the recon pass before the path
decision (line 31) and reads Coupling off its disjoint-set finding (line 101), and a verifier applying
that gate caught this very task for choosing `direct` before any recon existed. **The defect is
staging, not absence.** Nothing makes recon precede the path decision at command time, so it can be
satisfied late and still reach a green gate at verification. And the kernel can represent only the
first of the two mechanisms as a stage prerequisite. `StagePrerequisiteRules.EnsureDesign` requires a completed
run whose subject role is `Researcher`, and `GovernedArtifactKind` has no recon member — so a recon
pass can neither satisfy the arm nor even be filed against it.

## what that forces

A task whose orchestrator judged no external research necessary has two ways into Design, and both
are wrong:

1. dispatch a `technical-researcher` run at an internal target, which is what its own line 13
   forbids; or
2. take an operator waiver, every time, which is how a waiver stops being read.

## measured on 2026-09-20_0817-read-the-refusal-back

That task is entirely internal — a diagnostic in `ClaimRules`, a key in `CoordinatorMeasurement`, a
counter in `RefusalJournal`. It has no external-behaviour claim anywhere in its record.

It dispatched **three** researcher runs. `R1`, `R2` and `R3`, all `technical-researcher`, all
pointed at the kernel's own source, because the arm admitted nothing else. `R3` produced `R3C1`–`R3C6`
and the verifier relied on them, so the work was good — the skill was simply being used against its
stated scope, three times, to get past a gate.

The task raised this as escalation `X1` on the day it opened and carried it unresolved to closeout.

## the shape

Recon becomes representable, and the arm reads it:

- a `GovernedArtifactKind` for the recon output, filed by the run that produced it, like every other
  governed document;
- `EnsureDesign` accepts **either** a completed Researcher run **or** a current recon artifact, where
  no external-behaviour claim is open.

The second condition is what keeps the arm honest: a task that *does* have an open external claim
still owes an outward researcher run, and recon does not excuse it.

## what must not be done

Do not widen `technical-researcher` to admit internal targets. It is outward-directed on purpose, and
widening it would erase a deliberate boundary to work around a defect somewhere else. This was
proposed on `X1` and rejected by the operator for that reason.

Do not normalise the waiver either. `D14` records both rejections.

## related

Row 47, *a researcher and a worker cannot prove they ran* — same root, different face:
`GovernedArtifactKind` has no member for a research output or execution notes, so a brief that asks
either role for a filed document is unsatisfiable. Both rows want the same enum to grow, and doing
them together is cheaper than doing either alone.
