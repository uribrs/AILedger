# Query Matcher Slice

Implement the concrete Shared Query `IMatcher` stage behind the SDK contract.

The matcher must accept an SDK `DistributedResponse`, the matching logical `QueryV2`, and the job id, and produce zero or more SDK `Finding` instances. Because the SDK does not yet expose structured expression, IP, or result-type filtering models, this slice is admission-only: each `DistributedResponse` becomes one `Finding` with `ResultType` taken from the matched `QueryV2`. Filtering is deferred until SDK exposes structured rules.

