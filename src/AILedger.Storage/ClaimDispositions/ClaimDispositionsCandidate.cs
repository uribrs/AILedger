using AILedger.Core.Contracts;
using AILedger.Core.ClaimDispositions;
using AILedger.Core.Findings;
using AILedger.Storage.ClaimDispositions;

namespace AILedger.Storage;

public sealed partial class FileGovernedTaskService
{
    private CommandOutcome BuildClaimDispositionsCandidate(GovernedTaskState state, ClaimDispositionsBinding binding,
        ClaimDispositionsRequest request, ClaimDispositionsAttempt attempt)
    {
        var events = new List<LedgerEvent>();
        var now = DateTimeOffset.UtcNow;
        for (var i = 0; i < request.Dispositions.Count; i++)
        {
            var item = request.Dispositions[i];
            var command = new ResolveClaimCommand(binding.ActorId, binding.CausationId, binding.CorrelationId,
                new ClaimId(item.Claim.ClaimId), item.Status, item.Evidence.Select(e => new EvidenceId(e.EvidenceId)).ToArray(),
                Rationale: item.Rationale);
            attempt.Command = command;
            attempt.ItemPath = $"dispositions[{i}]";
            // Check authority before revealing state/reference diagnostics. Handler still owns resolution rules.
            new AILedger.Core.Domain.AuthorizationPolicy().Authorize(state, command);
            if (!state.Claims.TryGetValue(command.ClaimId, out var claim))
                throw new FindingsRequestException("invalid_reference", $"Unknown claim '{command.ClaimId}'.", attempt.ItemPath + ".claim.claim_id");
            if (claim.Status != item.ExpectedStatus)
                throw new FindingsRequestException("state_conflict",
                    $"Claim '{claim.Id}' is '{claim.Status}', expected '{item.ExpectedStatus}'. Re-read the claim and reconsider the judgment.",
                    attempt.ItemPath + ".expected_status");
            for (var j = 0; j < item.Evidence.Count; j++)
                if (!state.Evidence.ContainsKey(command.EvidenceIds[j]))
                    throw new FindingsRequestException("invalid_reference", $"Unknown evidence '{command.EvidenceIds[j]}'.",
                        attempt.ItemPath + $".evidence[{j}].evidence_id");
            var outcome = _commandHandler.Handle(state, command, now);
            ValidateOutcome(binding.TaskId, state, outcome);
            events.AddRange(outcome.Events);
            state = outcome.State;
        }
        return new CommandOutcome(state, events);
    }

    private static ClaimDispositionsReceipt CreateClaimDispositionsReceipt(GovernedTaskState original,
        ClaimDispositionsBinding binding, ClaimDispositionsRequest request, string fingerprint, CommandOutcome outcome)
    {
        var changes = new List<ClaimDispositionChange>();
        var position = 0;
        foreach (var item in request.Dispositions)
        {
            var resolution = outcome.Events[position++];
            var dependencies = new List<DispositionDependencyChange>();
            while (position < outcome.Events.Count && outcome.Events[position].Data is not ClaimResolved)
                dependencies.Add(ClaimDispositionsReceiptEnvelope.Dependency(outcome.Events[position++]));
            changes.Add(new(item.Key, item.Claim.ClaimId, original.Claims[new(item.Claim.ClaimId)].Status,
                item.Status, item.Rationale, item.Evidence, resolution.EventId.Value, dependencies.AsReadOnly()));
        }
        return new(1, request.RequestId, Guid.NewGuid().ToString("N"), binding.TaskId.Value,
            binding.ActorId.Value, binding.RunId?.Value, binding.CorrelationId, binding.CausationId?.Value,
            fingerprint, ClaimDispositionsFingerprint.Algorithm, outcome.Events[0].RecordedAt, outcome.State.Version,
            Array.AsReadOnly(outcome.Events.Select(e => e.EventId.Value).ToArray()), changes.AsReadOnly());
    }
}
