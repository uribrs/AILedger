# Query Distributor Slice

Implement the concrete Shared Query `IDistributor` stage behind the SDK contract.

The distributor must read execution-unit mappings from `ExecutionPlan`, fan each `RawResponse` out to the represented logical query ids and owning section ids, preserve the original raw response, and fail clearly when the plan cannot resolve a response to known units or sections.

