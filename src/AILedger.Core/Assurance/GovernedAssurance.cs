using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Runs;
using AILedger.Core.WorkItems;
using static AILedger.Core.Assurance.AssuranceValidation;

namespace AILedger.Core.Assurance;

// Optional versioned host policy association. It never arrives in a provider tool body.
public sealed record GovernedAssuranceScope(int SchemaVersion, string TaskId, IReadOnlyList<string> WorkItemIds);
public sealed record GovernedAssuranceBasis(int SchemaVersion, string TaskId, string CandidateId,
    IReadOnlyList<AssuranceWorkVersion> WorkVersions, string GoverningSha256, string AuthoritySha256);
public sealed record GovernedAcceptanceReceipt(int SchemaVersion, GovernedAssuranceBasis Basis,
    RunId VerifierRunId, RunId ReviewerRunId, IReadOnlyList<AssuranceReceipt> Acceptances);

// Process-local host admission, deliberately not deserializable from a command or recovered journal.
// The trusted storage host creates this only after live owning assurance validation under its lock.
public sealed class WorkCompletionAdmission(GovernedAcceptanceReceipt receipt, long version)
{
    public GovernedAcceptanceReceipt Receipt { get; } = receipt;
    public long Version { get; } = version;
}
public interface IWorkCompletionAdmission
{
    Task<IWorkCompletionLease> AcquireAsync(GovernedTaskState state, WorkItemId work, CancellationToken token);
}
public interface IWorkCompletionLease : IAsyncDisposable
{
    GovernedAcceptanceReceipt Receipt { get; }
    Task RevalidateAsync(CancellationToken token);
}
public interface IGovernedAssuranceContext
{
    Task<IGovernedAssuranceLease> AcquireAsync(AssurancePolicy policy, AssuranceSession session, CancellationToken token);
}
public interface IGovernedAssuranceLease : IAsyncDisposable
{
    GovernedTaskState State { get; }
    void EnsureCurrent();
    Task RevalidateAsync(CancellationToken token) { EnsureCurrent(); return Task.CompletedTask; }
}

public static class GovernedAssuranceRules
{
    public static GovernedAssuranceBasis Capture(GovernedTaskState state, GovernedAssuranceScope scope, string candidate)
    {
        Require(scope.SchemaVersion == 1 && scope.TaskId == state.TaskId.Value, "wrong_governed_binding", "Policy must name this governed task.");
        Unique(scope.WorkItemIds, "governed work members");
        Require(scope.WorkItemIds.Count > 0, "wrong_governed_binding", "An explicit work member set is required.");
        var versions = new List<AssuranceWorkVersion>();
        foreach (var id in scope.WorkItemIds.Order(StringComparer.Ordinal))
        {
            var member = new WorkItemId(id);
            Require(state.WorkItems.ContainsKey(member), "wrong_governed_binding", "Unknown governed work member.");
            EnsureCurrentWork(state, member);
            var work = WorkItemVerificationRules.LatestCompletedWorkingRun(state, member)!;
            versions.Add(new(member, work.Id));
        }
        // This remains the complete acceptance basis. Selective review reassessment lives in
        // the assurance owner and never changes these original run/context identities.
        var governing = Hash(new
        {
            state.TaskId, state.Goal,
            Work = scope.WorkItemIds.Order(StringComparer.Ordinal).Select(id => state.WorkItems[new(id)] with { Status = WorkItemStatus.Proposed }).ToArray(),
            Artifacts = ArtifactApplicability.Current(state).Where(a => a.Kind is GovernedArtifactKind.UserRequest or GovernedArtifactKind.PromptContract or GovernedArtifactKind.OrchestrationPlan)
                .OrderBy(a => a.ArtifactId.Value, StringComparer.Ordinal).ToArray(),
            Claims = state.Claims.Values.OrderBy(c => c.Id.Value, StringComparer.Ordinal).ToArray(),
            Constraints = state.Constraints.Values.OrderBy(c => c.Id.Value, StringComparer.Ordinal).ToArray(),
            Decisions = state.Decisions.Values.OrderBy(c => c.Id.Value, StringComparer.Ordinal).ToArray()
        });
        return new(1, state.TaskId.Value, candidate, versions, governing,
            Hash(state.Roles.Values.OrderBy(a => a.ActorId.Value, StringComparer.Ordinal).ToArray()));
    }

    public static void EnsureCurrentWork(GovernedTaskState state, WorkItemId work)
    {
        var latest = WorkItemVerificationRules.LatestCompletedWorkingRun(state, work);
        if (latest is null) throw new GovernanceException("Applicable assurance requires completed working provenance.");
        if (state.Runs.Values.Any(r => r.WorkItemId == work && r.Id != latest.Id &&
            r.SubjectRole is RoleKind.Worker or RoleKind.Researcher && r.StartedAt >= latest.StartedAt))
            throw new GovernanceException("Newer or uncertain working execution requires fresh assurance; old completion evidence is stale.");
        foreach (var interrupted in state.Runs.Values.Where(r => r.WorkItemId == work &&
            r.SubjectRole is RoleKind.Worker or RoleKind.Researcher && r.Status is AgentRunStatus.Failed or AgentRunStatus.Cancelled))
            if (!HasStoppedEvidence(state, interrupted))
                throw new GovernanceException("Interrupted working execution lacks trusted process termination evidence; later completion and file equality cannot establish process termination.");
    }

    public static bool HasStoppedEvidence(GovernedTaskState state, AgentRun run) =>
        state.Evidence.Values.Any(e => e.TrustedTerminationVersion == 1 && e.SourceType == "process-termination" && e.Citation == $"{state.TaskId.Value}/{run.Id.Value}" &&
            e.Provenance.RecordedAt >= run.StartedAt && state.Roles.TryGetValue(e.Provenance.ActorId, out var actor) && actor.Role == RoleKind.Operator);

    public static (AgentRun Verifier, AgentRun Reviewer) CompletionPair(GovernedTaskState state, WorkItemId work, string candidate)
    {
        EnsureCurrentWork(state, work);
        if (state.Challenges.Values.Any(c => c.Status == ChallengeStatus.Open) ||
            state.Escalations.Values.Any(c => c.Status == EscalationStatus.Open))
            throw new GovernanceException("Unresolved challenges or escalations prevent acceptance/completion.");
        var review = state.Runs.Values.Where(r => AssuranceRules.QualifiesReview(state, r, work) && r.Assurance!.CandidateId == candidate)
            .OrderByDescending(r => r.StartedAt).FirstOrDefault()
            ?? throw new GovernanceException("Completion requires current candidate-bound independent verification and blind review.");
        var verifier = state.Runs[review.Assurance!.VerifierRunId!.Value];
        foreach (var run in new[] { verifier, review })
        {
            if (state.Runs.Values.Any(r => r.SubjectRole == run.SubjectRole && WorkCoverage.Effective(r.WorkItemId, r.Assurance).Contains(work) &&
                r.Id != run.Id && r.StartedAt >= run.StartedAt))
                throw new GovernanceException("Newer assurance execution makes the previous evidence uncertain.");
            if (run.RoutingAssessment is { Disposition: not RoutingDisposition.Proceed } ||
                run.ProducerOutcome is { Outcome: not "reported-complete" })
                throw new GovernanceException("Adverse producer judgment cannot be overridden by completion.");
        }
        if (state.Runs.Values.Any(r => r.Status == AgentRunStatus.Active && WorkCoverage.Effective(r.WorkItemId, r.Assurance).Contains(work)))
            throw new GovernanceException("An active work or assurance run prevents acceptance/completion.");
        var independentActors = state.Runs.Values.Where(r => r.SubjectRole is RoleKind.Verifier or RoleKind.CodeReviewer &&
            WorkCoverage.Effective(r.WorkItemId, r.Assurance).Contains(work)).Select(r => r.ActorId).ToHashSet();
        if (state.Claims.Values.Any(c => independentActors.Contains(c.Provenance.ActorId) && c.Status is ClaimStatus.Open or ClaimStatus.Superseded))
            throw new GovernanceException("Independent governed findings remain unresolved; supersession or producer completion cannot dispose them.");
        EnsureCleanVerifier(state, work);
        return (verifier, review);
    }

    public static void EnsureCleanVerifier(GovernedTaskState state, WorkItemId work)
    {
        foreach (var artifact in ArtifactApplicability.Current(state).Where(a => a.Kind == GovernedArtifactKind.VerifierOutput &&
            ArtifactApplicability.CurrentMembers(state, a).Contains(work)))
        {
            var observation = VerifierOutputDocuments.Observe(artifact);
            if (observation.ClaimStatuses.GetValueOrDefault("REJECTED") > 0 || observation.ClaimStatuses.GetValueOrDefault("NEVER-TESTED") > 0 ||
                observation.AttentionDispositions.GetValueOrDefault("unresolved") > 0 || observation.AttentionDispositions.GetValueOrDefault("accepted-risk") > 0)
                throw new GovernanceException("Negative, NEVER-TESTED, unresolved or unsupported residual-risk verifier evidence prevents completion.");
        }
    }

    public static void ValidateReceipt(GovernedTaskState state, WorkItemId work, GovernedAcceptanceReceipt receipt)
    {
        if (receipt.SchemaVersion != 1 || receipt.Basis.SchemaVersion != 1 || receipt.Basis.TaskId != state.TaskId.Value ||
            !receipt.Basis.WorkVersions.Any(v => v.WorkItemId == work) || receipt.Acceptances.Count == 0 ||
            receipt.Acceptances.Any(r => r.Operation != "accept_assurance" || !AILedger.Core.Artifacts.ArtifactSubmissionIdentity.IsHash(r.ContentSha256)) ||
            !state.Runs.ContainsKey(receipt.VerifierRunId) || !state.Runs.ContainsKey(receipt.ReviewerRunId))
            throw new GovernanceException("Invalid versioned governed acceptance association.");
    }
}
