using AILedger.Core.Application;
using AILedger.Core.Artifacts;
using AILedger.Core.Claims;
using AILedger.Core.Contracts;
using AILedger.Core.CoordinatorSessions;
using AILedger.Core.Domain;
using AILedger.Core.WorkItems;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Runs;

// Orchestrates run start and completion after its collaborators have admitted each transition.
internal static class RunLifecycleRules
{
    internal static IReadOnlyList<LedgerEventData> Start(
        GovernedTaskState state,
        StartRunCommand command,
        DateTimeOffset now)
    {
        RequireId(command.RunId.Value, nameof(command.RunId));
        EnsureNew(state.Runs, command.RunId, "run");
        RequireText(command.Provider, nameof(command.Provider));

        var subjectActorId = command.SubjectActorId ?? command.ActorId;
        ActorId? launchedBy = subjectActorId == command.ActorId ? null : command.ActorId;
        var briefWaiver = RunDispatchRules.EnsurePermitted(
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
                run.WorkItemId == workItemId && run.Status is AgentRunStatus.Active);
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
            subjectRole,
            // The five cost and manifest fields between SubjectRole and this one are learned at
            // completion and stay absent here. The session link is known at dispatch, which is the
            // only moment it can be known: the coordinator names the bracket it is dispatching from.
            ManifestHash: null,
            ManifestArtifactCount: null,
            Turns: null,
            OutputTokens: null,
            MillisecondsToFirstLedgerWrite: null,
            TokensInUncached: null,
            TokensInCacheWrite: null,
            TokensInCacheRead: null,
            CoordinatorSessionId: command.CoordinatorSessionId,
            // Learned at completion like the cost fields: nothing has read the stream yet, and null
            // here is "nobody counted" rather than "nothing was cut".
            TruncatedLines: null);
        // The waiver precedes the run it let through and carries the justification alone, as on
        // work.added: the reducer joins the pair on causationId rather than copying it onto the run.
        return briefWaiver is null ? [new RunStarted(run)] : [briefWaiver, new RunStarted(run)];
    }

    internal static IReadOnlyList<LedgerEventData> Complete(
        GovernedTaskState state,
        CompleteRunCommand command,
        DateTimeOffset now)
    {
        var run = Get(state.Runs, command.RunId, "run");
        RunCompletionAuthorization.EnsureActorMayComplete(state, command.ActorId, run);
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
        //
        // So is a run that declared no provider at all. Such a run holds a record — an artifact
        // needs a producer run — and spawns no child, so there is no session to lose and nothing to
        // resume; refusing it Completed forced the operator to close a successful filing as
        // Cancelled, and 27 of the 111 unsuccessful runs in this ledger are that bookkeeping and
        // not a failure. The exemption cannot be claimed after the fact: both halves of the
        // declaration are on the run.started event, and a launcher-managed run carries a token hash
        // and is therefore never eligible however it names its provider.
        if (command.Status is AgentRunStatus.Completed &&
            providerSessionId is null &&
            !run.HasNoProviderSessionByDeclaration)
        {
            throw new GovernanceException(
                $"Run '{command.RunId}' cannot be recorded as completed without a provider session identity; " +
                "a completed run must stay resumable. A run that spawns no provider declares " +
                $"'--provider {AgentRun.NoProvider}' at run start and is exempt.");
        }

        if (command.Status == AgentRunStatus.Completed && run.SubjectRole is RoleKind.Verifier or RoleKind.CodeReviewer)
        {
            var requiredKind = run.SubjectRole == RoleKind.Verifier
                ? GovernedArtifactKind.VerifierOutput
                : GovernedArtifactKind.CodeReviewOutput;
            if (!ArtifactRevisionRules.Current(state).Any(artifact =>
                    artifact.Kind == requiredKind && artifact.ProducerRunId == run.Id))
            {
                throw new GovernanceException(
                    $"Completing {run.SubjectRole} run '{run.Id}' requires its matching '{requiredKind}' artifact.");
            }
        }

        RunCompletionRecordRules.EnsureManifestIsWellFormed(command.ManifestHash, command.ManifestArtifactCount);
        RunCompletionRecordRules.EnsureCostIsWellFormed(
            command.Turns, command.OutputTokens, command.MillisecondsToFirstLedgerWrite,
            command.TokensInUncached, command.TokensInCacheWrite, command.TokensInCacheRead);

        var launcherAuthorized = RunCompletionAuthorization.AuthorizeLauncher(state, command, run);
        return
        [
            new RunCompleted(
                command.RunId, command.Status, providerSessionId, now, launcherAuthorized,
                TrimOrNull(command.ManifestHash), command.ManifestArtifactCount,
                command.Turns, command.OutputTokens, command.MillisecondsToFirstLedgerWrite,
                TrimOrNull(command.Model),
                command.TokensInUncached, command.TokensInCacheWrite, command.TokensInCacheRead,
                // Passed through unchecked, and deliberately so. The count is the launcher's own
                // counter and cannot go negative, and the twin rule in this kernel is asymmetric:
                // a rule added here would have no replay counterpart, because R4 requires the
                // validator to gain nothing that keys on this field. No rule on either side keeps
                // the two halves honest rather than divergent.
                command.TruncatedLines,
                // The same precedent, applied to the two fields measures 11 and 12 read. The timeout
                // is the launcher's own parsed value and the CLI already refuses a non-positive one
                // before it reaches here, and the reason is the provider's text; so neither has a
                // rule at command time and neither has one at replay, which keeps the two halves
                // symmetric rather than one tightening the other cannot mirror (MC4, R4).
                //
                // The reason is trimmed and blank becomes absent, so a provider that reports an empty
                // string is recorded as reporting nothing rather than as reporting a failure with no
                // account of it.
                command.LaunchTimeoutSeconds,
                TrimOrNull(command.TerminalFailureReason),
                // And the same again for the observed termination. There is nothing to validate: a
                // three-state flag the launcher either observed or did not is already the whole
                // contract, and a rule keyed on it at replay would refuse the histories that predate
                // it — the failure R4 names.
                command.EndedAtTheLaunchTimeout)
        ];
    }
}
