using AILedger.Core.Contracts;
using AILedger.Core.Artifacts;
using AILedger.Storage.Artifacts;

namespace AILedger.Storage;

public sealed partial class FileGovernedTaskService
{
    private CommandOutcome BuildArtifactSubmissionCandidate(GovernedTaskState state, ArtifactSubmissionBinding binding,
        ArtifactSubmissionRequest request, ArtifactSubmissionAttempt attempt)
    {
        var run = state.Runs[binding.RunId]; // Ownership was checked before receipt lookup.
        var taken = state.Artifacts.Keys.Select(k => k.Value).ToHashSet(StringComparer.Ordinal);
        var command = new RecordArtifactCommand(binding.ActorId, binding.CausationId, binding.CorrelationId,
            new ArtifactId(AllocateFindingsId("AS_", taken)), request.Kind, request.Title, request.Content,
            run.Assurance is null ? run.WorkItemId : null, run.Id,
            request.SupersedesArtifactId is { } predecessor ? new ArtifactId(predecessor) : null);
        attempt.Command = command;
        attempt.ItemPath = "artifact";
        var outcome = _commandHandler.Handle(state, command, DateTimeOffset.UtcNow);
        ValidateOutcome(binding.TaskId, state, outcome);
        if (outcome.Events.Count != 1 || outcome.Events[0].Data is not ArtifactRecorded ||
            outcome.Events[0].ActorId != binding.ActorId || outcome.Events[0].CorrelationId != binding.CorrelationId ||
            outcome.Events[0].CausationId != binding.CausationId)
            throw new InvalidOperationException("Artifact submission must emit one correctly bound artifact event.");
        return outcome;
    }

    private static ArtifactSubmissionReceipt CreateArtifactSubmissionReceipt(ArtifactSubmissionBinding binding,
        ArtifactSubmissionRequest request, string fingerprint, CommandOutcome outcome) =>
        new(1, request.RequestId, Guid.NewGuid().ToString("N"), binding.TaskId.Value,
            binding.ActorId.Value, binding.RunId.Value, binding.CorrelationId, binding.CausationId?.Value,
            fingerprint, ArtifactSubmissionIdentity.Algorithm, outcome.Events[0].RecordedAt, outcome.State.Version,
            Array.AsReadOnly(outcome.Events.Select(e => e.EventId.Value).ToArray()),
            ArtifactSubmissionIdentity.Describe(binding.TaskId.Value, ((ArtifactRecorded)outcome.Events[0].Data).Artifact));
}
