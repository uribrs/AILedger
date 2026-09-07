# Query Section Publisher Wiring Slice

Wire `QuerySectionPublisher` to consume `QuerySectionTracker.CompletedSections` and emit `QueryJobCompletedV2` at job end inside `QueryAdapterPipeline`.

Composition-only work. No new infrastructure dependency. No real Postgres, S3, or RabbitMQ. No real Query clients.

The slice closes the only remaining Query Shared composition gap between the SDK section tracker stage and the SDK section publisher stage. The tracker emits `SectionResultV2` exactly once per section and `CompletedSections` is single-consumer; the publisher publishes `SectionResultV2` and `QueryJobCompletedV2` through `IAdapterExecutionContext.PublishAsync`. The pipeline shell drives the boundary, including the per-job `QueryJobCompletedV2` envelope produced after the tracker stream completes.

Spillover (inline-vs-S3 for `FindingRefV2`), structured matcher filtering, a concrete `IExecutionPlanStore`, real or fake Query clients, recovery planner, and run/progress/done envelopes are out of scope.
