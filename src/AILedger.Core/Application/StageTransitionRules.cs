using AILedger.Core.Contracts;
using AILedger.Core.CoordinatorSessions;
using AILedger.Core.Artifacts;
using AILedger.Core.Domain;
using AILedger.Core.WorkItems;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Application;

// Replay counterpart: TaskTransitionValidator.ValidateStageTransitioned.
internal static class StageTransitionRules
{
    internal static IReadOnlyList<LedgerEventData> TransitionStage(
        GovernedTaskState state,
        RequestStageTransitionCommand command,
        DateTimeOffset now)
    {
        StageTransitionPolicy.EnsureAllowed(state.Stage, command.TargetStage);

        // A move that goes back in the pipeline says something was learned that invalidates work
        // already done, and that sentence is worth more than the arrow. A forward move refuses a
        // reason rather than dropping it: a caller who passed one meant it to be recorded, and a
        // silently ignored reason reads in the log exactly like one that was never written. Both
        // directions therefore test the raw value, not the trimmed one: a blank reason trims to null,
        // so a branch reading `reason` cannot tell a caller who passed "   " from one who passed
        // nothing, and the forward branch let that caller through for as long as it read the trimmed
        // value. `reason` is what gets recorded; `command.Reason` is what says the caller passed one.
        var reason = TrimOrNull(command.Reason);
        if (StageTransitionPolicy.IsBackward(state.Stage, command.TargetStage))
        {
            if (command.Reason is null)
            {
                throw new GovernanceException(
                    $"Transition from '{state.Stage}' to '{command.TargetStage}' goes back in the pipeline " +
                    "and needs a reason. Pass --reason to record what was learned that sends the work back.");
            }

            if (reason is null)
            {
                throw new GovernanceException(
                    "A backward transition needs a reason; a blank reason records nothing.");
            }
        }
        else if (command.Reason is not null)
        {
            throw new GovernanceException(
                $"Transition from '{state.Stage}' to '{command.TargetStage}' goes forward and does not " +
                "take a reason. Only a transition that goes back records why.");
        }

        if (command.SerialJustification is not null && command.TargetStage != TaskStage.Verification)
        {
            throw new GovernanceException(
                $"Transition from '{state.Stage}' to '{command.TargetStage}' cannot record a serial " +
                "justification. Serial justification applies only when entering 'Verification'.");
        }

        var waiver = TrimOrNull(command.WithoutPrerequisitesReason);
        if (command.WithoutPrerequisitesReason is not null && waiver is null)
        {
            throw new GovernanceException(
                "Waiving stage prerequisites needs a reason; a blank waiver records nothing.");
        }

        // Validated here rather than inside the arm, beside the reason and the waiver, because the
        // three are the same kind of thing: what the caller passed to be recorded. A caller who
        // names an alternative that does not exist has recorded nothing, and that is worth refusing
        // whether or not the arm below would have fired — including under a waiver, which skips the
        // arm but still carries this id onto the event. Existence only, exactly as
        // WorkItemLifecycleRules.Add checks its NotSplitJustification: the kernel cannot judge
        // whether the alternative says anything true, only that it is on the record.
        if (command.SerialJustification is { } serialJustification)
        {
            _ = Get(state.Alternatives, serialJustification, "alternative");
        }

        if (waiver is null)
        {
            EnsureStagePrerequisites(state, command.TargetStage, command.SerialJustification);
        }
        else if (!RoleAssignmentRules.IsOperator(state, command.ActorId))
        {
            throw new GovernanceException("Only an operator can transition stages without prerequisites.");
        }

        var transition = new StageTransitioned(
            state.Stage, command.TargetStage, reason, command.SerialJustification);
        var provenance = waiver is null
            ? null
            : CoordinatorSessionWaiverAttribution.Resolve(state, command.ActorId);
        if (command.TargetStage != TaskStage.Archive)
        {
            return waiver is null
                ? [transition]
                : [new StagePrerequisitesWaived(command.TargetStage, waiver, provenance), transition];
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
            events = events.Append(new StagePrerequisitesWaived(command.TargetStage, waiver, provenance));
        }

        return events.Append(transition).ToArray();
    }

    private static void EnsureStagePrerequisites(
        GovernedTaskState state,
        TaskStage target,
        AlternativeId? serialJustification)
    {
        var currentArtifacts = ArtifactRevisionRules.Current(state);
        // WorkItemVerificationRules.DidWork, not a bare Completed check: a run declaring AgentRun.NoProvider
        // spawns no provider, so it staffs no role. Without this, two commands stand in for a
        // Researcher run and the Design arm passes on a role nobody held.
        var completedRoles = state.Runs.Values
            .Where(run => WorkItemVerificationRules.DidWork(run) && run.SubjectRole is not null)
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
                // Exact, like its three neighbours: Design demands a Researcher, Review a Verifier,
                // Learn a CodeReviewer. Verification was the only arm carrying a disjunction, and so
                // the only one a coordinating role could satisfy on its own run — a lead dispatching
                // the work counted as having done it, and nothing was ever staffed.
                if (!completedRoles.Contains(RoleKind.Worker))
                {
                    throw new GovernanceException(
                        "Verification requires a completed Worker run. A coordinating role's run does " +
                        "not satisfy it; dispatch a Worker against the work item.");
                }
                EnsureSerializableFanoutIsExplained(state, serialJustification);
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

    private static void EnsureSerializableFanoutIsExplained(
        GovernedTaskState state,
        AlternativeId? serialJustification)
    {
        var workedItems = state.WorkItems.Values
            .Where(item => WorkItemVerificationRules.HasCompletedWorkingRun(state, item.Id))
            .ToArray();
        if (workedItems.Length < 2 || !ArePairwiseDisjoint(workedItems))
        {
            return;
        }

        var workingRuns = state.Runs.Values
            .Where(WorkItemVerificationRules.DidWorkUnderAWorkingRole)
            .Where(run => run.WorkItemId is not null)
            .ToArray();
        if (HasCrossItemOverlap(workingRuns) || serialJustification is not null)
        {
            return;
        }

        throw new GovernanceException(
            "Verification follows two or more disjoint work items whose working runs were entirely " +
            "serial. Record an alternative explaining why they were not dispatched concurrently, " +
            "then pass its ID as the serial justification.");
    }

    private static bool ArePairwiseDisjoint(IReadOnlyList<WorkItem> items)
    {
        for (var first = 0; first < items.Count; first++)
        {
            for (var second = first + 1; second < items.Count; second++)
            {
                if (!WorkItemScopeRules.AreDisjoint(items[first], items[second]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool HasCrossItemOverlap(IReadOnlyList<AgentRun> runs)
    {
        for (var first = 0; first < runs.Count; first++)
        {
            for (var second = first + 1; second < runs.Count; second++)
            {
                if (runs[first].WorkItemId == runs[second].WorkItemId)
                {
                    continue;
                }

                if (runs[first].StartedAt < runs[second].EndedAt &&
                    runs[second].StartedAt < runs[first].EndedAt)
                {
                    return true;
                }
            }
        }

        return false;
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
