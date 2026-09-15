namespace AILedger.Core.Contracts;

public enum WorkItemStatus
{
    Proposed,
    Active,
    Paused,
    Blocked,
    Stale,
    Completed,
    // Work is released as well as finished. A dead end that can never be completed used to hold its
    // directory area forever, so nothing else could claim it.
    Abandoned
}
