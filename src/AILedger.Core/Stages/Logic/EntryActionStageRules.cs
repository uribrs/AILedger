using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Core.Stages;

// Command-time only. These gates read aggregate stage state, so applying them during replay would
// retroactively reject records that were legal under an earlier methodology.
internal static class EntryActionStageRules
{
    internal static void EnsureAllowed(GovernedTaskState state, LedgerCommand command)
    {
        var rule = RuleFor(state, command);
        if (rule is null || rule.Value.Stages.Contains(state.Stage))
        {
            return;
        }

        var admitted = string.Join(", ", rule.Value.Stages.Select(stage => $"'{stage}'"));
        throw new GovernanceException(
            $"Action '{rule.Value.Action}' is not allowed at stage '{state.Stage}'; " +
            $"admitting stage{(rule.Value.Stages.Count == 1 ? " is" : "s are")}: {admitted}.");
    }

    private static EntryRule? RuleFor(GovernedTaskState state, LedgerCommand command) => command switch
    {
        RecordArtifactCommand artifact => ArtifactRule(artifact.Kind),
        AddWorkItemCommand => Rule("work add", TaskStage.Ready),
        StartRunCommand run => RunRule(state, run),
        MarkLessonBearingCommand => Rule("lesson mark", TaskStage.Learn),
        _ => null
    };

    private static EntryRule? ArtifactRule(GovernedArtifactKind kind) => kind switch
    {
        GovernedArtifactKind.UserRequest => Rule(
            "artifact record --kind UserRequest",
            TaskStage.Discovery,
            TaskStage.Research,
            TaskStage.Design,
            TaskStage.Scope,
            TaskStage.Ready),
        GovernedArtifactKind.InternalRecon =>
            Rule("artifact record --kind InternalRecon", TaskStage.Research, TaskStage.Design),
        GovernedArtifactKind.PromptContract =>
            Rule("artifact record --kind PromptContract", TaskStage.Design),
        GovernedArtifactKind.OrchestrationPlan =>
            Rule("artifact record --kind OrchestrationPlan", TaskStage.Design, TaskStage.Scope),
        GovernedArtifactKind.VerifierOutput =>
            Rule("artifact record --kind VerifierOutput", TaskStage.Verification),
        GovernedArtifactKind.CodeReviewOutput =>
            Rule("artifact record --kind CodeReviewOutput", TaskStage.Review),
        // WorkflowRetrospective already has its Archive-only entry condition in ArtifactRules,
        // including the additional no-live-work and no-active-run requirements that belong to it.
        // Leaving it there preserves that single, complete refusal surface.
        GovernedArtifactKind.WorkflowRetrospective => null,
        _ => null
    };

    private static EntryRule? RunRule(GovernedTaskState state, StartRunCommand command)
    {
        var subject = command.SubjectActorId ?? command.ActorId;
        if (!state.Roles.TryGetValue(subject, out var assignment))
        {
            // Preserve RunDispatchRules' more specific unknown-role refusal.
            return null;
        }

        return assignment.Role switch
        {
            RoleKind.Researcher => Rule("run start for Researcher", TaskStage.Research),
            RoleKind.Worker => Rule("run start for Worker", TaskStage.Execution, TaskStage.Repair),
            RoleKind.Verifier => Rule("run start for Verifier", TaskStage.Verification),
            RoleKind.CodeReviewer => Rule("run start for CodeReviewer", TaskStage.Review),
            _ => null
        };
    }

    private static EntryRule Rule(string action, params TaskStage[] stages) => new(action, stages);

    private readonly record struct EntryRule(string Action, IReadOnlyList<TaskStage> Stages);
}
