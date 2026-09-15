using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.CoordinatorSessions;

// Owns the optional link from a dispatched run back to the session that dispatched it.
internal static class CoordinatorSessionDispatchRules
{
    internal static void EnsureUsable(
        GovernedTaskState state,
        ActorId actorId,
        CoordinatorSessionId? sessionId)
    {
        // Absence is the ordinary manual path and must remain legal for existing history.
        if (sessionId is not { } id)
        {
            return;
        }

        var session = Get(state.CoordinatorSessions, id, "coordinator session");
        if (session.EndedAt is not null)
        {
            throw new GovernanceException(
                $"Coordinator session '{id}' is closed and cannot dispatch further runs.");
        }

        if (session.ActorId != actorId)
        {
            throw new GovernanceException(
                $"Coordinator session '{id}' belongs to '{session.ActorId}', so '{actorId}' cannot dispatch inside it.");
        }
    }
}
