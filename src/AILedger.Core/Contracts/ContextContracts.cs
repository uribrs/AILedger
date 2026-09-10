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

// One skill as it was served. The id alone would record that a brief carried a skill by that name
// and nothing about what it said, so a skill edited afterwards would leave an event that still
// looks satisfied — the same defect as a verify command that cannot fail. The hash is what makes
// the record falsifiable.
public sealed record ContextSkill(string SkillId, string ContentHash);

// What one actor was served, and when. Held per actor rather than per invocation: the gate asks
// whether this actor is briefed against the cognitive layer as it stands now, and a brief that a
// later one replaced answers a question nobody is asking.
public sealed record ContextBuild(
    ActorId ActorId,
    RoleKind Role,
    WorkItemId? WorkItemId,
    IReadOnlyList<ContextSkill> Skills,
    DateTimeOffset BuiltAt);

public interface IContextAssembler
{
    ContextManifest Build(
        GovernedTaskState state,
        ActorId actorId,
        WorkItemId? workItemId,
        IReadOnlyList<ContextArtifact> availableArtifacts,
        DateTimeOffset assembledAt);

    // The skills this role would be served now, in manifest order. The gate needs them without
    // building a whole manifest, and deriving them from the same filter and the same ordering is
    // what keeps a freshness check from refusing a brief that is in fact current.
    IReadOnlyList<ContextSkill> SkillsServed(
        RoleKind role,
        IReadOnlyList<ContextArtifact> availableArtifacts);
}
