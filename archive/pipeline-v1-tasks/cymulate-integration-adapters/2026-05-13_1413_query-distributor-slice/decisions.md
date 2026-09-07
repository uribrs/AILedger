- Implement distributor in Dataflow as the concrete stage consumed by `QueryAdapterPipeline`.
- Build an in-memory unit index once per `DistributeAsync` call for O(1) response lookup.
- Build query-to-section mappings from `SectionMembership` because SDK `DistributedResponse` includes section id.
- Preserve streaming semantics by yielding distributed responses directly from the input response stream.
- Leave deduplication by `RawResponse.ResponseId`, matching policy, section accumulation, and recovery persistence to later stages.

