namespace AILedger.Core.Contracts;

public sealed record Challenge(
    ChallengeId Id,
    string TargetType,
    string TargetId,
    string Reason,
    ChallengeStatus Status,
    IReadOnlyList<EvidenceId> EvidenceIds,
    Provenance Provenance);
