using AILedger.Core.Application;
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
                ValidateRunStarted(Require(state), @event, started.Run);
                break;
            case RunCompleted completed:
                ValidateRunCompleted(Require(state), @event, completed);
                break;
            case StagePrerequisitesWaived waived:
                ValidateStagePrerequisitesWaived(Require(state), @event, waived);
                break;
            case StageTransitioned transitioned:
                ValidateStageTransitioned(Require(state), @event, transitioned);
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
                ValidateLessonMinted(Require(state), @event, minted.Lesson);
                break;
            case LessonRecalled recalled:
                ValidateLessonRecalled(Require(state), @event, recalled.Lesson);
                break;
            case LessonMarked marked:
                ValidateLessonMarked(Require(state), @event, marked.Mark);
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

    private static void ValidateLessonMinted(
        GovernedTaskState state,
        LedgerEvent @event,
        Lesson lesson)
    {
        RequireAuthority(state, @event.ActorId, Capability.RequestTransition);
        if (state.Stage != TaskStage.Learn)
        {
            throw new GovernanceException("Lessons can only be minted while closing a task from the Learn stage.");
        }

        ValidateLessonShape(lesson);
        if (lesson.SourceTaskId != state.TaskId)
        {
            throw new GovernanceException("A minted lesson must name the task that is minting it.");
        }

        if (state.Lessons.ContainsKey(lesson.Id))
        {
            throw new GovernanceException($"Lesson '{lesson.Id}' is already present in the task.");
        }

        var sourceMatches = lesson.SourceKind switch
        {
            LessonSourceKind.ValidatedClaim =>
                state.Claims.TryGetValue(new ClaimId(lesson.SourceRecordId), out var claim) &&
                claim.Status == ClaimStatus.Validated && claim.Statement == lesson.Statement,
            LessonSourceKind.RejectedClaim =>
                state.Claims.TryGetValue(new ClaimId(lesson.SourceRecordId), out var rejected) &&
                rejected.Status == ClaimStatus.Rejected && rejected.Statement == lesson.Statement,
            LessonSourceKind.RejectedAlternative =>
                state.Alternatives.TryGetValue(new AlternativeId(lesson.SourceRecordId), out var alternative) &&
                alternative.Statement == lesson.Statement && alternative.RejectionRationale == lesson.Outcome,
            LessonSourceKind.ResolvedEscalation =>
                state.Escalations.TryGetValue(new EscalationId(lesson.SourceRecordId), out var escalation) &&
                escalation.Status == EscalationStatus.Resolved && escalation.Question == lesson.Statement &&
                escalation.Resolution == lesson.Outcome,
            _ => false
        };
        var expectedId = $"{state.TaskId.Value}:{lesson.SourceKind.ToString().ToLowerInvariant()}:{lesson.SourceRecordId}";
        if (!sourceMatches || lesson.Id.Value != expectedId)
        {
            throw new GovernanceException("A minted lesson must faithfully represent a lesson-bearing source record.");
        }

        // Histories written before selective minting contain LessonMinted with no preceding mark.
        // Once a history contains marks, every minted lesson must faithfully carry its mark.
        if (state.LessonMarks.Count != 0)
        {
            var markId = LessonMarkIdFor(lesson.SourceKind, lesson.SourceRecordId);
            if (!state.LessonMarks.TryGetValue(markId, out var mark) ||
                mark.SupersedesLessonId != lesson.SupersedesLessonId ||
                mark.Class != lesson.Class ||
                !string.Equals(mark.Repo, lesson.Repo, StringComparison.Ordinal) ||
                !OptionalSequenceEqual(mark.Tags, lesson.Tags) ||
                // Both sides are absent in every history written before these fields existed, so
                // comparing them cannot reject a history that was legal when it was written.
                !string.Equals(mark.Verify, lesson.Verify, StringComparison.Ordinal) ||
                !string.Equals(mark.DoNot, lesson.DoNot, StringComparison.Ordinal) ||
                mark.Actor != lesson.Actor ||
                mark.Kind != lesson.Kind ||
                !OptionalSequenceEqual(mark.Audience, lesson.Audience) ||
                mark.VerifyExpects != lesson.VerifyExpects)
            {
                throw new GovernanceException("A minted lesson must match its lesson-bearing mark.");
            }
        }

        var expectedCitations = lesson.SourceKind switch
        {
            LessonSourceKind.ValidatedClaim or LessonSourceKind.RejectedClaim =>
                state.Claims[new ClaimId(lesson.SourceRecordId)].EvidenceIds
                    .Select(id => state.Evidence[id].Citation),
            LessonSourceKind.ResolvedEscalation => state.Escalations[new EscalationId(lesson.SourceRecordId)]
                .AttemptEvidenceIds.Select(id => state.Evidence[id].Citation),
            _ => []
        };
        if (!lesson.Citations.SequenceEqual(
                expectedCitations.Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal),
                StringComparer.Ordinal))
        {
            throw new GovernanceException("A minted lesson must retain the citations of its source record.");
        }

        ValidateProvenance(@event, lesson.Provenance, "stage.archive");
    }

    private static void ValidateLessonMarked(
        GovernedTaskState state,
        LedgerEvent @event,
        LessonMark mark)
    {
        var assignment = Get(state.Roles, @event.ActorId, "actor role");
        if (assignment.Role is not (RoleKind.Operator or RoleKind.PlanningLead or RoleKind.ImplementationLead))
        {
            throw new GovernanceException("Only an operator or lead can mark a source lesson-bearing.");
        }

        RequireDefined(mark.SourceKind, nameof(mark.SourceKind));
        ValidateLessonMetadata(
            mark.Class, mark.Repo, mark.Tags, mark.Verify, mark.DoNot, mark.Actor,
            mark.Kind, mark.Audience, mark.VerifyExpects, "lesson mark");
        RequireId(mark.SourceRecordId, nameof(mark.SourceRecordId));
        var expectedId = LessonMarkIdFor(mark.SourceKind, mark.SourceRecordId);
        if (mark.Id != expectedId)
        {
            throw new GovernanceException("A lesson mark identifier must match its source record.");
        }
        EnsureNew(state.LessonMarks, mark.Id, "lesson mark");

        var eligible = mark.SourceKind switch
        {
            LessonSourceKind.ValidatedClaim =>
                state.Claims.TryGetValue(new ClaimId(mark.SourceRecordId), out var claim) &&
                claim.Status == ClaimStatus.Validated,
            // Widening only: no history written before this kind existed can carry it, so replay
            // accepts strictly more than it did and no archived task becomes unreadable.
            LessonSourceKind.RejectedClaim =>
                state.Claims.TryGetValue(new ClaimId(mark.SourceRecordId), out var rejected) &&
                rejected.Status == ClaimStatus.Rejected,
            LessonSourceKind.RejectedAlternative =>
                state.Alternatives.ContainsKey(new AlternativeId(mark.SourceRecordId)),
            LessonSourceKind.ResolvedEscalation =>
                state.Escalations.TryGetValue(new EscalationId(mark.SourceRecordId), out var escalation) &&
                escalation.Status == EscalationStatus.Resolved,
            _ => false
        };
        if (!eligible)
        {
            throw new GovernanceException("A lesson mark must name an eligible source record.");
        }

        if (mark.SupersedesLessonId is { } superseded)
        {
            _ = Get(state.Lessons, superseded, "superseded lesson");
            if (state.LessonMarks.Values.Any(existing => existing.SupersedesLessonId == superseded))
            {
                throw new GovernanceException($"Lesson '{superseded}' is already superseded by another mark.");
            }
        }

        ValidateProvenance(@event, mark.Provenance, "lesson.mark");
    }

    private static void ValidateLessonRecalled(
        GovernedTaskState state,
        LedgerEvent @event,
        Lesson lesson)
    {
        // Recall events are part of task opening. The preceding opening role is the authority;
        // the lesson's provenance intentionally remains that of the source task.
        if (state.Version < 2 || state.Stage != TaskStage.Discovery || state.Roles.Count != 1 ||
            state.Claims.Count != 0 || state.Evidence.Count != 0 || state.Decisions.Count != 0 ||
            state.Challenges.Count != 0 || state.WorkItems.Count != 0 || state.Runs.Count != 0 ||
            state.Escalations.Count != 0 || state.Alternatives.Count != 0 || state.Constraints.Count != 0 ||
            !state.Roles.TryGetValue(@event.ActorId, out var assignment) || assignment.Role != RoleKind.Operator)
        {
            throw new GovernanceException("Lessons can only be recalled into a newly opened task.");
        }

        ValidateLessonShape(lesson);
        var expectedId = $"{lesson.SourceTaskId.Value}:{lesson.SourceKind.ToString().ToLowerInvariant()}:{lesson.SourceRecordId}";
        if (lesson.Id.Value != expectedId)
        {
            throw new GovernanceException("A recalled lesson identifier must match its source record.");
        }
        if (lesson.SourceTaskId == state.TaskId)
        {
            throw new GovernanceException("A task cannot recall its own lesson.");
        }

        if (state.Lessons.ContainsKey(lesson.Id))
        {
            throw new GovernanceException($"Lesson '{lesson.Id}' is already present in the task.");
        }


        // A recalled lesson was minted by an archive, or carried in from the pre-kernel ledger where
        // the close-out was a verifier's report rather than a stage transition. Both are real
        // provenance and neither can be forged into the other without lying about where the lesson
        // came from. This is a loosening, which is the safe direction at replay: it accepts a source
        // no earlier history could contain and rejects nothing that was ever legal.
        if (lesson.Provenance.RecordedAt == default || string.IsNullOrWhiteSpace(lesson.Provenance.ActorId.Value) ||
            !(string.Equals(lesson.Provenance.Source, "stage.archive", StringComparison.Ordinal) ||
              (string.Equals(lesson.Provenance.Source, "lesson.import", StringComparison.Ordinal) &&
               lesson.SourceKind == LessonSourceKind.Imported)))
        {
            throw new GovernanceException("A recalled lesson must retain valid close-out provenance.");
        }
    }

    private static void ValidateLessonShape(Lesson lesson)
    {
        RequireId(lesson.Id.Value, nameof(lesson.Id));
        RequireId(lesson.SourceTaskId.Value, nameof(lesson.SourceTaskId));
        RequireDefined(lesson.SourceKind, nameof(lesson.SourceKind));
        RequireId(lesson.SourceRecordId, nameof(lesson.SourceRecordId));
        RequireText(lesson.Statement, nameof(lesson.Statement));
        RequireText(lesson.Outcome, nameof(lesson.Outcome));
        EnsureUnique(lesson.Citations, "Lesson citations", StringComparer.Ordinal);
        if (lesson.Citations.Any(string.IsNullOrWhiteSpace))
        {
            throw new GovernanceException("A lesson cannot carry an empty citation.");
        }
        if (lesson.SupersedesLessonId is { } supersedes)
        {
            RequireId(supersedes.Value, nameof(lesson.SupersedesLessonId));
            if (supersedes == lesson.Id)
            {
                throw new GovernanceException("A lesson cannot supersede itself.");
            }
        }
        ValidateLessonMetadata(
            lesson.Class, lesson.Repo, lesson.Tags, lesson.Verify, lesson.DoNot, lesson.Actor,
            lesson.Kind, lesson.Audience, lesson.VerifyExpects, "lesson");
    }

    // Metadata is optional only for replay compatibility. Validate every field an event actually
    // carries, but do not require fields absent from histories written before lesson classification
    // or before verify, do-not and actor were added. What the verify says is a command-time rule
    // only: CommandHandler refuses a bare "none", and this copy checks the field is well formed and
    // never what it claims, because the admission's wording is methodology the operator may change.
    private static void ValidateLessonMetadata(
        LessonClass? lessonClass,
        string? repo,
        IReadOnlyList<string>? tags,
        string? verify,
        string? doNot,
        LessonActor? actor,
        LessonKind? lessonKind,
        IReadOnlyList<RoleKind>? audience,
        VerifyExpectation? verifyExpects,
        string subject)
    {
        if (lessonClass is { } presentClass)
        {
            RequireDefined(presentClass, nameof(Lesson.Class));
        }
        if (repo is not null)
        {
            RequireText(repo, nameof(Lesson.Repo));
        }
        if (verify is not null)
        {
            RequireText(verify, nameof(Lesson.Verify));
        }
        if (doNot is not null)
        {
            RequireText(doNot, nameof(Lesson.DoNot));
        }
        if (actor is { } presentActor)
        {
            RequireDefined(presentActor, nameof(Lesson.Actor));
        }
        // Each of these three is absent in every history written before it existed, so validating
        // only what an event actually carries keeps replay accepting every history that was ever
        // legal. Which way the verify has to come out is required at command time and never here,
        // for the same reason the wording of the verify itself is: CommandHandler may tighten and
        // this copy may not.
        if (lessonKind is { } presentKind)
        {
            RequireDefined(presentKind, nameof(Lesson.Kind));
        }
        if (verifyExpects is { } presentExpectation)
        {
            RequireDefined(presentExpectation, nameof(Lesson.VerifyExpects));
        }
        if (audience is not null)
        {
            EnsureUnique(audience, $"{subject} audience");
            foreach (var role in audience)
            {
                RequireDefined(role, nameof(Lesson.Audience));
            }
        }
        ValidateOptionalTags(tags, subject);
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

    private static LessonMarkId LessonMarkIdFor(LessonSourceKind kind, string sourceRecordId) =>
        new($"{kind.ToString().ToLowerInvariant()}:{sourceRecordId}");

    private static bool OptionalSequenceEqual(
        IReadOnlyList<string>? left,
        IReadOnlyList<string>? right) =>
        left is null ? right is null : right is not null && left.SequenceEqual(right, StringComparer.Ordinal);

    private static bool OptionalSequenceEqual(
        IReadOnlyList<RoleKind>? left,
        IReadOnlyList<RoleKind>? right) =>
        left is null ? right is null : right is not null && left.SequenceEqual(right);

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

    private static void ValidateRunStarted(GovernedTaskState state, LedgerEvent @event, AgentRun run)
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

        // Mirrors RunRules.StartRun. LaunchedBy is set only when an operator dispatched the
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

        // Mirrors RunRules.StartRun. Adding Abandoned tightens replay, which is normally the
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

        if (run.Status is not AgentRunStatus.Active ||
            completed.Status is AgentRunStatus.Active)
        {
            throw new GovernanceException("Only an active run can transition to a terminal status.");
        }

        // Mirrors RunRules.EnsureLauncherAuthorizedCompletion. The launcher's secret is
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
        // deliberately unchecked here, and RunRules does not check them either. The asymmetry this
        // kernel has to respect
        // runs one way — command time may tighten and replay may not — so the safe shape for a
        // trailing nullable field is no rule on either side rather than a rule here that the
        // command-time half cannot later relax (MC4, R4).
    }

    // Mirrors RunRules.EnsureCostRecordIsWellFormed. Safe to write here by construction rather than
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

    // Mirrors RunRules.EnsureManifestRecordIsWellFormed. Safe to write here by construction rather
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
        if (state.PendingStagePrerequisiteWaiver is { } waiver)
        {
            if (waiver.ActorId != @event.ActorId ||
                waiver.TargetStage != transitioned.Current ||
                @event.CausationId != waiver.EventId)
            {
                throw new GovernanceException(
                    "A stage prerequisite waiver must be consumed by its immediately caused transition.");
            }
        }
        else
        {
            EnsureStagePrerequisites(state, transitioned.Current);
        }
        // C3 (lesson-closeout-replay-compatibility): CommandHandler requires and emits lessons for
        // a new Learn -> Archive command. Replay deliberately does not require a preceding lesson:
        // every archive history written before lesson events existed has none.

        // The transition-reason rule is command-time only, in all three of its halves, and only the
        // first half has to be. That a backward transition carries a reason keys on Reason being
        // absent, and almost every stage.transitioned in this ledger was written before the field
        // existed and carries none — a replay copy would refuse histories that were legal when
        // they were written, which is the direction this file may never move in. The other two
        // halves — that a forward transition carries no reason, and that a present reason is not
        // blank — key on Reason being present, which no event written before the field can be, so
        // they are safe by construction and could have been written here. They were left out
        // deliberately rather than forgotten. What their absence costs is small and worth stating
        // plainly: a hand-edited events.jsonl line could replay a stage state the command path
        // would have refused to produce. Replay's protection against a forged line is provenance
        // and sequence, not a second copy of every rule.
    }

    private static void ValidateStagePrerequisitesWaived(
        GovernedTaskState state,
        LedgerEvent @event,
        StagePrerequisitesWaived waived)
    {
        RequireAuthority(state, @event.ActorId, Capability.RequestTransition);
        RequireDefined(waived.TargetStage, nameof(waived.TargetStage));
        if (string.IsNullOrWhiteSpace(waived.Reason))
        {
            throw new GovernanceException(
                "Waiving stage prerequisites needs a reason; a blank waiver records nothing.");
        }

        if (!IsOperator(state, @event.ActorId))
        {
            throw new GovernanceException("Only an operator can transition stages without prerequisites.");
        }

        if (state.PendingStagePrerequisiteWaiver is not null)
        {
            throw new GovernanceException("A stage prerequisite waiver is already pending.");
        }

        StageTransitionPolicy.EnsureAllowed(state.Stage, waived.TargetStage);
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

    private static void EnsureStagePrerequisites(GovernedTaskState state, TaskStage target)
    {
        // The coordinator's stage-engagement arms are command-time methodology rules and are
        // deliberately absent here. Adding them during replay would reject histories that were
        // legal before those prerequisites existed. Keep only the legacy structural gates below.
        if (target == TaskStage.Execution && state.WorkItems.Count == 0)
        {
            throw new GovernanceException("Execution requires at least one governed work item.");
        }

        if (target == TaskStage.Archive &&
            (state.Runs.Values.Any(run => run.Status is AgentRunStatus.Active) ||
             state.Challenges.Values.Any(challenge => challenge.Status == ChallengeStatus.Open)))
        {
            throw new GovernanceException("A task with active runs or open challenges cannot be archived.");
        }
    }

    private static GovernedTaskState Require(GovernedTaskState? state) =>
        state ?? throw new GovernanceException("Task has not been opened.");

}
