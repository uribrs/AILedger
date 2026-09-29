using AILedger.Core.Contracts;
using AILedger.Core.Alternatives;
using AILedger.Storage.Alternatives;

namespace AILedger.Storage;

public sealed partial class FileGovernedTaskService
{
    private CommandOutcome BuildAlternativesCandidate(GovernedTaskState state, AlternativesBinding binding,
        AlternativesRequest request, AlternativesAttempt attempt)
    {
        var taken = state.Alternatives.Keys.Select(k => k.Value).ToHashSet(StringComparer.Ordinal);
        var events = new List<LedgerEvent>();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < request.Alternatives.Count; i++)
        {
            var item = request.Alternatives[i];
            var command = new RecordAlternativeCommand(binding.ActorId, binding.CausationId, binding.CorrelationId,
                new AlternativeId(AllocateFindingsId("AF_", taken)), item.Statement, item.RejectionRationale,
                item.ReplacedByDecisionId is { } decision ? new DecisionId(decision) : null,
                item.FromLesson is { } lesson ? new LessonId(lesson) : null);
            attempt.Command = command;
            attempt.ItemPath = $"alternatives[{i}]";
            var outcome = _commandHandler.Handle(state, command, now);
            ValidateOutcome(binding.TaskId, state, outcome);
            if (outcome.Events.Count != 1 || outcome.Events[0].Data is not AlternativeRecorded ||
                outcome.Events[0].ActorId != binding.ActorId || outcome.Events[0].CorrelationId != binding.CorrelationId ||
                outcome.Events[0].CausationId != binding.CausationId)
                throw new InvalidOperationException("Alternatives commands must emit exactly one correctly bound event.");
            events.Add(outcome.Events[0]);
            state = outcome.State;
        }
        return new CommandOutcome(state, events);
    }

    private static AlternativesReceipt CreateAlternativesReceipt(AlternativesBinding binding,
        AlternativesRequest request, string fingerprint, CommandOutcome outcome)
    {
        var maps = request.Alternatives.Select((item, i) => new AlternativeMap(item.Key,
            ((AlternativeRecorded)outcome.Events[i].Data).Alternative.Id.Value, outcome.Events[i].EventId.Value)).ToArray();
        return new(1, request.RequestId, Guid.NewGuid().ToString("N"), binding.TaskId.Value,
            binding.ActorId.Value, binding.RunId?.Value, binding.CorrelationId, binding.CausationId?.Value,
            fingerprint, AlternativesFingerprint.Algorithm, outcome.Events[0].RecordedAt, outcome.State.Version,
            Array.AsReadOnly(outcome.Events.Select(e => e.EventId.Value).ToArray()), Array.AsReadOnly(maps));
    }
}
