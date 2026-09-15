using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Domain.ReplayValidationRules;

namespace AILedger.Core.Claims;

// Replay validates the event shape that command-time ClaimRules emits. These checks deliberately
// remain separate from command rules: replay must continue accepting every history that was legal.
internal static class ClaimEventValidator
{
    internal static void ValidateAdded(
        GovernedTaskState state,
        LedgerEvent @event,
        Claim claim)
    {
        EnsureCitedLessonWasRecalled(state, claim.FromLesson);
        RequireAuthority(state, @event.ActorId, Capability.AddClaim);
        EnsureNew(state.Claims, claim.Id, "claim");
        RequireId(claim.Id.Value, nameof(claim.Id));
        RequireText(claim.Statement, nameof(claim.Statement));
        RequireDefined(claim.Status, nameof(claim.Status));
        if (claim.Status != ClaimStatus.Open || claim.EvidenceIds.Count != 0)
        {
            throw new GovernanceException("A newly added claim must be open and have no resolved evidence.");
        }

        ValidateProvenance(@event, claim.Provenance, "claim.add");
    }

    internal static void ValidateResolved(
        GovernedTaskState state,
        LedgerEvent @event,
        ClaimResolved resolved)
    {
        RequireAuthority(state, @event.ActorId, Capability.ResolveClaim);
        RequireDefined(resolved.Status, nameof(resolved.Status));
        var claim = Get(state.Claims, resolved.ClaimId, "claim");
        EnsureClaimResolution(claim, resolved.Status);
        EnsureUnique(resolved.EvidenceIds, "Evidence IDs");
        EnsureReferencesExist(state.Evidence, resolved.EvidenceIds, "evidence");

        if (resolved.Status is ClaimStatus.Validated or ClaimStatus.Rejected && resolved.EvidenceIds.Count == 0)
        {
            throw new GovernanceException($"A {resolved.Status.ToString().ToLowerInvariant()} claim requires evidence.");
        }

        foreach (var evidenceId in resolved.EvidenceIds)
        {
            var evidence = state.Evidence[evidenceId];
            var directionMatches = resolved.Status switch
            {
                ClaimStatus.Validated => evidence.Supports.Contains(resolved.ClaimId),
                ClaimStatus.Rejected => evidence.Refutes.Contains(resolved.ClaimId),
                _ => true
            };

            if (!directionMatches)
            {
                throw new GovernanceException(
                    $"Evidence '{evidenceId}' does not support the requested '{resolved.Status}' claim transition.");
            }
        }

        ValidateSupersession(state, resolved);
    }

    internal static void ValidateDependenciesRepointed(
        GovernedTaskState state,
        LedgerEvent @event,
        ClaimDependenciesRepointed repointed)
    {
        RequireAuthority(state, @event.ActorId, Capability.ResolveClaim);
        var superseded = Get(state.Claims, repointed.SupersededClaimId, "claim");
        var replacement = Get(state.Claims, repointed.ReplacementClaimId, "claim");
        if (superseded.Status != ClaimStatus.Superseded)
        {
            throw new GovernanceException("Dependencies are only re-pointed away from a superseded claim.");
        }

        if (superseded.SupersededByClaimId != replacement.Id)
        {
            throw new GovernanceException("Dependencies can only be re-pointed at the claim that superseded them.");
        }

        // Mirrors ClaimRules.DeriveSupersession: only an earned refinement re-points instead of invalidating.
        if (replacement.Status != ClaimStatus.Validated ||
            state.Evidence.Values.Any(item => item.Refutes.Contains(superseded.Id)))
        {
            throw new GovernanceException(
                "Dependencies are only re-pointed when the replacement is validated and nothing refutes the original.");
        }
    }

    private static void ValidateSupersession(GovernedTaskState state, ClaimResolved resolved)
    {
        if (resolved.Status != ClaimStatus.Superseded)
        {
            if (resolved.SupersededByClaimId is not null || resolved.Outcome is not null)
            {
                throw new GovernanceException(
                    $"A '{resolved.Status}' resolution cannot name a superseding claim or outcome.");
            }

            return;
        }

        if (resolved.SupersededByClaimId is not { } replacementId)
        {
            throw new GovernanceException("Superseding a claim requires naming the claim that replaces it.");
        }

        if (replacementId == resolved.ClaimId)
        {
            throw new GovernanceException("A claim cannot supersede itself.");
        }

        var replacement = Get(state.Claims, replacementId, "claim");
        if (replacement.Status is ClaimStatus.Rejected or ClaimStatus.Superseded)
        {
            throw new GovernanceException(
                $"Replacement claim '{replacementId}' is '{replacement.Status}' and cannot replace another claim.");
        }

        if (resolved.Outcome is not { } outcome)
        {
            throw new GovernanceException("A supersession must record whether it was a refinement or a correction.");
        }

        RequireDefined(outcome, nameof(resolved.Outcome));
        var expected = replacement.Status == ClaimStatus.Validated &&
                       !state.Evidence.Values.Any(item => item.Refutes.Contains(resolved.ClaimId))
            ? SupersessionOutcome.Refinement
            : SupersessionOutcome.Correction;
        if (outcome != expected)
        {
            throw new GovernanceException(
                $"Supersession outcome '{outcome}' does not match the state-derived outcome '{expected}'.");
        }
    }

    private static void EnsureClaimResolution(Claim claim, ClaimStatus target)
    {
        if (target == ClaimStatus.Open)
        {
            throw new GovernanceException("Claim resolution cannot set a claim to open.");
        }

        if (claim.Status is ClaimStatus.Rejected or ClaimStatus.Superseded || claim.Status == target)
        {
            throw new GovernanceException($"Claim in status '{claim.Status}' cannot transition to '{target}'.");
        }
    }
}
