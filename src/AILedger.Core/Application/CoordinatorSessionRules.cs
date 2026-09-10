using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.Application;

// Replay counterparts: ValidateSessionStarted and ValidateSessionCompleted.
//
// A coordinating session is a bracket and nothing more. It records who was coordinating, in which
// harness, from when to when — and the runs it dispatched point back at it. Everything measured over
// it is derived from the log afterwards, so there is nothing here for a coordinator to report about
// itself (D1, D2).
//
// Every rule below is command-time only. Sessions are optional for the whole existing history, no
// command may be refused because a session is absent, and none of these arms is repeated at replay
// beyond the shape checks that are safe by construction.
internal static class CoordinatorSessionRules
{
    internal static IReadOnlyList<LedgerEventData> StartSession(
        GovernedTaskState state,
        StartCoordinatorSessionCommand command,
        DateTimeOffset now)
    {
        RequireId(command.SessionId.Value, nameof(command.SessionId));
        RequireText(command.Harness, nameof(command.Harness));
        EnsureNew(state.CoordinatorSessions, command.SessionId, "coordinator session");

        // One open session per actor. Two overlapping brackets on one actor would put the same
        // dispatched run inside both, and the idle time inside a session — the measure that matters
        // most here — would then be computed against a span that is not the conversation's.
        var open = state.CoordinatorSessions.Values.FirstOrDefault(session =>
            session.ActorId == command.ActorId && session.EndedAt is null);
        if (open is not null)
        {
            throw new GovernanceException(
                $"Actor '{command.ActorId}' already has an open coordinator session '{open.Id}'. " +
                "Close it before opening another.");
        }

        return
        [
            new SessionStarted(new CoordinatorSession(
                command.SessionId,
                command.ActorId,
                command.Harness.Trim(),
                TrimOrNull(command.HarnessSessionId),
                now,
                null))
        ];
    }

    internal static IReadOnlyList<LedgerEventData> CompleteSession(
        GovernedTaskState state,
        CompleteCoordinatorSessionCommand command,
        DateTimeOffset now)
    {
        var session = Get(state.CoordinatorSessions, command.SessionId, "coordinator session");
        if (session.EndedAt is not null)
        {
            throw new GovernanceException($"Coordinator session '{session.Id}' has already been closed.");
        }

        // The session's own actor, or an operator closing one that was left open. The same shape run
        // completion uses, and for the same reason: a bracket nobody closed is worse than one closed
        // late, because an open bracket makes wall clock and idle time unmeasurable.
        if (session.ActorId != command.ActorId && !RoleAssignmentRules.IsOperator(state, command.ActorId))
        {
            throw new GovernanceException(
                $"Only session actor '{session.ActorId}' or an operator can close coordinator session '{session.Id}'.");
        }

        if (now < session.StartedAt)
        {
            throw new GovernanceException("A coordinator session cannot end before it started.");
        }

        return [new SessionCompleted(session.Id, now)];
    }

    // The link a dispatched run carries back to the session that dispatched it. Checked only when the
    // run names one: a run that names none is every run ever recorded here, and refusing it would be
    // the rule this kernel must never write.
    internal static void EnsureDispatchingSessionIsUsable(
        GovernedTaskState state,
        ActorId actorId,
        CoordinatorSessionId? sessionId)
    {
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

        // The dispatching actor owns the bracket it dispatches inside. Letting one actor's run claim
        // another's session would attribute the dispatch — and the idle time around it — to a
        // conversation that never made it.
        if (session.ActorId != actorId)
        {
            throw new GovernanceException(
                $"Coordinator session '{id}' belongs to '{session.ActorId}', so '{actorId}' cannot dispatch inside it.");
        }
    }
}
