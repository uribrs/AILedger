namespace AILedger.Core.Contracts;

public sealed record AddWorkItemCommand(
    ActorId ActorId,
    EventId? CausationId,
    string CorrelationId,
    WorkItemId WorkItemId,
    string Title,
    ActorId? Owner,
    IReadOnlyList<ClaimId> DependsOnClaims,
    IReadOnlyList<string> ResourceScope,
    // The recorded alternative saying why more than one area was kept in a single work item.
    // Required only when the item claims more than one; a one-area item needs no defence.
    AlternativeId? NotSplitJustification = null,
    // The commit this item's work starts from. Captured by the CLI in the item's first scope
    // directory; null when that is not a git work tree.
    string? BaseRef = null,
    // What the cognitive layer would serve this actor right now, read by the caller because the
    // kernel has no filesystem. The gate needs it to tell a current brief from a stale one; null
    // means the caller could not read the layer, which is a refusal rather than a pass — a brief
    // that cannot be checked is not a current brief (VC1). Commands are not persisted, so this
    // reaches no event and no replay.
    IReadOnlyList<ContextSkill>? SkillsServedNow = null,
    // The operator door. An operator's decision that this command proceeds with no brief at all,
    // and the reason it was right. Operator-only, blank is refused, and it is recorded as its own
    // event — the shape and the spirit of --without-verification.
    string? WithoutBriefReason = null,
    // The conditional door. An evidence record on this task saying why proceeding on a brief that
    // is no longer current is correct. The kernel checks that the record exists and that a brief
    // exists to be stale, never whether the reason is a good one — the same contract as
    // NotSplitJustification above.
    EvidenceId? StaleBriefEvidenceId = null) : LedgerCommand(ActorId, CausationId, CorrelationId);
