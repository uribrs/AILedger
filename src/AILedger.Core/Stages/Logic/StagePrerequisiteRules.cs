using AILedger.Core.Artifacts;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using AILedger.Core.WorkItems;

namespace AILedger.Core.Stages;

// Command-time methodology arms. Replay deliberately owns a smaller, compatibility-safe set.
internal static class StagePrerequisiteRules
{
    internal static void EnsureSatisfied(
        GovernedTaskState state,
        TaskStage target,
        AlternativeId? serialJustification)
    {
        var currentArtifacts = ArtifactRevisionRules.Current(state);
        var completedRoles = CompletedRoles(state);
        var isBackward = StageTransitionPolicy.IsBackward(state.Stage, target);

        switch (target)
        {
            case TaskStage.Discovery:
                break;
            case TaskStage.Research:
                if (!isBackward)
                    EnsureResearch(state);
                break;
            case TaskStage.Design:
                // R2 (stale-claim-certification): backward entry opens the replanning workspace.
                if (!isBackward)
                    EnsureDesign(state);
                break;
            case TaskStage.Scope:
                // R1 (both-research-arms): recovery must satisfy full readiness before moving on.
                if (state.Stage == TaskStage.Design)
                {
                    EnsureDesign(state);
                    EnsureReconsiderationConsulted(state);
                }
                EnsureCurrentArtifact(currentArtifacts, target, GovernedArtifactKind.PromptContract);
                break;
            case TaskStage.Ready:
                EnsureReady(state, currentArtifacts);
                break;
            case TaskStage.Execution:
                EnsureExecution(state, currentArtifacts);
                break;
            case TaskStage.Verification:
                EnsureVerification(state, completedRoles, serialJustification);
                break;
            case TaskStage.Repair:
                EnsureRepair(state, currentArtifacts);
                break;
            case TaskStage.Review:
                EnsureCompletedRole(completedRoles, RoleKind.Verifier, "Review");
                break;
            case TaskStage.Learn:
                EnsureLearn(state, completedRoles);
                break;
            case TaskStage.Archive:
                EnsureArchive(state);
                break;
            default:
                throw new GovernanceException($"Unsupported target stage '{target}'.");
        }
    }

    // DidWork rather than a bare Completed check: a run declaring AgentRun.NoProvider spawns no
    // provider and staffs no role. Two commands must not stand in for a Researcher or Worker run.
    private static HashSet<RoleKind> CompletedRoles(GovernedTaskState state) =>
        state.Runs.Values
            .Where(run => WorkItemVerificationRules.DidWork(run) && run.SubjectRole is not null)
            .Select(run => run.SubjectRole!.Value)
            .ToHashSet();

    private static void EnsureResearch(GovernedTaskState state)
    {
        if (!state.Claims.Values.Any(claim => claim.Status == ClaimStatus.Open))
        {
            throw new GovernanceException("Research requires at least one open claim to investigate.");
        }
    }

    private static void EnsureDesign(GovernedTaskState state)
    {
        // R1 (both-research-arms): external research supplements current recon.
        EnsureResearchConsulted(state, InternalReconRules.EnsureDesign(state));
        if (state.Alternatives.Count == 0 &&
            !state.Decisions.Values.Any(decision => decision.Status == DecisionStatus.Accepted))
        {
            throw new GovernanceException(
                "Design requires at least one recorded alternative or accepted decision.");
        }
    }

    // A2 (research-consultation-arm). Stricter than the completed-Researcher check it replaces: each
    // external claim must be named by a research consultation from the current research episode,
    // made by a Researcher run that completed with real cognition. An old consultation about other
    // claims, or from before the latest entry into Research, does not count (K9, PALT10).
    private static void EnsureResearchConsulted(GovernedTaskState state, IReadOnlyList<ClaimId> externalClaims)
    {
        var episodeStart = state.ResearchOpenedAtVersion ?? 0;
        foreach (var claim in externalClaims)
        {
            var consulted = state.LessonConsultations.Any(consultation =>
                consultation.Purpose == LessonConsultationPurpose.Research &&
                consultation.Version > episodeStart &&
                consultation.ClaimIds.Contains(claim) &&
                state.Runs.TryGetValue(consultation.RunId, out var run) &&
                WorkItemVerificationRules.DidWork(run) &&
                run.SubjectRole == RoleKind.Researcher);
            if (!consulted)
            {
                throw new GovernanceException(
                    $"Design requires a research lesson consultation naming external claim '{claim}' " +
                    "by a completed Researcher run in the current research episode.");
            }
        }
    }

    // A3 (reconsideration-consultation-arm). Only after a return from a stage after Design into
    // Design or Research (PD9); one consultation after that return covers the rest of the episode.
    // Like A2, it counts only from a run that completed with real cognition (PD10): a provider-none,
    // failed, cancelled or still-active run has not reassessed the served lessons.
    private static void EnsureReconsiderationConsulted(GovernedTaskState state)
    {
        if (state.ReconsiderationOpenedAtVersion is not { } opened)
        {
            return;
        }
        if (!state.LessonConsultations.Any(consultation =>
                consultation.Purpose == LessonConsultationPurpose.Reconsideration &&
                consultation.Version > opened &&
                state.Runs.TryGetValue(consultation.RunId, out var run) &&
                WorkItemVerificationRules.DidWork(run)))
        {
            throw new GovernanceException(
                "Scope after replanning requires a reconsideration lesson consultation by a completed lead run " +
                "with real cognition, recorded after the latest return from a later stage into Design or Research.");
        }
    }

    private static void EnsureReady(
        GovernedTaskState state,
        IReadOnlyList<GovernedArtifact> currentArtifacts)
    {
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
            throw new GovernanceException("Ready requires a CodeReviewer assigned to a distinct actor.");
        }
    }

    private static void EnsureExecution(
        GovernedTaskState state,
        IReadOnlyList<GovernedArtifact> currentArtifacts)
    {
        if (state.WorkItems.Count == 0)
        {
            throw new GovernanceException("Execution requires at least one governed work item.");
        }

        var currentKinds = currentArtifacts.Select(item => item.Kind).ToHashSet();
        var missing = new[]
            {
                GovernedArtifactKind.UserRequest,
                GovernedArtifactKind.PromptContract,
                GovernedArtifactKind.OrchestrationPlan
            }
            .Where(kind => !currentKinds.Contains(kind))
            .ToArray();
        if (missing.Length != 0)
        {
            throw new GovernanceException(
                $"Execution requires current governed artifacts: {string.Join(", ", missing)}.");
        }
    }

    private static void EnsureVerification(
        GovernedTaskState state,
        IReadOnlySet<RoleKind> completedRoles,
        AlternativeId? serialJustification)
    {
        if (!completedRoles.Contains(RoleKind.Worker))
        {
            throw new GovernanceException(
                "Verification requires a completed Worker run. A coordinating role's run does " +
                "not satisfy it; dispatch a Worker against the work item.");
        }

        StageSerialExecutionRules.EnsureExplained(state, serialJustification);
    }

    private static void EnsureRepair(
        GovernedTaskState state,
        IReadOnlyList<GovernedArtifact> currentArtifacts)
    {
        if (!state.Challenges.Values.Any(challenge => challenge.Status == ChallengeStatus.Open) &&
            !currentArtifacts.Any(artifact => artifact.Kind == GovernedArtifactKind.VerifierOutput))
        {
            throw new GovernanceException(
                "Repair requires an open challenge or current VerifierOutput artifact.");
        }
    }

    private static void EnsureLearn(GovernedTaskState state, IReadOnlySet<RoleKind> completedRoles)
    {
        var codeBearing = state.WorkItems.Values.Any(item => item.ResourceScope.Count != 0);
        if (codeBearing && !completedRoles.Contains(RoleKind.CodeReviewer))
        {
            throw new GovernanceException(
                "Learn requires a completed CodeReviewer run for code-bearing work.");
        }
    }

    private static void EnsureArchive(GovernedTaskState state)
    {
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
    }

    private static void EnsureCompletedRole(
        IReadOnlySet<RoleKind> completedRoles,
        RoleKind role,
        string stage)
    {
        if (!completedRoles.Contains(role))
        {
            throw new GovernanceException($"{stage} requires a completed {role} run.");
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
