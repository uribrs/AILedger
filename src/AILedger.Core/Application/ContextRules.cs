using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Application;

// Replay counterpart: TaskTransitionValidator.ValidateContextBuilt.
internal static class ContextRules
{
    internal static IReadOnlyList<LedgerEventData> RecordContextBuilt(
        GovernedTaskState state,
        RecordContextBuiltCommand command)
    {
        var assignment = Get(state.Roles, command.ActorId, "actor role");
        if (command.WorkItemId is { } workItemId)
        {
            _ = Get(state.WorkItems, workItemId, "work item");
        }

        // Nothing else here can refuse. A brief that fails is a brief that blocks the work it was
        // for, and the two refusals above are the two the constraint allows: an unknown task never
        // reaches this method, and an unknown actor and an unknown work item are the same class of
        // mistake. In particular a brief carrying no skills is recorded as it stands — an empty
        // cognitive layer is a misconfiguration to see in the record, not a command to refuse.
        foreach (var skill in command.Skills)
        {
            RequireText(skill.SkillId, "Skill ID");
            RequireText(skill.ContentHash, "Skill content hash");
        }

        EnsureUnique(
            command.Skills.Select(skill => skill.SkillId).ToArray(),
            "Served skill IDs",
            StringComparer.Ordinal);

        // A repeat brief appends nothing, and the decision is taken here rather than in the caller.
        // The caller reads the state, decides, and submits, so two agents briefing at the same
        // instant both find nothing recorded and both append (VC2). This runs inside the durable
        // mutation lock against the state the append would be made on, so the second of two
        // concurrent identical briefs cannot slip past it.
        //
        // It leaves as a ContextAlreadyBriefedException and not a GovernanceException: nothing is
        // wrong with the command, no repair is owed, and the caller's read still succeeds. The two
        // refusals above are the only two this command has.
        if (ContextSkills.AlreadyRecorded(state, command.ActorId, command.Skills))
        {
            throw new ContextAlreadyBriefedException(
                $"Actor '{command.ActorId}' already holds this brief on task '{state.TaskId}'.");
        }

        return [new ContextBuilt(assignment.Role, command.WorkItemId, command.Skills.ToArray())];
    }
}
