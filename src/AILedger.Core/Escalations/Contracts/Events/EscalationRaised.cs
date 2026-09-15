namespace AILedger.Core.Contracts;

public sealed record EscalationRaised(Escalation Escalation) : LedgerEventData;
