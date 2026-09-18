using System.Security.Cryptography;
using System.Text;
using AILedger.Core.Artifacts;
using AILedger.Core.Contracts;
using AILedger.Core.WorkItems;

namespace AILedger.Core.Application;

// Deterministic closeout facts. This projection reads and joins; it makes no finding, severity,
// opportunity, repair, disposition, lesson, or grade judgement.
public static class TaskCloseoutEvidence
{
    private static readonly IReadOnlySet<GovernedArtifactKind> AssuranceKinds =
        new HashSet<GovernedArtifactKind>
        {
            GovernedArtifactKind.VerifierOutput,
            GovernedArtifactKind.CodeReviewOutput
        };

    public static TaskCloseoutEvidenceReport Build(
        GovernedTaskState state,
        IReadOnlyList<LedgerEvent> history)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(history);

        var revisions = BuildRevisions(state, history);
        return new TaskCloseoutEvidenceReport(
            state.TaskId,
            state.Stage,
            state.Version,
            revisions,
            BuildWorkItems(state, history),
            BuildRecords(state),
            BuildCompleteness(state, revisions));
    }

    private static IReadOnlyList<CloseoutAssuranceRevision> BuildRevisions(
        GovernedTaskState state,
        IReadOnlyList<LedgerEvent> history)
    {
        var current = ArtifactApplicability.Current(state)
            .Select(artifact => artifact.ArtifactId)
            .ToHashSet();
        var revisions = new List<CloseoutAssuranceRevision>();

        for (var index = 0; index < history.Count; index++)
        {
            if (history[index].Data is not ArtifactRecorded recorded ||
                !AssuranceKinds.Contains(recorded.Artifact.Kind))
            {
                continue;
            }

            var artifact = recorded.Artifact;
            revisions.Add(new CloseoutAssuranceRevision(
                artifact.ArtifactId.Value,
                artifact.Kind,
                artifact.Title,
                index,
                history[index].RecordedAt,
                Encoding.UTF8.GetByteCount(artifact.Content),
                Sha256(artifact.Content),
                current.Contains(artifact.ArtifactId),
                BuildProducer(state, artifact.ProducerRunId),
                WorkCoverage.Effective(artifact.WorkItemId, artifact.Assurance)
                    .Select(id => id.Value).ToArray(),
                ArtifactApplicability.CurrentMembers(state, artifact).Select(id => id.Value).ToArray(),
                artifact.Assurance?.CandidateId,
                artifact.SupersedesArtifactId?.Value,
                (artifact.MemberReplacements ?? [])
                    .Select(row => new CloseoutMemberReplacement(
                        row.WorkItemId.Value,
                        row.ArtifactIds.Select(id => id.Value).ToArray()))
                    .ToArray()));
        }

        return revisions;
    }

    private static CloseoutProducer? BuildProducer(GovernedTaskState state, RunId? producerRunId)
    {
        if (producerRunId is not { } runId || !state.Runs.TryGetValue(runId, out var run))
        {
            return null;
        }

        return new CloseoutProducer(
            run.Id.Value,
            run.ActorId.Value,
            run.SubjectRole,
            run.Provider,
            run.Model,
            run.ProviderVersion,
            run.Status,
            run.TerminalFailureReason,
            run.TruncatedLines);
    }

    private static IReadOnlyList<CloseoutWorkItem> BuildWorkItems(
        GovernedTaskState state,
        IReadOnlyList<LedgerEvent> history)
    {
        var order = new Dictionary<RunId, int>();
        for (var index = 0; index < history.Count; index++)
        {
            if (history[index].Data is RunStarted started && !order.ContainsKey(started.Run.Id))
            {
                order[started.Run.Id] = index;
            }
        }

        return state.WorkItems.Values
            .OrderBy(item => item.Id.Value, StringComparer.Ordinal)
            .Select(item => new CloseoutWorkItem(
                item.Id.Value,
                item.Status,
                item.ResourceScope,
                item.DependsOnClaims.Select(claim => claim.Value).ToArray(),
                state.Runs.Values
                    .Where(run => WorkCoverage.Effective(run.WorkItemId, run.Assurance).Contains(item.Id))
                    .OrderBy(run => order.TryGetValue(run.Id, out var position) ? position : int.MaxValue)
                    .ThenBy(run => run.Id.Value, StringComparer.Ordinal)
                    .Select(run => new CloseoutRun(
                        run.Id.Value,
                        run.ActorId.Value,
                        run.SubjectRole,
                        run.Provider,
                        run.Model,
                        run.Status,
                        run.TerminalFailureReason,
                        run.StartedAt,
                        run.EndedAt,
                        run.Assurance?.CandidateId))
                    .ToArray()))
            .ToArray();
    }

    private static CloseoutRecords BuildRecords(GovernedTaskState state) =>
        new(
            state.Claims.Values
                .OrderBy(claim => claim.Id.Value, StringComparer.Ordinal)
                .Select(claim => new CloseoutClaim(
                    claim.Id.Value,
                    claim.Status,
                    claim.Statement,
                    claim.EvidenceIds.Select(id => id.Value).ToArray(),
                    claim.Provenance.ActorId.Value))
                .ToArray(),
            state.Evidence.Values
                .OrderBy(evidence => evidence.Id.Value, StringComparer.Ordinal)
                .Select(evidence => new CloseoutEvidenceRecord(
                    evidence.Id.Value,
                    evidence.SourceType,
                    evidence.Citation,
                    evidence.Summary,
                    evidence.Supports.Select(id => id.Value).ToArray(),
                    evidence.Refutes.Select(id => id.Value).ToArray(),
                    evidence.Provenance.ActorId.Value))
                .ToArray(),
            state.Challenges.Values
                .OrderBy(challenge => challenge.Id.Value, StringComparer.Ordinal)
                .Select(challenge => new CloseoutChallenge(
                    challenge.Id.Value,
                    challenge.Status,
                    challenge.TargetType,
                    challenge.TargetId,
                    challenge.Reason,
                    challenge.EvidenceIds.Select(id => id.Value).ToArray(),
                    challenge.Provenance.ActorId.Value))
                .ToArray());

    private static CloseoutCompleteness BuildCompleteness(
        GovernedTaskState state,
        IReadOnlyList<CloseoutAssuranceRevision> revisions) =>
        new(
            revisions.Count,
            revisions.Count(revision => revision.IsCurrent),
            revisions.Count(revision => revision.Producer is { Status: not AgentRunStatus.Completed }),
            revisions.Count(revision => revision.Producer is null),
            revisions.Count(revision => revision.CandidateId is null),
            ArtifactApplicability.Current(state).Count(artifact =>
                artifact.Kind == GovernedArtifactKind.CloseoutSynthesis),
            ArtifactApplicability.Current(state).Count(artifact =>
                artifact.Kind == GovernedArtifactKind.WorkflowRetrospective));

    private static string Sha256(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();
}

public sealed record CloseoutMemberReplacement(
    string WorkItemId,
    IReadOnlyList<string> ArtifactIds);

public sealed record CloseoutAssuranceRevision(
    string ArtifactId,
    GovernedArtifactKind Kind,
    string Title,
    int EventIndex,
    DateTimeOffset RecordedAt,
    int ContentBytes,
    string ContentSha256,
    bool IsCurrent,
    CloseoutProducer? Producer,
    IReadOnlyList<string> CoveredWorkItemIds,
    IReadOnlyList<string> ApplicableWorkItemIds,
    string? CandidateId,
    string? SupersedesArtifactId,
    IReadOnlyList<CloseoutMemberReplacement> MemberReplacements);

public sealed record CloseoutProducer(
    string RunId,
    string ActorId,
    RoleKind? SubjectRole,
    string Provider,
    string? Model,
    string? ProviderVersion,
    AgentRunStatus Status,
    string? TerminalFailureReason,
    int? TruncatedLines);

public sealed record CloseoutRun(
    string RunId,
    string ActorId,
    RoleKind? SubjectRole,
    string Provider,
    string? Model,
    AgentRunStatus Status,
    string? TerminalFailureReason,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? CandidateId);

public sealed record CloseoutWorkItem(
    string WorkItemId,
    WorkItemStatus Status,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> DependsOnClaims,
    IReadOnlyList<CloseoutRun> Runs);

public sealed record CloseoutClaim(
    string ClaimId,
    ClaimStatus Status,
    string Statement,
    IReadOnlyList<string> EvidenceIds,
    string RecordedBy);

public sealed record CloseoutEvidenceRecord(
    string EvidenceId,
    string SourceType,
    string Citation,
    string Summary,
    IReadOnlyList<string> Supports,
    IReadOnlyList<string> Refutes,
    string RecordedBy);

public sealed record CloseoutChallenge(
    string ChallengeId,
    ChallengeStatus Status,
    string TargetType,
    string TargetId,
    string Reason,
    IReadOnlyList<string> EvidenceIds,
    string RecordedBy);

public sealed record CloseoutRecords(
    IReadOnlyList<CloseoutClaim> Claims,
    IReadOnlyList<CloseoutEvidenceRecord> Evidence,
    IReadOnlyList<CloseoutChallenge> Challenges);

public sealed record CloseoutCompleteness(
    int AssuranceRevisions,
    int CurrentAssuranceRevisions,
    int RevisionsFromUnfinishedRuns,
    int RevisionsWithoutProducerRun,
    int RevisionsWithoutCandidateBinding,
    int CurrentCloseoutSyntheses,
    int CurrentWorkflowRetrospectives);

public sealed record TaskCloseoutEvidenceReport(
    TaskId Task,
    TaskStage Stage,
    long Version,
    IReadOnlyList<CloseoutAssuranceRevision> AssuranceRevisions,
    IReadOnlyList<CloseoutWorkItem> WorkItems,
    CloseoutRecords Records,
    CloseoutCompleteness Completeness);
