using AILedger.Core.Contracts;
using AILedger.Core.Domain;

namespace AILedger.Core.Application;

// Command time only, and it has no replay counterpart on purpose.
//
// Every task in this ledger was created before context.built existed. A replay rule requiring one
// would make all of them permanently unreadable, which is the failure this repository has already
// produced twice. CommandHandler may tighten; TaskTransitionValidator may not. If a later change
// adds an arm keyed on context.built to the validator, that is the stop condition, not a repair.
internal static class ContextGateRules
{
    internal static void EnsureBriefed(
        GovernedTaskState state,
        ActorId actorId,
        IReadOnlyList<ContextSkill>? servedNow,
        string action)
    {
        if (!state.ContextBuilds.TryGetValue(actorId, out var build))
        {
            throw new GovernanceException(
                $"Actor '{actorId}' has not built its context on this task and cannot {action}. " +
                $"Run: ailedger context build --task {state.TaskId} --actor {actorId} --cognitive-root <path>");
        }

        // Null means the caller could not read the cognitive layer, so there is nothing to compare
        // against and the presence check above is the whole gate. It is not a pass: a brief that
        // cannot be checked is reported as unchecked rather than treated as current.
        if (servedNow is null || ContextSkills.SameAs(build.Skills, servedNow))
        {
            return;
        }

        throw new GovernanceException(
            $"The skills served to actor '{actorId}' have changed since its context was built, so it " +
            $"cannot {action}. Run: ailedger context build --task {state.TaskId} --actor {actorId} " +
            "--cognitive-root <path>");
    }
}
