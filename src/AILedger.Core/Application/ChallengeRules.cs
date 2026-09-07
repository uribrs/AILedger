using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Application;

// Command-time counterparts: ValidateChallengeRaised, ValidateChallengeDisposed, ValidateDecisionOverturned.
internal static class ChallengeRules
{
    internal static IReadOnlyList<LedgerEventData> RaiseChallenge(
        GovernedTaskState state,
        RaiseChallengeCommand command,
        DateTimeOffset now)
    {
        RequireId(command.ChallengeId.Value, nameof(command.ChallengeId));
        EnsureNew(state.Challenges, command.ChallengeId, "challenge");
        RequireText(command.TargetType, nameof(command.TargetType));
        RequireText(command.TargetId, nameof(command.TargetId));
        RequireText(command.Reason, nameof(command.Reason));
        EnsureUnique(command.EvidenceIds, "Evidence IDs");
        EnsureReferencesExist(state.Evidence, command.EvidenceIds, "evidence");
        EnsureChallengeTargetExists(state, command.TargetType, command.TargetId);
    
        var challenge = new Challenge(
            command.ChallengeId,
            command.TargetType.Trim(),
            command.TargetId.Trim(),
            command.Reason.Trim(),
            ChallengeStatus.Open,
            command.EvidenceIds.ToArray(),
            new Provenance(command.ActorId, now, "challenge.raise"));
        return [new ChallengeRaised(challenge)];
    }

    internal static IReadOnlyList<LedgerEventData> DisposeChallenge(
        GovernedTaskState state,
        DisposeChallengeCommand command)
    {
        var challenge = Get(state.Challenges, command.ChallengeId, "challenge");
        if (challenge.Status != ChallengeStatus.Open)
        {
            throw new GovernanceException("Only an open challenge can be disposed.");
        }
    
        if (command.Status == ChallengeStatus.Open)
        {
            throw new GovernanceException("Challenge disposition must be terminal.");
        }
    
        var events = new List<LedgerEventData> { new ChallengeDisposed(command.ChallengeId, command.Status) };
        if (command.Status == ChallengeStatus.Supported)
        {
            AddChallengeConsequence(state, command, challenge, events);
        }
    
        return events;
    }
    
    // An upheld challenge has to change something, or the protocol is advisory. The consequence
    // is whatever the target kind already has machinery for.

    private static void AddChallengeConsequence(
        GovernedTaskState state,
        DisposeChallengeCommand command,
        Challenge challenge,
        ICollection<LedgerEventData> events)
    {
        var targetId = challenge.TargetId.Trim();
    
        // Supporting a challenge is not an unevidenced route to overturning anything. The claim
        // arm below additionally checks direction, which the model only expresses for claims.
        if (challenge.EvidenceIds.Count == 0)
        {
            throw new GovernanceException(
                $"Challenge '{challenge.Id}' carries no evidence and cannot be supported.");
        }
    
        switch (challenge.TargetType.Trim().ToLowerInvariant())
        {
            case "claim":
            {
                var claim = Get(state.Claims, new ClaimId(targetId), "claim");
                if (claim.Status is ClaimStatus.Rejected or ClaimStatus.Superseded)
                {
                    throw new GovernanceException(
                        $"Claim '{claim.Id}' is already '{claim.Status}'; supporting this challenge would change nothing.");
                }
    
                // R1 (challenge-capability-replay-trap): the emitted ClaimResolved is authorised at
                // replay against ResolveClaim, so demand it here or the task becomes unreplayable.
                RequireConsequenceCapability(state, command.ActorId, Capability.ResolveClaim);
                var refuting = challenge.EvidenceIds
                    .Where(id => state.Evidence[id].Refutes.Contains(claim.Id))
                    .ToArray();
                if (refuting.Length != challenge.EvidenceIds.Count || refuting.Length == 0)
                {
                    throw new GovernanceException(
                        $"A challenge against claim '{claim.Id}' can only be supported when every piece of its evidence refutes that claim.");
                }
    
                events.Add(new ClaimResolved(claim.Id, ClaimStatus.Rejected, refuting));
                ClaimDependencyRules.AddDependencyInvalidations(state, claim.Id, events);
                return;
            }
    
            case "decision":
            {
                var decision = Get(state.Decisions, new DecisionId(targetId), "decision");
                if (decision.Status is not (DecisionStatus.Proposed or DecisionStatus.Accepted))
                {
                    throw new GovernanceException(
                        $"Decision '{decision.Id}' is already '{decision.Status}'; supporting this challenge would change nothing.");
                }
    
                RequireConsequenceCapability(state, command.ActorId, Capability.ResolveDecision);
                events.Add(new DecisionOverturned(decision.Id, command.ChallengeId));
                return;
            }
    
            case "work" or "workitem" or "work-item":
            {
                var workItem = Get(state.WorkItems, new WorkItemId(targetId), "work item");
                if (workItem.Status is WorkItemStatus.Blocked or WorkItemStatus.Stale or
                    WorkItemStatus.Completed or WorkItemStatus.Abandoned)
                {
                    throw new GovernanceException(
                        $"Work item '{workItem.Id}' is already '{workItem.Status}'; supporting this challenge would change nothing.");
                }
    
                RequireConsequenceCapability(state, command.ActorId, Capability.ManageWork);
                events.Add(new WorkItemBlocked(
                    workItem.Id, $"Challenge '{command.ChallengeId}' was supported.", null));
                return;
            }
    
            default:
                throw new GovernanceException($"Unsupported challenge target type '{challenge.TargetType}'.");
        }
    }

    private static void RequireConsequenceCapability(GovernedTaskState state, ActorId actorId, Capability capability)
    {
        if (!state.Roles[actorId].Capabilities.Contains(capability))
        {
            throw new GovernanceException(
                $"Supporting this challenge emits a '{capability}' transition, which actor '{actorId}' lacks.");
        }
    }

    private static void EnsureChallengeTargetExists(GovernedTaskState state, string targetType, string targetId)
    {
        var exists = targetType.Trim().ToLowerInvariant() switch
        {
            "claim" => state.Claims.Keys.Any(id => id.Value == targetId.Trim()),
            "decision" => state.Decisions.Keys.Any(id => id.Value == targetId.Trim()),
            "work" or "workitem" or "work-item" => state.WorkItems.Keys.Any(id => id.Value == targetId.Trim()),
            _ => throw new GovernanceException($"Unsupported challenge target type '{targetType}'.")
        };
    
        if (!exists)
        {
            throw new GovernanceException($"Challenge target '{targetType}:{targetId}' does not exist.");
        }
    }
}
