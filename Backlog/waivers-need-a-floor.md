a waiver reason should have to be worth reading

the ledger holds this event:

    {"eventType": "stage.prerequisites-waived", "targetStage": "research", "reason": "x"}

one character, and the kernel took it. `CLAUDE.md` says of the sibling flag on `work complete`: the
log records that decision permanently, and it is read as one. a reason of `x` is not read as
anything.

## how often the gate is bypassed

    stage.transitioned          29
    stage.prerequisites-waived  14

48% of every stage transition in this repository skipped its arm. thirteen of those carry one
legitimate reason — the three `ledger-*` tasks predate the arms and were walked forward to Archive
to close them out — so the honest number for ordinary use is smaller. it is not zero, and the one
outlier is the one with no reason at all.

## why the count matters more than the outlier

the arms in `StageTransitionRules.cs` are eleven refusals that encode the methodology. a waiver is
the single hole through all of them, it is operator-only, and its only constraint is
`RequireText` — non-empty after trimming. every other cheap path in this kernel is gated on
something the kernel can check. this one is gated on a string.

the risk is not abuse. it is erosion: the first `x` makes the second one normal, and the arms stop
being rules and become a prompt.

## the shape

three checks, in order of how much they buy:

- a minimum length. arbitrary, but it stops a single character, and it is one line.
- require the reason to name a record — a constraint, an alternative, or an escalation id that
  exists in the task. a waiver is a decision to proceed against the methodology, and this kernel
  already has a place to put a decision. `work add --not-split-because ALT10` is exactly this
  pattern and it already works.
- surface waivers in `status` and in `who`, counted per task, so a task that waived eight arms says
  so where it is read rather than only in the log.

the second is the real one. the first is a stopgap that can ship today.

## what it must not become

a waiver that is hard to record is a waiver that gets replaced by not transitioning stages at all,
which is worse — `2026-09-07_1352-decompose-command-handler` sits at Discovery with 333 events, 23
completed runs and seven work items, and never transitioned once. the arms cost nothing there
because the task never asked them anything.

so: make the reason worth reading, do not make the waiver expensive. one required id is the ceiling.

## cost

one predicate in `CommandHandler`. command-time only, per LD16 — replay must keep accepting the `x`
that is already on disk, which is the whole reason the twin is asymmetric.
