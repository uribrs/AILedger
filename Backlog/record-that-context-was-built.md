record which manifest briefed a run

`BuildContext` is a capability in `GovernanceModels.cs:43`. it has no event. no task on disk carries
a single record of a manifest being built.

so the kernel knows which runs happened, which claims they wrote, and which artifacts they produced.
it does not know whether any of them read their brief.

## why it is worth recording at all

`self-scoring` asks nine dimensions of questions. several of them are unanswerable today, and this is
the cheapest missing input by a wide margin:

- were relevant prior lessons recalled? — partly answerable, `lesson.recalled` exists
- did the agent work from the task's settled decisions or re-decide them? — not answerable
- did a run that produced a defect skip its manifest? — not answerable
- does building context correlate with anything at all? — not answerable

a retrospective cannot score a behaviour that leaves no evidence. `CLAUDE.md` calls the manifest the
difference between working from what the task has settled and re-deciding it. that is a strong claim
about causation and there is currently no data behind it either way.

this is not urgent. it is worth doing early only because the cost is two nullable fields, and
because every task that runs without it is a task the retrospective will never be able to read.

## the shape I first proposed, and why it is wrong

the obvious move is a `context.built` event emitted by the existing command. it is wrong, and the
reason is worth keeping in the entry.

## what that would actually cost

`context build` is a pure read today. `CliApplication.cs:768` calls `CreateContextAsync`, reads
state, writes a JSON file, and touches the ledger not at all.

emitting an event makes it a mutation. it takes the task mutation lock, appends to the log, and
rewrites the four projections. that is a real change in character and it has two consequences worth
weighing before building this:

- **it can now fail and it can now contend.** a read that never blocked becomes one that waits behind
  an active run. `CLAUDE.md` tells every session to run `context build` as its first act, so this is
  the command most likely to be issued while an agent is mid-run.
- **it grows the log on a path that is currently free.** every session opening the repository would
  append an event to a task it may then not touch. against the 10,000-event cap, and against the
  quadratic append in `the-append-is-quadratic`, that is the wrong thing to make cheap.

## the shape

record the build that briefs a run, not every build.

`provider launch` already mutates — it starts a run. the manifest it hands the subject is the one
whose influence is worth measuring, because it is the only one attached to work that produced
events.

so put the hash on `run.started` rather than inventing a `context.built` event:

    run.started
      ... existing fields ... , manifestHash, manifestArtifactCount

a launched agent's brief is then recorded, at zero additional cost, on a write that was happening
anyway. an operator building a manifest to read it themselves stays a pure read and stays free.

that answers the question the retrospective actually asks — did the run that produced this work read
its brief — and it answers it without touching the cost of anything.

## what it must not become

not a gate. do **not** require a mutation to carry a recent `context.built`. that turns a free
observation into a ceremony, and it would punish exactly the short tasks that should stay cheap —
a one-line fix inside a governed task has no reason to build a manifest.

record it, do not require it. if the data later shows that runs without a manifest produce worse
outcomes, that is an argument the evidence can make on its own.

## cost

two nullable fields on an existing event, set at `provider launch`. no new event type, no new
command, no new write path, and no change to what `context build` costs.

the twin rule applies and is trivial: the fields are absent on every event already on disk, so a
replay rule keyed on them is safe by construction.
