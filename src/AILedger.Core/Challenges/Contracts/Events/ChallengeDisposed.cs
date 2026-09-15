namespace AILedger.Core.Contracts;

public sealed record ChallengeDisposed(
    ChallengeId ChallengeId,
    ChallengeStatus Status) : LedgerEventData;
