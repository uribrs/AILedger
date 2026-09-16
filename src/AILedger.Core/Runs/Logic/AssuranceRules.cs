using AILedger.Core.Claims;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.WorkItems;

namespace AILedger.Core.Runs;

// Presence of the fresh envelope is the compatibility boundary. These checks never govern old events.
internal static class AssuranceRules
{
    internal static AssuranceBinding Copy(AssuranceBinding binding) => binding with
    {
        WorkItemIds = binding.WorkItemIds.OrderBy(id => id.Value, StringComparer.Ordinal).ToArray(),
        WorkVersions = binding.WorkVersions.OrderBy(row => row.WorkItemId.Value, StringComparer.Ordinal)
            .Select(row => row with { }).ToArray()
    };

    internal static IReadOnlyList<WorkItemId> ValidateShape(WorkItemId? anchor, AssuranceBinding binding)
    {
        var members = ValidateMembership(anchor, binding);
        ValidateVersions(binding, members);
        return members;
    }

    private static IReadOnlyList<WorkItemId> ValidateMembership(WorkItemId? anchor, AssuranceBinding binding)
    {
        var members = WorkCoverage.Effective(anchor, binding);
        if (binding.SchemaVersion != 1)
            throw new GovernanceException("Assurance coverage: unsupported schema version.");
        return members;
    }

    private static void ValidateVersions(AssuranceBinding binding, IReadOnlyList<WorkItemId> members)
    {
        if (binding.WorkVersions is null || binding.WorkVersions.Count != members.Count ||
            binding.WorkVersions.Any(row => row is null || string.IsNullOrWhiteSpace(row.WorkingRunId.Value)) ||
            !binding.WorkVersions.Select(row => row.WorkItemId).OrderBy(id => id.Value, StringComparer.Ordinal)
                .SequenceEqual(members))
            throw new GovernanceException("Assurance coverage: working versions must name every member exactly once.");
    }

    internal static bool Same(AssuranceBinding left, AssuranceBinding right, bool includePair = true) =>
        left.SchemaVersion == right.SchemaVersion && left.CandidateId == right.CandidateId &&
        (!includePair || left.VerifierRunId == right.VerifierRunId) &&
        left.WorkItemIds.OrderBy(id => id.Value, StringComparer.Ordinal)
            .SequenceEqual(right.WorkItemIds.OrderBy(id => id.Value, StringComparer.Ordinal)) &&
        left.WorkVersions.OrderBy(row => row.WorkItemId.Value, StringComparer.Ordinal)
            .SequenceEqual(right.WorkVersions.OrderBy(row => row.WorkItemId.Value, StringComparer.Ordinal));

    internal static bool IsReal(AgentRun run) => run.Status == AgentRunStatus.Completed &&
        !string.Equals(run.Provider, AgentRun.NoProvider, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(run.ProviderSessionId);

    internal static AgentRun LatestWork(GovernedTaskState state, WorkItemId member)
    {
        var runs = state.Runs.Values.Where(run => run.WorkItemId == member && IsReal(run) &&
            run.SubjectRole is RoleKind.Worker or RoleKind.Researcher).ToArray();
        if (runs.Length == 0)
            throw Member(member, "completed real working run is required.");
        var latestEnd = runs.Max(item => item.EndedAt);
        var latest = runs.Where(run => run.EndedAt == latestEnd).ToArray();
        if (latest.Length != 1)
            throw Member(member, "latest working run is ambiguous.");
        return latest[0];
    }

    internal static void EnsureStart(GovernedTaskState state, ActorId dispatcher, RoleKind? role,
        WorkItemId? anchor, AssuranceBinding binding, string provider, string? session)
    {
        var members = ValidateMembership(anchor, binding);
        foreach (var member in members)
        {
            if (!state.WorkItems.TryGetValue(member, out var item))
                throw Member(member, "unknown work item.");
            if (item.Status is WorkItemStatus.Blocked or WorkItemStatus.Stale or WorkItemStatus.Completed or WorkItemStatus.Abandoned)
                throw Member(member, $"cannot start in status '{item.Status}'.");
        }
        if (role is not (RoleKind.Verifier or RoleKind.CodeReviewer))
            throw new GovernanceException("Assurance coverage: only Verifier or CodeReviewer may carry assurance.");
        foreach (var member in members)
        {
            try { WorkItemLifecycleRules.EnsureCanStart(state, dispatcher, state.WorkItems[member]); }
            catch (GovernanceException error) { throw Member(member, error.Message); }
        }
        foreach (var member in members)
        {
            try { ClaimDependencyRules.EnsureDependenciesAreCurrent(state, state.WorkItems[member].DependsOnClaims); }
            catch (GovernanceException error) { throw Member(member, error.Message); }
            if (state.Escalations.Values.Any(e => e.WorkItemId == member && e.Status == EscalationStatus.Open))
                throw Member(member, "an open escalation blocks assurance.");
        }
        foreach (var member in members)
            if (state.Runs.Values.Any(run => run.Status == AgentRunStatus.Active &&
                WorkCoverage.Effective(run.WorkItemId, run.Assurance).Contains(member)))
                throw Member(member, "already has an active orchestration run.");
        // CLI snapshots cannot supply a real working ID for an unknown/unworked member. Let
        // existence and readiness name that member before validating the supplied provenance set.
        var latestWork = members.ToDictionary(member => member, member => LatestWork(state, member));
        ValidateVersions(binding, members);
        foreach (var member in members)
            if (binding.WorkVersions.Single(row => row.WorkItemId == member).WorkingRunId != latestWork[member].Id)
                throw Member(member, "working provenance does not match the latest working run.");
        if (binding.CandidateId is null || binding.CandidateId.Length != 64 ||
            !binding.CandidateId.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new GovernanceException("Assurance candidate: identity must be 64 lowercase hexadecimal characters.");
        if (role == RoleKind.Verifier)
        {
            if (binding.VerifierRunId is not null)
                throw new GovernanceException("Assurance pairing: a verifier cannot name a verifier run.");
            foreach (var member in members)
                if (string.Equals(latestWork[member].Provider, provider, StringComparison.OrdinalIgnoreCase))
                    throw Member(member, "verifier provider must be independent of the latest working provider.");
        }
        else
        {
            if (binding.VerifierRunId is not { } pair || !state.Runs.TryGetValue(pair, out var verifier) ||
                verifier.SubjectRole != RoleKind.Verifier || !IsReal(verifier) || verifier.Assurance is null ||
                !Same(binding, verifier.Assurance, includePair: false))
                throw new GovernanceException("Assurance pairing: reviewer requires a completed verifier with identical members, candidate and working versions.");
            foreach (var member in members)
                if (!QualifiesVerifier(state, verifier, member))
                    throw new GovernanceException($"Assurance pairing: verifier '{pair}' is not current and independent for member '{member}'.");
        }
        if (session is not null)
            throw new GovernanceException("Frozen assurance runs require a fresh provider session; use provider launch.");
    }

    internal static bool HasOutput(GovernedTaskState state, AgentRun run, GovernedArtifactKind kind, WorkItemId member) =>
        ArtifactApplicability.Current(state).Any(artifact => artifact.Kind == kind && artifact.ProducerRunId == run.Id &&
            ArtifactApplicability.CurrentMembers(state, artifact).Contains(member));

    internal static void EnsureCompletedOutput(GovernedTaskState state, AgentRun run)
    {
        var kind = run.SubjectRole == RoleKind.Verifier
            ? GovernedArtifactKind.VerifierOutput : GovernedArtifactKind.CodeReviewOutput;
        foreach (var member in WorkCoverage.Effective(run.WorkItemId, run.Assurance))
            if (!HasOutput(state, run, kind, member))
                throw new GovernanceException(
                    $"Assurance artifact coverage: completing run '{run.Id}' requires its applicable '{kind}' on member '{member}'.");
    }

    internal static bool QualifiesVerifier(GovernedTaskState state, AgentRun run, WorkItemId member)
    {
        if (run.Assurance is null || run.SubjectRole != RoleKind.Verifier || !IsReal(run) ||
            !run.Assurance.WorkItemIds.Contains(member) || !HasOutput(state, run, GovernedArtifactKind.VerifierOutput, member))
            return false;
        try
        {
            var work = LatestWork(state, member);
            return run.Assurance.WorkVersions.Single(row => row.WorkItemId == member).WorkingRunId == work.Id &&
                !string.Equals(work.Provider, run.Provider, StringComparison.OrdinalIgnoreCase);
        }
        catch (GovernanceException) { return false; }
    }

    internal static bool QualifiesReview(GovernedTaskState state, AgentRun review, WorkItemId member) =>
        review.Assurance is { VerifierRunId: { } pair } && review.SubjectRole == RoleKind.CodeReviewer && IsReal(review) &&
        review.Assurance.WorkItemIds.Contains(member) && HasOutput(state, review, GovernedArtifactKind.CodeReviewOutput, member) &&
        state.Runs.TryGetValue(pair, out var verifier) && verifier.Assurance is not null &&
        Same(review.Assurance, verifier.Assurance, includePair: false) && review.StartedAt >= verifier.EndedAt &&
        QualifiesVerifier(state, verifier, member);

    internal static bool HasNewAssurance(GovernedTaskState state, WorkItemId member) =>
        state.Runs.Values.Any(run => run.Assurance is not null && run.Assurance.WorkItemIds.Contains(member));

    private static GovernanceException Member(WorkItemId member, string reason) =>
        new($"Assurance member '{member}': {reason}");
}
