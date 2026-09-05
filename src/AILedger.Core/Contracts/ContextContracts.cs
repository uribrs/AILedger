namespace AILedger.Core.Contracts;

public enum ContextArtifactKind
{
    Rules,
    Skill,
    TaskGoal,
    Constraint,
    Claim,
    Decision,
    Evidence,
    WorkItem,
    StopCondition,
    UserRequest,
    PromptContract,
    OrchestrationPlan,
    VerifierOutput
}

public sealed record ContextArtifact(
    ContextArtifactKind Kind,
    string Id,
    string Content,
    IReadOnlyList<string> RelatedIds);

public sealed record ContextManifest(
    int SchemaVersion,
    TaskId TaskId,
    ActorId ActorId,
    RoleKind Role,
    WorkItemId? WorkItemId,
    long TaskVersion,
    IReadOnlyList<Capability> Capabilities,
    IReadOnlyList<ContextArtifact> Artifacts,
    IReadOnlyList<string> StopConditions,
    DateTimeOffset AssembledAt);

public interface IContextAssembler
{
    ContextManifest Build(
        GovernedTaskState state,
        ActorId actorId,
        WorkItemId? workItemId,
        IReadOnlyList<ContextArtifact> availableArtifacts,
        DateTimeOffset assembledAt);
}
