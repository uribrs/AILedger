namespace AILedger.Core.Contracts;

public sealed record DecisionInvalidated(DecisionId DecisionId, ClaimId RejectedClaimId) : LedgerEventData;
