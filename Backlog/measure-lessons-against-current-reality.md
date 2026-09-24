# Measure lessons against current reality

AILedger has minted hundreds of lessons during its existence. Some may have become stale or
outdated, and some may not have been sufficiently supported by their original sources. The broader
objective is to assess those lessons individually against both their sources and current reality.

Before undertaking that audit, we need to understand the memory lifecycle. It is not yet clear how
lessons get served, whether the store is consulted again after memories are initially served, or
whether useful knowledge is being underused. We also need to understand the value of retaining older
lessons as a history of how understanding changed, even when they no longer belong in current guidance.

## Prerequisite: establish the memory lifecycle

Investigate and substantiate:

- When the lesson store is consulted: task creation, context builds, agent launches, ongoing work,
  or other points. Distinguish supported access paths from observed use.
- How lessons are selected and presented: matching, ordering, limits, role filtering, and the effects
  of correction, refutation, and supersession on what an agent receives.
- What happens after initial serving: whether agents can consult the store again, whether they
  actually do, and whether new or corrected lessons can reach work already in progress.
- What historical retention provides: whether older lessons preserve the evidence and transitions
  needed to understand how a conclusion changed, and how that history remains accessible.
- What the record can establish about lessons being served, consulted, challenged, or used. Identify
  observability gaps explicitly; a lesson appearing in a manifest does not by itself prove it was used.

Stale guidance reaching agents, useful lessons remaining undiscovered, and historical lessons being
retained appropriately are distinct possibilities to investigate. None is an established finding.

## Assess lessons in light of that lifecycle

Use the lifecycle findings to agree on the scope and method of the individual lesson audit. For each
lesson examined, trace its original claim, evidence, decision, or incident; assess whether that source
justified the lesson; and check whether its conclusion still applies to the current system. A stored
verification command may contribute evidence, but its result must actually test the lesson's conclusion.

Distinguish a lesson documenting why an assumption was refuted from a lesson whose own conclusion has
since been refuted. Also distinguish historical accuracy from present applicability. An older lesson
may remain valuable as history while requiring correction or exclusion from current guidance.

Record evidence-backed assessments, including unresolved cases, and use them to recommend any changes
to lessons, recall behavior, access, or retention. Age alone does not justify removal, and this work
item does not prescribe deletion, a retention policy, or a mechanism change in advance of the findings.

## Expected outcomes and engagement boundary

- An evidence-backed account of the memory lifecycle, including observed usage and explicit unknowns.
- An agreed audit scope and method informed by that account.
- Individual lesson assessments and justified recommendations, preserving the history needed to
  explain transitions in understanding.

This expands the work beyond checking individual lessons. Updating this backlog item does not begin
the investigation. Check back with the user and decide together where to start before engaging.

## Narrowed first step: purposeful lesson refresh

Task `2026-09-24_1133-purposeful-lesson-refresh` takes one part of this item. It does not replace it.

Covered:

- `ailedger lesson consult` retrieves store lessons during an ongoing task, for three purposes:
  recon, research and strategy reconsideration. Each consultation is a `lesson.consulted` event with
  its run, question, tags, named claims and claim-set hash. Every served lesson joins the task and can
  be cited with `--from-lesson`.
- The kernel refuses to move past each point without a consultation of the current episode:
  InternalRecon filing, external claims at the Design gate, and Design-to-Scope after a return from a
  stage after Design into Design or Research (Scope-to-Design, Execution-to-Design,
  Execution-to-Research).
- A reconsideration re-serves the lessons already in the task that the consulting role may see.

Left out, and still open here: pruning, retention redesign, the individual lesson audit, memory-index
activation, and correction-lifecycle cleanup.

Remaining limits:

- The kernel does not check relevance. It checks that a consultation is bound to the claim set, the
  named claims and the episode, not that its question and tags describe them.
- Selection is any-tag overlap. A lesson tagged outside the consulting tags is not served.
- One reconsideration consultation covers the whole replanning episode, including
  Design-to-Research-to-Design detours inside it. Later claim changes in that episode do not require
  another one.
- Re-scoping without Design is not covered. Execution-to-Scope-to-Ready-to-Execution never passes
  Design, so no reconsideration consultation is required on that path. Whether a plan revision can
  be filed on that path was not examined; if it can, a strategy change there is not caught.
- Research findings are covered only when the Researcher names its new claims in a second
  consultation. Otherwise the Design gate refuses them and a new Researcher run must consult.
- A consultation shows that a lesson was served, not that it was used. Use is visible only where a
  record cites it with `--from-lesson`.
