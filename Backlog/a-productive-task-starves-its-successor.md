the more a task learns, the less the next one is told

recall selects by tag intersection, then orders by recency and takes ten.
`FileGovernedTaskService.cs:12,229-241`:

    TaggedRecalledLessonLimit = 10

    .Where(tag intersects)
    .OrderByDescending(lesson => lesson.Provenance.RecordedAt)
    .Take(TaggedRecalledLessonLimit)

no relevance weighting, no diversity across source tasks. recency alone decides.

so a task that mints ten lessons consumes its successor's entire budget.

## measured, twenty minutes after it happened

`2026-09-08_1048-refusal-journal` archived at 14:18 and minted ten lessons. `2026-09-08_1428-run-cost`
opened at 14:28 with tags `kernel`, `observability`, `persisted-state`, `replay`.

    lessons matching those tags   38
    recall cap                    10
    slots filled by the task that archived ten minutes earlier   10 of 10

the four closest matches in the whole store to the new task's work were all crowded out:

- *adding a field to an existing record in this kernel is contained to the command handler, the
  reducer and the state* — refuted. do not size a change by the aggregate it touches; count the
  exhaustive switches.
- *provider launch records run.started before the manifest exists, so run.started cannot carry the
  manifest hash* — refuted. do not put a field on run.started that the launcher learns later.
- *hoisting the manifest build above run.start changes the manifest's content* — refuted.
- *a rule added to the replay validator retroactively invalidates history that was legal when
  written* — refuted.

the new task adds fields learned at completion to an event-sourced record and touches the replay
validator. those four answer its design questions directly and it was handed none of them. they had
to be copied into a constraint by hand.

## why this is the wrong shape and not just a small cap

the store's value to the next task **falls as the system gets better at producing lessons**. a task
that mints one lesson leaves nine slots of history; a task that mints twelve leaves none and pushes
two of its own out as well.

that inverts the premise. `did-the-lesson-matter` was built so a lesson that never gets cited can be
demoted out of default recall — but a lesson cannot be cited if it is never delivered, so the
citation signal is measuring delivery order rather than usefulness.

raising the cap does not fix it. thirty-eight matches at a cap of twenty still starves a task after
two productive predecessors, and the manifest is what every launched agent pays for in tokens.

## the shape

two changes, both small, and the second matters more.

**a diversity cap per source task.** at most two or three lessons from any one task in a single
recall. ten slots then reach at least four source tasks instead of one.

**order by something other than the clock.** the honest candidates, cheapest first:

- tag overlap count. a lesson matching three of four requested tags outranks one matching one.
- citation history. `--from-lesson` already records which lessons changed a claim, a decision or an
  alternative, so a lesson that has paid before can outrank one that never has.
- class. a `refuted` lesson is a recorded wrong belief and is the most dangerous thing to omit.

tag overlap is one `OrderByDescending` and needs no new data. citation history needs the aggregation
that `self-scoring` wants anyway.

## what this does not change

recency is still a tiebreaker and should stay one — a lesson about a system that has since moved is
worth less than a fresh one about the same subject. `drifted` exists for exactly that.

## cost

one `GroupBy` for the diversity cap and one ordering change, both inside `SelectRecalled`. no event,
no state, no replay counterpart — recall is a read performed at task opening.

it belongs with `route-the-workflow-lesson`, which already has to touch this method to give workflow
lessons their own budget. doing both at once is one change to one function.
