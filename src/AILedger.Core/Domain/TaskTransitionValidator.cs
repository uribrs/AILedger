using AILedger.Core.Alternatives;
using AILedger.Core.Artifacts;
using AILedger.Core.Claims;
using AILedger.Core.Challenges;
using AILedger.Core.Constraints;
using AILedger.Core.Contracts;
using AILedger.Core.CoordinatorSessions;
using AILedger.Core.ContextBriefing;
using AILedger.Core.Decisions;
using AILedger.Core.Evidences;
using AILedger.Core.Escalations;
using AILedger.Core.Lessons;
using AILedger.Core.Runs;
using AILedger.Core.Roles;
using AILedger.Core.Stages;
using AILedger.Core.TaskOpening;
using AILedger.Core.WorkItems;
using static AILedger.Core.Domain.ReplayValidationRules;

namespace AILedger.Core.Domain;

internal static class TaskTransitionValidator
{
    public static void Validate(GovernedTaskState? state, LedgerEvent @event)
    {
        ValidateEventMetadata(@event);

        switch (@event.Data)
        {
            case TaskOpened opened:
                TaskOpeningEventValidator.ValidateOpened(opened);
                break;
            case RoleAssigned assigned:
                RoleEventValidator.ValidateAssigned(Require(state), @event, assigned.Assignment);
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
                ContextEventValidator.ValidateBuilt(Require(state), @event, built);
                break;
            case ContextBriefWaived waivedBrief:
                ContextEventValidator.ValidateBriefWaived(Require(state), @event, waivedBrief);
                break;
            case SessionStarted started:
                CoordinatorSessionEventValidator.ValidateStarted(Require(state), @event, started.Session);
                break;
            case SessionCompleted completed:
                CoordinatorSessionEventValidator.ValidateCompleted(Require(state), @event, completed);
                break;
            case LessonsConsulted consulted:
                LessonEventValidator.ValidateConsulted(Require(state), @event, consulted);
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

    private static GovernedTaskState Require(GovernedTaskState? state) =>
        state ?? throw new GovernanceException("Task has not been opened.");
}
