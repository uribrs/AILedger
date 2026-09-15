namespace AILedger.Core.Contracts;

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
    EvidenceId? StaleBriefEvidenceId,
    WaiverProvenance? Provenance = null)
{
    public const string WorkItemKind = "work item";
    public const string ProviderLaunchKind = "provider launch";
}
