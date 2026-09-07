using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Application;

// Command-time counterpart: TaskTransitionValidator.ValidateStageTransitioned.
internal static class StageTransitionRules
{
    internal static IReadOnlyList<LedgerEventData> TransitionStage(
        GovernedTaskState state,
        RequestStageTransitionCommand command,
        DateTimeOffset now)
    {
        StageTransitionPolicy.EnsureAllowed(state.Stage, command.TargetStage);
        var waiver = TrimOrNull(command.WithoutPrerequisitesReason);
        if (command.WithoutPrerequisitesReason is not null && waiver is null)
        {
            throw new GovernanceException(
                "Waiving stage prerequisites needs a reason; a blank waiver records nothing.");
        }

        if (waiver is null)
        {
            EnsureStagePrerequisites(state, command.TargetStage);
        }
        else if (!RoleAssignmentRules.IsOperator(state, command.ActorId))
        {
            throw new GovernanceException("Only an operator can transition stages without prerequisites.");
        }

        var transition = new StageTransitioned(state.Stage, command.TargetStage);
        if (command.TargetStage != TaskStage.Archive)
        {
            return waiver is null
                ? [transition]
                : [new StagePrerequisitesWaived(command.TargetStage, waiver), transition];
        }
    
        var lessons = LessonRules.MintLessons(state, command.ActorId, now);
        if (lessons.Count == 0)
        {
            throw new GovernanceException(
                "Archiving requires at least one eligible lesson-bearing mark on a validated claim, " +
                "rejected alternative, or resolved escalation.");
        }
    
        var events = lessons.Select<Lesson, LedgerEventData>(lesson => new LessonMinted(lesson));
        if (waiver is not null)
        {
            events = events.Append(new StagePrerequisitesWaived(command.TargetStage, waiver));
        }

        return events.Append(transition).ToArray();
    }

    private static void EnsureStagePrerequisites(GovernedTaskState state, TaskStage target)
    {
        var currentArtifacts = ArtifactRules.CurrentArtifacts(state);
        var completedRoles = state.Runs.Values
            .Where(run => run.Status == AgentRunStatus.Completed && run.SubjectRole is not null)
            .Select(run => run.SubjectRole!.Value)
            .ToHashSet();
        var codeBearing = state.WorkItems.Values.Any(item => item.ResourceScope.Count != 0);
    
        switch (target)
        {
            case TaskStage.Discovery:
                break;
    
            case TaskStage.Research:
                if (!state.Claims.Values.Any(claim => claim.Status == ClaimStatus.Open))
                {
                    throw new GovernanceException("Research requires at least one open claim to investigate.");
                }
                break;
    
            case TaskStage.Design:
                if (!completedRoles.Contains(RoleKind.Researcher))
                {
                    throw new GovernanceException("Design requires a completed Researcher run.");
                }
                if (state.Alternatives.Count == 0 &&
                    !state.Decisions.Values.Any(decision => decision.Status == DecisionStatus.Accepted))
                {
                    throw new GovernanceException(
                        "Design requires at least one recorded alternative or accepted decision.");
                }
                break;
    
            case TaskStage.Scope:
                EnsureCurrentArtifact(currentArtifacts, TaskStage.Scope, GovernedArtifactKind.PromptContract);
                break;
    
            case TaskStage.Ready:
                EnsureCurrentArtifact(currentArtifacts, TaskStage.Ready, GovernedArtifactKind.OrchestrationPlan);
                var assignedRoles = state.Roles.Values.Select(assignment => assignment.Role).ToHashSet();
                if (!assignedRoles.Any(IsWorkingRole))
                {
                    throw new GovernanceException("Ready requires a working role assigned to a distinct actor.");
                }
                if (!assignedRoles.Contains(RoleKind.Verifier))
                {
                    throw new GovernanceException("Ready requires a Verifier assigned to a distinct actor.");
                }
                if (!assignedRoles.Contains(RoleKind.CodeReviewer))
                {
                    throw new GovernanceException(
                        "Ready requires a CodeReviewer assigned to a distinct actor.");
                }
                break;
    
            case TaskStage.Execution:
                if (state.WorkItems.Count == 0)
                {
                    throw new GovernanceException("Execution requires at least one governed work item.");
                }
                var currentKinds = currentArtifacts.Select(item => item.Kind).ToHashSet();
                var required = new[]
                {
                    GovernedArtifactKind.UserRequest,
                    GovernedArtifactKind.PromptContract,
                    GovernedArtifactKind.OrchestrationPlan
                };
                var missing = required.Where(kind => !currentKinds.Contains(kind)).ToArray();
                if (missing.Length != 0)
                {
                    throw new GovernanceException(
                        $"Execution requires current governed artifacts: {string.Join(", ", missing)}.");
                }
                break;
    
            case TaskStage.Verification:
                if (!completedRoles.Any(role =>
                        role is RoleKind.Worker or RoleKind.ImplementationLead))
                {
                    throw new GovernanceException(
                        "Verification requires a completed Worker or ImplementationLead run.");
                }
                break;
    
            case TaskStage.Repair:
                if (!state.Challenges.Values.Any(challenge => challenge.Status == ChallengeStatus.Open) &&
                    !currentArtifacts.Any(artifact => artifact.Kind == GovernedArtifactKind.VerifierOutput))
                {
                    throw new GovernanceException(
                        "Repair requires an open challenge or current VerifierOutput artifact.");
                }
                break;
    
            case TaskStage.Review:
                if (!completedRoles.Contains(RoleKind.Verifier))
                {
                    throw new GovernanceException("Review requires a completed Verifier run.");
                }
                break;
    
            case TaskStage.Learn:
                if (codeBearing && !completedRoles.Contains(RoleKind.CodeReviewer))
                {
                    throw new GovernanceException(
                        "Learn requires a completed CodeReviewer run for code-bearing work.");
                }
                break;
    
            case TaskStage.Archive:
                if (state.Runs.Values.Any(run => run.Status is AgentRunStatus.Active))
                {
                    throw new GovernanceException("A task with active runs cannot be archived.");
                }
                if (state.Challenges.Values.Any(challenge => challenge.Status == ChallengeStatus.Open))
                {
                    throw new GovernanceException("A task with open challenges cannot be archived.");
                }
                if (state.LessonMarks.Count == 0)
                {
                    throw new GovernanceException("Archive requires at least one lesson-bearing mark.");
                }
                break;
    
            default:
                throw new GovernanceException($"Unsupported target stage '{target}'.");
        }
    }

    private static bool IsWorkingRole(RoleKind role) =>
        role is not (RoleKind.Verifier or RoleKind.CodeReviewer);

    private static void EnsureCurrentArtifact(
        IReadOnlyList<GovernedArtifact> currentArtifacts,
        TaskStage target,
        GovernedArtifactKind requiredKind)
    {
        if (!currentArtifacts.Any(artifact => artifact.Kind == requiredKind))
        {
            throw new GovernanceException($"{target} requires a current {requiredKind} artifact.");
        }
    }
}
