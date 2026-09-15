namespace AILedger.Core.Contracts;

public sealed record DecisionOverturned(DecisionId DecisionId, ChallengeId ChallengeId) : LedgerEventData;
