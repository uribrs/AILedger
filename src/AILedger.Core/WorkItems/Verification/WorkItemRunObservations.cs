namespace AILedger.Core.Contracts;

// Shares the owning qualification predicate without changing historical admission or replay.
public static class WorkItemRunObservations
{
    public static AgentRun? LatestCompletedWork(GovernedTaskState state, WorkItemId work) =>
        AILedger.Core.WorkItems.WorkItemVerificationRules.LatestCompletedWorkingRun(state, work);
}
