using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Domain.ReplayValidationRules;

namespace AILedger.Core.Decisions;

// Replay validates historical Decision event shapes independently from today's command rules.
// Command-time policy may tighten; this validator must keep every previously legal history readable.
internal static class DecisionEventValidator
{
    internal static void ValidateProposed(
        GovernedTaskState state,
        LedgerEvent @event,
        Decision decision)
    {
        EnsureCitedLessonWasRecalled(state, decision.FromLesson);
        RequireAuthority(state, @event.ActorId, Capability.ProposeDecision);
        EnsureNew(state.Decisions, decision.Id, "decision");
        RequireId(decision.Id.Value, nameof(decision.Id));
        RequireText(decision.Statement, nameof(decision.Statement));
        RequireText(decision.Rationale, nameof(decision.Rationale));
        RequireDefined(decision.Status, nameof(decision.Status));
        if (decision.Status != DecisionStatus.Proposed)
        {
            throw new GovernanceException("A newly proposed decision must have proposed status.");
        }

        EnsureUnique(decision.DependsOnClaims, "Dependent claim IDs");
        EnsureReferencesExist(state.Claims, decision.DependsOnClaims, "claim");
        EnsureDependenciesAreCurrent(state, decision.DependsOnClaims);
        if (decision.Supersedes is { } supersededId)
        {
            if (supersededId == decision.Id)
            {
                throw new GovernanceException("A decision cannot supersede itself.");
            }

            EnsureDecisionIsCurrent(Get(state.Decisions, supersededId, "decision"));
        }

        ValidateProvenance(@event, decision.Provenance, "decision.propose");
    }

    internal static void ValidateResolved(
        GovernedTaskState state,
        LedgerEvent @event,
        DecisionResolved resolved)
    {
        RequireAuthority(state, @event.ActorId, Capability.ResolveDecision);
        RequireDefined(resolved.Status, nameof(resolved.Status));
        if (resolved.Status is not (DecisionStatus.Accepted or DecisionStatus.Superseded))
        {
            throw new GovernanceException(
                "A decision may be explicitly accepted or superseded; invalidation is causal.");
        }

        var decision = Get(state.Decisions, resolved.DecisionId, "decision");
        if (decision.Status == DecisionStatus.Proposed)
        {
            if (resolved.Status == DecisionStatus.Accepted)
            {
                EnsureDependenciesAreCurrent(state, decision.DependsOnClaims);
                if (decision.Supersedes is { } predecessorId)
                {
                    EnsureDecisionIsCurrent(Get(state.Decisions, predecessorId, "decision"));
                    if (state.Decisions.Values.Any(candidate =>
                            candidate.Status == DecisionStatus.Accepted &&
                            candidate.Supersedes == predecessorId))
                    {
                        throw new GovernanceException(
                            $"Decision '{predecessorId}' already has an accepted replacement.");
                    }
                }
            }

            return;
        }

        if (decision.Status == DecisionStatus.Accepted &&
            resolved.Status == DecisionStatus.Superseded &&
            state.Decisions.Values.Any(candidate =>
                candidate.Status == DecisionStatus.Accepted && candidate.Supersedes == decision.Id))
        {
            return;
        }

        throw new GovernanceException(
            $"Decision in status '{decision.Status}' cannot transition to '{resolved.Status}'.");
    }

    internal static void ValidateInvalidated(
        GovernedTaskState state,
        LedgerEvent @event,
        DecisionInvalidated invalidated)
    {
        RequireAuthority(state, @event.ActorId, Capability.ResolveClaim);
        var decision = Get(state.Decisions, invalidated.DecisionId, "decision");
        EnsureDecisionIsCurrent(decision);
        var claim = Get(state.Claims, invalidated.RejectedClaimId, "claim");
        if (claim.Status is not (ClaimStatus.Rejected or ClaimStatus.Superseded) ||
            !decision.DependsOnClaims.Contains(claim.Id))
        {
            throw new GovernanceException(
                "A decision can only be invalidated by one of its rejected or superseded claims.");
        }
    }

    internal static void ValidateOverturned(
        GovernedTaskState state,
        LedgerEvent @event,
        DecisionOverturned overturned)
    {
        RequireAuthority(state, @event.ActorId, Capability.ResolveDecision);
        var decision = Get(state.Decisions, overturned.DecisionId, "decision");
        if (decision.Status is not (DecisionStatus.Proposed or DecisionStatus.Accepted))
        {
            throw new GovernanceException(
                $"Only a current decision can be overturned; this one is '{decision.Status}'.");
        }

        var challenge = Get(state.Challenges, overturned.ChallengeId, "challenge");
        if (challenge.Status != ChallengeStatus.Supported ||
            !string.Equals(challenge.TargetId.Trim(), decision.Id.Value, StringComparison.Ordinal))
        {
            throw new GovernanceException("A decision is only overturned by a supported challenge against it.");
        }

        if (challenge.EvidenceIds.Count == 0)
        {
            throw new GovernanceException(
                $"Challenge '{challenge.Id}' carries no evidence and cannot overturn a decision.");
        }
    }

    private static void EnsureDecisionIsCurrent(Decision decision)
    {
        if (decision.Status is DecisionStatus.Superseded or DecisionStatus.Invalidated)
        {
            throw new GovernanceException("Only a current decision can be superseded.");
        }
    }

    private static void EnsureDependenciesAreCurrent(
        GovernedTaskState state,
        IEnumerable<ClaimId> claimIds)
    {
        var invalid = claimIds
            .Select(claimId => state.Claims[claimId])
            .FirstOrDefault(claim => claim.Status is ClaimStatus.Rejected or ClaimStatus.Superseded);
        if (invalid is not null)
        {
            throw new GovernanceException(
                $"Dependency claim '{invalid.Id}' is '{invalid.Status}' and cannot support actionable work.");
        }
    }
}
