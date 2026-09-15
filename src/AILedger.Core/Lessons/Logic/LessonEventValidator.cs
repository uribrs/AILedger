using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Domain.ReplayValidationRules;

namespace AILedger.Core.Lessons;

// Replay validates historical Lesson events separately from command-time policy. These checks may
// accept new event shapes, but must never reject a history that was legal when written.
internal static class LessonEventValidator
{
    internal static void ValidateMinted(
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

        EnsureMintedSourceMatches(state, lesson);
        EnsureMarkMatches(state, lesson);
        EnsureCitationsMatch(state, lesson);
        ValidateProvenance(@event, lesson.Provenance, "stage.archive");
    }

    internal static void ValidateMarked(
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

        EnsureMarkedSourceIsEligible(state, mark);
        EnsureSupersessionAvailable(state, mark);
        ValidateProvenance(@event, mark.Provenance, "lesson.mark");
    }

    internal static void ValidateRecalled(
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
        var expectedId =
            $"{lesson.SourceTaskId.Value}:{lesson.SourceKind.ToString().ToLowerInvariant()}:{lesson.SourceRecordId}";
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

        EnsureRecallProvenance(lesson);
    }

    private static void EnsureMintedSourceMatches(GovernedTaskState state, Lesson lesson)
    {
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
        var expectedId =
            $"{state.TaskId.Value}:{lesson.SourceKind.ToString().ToLowerInvariant()}:{lesson.SourceRecordId}";
        if (!sourceMatches || lesson.Id.Value != expectedId)
        {
            throw new GovernanceException("A minted lesson must faithfully represent a lesson-bearing source record.");
        }
    }

    private static void EnsureMarkMatches(GovernedTaskState state, Lesson lesson)
    {
        // Histories written before selective minting contain LessonMinted with no preceding mark.
        // Once a history contains marks, every minted lesson must faithfully carry its mark.
        if (state.LessonMarks.Count == 0)
        {
            return;
        }

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

    private static void EnsureCitationsMatch(GovernedTaskState state, Lesson lesson)
    {
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
    }

    private static void EnsureMarkedSourceIsEligible(GovernedTaskState state, LessonMark mark)
    {
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
    }

    private static void EnsureSupersessionAvailable(GovernedTaskState state, LessonMark mark)
    {
        if (mark.SupersedesLessonId is not { } superseded)
        {
            return;
        }

        _ = Get(state.Lessons, superseded, "superseded lesson");
        if (state.LessonMarks.Values.Any(existing => existing.SupersedesLessonId == superseded))
        {
            throw new GovernanceException($"Lesson '{superseded}' is already superseded by another mark.");
        }
    }

    private static void EnsureRecallProvenance(Lesson lesson)
    {
        // A recalled lesson was minted by an archive, or carried in from the pre-kernel ledger where
        // the close-out was a verifier's report rather than a stage transition. Both are real
        // provenance and neither can be forged into the other without lying about where the lesson
        // came from. This accepts a source no earlier history could contain and rejects nothing
        // that was ever legal.
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
    // or before verify, do-not and actor were added. What the verify says is command-time policy.
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
}
