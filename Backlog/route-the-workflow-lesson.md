a workflow lesson has no kind and no route back into a task

`self-scoring` separates two things correctly. a DomainLesson describes the repository, product,
api, architecture or environment. a WorkflowLesson describes how AILedger itself should plan,
route, brief, verify, escalate, recall or interact with the operator.

the store holds 129 lessons and every one of them is the first kind. there is no field that says
which, and no path by which the second kind reaches the next task.

## what the store actually holds

    class        refuted 81      untested 36     drifted 12
    sourceKind   imported 99     validatedClaim 28   rejectedClaim 2
    actor        verifier 111    executor 6      unset 12
    repo         AILedger 36                      cymulate-integration-adapters 33
                 IntegrationServiceBus 28         cymulate-integration-parsers 9
                 adapter-data-normalizer 10       AdapterDataNormalizer 10
                 yaml-adapter-engine 1            AgentService 1        CollectorBase 1

`LessonClass` is `Refuted` / `Untested` / `Drifted`. that is a confidence axis, not a subject axis,
and it is orthogonal to domain-versus-workflow: a workflow lesson can be refuted just as a domain
one can.

the 36 AILedger lessons are the interesting case. they are lessons about this repository's code —
`TaskTransitionValidator` duplicates command-time rules deliberately, `dotnet test` is blocked in
the sandbox. none of them is a lesson about how the kernel should have run the task, which is the
only kind `self-scoring` produces.

## why recall cannot carry one

recall is tag-filtered with a cap. `FileGovernedTaskService.cs:12`:

    TaggedRecalledLessonLimit = 10

a task opened with tags gets at most ten lessons whose tags intersect its own. a task opened with
no tags falls back to `RecalledArchivedTaskLimit = 3` — the lessons of the last three archived
tasks in this root.

put a workflow lesson into that store unchanged and one of two things happens. its tags describe a
task shape rather than a repository, so it intersects nothing and is never recalled. or it is
tagged broadly enough to match, and it competes for ten slots against the domain lessons that
actually describe the code being changed.

neither is acceptable, and the second is worse, because it degrades recall for every task in every
repository to serve a mechanism that only concerns this one.

the `repo` field shows the filter is not yet canonical on its own terms:
`adapter-data-normalizer` and `AdapterDataNormalizer` are the same repository under two spellings,
ten lessons each. a shape-filtered recall built on top of that inherits the problem.

## the feature is write-only without this

`self-scoring` is explicit that the retrospective blocks nothing — not commit, push, pr, merge or
task completion. correct. but the document never says what reads it.

by its own definition, a mechanism that "consistently contributes neither correction, evidence,
confidence, nor useful durable reasoning" is a ceremony candidate. a retrospective nothing consumes
qualifies, and it would be the most expensive ceremony in the kernel.

the numbers say the existing route is already thin: 23 `lesson.recalled` events across all tasks,
and 2 citations through `--from-lesson`. recall happens; influence is barely recorded. adding a
second lesson kind to a channel with that throughput needs the channel fixed, not widened.

## the shape

**a kind on the lesson.** `LessonKind` — `Domain` or `Workflow` — as a nullable trailing field on
`Lesson` and `LessonMark`. safe for replay by construction: every lesson written before it carries
none, and `did-the-lesson-matter` set the precedent for optional trailing fields on these records.
a null reads as `Domain`, which is what all 129 are.

**a separate recall budget.** workflow lessons must not consume the ten domain slots. a small fixed
number of workflow lessons, selected by task shape rather than by repo tag, added alongside.

**task shape has to exist first.** `self-scoring` uses the phrase throughout — "affected task
shapes", "the next similar task" — and the kernel has no such concept. the nearest thing is
`task.opened`'s optional `tags`, which is a free-text list an operator types. either shape is
derived from what the task turned out to be (code-bearing, single-provider, number of work items,
which stages it used) or it is declared at opening and will be wrong. derived is the only version
that can be computed for the four already-archived tasks, which is also the only way to test it.

## what should not be built

the lifecycle the document describes — recurrence count, supporting future tasks, refuting future
tasks, last observed, status `candidate` / `accepted` / `superseded` / `rejected` — is a record that
is edited over time.

every record in this kernel is append-only and belongs to exactly one task root. the single
exception, `FileLessonStore`, is a rebuilt index and not a mutable entity; `PublishAsync` is
documented as a no-op for a lesson it already holds, precisely so republishing is safe.

a workflow lesson accumulating evidence across tasks would be the first genuinely mutable
cross-task entity in the system. that is an architecture decision and it does not need making:
derive it instead. recurrence is the count of retrospectives citing the same workflow lesson, which
is the shape `--from-lesson` already implements. refutation is a retrospective that cites it and
disagrees. the counts then cannot drift from their evidence, because they are computed from it.

## cost

two nullable fields, one recall budget, and one derived shape. the store format does not change and
existing lessons keep recalling exactly as they do today.

the shape derivation is the real work and it is worth doing on its own merits — `single-agent-relaxation`
and `attention-items-as-a-gate` both want to key behaviour on what kind of task this is, and both
currently have nothing to ask.
