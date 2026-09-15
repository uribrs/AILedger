using AILedger.Core.Contracts;

namespace AILedger.Core.Domain;

// Public namespace is preserved for source compatibility; physical ownership belongs to Stages.
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

    /// <summary>
    /// The stages that may legally follow <paramref name="current"/>, ordered by the pipeline
    /// order the enum declares so that two callers reading the same state read the same list.
    /// Empty at <see cref="TaskStage.Archive"/>, which is terminal.
    /// </summary>
    public static IReadOnlyList<TaskStage> LegalTargets(TaskStage current) =>
        AllowedTransitions[current].OrderBy(stage => stage).ToArray();

    /// <summary>
    /// Whether <paramref name="target"/> sits earlier in the pipeline than <paramref name="current"/>.
    /// </summary>
    /// <remarks>
    /// The enum's declaration order is the pipeline order, and this file already relies on that:
    /// <see cref="LegalTargets"/> orders by the enum so that two callers reading the same state read
    /// the same list. An ordinal rule is total over every pair, needs no second taxonomy beside the
    /// graph, and stays correct when a stage is added in its pipeline position.
    /// <para>
    /// <see cref="TaskStage.Repair"/> is declared after <see cref="TaskStage.Verification"/>, so
    /// Verification to Repair is forward and Repair to Verification is backward. That is intended:
    /// entering repair is the loop working as designed, and closing a repair cycle is worth one
    /// sentence saying what was repaired.
    /// </para>
    /// </remarks>
    public static bool IsBackward(TaskStage current, TaskStage target) => (int)target < (int)current;

    public static void EnsureAllowed(TaskStage current, TaskStage target)
    {
        if (current == target)
        {
            throw new GovernanceException($"Task is already in stage '{current}'.");
        }

        if (!CanTransition(current, target))
        {
            // The legal set is named because withholding it makes enumeration the cheapest way to
            // find it, and that is what callers did: 84 refused transitions in 12 bursts, one of
            // them 19 commands walking the enum from 'Discovery' in order. The graph is unchanged
            // and nothing new is permitted — the refusal now discloses the same table it is
            // refusing against.
            var legal = LegalTargets(current);
            var options = legal.Count == 0
                ? "'Archive' is terminal; no transition is legal from it"
                : $"legal from '{current}': {string.Join(", ", legal.Select(stage => $"'{stage}'"))}";
            throw new GovernanceException($"Transition from '{current}' to '{target}' is not legal. {options}.");
        }
    }

    private static IReadOnlySet<TaskStage> Set(params TaskStage[] stages) =>
        new HashSet<TaskStage>(stages);
}
