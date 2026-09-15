using AILedger.Core.CoordinatorSessions;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Domain.ReplayValidationRules;

namespace AILedger.Core.Runs;

// Validates the stable historical shape of Run events without importing newer command-time policy.
internal static class RunEventValidator
{
    internal static void ValidateStarted(GovernedTaskState state, LedgerEvent @event, AgentRun run)
    {
        RequireAuthority(state, @event.ActorId, Capability.ManageRuns);
        EnsureNew(state.Runs, run.Id, "run");
        RequireId(run.Id.Value, nameof(run.Id));
        RequireText(run.Provider, nameof(run.Provider));
        RequireDefined(run.Status, nameof(run.Status));
        // Checked only when present, because null is the legitimate shape of every run recorded
        // before SubjectRole existed. An undefined member is a different matter: it reaches state
        // and renders as its raw number, so a forged role has to fail here.
        if (run.SubjectRole is { } subjectRole)
        {
            RequireDefined(subjectRole, nameof(run.SubjectRole));
        }

        // Mirrors RunLifecycleRules.Start. LaunchedBy is set only when an operator dispatched the
        // run for another actor, so a run recorded before dispatch existed carries null here and
        // this reads exactly as it always did: the run belongs to the event actor. Replay accepts
        // every history that was ever legal; the new rule can only bite on the new shape.
        var dispatcherActorId = run.LaunchedBy ?? run.ActorId;
        if (dispatcherActorId != @event.ActorId || run.Status != AgentRunStatus.Active ||
            run.StartedAt != @event.RecordedAt || run.EndedAt is not null)
        {
            throw new GovernanceException(
                "A started run must be authorised by the event actor and begin active at the event timestamp.");
        }

        if (run.LaunchedBy is not null)
        {
            RequireAuthority(state, @event.ActorId, Capability.ManageRuns, operatorRequired: true);
            // The subject does the work and owns the run's provenance, so it must be a real actor
            // in this task. It deliberately needs no run authority of its own.
            Get(state.Roles, run.ActorId, "actor role");
        }

        CoordinatorSessionEventValidator.ValidateDispatchLink(state, run);

        if (run.WorkItemId is not { } workItemId)
        {
            return;
        }

        var workItem = Get(state.WorkItems, workItemId, "work item");
        EnsureDependenciesAreCurrent(state, workItem.DependsOnClaims);
        if (workItem.Owner is { } owner && owner != @event.ActorId && !IsOperator(state, @event.ActorId))
        {
            throw new GovernanceException($"Only work owner '{owner}' or an operator can start work item '{workItem.Id}'.");
        }

        // Mirrors RunLifecycleRules.Start. Adding Abandoned tightens replay, which is normally the
        // forbidden direction — it is safe only because the status cannot exist in any history
        // written before work.abandoned did, so no log that was legal when written now fails.
        if (workItem.Status is WorkItemStatus.Blocked or WorkItemStatus.Stale or
            WorkItemStatus.Completed or WorkItemStatus.Abandoned)
        {
            throw new GovernanceException($"Cannot start a run for work item in status '{workItem.Status}'.");
        }

        if (state.Runs.Values.Any(current =>
                current.WorkItemId == workItemId && current.Status is AgentRunStatus.Active))
        {
            throw new GovernanceException($"Work item '{workItemId}' already has an active orchestration run.");
        }

        // The rule that a code reviewer only starts after a verifier run that ended after the
        // latest working run is likewise command-time only. It reads SubjectRole on every run
        // involved, and SubjectRole is null on every run recorded before that field existed, so
        // replay cannot tell an out-of-order review from an ordinary old run — and must not guess.
        // Nothing here may require SubjectRole to be present.
    }

    internal static void ValidateCompleted(
        GovernedTaskState state,
        LedgerEvent @event,
        RunCompleted completed)
    {
        RequireAuthority(state, @event.ActorId, Capability.ManageRuns);
        RequireDefined(completed.Status, nameof(completed.Status));
        var run = Get(state.Runs, completed.RunId, "run");
        if (run.ActorId != @event.ActorId && !IsOperator(state, @event.ActorId))
        {
            throw new GovernanceException($"Only run actor '{run.ActorId}' or an operator can complete run '{run.Id}'.");
        }

        if (run.Status is not AgentRunStatus.Active ||
            completed.Status is AgentRunStatus.Active)
        {
            throw new GovernanceException("Only an active run can transition to a terminal status.");
        }

        // Mirrors RunCompletionAuthorization.AuthorizeLauncher. The launcher's secret is
        // deliberately absent from the log, so replay checks the shape rather than the secret: a
        // launcher-managed run is either closed with launcher authority, or closed by an operator
        // as a failure.
        if (run.LaunchTokenHash is not null && !completed.LauncherAuthorized)
        {
            if (completed.Status is not (AgentRunStatus.Failed or AgentRunStatus.Cancelled))
            {
                throw new GovernanceException(
                    "A launcher-managed run can only reach a successful terminal status through its launcher.");
            }

            RequireAuthority(state, @event.ActorId, Capability.ManageRuns, operatorRequired: true);
        }

        // "A completed run must stay resumable" is deliberately NOT checked here. Replay must
        // accept every history that was ever legal, and runs completed before that rule existed
        // carry no session identity. Enforcing it here made a live task permanently unreadable —
        // for the second time, after the same mistake with scope occupancy. The distinction is
        // the same one every time: command-time rules may tighten, replay-time rules may not.
        //
        // The launcher-authority check above is safe by construction rather than by exemption:
        // it only applies when the run carries a LaunchTokenHash, which no pre-rule run does.

        if (completed.EndedAt != @event.RecordedAt || completed.EndedAt < run.StartedAt)
        {
            throw new GovernanceException("Run completion time must match the event timestamp and follow run start.");
        }

        if (run.ProviderSessionId is not null && completed.ProviderSessionId != run.ProviderSessionId)
        {
            throw new GovernanceException("Run completion session identity does not match the started run.");
        }

        ValidateManifestRecord(completed.ManifestHash, completed.ManifestArtifactCount);
        ValidateCostRecord(
            completed.Turns, completed.OutputTokens, completed.MillisecondsToFirstLedgerWrite,
            completed.TokensInUncached, completed.TokensInCacheWrite, completed.TokensInCacheRead);
        // TruncatedLines, LaunchTimeoutSeconds, TerminalFailureReason and EndedAtTheLaunchTimeout are
        // deliberately unchecked here, and RunLifecycleRules does not check them either. The asymmetry this
        // kernel has to respect
        // runs one way — command time may tighten and replay may not — so the safe shape for a
        // trailing nullable field is no rule on either side rather than a rule here that the
        // command-time half cannot later relax (MC4, R4).
    }

    // Mirrors RunCompletionRecordRules.EnsureCostIsWellFormed. Safe by construction rather than
    // by exemption, on exactly the terms the manifest pair set: each check fires only on a value
    // that is present, and no run.completed already on disk carries any of the six. A history
    // recorded before they existed replays unchanged, and a forged one is still refused.
    //
    // Nothing here may require any of them to be present. Turns is genuinely absent for a provider
    // that reports no turn count (D4, IC1), a null time to first ledger write is the measurement
    // rather than a gap — it says the run never reached the ledger at all — and a stream that
    // carried no usage object yields no bucket.
    private static void ValidateCostRecord(
        int? turns,
        long? outputTokens,
        long? millisecondsToFirstLedgerWrite,
        long? tokensInUncached,
        long? tokensInCacheWrite,
        long? tokensInCacheRead)
    {
        if (turns < 0)
        {
            throw new GovernanceException("A run's turn count cannot be negative.");
        }

        if (outputTokens < 0)
        {
            throw new GovernanceException("A run's output token count cannot be negative.");
        }

        if (millisecondsToFirstLedgerWrite < 0)
        {
            throw new GovernanceException("A run's time to first ledger write cannot be negative.");
        }

        if (tokensInUncached < 0)
        {
            throw new GovernanceException("A run's uncached input token count cannot be negative.");
        }

        if (tokensInCacheWrite < 0)
        {
            throw new GovernanceException("A run's cache write input token count cannot be negative.");
        }

        if (tokensInCacheRead < 0)
        {
            throw new GovernanceException("A run's cache read input token count cannot be negative.");
        }
    }

    // Mirrors RunCompletionRecordRules.EnsureManifestIsWellFormed. Safe by construction rather
    // than by exemption: both fields are absent on every run.completed already on disk, so a
    // history recorded before they existed takes the early return and replays unchanged.
    private static void ValidateManifestRecord(string? manifestHash, int? artifactCount)
    {
        if (manifestHash is null && artifactCount is null)
        {
            return;
        }

        if (manifestHash is null || artifactCount is null)
        {
            throw new GovernanceException(
                "A run's manifest hash and manifest artifact count must be recorded together.");
        }

        if (manifestHash.Length != 64 || !manifestHash.All(character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            throw new GovernanceException(
                "A run's manifest hash must be 64 lowercase hexadecimal characters.");
        }

        if (artifactCount < 0)
        {
            throw new GovernanceException("A run's manifest artifact count cannot be negative.");
        }
    }

    private static void EnsureDependenciesAreCurrent(GovernedTaskState state, IEnumerable<ClaimId> claimIds)
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
