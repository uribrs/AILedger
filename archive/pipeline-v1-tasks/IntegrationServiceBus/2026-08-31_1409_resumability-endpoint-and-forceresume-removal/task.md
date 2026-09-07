# Task

Replace Admin's Mongo-inferred Resume-button heuristic with an authoritative resumability answer
from IntegrationServiceBus, and remove the `forceResume` marker from all three repositories.

One predicate, two callers at two times:

- Admin, at page render, asks "would a resume work for this correlationId?" — it drives whether the
  Resume button is drawn.
- ISB gate 4, at dispatch, asks "should this run resume or start fresh?"

Today these are answered by structurally unrelated code — a client-side predicate in a `.ejs` file
and a C# expression in `ExecuteWithResumeAsync` — and they disagree. This task makes them one
evaluator: Admin calls it over HTTP, ISB gate 4 calls it in-process.

Removing `forceResume` is safe only because the evaluator includes the collector's own
`CanResumeFrom` verdict. Presence alone would leave the operator invited into a refusal that
renews the retention clock.

Three repositories: IntegrationServiceBus, Admin, cymulate-integrations.
