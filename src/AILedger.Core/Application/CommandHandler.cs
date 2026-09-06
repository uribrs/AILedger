using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Core.Application;

public sealed class CommandHandler : ICommandHandler
{
    private readonly ITaskReducer _reducer;
    private readonly AuthorizationPolicy _authorizationPolicy;

    public CommandHandler()
        : this(new TaskReducer(), new AuthorizationPolicy())
    {
    }

    public CommandHandler(ITaskReducer reducer, AuthorizationPolicy authorizationPolicy)
    {
        _reducer = reducer;
        _authorizationPolicy = authorizationPolicy;
    }

    public CommandOutcome Handle(GovernedTaskState? state, LedgerCommand command, DateTimeOffset now)
    {
        ValidateEnvelope(command);
        ValidateEnums(command);

        var eventData = state is null
            ? HandleOpening(command, now)
            : HandleExisting(state, command, now);

        return ApplyEvents(state, command, eventData, now);
    }

    private IReadOnlyList<LedgerEventData> HandleOpening(LedgerCommand command, DateTimeOffset now)
    {
        if (command is not OpenTaskCommand open)
        {
            throw new GovernanceException("A task must be opened before other commands are handled.");
        }

        RequireId(open.TaskId.Value, nameof(open.TaskId));
        RequireText(open.Title, nameof(open.Title));
        RequireText(open.Goal, nameof(open.Goal));

        var operatorRole = new RoleAssignment(
            open.ActorId,
            RoleKind.Operator,
            Enum.GetValues<Capability>(),
            new Provenance(open.ActorId, now, "task.open"));

        var events = new List<LedgerEventData>
        {
            new TaskOpened(open.Title.Trim(), open.Goal.Trim()),
            new RoleAssigned(operatorRole)
        };

        var recalledLessons = open.RecalledLessons ?? [];
        EnsureUnique(recalledLessons.Select(lesson => lesson.Id).ToArray(), "Recalled lesson IDs");
        foreach (var lesson in recalledLessons.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            ValidateRecalledLesson(open.TaskId, lesson);
            events.Add(new LessonRecalled(lesson));
        }

        return events;
    }

    private IReadOnlyList<LedgerEventData> HandleExisting(
        GovernedTaskState state,
        LedgerCommand command,
        DateTimeOffset now)
    {
        if (command is OpenTaskCommand)
        {
            throw new GovernanceException("Task is already open.");
        }

        _authorizationPolicy.Authorize(state, command);

        return command switch
        {
            AssignRoleCommand assign => AssignRole(state, assign, now),
            AddClaimCommand add => AddClaim(state, add, now),
            ResolveClaimCommand resolve => ResolveClaim(state, resolve),
            AddEvidenceCommand add => AddEvidence(state, add, now),
            ProposeDecisionCommand propose => ProposeDecision(state, propose, now),
            ResolveDecisionCommand resolve => ResolveDecision(state, resolve),
            RaiseChallengeCommand raise => RaiseChallenge(state, raise, now),
            DisposeChallengeCommand dispose => DisposeChallenge(state, dispose),
            AddWorkItemCommand add => AddWorkItem(state, add),
            StartRunCommand start => StartRun(state, start, now),
            CompleteRunCommand complete => CompleteRun(state, complete, now),
            RequestStageTransitionCommand transition => TransitionStage(state, transition, now),
            RaiseEscalationCommand raise => RaiseEscalation(state, raise, now),
            ResolveEscalationCommand resolve => ResolveEscalation(state, resolve),
            RecordAlternativeCommand record => RecordAlternative(state, record, now),
            MarkLessonBearingCommand mark => MarkLessonBearing(state, mark, now),
            AddConstraintCommand add => AddConstraint(state, add, now),
            SupersedeConstraintCommand supersede => SupersedeConstraint(state, supersede),
            CompleteWorkItemCommand complete => CompleteWorkItem(state, complete),
            BlockWorkItemCommand block => BlockWorkItem(state, block),
            UnblockWorkItemCommand unblock => UnblockWorkItem(state, unblock),
            AbandonWorkItemCommand abandon => AbandonWorkItem(state, abandon),
            _ => throw new GovernanceException($"Unsupported command '{command.GetType().Name}'.")
        };
    }

    private static IReadOnlyList<LedgerEventData> AssignRole(
        GovernedTaskState state,
        AssignRoleCommand command,
        DateTimeOffset now)
    {
        RequireId(command.TargetActorId.Value, nameof(command.TargetActorId));
        if (command.TargetActorId == command.ActorId)
        {
            throw new GovernanceException("Actors cannot assign or expand their own authority.");
        }

        var capabilities = DistinctCapabilities(command.Capabilities);
        if (command.Role != RoleKind.Operator &&
            capabilities.Any(capability => capability is Capability.ManageRoles or Capability.ManageScope))
        {
            throw new GovernanceException("Only an operator role can receive role-management or scope-management authority.");
        }
        var assignment = new RoleAssignment(
            command.TargetActorId,
            command.Role,
            capabilities,
            new Provenance(command.ActorId, now, "actor.assign-role"));

        return [new RoleAssigned(assignment)];
    }

    private static IReadOnlyList<LedgerEventData> AddClaim(
        GovernedTaskState state,
        AddClaimCommand command,
        DateTimeOffset now)
    {
        RequireId(command.ClaimId.Value, nameof(command.ClaimId));
        EnsureNew(state.Claims, command.ClaimId, "claim");
        RequireText(command.Statement, nameof(command.Statement));

        var claim = new Claim(
            command.ClaimId,
            command.Statement.Trim(),
            ClaimStatus.Open,
            [],
            TrimOrNull(command.ConsequenceIfWrong),
            new Provenance(command.ActorId, now, "claim.add"));
        return [new ClaimAdded(claim)];
    }

    private static IReadOnlyList<LedgerEventData> ResolveClaim(
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
                    $"Evidence '{evidenceId}' does not {EvidenceDirection(command.Status)} claim '{command.ClaimId}'.");
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
            AddDependencyInvalidations(state, command.ClaimId, events);
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

    private static IReadOnlyList<LedgerEventData> AddEvidence(
        GovernedTaskState state,
        AddEvidenceCommand command,
        DateTimeOffset now)
    {
        RequireId(command.EvidenceId.Value, nameof(command.EvidenceId));
        EnsureNew(state.Evidence, command.EvidenceId, "evidence");
        RequireText(command.SourceType, nameof(command.SourceType));
        RequireText(command.Citation, nameof(command.Citation));
        RequireText(command.Summary, nameof(command.Summary));
        EnsureUnique(command.Supports, "Supported claim IDs");
        EnsureUnique(command.Refutes, "Refuted claim IDs");
        EnsureReferencesExist(state.Claims, command.Supports, "claim");
        EnsureReferencesExist(state.Claims, command.Refutes, "claim");

        if (command.Supports.Intersect(command.Refutes).Any())
        {
            throw new GovernanceException("The same evidence cannot both support and refute a claim.");
        }

        var evidence = new Evidence(
            command.EvidenceId,
            command.SourceType.Trim(),
            command.Citation.Trim(),
            command.Summary.Trim(),
            command.Supports.ToArray(),
            command.Refutes.ToArray(),
            new Provenance(command.ActorId, now, "evidence.add"));
        return [new EvidenceAdded(evidence)];
    }

    private static IReadOnlyList<LedgerEventData> ProposeDecision(
        GovernedTaskState state,
        ProposeDecisionCommand command,
        DateTimeOffset now)
    {
        RequireId(command.DecisionId.Value, nameof(command.DecisionId));
        EnsureNew(state.Decisions, command.DecisionId, "decision");
        RequireText(command.Statement, nameof(command.Statement));
        RequireText(command.Rationale, nameof(command.Rationale));
        EnsureUnique(command.DependsOnClaims, "Dependent claim IDs");
        EnsureReferencesExist(state.Claims, command.DependsOnClaims, "claim");
        EnsureDependenciesAreCurrent(state, command.DependsOnClaims);

        if (command.Supersedes is { } supersededId)
        {
            if (supersededId == command.DecisionId)
            {
                throw new GovernanceException("A decision cannot supersede itself.");
            }

            var superseded = Get(state.Decisions, supersededId, "decision");
            if (superseded.Status is DecisionStatus.Superseded or DecisionStatus.Invalidated)
            {
                throw new GovernanceException("Only a current decision can be superseded.");
            }
        }

        var decision = new Decision(
            command.DecisionId,
            command.Statement.Trim(),
            DecisionStatus.Proposed,
            command.Rationale.Trim(),
            command.DependsOnClaims.ToArray(),
            command.Supersedes,
            new Provenance(command.ActorId, now, "decision.propose"));
        return [new DecisionProposed(decision)];
    }

    private static IReadOnlyList<LedgerEventData> ResolveDecision(
        GovernedTaskState state,
        ResolveDecisionCommand command)
    {
        var decision = Get(state.Decisions, command.DecisionId, "decision");
        if (decision.Status != DecisionStatus.Proposed)
        {
            throw new GovernanceException("Only a proposed decision can be resolved.");
        }


        EnsureDependenciesAreCurrent(state, decision.DependsOnClaims);

        if (command.Status is not (DecisionStatus.Accepted or DecisionStatus.Superseded))
        {
            throw new GovernanceException("A decision may be explicitly accepted or superseded; invalidation is causal.");
        }

        var events = new List<LedgerEventData> { new DecisionResolved(command.DecisionId, command.Status) };
        if (command.Status == DecisionStatus.Accepted && decision.Supersedes is { } supersededId)
        {
            var predecessor = Get(state.Decisions, supersededId, "decision");
            if (predecessor.Status is DecisionStatus.Superseded or DecisionStatus.Invalidated)
            {
                throw new GovernanceException("A replacement can only be accepted while its predecessor is current.");
            }

            events.Add(new DecisionResolved(supersededId, DecisionStatus.Superseded));
        }

        return events;
    }

    private static IReadOnlyList<LedgerEventData> RaiseChallenge(
        GovernedTaskState state,
        RaiseChallengeCommand command,
        DateTimeOffset now)
    {
        RequireId(command.ChallengeId.Value, nameof(command.ChallengeId));
        EnsureNew(state.Challenges, command.ChallengeId, "challenge");
        RequireText(command.TargetType, nameof(command.TargetType));
        RequireText(command.TargetId, nameof(command.TargetId));
        RequireText(command.Reason, nameof(command.Reason));
        EnsureUnique(command.EvidenceIds, "Evidence IDs");
        EnsureReferencesExist(state.Evidence, command.EvidenceIds, "evidence");
        EnsureChallengeTargetExists(state, command.TargetType, command.TargetId);

        var challenge = new Challenge(
            command.ChallengeId,
            command.TargetType.Trim(),
            command.TargetId.Trim(),
            command.Reason.Trim(),
            ChallengeStatus.Open,
            command.EvidenceIds.ToArray(),
            new Provenance(command.ActorId, now, "challenge.raise"));
        return [new ChallengeRaised(challenge)];
    }

    private static IReadOnlyList<LedgerEventData> DisposeChallenge(
        GovernedTaskState state,
        DisposeChallengeCommand command)
    {
        var challenge = Get(state.Challenges, command.ChallengeId, "challenge");
        if (challenge.Status != ChallengeStatus.Open)
        {
            throw new GovernanceException("Only an open challenge can be disposed.");
        }

        if (command.Status == ChallengeStatus.Open)
        {
            throw new GovernanceException("Challenge disposition must be terminal.");
        }

        var events = new List<LedgerEventData> { new ChallengeDisposed(command.ChallengeId, command.Status) };
        if (command.Status == ChallengeStatus.Supported)
        {
            AddChallengeConsequence(state, command, challenge, events);
        }

        return events;
    }

    // An upheld challenge has to change something, or the protocol is advisory. The consequence
    // is whatever the target kind already has machinery for.
    private static void AddChallengeConsequence(
        GovernedTaskState state,
        DisposeChallengeCommand command,
        Challenge challenge,
        ICollection<LedgerEventData> events)
    {
        var targetId = challenge.TargetId.Trim();

        // Supporting a challenge is not an unevidenced route to overturning anything. The claim
        // arm below additionally checks direction, which the model only expresses for claims.
        if (challenge.EvidenceIds.Count == 0)
        {
            throw new GovernanceException(
                $"Challenge '{challenge.Id}' carries no evidence and cannot be supported.");
        }

        switch (challenge.TargetType.Trim().ToLowerInvariant())
        {
            case "claim":
            {
                var claim = Get(state.Claims, new ClaimId(targetId), "claim");
                if (claim.Status is ClaimStatus.Rejected or ClaimStatus.Superseded)
                {
                    throw new GovernanceException(
                        $"Claim '{claim.Id}' is already '{claim.Status}'; supporting this challenge would change nothing.");
                }

                // R1 (challenge-capability-replay-trap): the emitted ClaimResolved is authorised at
                // replay against ResolveClaim, so demand it here or the task becomes unreplayable.
                RequireConsequenceCapability(state, command.ActorId, Capability.ResolveClaim);
                var refuting = challenge.EvidenceIds
                    .Where(id => state.Evidence[id].Refutes.Contains(claim.Id))
                    .ToArray();
                if (refuting.Length != challenge.EvidenceIds.Count || refuting.Length == 0)
                {
                    throw new GovernanceException(
                        $"A challenge against claim '{claim.Id}' can only be supported when every piece of its evidence refutes that claim.");
                }

                events.Add(new ClaimResolved(claim.Id, ClaimStatus.Rejected, refuting));
                AddDependencyInvalidations(state, claim.Id, events);
                return;
            }

            case "decision":
            {
                var decision = Get(state.Decisions, new DecisionId(targetId), "decision");
                if (decision.Status is not (DecisionStatus.Proposed or DecisionStatus.Accepted))
                {
                    throw new GovernanceException(
                        $"Decision '{decision.Id}' is already '{decision.Status}'; supporting this challenge would change nothing.");
                }

                RequireConsequenceCapability(state, command.ActorId, Capability.ResolveDecision);
                events.Add(new DecisionOverturned(decision.Id, command.ChallengeId));
                return;
            }

            case "work" or "workitem" or "work-item":
            {
                var workItem = Get(state.WorkItems, new WorkItemId(targetId), "work item");
                if (workItem.Status is WorkItemStatus.Blocked or WorkItemStatus.Stale or
                    WorkItemStatus.Completed or WorkItemStatus.Abandoned)
                {
                    throw new GovernanceException(
                        $"Work item '{workItem.Id}' is already '{workItem.Status}'; supporting this challenge would change nothing.");
                }

                RequireConsequenceCapability(state, command.ActorId, Capability.ManageWork);
                events.Add(new WorkItemBlocked(
                    workItem.Id, $"Challenge '{command.ChallengeId}' was supported.", null));
                return;
            }

            default:
                throw new GovernanceException($"Unsupported challenge target type '{challenge.TargetType}'.");
        }
    }

    private static void RequireConsequenceCapability(GovernedTaskState state, ActorId actorId, Capability capability)
    {
        if (!state.Roles[actorId].Capabilities.Contains(capability))
        {
            throw new GovernanceException(
                $"Supporting this challenge emits a '{capability}' transition, which actor '{actorId}' lacks.");
        }
    }

    private static IReadOnlyList<LedgerEventData> AddWorkItem(
        GovernedTaskState state,
        AddWorkItemCommand command)
    {
        RequireId(command.WorkItemId.Value, nameof(command.WorkItemId));
        EnsureNew(state.WorkItems, command.WorkItemId, "work item");
        RequireText(command.Title, nameof(command.Title));
        EnsureUnique(command.DependsOnClaims, "Dependent claim IDs");
        EnsureReferencesExist(state.Claims, command.DependsOnClaims, "claim");
        EnsureDependenciesAreCurrent(state, command.DependsOnClaims);
        EnsureUnique(command.ResourceScope, "Resource scope entries", StringComparer.Ordinal);

        if (command.ResourceScope.Any(string.IsNullOrWhiteSpace))
        {
            throw new GovernanceException("Resource scope entries cannot be empty.");
        }

        if (command.ResourceScope.Any(scope => !Path.IsPathFullyQualified(scope.Trim())))
        {
            throw new GovernanceException("Resource scope entries must be absolute paths.");
        }

        // Two disjoint areas in one work item is one agent holding what two could have held. The
        // kernel cannot judge whether the split was right — that needs to know the work — but it can
        // refuse to let the choice not to split go unrecorded and unattributed.
        if (command.ResourceScope.Count > 1)
        {
            if (command.NotSplitJustification is not { } justificationId)
            {
                throw new GovernanceException(
                    $"Work item '{command.WorkItemId}' claims more than one area. Record why they were not " +
                    "split into separate work items, and name that alternative.");
            }

            _ = Get(state.Alternatives, justificationId, "alternative");
        }

        EnsureScopeIsNotAlreadyOccupied(state, command.WorkItemId, command.ResourceScope);

        if (command.Owner is { } owner && !state.Roles.ContainsKey(owner))
        {
            throw new GovernanceException($"Work owner '{owner}' has no assigned role.");
        }

        var workItem = new WorkItem(
            command.WorkItemId,
            command.Title.Trim(),
            command.Owner,
            WorkItemStatus.Proposed,
            command.DependsOnClaims.ToArray(),
            command.ResourceScope.Select(item => item.Trim()).ToArray(),
            NotSplitJustification: command.NotSplitJustification);
        return [new WorkItemAdded(workItem)];
    }

    private static IReadOnlyList<LedgerEventData> StartRun(
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
            if (!IsOperator(state, command.ActorId))
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
            EnsureCanStartWork(state, command.ActorId, workItem);
            EnsureDependenciesAreCurrent(state, workItem.DependsOnClaims);
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
            if (subjectRole == RoleKind.CodeReviewer && !HasVerifierRunAfterLatestWork(state, workItemId))
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

    private static IReadOnlyList<LedgerEventData> CompleteRun(
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

        var launcherAuthorized = EnsureLauncherAuthorizedCompletion(state, command, run);
        return [new RunCompleted(command.RunId, command.Status, providerSessionId, now, launcherAuthorized)];
    }

    private static void EnsureCanStartWork(
        GovernedTaskState state,
        ActorId actorId,
        WorkItem workItem)
    {
        if (workItem.Owner is null || workItem.Owner == actorId || IsOperator(state, actorId))
        {
            return;
        }

        throw new GovernanceException($"Only work owner '{workItem.Owner}' or an operator can start work item '{workItem.Id}'.");
    }

    // A launched agent shares the run's actor identity, so identity cannot distinguish it from the
    // process that launched it. The launcher's secret can. Three live agents closed their own runs
    // despite a briefing telling them not to, which is why this is a rule rather than a sentence.
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
        if (IsOperator(state, command.ActorId) &&
            command.Status is AgentRunStatus.Failed or AgentRunStatus.Cancelled)
        {
            return false;
        }

        throw new GovernanceException(
            $"Run '{run.Id}' was started by a provider launch and is closed by that launcher. " +
            "An agent running inside it cannot complete it. An operator may close an orphaned run " +
            "as failed or cancelled.");
    }

    public static string HashLaunchToken(string token) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(token)));

    private static void EnsureCanCompleteRun(
        GovernedTaskState state,
        ActorId actorId,
        AgentRun run)
    {
        if (run.ActorId == actorId || IsOperator(state, actorId))
        {
            return;
        }

        throw new GovernanceException($"Only run actor '{run.ActorId}' or an operator can complete run '{run.Id}'.");
    }

    private static bool IsOperator(GovernedTaskState state, ActorId actorId) =>
        state.Roles.TryGetValue(actorId, out var assignment) && assignment.Role == RoleKind.Operator;

    private static IReadOnlyList<LedgerEventData> TransitionStage(
        GovernedTaskState state,
        RequestStageTransitionCommand command,
        DateTimeOffset now)
    {
        StageTransitionPolicy.EnsureAllowed(state.Stage, command.TargetStage);
        EnsureStagePrerequisites(state, command.TargetStage);
        if (command.TargetStage != TaskStage.Archive)
        {
            return [new StageTransitioned(state.Stage, command.TargetStage)];
        }

        var lessons = MintLessons(state, command.ActorId, now);
        if (lessons.Count == 0)
        {
            throw new GovernanceException(
                "Archiving requires at least one eligible lesson-bearing mark on a validated claim, " +
                "rejected alternative, or resolved escalation.");
        }

        return lessons.Select<Lesson, LedgerEventData>(lesson => new LessonMinted(lesson))
            .Append(new StageTransitioned(state.Stage, command.TargetStage))
            .ToArray();
    }

    private static IReadOnlyList<Lesson> MintLessons(
        GovernedTaskState state,
        ActorId actorId,
        DateTimeOffset now)
    {
        var provenance = new Provenance(actorId, now, "stage.archive");
        var lessons = new List<Lesson>();

        foreach (var mark in state.LessonMarks.Values.OrderBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            var lesson = CreateLesson(state, mark, provenance);
            if (lesson is not null)
            {
                lessons.Add(lesson);
            }
        }

        return lessons;
    }

    private static Lesson? CreateLesson(GovernedTaskState state, LessonMark mark, Provenance provenance) =>
        mark.SourceKind switch
        {
            LessonSourceKind.ValidatedClaim when
                state.Claims.TryGetValue(new ClaimId(mark.SourceRecordId), out var claim) &&
                claim.Status == ClaimStatus.Validated => new Lesson(
                    LessonIdFor(state.TaskId, mark.SourceKind, mark.SourceRecordId), state.TaskId,
                    mark.SourceKind, mark.SourceRecordId, claim.Statement, "Validated",
                    claim.EvidenceIds.Select(id => state.Evidence[id].Citation)
                        .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                    provenance, mark.SupersedesLessonId),
            LessonSourceKind.RejectedAlternative when
                state.Alternatives.TryGetValue(new AlternativeId(mark.SourceRecordId), out var alternative) =>
                new Lesson(LessonIdFor(state.TaskId, mark.SourceKind, mark.SourceRecordId), state.TaskId,
                    mark.SourceKind, mark.SourceRecordId, alternative.Statement, alternative.RejectionRationale,
                    [], provenance, mark.SupersedesLessonId),
            LessonSourceKind.ResolvedEscalation when
                state.Escalations.TryGetValue(new EscalationId(mark.SourceRecordId), out var escalation) &&
                escalation.Status == EscalationStatus.Resolved => new Lesson(
                    LessonIdFor(state.TaskId, mark.SourceKind, mark.SourceRecordId), state.TaskId,
                    mark.SourceKind, mark.SourceRecordId, escalation.Question, escalation.Resolution!,
                    escalation.AttemptEvidenceIds.Select(id => state.Evidence[id].Citation)
                        .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                    provenance, mark.SupersedesLessonId),
            _ => null
        };

    private static IReadOnlyList<LedgerEventData> MarkLessonBearing(
        GovernedTaskState state,
        MarkLessonBearingCommand command,
        DateTimeOffset now)
    {
        RequireDefined(command.SourceKind, nameof(command.SourceKind));
        RequireId(command.SourceRecordId, nameof(command.SourceRecordId));
        var markId = LessonMarkIdFor(command.SourceKind, command.SourceRecordId);
        EnsureNew(state.LessonMarks, markId, "lesson mark");

        var eligible = command.SourceKind switch
        {
            LessonSourceKind.ValidatedClaim =>
                state.Claims.TryGetValue(new ClaimId(command.SourceRecordId), out var claim) &&
                claim.Status == ClaimStatus.Validated,
            LessonSourceKind.RejectedAlternative =>
                state.Alternatives.ContainsKey(new AlternativeId(command.SourceRecordId)),
            LessonSourceKind.ResolvedEscalation =>
                state.Escalations.TryGetValue(new EscalationId(command.SourceRecordId), out var escalation) &&
                escalation.Status == EscalationStatus.Resolved,
            _ => false
        };
        if (!eligible)
        {
            throw new GovernanceException("A lesson mark must name an eligible source record.");
        }

        if (command.SupersedesLessonId is { } superseded)
        {
            _ = Get(state.Lessons, superseded, "superseded lesson");
            if (state.LessonMarks.Values.Any(mark => mark.SupersedesLessonId == superseded))
            {
                throw new GovernanceException($"Lesson '{superseded}' is already superseded by another mark.");
            }
        }

        return [new LessonMarked(new LessonMark(
            markId, command.SourceKind, command.SourceRecordId.Trim(), command.SupersedesLessonId,
            new Provenance(command.ActorId, now, "lesson.mark")))];
    }

    private static LessonMarkId LessonMarkIdFor(LessonSourceKind kind, string sourceRecordId) =>
        new($"{kind.ToString().ToLowerInvariant()}:{sourceRecordId.Trim()}");

    private static LessonId LessonIdFor(TaskId taskId, LessonSourceKind kind, string sourceRecordId) =>
        new($"{taskId.Value}:{kind.ToString().ToLowerInvariant()}:{sourceRecordId}");

    private static void ValidateRecalledLesson(TaskId openedTaskId, Lesson lesson)
    {
        RequireId(lesson.Id.Value, nameof(lesson.Id));
        RequireId(lesson.SourceTaskId.Value, nameof(lesson.SourceTaskId));
        RequireDefined(lesson.SourceKind, nameof(lesson.SourceKind));
        RequireId(lesson.SourceRecordId, nameof(lesson.SourceRecordId));
        RequireText(lesson.Statement, nameof(lesson.Statement));
        RequireText(lesson.Outcome, nameof(lesson.Outcome));
        if (lesson.SourceTaskId == openedTaskId)
        {
            throw new GovernanceException("A task cannot recall a lesson from itself while it is being opened.");
        }
    }

    private static IReadOnlyList<LedgerEventData> RaiseEscalation(
        GovernedTaskState state,
        RaiseEscalationCommand command,
        DateTimeOffset now)
    {
        RequireId(command.EscalationId.Value, nameof(command.EscalationId));
        EnsureNew(state.Escalations, command.EscalationId, "escalation");
        RequireText(command.Question, nameof(command.Question));

        if (command.WorkItemId is { } workItemId)
        {
            _ = Get(state.WorkItems, workItemId, "work item");
        }

        var options = command.Options.Select(option => option.Trim()).ToArray();
        var recommendation = TrimOrNull(command.Recommendation);
        if (options.Any(string.IsNullOrWhiteSpace))
        {
            throw new GovernanceException("An escalation cannot carry an empty option.");
        }

        EnsureUnique(options, "Options", StringComparer.Ordinal);
        EnsureUnique(command.AttemptEvidenceIds, "Attempt evidence IDs");
        EnsureReferencesExist(state.Evidence, command.AttemptEvidenceIds, "evidence");

        // The two kinds exist to keep everything else off the operator's desk, so each
        // one has to carry the payload that proves it earned the interruption.
        switch (command.Kind)
        {
            case EscalationKind.BusinessDecision:
                if (options.Length < 2)
                {
                    throw new GovernanceException(
                        "A business decision requires at least two options for the operator to choose between.");
                }

                if (recommendation is null)
                {
                    throw new GovernanceException("A business decision requires a recommendation.");
                }

                if (!options.Contains(recommendation, StringComparer.Ordinal))
                {
                    throw new GovernanceException("A business decision recommendation must name one of its options.");
                }

                break;
            case EscalationKind.TrueUnknown:
                if (command.AttemptEvidenceIds.Count == 0)
                {
                    throw new GovernanceException(
                        "A true unknown requires evidence of the attempt that failed to answer it.");
                }

                break;
            default:
                throw new GovernanceException($"Unsupported escalation kind '{command.Kind}'.");
        }

        var escalation = new Escalation(
            command.EscalationId,
            command.Kind,
            command.Question.Trim(),
            EscalationStatus.Open,
            command.WorkItemId,
            options,
            recommendation,
            command.AttemptEvidenceIds.ToArray(),
            null,
            null,
            new Provenance(command.ActorId, now, "escalation.raise"));
        return [new EscalationRaised(escalation)];
    }

    private static IReadOnlyList<LedgerEventData> ResolveEscalation(
        GovernedTaskState state,
        ResolveEscalationCommand command)
    {
        var escalation = Get(state.Escalations, command.EscalationId, "escalation");
        if (escalation.Status != EscalationStatus.Open)
        {
            throw new GovernanceException("Only an open escalation can be resolved.");
        }

        if (command.Status == EscalationStatus.Open)
        {
            throw new GovernanceException("Escalation resolution must be terminal.");
        }

        var resolution = TrimOrNull(command.Resolution);
        if (command.Status == EscalationStatus.Resolved && resolution is null)
        {
            throw new GovernanceException("Resolving an escalation requires the operator's answer.");
        }

        return [new EscalationResolved(command.EscalationId, command.Status, resolution, command.ActorId)];
    }

    private static IReadOnlyList<LedgerEventData> RecordAlternative(
        GovernedTaskState state,
        RecordAlternativeCommand command,
        DateTimeOffset now)
    {
        RequireId(command.AlternativeId.Value, nameof(command.AlternativeId));
        EnsureNew(state.Alternatives, command.AlternativeId, "alternative");
        RequireText(command.Statement, nameof(command.Statement));
        RequireText(command.RejectionRationale, nameof(command.RejectionRationale));

        if (command.ReplacedByDecisionId is { } decisionId)
        {
            _ = Get(state.Decisions, decisionId, "decision");
        }

        var alternative = new Alternative(
            command.AlternativeId,
            command.Statement.Trim(),
            command.RejectionRationale.Trim(),
            command.ReplacedByDecisionId,
            new Provenance(command.ActorId, now, "alternative.record"));
        return [new AlternativeRecorded(alternative)];
    }

    private static IReadOnlyList<LedgerEventData> AddConstraint(
        GovernedTaskState state,
        AddConstraintCommand command,
        DateTimeOffset now)
    {
        RequireId(command.ConstraintId.Value, nameof(command.ConstraintId));
        EnsureNew(state.Constraints, command.ConstraintId, "constraint");
        RequireText(command.Statement, nameof(command.Statement));
        RequireText(command.Source, nameof(command.Source));
        EnsureUnique(command.Scope, "Constraint scope entries", StringComparer.Ordinal);

        if (command.Scope.Any(string.IsNullOrWhiteSpace))
        {
            throw new GovernanceException("Constraint scope entries cannot be empty.");
        }

        var constraint = new Constraint(
            command.ConstraintId,
            command.Statement.Trim(),
            command.Source.Trim(),
            command.Scope.Select(entry => entry.Trim()).ToArray(),
            ConstraintStatus.Active,
            new Provenance(command.ActorId, now, "constraint.add"));
        return [new ConstraintAdded(constraint)];
    }

    private static IReadOnlyList<LedgerEventData> SupersedeConstraint(
        GovernedTaskState state,
        SupersedeConstraintCommand command)
    {
        var constraint = Get(state.Constraints, command.ConstraintId, "constraint");
        if (constraint.Status != ConstraintStatus.Active)
        {
            throw new GovernanceException("Only an active constraint can be superseded.");
        }

        return [new ConstraintSuperseded(command.ConstraintId)];
    }

    private static IReadOnlyList<LedgerEventData> CompleteWorkItem(
        GovernedTaskState state,
        CompleteWorkItemCommand command)
    {
        var workItem = Get(state.WorkItems, command.WorkItemId, "work item");
        if (workItem.Status is WorkItemStatus.Completed)
        {
            throw new GovernanceException("Work item is already completed.");
        }

        // Kept out of the "repair it first" refusal below: an abandoned item is not damaged and
        // there is nothing to repair. It was given up, and giving up is as terminal as finishing.
        if (workItem.Status is WorkItemStatus.Abandoned)
        {
            throw new GovernanceException(
                "An abandoned work item cannot be completed. Add a new work item instead of reviving this one.");
        }

        if (workItem.Status is WorkItemStatus.Blocked or WorkItemStatus.Stale)
        {
            throw new GovernanceException(
                $"Work item in status '{workItem.Status}' cannot be completed before it is repaired.");
        }

        // A provider exiting zero is not the same claim as the work being done, so the
        // run has to be closed before anyone can assert completion.
        if (HasOpenRun(state, command.WorkItemId))
        {
            throw new GovernanceException("Work item cannot be completed while a run is still active.");
        }

        if (HasOpenEscalation(state, command.WorkItemId))
        {
            throw new GovernanceException("Work item cannot be completed while an escalation on it is open.");
        }

        // The actor that did the work asserting the work is done is the same actor grading its own
        // homework, and a provider exiting zero says nothing either. Something adversarial has to
        // have looked at it. An operator may still overrule that, but only on the record.
        var waiver = TrimOrNull(command.WithoutVerificationReason);
        if (command.WithoutVerificationReason is not null && waiver is null)
        {
            throw new GovernanceException(
                "Waiving the required runs needs a reason; a blank waiver records nothing.");
        }

        if (waiver is null)
        {
            // A work item completed with no run at all means the kernel never saw who did the work,
            // or whether anyone did. Every work item completed in this task so far is that shape,
            // which is exactly how the gap went unnoticed for as long as it did.
            if (!HasCompletedWorkingRun(state, command.WorkItemId))
            {
                throw new GovernanceException(
                    $"Work item '{command.WorkItemId}' has no completed run by a working role and cannot be " +
                    "completed. An operator may complete it without one by recording why.");
            }

            if (!HasCompletedVerifierRun(state, command.WorkItemId))
            {
                throw new GovernanceException(
                    $"Work item '{command.WorkItemId}' has no completed verifier run and cannot be completed. " +
                    "An operator may complete it without one by recording why.");
            }

            // Separate from the check above so the refusal says which of the two failed. Having
            // been verified at some point and having been verified as finished are different
            // claims, and only the second one is worth anything.
            if (!HasVerifierRunAfterLatestWork(state, command.WorkItemId))
            {
                throw new GovernanceException(
                    $"Work item '{command.WorkItemId}' was verified before its latest working run finished, " +
                    "so no verifier run has seen the finished work. Verify it again, or an operator may " +
                    "complete it without doing so by recording why.");
            }
        }
        else if (!IsOperator(state, command.ActorId))
        {
            throw new GovernanceException("Only an operator can complete work without the required runs.");
        }

        return [new WorkItemCompleted(command.WorkItemId, waiver)];
    }

    // Work is released as well as finished. An item that turned out to be a dead end could not be
    // completed and could not be given up either, so it held its directory area against everyone
    // else for the rest of the task.
    private static IReadOnlyList<LedgerEventData> AbandonWorkItem(
        GovernedTaskState state,
        AbandonWorkItemCommand command)
    {
        var workItem = Get(state.WorkItems, command.WorkItemId, "work item");
        RequireText(command.Reason, nameof(command.Reason));
        // Stale is here for a reason of its own: an already abandoned item that a later claim
        // rejection moved to Stale could be abandoned a second time, and the second reason
        // overwrote the first. The record of why an area was given up is the only thing left of
        // the work, so nothing may quietly replace it.
        if (workItem.Status is WorkItemStatus.Completed or WorkItemStatus.Stale)
        {
            throw new GovernanceException("A completed or stale work item cannot be abandoned.");
        }

        if (workItem.Status is WorkItemStatus.Abandoned)
        {
            throw new GovernanceException("Work item is already abandoned.");
        }

        // Same reason completion waits for the run to close: releasing the area under a live agent
        // would hand it to a second one while the first is still writing in it.
        if (HasOpenRun(state, command.WorkItemId))
        {
            throw new GovernanceException("Work item cannot be abandoned while a run is still active.");
        }

        // Abandoning is as terminal as completing, so it carries completion's escalation rule too.
        // An open escalation against an item nobody will ever work on is a question the log says is
        // still live and that nothing will ever answer: archive checks only active runs and open
        // challenges, so it would sit there forever. The operator holds both commands, and
        // withdrawing the escalation first says out loud that the question stopped mattering.
        if (HasOpenEscalation(state, command.WorkItemId))
        {
            throw new GovernanceException("Work item cannot be abandoned while an escalation on it is open.");
        }

        return [new WorkItemAbandoned(command.WorkItemId, command.Reason.Trim())];
    }

    private static IReadOnlyList<LedgerEventData> BlockWorkItem(
        GovernedTaskState state,
        BlockWorkItemCommand command)
    {
        var workItem = Get(state.WorkItems, command.WorkItemId, "work item");
        RequireText(command.Reason, nameof(command.Reason));
        // Blocked is a live status that holds the item's area, so blocking a released item takes
        // that area back from whoever has since been given it. Stale belongs here with the other
        // two: it was the way back in, because a rejected claim moves a Completed or Abandoned item
        // to Stale and blocking it from there revived it on top of its successor. Refusing it also
        // fixes an older trap — a stale item that got blocked could never be unblocked, since
        // UnblockWorkItem refuses an item whose claim is rejected, so it held its area forever.
        if (workItem.Status is WorkItemStatus.Completed or WorkItemStatus.Abandoned or WorkItemStatus.Stale)
        {
            throw new GovernanceException("A completed, abandoned or stale work item cannot be blocked.");
        }

        if (command.EscalationId is { } escalationId)
        {
            var escalation = Get(state.Escalations, escalationId, "escalation");
            if (escalation.Status != EscalationStatus.Open)
            {
                throw new GovernanceException("A work item can only be blocked on an open escalation.");
            }
        }

        return [new WorkItemBlocked(command.WorkItemId, command.Reason.Trim(), command.EscalationId)];
    }

    private static IReadOnlyList<LedgerEventData> UnblockWorkItem(
        GovernedTaskState state,
        UnblockWorkItemCommand command)
    {
        var workItem = Get(state.WorkItems, command.WorkItemId, "work item");
        if (workItem.Status != WorkItemStatus.Blocked)
        {
            throw new GovernanceException($"Only a blocked work item can be unblocked; this one is '{workItem.Status}'.");
        }

        // Unblocking must not undo causal invalidation. A rejected claim is terminal, so work
        // resting on one is repaired by a replacement item, not by clearing the block.
        if (workItem.DependsOnClaims
            .Select(claimId => state.Claims[claimId])
            .FirstOrDefault(claim => claim.Status is ClaimStatus.Rejected or ClaimStatus.Superseded) is { } invalid)
        {
            throw new GovernanceException(
                $"Work item depends on '{invalid.Status.ToString().ToLowerInvariant()}' claim '{invalid.Id}'. " +
                "Add a replacement work item on a current claim instead of unblocking this one.");
        }

        return [new WorkItemUnblocked(command.WorkItemId)];
    }

    private static bool HasOpenRun(GovernedTaskState state, WorkItemId workItemId) =>
        state.Runs.Values.Any(run =>
            run.WorkItemId == workItemId && run.Status is AgentRunStatus.Active);

    // Every role that is not judging the work is doing it. Naming the two judging roles rather than
    // listing the working ones means a role added later counts as work by default, which is the
    // safe direction: a new role failing to satisfy this would block completion for no good reason.
    // A null SubjectRole does not count — it predates the field, so the kernel did not see the role
    // and cannot now claim it did.
    private static bool HasCompletedWorkingRun(GovernedTaskState state, WorkItemId workItemId) =>
        state.Runs.Values.Any(run =>
            run.WorkItemId == workItemId &&
            run.Status is AgentRunStatus.Completed &&
            run.SubjectRole is not null and not (RoleKind.Verifier or RoleKind.CodeReviewer));

    // The run has to have reached Completed, not merely ended: a verifier whose run failed or was
    // cancelled looked at nothing, and reading the actor's present role instead of the role the run
    // recorded would let a later re-assignment turn any old run into a verification.
    private static bool HasCompletedVerifierRun(GovernedTaskState state, WorkItemId workItemId) =>
        state.Runs.Values.Any(run =>
            run.WorkItemId == workItemId &&
            run.Status is AgentRunStatus.Completed &&
            run.SubjectRole is RoleKind.Verifier);

    // The LATEST completed working run, not the earliest. Work done after a verification was not
    // covered by it, so the verifier has to have finished after the last thing it was meant to
    // check. Comparing against the earliest would let an agent verify, keep working, and complete.
    private static DateTimeOffset? LatestCompletedWorkingRunEnd(GovernedTaskState state, WorkItemId workItemId) =>
        state.Runs.Values
            .Where(run => run.WorkItemId == workItemId &&
                          run.Status is AgentRunStatus.Completed &&
                          run.SubjectRole is not null and not (RoleKind.Verifier or RoleKind.CodeReviewer))
            .Max(run => run.EndedAt);

    // Existence was never the question the gate meant to ask. A verifier run that ended before the
    // work did read an unfinished work item and proves nothing about the finished one. Equal
    // timestamps pass, matching how run completion already compares EndedAt against StartedAt.
    private static bool HasVerifierRunAfterLatestWork(GovernedTaskState state, WorkItemId workItemId)
    {
        var workedAt = LatestCompletedWorkingRunEnd(state, workItemId);
        return state.Runs.Values.Any(run =>
            run.WorkItemId == workItemId &&
            run.Status is AgentRunStatus.Completed &&
            run.SubjectRole is RoleKind.Verifier &&
            (workedAt is null || run.EndedAt >= workedAt));
    }

    private static bool HasOpenEscalation(GovernedTaskState state, WorkItemId workItemId) =>
        state.Escalations.Values.Any(escalation =>
            escalation.WorkItemId == workItemId && escalation.Status == EscalationStatus.Open);

    private CommandOutcome ApplyEvents(
        GovernedTaskState? initialState,
        LedgerCommand command,
        IReadOnlyList<LedgerEventData> eventData,
        DateTimeOffset now)
    {
        var taskId = initialState?.TaskId ?? ((OpenTaskCommand)command).TaskId;
        var state = initialState;
        var events = new List<LedgerEvent>(eventData.Count);
        LedgerEvent? previous = null;

        for (var index = 0; index < eventData.Count; index++)
        {
            var @event = new LedgerEvent(
                GovernedTaskState.CurrentSchemaVersion,
                CreateEventId(taskId, state?.Version ?? 0),
                taskId,
                command.ActorId,
                now,
                previous?.EventId ?? command.CausationId,
                command.CorrelationId.Trim(),
                eventData[index]);

            state = _reducer.Apply(state, @event);
            events.Add(@event);
            previous = @event;
        }

        return new CommandOutcome(state!, events);
    }

    private static EventId CreateEventId(TaskId taskId, long currentVersion) =>
        new($"{taskId.Value}:{currentVersion + 1:D10}");

    private static void ValidateEnvelope(LedgerCommand command)
    {
        RequireId(command.ActorId.Value, nameof(command.ActorId));
        RequireText(command.CorrelationId, nameof(command.CorrelationId));
        if (command.CausationId is { } causationId)
        {
            RequireId(causationId.Value, nameof(command.CausationId));
        }
    }

    private static void ValidateEnums(LedgerCommand command)
    {
        switch (command)
        {
            case AssignRoleCommand assign:
                RequireDefined(assign.Role, nameof(assign.Role));
                foreach (var capability in assign.Capabilities)
                {
                    RequireDefined(capability, nameof(assign.Capabilities));
                }
                break;
            case ResolveClaimCommand resolve:
                RequireDefined(resolve.Status, nameof(resolve.Status));
                break;
            case ResolveDecisionCommand resolve:
                RequireDefined(resolve.Status, nameof(resolve.Status));
                break;
            case DisposeChallengeCommand dispose:
                RequireDefined(dispose.Status, nameof(dispose.Status));
                break;
            case CompleteRunCommand complete:
                RequireDefined(complete.Status, nameof(complete.Status));
                break;
            case RequestStageTransitionCommand transition:
                RequireDefined(transition.TargetStage, nameof(transition.TargetStage));
                break;
            case RaiseEscalationCommand raise:
                RequireDefined(raise.Kind, nameof(raise.Kind));
                break;
            case ResolveEscalationCommand resolve:
                RequireDefined(resolve.Status, nameof(resolve.Status));
                break;
            case MarkLessonBearingCommand mark:
                RequireDefined(mark.SourceKind, nameof(mark.SourceKind));
                break;
        }
    }

    private static void RequireDefined<TEnum>(TEnum value, string field) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new GovernanceException($"'{value}' is not a defined {field} value.");
        }
    }

    private static void AddDependencyInvalidations(
        GovernedTaskState state,
        ClaimId claimId,
        ICollection<LedgerEventData> events)
    {
        foreach (var decision in state.Decisions.Values
                     .Where(item => item.DependsOnClaims.Contains(claimId))
                     .Where(item => item.Status is DecisionStatus.Proposed or DecisionStatus.Accepted)
                     .OrderBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            events.Add(new DecisionInvalidated(decision.Id, claimId));
        }

        foreach (var workItem in state.WorkItems.Values
                     .Where(item => item.DependsOnClaims.Contains(claimId))
                     // R3 (workitem-blocked-conflation): an operator `work block` sets the same
                     // Blocked member as invalidation does. Skipping Blocked here would mean a
                     // manually blocked item never goes Stale when its claim is later rejected.
                     .Where(item => item.Status is not WorkItemStatus.Stale)
                     .OrderBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            var status = workItem.Status == WorkItemStatus.Active
                ? WorkItemStatus.Blocked
                : WorkItemStatus.Stale;
            events.Add(new WorkItemInvalidated(workItem.Id, claimId, status));
        }
    }

    // Disjoint work items were only ever disjoint by assertion. An area is occupied while the work
    // item holding it is still live, so a second actor cannot claim it and redo the same work.
    private static void EnsureScopeIsNotAlreadyOccupied(
        GovernedTaskState state,
        WorkItemId workItemId,
        IReadOnlyList<string> requestedScope)
    {
        foreach (var existing in state.WorkItems.Values
                     .Where(item => item.Id != workItemId)
                     // Which statuses release an area is this rule written twice: the second copy is
                     // the `occupied` query in CliApplication.WriteWhoAsync. They must change
                     // together. Apart, `who` shows an operator an area as taken that `work add`
                     // hands to someone else in the next command, or the reverse.
                     .Where(item => item.Status is not (WorkItemStatus.Completed or WorkItemStatus.Stale
                         or WorkItemStatus.Abandoned))
                     .OrderBy(item => item.Id.Value, StringComparer.Ordinal))
        {
            foreach (var held in existing.ResourceScope)
            {
                var clash = requestedScope.FirstOrDefault(requested => PathsOverlap(requested.Trim(), held));
                if (clash is not null)
                {
                    throw new GovernanceException(
                        $"Scope '{clash}' overlaps '{held}', already held by work item '{existing.Id}' in status " +
                        $"'{existing.Status}'. Complete or supersede that item, or narrow this scope.");
                }
            }
        }
    }

    private static bool PathsOverlap(string first, string second) =>
        IsSameOrInside(first, second) || IsSameOrInside(second, first);

    private static bool IsSameOrInside(string candidate, string container)
    {
        if (string.Equals(candidate, container, StringComparison.Ordinal))
        {
            return true;
        }

        var prefix = container.EndsWith(Path.DirectorySeparatorChar) ? container : container + Path.DirectorySeparatorChar;
        return candidate.StartsWith(prefix, StringComparison.Ordinal);
    }

    private static void EnsureDependenciesAreCurrent(
        GovernedTaskState state,
        IEnumerable<ClaimId> claimIds)
    {
        var invalid = claimIds
            .Select(claimId => state.Claims[claimId])
            .FirstOrDefault(claim => claim.Status is ClaimStatus.Rejected or ClaimStatus.Superseded);
        if (invalid is not null)
        {
            throw new GovernanceException(
                $"Dependency claim '{invalid.Id}' is '{invalid.Status}' and cannot support new or actionable work.");
        }
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

    private static void EnsureStagePrerequisites(GovernedTaskState state, TaskStage target)
    {
        if (target == TaskStage.Execution && state.WorkItems.Count == 0)
        {
            throw new GovernanceException("Execution requires at least one governed work item.");
        }

        if (target == TaskStage.Archive)
        {
            if (state.Runs.Values.Any(run => run.Status is AgentRunStatus.Active))
            {
                throw new GovernanceException("A task with active runs cannot be archived.");
            }

            if (state.Challenges.Values.Any(challenge => challenge.Status == ChallengeStatus.Open))
            {
                throw new GovernanceException("A task with open challenges cannot be archived.");
            }
        }
    }

    private static void EnsureChallengeTargetExists(GovernedTaskState state, string targetType, string targetId)
    {
        var exists = targetType.Trim().ToLowerInvariant() switch
        {
            "claim" => state.Claims.Keys.Any(id => id.Value == targetId.Trim()),
            "decision" => state.Decisions.Keys.Any(id => id.Value == targetId.Trim()),
            "work" or "workitem" or "work-item" => state.WorkItems.Keys.Any(id => id.Value == targetId.Trim()),
            _ => throw new GovernanceException($"Unsupported challenge target type '{targetType}'.")
        };

        if (!exists)
        {
            throw new GovernanceException($"Challenge target '{targetType}:{targetId}' does not exist.");
        }
    }

    private static IReadOnlyList<Capability> DistinctCapabilities(IReadOnlyList<Capability> capabilities)
    {
        EnsureUnique(capabilities, "Capabilities");
        return capabilities.OrderBy(value => value).ToArray();
    }

    private static TValue Get<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> values,
        TKey id,
        string kind)
        where TKey : notnull
    {
        if (!values.TryGetValue(id, out var value))
        {
            throw new GovernanceException($"Unknown {kind} '{id}'.");
        }

        return value;
    }

    private static void EnsureNew<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> values,
        TKey id,
        string kind)
        where TKey : notnull
    {
        if (values.ContainsKey(id))
        {
            throw new GovernanceException($"A {kind} with ID '{id}' already exists.");
        }
    }

    private static void EnsureReferencesExist<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> values,
        IEnumerable<TKey> ids,
        string kind)
        where TKey : notnull
    {
        foreach (var id in ids)
        {
            _ = Get(values, id, kind);
        }
    }

    private static void EnsureUnique<T>(IReadOnlyList<T> values, string label, IEqualityComparer<T>? comparer = null)
    {
        if (values.Count != values.Distinct(comparer).Count())
        {
            throw new GovernanceException($"{label} cannot contain duplicates.");
        }
    }

    private static void RequireText(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new GovernanceException($"{name} is required.");
        }
    }

    private static void RequireId(string value, string name) => RequireText(value, name);

    private static string? TrimOrNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
