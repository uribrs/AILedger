using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Runs;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Artifacts;

// Decides which Artifact event a recording command may emit. The focused collaborators own the
// policy details; this class owns the recording use case and keeps its construction in one place.
internal static class ArtifactRules
{
    internal static IReadOnlyList<LedgerEventData> Record(
        GovernedTaskState state,
        RecordArtifactCommand command,
        DateTimeOffset now)
    {
        RequireId(command.ArtifactId.Value, nameof(command.ArtifactId));
        EnsureNew(state.Artifacts, command.ArtifactId, "artifact");
        RequireText(command.Title, nameof(command.Title));
        RequireText(command.Content, nameof(command.Content));

        if (command.ProducerRunId is { } producerId && state.Runs.TryGetValue(producerId, out var producer) &&
            producer.Assurance is not null)
            return RecordAssurance(state, command, producer, now);
        if (command.CoveredWorkItemIds is not null)
        {
            var asserted = WorkCoverage.Normalize(command.WorkItemId, command.CoveredWorkItemIds);
            if (!asserted.SequenceEqual(WorkCoverage.Effective(command.WorkItemId, null)))
                throw new GovernanceException("Assurance artifact coverage: legacy producer cannot assert multiple members.");
        }
        if (command.Kind is GovernedArtifactKind.VerifierOutput or GovernedArtifactKind.CodeReviewOutput &&
            command.WorkItemId is { } member && AssuranceRules.HasNewAssurance(state, member))
            throw new GovernanceException($"Assurance artifact coverage: member '{member}' requires a new candidate run.");

        var assignment = Get(state.Roles, command.ActorId, "actor role");
        ArtifactScopeRules.Ensure(command.Kind, command.WorkItemId);
        ArtifactAuthorityRules.Ensure(
            state,
            command.Kind,
            command.ActorId,
            assignment.Role,
            command.WorkItemId,
            command.ProducerRunId);
        ArtifactRevisionRules.Ensure(
            state,
            command.Kind,
            command.WorkItemId,
            command.ArtifactId,
            command.SupersedesArtifactId);

        if (command.Kind == GovernedArtifactKind.WorkflowRetrospective)
        {
            WorkflowRetrospectiveRules.EnsureEntryCondition(state);
        }

        if (command.Kind == GovernedArtifactKind.CloseoutSynthesis)
        {
            CloseoutSynthesisRules.EnsureEntryCondition(state);
        }

        ArtifactDocumentRules.Validate(state, command.Kind, command.WorkItemId, command.Content);

        if (command.Kind == GovernedArtifactKind.InternalRecon)
        {
            InternalReconRules.EnsureConsulted(state, command.ProducerRunId);
        }

        return [new ArtifactRecorded(new GovernedArtifact(
            command.ArtifactId,
            command.Kind,
            command.Title.Trim(),
            command.Content,
            command.WorkItemId,
            command.ProducerRunId,
            command.SupersedesArtifactId,
            new Provenance(command.ActorId, now, "artifact.record")))];
    }

    private static IReadOnlyList<LedgerEventData> RecordAssurance(
        GovernedTaskState state, RecordArtifactCommand command, AgentRun run, DateTimeOffset now)
    {
        if (command.Kind is not (GovernedArtifactKind.VerifierOutput or GovernedArtifactKind.CodeReviewOutput))
            throw new GovernanceException("Assurance artifact coverage: task-wide artifacts cannot use an assurance producer or work flags.");
        var members = WorkCoverage.Effective(run.WorkItemId, run.Assurance);
        if (command.WorkItemId is not null || command.CoveredWorkItemIds is not null)
        {
            var asserted = WorkCoverage.Normalize(command.WorkItemId, command.CoveredWorkItemIds);
            if (!asserted.SequenceEqual(members))
                throw new GovernanceException("Assurance artifact coverage: supplied work flags must equal every producer member.");
        }
        ArtifactAuthorityRules.Ensure(state, command.Kind, command.ActorId,
            Get(state.Roles, command.ActorId, "actor role").Role, run.WorkItemId, run.Id);
        var edges = AssuranceArtifactRules.Replacements(state, command.Kind, members);
        if (command.SupersedesArtifactId is { } assertion && !edges.Any(row => row.ArtifactIds.Contains(assertion)))
            throw new GovernanceException("Assurance artifact coverage: supersedes assertion is not a current intersecting predecessor.");
        ArtifactDocumentRules.Validate(state, command.Kind, run.WorkItemId, command.Content, members);
        var artifact = new GovernedArtifact(command.ArtifactId, command.Kind, command.Title.Trim(), command.Content,
            run.WorkItemId, run.Id, null, new Provenance(command.ActorId, now, "artifact.record"),
            AssuranceRules.Copy(run.Assurance!), edges);
        AssuranceArtifactRules.Validate(state, artifact);
        return [new ArtifactRecorded(artifact)];
    }

}
