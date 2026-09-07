using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Application;

// Replay counterparts: ValidateLessonMarked, ValidateLessonMinted, ValidateLessonRecalled.
internal static class LessonRules
{
    internal static IReadOnlyList<Lesson> MintLessons(
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
                    provenance, mark.SupersedesLessonId, mark.Class, mark.Repo, mark.Tags,
                    mark.Verify, mark.DoNot, mark.Actor),
            // The refuting evidence is what makes this lesson worth carrying: a later task that
            // recalls it gets the belief and what contradicted it, not just the verdict.
            LessonSourceKind.RejectedClaim when
                state.Claims.TryGetValue(new ClaimId(mark.SourceRecordId), out var rejected) &&
                rejected.Status == ClaimStatus.Rejected => new Lesson(
                    LessonIdFor(state.TaskId, mark.SourceKind, mark.SourceRecordId), state.TaskId,
                    mark.SourceKind, mark.SourceRecordId, rejected.Statement, "Rejected",
                    rejected.EvidenceIds.Select(id => state.Evidence[id].Citation)
                        .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                    provenance, mark.SupersedesLessonId, mark.Class, mark.Repo, mark.Tags,
                    mark.Verify, mark.DoNot, mark.Actor),
            LessonSourceKind.RejectedAlternative when
                state.Alternatives.TryGetValue(new AlternativeId(mark.SourceRecordId), out var alternative) =>
                new Lesson(LessonIdFor(state.TaskId, mark.SourceKind, mark.SourceRecordId), state.TaskId,
                    mark.SourceKind, mark.SourceRecordId, alternative.Statement, alternative.RejectionRationale,
                    [], provenance, mark.SupersedesLessonId, mark.Class, mark.Repo, mark.Tags,
                    mark.Verify, mark.DoNot, mark.Actor),
            LessonSourceKind.ResolvedEscalation when
                state.Escalations.TryGetValue(new EscalationId(mark.SourceRecordId), out var escalation) &&
                escalation.Status == EscalationStatus.Resolved => new Lesson(
                    LessonIdFor(state.TaskId, mark.SourceKind, mark.SourceRecordId), state.TaskId,
                    mark.SourceKind, mark.SourceRecordId, escalation.Question, escalation.Resolution!,
                    escalation.AttemptEvidenceIds.Select(id => state.Evidence[id].Citation)
                        .Distinct(StringComparer.Ordinal).OrderBy(value => value, StringComparer.Ordinal).ToArray(),
                    provenance, mark.SupersedesLessonId, mark.Class, mark.Repo, mark.Tags,
                    mark.Verify, mark.DoNot, mark.Actor),
            _ => null
        };

    internal static IReadOnlyList<LedgerEventData> MarkLessonBearing(
        GovernedTaskState state,
        MarkLessonBearingCommand command,
        DateTimeOffset now)
    {
        RequireDefined(command.SourceKind, nameof(command.SourceKind));
        if (command.Class is not { } lessonClass)
        {
            throw new GovernanceException("A lesson mark must specify its lesson class.");
        }
        RequireDefined(lessonClass, nameof(command.Class));
        RequireId(command.SourceRecordId, nameof(command.SourceRecordId));
        if (command.Repo is not { } repo)
        {
            throw new GovernanceException("A lesson mark must specify its repository.");
        }
        RequireText(repo, nameof(command.Repo));
        var tags = command.Tags?.Select(tag => tag.Trim()).ToArray() ?? [];
        EnsureUnique(tags, "Lesson tags", StringComparer.Ordinal);
        if (tags.Any(string.IsNullOrWhiteSpace))
        {
            throw new GovernanceException("A lesson mark cannot carry an empty tag.");
        }
        if (command.Verify is not { } verify)
        {
            throw new GovernanceException(
                "A lesson mark must specify the command that re-establishes it.");
        }
        RequireText(verify, nameof(command.Verify));
        RequireCheckableVerify(verify);
        if (command.DoNot is not { } doNot)
        {
            throw new GovernanceException("A lesson mark must specify what it prohibits re-assuming.");
        }
        RequireText(doNot, nameof(command.DoNot));
        if (command.Actor is not { } lessonActor)
        {
            throw new GovernanceException("A lesson mark must specify which actor established it.");
        }
        RequireDefined(lessonActor, nameof(command.Actor));
        var markId = LessonMarkIdFor(command.SourceKind, command.SourceRecordId);
        EnsureNew(state.LessonMarks, markId, "lesson mark");
    
        var eligible = command.SourceKind switch
        {
            LessonSourceKind.ValidatedClaim =>
                state.Claims.TryGetValue(new ClaimId(command.SourceRecordId), out var claim) &&
                claim.Status == ClaimStatus.Validated,
            LessonSourceKind.RejectedClaim =>
                state.Claims.TryGetValue(new ClaimId(command.SourceRecordId), out var rejected) &&
                rejected.Status == ClaimStatus.Rejected,
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
            new Provenance(command.ActorId, now, "lesson.mark"), lessonClass, repo.Trim(), tags,
            verify.Trim(), doNot.Trim(), lessonActor))];
    }
    
    // A fabricated verify command reads as evidence to every future recall, so the only way to
    // say there is nothing to run is the admission itself. A bare "none" is refused. This is a
    // methodology rule the operator is still changing, so it is command-time only: the replay
    // copy validates that the field is well formed and never what it says.

    private static void RequireCheckableVerify(string verify)
    {
        var trimmed = verify.Trim();
        var claimsNothingToRun =
            trimmed.StartsWith("none", StringComparison.OrdinalIgnoreCase) &&
            (trimmed.Length == 4 || !(char.IsLetterOrDigit(trimmed[4]) || trimmed[4] == '_'));
        var admission = trimmed.Length > 4 ? trimmed[4..].TrimStart() : string.Empty;
        var hasReason = admission.Length > 1 &&
            admission[0] is '-' or '—' or '–' or ':' &&
            !string.IsNullOrWhiteSpace(admission[1..]);
        if (!claimsNothingToRun || hasReason)
        {
            return;
        }
    
        throw new GovernanceException(
            "A lesson mark's verify must be a runnable command, or 'none' followed by " +
            "a dash or colon and a reason.");
    }

    private static LessonMarkId LessonMarkIdFor(LessonSourceKind kind, string sourceRecordId) =>
        new($"{kind.ToString().ToLowerInvariant()}:{sourceRecordId.Trim()}");

    private static LessonId LessonIdFor(TaskId taskId, LessonSourceKind kind, string sourceRecordId) =>
        new($"{taskId.Value}:{kind.ToString().ToLowerInvariant()}:{sourceRecordId}");
}
