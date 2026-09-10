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

// What one actor was served. Held per actor and keyed on the skill set rather than per invocation:
// the gate asks whether this actor is briefed against the cognitive layer as it stands now, and a
// brief that a later one replaced answers a question nobody is asking.
//
// It carries no work item, because identity here is the actor and the skill set alone (MD2). A
// second build for a different work item with the same skills appends nothing, so a work item on
// this projection would name whichever one happened to be first and then go stale. The invocation's
// work item is on the context.built event, which records one invocation truthfully; this is the
// derived answer to a narrower question.
public sealed record ContextBuild(
    ActorId ActorId,
    RoleKind Role,
    IReadOnlyList<ContextSkill> Skills,
    // When this skill set was first recorded for this actor — not the time of the latest build.
    // A repeat build of the same skills appends no event, so nothing moves this forward; it moves
    // only when the skills change and a new brief replaces the row.
    DateTimeOffset BuiltAt);

// One door on the context gate, joined to what it carried through. The justification is recorded in
// exactly one place — the `context.brief-waived` event — and this is the projection that ties it to
// the work item or the run whose event names that waiver as its cause. Without it the join exists
// only in the log, and `status` reads state and never the log, so the question "which live work came
// through a door, opened by whom, and why" would be answerable only by hand (GX1).
public sealed record ContextBriefWaiver(
    // What the waiver carried through, taken from the event that consumed it rather than from the
    // waiver's own Action text, so the row names a thing the reader can go and look at.
    string Kind,
    string TargetId,
    // Who opened the door: the actor on the waiver event, which is the actor the gate was checked
    // against. On a dispatch that is the authorising operator, not the subject that did the work.
    ActorId Actor,
    // Exactly one of these is set, because an absent brief and a stale brief are different failures
    // and the gate refuses a command naming both.
    string? OperatorReason,
    EvidenceId? StaleBriefEvidenceId)
{
    public const string WorkItemKind = "work item";
    public const string ProviderLaunchKind = "provider launch";
}

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
