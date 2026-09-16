using AILedger.Core.Claims;
using AILedger.Core.Contracts;
using AILedger.Core.CoordinatorSessions;
using AILedger.Core.Domain;
using AILedger.Core.Runs;
using AILedger.Core.WorkItems;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Application;

/// <summary>Read-only start admission, shared by durable start and full provider preflight.</summary>
/// <remarks>Call after command authorization and entry-stage checks. Emits no events or waivers.</remarks>
public static class RunAdmission
{
    public static void EnsurePermitted(GovernedTaskState state, StartRunCommand command)
    {
        if (command.Assurance is not null)
        {
            _ = RunDispatchRules.EnsurePermitted(state, command.ActorId, command.SubjectActorId,
                command.SkillsServedNow, command.LaunchTokenHash is not null, command.WorkItemId,
                command.WithoutBriefReason, command.StaleBriefEvidenceId, authorityAndBriefOnly: true);
            RequireId(command.RunId.Value, nameof(command.RunId));
            EnsureNew(state.Runs, command.RunId, "run");
            RequireText(command.Provider, nameof(command.Provider));
            AssuranceRules.EnsureStart(state, command.ActorId,
                state.Roles[command.SubjectActorId ?? command.ActorId].Role, command.WorkItemId,
                command.Assurance, command.Provider.Trim(), command.ProviderSessionId);
            CoordinatorSessionDispatchRules.EnsureUsable(state, command.ActorId, command.CoordinatorSessionId);
            return;
        }
        RequireId(command.RunId.Value, nameof(command.RunId));
        EnsureNew(state.Runs, command.RunId, "run");
        RequireText(command.Provider, nameof(command.Provider));

        var subjectActorId = command.SubjectActorId ?? command.ActorId;
        _ = RunDispatchRules.EnsurePermitted(
            state, command.ActorId, command.SubjectActorId, command.SkillsServedNow,
            isProviderLaunch: command.LaunchTokenHash is not null,
            command.WorkItemId,
            command.WithoutBriefReason, command.StaleBriefEvidenceId);
        // Checked only when the run names a session, which no run recorded before sessions existed
        // does. A run that names none is dispatched outside a bracket, and that is legal (D7).
        CoordinatorSessionDispatchRules.EnsureUsable(
            state, command.ActorId, command.CoordinatorSessionId);

        // Which role the subject holds now, recorded on the run. Role assignments change, so asking
        // the current assignment whether a past run was a verifier's answers a different question.
        var subjectRole = state.Roles[subjectActorId].Role;

        if (command.WorkItemId is { } workItemId)
        {
            var workItem = Get(state.WorkItems, workItemId, "work item");
            WorkItemLifecycleRules.EnsureCanStart(state, command.ActorId, workItem);
            ClaimDependencyRules.EnsureDependenciesAreCurrent(state, workItem.DependsOnClaims);
            // The status refusal that used to stand here — Blocked, Stale, Completed or Abandoned,
            // Abandoned for the same reason Completed is and for one more: starting a run moves the
            // item to Active, which would take back a directory area already handed to another work
            // item — now lives in RunDispatchRules.EnsurePermitted above, ahead of the coordinating-role
            // refusal it has to precede. It therefore speaks before the owner and dependency checks
            // above rather than after them, and is not restated here: two copies of one rule is the
            // defect KC1 was.

            var hasActiveRun = state.Runs.Values.Any(run =>
                WorkCoverage.Effective(run.WorkItemId, run.Assurance).Contains(workItemId) && run.Status is AgentRunStatus.Active);
            if (hasActiveRun)
            {
                throw new GovernanceException($"Work item '{workItemId}' already has an active orchestration run.");
            }

            // The coordinating-role refusal that used to stand here now lives in
            // RunDispatchRules.EnsurePermitted above, so a provider launch reaches it before it has spawned
            // a process. It therefore speaks before the status and active-run checks above rather
            // than after them, which is stated at its new site.

            // A code reviewer reading unverified work reviews something nobody has established
            // is finished, and its findings then compete with the verifier's instead of following
            // them. The ordering is the whole point of having two passes, so a verifier run that
            // ended before the latest work does not open the gate either — it read a different,
            // earlier work item than the one the reviewer would be looking at.
            if (subjectRole == RoleKind.CodeReviewer &&
                !WorkItemVerificationRules.HasVerifierRunAfterLatestWork(state, workItemId))
            {
                throw new GovernanceException(
                    $"A code reviewer can only start on work item '{workItemId}' after a verifier run has " +
                    "completed against the latest work done on it.");
            }
            if (subjectRole is RoleKind.Verifier or RoleKind.CodeReviewer && AssuranceRules.HasNewAssurance(state, workItemId))
                throw new GovernanceException($"Assurance member '{workItemId}': subsequent assurance requires --candidate.");
        }

    }
}
