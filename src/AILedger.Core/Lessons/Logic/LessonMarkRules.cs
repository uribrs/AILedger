using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Lessons;

// Replay counterpart: LessonEventValidator.ValidateMarked.
internal static class LessonMarkRules
{
    internal static IReadOnlyList<LedgerEventData> MarkLessonBearing(
        GovernedTaskState state,
        MarkLessonBearingCommand command,
        DateTimeOffset now)
    {
        RequireDefined(command.SourceKind, nameof(command.SourceKind));
        var lessonClass = RequireLessonClass(command.Class);
        RequireId(command.SourceRecordId, nameof(command.SourceRecordId));
        var repo = RequireValue(command.Repo, "A lesson mark must specify its repository.", nameof(command.Repo));
        var tags = NormalizeTags(command.Tags);
        var verify = RequireValue(
            command.Verify,
            "A lesson mark must specify the command that re-establishes it.",
            nameof(command.Verify));
        RequireCheckableVerify(verify);
        var doNot = RequireValue(
            command.DoNot,
            "A lesson mark must specify what it prohibits re-assuming.",
            nameof(command.DoNot));
        var lessonActor = RequireLessonActor(command.Actor);
        var verifyExpects = ValidateVerifyExpectation(verify, command.VerifyExpects);
        var lessonKind = ValidateLessonKind(command.Kind);
        var audience = NormalizeAudience(command.Audience);
        var markId = LessonMarkIdFor(command.SourceKind, command.SourceRecordId);
        EnsureNew(state.LessonMarks, markId, "lesson mark");

        EnsureEligibleSource(state, command.SourceKind, command.SourceRecordId);
        EnsureSupersessionAvailable(state, command.SupersedesLessonId);

        return [new LessonMarked(new LessonMark(
            markId, command.SourceKind, command.SourceRecordId.Trim(), command.SupersedesLessonId,
            new Provenance(command.ActorId, now, "lesson.mark"), lessonClass, repo.Trim(), tags,
            verify.Trim(), doNot.Trim(), lessonActor, lessonKind, audience, verifyExpects))];
    }

    private static LessonClass RequireLessonClass(LessonClass? lessonClass)
    {
        if (lessonClass is not { } value)
        {
            throw new GovernanceException("A lesson mark must specify its lesson class.");
        }

        RequireDefined(value, nameof(MarkLessonBearingCommand.Class));
        return value;
    }

    private static LessonActor RequireLessonActor(LessonActor? lessonActor)
    {
        if (lessonActor is not { } value)
        {
            throw new GovernanceException("A lesson mark must specify which actor established it.");
        }

        RequireDefined(value, nameof(MarkLessonBearingCommand.Actor));
        return value;
    }

    private static string RequireValue(string? value, string error, string parameter)
    {
        if (value is null)
        {
            throw new GovernanceException(error);
        }

        RequireText(value, parameter);
        return value;
    }

    private static string[] NormalizeTags(IReadOnlyList<string>? source)
    {
        var tags = source?.Select(tag => tag.Trim()).ToArray() ?? [];
        EnsureUnique(tags, "Lesson tags", StringComparer.Ordinal);
        if (tags.Any(string.IsNullOrWhiteSpace))
        {
            throw new GovernanceException("A lesson mark cannot carry an empty tag.");
        }

        return tags;
    }

    private static VerifyExpectation? ValidateVerifyExpectation(
        string verify,
        VerifyExpectation? verifyExpects)
    {
        var runnable = !LessonVerification.ClaimsNothingToRun(verify);
        if (runnable && verifyExpects is null)
        {
            throw new GovernanceException(
                "A lesson mark must specify which way its verify has to come out: present or absent.");
        }

        if (!runnable && verifyExpects is not null)
        {
            throw new GovernanceException(
                "A lesson mark whose verify records that nothing can be run cannot also record a " +
                "direction for it to come out.");
        }

        if (verifyExpects is { } presentExpectation)
        {
            RequireDefined(presentExpectation, nameof(MarkLessonBearingCommand.VerifyExpects));
        }

        return verifyExpects;
    }

    private static LessonKind? ValidateLessonKind(LessonKind? lessonKind)
    {
        if (lessonKind is { } presentKind)
        {
            RequireDefined(presentKind, nameof(MarkLessonBearingCommand.Kind));
        }

        return lessonKind;
    }

    // Empty and absent both mean every role, so the record keeps one representation of that rather
    // than two: an empty list would make a reader ask which of the two it is.
    private static IReadOnlyList<RoleKind>? NormalizeAudience(IReadOnlyList<RoleKind>? audience)
    {
        if (audience is null || audience.Count == 0)
        {
            return null;
        }

        EnsureUnique(audience, "Lesson audience");
        foreach (var role in audience)
        {
            RequireDefined(role, nameof(LessonMark.Audience));
        }

        return audience.ToArray();
    }

    private static void EnsureEligibleSource(
        GovernedTaskState state,
        LessonSourceKind sourceKind,
        string sourceRecordId)
    {
        var eligible = sourceKind switch
        {
            LessonSourceKind.ValidatedClaim =>
                state.Claims.TryGetValue(new ClaimId(sourceRecordId), out var claim) &&
                claim.Status == ClaimStatus.Validated,
            LessonSourceKind.RejectedClaim =>
                state.Claims.TryGetValue(new ClaimId(sourceRecordId), out var rejected) &&
                rejected.Status == ClaimStatus.Rejected,
            LessonSourceKind.RejectedAlternative =>
                state.Alternatives.ContainsKey(new AlternativeId(sourceRecordId)),
            LessonSourceKind.ResolvedEscalation =>
                state.Escalations.TryGetValue(new EscalationId(sourceRecordId), out var escalation) &&
                escalation.Status == EscalationStatus.Resolved,
            _ => false
        };

        if (!eligible)
        {
            throw new GovernanceException("A lesson mark must name an eligible source record.");
        }
    }

    private static void EnsureSupersessionAvailable(GovernedTaskState state, LessonId? superseded)
    {
        if (superseded is not { } lessonId)
        {
            return;
        }

        _ = Get(state.Lessons, lessonId, "superseded lesson");
        if (state.LessonMarks.Values.Any(mark => mark.SupersedesLessonId == lessonId))
        {
            throw new GovernanceException($"Lesson '{lessonId}' is already superseded by another mark.");
        }
    }

    // A fabricated verify command reads as evidence to every future recall, so the only way to
    // say there is nothing to run is the admission itself. A bare "none" is refused. This is a
    // methodology rule the operator is still changing, so it is command-time only.
    private static void RequireCheckableVerify(string verify)
    {
        var trimmed = verify.Trim();
        var admission = trimmed.Length > 4 ? trimmed[4..].TrimStart() : string.Empty;
        var hasReason = admission.Length > 1 &&
            admission[0] is '-' or '—' or '–' or ':' &&
            !string.IsNullOrWhiteSpace(admission[1..]);
        if (!LessonVerification.ClaimsNothingToRun(trimmed) || hasReason)
        {
            return;
        }

        throw new GovernanceException(
            "A lesson mark's verify must be a runnable command, or 'none' followed by " +
            "a dash or colon and a reason.");
    }

    private static LessonMarkId LessonMarkIdFor(LessonSourceKind kind, string sourceRecordId) =>
        new($"{kind.ToString().ToLowerInvariant()}:{sourceRecordId.Trim()}");
}
