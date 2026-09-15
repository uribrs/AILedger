namespace AILedger.Core.Contracts;

// The closing end of a coordinating session's bracket. Histories recorded before sessions existed
// carry neither event; no boundary is inferred for them.
public sealed record SessionCompleted(CoordinatorSessionId SessionId, DateTimeOffset EndedAt) : LedgerEventData;
