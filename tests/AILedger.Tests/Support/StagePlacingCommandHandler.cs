using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Tests.Support;

// Tests of rules other than stage admission intentionally construct only the state those rules
// need. Place stage-entry producers at an admitting stage without adding synthetic stage events or
// bypassing the production handler, then restore the fixture's stage in the returned projection.
// Tests of stage admission opt out and use CommandHandler directly.
internal sealed class StagePlacingCommandHandler : ICommandHandler
{
    private readonly CommandHandler _inner;

    public StagePlacingCommandHandler(ITaskReducer reducer) =>
        _inner = new CommandHandler(reducer, new AuthorizationPolicy());

    public CommandOutcome Handle(GovernedTaskState? state, LedgerCommand command, DateTimeOffset now)
    {
        if (state is null || StageFor(state, command) is not { } stage)
        {
            return _inner.Handle(state, command, now);
        }

        var original = state.Stage;
        var placed = state with { Stage = stage };
        var outcome = _inner.Handle(placed, command, now);
        return outcome with { State = outcome.State with { Stage = original } };
    }

    private static TaskStage? StageFor(GovernedTaskState state, LedgerCommand command) => command switch
    {
        AddWorkItemCommand => TaskStage.Ready,
        MarkLessonBearingCommand => TaskStage.Learn,
        RecordArtifactCommand { Kind: GovernedArtifactKind.UserRequest } => TaskStage.Ready,
        RecordArtifactCommand { Kind: GovernedArtifactKind.PromptContract } => TaskStage.Design,
        RecordArtifactCommand { Kind: GovernedArtifactKind.OrchestrationPlan } => TaskStage.Scope,
        RecordArtifactCommand { Kind: GovernedArtifactKind.VerifierOutput } => TaskStage.Verification,
        RecordArtifactCommand { Kind: GovernedArtifactKind.CodeReviewOutput } => TaskStage.Review,
        StartRunCommand start => RunStage(state, start),
        ConsultLessonsCommand consult => ConsultStage(state, consult.Purpose),
        _ => null
    };

    private static TaskStage? ConsultStage(GovernedTaskState state, LessonConsultationPurpose purpose) =>
        purpose switch
        {
            LessonConsultationPurpose.Recon when state.Stage is TaskStage.Research or TaskStage.Design => null,
            LessonConsultationPurpose.Recon or LessonConsultationPurpose.Research => TaskStage.Research,
            LessonConsultationPurpose.Reconsideration => TaskStage.Design,
            _ => null
        };

    private static TaskStage? RunStage(GovernedTaskState state, StartRunCommand command)
    {
        var subject = command.SubjectActorId ?? command.ActorId;
        if (!state.Roles.TryGetValue(subject, out var assignment))
        {
            return null;
        }

        return assignment.Role switch
        {
            RoleKind.Researcher => TaskStage.Research,
            RoleKind.Worker when state.Stage == TaskStage.Repair => TaskStage.Repair,
            RoleKind.Worker => TaskStage.Execution,
            RoleKind.Verifier => TaskStage.Verification,
            RoleKind.CodeReviewer => TaskStage.Review,
            _ => null
        };
    }
}
