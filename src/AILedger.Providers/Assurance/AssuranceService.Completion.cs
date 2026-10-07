using AILedger.Core.Assurance;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Assurance.AssuranceValidation;

namespace AILedger.Providers.Assurance;

public sealed partial class AssuranceService
{
    private void ValidateGovernedAcceptance(AssuranceSnapshot snapshot, IReadOnlyList<AssuranceEntry> chosen)
    {
        var state = _governedLease.Value?.State ?? throw new AssuranceRefusal("missing_governed_host", "Acceptance needs a current governed host.");
        var basis = snapshot.Governed!;
        foreach (var version in basis.WorkVersions)
        {
            var pair = GovernedAssuranceRules.CompletionPair(state, version.WorkItemId, basis.CandidateId);
            Require(chosen.Any(e => Payload<AssuranceReport>(e).Role == "verification" &&
                e.Receipt.Principal == pair.Verifier.ActorId.Value && e.Receipt.SessionId == pair.Verifier.Id.Value &&
                e.Receipt.Provider == pair.Verifier.Provider && e.Receipt.RecordedAt >= pair.Verifier.StartedAt && e.Receipt.RecordedAt <= pair.Verifier.EndedAt),
                "wrong_governed_binding", "Independent verification must come from the actual paired governed verifier run.");
            Require(chosen.Where(e => Payload<AssuranceReport>(e).Role == "review").All(e =>
                e.Receipt.Principal != pair.Reviewer.ActorId.Value && e.Receipt.SessionId != pair.Reviewer.Id.Value),
                "incompatible_context", "Requirements-aware assurance and blind governed review require separate principals and contexts.");
        }
    }

    // Called only by the trusted completion adapter while storage holds the task mutation lock.
    // Retain the journal lease until work.completed is appended, so a new finding cannot race admission.
    public async Task<IWorkCompletionLease> AcquireCompletionAsync(GovernedTaskState state, WorkItemId work, CancellationToken token)
    {
        var gate = await _store.AcquireAsync(_id, token).ConfigureAwait(false);
        try
        {
            var association = new FixedGovernedLease(state);
            _governedLease.Value = association;
            var receipt = await ValidateCompletionAsync(state, work, token).ConfigureAwait(false);
            return new CompletionLease(receipt, gate, async ct =>
            {
                _governedLease.Value = association;
                var current = await ValidateCompletionAsync(state, work, ct).ConfigureAwait(false);
                Require(Hash(current) == Hash(receipt), "stale_assurance", "Completion applicability changed before commit.");
            });
        }
        catch { await gate.DisposeAsync().ConfigureAwait(false); throw; }
    }

    private async Task<GovernedAcceptanceReceipt> ValidateCompletionAsync(GovernedTaskState state, WorkItemId work, CancellationToken token)
    {
        var policy = await AuthorizeAsync(token).ConfigureAwait(false);
        Require(policy.Governed is { SchemaVersion: 1 } scope && scope.TaskId == state.TaskId.Value && scope.WorkItemIds.Contains(work.Value),
            "wrong_governed_binding", "Completion requires explicit task/work association in the trusted policy.");
        Require(state.Roles.TryGetValue(new(_session.Principal), out var authority) && authority.Role == RoleKind.Operator && authority.Capabilities.Contains(Capability.ManageWork),
            "authorization_denied", "Completion observation requires current accepting authority.");
        var records = await _store.ReadAsync(_id, token).ConfigureAwait(false);
        var entries = Entries(records);
        Require(!PendingChecks(records, entries).Any(), "incomplete_assurance", "An interrupted check remains unresolved.");
        var receipts = new List<AssuranceReceipt>();
        GovernedAssuranceBasis? basis = null;
        foreach (var area in policy.Areas.OrderBy(a => a.Id, StringComparer.Ordinal))
        {
            var snapshot = await CurrentAsync(policy, area.Id, null, token).ConfigureAwait(false);
            basis = snapshot.Governed!;
            var accepted = entries.LastOrDefault(e => e.Receipt.Operation == "accept_assurance" && Snapshot(e).AreaId == area.Id &&
                Reasons(e, snapshot, policy, entries).Count == 0);
            if (accepted is null && CurrentFindings(entries, snapshot, policy).Any(e => FindingIds(e).Count > 0))
                throw new AssuranceRefusal("unresolved_findings", "Findings require evidenced adjudication and explicit acceptance: " +
                    string.Join(", ", CurrentFindings(entries, snapshot, policy).SelectMany(FindingIds)));
            Require(accepted is not null, "missing_acceptance", "Applicable explicit acceptance is required for every declared area; receipt recovery is not fresh authority.");
            receipts.Add(accepted!.Receipt);
        }
        var pair = GovernedAssuranceRules.CompletionPair(state, work, basis!.CandidateId);
        return new(1, basis, pair.Verifier.Id, pair.Reviewer.Id, receipts);
    }

    private sealed class FixedGovernedLease(GovernedTaskState state) : IGovernedAssuranceLease
    {
        public GovernedTaskState State => state;
        public void EnsureCurrent() { } // Outer storage owns the task lock and live command authority.
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class CompletionLease(GovernedAcceptanceReceipt receipt, IAsyncDisposable gate,
        Func<CancellationToken, Task> revalidate) : IWorkCompletionLease
    {
        public GovernedAcceptanceReceipt Receipt => receipt;
        public Task RevalidateAsync(CancellationToken token) => revalidate(token);
        public ValueTask DisposeAsync() => gate.DisposeAsync();
    }
}
