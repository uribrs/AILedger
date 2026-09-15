namespace AILedger.Core.Contracts;

public sealed record DecisionProposed(Decision Decision) : LedgerEventData;
public sealed record DecisionResolved(DecisionId DecisionId, DecisionStatus Status) : LedgerEventData;
public sealed record DecisionInvalidated(DecisionId DecisionId, ClaimId RejectedClaimId) : LedgerEventData;
public sealed record DecisionOverturned(DecisionId DecisionId, ChallengeId ChallengeId) : LedgerEventData;
