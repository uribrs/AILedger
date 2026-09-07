using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Application;

// Command-time counterparts: ValidateRunStarted and ValidateRunCompleted.
internal static class RunRules
{
    internal static IReadOnlyList<LedgerEventData> StartRun(
        GovernedTaskState state,
        StartRunCommand command,
        DateTimeOffset now)
    {
        RequireId(command.RunId.Value, nameof(command.RunId));
        EnsureNew(state.Runs, command.RunId, "run");
        RequireText(command.Provider, nameof(command.Provider));
    
        // A run is authorised by one actor and worked by another only when an operator dispatches
        // it. Starting the run under the working actor is what made the roles that hold no run
        // authority — researcher, worker, verifier, code reviewer — impossible to launch at all.
        // Dispatch separates the two without handing those roles authority they must not have.
        var subjectActorId = command.SubjectActorId ?? command.ActorId;
        ActorId? launchedBy = subjectActorId == command.ActorId ? null : command.ActorId;
        if (launchedBy is not null)
        {
            if (!RoleAssignmentRules.IsOperator(state, command.ActorId))
            {
                throw new GovernanceException(
                    $"Only an operator can start a run on another actor's behalf; '{command.ActorId}' cannot " +
                    $"dispatch for '{subjectActorId}'.");
            }
    
            if (!state.Roles.ContainsKey(subjectActorId))
            {
                throw new GovernanceException($"Run subject '{subjectActorId}' has no assigned role.");
            }
        }
    
        // Which role the subject holds now, recorded on the run. Role assignments change, so asking
        // the current assignment whether a past run was a verifier's answers a different question.
        var subjectRole = state.Roles[subjectActorId].Role;
    
        if (command.WorkItemId is { } workItemId)
        {
            var workItem = Get(state.WorkItems, workItemId, "work item");
            WorkItemRules.EnsureCanStartWork(state, command.ActorId, workItem);
            ClaimDependencyRules.EnsureDependenciesAreCurrent(state, workItem.DependsOnClaims);
            // Abandoned belongs here for the same reason Completed does, and for one more: starting
            // a run moves the item to Active, which would take back a directory area that has
            // already been handed to another work item.
            if (workItem.Status is WorkItemStatus.Blocked or WorkItemStatus.Stale or
                WorkItemStatus.Completed or WorkItemStatus.Abandoned)
            {
                throw new GovernanceException($"Cannot start a run for work item in status '{workItem.Status}'.");
            }
    
            var hasActiveRun = state.Runs.Values.Any(run =>
                run.WorkItemId == workItemId && run.Status is AgentRunStatus.Active);
            if (hasActiveRun)
            {
                throw new GovernanceException($"Work item '{workItemId}' already has an active orchestration run.");
            }
    
            // A code reviewer reading unverified work reviews something nobody has established
            // is finished, and its findings then compete with the verifier's instead of following
            // them. The ordering is the whole point of having two passes, so a verifier run that
            // ended before the latest work does not open the gate either — it read a different,
            // earlier work item than the one the reviewer would be looking at.
            if (subjectRole == RoleKind.CodeReviewer && !WorkItemRules.HasVerifierRunAfterLatestWork(state, workItemId))
            {
                throw new GovernanceException(
                    $"A code reviewer can only start on work item '{workItemId}' after a verifier run has " +
                    "completed against the latest work done on it.");
            }
        }
    
        var run = new AgentRun(
            command.RunId,
            subjectActorId,
            command.WorkItemId,
            command.Provider.Trim(),
            TrimOrNull(command.ProviderSessionId),
            AgentRunStatus.Active,
            now,
            null,
            TrimOrNull(command.Model),
            TrimOrNull(command.ProviderVersion),
            TrimOrNull(command.LaunchTokenHash),
            launchedBy,
            subjectRole);
        return [new RunStarted(run)];
    }

    internal static IReadOnlyList<LedgerEventData> CompleteRun(
        GovernedTaskState state,
        CompleteRunCommand command,
        DateTimeOffset now)
    {
        var run = Get(state.Runs, command.RunId, "run");
        EnsureCanCompleteRun(state, command.ActorId, run);
        if (run.Status is not AgentRunStatus.Active)
        {
            throw new GovernanceException("Only an active run can be completed.");
        }
    
        if (command.Status is AgentRunStatus.Active)
        {
            throw new GovernanceException("Run completion requires a terminal status.");
        }
    
        var providerSessionId = TrimOrNull(command.ProviderSessionId) ?? run.ProviderSessionId;
        if (run.ProviderSessionId is not null && providerSessionId != run.ProviderSessionId)
        {
            throw new GovernanceException("Run completion session identity does not match the started run.");
        }
    
        // A run recorded as completed is a run someone may later resume, and resume requires the
        // exact provider session. The identity lives only in the adapter until completion records
        // it, so a completion without one loses it silently. Terminal failures are exempt: a run
        // that died before its session existed genuinely has no identity to record.
        if (command.Status is AgentRunStatus.Completed && providerSessionId is null)
        {
            throw new GovernanceException(
                $"Run '{command.RunId}' cannot be recorded as completed without a provider session identity; " +
                "a completed run must stay resumable.");
        }
    
        if (command.Status == AgentRunStatus.Completed && run.SubjectRole is RoleKind.Verifier or RoleKind.CodeReviewer)
        {
            var requiredKind = run.SubjectRole == RoleKind.Verifier
                ? GovernedArtifactKind.VerifierOutput
                : GovernedArtifactKind.CodeReviewOutput;
            if (!ArtifactRules.CurrentArtifacts(state).Any(artifact =>
                    artifact.Kind == requiredKind && artifact.ProducerRunId == run.Id))
            {
                throw new GovernanceException(
                    $"Completing {run.SubjectRole} run '{run.Id}' requires its matching '{requiredKind}' artifact.");
            }
        }
    
        var launcherAuthorized = EnsureLauncherAuthorizedCompletion(state, command, run);
        return [new RunCompleted(command.RunId, command.Status, providerSessionId, now, launcherAuthorized)];
    }

    private static bool EnsureLauncherAuthorizedCompletion(
        GovernedTaskState state,
        CompleteRunCommand command,
        AgentRun run)
    {
        if (run.LaunchTokenHash is not { } expectedHash)
        {
            return false;
        }
    
        if (TrimOrNull(command.LaunchToken) is { } token &&
            string.Equals(HashLaunchToken(token), expectedHash, StringComparison.Ordinal))
        {
            return true;
        }
    
        // Escape hatch for an orphan: the launching process died and its secret went with it. An
        // operator may close the run, but only as a failure — a run nobody watched finish must
        // never be laundered into a success.
        if (RoleAssignmentRules.IsOperator(state, command.ActorId) &&
            command.Status is AgentRunStatus.Failed or AgentRunStatus.Cancelled)
        {
            return false;
        }
    
        throw new GovernanceException(
            $"Run '{run.Id}' was started by a provider launch and is closed by that launcher. " +
            "An agent running inside it cannot complete it. An operator may close an orphaned run " +
            "as failed or cancelled.");
    }

    internal static string HashLaunchToken(string token) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(token)));

    private static void EnsureCanCompleteRun(
        GovernedTaskState state,
        ActorId actorId,
        AgentRun run)
    {
        if (run.ActorId == actorId || RoleAssignmentRules.IsOperator(state, actorId))
        {
            return;
        }
    
        throw new GovernanceException($"Only run actor '{run.ActorId}' or an operator can complete run '{run.Id}'.");
    }
}
