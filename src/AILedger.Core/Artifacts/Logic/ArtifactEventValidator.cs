using AILedger.Core.Contracts;
using static AILedger.Core.Domain.ReplayValidationRules;

namespace AILedger.Core.Artifacts;

internal static class ArtifactEventValidator
{
    internal static void ValidateRecorded(
        GovernedTaskState state,
        LedgerEvent @event,
        GovernedArtifact artifact)
    {
        RequireAuthority(state, @event.ActorId, Capability.RecordArtifact);
        EnsureNew(state.Artifacts, artifact.ArtifactId, "artifact");
        RequireId(artifact.ArtifactId.Value, nameof(artifact.ArtifactId));
        RequireDefined(artifact.Kind, nameof(artifact.Kind));
        RequireText(artifact.Title, nameof(artifact.Title));
        RequireText(artifact.Content, nameof(artifact.Content));

        if (artifact.Assurance is null && artifact.ProducerRunId is { } producer &&
            state.Runs.TryGetValue(producer, out var run) && run.Assurance is not null)
            throw new AILedger.Core.Domain.GovernanceException("Assurance artifact coverage: producer binding is required.");
        ArtifactScopeRules.Ensure(artifact.Kind, artifact.WorkItemId);
        var assignment = Get(state.Roles, @event.ActorId, "actor role");
        ArtifactAuthorityRules.Ensure(
            state,
            artifact.Kind,
            @event.ActorId,
            assignment.Role,
            artifact.WorkItemId,
            artifact.ProducerRunId);
        if (artifact.Assurance is not null)
            AssuranceArtifactRules.Validate(state, artifact);
        else ArtifactRevisionRules.Ensure(
            state,
            artifact.Kind,
            artifact.WorkItemId,
            artifact.ArtifactId,
            artifact.SupersedesArtifactId);
        ArtifactDocumentRules.Validate(state, artifact.Kind, artifact.WorkItemId, artifact.Content,
            artifact.Assurance is null ? null : WorkCoverage.Effective(artifact.WorkItemId, artifact.Assurance));
        ValidateProvenance(@event, artifact.Provenance, "artifact.record");
    }
}
