namespace AILedger.Core.Contracts;

public interface IContextAssembler
{
    ContextManifest Build(
        GovernedTaskState state,
        ActorId actorId,
        WorkItemId? workItemId,
        IReadOnlyList<ContextArtifact> availableArtifacts,
        DateTimeOffset assembledAt,
        IReadOnlyList<WorkItemId>? coveredWorkItemIds = null,
        AssuranceBinding? assurance = null);

    ContextManifest BuildForRun(
        GovernedTaskState state,
        ActorId actorId,
        RunId runId,
        IReadOnlyList<ContextArtifact> availableArtifacts,
        DateTimeOffset assembledAt);

    // The skills this role would be served now, in manifest order. The gate needs them without
    // building a whole manifest, and deriving them from the same filter and the same ordering is
    // what keeps a freshness check from refusing a brief that is in fact current.
    IReadOnlyList<ContextSkill> SkillsServed(
        RoleKind role,
        IReadOnlyList<ContextArtifact> availableArtifacts);
}
