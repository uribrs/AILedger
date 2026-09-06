using AILedger.Core.Contracts;

namespace AILedger.Core.Domain;

internal static class TaskTransitionValidator
{
    private const string TaskOpenSource = "task.open";

    public static void Validate(GovernedTaskState? state, LedgerEvent @event)
    {
        ValidateEventMetadata(@event);

        switch (@event.Data)
        {
            case TaskOpened opened:
                ValidateTaskOpened(opened);
                break;
            case RoleAssigned assigned:
                ValidateRoleAssigned(Require(state), @event, assigned.Assignment);
                break;
            case ClaimAdded added:
                ValidateClaimAdded(Require(state), @event, added.Claim);
                break;
            case ClaimResolved resolved:
                ValidateClaimResolved(Require(state), @event, resolved);
                break;
            case EvidenceAdded added:
                ValidateEvidenceAdded(Require(state), @event, added.Evidence);
                break;
            case DecisionProposed proposed:
                ValidateDecisionProposed(Require(state), @event, proposed.Decision);
                break;
            case DecisionResolved resolved:
                ValidateDecisionResolved(Require(state), @event, resolved);
                break;
            case DecisionInvalidated invalidated:
                ValidateDecisionInvalidated(Require(state), @event, invalidated);
                break;
            case ChallengeRaised raised:
                ValidateChallengeRaised(Require(state), @event, raised.Challenge);
                break;
            case ChallengeDisposed disposed:
                ValidateChallengeDisposed(Require(state), @event, disposed);
                break;
            case WorkItemAdded added:
                ValidateWorkItemAdded(Require(state), @event, added.WorkItem);
                break;
            case WorkItemInvalidated invalidated:
                ValidateWorkItemInvalidated(Require(state), @event, invalidated);
                break;
            case RunStarted started:
                ValidateRunStarted(Require(state), @event, started.Run);
                break;
            case RunCompleted completed:
                ValidateRunCompleted(Require(state), @event, completed);
                break;
            case StageTransitioned transitioned:
                ValidateStageTransitioned(Require(state), @event, transitioned);
                break;
            case EscalationRaised raised:
                ValidateEscalationRaised(Require(state), @event, raised.Escalation);
                break;
            case EscalationResolved resolved:
                ValidateEscalationResolved(Require(state), @event, resolved);
                break;
            case AlternativeRecorded recorded:
                ValidateAlternativeRecorded(Require(state), @event, recorded.Alternative);
                break;
            case ConstraintAdded added:
                ValidateConstraintAdded(Require(state), @event, added.Constraint);
                break;
            case ConstraintSuperseded superseded:
                ValidateConstraintSuperseded(Require(state), @event, superseded);
                break;
            case WorkItemCompleted completed:
                ValidateWorkItemCompleted(Require(state), @event, completed);
                break;
            case WorkItemBlocked blocked:
                ValidateWorkItemBlocked(Require(state), @event, blocked);
                break;
            case WorkItemUnblocked unblocked:
                ValidateWorkItemUnblocked(Require(state), @event, unblocked);
                break;
            default:
                throw new GovernanceException($"Unsupported event data '{@event.Data.GetType().Name}'.");
        }
    }

    private static void ValidateEventMetadata(LedgerEvent @event)
    {
        RequireId(@event.EventId.Value, nameof(@event.EventId));
        RequireId(@event.TaskId.Value, nameof(@event.TaskId));
        RequireId(@event.ActorId.Value, nameof(@event.ActorId));
        RequireText(@event.CorrelationId, nameof(@event.CorrelationId));
        if (@event.RecordedAt == default)
        {
            throw new GovernanceException("Event recorded-at metadata is required.");
        }
    }

    private static void ValidateTaskOpened(TaskOpened opened)
    {
        RequireText(opened.Title, nameof(opened.Title));
        RequireText(opened.Goal, nameof(opened.Goal));
    }

    private static void ValidateRoleAssigned(
        GovernedTaskState state,
        LedgerEvent @event,
        RoleAssignment assignment)
    {
        RequireId(assignment.ActorId.Value, nameof(assignment.ActorId));
        RequireDefined(assignment.Role, nameof(assignment.Role));
        EnsureUnique(assignment.Capabilities, "Role capabilities");
        foreach (var capability in assignment.Capabilities)
        {
            RequireDefined(capability, nameof(assignment.Capabilities));
        }

        if (IsOpeningRole(state, @event))
        {
            ValidateOpeningRole(@event, assignment);
            return;
        }

        RequireAuthority(state, @event.ActorId, Capability.ManageRoles, operatorRequired: true);
        if (assignment.ActorId == @event.ActorId)
        {
            throw new GovernanceException("Actors cannot assign or expand their own authority.");
        }

        if (assignment.Role != RoleKind.Operator &&
            assignment.Capabilities.Any(capability => capability is Capability.ManageRoles or Capability.ManageScope))
        {
            throw new GovernanceException("Only an operator role can receive role-management or scope-management authority.");
        }

        ValidateProvenance(@event, assignment.AssignedBy, "actor.assign-role");
    }

    private static bool IsOpeningRole(GovernedTaskState state, LedgerEvent @event) =>
        state.Version == 1 &&
        state.Roles.Count == 0 &&
        state.PendingOpeningActor == @event.ActorId;

    private static void ValidateOpeningRole(LedgerEvent @event, RoleAssignment assignment)
    {
        if (assignment.ActorId != @event.ActorId || assignment.Role != RoleKind.Operator)
        {
            throw new GovernanceException("The opening role must grant the task-opening actor the operator role.");
        }

        var allCapabilities = Enum.GetValues<Capability>();
        if (assignment.Capabilities.Count != allCapabilities.Length ||
            allCapabilities.Except(assignment.Capabilities).Any())
        {
            throw new GovernanceException("The opening operator role must contain every capability.");
        }

        ValidateProvenance(@event, assignment.AssignedBy, TaskOpenSource);
    }

    private static void ValidateClaimAdded(GovernedTaskState state, LedgerEvent @event, Claim claim)
    {
        RequireAuthority(state, @event.ActorId, Capability.AddClaim);
        EnsureNew(state.Claims, claim.Id, "claim");
        RequireId(claim.Id.Value, nameof(claim.Id));
        RequireText(claim.Statement, nameof(claim.Statement));
        RequireDefined(claim.Status, nameof(claim.Status));
        if (claim.Status != ClaimStatus.Open || claim.EvidenceIds.Count != 0)
        {
            throw new GovernanceException("A newly added claim must be open and have no resolved evidence.");
        }

        ValidateProvenance(@event, claim.Provenance, "claim.add");
    }

    private static void ValidateClaimResolved(
        GovernedTaskState state,
        LedgerEvent @event,
        ClaimResolved resolved)
    {
        RequireAuthority(state, @event.ActorId, Capability.ResolveClaim);
        RequireDefined(resolved.Status, nameof(resolved.Status));
        var claim = Get(state.Claims, resolved.ClaimId, "claim");
        EnsureClaimResolution(claim, resolved.Status);
        EnsureUnique(resolved.EvidenceIds, "Evidence IDs");
        EnsureReferencesExist(state.Evidence, resolved.EvidenceIds, "evidence");

        if (resolved.Status is ClaimStatus.Validated or ClaimStatus.Rejected && resolved.EvidenceIds.Count == 0)
        {
            throw new GovernanceException($"A {resolved.Status.ToString().ToLowerInvariant()} claim requires evidence.");
        }

        foreach (var evidenceId in resolved.EvidenceIds)
        {
            var evidence = state.Evidence[evidenceId];
            var directionMatches = resolved.Status switch
            {
                ClaimStatus.Validated => evidence.Supports.Contains(resolved.ClaimId),
                ClaimStatus.Rejected => evidence.Refutes.Contains(resolved.ClaimId),
                _ => true
            };

            if (!directionMatches)
            {
                throw new GovernanceException(
                    $"Evidence '{evidenceId}' does not support the requested '{resolved.Status}' claim transition.");
            }
        }
    }

    private static void ValidateEvidenceAdded(GovernedTaskState state, LedgerEvent @event, Evidence evidence)
    {
        RequireAuthority(state, @event.ActorId, Capability.AddEvidence);
        EnsureNew(state.Evidence, evidence.Id, "evidence");
        RequireId(evidence.Id.Value, nameof(evidence.Id));
        RequireText(evidence.SourceType, nameof(evidence.SourceType));
        RequireText(evidence.Citation, nameof(evidence.Citation));
        RequireText(evidence.Summary, nameof(evidence.Summary));
        EnsureUnique(evidence.Supports, "Supported claim IDs");
        EnsureUnique(evidence.Refutes, "Refuted claim IDs");
        EnsureReferencesExist(state.Claims, evidence.Supports, "claim");
        EnsureReferencesExist(state.Claims, evidence.Refutes, "claim");
        if (evidence.Supports.Intersect(evidence.Refutes).Any())
        {
            throw new GovernanceException("The same evidence cannot both support and refute a claim.");
        }

        ValidateProvenance(@event, evidence.Provenance, "evidence.add");
    }

    private static void ValidateDecisionProposed(GovernedTaskState state, LedgerEvent @event, Decision decision)
    {
        RequireAuthority(state, @event.ActorId, Capability.ProposeDecision);
        EnsureNew(state.Decisions, decision.Id, "decision");
        RequireId(decision.Id.Value, nameof(decision.Id));
        RequireText(decision.Statement, nameof(decision.Statement));
        RequireText(decision.Rationale, nameof(decision.Rationale));
        RequireDefined(decision.Status, nameof(decision.Status));
        if (decision.Status != DecisionStatus.Proposed)
        {
            throw new GovernanceException("A newly proposed decision must have proposed status.");
        }

        EnsureUnique(decision.DependsOnClaims, "Dependent claim IDs");
        EnsureReferencesExist(state.Claims, decision.DependsOnClaims, "claim");
        EnsureDependenciesAreCurrent(state, decision.DependsOnClaims);
        if (decision.Supersedes is { } supersededId)
        {
            if (supersededId == decision.Id)
            {
                throw new GovernanceException("A decision cannot supersede itself.");
            }

            EnsureDecisionIsCurrent(Get(state.Decisions, supersededId, "decision"));
        }

        ValidateProvenance(@event, decision.Provenance, "decision.propose");
    }

    private static void ValidateDecisionResolved(
        GovernedTaskState state,
        LedgerEvent @event,
        DecisionResolved resolved)
    {
        RequireAuthority(state, @event.ActorId, Capability.ResolveDecision);
        RequireDefined(resolved.Status, nameof(resolved.Status));
        if (resolved.Status is not (DecisionStatus.Accepted or DecisionStatus.Superseded))
        {
            throw new GovernanceException("A decision may be explicitly accepted or superseded; invalidation is causal.");
        }

        var decision = Get(state.Decisions, resolved.DecisionId, "decision");
        if (decision.Status == DecisionStatus.Proposed)
        {
            if (resolved.Status == DecisionStatus.Accepted)
            {
                EnsureDependenciesAreCurrent(state, decision.DependsOnClaims);
                if (decision.Supersedes is { } predecessorId)
                {
                    EnsureDecisionIsCurrent(Get(state.Decisions, predecessorId, "decision"));
                    if (state.Decisions.Values.Any(candidate =>
                            candidate.Status == DecisionStatus.Accepted &&
                            candidate.Supersedes == predecessorId))
                    {
                        throw new GovernanceException(
                            $"Decision '{predecessorId}' already has an accepted replacement.");
                    }
                }
            }

            return;
        }

        if (decision.Status == DecisionStatus.Accepted &&
            resolved.Status == DecisionStatus.Superseded &&
            state.Decisions.Values.Any(candidate =>
                candidate.Status == DecisionStatus.Accepted && candidate.Supersedes == decision.Id))
        {
            return;
        }

        throw new GovernanceException(
            $"Decision in status '{decision.Status}' cannot transition to '{resolved.Status}'.");
    }

    private static void ValidateDecisionInvalidated(
        GovernedTaskState state,
        LedgerEvent @event,
        DecisionInvalidated invalidated)
    {
        RequireAuthority(state, @event.ActorId, Capability.ResolveClaim);
        var decision = Get(state.Decisions, invalidated.DecisionId, "decision");
        EnsureDecisionIsCurrent(decision);
        var claim = Get(state.Claims, invalidated.RejectedClaimId, "claim");
        if (claim.Status is not (ClaimStatus.Rejected or ClaimStatus.Superseded) ||
            !decision.DependsOnClaims.Contains(claim.Id))
        {
            throw new GovernanceException("A decision can only be invalidated by one of its rejected or superseded claims.");
        }
    }

    private static void ValidateChallengeRaised(GovernedTaskState state, LedgerEvent @event, Challenge challenge)
    {
        RequireAuthority(state, @event.ActorId, Capability.RaiseChallenge);
        EnsureNew(state.Challenges, challenge.Id, "challenge");
        RequireId(challenge.Id.Value, nameof(challenge.Id));
        RequireText(challenge.TargetType, nameof(challenge.TargetType));
        RequireText(challenge.TargetId, nameof(challenge.TargetId));
        RequireText(challenge.Reason, nameof(challenge.Reason));
        RequireDefined(challenge.Status, nameof(challenge.Status));
        if (challenge.Status != ChallengeStatus.Open)
        {
            throw new GovernanceException("A newly raised challenge must be open.");
        }

        EnsureUnique(challenge.EvidenceIds, "Evidence IDs");
        EnsureReferencesExist(state.Evidence, challenge.EvidenceIds, "evidence");
        EnsureChallengeTargetExists(state, challenge.TargetType, challenge.TargetId);
        ValidateProvenance(@event, challenge.Provenance, "challenge.raise");
    }

    private static void ValidateChallengeDisposed(
        GovernedTaskState state,
        LedgerEvent @event,
        ChallengeDisposed disposed)
    {
        RequireAuthority(state, @event.ActorId, Capability.DisposeChallenge);
        RequireDefined(disposed.Status, nameof(disposed.Status));
        var challenge = Get(state.Challenges, disposed.ChallengeId, "challenge");
        if (challenge.Status != ChallengeStatus.Open || disposed.Status == ChallengeStatus.Open)
        {
            throw new GovernanceException("Only an open challenge can transition to a terminal disposition.");
        }
    }

    private static void ValidateWorkItemAdded(GovernedTaskState state, LedgerEvent @event, WorkItem workItem)
    {
        RequireAuthority(state, @event.ActorId, Capability.ManageWork, operatorRequired: true);
        RequireAuthority(state, @event.ActorId, Capability.ManageScope, operatorRequired: true);
        EnsureNew(state.WorkItems, workItem.Id, "work item");
        RequireId(workItem.Id.Value, nameof(workItem.Id));
        RequireText(workItem.Title, nameof(workItem.Title));
        RequireDefined(workItem.Status, nameof(workItem.Status));
        if (workItem.Status != WorkItemStatus.Proposed)
        {
            throw new GovernanceException("A newly added work item must have proposed status.");
        }

        EnsureUnique(workItem.DependsOnClaims, "Dependent claim IDs");
        EnsureReferencesExist(state.Claims, workItem.DependsOnClaims, "claim");
        EnsureDependenciesAreCurrent(state, workItem.DependsOnClaims);
        EnsureUnique(workItem.ResourceScope, "Resource scope entries", StringComparer.Ordinal);
        if (workItem.ResourceScope.Any(string.IsNullOrWhiteSpace) ||
            workItem.ResourceScope.Any(scope => !Path.IsPathFullyQualified(scope)))
        {
            throw new GovernanceException("Resource scope entries must be non-empty absolute paths.");
        }

        if (workItem.Owner is { } owner && !state.Roles.ContainsKey(owner))
        {
            throw new GovernanceException($"Work owner '{owner}' has no assigned role.");
        }
    }

    private static void ValidateWorkItemInvalidated(
        GovernedTaskState state,
        LedgerEvent @event,
        WorkItemInvalidated invalidated)
    {
        RequireAuthority(state, @event.ActorId, Capability.ResolveClaim);
        RequireDefined(invalidated.Status, nameof(invalidated.Status));
        var workItem = Get(state.WorkItems, invalidated.WorkItemId, "work item");
        // R3 (workitem-blocked-conflation): mirrors the CommandHandler filter. A Blocked item
        // may have been blocked by the operator rather than by invalidation, so only Stale is
        // proof that invalidation already covered it.
        if (workItem.Status is WorkItemStatus.Stale)
        {
            throw new GovernanceException("An already invalidated work item cannot be invalidated again.");
        }

        var expectedStatus = workItem.Status == WorkItemStatus.Active
            ? WorkItemStatus.Blocked
            : WorkItemStatus.Stale;
        if (invalidated.Status != expectedStatus)
        {
            throw new GovernanceException($"Work item invalidation must transition to '{expectedStatus}'.");
        }

        var claim = Get(state.Claims, invalidated.RejectedClaimId, "claim");
        if (claim.Status is not (ClaimStatus.Rejected or ClaimStatus.Superseded) ||
            !workItem.DependsOnClaims.Contains(claim.Id))
        {
            throw new GovernanceException("A work item can only be invalidated by one of its rejected or superseded claims.");
        }
    }

    private static void ValidateRunStarted(GovernedTaskState state, LedgerEvent @event, AgentRun run)
    {
        RequireAuthority(state, @event.ActorId, Capability.ManageRuns);
        EnsureNew(state.Runs, run.Id, "run");
        RequireId(run.Id.Value, nameof(run.Id));
        RequireText(run.Provider, nameof(run.Provider));
        RequireDefined(run.Status, nameof(run.Status));
        if (run.ActorId != @event.ActorId || run.Status != AgentRunStatus.Active ||
            run.StartedAt != @event.RecordedAt || run.EndedAt is not null)
        {
            throw new GovernanceException("A started run must belong to the event actor and begin active at the event timestamp.");
        }

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

        if (workItem.Status is WorkItemStatus.Blocked or WorkItemStatus.Stale or WorkItemStatus.Completed)
        {
            throw new GovernanceException($"Cannot start a run for work item in status '{workItem.Status}'.");
        }

        if (state.Runs.Values.Any(current =>
                current.WorkItemId == workItemId && current.Status is AgentRunStatus.Pending or AgentRunStatus.Active))
        {
            throw new GovernanceException($"Work item '{workItemId}' already has an active orchestration run.");
        }
    }

    private static void ValidateRunCompleted(
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

        if (run.Status is not (AgentRunStatus.Active or AgentRunStatus.Pending) ||
            completed.Status is AgentRunStatus.Active or AgentRunStatus.Pending)
        {
            throw new GovernanceException("Only an active or pending run can transition to a terminal status.");
        }

        if (completed.EndedAt != @event.RecordedAt || completed.EndedAt < run.StartedAt)
        {
            throw new GovernanceException("Run completion time must match the event timestamp and follow run start.");
        }

        if (run.ProviderSessionId is not null && completed.ProviderSessionId != run.ProviderSessionId)
        {
            throw new GovernanceException("Run completion session identity does not match the started run.");
        }
    }

    private static void ValidateStageTransitioned(
        GovernedTaskState state,
        LedgerEvent @event,
        StageTransitioned transitioned)
    {
        RequireAuthority(state, @event.ActorId, Capability.RequestTransition);
        RequireDefined(transitioned.Previous, nameof(transitioned.Previous));
        RequireDefined(transitioned.Current, nameof(transitioned.Current));
        if (state.Stage != transitioned.Previous)
        {
            throw new GovernanceException(
                $"Stage transition expected '{transitioned.Previous}', but task is in '{state.Stage}'.");
        }

        StageTransitionPolicy.EnsureAllowed(transitioned.Previous, transitioned.Current);
        EnsureStagePrerequisites(state, transitioned.Current);
    }

    private static void ValidateEscalationRaised(GovernedTaskState state, LedgerEvent @event, Escalation escalation)
    {
        RequireAuthority(state, @event.ActorId, Capability.RaiseEscalation);
        EnsureNew(state.Escalations, escalation.Id, "escalation");
        RequireId(escalation.Id.Value, nameof(escalation.Id));
        RequireText(escalation.Question, nameof(escalation.Question));
        RequireDefined(escalation.Kind, nameof(escalation.Kind));
        RequireDefined(escalation.Status, nameof(escalation.Status));
        if (escalation.Status != EscalationStatus.Open)
        {
            throw new GovernanceException("A newly raised escalation must be open.");
        }

        if (escalation.Resolution is not null || escalation.ResolvedBy is not null)
        {
            throw new GovernanceException("A newly raised escalation cannot carry a resolution.");
        }

        if (escalation.WorkItemId is { } workItemId)
        {
            _ = Get(state.WorkItems, workItemId, "work item");
        }

        EnsureUnique(escalation.AttemptEvidenceIds, "Attempt evidence IDs");
        EnsureReferencesExist(state.Evidence, escalation.AttemptEvidenceIds, "evidence");
        if (escalation.Options.Any(string.IsNullOrWhiteSpace))
        {
            throw new GovernanceException("An escalation cannot carry an empty option.");
        }

        EnsureUnique(escalation.Options, "Options", StringComparer.Ordinal);

        // R1 (dual-kernel-rule-drift): these payload rules are the replay-side copy of
        // CommandHandler.RaiseEscalation. Change one and you must change the other.
        switch (escalation.Kind)
        {
            case EscalationKind.BusinessDecision:
                if (escalation.Options.Count < 2)
                {
                    throw new GovernanceException(
                        "A business decision requires at least two options for the operator to choose between.");
                }

                if (string.IsNullOrWhiteSpace(escalation.Recommendation))
                {
                    throw new GovernanceException("A business decision requires a recommendation.");
                }

                if (!escalation.Options.Contains(escalation.Recommendation, StringComparer.Ordinal))
                {
                    throw new GovernanceException("A business decision recommendation must name one of its options.");
                }

                break;
            case EscalationKind.TrueUnknown:
                if (escalation.AttemptEvidenceIds.Count == 0)
                {
                    throw new GovernanceException(
                        "A true unknown requires evidence of the attempt that failed to answer it.");
                }

                break;
            default:
                throw new GovernanceException($"Unsupported escalation kind '{escalation.Kind}'.");
        }

        ValidateProvenance(@event, escalation.Provenance, "escalation.raise");
    }

    private static void ValidateEscalationResolved(
        GovernedTaskState state,
        LedgerEvent @event,
        EscalationResolved resolved)
    {
        RequireAuthority(state, @event.ActorId, Capability.ResolveEscalation, operatorRequired: true);
        RequireDefined(resolved.Status, nameof(resolved.Status));
        var escalation = Get(state.Escalations, resolved.EscalationId, "escalation");
        if (escalation.Status != EscalationStatus.Open || resolved.Status == EscalationStatus.Open)
        {
            throw new GovernanceException("Only an open escalation can transition to a terminal disposition.");
        }

        if (resolved.Status == EscalationStatus.Resolved && string.IsNullOrWhiteSpace(resolved.Resolution))
        {
            throw new GovernanceException("Resolving an escalation requires the operator's answer.");
        }

        if (resolved.ResolvedBy != @event.ActorId)
        {
            throw new GovernanceException("An escalation must record the actor that resolved it.");
        }
    }

    private static void ValidateAlternativeRecorded(
        GovernedTaskState state,
        LedgerEvent @event,
        Alternative alternative)
    {
        RequireAuthority(state, @event.ActorId, Capability.RecordAlternative);
        EnsureNew(state.Alternatives, alternative.Id, "alternative");
        RequireId(alternative.Id.Value, nameof(alternative.Id));
        RequireText(alternative.Statement, nameof(alternative.Statement));
        RequireText(alternative.RejectionRationale, nameof(alternative.RejectionRationale));
        if (alternative.ReplacedByDecisionId is { } decisionId)
        {
            _ = Get(state.Decisions, decisionId, "decision");
        }

        ValidateProvenance(@event, alternative.Provenance, "alternative.record");
    }

    private static void ValidateConstraintAdded(GovernedTaskState state, LedgerEvent @event, Constraint constraint)
    {
        RequireAuthority(state, @event.ActorId, Capability.ManageConstraints, operatorRequired: true);
        EnsureNew(state.Constraints, constraint.Id, "constraint");
        RequireId(constraint.Id.Value, nameof(constraint.Id));
        RequireText(constraint.Statement, nameof(constraint.Statement));
        RequireText(constraint.Source, nameof(constraint.Source));
        RequireDefined(constraint.Status, nameof(constraint.Status));
        if (constraint.Status != ConstraintStatus.Active)
        {
            throw new GovernanceException("A newly added constraint must be active.");
        }

        EnsureUnique(constraint.Scope, "Constraint scope entries", StringComparer.Ordinal);
        if (constraint.Scope.Any(string.IsNullOrWhiteSpace))
        {
            throw new GovernanceException("Constraint scope entries cannot be empty.");
        }

        ValidateProvenance(@event, constraint.Provenance, "constraint.add");
    }

    private static void ValidateConstraintSuperseded(
        GovernedTaskState state,
        LedgerEvent @event,
        ConstraintSuperseded superseded)
    {
        RequireAuthority(state, @event.ActorId, Capability.ManageConstraints, operatorRequired: true);
        var constraint = Get(state.Constraints, superseded.ConstraintId, "constraint");
        if (constraint.Status != ConstraintStatus.Active)
        {
            throw new GovernanceException("Only an active constraint can be superseded.");
        }
    }

    private static void ValidateWorkItemCompleted(
        GovernedTaskState state,
        LedgerEvent @event,
        WorkItemCompleted completed)
    {
        RequireAuthority(state, @event.ActorId, Capability.ManageWork);
        var workItem = Get(state.WorkItems, completed.WorkItemId, "work item");
        if (workItem.Status is WorkItemStatus.Completed)
        {
            throw new GovernanceException("Work item is already completed.");
        }

        if (workItem.Status is WorkItemStatus.Blocked or WorkItemStatus.Stale)
        {
            throw new GovernanceException(
                $"Work item in status '{workItem.Status}' cannot be completed before it is repaired.");
        }

        if (state.Runs.Values.Any(run =>
                run.WorkItemId == completed.WorkItemId &&
                run.Status is AgentRunStatus.Pending or AgentRunStatus.Active))
        {
            throw new GovernanceException("Work item cannot be completed while a run is still active.");
        }

        if (state.Escalations.Values.Any(escalation =>
                escalation.WorkItemId == completed.WorkItemId && escalation.Status == EscalationStatus.Open))
        {
            throw new GovernanceException("Work item cannot be completed while an escalation on it is open.");
        }
    }

    private static void ValidateWorkItemBlocked(
        GovernedTaskState state,
        LedgerEvent @event,
        WorkItemBlocked blocked)
    {
        RequireAuthority(state, @event.ActorId, Capability.ManageWork);
        var workItem = Get(state.WorkItems, blocked.WorkItemId, "work item");
        RequireText(blocked.Reason, nameof(blocked.Reason));
        if (workItem.Status is WorkItemStatus.Completed)
        {
            throw new GovernanceException("A completed work item cannot be blocked.");
        }

        if (blocked.EscalationId is { } escalationId)
        {
            var escalation = Get(state.Escalations, escalationId, "escalation");
            if (escalation.Status != EscalationStatus.Open)
            {
                throw new GovernanceException("A work item can only be blocked on an open escalation.");
            }
        }
    }

    private static void ValidateWorkItemUnblocked(
        GovernedTaskState state,
        LedgerEvent @event,
        WorkItemUnblocked unblocked)
    {
        RequireAuthority(state, @event.ActorId, Capability.ManageWork);
        var workItem = Get(state.WorkItems, unblocked.WorkItemId, "work item");
        if (workItem.Status != WorkItemStatus.Blocked)
        {
            throw new GovernanceException($"Only a blocked work item can be unblocked; this one is '{workItem.Status}'.");
        }

        // Mirrors CommandHandler.UnblockWorkItem: unblocking never undoes causal invalidation.
        if (workItem.DependsOnClaims
            .Select(claimId => Get(state.Claims, claimId, "claim"))
            .Any(claim => claim.Status is ClaimStatus.Rejected or ClaimStatus.Superseded))
        {
            throw new GovernanceException(
                "A work item depending on a rejected or superseded claim cannot be unblocked.");
        }
    }

    private static void RequireAuthority(
        GovernedTaskState state,
        ActorId actorId,
        Capability capability,
        bool operatorRequired = false)
    {
        var assignment = Get(state.Roles, actorId, "actor role");
        if (operatorRequired && assignment.Role != RoleKind.Operator)
        {
            throw new GovernanceException("Only an operator can perform this transition.");
        }

        if (!assignment.Capabilities.Contains(capability))
        {
            throw new GovernanceException($"Actor '{actorId}' lacks capability '{capability}'.");
        }
    }

    private static bool IsOperator(GovernedTaskState state, ActorId actorId) =>
        state.Roles.TryGetValue(actorId, out var assignment) && assignment.Role == RoleKind.Operator;

    private static void ValidateProvenance(LedgerEvent @event, Provenance provenance, string expectedSource)
    {
        if (provenance.ActorId != @event.ActorId ||
            provenance.RecordedAt != @event.RecordedAt ||
            !string.Equals(provenance.Source, expectedSource, StringComparison.Ordinal))
        {
            throw new GovernanceException(
                "Payload provenance must match the event actor, timestamp, and transition source.");
        }
    }

    private static void EnsureDecisionIsCurrent(Decision decision)
    {
        if (decision.Status is DecisionStatus.Superseded or DecisionStatus.Invalidated)
        {
            throw new GovernanceException("Only a current decision can be superseded.");
        }
    }

    private static void EnsureClaimResolution(Claim claim, ClaimStatus target)
    {
        if (target == ClaimStatus.Open)
        {
            throw new GovernanceException("Claim resolution cannot set a claim to open.");
        }

        if (claim.Status is ClaimStatus.Rejected or ClaimStatus.Superseded || claim.Status == target)
        {
            throw new GovernanceException($"Claim in status '{claim.Status}' cannot transition to '{target}'.");
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

    private static void EnsureStagePrerequisites(GovernedTaskState state, TaskStage target)
    {
        if (target == TaskStage.Execution && state.WorkItems.Count == 0)
        {
            throw new GovernanceException("Execution requires at least one governed work item.");
        }

        if (target == TaskStage.Archive &&
            (state.Runs.Values.Any(run => run.Status is AgentRunStatus.Pending or AgentRunStatus.Active) ||
             state.Challenges.Values.Any(challenge => challenge.Status == ChallengeStatus.Open)))
        {
            throw new GovernanceException("A task with active runs or open challenges cannot be archived.");
        }
    }

    private static GovernedTaskState Require(GovernedTaskState? state) =>
        state ?? throw new GovernanceException("Task has not been opened.");

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

    private static void EnsureUnique<T>(
        IReadOnlyList<T> values,
        string label,
        IEqualityComparer<T>? comparer = null)
    {
        if (values.Count != values.Distinct(comparer).Count())
        {
            throw new GovernanceException($"{label} cannot contain duplicates.");
        }
    }

    private static void RequireDefined<TEnum>(TEnum value, string field) where TEnum : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new GovernanceException($"'{value}' is not a defined {field} value.");
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
}
