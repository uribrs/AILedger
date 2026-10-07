using AILedger.Core.Authority;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
namespace AILedger.Storage;

public sealed partial class FileGovernedTaskService : IDurableHostRequests
{
    private async Task<IReadOnlyList<LedgerEvent>> FindHostRequestAsync(string directory, HostRequestIdentity identity, CancellationToken token)
    {
        if (identity.SchemaVersion != 1 || string.IsNullOrWhiteSpace(identity.Binding) || string.IsNullOrWhiteSpace(identity.RequestId) ||
            identity.Body.Length > 256 * 1024) throw new GovernanceException("Invalid host request identity.");
        var found = new List<LedgerEvent>();
        var path = Path.Combine(directory, _layout.EventsFileName);
        if (!File.Exists(path)) return found;
        await foreach (var row in ReadEventsAsync(path, token).ConfigureAwait(false))
        {
            if (row.HostRequest is not { } receipt || receipt.Binding != identity.Binding || receipt.RequestId != identity.RequestId) continue;
            if (receipt != identity) throw new GovernanceException("Request key already belongs to different content or schema.");
            found.Add(row);
        }
        return found;
    }
}
