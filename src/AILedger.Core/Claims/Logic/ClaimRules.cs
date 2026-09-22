using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.Lessons;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Claims;

// Replay counterparts: ClaimEventValidator.ValidateAdded, ClaimEventValidator.ValidateResolved,
// ClaimEventValidator.ValidateDependenciesRepointed.
internal static class ClaimRules
{
    internal static IReadOnlyList<LedgerEventData> AddClaim(
        GovernedTaskState state,
        AddClaimCommand command,
        DateTimeOffset now)
    {
        RequireId(command.ClaimId.Value, nameof(command.ClaimId));
        EnsureNew(state.Claims, command.ClaimId, "claim");
        RequireText(command.Statement, nameof(command.Statement));
        LessonCitationRules.EnsureCitedLessonWasRecalled(state, command.FromLesson);

        var claim = new Claim(
            command.ClaimId,
            command.Statement.Trim(),
            ClaimStatus.Open,
            [],
            TrimOrNull(command.ConsequenceIfWrong),
            new Provenance(command.ActorId, now, "claim.add"),
            null,
            command.FromLesson);
        return [new ClaimAdded(claim)];
    }

    internal static IReadOnlyList<LedgerEventData> ResolveClaim(
        GovernedTaskState state,
        ResolveClaimCommand command)
    {
        var claim = Get(state.Claims, command.ClaimId, "claim");
        EnsureClaimResolution(claim, command.Status);
        EnsureUnique(command.EvidenceIds, "Evidence IDs");
        EnsureReferencesExist(state.Evidence, command.EvidenceIds, "evidence");

        if (command.Status is ClaimStatus.Validated or ClaimStatus.Rejected && command.EvidenceIds.Count == 0)
        {
            throw new GovernanceException($"A {command.Status.ToString().ToLowerInvariant()} claim requires evidence.");
        }

        foreach (var evidenceId in command.EvidenceIds)
        {
            var evidence = state.Evidence[evidenceId];
            var hasRequiredDirection = command.Status switch
            {
                ClaimStatus.Validated => evidence.Supports.Contains(command.ClaimId),
                ClaimStatus.Rejected => evidence.Refutes.Contains(command.ClaimId),
                _ => true
            };

            if (!hasRequiredDirection)
            {
                throw new GovernanceException(
                    $"Evidence '{evidenceId}' does not {EvidenceDirection(command.Status)} claim '{command.ClaimId}'." +
                    DirectionalContext(state, command.Status, evidence, command.ClaimId));
            }
        }

        var replacement = EnsureSupersessionReplacement(state, command);
        var outcome = replacement is null ? (SupersessionOutcome?)null : DeriveSupersession(state, claim, replacement);

        var events = new List<LedgerEventData>
        {
            new ClaimResolved(command.ClaimId, command.Status, command.EvidenceIds.ToArray(),
                command.SupersededByClaimId, outcome)
        };

        if (outcome == SupersessionOutcome.Refinement)
        {
            // The replacement was validated and nothing refutes the original, so dependent work
            // is still sound. Move it onto the replacement rather than invalidating it.
            events.Add(new ClaimDependenciesRepointed(command.ClaimId, replacement!.Id));
        }
        else if (command.Status is ClaimStatus.Rejected or ClaimStatus.Superseded)
        {
            ClaimDependencyRules.AddDependencyInvalidations(state, command.ClaimId, events);
        }

        return events;
    }

    private static Claim? EnsureSupersessionReplacement(GovernedTaskState state, ResolveClaimCommand command)
    {
        if (command.Status != ClaimStatus.Superseded)
        {
            if (command.SupersededByClaimId is not null)
            {
                throw new GovernanceException(
                    $"A '{command.Status}' resolution cannot name a superseding claim.");
            }

            return null;
        }

        if (command.SupersededByClaimId is not { } replacementId)
        {
            throw new GovernanceException("Superseding a claim requires naming the claim that replaces it.");
        }

        if (replacementId == command.ClaimId)
        {
            throw new GovernanceException("A claim cannot supersede itself.");
        }

        var replacement = Get(state.Claims, replacementId, "claim");
        if (replacement.Status is ClaimStatus.Rejected or ClaimStatus.Superseded)
        {
            throw new GovernanceException(
                $"Replacement claim '{replacementId}' is '{replacement.Status}' and cannot replace another claim.");
        }

        return replacement;
    }

    // The actor resolving a claim is usually an agent, so this is derived from state and never
    // declared. Correction is the default; refinement has to be earned.
    private static SupersessionOutcome DeriveSupersession(
        GovernedTaskState state,
        Claim superseded,
        Claim replacement)
    {
        var replacementIsValidated = replacement.Status == ClaimStatus.Validated;
        var somethingRefutesTheOriginal = state.Evidence.Values.Any(item => item.Refutes.Contains(superseded.Id));
        return replacementIsValidated && !somethingRefutesTheOriginal
            ? SupersessionOutcome.Refinement
            : SupersessionOutcome.Correction;
    }

    private static string EvidenceDirection(ClaimStatus status) => status switch
    {
        ClaimStatus.Validated => "support",
        ClaimStatus.Rejected => "refute",
        _ => "relate to"
    };

    // What the two records actually point at, appended to the refusal that says they do not point at
    // each other. On one task sixteen of thirty-two refusals were this rule, arriving in paired
    // rounds over eight claims, and every one of them said the pair was wrong while none said the
    // evidence pointed at nothing — which was the single cause behind all eight. Both lines are
    // in-memory lookups: the selected evidence is already held and state.Evidence is already
    // materialised, so the refusal path reads no file and refusals.jsonl feeds nothing here.
    //
    // Both lines name both directions, always, with the direction the command asked for first. A
    // first revision printed only the requested direction, which rendered evidence attached the
    // wrong way round byte-for-byte identically to evidence attached to nothing — the one shape the
    // caller most needs told apart from the other.
    private static string DirectionalContext(
        GovernedTaskState state,
        ClaimStatus status,
        Evidence evidence,
        ClaimId claimId)
    {
        var requested = status == ClaimStatus.Rejected ? ClaimStatus.Rejected : ClaimStatus.Validated;
        var opposite = requested == ClaimStatus.Rejected ? ClaimStatus.Validated : ClaimStatus.Rejected;

        return $"{Environment.NewLine}  {evidence.Id.Value} " +
               $"{PointsAt(evidence, requested)}, {PointsAt(evidence, opposite)}" +
               $"{Environment.NewLine}  {claimId.Value} is " +
               $"{PointedAtBy(state, claimId, requested)}, {PointedAtBy(state, claimId, opposite)}";
    }

    private static string PointsAt(Evidence evidence, ClaimStatus direction) =>
        $"{(direction == ClaimStatus.Rejected ? "refutes" : "supports")}: " +
        Names(DirectedClaims(evidence, direction).Select(id => id.Value));

    private static string PointedAtBy(GovernedTaskState state, ClaimId claimId, ClaimStatus direction) =>
        $"{(direction == ClaimStatus.Rejected ? "refuted by" : "supported by")}: " +
        Names(state.Evidence.Values
            .Where(candidate => DirectedClaims(candidate, direction).Contains(claimId))
            .Select(candidate => candidate.Id.Value));

    private static IReadOnlyList<ClaimId> DirectedClaims(Evidence evidence, ClaimStatus status) =>
        status == ClaimStatus.Rejected ? evidence.Refutes : evidence.Supports;

    // Ordinal, so two runs of the same refusal read the same way.
    private static string Names(IEnumerable<string> ids)
    {
        var ordered = ids.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        return ordered.Length == 0 ? "(none)" : string.Join(", ", ordered);
    }

    private static void EnsureClaimResolution(Claim claim, ClaimStatus target)
    {
        if (target == ClaimStatus.Open)
        {
            throw new GovernanceException("Claim resolution cannot set a claim to open.");
        }

        if (claim.Status is ClaimStatus.Rejected or ClaimStatus.Superseded)
        {
            throw new GovernanceException($"Claim in terminal status '{claim.Status}' cannot be resolved again.");
        }

        if (claim.Status == target)
        {
            throw new GovernanceException($"Claim is already '{target}'.");
        }
    }
}
