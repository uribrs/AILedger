namespace AILedger.Core.Contracts;

public readonly record struct ClaimId(string Value)
{
    public override string ToString() => Value;
}

public enum ClaimStatus
{
    Open,
    Validated,
    Rejected,
    Superseded
}

// Superseding is not one act. A refinement sharpens a claim and its dependents stay
// valid; a correction narrows or contradicts it and they do not. The kernel derives
// which from state, never from a flag set by the actor doing the superseding.
public enum SupersessionOutcome
{
    Correction,
    Refinement
}

public sealed record Claim(
    ClaimId Id,
    string Statement,
    ClaimStatus Status,
    IReadOnlyList<EvidenceId> EvidenceIds,
    string? ConsequenceIfWrong,
    Provenance Provenance,
    ClaimId? SupersededByClaimId = null,
    // The lesson that prompted this record, when one did. Optional on purpose: the honest answer is
    // usually that no lesson caused it, and a required field would collect a plausible id rather
    // than a true one. An optional citation that is sometimes used is data; a mandatory one is a
    // field. Validated against the lessons this task recalled, never against the foreign store.
    LessonId? FromLesson = null);
