using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Domain.ReplayValidationRules;

namespace AILedger.Core.CoordinatorSessions;

// Replay validates only stable event shape. The one-open-session policy and the ownership/open
// checks for linked runs deliberately remain command-time rules because replay may never tighten
// around histories that were legal when recorded.
internal static class CoordinatorSessionEventValidator
{
    internal static void ValidateStarted(
        GovernedTaskState state,
        LedgerEvent @event,
        CoordinatorSession session)
    {
        RequireAuthority(state, @event.ActorId, Capability.ManageRuns);
        EnsureNew(state.CoordinatorSessions, session.Id, "coordinator session");
        RequireId(session.Id.Value, nameof(session.Id));
        RequireId(session.ActorId.Value, nameof(session.ActorId));
        RequireText(session.Harness, nameof(session.Harness));
        if (session.ActorId != @event.ActorId || session.StartedAt != @event.RecordedAt ||
            session.EndedAt is not null)
        {
            throw new GovernanceException(
                "A started coordinator session must belong to the event actor and begin open at the event timestamp.");
        }
    }

    internal static void ValidateCompleted(
        GovernedTaskState state,
        LedgerEvent @event,
        SessionCompleted completed)
    {
        RequireAuthority(state, @event.ActorId, Capability.ManageRuns);
        var session = Get(state.CoordinatorSessions, completed.SessionId, "coordinator session");
        if (session.EndedAt is not null)
        {
            throw new GovernanceException($"Coordinator session '{session.Id}' has already been closed.");
        }

        if (session.ActorId != @event.ActorId && !IsOperator(state, @event.ActorId))
        {
            throw new GovernanceException(
                $"Only session actor '{session.ActorId}' or an operator can close coordinator session '{session.Id}'.");
        }

        if (completed.EndedAt != @event.RecordedAt || completed.EndedAt < session.StartedAt)
        {
            throw new GovernanceException(
                "Coordinator session completion time must match the event timestamp and follow the session start.");
        }
    }

    // A session link is a field that did not exist on older RunStarted events, so existence is safe
    // to validate at replay. Open/closed and actor ownership remain relaxable command-time policy.
    internal static void ValidateDispatchLink(GovernedTaskState state, AgentRun run)
    {
        if (run.CoordinatorSessionId is { } sessionId)
        {
            _ = Get(state.CoordinatorSessions, sessionId, "coordinator session");
        }
    }
}
