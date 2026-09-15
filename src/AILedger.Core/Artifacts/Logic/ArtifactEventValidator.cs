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

        ArtifactScopeRules.Ensure(artifact.Kind, artifact.WorkItemId);
        var assignment = Get(state.Roles, @event.ActorId, "actor role");
        ArtifactAuthorityRules.Ensure(
            state,
            artifact.Kind,
            @event.ActorId,
            assignment.Role,
            artifact.WorkItemId,
            artifact.ProducerRunId);
        ArtifactRevisionRules.Ensure(
            state,
            artifact.Kind,
            artifact.WorkItemId,
            artifact.ArtifactId,
            artifact.SupersedesArtifactId);
        ArtifactDocumentRules.Validate(state, artifact.Kind, artifact.WorkItemId, artifact.Content);
        ValidateProvenance(@event, artifact.Provenance, "artifact.record");
    }
}
