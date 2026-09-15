namespace AILedger.Core.Contracts;

// Which brief was assembled, for whom, and what it carried. The actor is not repeated here: it is
// the envelope's ActorId on the same line of events.jsonl, and two copies of one fact are two
// things that can disagree. Skills are ordered as served, and each carries the hash of the content
// that was served — an id alone would leave an event that proves nothing after the skill is edited.
public sealed record ContextBuilt(
    RoleKind Role,
    WorkItemId? WorkItemId,
    IReadOnlyList<ContextSkill> Skills) : LedgerEventData;
