using AILedger.Core.Contracts;
using AILedger.Core.CoordinatorSessions;
using AILedger.Core.Domain;
using AILedger.Core.Lessons;

namespace AILedger.Core.Stages;

// Replay counterpart: StageEventValidator.ValidateTransitioned.
internal static class StageTransitionRules
{
    internal static IReadOnlyList<LedgerEventData> Transition(
        GovernedTaskState state,
        RequestStageTransitionCommand command,
        DateTimeOffset now)
    {
        var request = StageTransitionRequestRules.Validate(state, command);
        if (request.Waiver is null)
        {
            StagePrerequisiteRules.EnsureSatisfied(state, command.TargetStage, command.SerialJustification);
        }

        var transition = new StageTransitioned(
            state.Stage,
            command.TargetStage,
            request.Reason,
            command.SerialJustification);
        var provenance = request.Waiver is null
            ? null
            : CoordinatorSessionWaiverAttribution.Resolve(state, command.ActorId);

        if (command.TargetStage != TaskStage.Archive)
        {
            return request.Waiver is null
                ? [transition]
                : [new StagePrerequisitesWaived(command.TargetStage, request.Waiver, provenance), transition];
        }

        return Archive(state, command, now, request.Waiver, provenance, transition);
    }

    private static IReadOnlyList<LedgerEventData> Archive(
        GovernedTaskState state,
        RequestStageTransitionCommand command,
        DateTimeOffset now,
        string? waiver,
        WaiverProvenance? provenance,
        StageTransitioned transition)
    {
        var lessons = LessonMintingRules.MintLessons(state, command.ActorId, now);
        if (lessons.Count == 0)
        {
            throw new GovernanceException(
                "Archiving requires at least one eligible lesson-bearing mark on a validated claim, " +
                "rejected alternative, or resolved escalation.");
        }

        var events = lessons.Select<Lesson, LedgerEventData>(lesson => new LessonMinted(lesson));
        if (waiver is not null)
        {
            events = events.Append(new StagePrerequisitesWaived(command.TargetStage, waiver, provenance));
        }

        return events.Append(transition).ToArray();
    }
}
