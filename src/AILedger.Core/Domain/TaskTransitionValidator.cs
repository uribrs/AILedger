using AILedger.Core.Alternatives;
using AILedger.Core.Artifacts;
using AILedger.Core.Claims;
using AILedger.Core.Challenges;
using AILedger.Core.Constraints;
using AILedger.Core.Contracts;
using AILedger.Core.CoordinatorSessions;
using AILedger.Core.Decisions;
using AILedger.Core.Evidences;
using AILedger.Core.Escalations;
using AILedger.Core.Lessons;
using AILedger.Core.Runs;
using AILedger.Core.Stages;
using AILedger.Core.WorkItems;
using static AILedger.Core.Domain.ReplayValidationRules;

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
                ClaimEventValidator.ValidateAdded(Require(state), @event, added.Claim);
                break;
            case ClaimResolved resolved:
                ClaimEventValidator.ValidateResolved(Require(state), @event, resolved);
                break;
            case EvidenceAdded added:
                EvidenceEventValidator.ValidateAdded(Require(state), @event, added.Evidence);
                break;
            case DecisionProposed proposed:
                DecisionEventValidator.ValidateProposed(Require(state), @event, proposed.Decision);
                break;
            case DecisionResolved resolved:
                DecisionEventValidator.ValidateResolved(Require(state), @event, resolved);
                break;
            case DecisionInvalidated invalidated:
                DecisionEventValidator.ValidateInvalidated(Require(state), @event, invalidated);
                break;
            case ChallengeRaised raised:
                ChallengeEventValidator.ValidateRaised(Require(state), @event, raised.Challenge);
                break;
            case ChallengeDisposed disposed:
                ChallengeEventValidator.ValidateDisposed(Require(state), @event, disposed);
                break;
            case WorkItemAdded added:
                WorkItemEventValidator.ValidateAdded(Require(state), @event, added.WorkItem);
                break;
            case WorkItemInvalidated invalidated:
                WorkItemEventValidator.ValidateInvalidated(Require(state), @event, invalidated);
                break;
            case RunStarted started:
                RunEventValidator.ValidateStarted(Require(state), @event, started.Run);
                break;
            case RunCompleted completed:
                RunEventValidator.ValidateCompleted(Require(state), @event, completed);
                break;
            case StagePrerequisitesWaived waived:
                StageEventValidator.ValidatePrerequisitesWaived(Require(state), @event, waived);
                break;
            case StageTransitioned transitioned:
                StageEventValidator.ValidateTransitioned(Require(state), @event, transitioned);
                break;
            case EscalationRaised raised:
                EscalationEventValidator.ValidateRaised(Require(state), @event, raised.Escalation);
                break;
            case EscalationResolved resolved:
                EscalationEventValidator.ValidateResolved(Require(state), @event, resolved);
                break;
            case AlternativeRecorded recorded:
                AlternativeEventValidator.ValidateRecorded(Require(state), @event, recorded.Alternative);
                break;
            case ConstraintAdded added:
                ConstraintEventValidator.ValidateAdded(Require(state), @event, added.Constraint);
                break;
            case ConstraintSuperseded superseded:
                ConstraintEventValidator.ValidateSuperseded(Require(state), @event, superseded);
                break;
            case WorkItemCompleted completed:
                WorkItemEventValidator.ValidateCompleted(Require(state), @event, completed);
                break;
            case WorkItemBlocked blocked:
                WorkItemEventValidator.ValidateBlocked(Require(state), @event, blocked);
                break;
            case WorkItemUnblocked unblocked:
                WorkItemEventValidator.ValidateUnblocked(Require(state), @event, unblocked);
                break;
            case WorkItemAbandoned abandoned:
                WorkItemEventValidator.ValidateAbandoned(Require(state), @event, abandoned);
                break;
            case ClaimDependenciesRepointed repointed:
                ClaimEventValidator.ValidateDependenciesRepointed(Require(state), @event, repointed);
                break;
            case DecisionOverturned overturned:
                DecisionEventValidator.ValidateOverturned(Require(state), @event, overturned);
                break;
            case LessonMinted minted:
                LessonEventValidator.ValidateMinted(Require(state), @event, minted.Lesson);
                break;
            case LessonRecalled recalled:
                LessonEventValidator.ValidateRecalled(Require(state), @event, recalled.Lesson);
                break;
            case LessonMarked marked:
                LessonEventValidator.ValidateMarked(Require(state), @event, marked.Mark);
                break;
            case ArtifactRecorded recorded:
                ArtifactEventValidator.ValidateRecorded(Require(state), @event, recorded.Artifact);
                break;
            case ContextBuilt built:
                ValidateContextBuilt(Require(state), @event, built);
                break;
            case ContextBriefWaived waivedBrief:
                ValidateContextBriefWaived(Require(state), @event, waivedBrief);
                break;
            case SessionStarted started:
                CoordinatorSessionEventValidator.ValidateStarted(Require(state), @event, started.Session);
                break;
            case SessionCompleted completed:
                CoordinatorSessionEventValidator.ValidateCompleted(Require(state), @event, completed);
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
        // Validate the shape of the tags that are present and never require presence: every task
        // in an existing root was opened before tags existed, and requiring them would reject a
        // history that was legal when it was written.
        ValidateOptionalTags(opened.Tags, "task");
    }

    private static void ValidateOptionalTags(IReadOnlyList<string>? tags, string subject)
    {
        if (tags is null)
        {
            return;
        }

        EnsureUnique(tags, $"{subject} tags", StringComparer.Ordinal);
        if (tags.Any(string.IsNullOrWhiteSpace))
        {
            throw new GovernanceException($"A {subject} cannot carry an empty tag.");
        }
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

        // Opening events written before RecordArtifact existed cannot carry that capability.
        // Requiring the stable baseline preserves those histories while command time continues
        // to grant every capability currently defined.
        Capability[] legacyBaseline =
        [
            Capability.ManageRoles, Capability.ManageScope, Capability.AddClaim,
            Capability.ResolveClaim, Capability.AddEvidence, Capability.ProposeDecision,
            Capability.ResolveDecision, Capability.RaiseChallenge, Capability.DisposeChallenge,
            Capability.ManageWork, Capability.ManageRuns, Capability.RequestTransition,
            Capability.BuildContext, Capability.RaiseEscalation, Capability.ResolveEscalation,
            Capability.RecordAlternative, Capability.ManageConstraints
        ];
        if (legacyBaseline.Except(assignment.Capabilities).Any())
        {
            throw new GovernanceException("The opening operator role must contain the legacy capability baseline.");
        }

        ValidateProvenance(@event, assignment.AssignedBy, TaskOpenSource);
    }

    // Reads nothing but the event in front of it and the role that event's actor held at that
    // point. Every rule here keys on a field only a context.built event carries, so it is safe by
    // construction: no history written before this event type existed can reach it.
    //
    // What must never appear in this file is the other half — an arm requiring a context.built
    // before work.added or run.started. Command time may demand it; replay may not, because all 36
    // tasks in this ledger were written without one.
    private static void ValidateContextBuilt(
        GovernedTaskState state,
        LedgerEvent @event,
        ContextBuilt built)
    {
        RequireDefined(built.Role, nameof(built.Role));
        var assignment = Get(state.Roles, @event.ActorId, "actor role");
        if (built.Role != assignment.Role)
        {
            throw new GovernanceException(
                $"Context brief for '{@event.ActorId}' records role '{built.Role}' but the actor held " +
                $"'{assignment.Role}'.");
        }

        if (built.WorkItemId is { } workItemId)
        {
            _ = Get(state.WorkItems, workItemId, "work item");
        }

        foreach (var skill in built.Skills)
        {
            RequireId(skill.SkillId, "Skill ID");
            RequireText(skill.ContentHash, "Skill content hash");
        }

        if (built.Skills.Select(skill => skill.SkillId).Distinct(StringComparer.Ordinal).Count() !=
            built.Skills.Count)
        {
            throw new GovernanceException("A context brief cannot serve one skill twice.");
        }
    }

    // Safe by construction, on the same grounds as ValidateContextBuilt above and the waiver arm in
    // WorkItemEventValidator.ValidateCompleted: every rule here keys on a field only a context.brief-waived event
    // carries, and no history written before this event type existed can reach it. What must never
    // appear is the other direction — an arm requiring a brief, or a waiver, before work.added or
    // run.started. Every task in this ledger carries neither.
    private static void ValidateContextBriefWaived(
        GovernedTaskState state,
        LedgerEvent @event,
        ContextBriefWaived waived)
    {
        RequireText(waived.Action, nameof(waived.Action));
        if ((waived.OperatorReason is not null) == (waived.StaleBriefEvidenceId is not null))
        {
            throw new GovernanceException(
                "A context brief waiver carries exactly one justification: an operator's reason for an " +
                "absent brief, or an evidence record for a stale one.");
        }

        if (waived.OperatorReason is { } reason)
        {
            if (string.IsNullOrWhiteSpace(reason))
            {
                throw new GovernanceException(
                    "Proceeding without a brief needs a reason; a blank waiver records nothing.");
            }

            if (!IsOperator(state, @event.ActorId))
            {
                throw new GovernanceException(
                    $"Only an operator can {waived.Action} without a brief.");
            }
        }

        if (waived.StaleBriefEvidenceId is { } evidenceId)
        {
            _ = Get(state.Evidence, evidenceId, "evidence");
            if (!state.ContextBuilds.ContainsKey(@event.ActorId))
            {
                throw new GovernanceException(
                    $"Actor '{@event.ActorId}' has no context brief at all, so there is no stale brief to " +
                    "proceed on.");
            }
        }
    }
    private static GovernedTaskState Require(GovernedTaskState? state) =>
        state ?? throw new GovernanceException("Task has not been opened.");

}
