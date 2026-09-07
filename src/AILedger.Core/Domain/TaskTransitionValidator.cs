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
            case StagePrerequisitesWaived waived:
                ValidateStagePrerequisitesWaived(Require(state), @event, waived);
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
            case WorkItemAbandoned abandoned:
                ValidateWorkItemAbandoned(Require(state), @event, abandoned);
                break;
            case ClaimDependenciesRepointed repointed:
                ValidateClaimDependenciesRepointed(Require(state), @event, repointed);
                break;
            case DecisionOverturned overturned:
                ValidateDecisionOverturned(Require(state), @event, overturned);
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
                ValidateArtifactRecorded(Require(state), @event, recorded.Artifact);
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
                mark.Actor != lesson.Actor)
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
            mark.Class, mark.Repo, mark.Tags, mark.Verify, mark.DoNot, mark.Actor, "lesson mark");
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
            lesson.Class, lesson.Repo, lesson.Tags, lesson.Verify, lesson.DoNot, lesson.Actor, "lesson");
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

    private static void ValidateArtifactRecorded(
        GovernedTaskState state,
        LedgerEvent @event,
        GovernedArtifact artifact)
    {
        RequireAuthority(state, @event.ActorId, Capability.RecordArtifact);
        EnsureNew(state.Artifacts, artifact.ArtifactId, "artifact");
        RequireId(artifact.ArtifactId.Value, nameof(artifact.ArtifactId));
        RequireDefined(artifact.Kind, nameof(artifact.Kind));
        RequireText(artifact.Title, nameof(artifact.Title));
        RequireText(artifact.Content, nameof(artifact.Content));

        var workScoped = artifact.Kind is
            GovernedArtifactKind.VerifierOutput or GovernedArtifactKind.CodeReviewOutput;
        if (workScoped != artifact.WorkItemId.HasValue)
        {
            throw new GovernanceException(workScoped
                ? $"A '{artifact.Kind}' artifact must name its work item."
                : $"A '{artifact.Kind}' artifact is task-wide and cannot name a work item.");
        }

        var assignment = Get(state.Roles, @event.ActorId, "actor role");
        ValidateArtifactProducer(state, @event, artifact, assignment.Role);
        ValidateArtifactRevision(state, artifact);
        if (artifact.Kind == GovernedArtifactKind.VerifierOutput)
        {
            ValidateVerifierOutput(state, artifact.WorkItemId!.Value, artifact.Content);
        }
        ValidateProvenance(@event, artifact.Provenance, "artifact.record");
    }

    private static void ValidateArtifactProducer(
        GovernedTaskState state,
        LedgerEvent @event,
        GovernedArtifact artifact,
        RoleKind actorRole)
    {
        if (artifact.Kind == GovernedArtifactKind.UserRequest)
        {
            if (actorRole != RoleKind.Operator || artifact.ProducerRunId is not null)
            {
                throw new GovernanceException(
                    "A user-request artifact must be operator-authored and cannot name a producer run.");
            }
            return;
        }

        if (artifact.ProducerRunId is not { } runId)
        {
            throw new GovernanceException($"A '{artifact.Kind}' artifact must name its active producer run.");
        }
        var run = Get(state.Runs, runId, "producer run");
        if (run.Status != AgentRunStatus.Active || run.ActorId != @event.ActorId)
        {
            throw new GovernanceException(
                $"Artifact producer run '{runId}' must be active and belong to actor '{@event.ActorId}'.");
        }
        if (artifact.WorkItemId is not null && run.WorkItemId != artifact.WorkItemId)
        {
            throw new GovernanceException($"Artifact work item must match producer run '{runId}'.");
        }

        var requiredRole = artifact.Kind switch
        {
            GovernedArtifactKind.VerifierOutput => RoleKind.Verifier,
            GovernedArtifactKind.CodeReviewOutput => RoleKind.CodeReviewer,
            _ => (RoleKind?)null
        };
        if (requiredRole is { } role && run.SubjectRole != role)
        {
            throw new GovernanceException($"A '{artifact.Kind}' artifact requires a matching active {role} run.");
        }
        if (requiredRole is null && run.SubjectRole is not
            (RoleKind.Operator or RoleKind.PlanningLead or RoleKind.ImplementationLead))
        {
            throw new GovernanceException(
                "Only an operator, planning lead, or implementation lead can record a governing workflow artifact.");
        }
    }

    private static void ValidateArtifactRevision(GovernedTaskState state, GovernedArtifact artifact)
    {
        var current = CurrentArtifacts(state)
            .Where(item => item.Kind == artifact.Kind && item.WorkItemId == artifact.WorkItemId)
            .ToArray();
        if (artifact.SupersedesArtifactId is not { } predecessorId)
        {
            if (current.Length != 0)
            {
                throw new GovernanceException(
                    $"A current '{artifact.Kind}' artifact already exists for this scope; a revision must supersede it.");
            }
            return;
        }
        if (predecessorId == artifact.ArtifactId)
        {
            throw new GovernanceException("An artifact cannot supersede itself.");
        }
        var predecessor = Get(state.Artifacts, predecessorId, "superseded artifact");
        if (predecessor.Kind != artifact.Kind || predecessor.WorkItemId != artifact.WorkItemId)
        {
            throw new GovernanceException(
                "An artifact can only supersede the current artifact of the same kind and work scope.");
        }
        if (!current.Any(item => item.ArtifactId == predecessorId))
        {
            throw new GovernanceException($"Artifact '{predecessorId}' is not current and cannot be superseded.");
        }
    }

    private static IReadOnlyList<GovernedArtifact> CurrentArtifacts(GovernedTaskState state)
    {
        var superseded = state.Artifacts.Values
            .Where(item => item.SupersedesArtifactId is not null)
            .Select(item => item.SupersedesArtifactId!.Value)
            .ToHashSet();
        return state.Artifacts.Values.Where(item => !superseded.Contains(item.ArtifactId)).ToArray();
    }

    private static void ValidateVerifierOutput(
        GovernedTaskState state,
        WorkItemId workItemId,
        string content)
    {
        var workItem = Get(state.WorkItems, workItemId, "work item");
        var assumptions = ReadMarkdownTable(content, ["id", "status", "name", "citation", "actor"]);
        EnsureUnique(assumptions.Select(row => row[0]).ToArray(),
            "Verifier assumption disposition IDs", StringComparer.Ordinal);
        foreach (var claimId in workItem.DependsOnClaims.Where(id =>
                     state.Claims[id].Status is ClaimStatus.Open or ClaimStatus.Validated))
        {
            var row = assumptions.SingleOrDefault(candidate => candidate[0] == claimId.Value)
                ?? throw new GovernanceException($"Verifier output must dispose dependent claim '{claimId}'.");
            if (row[1] is not ("VALIDATED" or "REJECTED" or "NEVER-TESTED") ||
                string.IsNullOrWhiteSpace(row[2]) || string.IsNullOrWhiteSpace(row[3]) ||
                string.IsNullOrWhiteSpace(row[4]))
            {
                throw new GovernanceException(
                    $"Verifier disposition for claim '{claimId}' must be terminal and carry a name, citation, and actor.");
            }
        }

        var plan = CurrentArtifacts(state).SingleOrDefault(item =>
            item.Kind == GovernedArtifactKind.OrchestrationPlan);
        if (plan is null)
        {
            throw new GovernanceException("A verifier output requires a current orchestration plan.");
        }
        var attentionIds = plan.Content.Contains("No material attention items", StringComparison.OrdinalIgnoreCase)
            ? Array.Empty<string>()
            : ReadMarkdownTable(plan.Content,
                    ["id", "name", "failure mode", "causal path and impact", "planned handling", "source"])
                .Select(row => row[0]).Where(IsAttentionId).ToArray();
        EnsureUnique(attentionIds, "Orchestration-plan attention item IDs", StringComparer.Ordinal);

        var dispositions = ReadMarkdownTable(content, ["id", "final disposition", "name", "evidence"]);
        EnsureUnique(dispositions.Select(row => row[0]).ToArray(),
            "Verifier attention disposition IDs", StringComparer.Ordinal);
        foreach (var attentionId in attentionIds)
        {
            var row = dispositions.SingleOrDefault(candidate => candidate[0] == attentionId)
                ?? throw new GovernanceException($"Verifier output must dispose attention item '{attentionId}'.");
            if (row[1] is not ("handled" or "accepted-risk" or "not-applicable" or "unresolved") ||
                string.IsNullOrWhiteSpace(row[2]) || string.IsNullOrWhiteSpace(row[3]))
            {
                throw new GovernanceException(
                    $"Verifier attention disposition '{attentionId}' must use an allowed value and carry a name and evidence.");
            }
        }
    }

    private static IReadOnlyList<string[]> ReadMarkdownTable(string content, IReadOnlyList<string> header)
    {
        var lines = content.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            if (!SplitMarkdownRow(lines[index]).SequenceEqual(header, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            var rows = new List<string[]>();
            for (var rowIndex = index + 2; rowIndex < lines.Length; rowIndex++)
            {
                var row = SplitMarkdownRow(lines[rowIndex]);
                if (row.Length != header.Count)
                {
                    break;
                }
                rows.Add(row);
            }
            return rows;
        }
        throw new GovernanceException($"Artifact body is missing the required '{string.Join(" | ", header)}' table.");
    }

    private static string[] SplitMarkdownRow(string line)
    {
        var trimmed = line.Trim();
        return trimmed.StartsWith('|') && trimmed.EndsWith('|')
            ? trimmed[1..^1].Split('|').Select(cell => cell.Trim()).ToArray()
            : [];
    }

    private static bool IsAttentionId(string value)
    {
        if (value.Length < 2 || value[0] != 'R')
        {
            return false;
        }
        var digits = value.Skip(1).TakeWhile(char.IsDigit).Count();
        return digits > 0 && (digits == value.Length - 1 ||
            digits == value.Length - 2 && char.IsLetter(value[^1]));
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

        // Mirrors CommandHandler.EnsureSupersessionReplacement and DeriveSupersession. The outcome
        // is carried on the event, and re-derived here so a forged one is refused.
        if (resolved.Status != ClaimStatus.Superseded)
        {
            if (resolved.SupersededByClaimId is not null || resolved.Outcome is not null)
            {
                throw new GovernanceException(
                    $"A '{resolved.Status}' resolution cannot name a superseding claim or outcome.");
            }

            return;
        }

        if (resolved.SupersededByClaimId is not { } replacementId)
        {
            throw new GovernanceException("Superseding a claim requires naming the claim that replaces it.");
        }

        if (replacementId == resolved.ClaimId)
        {
            throw new GovernanceException("A claim cannot supersede itself.");
        }

        var replacement = Get(state.Claims, replacementId, "claim");
        if (replacement.Status is ClaimStatus.Rejected or ClaimStatus.Superseded)
        {
            throw new GovernanceException(
                $"Replacement claim '{replacementId}' is '{replacement.Status}' and cannot replace another claim.");
        }

        if (resolved.Outcome is not { } outcome)
        {
            throw new GovernanceException("A supersession must record whether it was a refinement or a correction.");
        }

        RequireDefined(outcome, nameof(resolved.Outcome));
        var expected = replacement.Status == ClaimStatus.Validated &&
                       !state.Evidence.Values.Any(item => item.Refutes.Contains(resolved.ClaimId))
            ? SupersessionOutcome.Refinement
            : SupersessionOutcome.Correction;
        if (outcome != expected)
        {
            throw new GovernanceException(
                $"Supersession outcome '{outcome}' does not match the state-derived outcome '{expected}'.");
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

        // Scope occupancy is deliberately NOT checked here. It is a coordination rule about who may
        // claim an area next, not a structural invariant of the event, and the replay validator has
        // to accept every history that was ever legal. Adding it here made an existing log
        // unreadable: work items recorded before the rule existed held overlapping areas legally,
        // and re-validating their creation events failed. Command-time rules may tighten over time;
        // replay-time rules may not.
        //
        // For the same reason, replay does not require a multi-area item to carry a
        // NotSplitJustification. That rule keys on the ABSENCE of one, and W10 in the live ledger
        // holds two areas with none — it was recorded before the rule existed. Requiring it here
        // would reject that event and destroy the log. Only the shape below is checked, because it
        // keys on the field being PRESENT, which no older event can be.
        if (workItem.NotSplitJustification is { } justificationId)
        {
            _ = Get(state.Alternatives, justificationId, "alternative");
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
        // Checked only when present, because null is the legitimate shape of every run recorded
        // before SubjectRole existed. An undefined member is a different matter: it reaches state
        // and renders as its raw number, so a forged role has to fail here.
        if (run.SubjectRole is { } subjectRole)
        {
            RequireDefined(subjectRole, nameof(run.SubjectRole));
        }

        // Mirrors CommandHandler.StartRun. LaunchedBy is set only when an operator dispatched the
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

        // Mirrors CommandHandler.StartRun. Adding Abandoned tightens replay, which is normally the
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

        // Mirrors CommandHandler.EnsureLauncherAuthorizedCompletion. The launcher's secret is
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

        // Mirrors CommandHandler.CompleteWorkItem, including its separation from the repair
        // refusal below: an abandoned item is not damaged, so "repair it first" would misdescribe
        // it. Safe by construction — no history predating work.abandoned carries this status.
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

        if (state.Runs.Values.Any(run =>
                run.WorkItemId == completed.WorkItemId &&
                run.Status is AgentRunStatus.Active))
        {
            throw new GovernanceException("Work item cannot be completed while a run is still active.");
        }

        if (state.Escalations.Values.Any(escalation =>
                escalation.WorkItemId == completed.WorkItemId && escalation.Status == EscalationStatus.Open))
        {
            throw new GovernanceException("Work item cannot be completed while an escalation on it is open.");
        }

        // Mirrors the two waiver rules in CommandHandler.CompleteWorkItem. Both key on the PRESENCE
        // of WithoutVerificationReason, which is null on every event recorded before the field
        // existed, so neither can bite on a history that was legal when it was written. This is
        // deliberately not "a completion must carry a waiver": it only constrains the waiver when
        // one is there.
        if (completed.WithoutVerificationReason is { } waiver)
        {
            if (string.IsNullOrWhiteSpace(waiver))
            {
                throw new GovernanceException(
                    "Waiving the required runs needs a reason; a blank waiver records nothing.");
            }

            if (!IsOperator(state, @event.ActorId))
            {
                throw new GovernanceException("Only an operator can complete work without the required runs.");
            }
        }

        // Neither required run — the verifier's, nor a working role's — is checked here, and nor is
        // the order they ran in. Every work item already completed in this ledger was completed
        // with no run of any kind, so enforcing any of it at replay would reject a history that was
        // legal when it was written and make those tasks unreadable. The ordering rule is doubly
        // unenforceable here: it compares EndedAt against the latest working run, and an old run
        // carries no SubjectRole, so replay cannot tell which runs were working runs at all. This is the third time the same distinction has had to be drawn, after
        // scope occupancy and run session identity: command-time rules may tighten, replay-time
        // rules may not.
        //
        // Unlike those two, the rule cannot be made safe by construction: it keys on the ABSENCE of
        // a run, and absence is exactly what every old history has. What replay does check is the
        // waiver above — that a reason is there and that an operator recorded it — because that
        // keys on the PRESENCE of a new field. Whether anyone ever worked or verified the item is
        // still never re-derived here: the event records what an operator decided, replay accepts it.
        //
        // The cross-provider rule added beside the ordering check —
        // CommandHandler.ProviderThatVerifiedItsOwnWork, which refuses a completion whose only
        // verifier runs came from the same provider that did the work — is command-time only for
        // the same reason and one of its own. Provider IS recorded on every run ever written, so
        // the field is not the problem; the problem is that no verifier run before this rule was
        // chosen under it. Any archived history that happens to have verified on the working
        // provider was legal when it was written, and replay would now call it forged. It also inherits
        // the ordering gate's blindness: it only counts verifier runs, and an old run carries no
        // SubjectRole, so replay cannot tell which runs were verifications at all.
    }

    private static void ValidateWorkItemBlocked(
        GovernedTaskState state,
        LedgerEvent @event,
        WorkItemBlocked blocked)
    {
        RequireAuthority(state, @event.ActorId, Capability.ManageWork);
        var workItem = Get(state.WorkItems, blocked.WorkItemId, "work item");
        RequireText(blocked.Reason, nameof(blocked.Reason));
        // Mirrors CommandHandler.BlockWorkItem: Blocked is a live status holding the item's area,
        // so a released item must not re-enter it. Abandoned is safe by construction — no history
        // predating work.abandoned carries that status.
        //
        // Stale is deliberately NOT included here, and this is the one place the two copies of the
        // rule differ. BlockWorkItem refused only Completed for the whole of this kernel's life, so
        // an operator blocking a stale item was legal, and some log somewhere records one. Adding
        // Stale here would reject that event and lose the task. Unlike Abandoned, this status is
        // old, so the tightening is not safe by construction and stays command-time only.
        if (workItem.Status is WorkItemStatus.Completed or WorkItemStatus.Abandoned)
        {
            throw new GovernanceException("A completed or abandoned work item cannot be blocked.");
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

    private static void ValidateWorkItemAbandoned(
        GovernedTaskState state,
        LedgerEvent @event,
        WorkItemAbandoned abandoned)
    {
        RequireAuthority(state, @event.ActorId, Capability.ManageWork, operatorRequired: true);
        var workItem = Get(state.WorkItems, abandoned.WorkItemId, "work item");
        RequireText(abandoned.Reason, nameof(abandoned.Reason));
        // Mirrors CommandHandler.AbandonWorkItem, Stale included. Every rule on this event is safe
        // at replay whatever it keys on, because the event type itself is new: no history contains
        // a work.abandoned at all, so none can be rejected by tightening its rules.
        if (workItem.Status is WorkItemStatus.Completed or WorkItemStatus.Stale)
        {
            throw new GovernanceException("A completed or stale work item cannot be abandoned.");
        }

        // Abandoning is terminal in the same way completing is, so a second abandonment is refused
        // rather than tolerated as idempotent.
        if (workItem.Status is WorkItemStatus.Abandoned)
        {
            throw new GovernanceException("Work item is already abandoned.");
        }

        // Two command-time refusals are deliberately not repeated here: while a run on the item is
        // active, and while an escalation on it is open. Both are coordination rules about what may
        // happen next, like scope occupancy, rather than structural facts about the event. Both
        // could be enforced here safely — the event type is new — but this method deliberately
        // validates only the shape of the record, and adding one of the two and not the other would
        // be the arbitrary choice. ValidateWorkItemCompleted does check its equivalents, because
        // those rules predate it and every completion in every log already satisfies them.
    }

    private static void ValidateClaimDependenciesRepointed(
        GovernedTaskState state,
        LedgerEvent @event,
        ClaimDependenciesRepointed repointed)
    {
        RequireAuthority(state, @event.ActorId, Capability.ResolveClaim);
        var superseded = Get(state.Claims, repointed.SupersededClaimId, "claim");
        var replacement = Get(state.Claims, repointed.ReplacementClaimId, "claim");
        if (superseded.Status != ClaimStatus.Superseded)
        {
            throw new GovernanceException("Dependencies are only re-pointed away from a superseded claim.");
        }

        if (superseded.SupersededByClaimId != replacement.Id)
        {
            throw new GovernanceException("Dependencies can only be re-pointed at the claim that superseded them.");
        }

        // Mirrors DeriveSupersession: only an earned refinement re-points instead of invalidating.
        if (replacement.Status != ClaimStatus.Validated ||
            state.Evidence.Values.Any(item => item.Refutes.Contains(superseded.Id)))
        {
            throw new GovernanceException(
                "Dependencies are only re-pointed when the replacement is validated and nothing refutes the original.");
        }
    }

    private static void ValidateDecisionOverturned(
        GovernedTaskState state,
        LedgerEvent @event,
        DecisionOverturned overturned)
    {
        RequireAuthority(state, @event.ActorId, Capability.ResolveDecision);
        var decision = Get(state.Decisions, overturned.DecisionId, "decision");
        if (decision.Status is not (DecisionStatus.Proposed or DecisionStatus.Accepted))
        {
            throw new GovernanceException($"Only a current decision can be overturned; this one is '{decision.Status}'.");
        }

        var challenge = Get(state.Challenges, overturned.ChallengeId, "challenge");
        if (challenge.Status != ChallengeStatus.Supported ||
            !string.Equals(challenge.TargetId.Trim(), decision.Id.Value, StringComparison.Ordinal))
        {
            throw new GovernanceException("A decision is only overturned by a supported challenge against it.");
        }

        // Mirrors the evidence bar in CommandHandler.AddChallengeConsequence.
        if (challenge.EvidenceIds.Count == 0)
        {
            throw new GovernanceException(
                $"Challenge '{challenge.Id}' carries no evidence and cannot overturn a decision.");
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
