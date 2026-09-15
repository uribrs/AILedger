namespace AILedger.Core.Contracts;

public readonly record struct DecisionId(string Value)
{
    public override string ToString() => Value;
}

public enum DecisionStatus
{
    Proposed,
    Accepted,
    Superseded,
    Invalidated
}

public sealed record Decision(
    DecisionId Id,
    string Statement,
    DecisionStatus Status,
    string Rationale,
    IReadOnlyList<ClaimId> DependsOnClaims,
    DecisionId? Supersedes,
    Provenance Provenance,
    // The lesson that prompted this record, when one did. Optional on purpose: the honest answer is
    // usually that no lesson caused it, and a required field would collect a plausible id rather
    // than a true one. Validated against lessons recalled by this task, never the foreign store.
    LessonId? FromLesson = null);
