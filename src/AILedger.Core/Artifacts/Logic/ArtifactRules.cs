using AILedger.Core.Application;
using AILedger.Core.Contracts;
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

        ArtifactDocumentRules.Validate(state, command.Kind, command.WorkItemId, command.Content);

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
}
