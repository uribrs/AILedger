# Query Unit Dispatcher Slice

Implement the concrete Shared Query `IUnitDispatcher` stage behind the SDK contract.

The dispatcher must execute planned time-window and native units against an `IQueryAdapter`, enforce declared query capabilities, apply a bounded per-run unit concurrency cap, and stream `RawResponse` values without materializing the whole job result.

