using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Application;

// Replay counterparts: ValidateDecisionProposed, ValidateDecisionResolved, ValidateDecisionInvalidated.
internal static class DecisionRules
{
    internal static IReadOnlyList<LedgerEventData> ProposeDecision(
        GovernedTaskState state,
        ProposeDecisionCommand command,
        DateTimeOffset now)
    {
        RequireId(command.DecisionId.Value, nameof(command.DecisionId));
        EnsureNew(state.Decisions, command.DecisionId, "decision");
        RequireText(command.Statement, nameof(command.Statement));
        RequireText(command.Rationale, nameof(command.Rationale));
        EnsureUnique(command.DependsOnClaims, "Dependent claim IDs");
        EnsureReferencesExist(state.Claims, command.DependsOnClaims, "claim");
        ClaimDependencyRules.EnsureDependenciesAreCurrent(state, command.DependsOnClaims);
        LessonCitationRules.EnsureCitedLessonWasRecalled(state, command.FromLesson);
    
        if (command.Supersedes is { } supersededId)
        {
            if (supersededId == command.DecisionId)
            {
                throw new GovernanceException("A decision cannot supersede itself.");
            }
    
            var superseded = Get(state.Decisions, supersededId, "decision");
            if (superseded.Status is DecisionStatus.Superseded or DecisionStatus.Invalidated)
            {
                throw new GovernanceException("Only a current decision can be superseded.");
            }
        }
    
        var decision = new Decision(
            command.DecisionId,
            command.Statement.Trim(),
            DecisionStatus.Proposed,
            command.Rationale.Trim(),
            command.DependsOnClaims.ToArray(),
            command.Supersedes,
            new Provenance(command.ActorId, now, "decision.propose"),
            command.FromLesson);
        return [new DecisionProposed(decision)];
    }

    internal static IReadOnlyList<LedgerEventData> ResolveDecision(
        GovernedTaskState state,
        ResolveDecisionCommand command)
    {
        var decision = Get(state.Decisions, command.DecisionId, "decision");
        if (decision.Status != DecisionStatus.Proposed)
        {
            throw new GovernanceException("Only a proposed decision can be resolved.");
        }
    
    
        ClaimDependencyRules.EnsureDependenciesAreCurrent(state, decision.DependsOnClaims);
    
        if (command.Status is not (DecisionStatus.Accepted or DecisionStatus.Superseded))
        {
            throw new GovernanceException("A decision may be explicitly accepted or superseded; invalidation is causal.");
        }
    
        var events = new List<LedgerEventData> { new DecisionResolved(command.DecisionId, command.Status) };
        if (command.Status == DecisionStatus.Accepted && decision.Supersedes is { } supersededId)
        {
            var predecessor = Get(state.Decisions, supersededId, "decision");
            if (predecessor.Status is DecisionStatus.Superseded or DecisionStatus.Invalidated)
            {
                throw new GovernanceException("A replacement can only be accepted while its predecessor is current.");
            }
    
            events.Add(new DecisionResolved(supersededId, DecisionStatus.Superseded));
        }
    
        return events;
    }
}
