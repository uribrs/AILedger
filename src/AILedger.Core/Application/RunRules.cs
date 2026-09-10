using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Application;

// Replay counterparts: ValidateRunStarted and ValidateRunCompleted.
internal static class RunRules
{
    // The refusals a provider launch must produce before it has cost anything, in the order they
    // have to speak. StartRun calls this, and so does the launcher before it resolves an adapter or
    // runs a provider binary for its version — one implementation, two moments. It was two moments
    // and no shared implementation that let an unbriefed actor spawn a process and only then be
    // refused (VC3).
    //
    // Returns the brief waiver to record when a door was opened, and null otherwise. The pre-flight
    // discards it; StartRun records it with the run.
    internal static ContextBriefWaived? EnsureDispatchIsPermitted(
        GovernedTaskState state,
        ActorId actorId,
        ActorId? subjectActorId,
        IReadOnlyList<ContextSkill>? servedNow,
        bool isProviderLaunch,
        string? withoutBriefReason = null,
        EvidenceId? staleBriefEvidenceId = null)
    {
        // A run is authorised by one actor and worked by another only when an operator dispatches
        // it. Starting the run under the working actor is what made the roles that hold no run
        // authority — researcher, worker, verifier, code reviewer — impossible to launch at all.
        // Dispatch separates the two without handing those roles authority they must not have.
        var subject = subjectActorId ?? actorId;
        if (subject != actorId)
        {
            if (!RoleAssignmentRules.IsOperator(state, actorId))
            {
                throw new GovernanceException(
                    $"Only an operator can start a run on another actor's behalf; '{actorId}' cannot " +
                    $"dispatch for '{subject}'.");
            }

            if (!state.Roles.ContainsKey(subject))
            {
                throw new GovernanceException($"Run subject '{subject}' has no assigned role.");
            }
        }

        // A provider launch, and only a provider launch. The launcher is the one caller that sets
        // LaunchTokenHash, so it is what tells a dispatch from a run started by hand (IC3), and a
        // dispatch is the other moment the pipeline is supposed to have been read first.
        //
        // Placed after the dispatch-authority checks above, not before them. An actor that may
        // never dispatch at all should be told that, not told to go and build context first: the
        // refusal an actor reads is the instruction it acts on, so the gate that cannot be
        // satisfied has to speak before the gate that can.
        if (isProviderLaunch)
        {
            return ContextGateRules.EnsureBriefed(
                state, actorId, servedNow, "launch a provider",
                withoutBriefReason, staleBriefEvidenceId);
        }

        // A run started by hand is not gated, so it cannot be waived either. A door offered where
        // there is no gate would record a waiver for a refusal that never happens, and a log full
        // of waivers nothing needed is how an override stops being read as a decision.
        if (withoutBriefReason is not null || staleBriefEvidenceId is not null)
        {
            throw new GovernanceException(
                "A run started by hand is not subject to the context gate, so there is nothing to waive. " +
                "The doors are for 'provider launch' and 'work add'.");
        }

        return null;
    }

    internal static IReadOnlyList<LedgerEventData> StartRun(
        GovernedTaskState state,
        StartRunCommand command,
        DateTimeOffset now)
    {
        RequireId(command.RunId.Value, nameof(command.RunId));
        EnsureNew(state.Runs, command.RunId, "run");
        RequireText(command.Provider, nameof(command.Provider));
    
        var subjectActorId = command.SubjectActorId ?? command.ActorId;
        ActorId? launchedBy = subjectActorId == command.ActorId ? null : command.ActorId;
        var briefWaiver = EnsureDispatchIsPermitted(
            state, command.ActorId, command.SubjectActorId, command.SkillsServedNow,
            isProviderLaunch: command.LaunchTokenHash is not null,
            command.WithoutBriefReason, command.StaleBriefEvidenceId);

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
        return briefWaiver is null ? [new RunStarted(run)] : [briefWaiver, new RunStarted(run)];
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
    
        EnsureManifestRecordIsWellFormed(command.ManifestHash, command.ManifestArtifactCount);
        EnsureCostRecordIsWellFormed(
            command.Turns, command.OutputTokens, command.MillisecondsToFirstLedgerWrite,
            command.TokensInUncached, command.TokensInCacheWrite, command.TokensInCacheRead);

        var launcherAuthorized = EnsureLauncherAuthorizedCompletion(state, command, run);
        return
        [
            new RunCompleted(
                command.RunId, command.Status, providerSessionId, now, launcherAuthorized,
                TrimOrNull(command.ManifestHash), command.ManifestArtifactCount,
                command.Turns, command.OutputTokens, command.MillisecondsToFirstLedgerWrite,
                TrimOrNull(command.Model),
                command.TokensInUncached, command.TokensInCacheWrite, command.TokensInCacheRead)
        ];
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

    // Mirrors TaskTransitionValidator.ValidateManifestRecord. Deliberately duplicated, not shared:
    // D13 holds that the two copies must agree, not that they must be one function.
    //
    // The pair is all-or-nothing because a hash with no count says a brief was handed over but not
    // how large it was, and a count with no hash cannot be matched to any manifest. Neither half
    // answers the question the fields exist for.
    internal const int ManifestHashLength = 64;

    private static void EnsureManifestRecordIsWellFormed(string? manifestHash, int? artifactCount)
    {
        var hash = TrimOrNull(manifestHash);
        if (hash is null && artifactCount is null)
        {
            return;
        }

        if (hash is null || artifactCount is null)
        {
            throw new GovernanceException(
                "A run's manifest hash and manifest artifact count must be recorded together.");
        }

        if (hash.Length != ManifestHashLength || !hash.All(character =>
                character is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            throw new GovernanceException(
                $"A run's manifest hash must be {ManifestHashLength} lowercase hexadecimal characters.");
        }

        if (artifactCount < 0)
        {
            throw new GovernanceException("A run's manifest artifact count cannot be negative.");
        }
    }

    // Mirrors TaskTransitionValidator.ValidateCostRecord, on the same terms as the manifest pair.
    //
    // The six are independent, unlike the manifest pair: a run can report turns and output tokens
    // and still never reach the ledger, a provider that states no turn count reports none (D4), and
    // each absence means something on its own. So there is no all-or-nothing rule here, only the one
    // thing a count cannot be.
    private static void EnsureCostRecordIsWellFormed(
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

        // Zero stays legal: a run that reached the ledger inside the first millisecond measured
        // zero, and that is a different fact from never having reached it, which is null.
        if (millisecondsToFirstLedgerWrite < 0)
        {
            throw new GovernanceException("A run's time to first ledger write cannot be negative.");
        }

        // The uncached bucket is the one a mapping can drive negative, because for codex it is a
        // subtraction (C6). None of the six checks here is relaxed for a provider that reports
        // nonsense: C15 is that the reader's accepted set has to be the subset, so RunCostReader
        // returns nothing rather than any value this rule refuses, and a negative arriving here is
        // a caller that did its own arithmetic. A direct or forged caller stays guarded.
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
}
