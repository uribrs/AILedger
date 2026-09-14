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
    //
    // workItemId is the item the run would be held against, and the two refusals below that read it
    // are the item's own status and the subject's role, in that order. A caller that passes none
    // asks for the refusals that do not depend on an item.
    internal static ContextBriefWaived? EnsureDispatchIsPermitted(
        GovernedTaskState state,
        ActorId actorId,
        ActorId? subjectActorId,
        IReadOnlyList<ContextSkill>? servedNow,
        bool isProviderLaunch,
        WorkItemId? workItemId = null,
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
        ContextBriefWaived? briefWaiver = null;
        if (isProviderLaunch)
        {
            briefWaiver = ContextGateRules.EnsureBriefed(
                state, actorId, servedNow, "launch a provider",
                withoutBriefReason, staleBriefEvidenceId);
        }
        // A run started by hand is not gated, so it cannot be waived either. A door offered where
        // there is no gate would record a waiver for a refusal that never happens, and a log full
        // of waivers nothing needed is how an override stops being read as a decision.
        else if (withoutBriefReason is not null || staleBriefEvidenceId is not null)
        {
            throw new GovernanceException(
                "A run started by hand is not subject to the context gate, so there is nothing to waive. " +
                "The doors are for 'provider launch' and 'work add'.");
        }

        // A coordinating role plans the work and dispatches it; it does not do it. Without this the
        // lead's own run counted as the item's working pass, so 'work complete' was satisfied by a
        // pass in which nobody worked. Stated as an action rather than a flag because there is no
        // door: the only route past a missing working run stays the operator's
        // 'work complete --without-verification REASON', which records its reason permanently.
        //
        // Here rather than in StartRun, which is where it was first written. There it sat past the
        // pre-flight, so a provider launch resolved an executable and ran the provider binary for
        // its version before the refusal spoke — the exact cost the pre-flight exists to avoid, and
        // the second time one refusal living at only one of the two moments produced it (VC3). The
        // decision behind the rule says it is refused at dispatch, and the pre-flight is dispatch.
        //
        // Placed after the two refusals above for the reason stated there: an actor that may never
        // dispatch, or has never been briefed, must read that first. The move brought the item's
        // status check with it rather than leaving it behind in StartRun, because the same ordering
        // rule puts the status ahead of the role: see the block below.
        //
        // Command-time only. SubjectRole is null on every run recorded before the field existed, so
        // a replay rule keyed on it would refuse histories that were legal when written, and this
        // ledger alone holds 16 work items closed with a coordinating run as their only working run.
        // TaskTransitionValidator.ValidateRunStarted deliberately re-derives none of it.
        if (workItemId is { } item)
        {
            // The item's own state speaks first, for the reason the two refusals above are ordered
            // as they are. A Blocked, Stale, Completed or Abandoned item accepts no run from any
            // subject; a coordinating subject is a property of the dispatch, and the caller changes
            // it by dispatching a Worker. Refused the other way round, the instruction an operator
            // reads — dispatch a Worker against the item — points at an item that is gone.
            //
            // The only statement of this rule at command time. StartRun reaches this function on
            // every path, with its own work item id, so the copy that used to stand below the
            // lookup there would now only be a second model of a rule that already exists (KC1).
            // Its replay twin in TaskTransitionValidator.ValidateRunStarted is unchanged, and the
            // set of histories refused is the same set: only which refusal speaks first has moved.
            var workItem = Get(state.WorkItems, item, "work item");
            if (workItem.Status is WorkItemStatus.Blocked or WorkItemStatus.Stale or
                WorkItemStatus.Completed or WorkItemStatus.Abandoned)
            {
                throw new GovernanceException($"Cannot start a run for work item in status '{workItem.Status}'.");
            }

            if (state.Roles.TryGetValue(subject, out var subjectAssignment) &&
                subjectAssignment.Role is RoleKind.Operator or RoleKind.PlanningLead or RoleKind.ImplementationLead)
            {
                throw new GovernanceException(
                    $"A run against work item '{item}' cannot be held by a coordinating role, and " +
                    $"'{subject}' is a {subjectAssignment.Role}. Dispatch a Worker, Researcher, Verifier or " +
                    "CodeReviewer against the item, or start this run without --work to file task-wide " +
                    "artifacts.");
            }
        }

        return briefWaiver;
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
            command.WorkItemId,
            command.WithoutBriefReason, command.StaleBriefEvidenceId);
        // Checked only when the run names a session, which no run recorded before sessions existed
        // does. A run that names none is dispatched outside a bracket, and that is legal (D7).
        CoordinatorSessionRules.EnsureDispatchingSessionIsUsable(
            state, command.ActorId, command.CoordinatorSessionId);

        // Which role the subject holds now, recorded on the run. Role assignments change, so asking
        // the current assignment whether a past run was a verifier's answers a different question.
        var subjectRole = state.Roles[subjectActorId].Role;
    
        if (command.WorkItemId is { } workItemId)
        {
            var workItem = Get(state.WorkItems, workItemId, "work item");
            WorkItemRules.EnsureCanStartWork(state, command.ActorId, workItem);
            ClaimDependencyRules.EnsureDependenciesAreCurrent(state, workItem.DependsOnClaims);
            // The status refusal that used to stand here — Blocked, Stale, Completed or Abandoned,
            // Abandoned for the same reason Completed is and for one more: starting a run moves the
            // item to Active, which would take back a directory area already handed to another work
            // item — now lives in EnsureDispatchIsPermitted above, ahead of the coordinating-role
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
            // EnsureDispatchIsPermitted above, so a provider launch reaches it before it has spawned
            // a process. It therefore speaks before the status and active-run checks above rather
            // than after them, which is stated at its new site.

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
