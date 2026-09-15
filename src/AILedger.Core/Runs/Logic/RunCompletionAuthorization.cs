using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Runs;

// Owns who may close a run and how launcher authority is proved.
internal static class RunCompletionAuthorization
{
    internal static bool AuthorizeLauncher(
        GovernedTaskState state,
        CompleteRunCommand command,
        AgentRun run)
    {
        if (run.LaunchTokenHash is not { } expectedHash)
        {
            return false;
        }

        if (TrimOrNull(command.LaunchToken) is { } token &&
            string.Equals(HashLaunchToken(token), expectedHash, StringComparison.Ordinal))
        {
            return true;
        }

        // Escape hatch for an orphan: the launching process died and its secret went with it. An
        // operator may close the run, but only as a failure — a run nobody watched finish must
        // never be laundered into a success.
        if (RoleAssignmentRules.IsOperator(state, command.ActorId) &&
            command.Status is AgentRunStatus.Failed or AgentRunStatus.Cancelled)
        {
            return false;
        }

        throw new GovernanceException(
            $"Run '{run.Id}' was started by a provider launch and is closed by that launcher. " +
            "An agent running inside it cannot complete it. An operator may close an orphaned run " +
            "as failed or cancelled.");
    }

    internal static string HashLaunchToken(string token) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(token)));

    internal static void EnsureActorMayComplete(
        GovernedTaskState state,
        ActorId actorId,
        AgentRun run)
    {
        if (run.ActorId == actorId || RoleAssignmentRules.IsOperator(state, actorId))
        {
            return;
        }

        throw new GovernanceException($"Only run actor '{run.ActorId}' or an operator can complete run '{run.Id}'.");
    }
}
