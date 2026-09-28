using AILedger.Core.Contracts;
using AILedger.Core.Findings;
using AILedger.Storage.Findings;

namespace AILedger.Storage;

public sealed partial class FileGovernedTaskService
{
    private CommandOutcome BuildFindingsCandidate(GovernedTaskState state, FindingsBinding binding,
        FindingsRequest request, FindingsAttempt attempt)
    {
        var taken = state.Claims.Keys.Select(k => k.Value).Concat(state.Evidence.Keys.Select(k => k.Value))
            .ToHashSet(StringComparer.Ordinal);
        var local = request.Findings.ToDictionary(f => f.Key, _ => new ClaimId(AllocateFindingsId("CF_", taken)),
            StringComparer.Ordinal);
        var events = new List<LedgerEvent>();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < request.Findings.Count; i++)
        {
            var item = request.Findings[i];
            var command = new AddClaimCommand(binding.ActorId, binding.CausationId, binding.CorrelationId,
                local[item.Key], item.Statement, item.ConsequenceIfWrong,
                item.FromLesson is { } lesson ? new LessonId(lesson) : null);
            state = ApplyFindingCommand(state, binding, command, $"findings[{i}]", now, events, attempt);
        }
        for (var i = 0; i < request.Evidence.Count; i++)
        {
            var item = request.Evidence[i];
            var command = new AddEvidenceCommand(binding.ActorId, binding.CausationId, binding.CorrelationId,
                new EvidenceId(AllocateFindingsId("EF_", taken)), item.SourceType, item.Citation, item.Summary,
                ResolveReferences(item.Supports, local), ResolveReferences(item.Refutes, local));
            state = ApplyFindingCommand(state, binding, command, $"evidence[{i}]", now, events, attempt);
        }
        return new CommandOutcome(state, events);
    }

    private GovernedTaskState ApplyFindingCommand(GovernedTaskState state, FindingsBinding binding,
        LedgerCommand command, string path, DateTimeOffset now, List<LedgerEvent> events, FindingsAttempt attempt)
    {
        attempt.Command = command;
        attempt.ItemPath = path;
        var outcome = _commandHandler.Handle(state, command, now);
        ValidateOutcome(binding.TaskId, state, outcome);
        if (outcome.Events.Count != 1 || outcome.Events[0].ActorId != binding.ActorId ||
            outcome.Events[0].CorrelationId != binding.CorrelationId || outcome.Events[0].CausationId != binding.CausationId)
            throw new InvalidOperationException("Findings commands must emit exactly one correctly bound event.");
        events.Add(outcome.Events[0]);
        return outcome.State;
    }

    private static ClaimId[] ResolveReferences(IReadOnlyList<FindingReference> references,
        IReadOnlyDictionary<string, ClaimId> local) => references
        .Select(r => r.Finding is { } key ? local[key] : new ClaimId(r.ClaimId!)).ToArray();

    private static string AllocateFindingsId(string prefix, HashSet<string> taken)
    {
        string id;
        do { id = prefix + Guid.NewGuid().ToString("N"); } while (!taken.Add(id));
        return id;
    }

    private static FindingsReceipt CreateFindingsReceipt(FindingsBinding binding, FindingsRequest request,
        string fingerprint, CommandOutcome outcome)
    {
        var findings = request.Findings.Select((item, i) => new FindingMap(item.Key,
            ((ClaimAdded)outcome.Events[i].Data).Claim.Id.Value, outcome.Events[i].EventId.Value)).ToArray();
        var evidence = request.Evidence.Select((item, i) =>
        {
            var e = outcome.Events[request.Findings.Count + i];
            return new EvidenceMap(item.Key, ((EvidenceAdded)e.Data).Evidence.Id.Value, e.EventId.Value);
        }).ToArray();
        return new FindingsReceipt(1, request.RequestId, Guid.NewGuid().ToString("N"), binding.TaskId.Value,
            binding.ActorId.Value, binding.RunId?.Value, binding.CorrelationId, binding.CausationId?.Value,
            fingerprint, FindingsFingerprint.Algorithm, outcome.Events[0].RecordedAt, outcome.State.Version,
            Array.AsReadOnly(outcome.Events.Select(e => e.EventId.Value).ToArray()),
            Array.AsReadOnly(findings), Array.AsReadOnly(evidence));
    }
}
