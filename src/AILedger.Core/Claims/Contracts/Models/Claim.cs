namespace AILedger.Core.Contracts;

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
