- Implement dispatcher in Dataflow as the concrete stage consumed by `QueryAdapterPipeline`.
- Use a local dispatcher options record for default max concurrent units because SDK `DispatchAsync` does not receive job settings.
- Treat job-specific `MaxConcurrentUnits` as blocked by the current SDK boundary and document the gap instead of changing SDK contracts from this repo.
- Enforce adapter capabilities at dispatch start before scheduling any unit of the unsupported kind.
- Preserve streaming semantics by using bounded channels rather than collecting all `RawResponse` values.

