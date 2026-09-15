using AILedger.Core.Contracts;

namespace AILedger.Core.Lessons;

// Replay counterpart: LessonEventValidator.ValidateMinted.
internal static class LessonMintingRules
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
                    Citations(state, claim.EvidenceIds), provenance, mark.SupersedesLessonId, mark.Class,
                    mark.Repo, mark.Tags, mark.Verify, mark.DoNot, mark.Actor, mark.Kind, mark.Audience,
                    mark.VerifyExpects),
            // The refuting evidence is what makes this lesson worth carrying: a later task that
            // recalls it gets the belief and what contradicted it, not just the verdict.
            LessonSourceKind.RejectedClaim when
                state.Claims.TryGetValue(new ClaimId(mark.SourceRecordId), out var rejected) &&
                rejected.Status == ClaimStatus.Rejected => new Lesson(
                    LessonIdFor(state.TaskId, mark.SourceKind, mark.SourceRecordId), state.TaskId,
                    mark.SourceKind, mark.SourceRecordId, rejected.Statement, "Rejected",
                    Citations(state, rejected.EvidenceIds), provenance, mark.SupersedesLessonId, mark.Class,
                    mark.Repo, mark.Tags, mark.Verify, mark.DoNot, mark.Actor, mark.Kind, mark.Audience,
                    mark.VerifyExpects),
            LessonSourceKind.RejectedAlternative when
                state.Alternatives.TryGetValue(new AlternativeId(mark.SourceRecordId), out var alternative) =>
                new Lesson(LessonIdFor(state.TaskId, mark.SourceKind, mark.SourceRecordId), state.TaskId,
                    mark.SourceKind, mark.SourceRecordId, alternative.Statement, alternative.RejectionRationale,
                    [], provenance, mark.SupersedesLessonId, mark.Class, mark.Repo, mark.Tags,
                    mark.Verify, mark.DoNot, mark.Actor, mark.Kind, mark.Audience, mark.VerifyExpects),
            LessonSourceKind.ResolvedEscalation when
                state.Escalations.TryGetValue(new EscalationId(mark.SourceRecordId), out var escalation) &&
                escalation.Status == EscalationStatus.Resolved => new Lesson(
                    LessonIdFor(state.TaskId, mark.SourceKind, mark.SourceRecordId), state.TaskId,
                    mark.SourceKind, mark.SourceRecordId, escalation.Question, escalation.Resolution!,
                    Citations(state, escalation.AttemptEvidenceIds), provenance, mark.SupersedesLessonId,
                    mark.Class, mark.Repo, mark.Tags, mark.Verify, mark.DoNot, mark.Actor, mark.Kind,
                    mark.Audience, mark.VerifyExpects),
            _ => null
        };

    private static string[] Citations(GovernedTaskState state, IEnumerable<EvidenceId> evidenceIds) =>
        evidenceIds.Select(id => state.Evidence[id].Citation)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();

    private static LessonId LessonIdFor(TaskId taskId, LessonSourceKind kind, string sourceRecordId) =>
        new($"{taskId.Value}:{kind.ToString().ToLowerInvariant()}:{sourceRecordId}");
}
