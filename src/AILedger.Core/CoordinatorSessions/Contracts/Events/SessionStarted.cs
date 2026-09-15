namespace AILedger.Core.Contracts;

// The opening end of a coordinating session's bracket. All measurements are derived from the
// persisted session and the existing event stream rather than reported by the coordinator.
public sealed record SessionStarted(CoordinatorSession Session) : LedgerEventData;
