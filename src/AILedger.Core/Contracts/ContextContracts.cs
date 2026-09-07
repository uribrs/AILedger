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
    VerifierOutput,
    // Appended: the enum's declaration order is the manifest sort order.
    Escalation,
    Alternative,
    Lesson,
    LessonMark,
    // The fifth governed workflow kind. It arrives last rather than beside VerifierOutput because
    // the declaration order is the sort order, and reordering would move every other kind in every
    // manifest a stored task has already produced.
    CodeReviewOutput
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
