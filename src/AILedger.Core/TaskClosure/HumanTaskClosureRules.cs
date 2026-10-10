using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Core.TaskClosure;

internal static class HumanTaskClosureRules
{
    internal static IReadOnlyList<LedgerEventData> Close(GovernedTaskState state,
        CloseTaskByHumanCommand command, DateTimeOffset now)
    {
        var admission = command.Admission;
        if (admission is null || admission.TaskId != state.TaskId || admission.ActorId != command.ActorId ||
            admission.Version > state.Version || admission.Reason != command.Reason ||
            !state.Roles.TryGetValue(command.ActorId, out var assignment) || assignment.AssignedBy != admission.OperatorAssignment)
            throw new GovernanceException("Task closure requires fresh, task-bound human confirmation through the local authentication host.");
        var closure = new HumanTaskClosure(command.ActorId, command.Reason, state.Stage, state.Version, admission.Version,
            admission.ConfirmedAt, admission.AuthenticationMethod, admission.HumanIdentity,
            InterruptedRuns(state), ExcludedWorkItems(state));
        Validate(state, command.ActorId, closure, now);
        return [new TaskClosedByHuman(closure)];
    }

    // Only the new event uses these invariants; no historical stage transition is reinterpreted.
    internal static void Validate(GovernedTaskState state, ActorId actor, HumanTaskClosure closure,
        DateTimeOffset recordedAt)
    {
        if (!state.Roles.TryGetValue(actor, out var role) || role.Role != RoleKind.Operator || closure.ActorId != actor)
            throw new GovernanceException("Only a confirmed human operator can close a task by override.");
        if (state.HumanClosure is not null || state.Stage == TaskStage.Archive)
            throw new GovernanceException("Task is already closed.");
        if (string.IsNullOrWhiteSpace(closure.Reason) || closure.Reason.Length > 2000 ||
            closure.Reason.Any(char.IsControl) || closure.Reason != closure.Reason.Trim())
            throw new GovernanceException("Human closure requires a reason of 1–2000 characters without control characters.");
        if (closure.BasisVersion != state.Version || closure.PreviousStage != state.Stage ||
            closure.ReviewedVersion < 1 || closure.ReviewedVersion > closure.BasisVersion ||
            closure.ConfirmedAt == default || closure.ConfirmedAt > recordedAt ||
            recordedAt - closure.ConfirmedAt > TimeSpan.FromMinutes(2) ||
            closure.AuthenticationMethod != "macos-local-authentication" || string.IsNullOrWhiteSpace(closure.HumanIdentity))
            throw new GovernanceException("Invalid or expired human closure confirmation.");
        if (!InterruptedRuns(state).SequenceEqual(closure.InterruptedRuns) ||
            !ExcludedWorkItems(state).SequenceEqual(closure.ExcludedWorkItems))
            throw new GovernanceException("Human closure must account for every active run and unfinished work item.");
    }

    internal static GovernedTaskState Apply(GovernedTaskState state, HumanTaskClosure closure, DateTimeOffset at) =>
        state with
        {
            Stage = TaskStage.Archive,
            HumanClosure = closure,
            Runs = state.Runs.ToDictionary(pair => pair.Key, pair =>
                pair.Value.Status == AgentRunStatus.Active
                    ? pair.Value with { Status = AgentRunStatus.Cancelled, EndedAt = at }
                    : pair.Value),
            WorkItems = state.WorkItems.ToDictionary(pair => pair.Key, pair =>
                pair.Value.Status is not (WorkItemStatus.Completed or WorkItemStatus.Abandoned)
                    ? pair.Value with { Status = WorkItemStatus.Abandoned, AbandonReason = "Human task closure: " + closure.Reason }
                    : pair.Value),
            CoordinatorSessions = state.CoordinatorSessions.ToDictionary(pair => pair.Key, pair =>
                pair.Value.EndedAt is null ? pair.Value with { EndedAt = at } : pair.Value)
        };

    private static RunId[] InterruptedRuns(GovernedTaskState state) => state.Runs.Values
        .Where(run => run.Status == AgentRunStatus.Active).Select(run => run.Id)
        .OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();

    private static WorkItemId[] ExcludedWorkItems(GovernedTaskState state) => state.WorkItems.Values
        .Where(work => work.Status is not (WorkItemStatus.Completed or WorkItemStatus.Abandoned)).Select(work => work.Id)
        .OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
}
