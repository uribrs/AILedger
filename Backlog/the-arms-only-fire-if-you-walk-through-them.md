a task can do all of its work at Discovery and meet no arm at all

`StageTransitionRules.EnsureStagePrerequisites` holds eleven refusals, one per stage, and they are
the methodology's phases turned into gates. They fire on a transition. A task that never transitions
never meets one.

Two tasks in this ledger have done exactly that:

    2026-09-07_1352-decompose-command-handler   333 events   23 runs   0 transitions
    2026-09-08_1428-run-cost                    302 events   19 runs   0 transitions

42 of the ledger's 187 runs — 22% — belong to tasks that have never left Discovery. Both are among
the three largest tasks by event count. `2026-09-08_1428-run-cost` is the one that shipped the run
cost fields; it is still at Discovery as this is written, and the coordinator who left it there is
the one writing this entry.

## this is a different hole from the waiver

    stage.transitioned            110
    stage.prerequisites-waived     17      15% of transitions

`waivers-need-a-floor.md` is about the recorded bypass: an operator says "skip this arm" and gives a
reason, and the reason can be the single character `x`. That entry stands, and 15% is the number it
is about.

This is the unrecorded bypass. Nothing was waived, because nothing was asked. The task simply stayed
where it was opened, and every arm it would have met — Research needs an open claim, Design needs a
completed researcher run, Scope needs a current `PromptContract`, Ready needs a plan and roles
staffed to distinct actors, Execution needs a work item and three governing artifacts — was never
evaluated. There is no event for it, so `status` reports nothing unusual and the log reads as
compliant.

## why it happens, which decides the fix

It is not evasion. Both tasks were coordinated interactively, work item by work item, and a stage
transition is a separate command that buys nothing at the moment you would run it: the work item
gate already refuses completion without a verifier run, so the arms feel like a second, slower copy
of a rule that already fired. The incentive points at not transitioning.

That is worth stating plainly because it rules out the obvious fix. Refusing `run start` outside
Execution would strand both of those tasks and every one like them, and an operator who cannot start
a run will stop using the kernel rather than walk eleven stages. `waivers-need-a-floor.md` already
names that failure mode: a bypass that is expensive gets replaced by not using the mechanism at all,
and `decompose-command-handler` is the proof — 333 events, 23 runs, zero arms, and no rule broken.

## the shape

Say it, do not refuse it. Three things, in order of cost:

- **`status` reports the stage a task is in and how long it has been there**, in events and in runs,
  not just the stage name. "discovery — 302 events, 19 runs, no transitions" is a sentence an
  operator reads and acts on. `owed-does-not-say-what-blocks.md` is the same shape: the gate knew,
  and the projection did not say.
- **the session-start check already does this and should keep doing it.** The hook that greets a
  session names a task that is "finished but never closed" and it is what surfaced this. It fires on
  archive-readiness only; the same check on a task carrying runs at Discovery is the cheapest
  version of this entry.
- **an arm on `work complete`, not on `run start`.** Completing a work item while the task sits at
  Discovery is the moment the mismatch becomes real: the item is done, and the stage says the task
  has not begun designing. A warning there, naming the transitions never made, costs one predicate
  and refuses nothing.

## what this does to the stage design

`docs/stage-engagement-design.md` closes the gap between the kernel and the eleven phases, and the
decisions behind it are LD11 to LD16 in `ledger-learning`. LD16 settled that the arms are
command-time only. That is right and this entry does not reopen it.

But the design assumes a task walks the stages. Two of this ledger's three largest tasks did not,
and the arms were not bypassed so much as never invoked. Any stage-driven behaviour — routing,
skill selection, a `single-agent-relaxation` keyed on stage, an `attention-items-as-a-gate` — reads
`discovery` for those tasks and would route 22% of this ledger's runs as though nothing had started.

That is the reason this is worth an entry rather than a note: the stage field is being treated as
task progress by work that has not been built yet, and it is not measuring that today.
