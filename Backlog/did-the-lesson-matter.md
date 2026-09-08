make it possible to tell whether a recalled lesson changed anything

the store holds 113 lessons. `2026-09-07_1352-decompose-command-handler` recalled six of them. what
those six did to the work is not recorded anywhere.

`self-scoring` asks it directly — were relevant prior lessons recalled, did recalled lessons
materially affect current work, did any stale or misleading lesson reduce quality. the first is
answerable today. the second and third are not.

## why this is not academic

the store is 65 `refuted`, 36 `untested`, 12 `drifted`. a `refuted` lesson is a recorded wrong
belief, kept deliberately so it is not believed again. that is the right thing to keep and it is
also the most dangerous thing to hand an agent with no signal about what to do with it.

recall is currently unconditional and untracked, so a lesson that actively misled a run looks
identical in the record to one that saved a day of work.

## the shape

one optional reference, on the records that already exist:

    ailedger claim add       --id C41 ... --from-lesson L-b345971b
    ailedger decision propose --id D9 ... --from-lesson L-b345971b
    ailedger alternative record --id ALT8 ... --from-lesson L-b345971b

that is the whole mechanism. an agent that recalls a lesson and then writes a claim because of it
says so, in the command it was already going to run.

what it buys, once a few tasks carry it:

- **cited and validated** — the lesson paid. its confidence should rise.
- **cited and rejected** — the lesson was wrong here. that is a refutation of the lesson, not just of
  the claim, and it is exactly the evidence `self-scoring`'s WorkflowLesson lifecycle wants for
  weakening a belief.
- **recalled, never cited, across many tasks** — the lesson is noise in the manifest. it is costing
  context and buying nothing, and it can be demoted out of default recall.

the third is the one that compounds. recall is the input every launched agent pays for in tokens,
and there is currently no mechanism that ever makes the recall set smaller.

## where the id is checked, which needs deciding

lessons live in the store at `<home>/lessons`, not in task state. so `--from-lesson L-b345971b`
cannot be validated the way `--supports C20` is — the kernel would be checking a foreign store, and
the twin rule makes that worse, because replay would have to check it again years later against a
store that has since changed.

the answer that keeps the log self-contained: validate against `lesson.recalled` events already in
the task. a lesson the task recalled is in its own state and can be checked at command time and at
replay with no external read. a citation of something never recalled is refused, which is also the
correct behaviour — an agent cannot have been influenced by a lesson it was never shown.

that makes recall a precondition for citation, which is a real constraint and should be stated
rather than discovered.

## why optional and never required

because the honest answer is often that no lesson caused the claim, and a required flag would get a
plausible id typed into it. an optional citation that is sometimes used is real data. a mandatory one
is a field.

the same reason nothing should refuse on this: a task that cites no lessons is not doing anything
wrong. it may simply have recalled nothing relevant.

## cost

one nullable field on three existing commands, carried through to their events, and a query that
joins citations back to the store. no gate, no new event type, no change to how recall works today.

the lesson-side aggregation — confidence rising and falling with citations — is the larger half and
belongs with `self-scoring`, not here. this entry is only about capturing the link at the moment it
is cheap to capture, so that the aggregation has something to read when it is built.
