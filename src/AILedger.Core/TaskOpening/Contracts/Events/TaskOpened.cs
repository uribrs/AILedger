namespace AILedger.Core.Contracts;

// Tags are optional and trail the original two fields: every task opened before they existed
// carries none, and replay must keep reading those histories.
public sealed record TaskOpened(
    string Title,
    string Goal,
    IReadOnlyList<string>? Tags = null) : LedgerEventData;
