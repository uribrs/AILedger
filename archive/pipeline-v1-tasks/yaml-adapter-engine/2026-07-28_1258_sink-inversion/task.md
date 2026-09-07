# Task — Sink Inversion

Make `IExecutionSink` the single, non-optional record path through the engine.

## The problem

The engine carries two behaviours depending on whether a consumer was supplied.
`WorkflowRunner`'s private `ExecuteStageAsync` declares `IExecutionSink? sink = null`
and every caller takes the default, so workflow stages run sink-less: the engine
accumulates records in `OperationResult.Records` and the runner publishes them
afterwards. That fork is the last external signal in the record path, and it is what
keeps `if (sink is not null) … else …` alive inside the 741-line
`ExecuteOperationCoreAsync`.

## Target

One internal path: records go to a sink. Always. What the sink does with them —
publish to S3, hold them in memory, count them on the way past — is the caller's
decision, expressed by which sink it supplies.

## Scope

- `Sinks/Logic/` (the concept has `Contracts/` only today): `InMemorySink` and a
  `CountingSink` decorator.
- The sink-less `ExecuteOperationAsync` overload survives as sugar over an
  `InMemorySink`, so no host call site breaks.
- `WorkflowRunner` supplies a sink for every stage.
- Delete the `sink is null` fork and the in-memory accumulation branch.
- Split `PublishStageRecordsAsync` (publishes, counts and collects in one method).

## Out of scope

Clock/`TimeProvider` inversion; definition-source inversion; page-loop
decomposition. In that order, after this.
