# Query Plan Builder Slice

Implement the next Shared Query Integration slice by adding a minimal `IPlanBuilder` implementation under Shared Query infrastructure.

The slice must transform SDK `QueryJobV2` input into SDK `ExecutionPlan` output with validation and deterministic unit ids, without implementing optimizer, dispatcher, matcher, tracker, storage, topology, or client behavior.
