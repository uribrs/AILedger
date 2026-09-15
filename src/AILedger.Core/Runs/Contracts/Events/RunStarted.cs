namespace AILedger.Core.Contracts;

public sealed record RunStarted(AgentRun Run) : LedgerEventData;
