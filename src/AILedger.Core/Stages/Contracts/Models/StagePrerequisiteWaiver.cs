namespace AILedger.Core.Contracts;

internal sealed record StagePrerequisiteWaiver(EventId EventId, ActorId ActorId, TaskStage TargetStage);
