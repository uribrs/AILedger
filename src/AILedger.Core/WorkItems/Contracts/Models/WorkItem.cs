namespace AILedger.Core.Contracts;

public sealed record WorkItem(
    WorkItemId Id,
    string Title,
    ActorId? Owner,
    WorkItemStatus Status,
    IReadOnlyList<ClaimId> DependsOnClaims,
    IReadOnlyList<string> ResourceScope,
    string? BlockReason = null,
    string? AbandonReason = null,
    // Carried onto the item so the justification for holding several areas lands in the log. Kept
    // only on the command, the decision the kernel demanded would be checked and then thrown away.
    AlternativeId? NotSplitJustification = null,
    // The commit the item's work starts from, captured in the item's own scope directory rather than
    // the shell's, because a scope points into whichever repository holds the work and that is rarely
    // this one. Both the verifier and the code reviewer need it to scope a diff: the skills name it as
    // `state.json.baseRef`, and two verifier runs in a row fell back to comparing the change against
    // the goal and the constraints because the kernel had nowhere to put it. Null when the scope is
    // not a git work tree, which is the state every item recorded before this field existed is in.
    string? BaseRef = null);
// No field here for the door the context gate was opened through to add this item. The waiver is its
// own event immediately before this one and is the sole record of the justification; copying it here
// would be two records of one fact that can disagree. GovernedTaskState.ContextBriefWaivers is the
// projection that joins the two, and it is what `status` reads (GX1).
