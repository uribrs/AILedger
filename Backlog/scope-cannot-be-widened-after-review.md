# Scope cannot be widened, so a cross-project finding releases the item that found it

## What happened

Code reviewer `R12` read the retrospective projection on work item `W5` and filed one Major
finding, `RC1`: the refusal reader returns null only when the journal file is absent, so a journal
with one malformed row is reported as a *complete* measurement of fewer refusals than the task took.
A correct finding, and the third pass over that code — three verifier runs had not seen it.

The repair spans two projects. The reader is `ReadRefusalsAsync` in
`src/AILedger.Cli/CliApplication.cs`; the `notMeasured` list it must feed is in
`src/AILedger.Core/Application/TaskRetrospective.cs`. `W5` held `src/AILedger.Core` and `tests`.

`ResolveProviderGrants` (`src/AILedger.Cli/CliApplication.cs:1405-1415`) refuses the working
directory and every `--add-dir` that is not contained in one of the item's resolved scopes. The
`work` command group is `add`, `complete`, `block`, `unblock`, `abandon`. There is no `rescope`.

So the item that hosted the review could not host the repair. `W5` was abandoned and `W6` opened
holding `src/AILedger.Core`, `src/AILedger.Cli` and `tests`, with `ALT4` recording why the patch was
not split along the project boundary. Recorded in
`2026-09-09_1010-retrospective-projection` as claim `C10` with evidence `E11`.

## Why it is a defect and not just an inconvenience

The cost is not the two extra commands. It is what the release throws away.

`W5` carried a completed verifier run (`R11`) and a completed code-review run (`R12`). Both are
still in the log, but they are attached to an abandoned item, so `W6` starts with neither. The
kernel will require a fresh verifier run before a reviewer may touch `W6` at all, and a fresh
review before `work complete`. **A reviewer's finding that crosses a project boundary therefore
costs a full work-item cycle to repair, and the deeper the review looked, the more likely it is to
have crossed one.** The incentive runs exactly the wrong way.

It also makes the record read wrong. Nothing in the log distinguishes an item abandoned because the
approach was a dead end — which is what `work abandon` was built for and what `ALT4`-style
alternatives describe — from an item abandoned because its scope was one directory too narrow. Both
appear as a released item with a reason string. A retrospective counting abandoned items as
dead ends will overcount them.

## The three candidate fixes, cheapest first

**Widen at repair time only.** A `work rescope --add-scope PATH --because ALT-ID` that appends an
area to a live item, refused if that area is occupied by another live item, with the same
`--not-split-because` requirement `work add` already imposes when an item claims more than one. The
event is `work.rescoped` and it is additive, so replay of older histories is untouched. This is the
smallest change and it does not weaken the occupancy guarantee: the area is still held by exactly
one live item.

The objection to it is real: an item that can grow has no fixed scope, and the whole point of
scope is that an agent knows what it may touch. The answer is that the *widening* is an operator
act recorded with a justification, not something a worker can do to itself — the same shape as
`--without-verification`.

**Let the repair inherit.** A `work add --repairs W5` that carries `W5`'s completed verifier and
review runs forward to the new item, so the repair does not re-earn what the review already
established. This does not solve the scope problem — the new item still needs the wider scope
from the start — but it removes most of the cost of getting it wrong, and it is the honest model of
what a repair is. It needs a rule about how many times a chain may inherit before the next verifier
is looking at code no run has ever verified.

**Scope by solution area, not by project.** Declare `--scope` at the level of the thing being
changed rather than the C# project. This is not a kernel change at all, it is a habit: an item
about a projection and the command that prints it should have claimed both from the beginning. It
costs nothing and it will fail again, because the point of a review is to find what the plan did
not anticipate.

## The unresolved question underneath

Occupancy exists so two agents cannot edit the same area. Scope-as-brief exists so an agent knows
what it may touch. These are two different jobs carried by one field, and this is the second entry
to arrive at that conclusion from a different direction — see `scope-cannot-follow-a-worktree`,
which found the same field failing at the repository boundary. Splitting them is a larger change
than either entry needs on its own, and it is probably the real fix for both.

## Acceptance criteria

- A live work item's areas can be widened by an operator, with a recorded reason, refused when the
  area is held by another live item.
- The widening is a new event type with no replay rule keyed on a field older events lack.
- `work abandon` distinguishes a released dead end from a released mis-scope, or the retrospective
  stops counting the two together.
- Both rule copies change together: `CommandHandler` and `TaskTransitionValidator`.
