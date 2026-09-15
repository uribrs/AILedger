namespace AILedger.Core.Contracts;

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
