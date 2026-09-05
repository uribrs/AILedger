using AILedger.Core.Contracts;

namespace AILedger.Core.Domain;

public static class StageTransitionPolicy
{
    private static readonly IReadOnlyDictionary<TaskStage, IReadOnlySet<TaskStage>> AllowedTransitions =
        new Dictionary<TaskStage, IReadOnlySet<TaskStage>>
        {
            [TaskStage.Discovery] = Set(TaskStage.Research),
            [TaskStage.Research] = Set(TaskStage.Discovery, TaskStage.Design),
            [TaskStage.Design] = Set(TaskStage.Research, TaskStage.Scope),
            [TaskStage.Scope] = Set(TaskStage.Design, TaskStage.Ready),
            [TaskStage.Ready] = Set(TaskStage.Scope, TaskStage.Execution),
            [TaskStage.Execution] = Set(TaskStage.Research, TaskStage.Design, TaskStage.Scope, TaskStage.Verification),
            [TaskStage.Verification] = Set(TaskStage.Execution, TaskStage.Repair, TaskStage.Review),
            [TaskStage.Repair] = Set(TaskStage.Execution, TaskStage.Verification),
            [TaskStage.Review] = Set(TaskStage.Repair, TaskStage.Learn),
            [TaskStage.Learn] = Set(TaskStage.Review, TaskStage.Archive),
            [TaskStage.Archive] = Set()
        };

    public static bool CanTransition(TaskStage current, TaskStage target) =>
        AllowedTransitions[current].Contains(target);

    public static void EnsureAllowed(TaskStage current, TaskStage target)
    {
        if (current == target)
        {
            throw new GovernanceException($"Task is already in stage '{current}'.");
        }

        if (!CanTransition(current, target))
        {
            throw new GovernanceException($"Transition from '{current}' to '{target}' is not legal.");
        }
    }

    private static IReadOnlySet<TaskStage> Set(params TaskStage[] stages) =>
        new HashSet<TaskStage>(stages);
}
