using System.Text.Json;
using AILedger.Core.Assurance;
using AILedger.Core.Contracts;
using static AILedger.Core.Assurance.AssuranceValidation;

namespace AILedger.Storage;

// Configuration continuity in the existing coordination store, not an acceptance decision.
public sealed record GovernedAssuranceAnchor(int SchemaVersion, string CaseId, string StorePath,
    IReadOnlyList<string> WorkItemIds);

public sealed partial class FileGovernedTaskService
{
    public async Task BindAssuranceCaseAsync(TaskId task, AssurancePolicy policy, string store, CancellationToken token)
    {
        var directory = _pathResolver.Resolve(task);
        await using var gate = await _mutationLock.AcquireAsync(Path.Combine(directory, _layout.LockFileName), token).ConfigureAwait(false);
        await CheckCoordinationAsync(directory, token).ConfigureAwait(false);
        var current = await ReadCoordinationAsync(directory, token).ConfigureAwait(false);
        var anchor = new GovernedAssuranceAnchor(1, policy.CaseId, Path.GetFullPath(store), policy.Governed!.WorkItemIds.Order(StringComparer.Ordinal).ToArray());
        if (current?.AssuranceAnchor is { } existing)
        {
            Require(Hash(existing) == Hash(anchor), "assurance_case_changed", "This governed profile is pinned to its original case, store and members. A new case cannot discard findings or reset the check budget; continuation is unresolved.");
            return;
        }
        current ??= new(1, 1, 1, "assurance-configuration", DateTimeOffset.MinValue, null);
        await WriteCoordinationAsync(directory, current with { Revision = checked(current.Revision + 1), AssuranceAnchor = anchor }, token).ConfigureAwait(false);
    }

    // Caller holds the existing task mutation lock (assurance or completion admission).
    public static async Task CheckAssuranceCaseAsync(string ledgerRoot, TaskId task, AssurancePolicy policy, string store, CancellationToken token)
    {
        Require(policy.Governed is not null, "wrong_governed_binding", "Completion needs a governed policy association.");
        var directory = new TaskWorkspacePathResolver(ledgerRoot).Resolve(task);
        var path = Path.Combine(directory, CoordinationFile);
        Require(File.Exists(path) && new FileInfo(path).Length <= 5 * 1024 * 1024 && new FileInfo(path).LinkTarget is null,
            "missing_governed_host", "A governed assurance case must be bound by trusted setup before use.");
        var current = JsonSerializer.Deserialize<CoordinationSnapshot>(await File.ReadAllTextAsync(path, token).ConfigureAwait(false), LedgerJson.CreateOptions());
        var expected = new GovernedAssuranceAnchor(1, policy.CaseId, Path.GetFullPath(store), policy.Governed!.WorkItemIds.Order(StringComparer.Ordinal).ToArray());
        Require(current?.AssuranceAnchor is { SchemaVersion: 1 } anchor && Hash(anchor) == Hash(expected), "assurance_case_changed",
            "Original governed case, store and work members must remain available; changing configuration cannot erase obligations.");
    }
}
