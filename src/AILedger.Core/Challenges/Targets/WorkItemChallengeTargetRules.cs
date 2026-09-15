using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Challenges;

internal sealed class WorkItemChallengeTargetRules : IChallengeTargetRules
{
    public bool Matches(string normalizedTargetType) =>
        normalizedTargetType is "work" or "workitem" or "work-item";

    public bool Exists(GovernedTaskState state, string targetId) =>
        state.WorkItems.Keys.Any(id => id.Value == targetId);

    public void AddSupportedConsequence(
        GovernedTaskState state,
        ActorId actorId,
        Challenge challenge,
        ICollection<LedgerEventData> events)
    {
        var workItem = Get(state.WorkItems, new WorkItemId(challenge.TargetId.Trim()), "work item");
        if (workItem.Status is WorkItemStatus.Blocked or WorkItemStatus.Stale or
            WorkItemStatus.Completed or WorkItemStatus.Abandoned)
        {
            throw new GovernanceException(
                $"Work item '{workItem.Id}' is already '{workItem.Status}'; supporting this challenge would change nothing.");
        }

        ChallengeTargetRules.RequireCapability(state, actorId, Capability.ManageWork);
        events.Add(new WorkItemBlocked(
            workItem.Id,
            $"Challenge '{challenge.Id}' was supported.",
            null));
    }
}
