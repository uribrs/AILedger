using AILedger.Core.Application;
using AILedger.Core.Contracts;
using AILedger.Core.Domain;
using static AILedger.Core.Application.CommandHandler;

namespace AILedger.Core.CoordinatorSessions;

// Decides which bracket events session commands may emit. Historical-event admissibility stays in
// CoordinatorSessionEventValidator so command-time policy can change without rewriting history.
internal static class CoordinatorSessionLifecycleRules
{
    internal static IReadOnlyList<LedgerEventData> Start(
        GovernedTaskState state,
        StartCoordinatorSessionCommand command,
        DateTimeOffset now)
    {
        RequireId(command.SessionId.Value, nameof(command.SessionId));
        RequireText(command.Harness, nameof(command.Harness));
        EnsureNew(state.CoordinatorSessions, command.SessionId, "coordinator session");

        // Two overlapping brackets on one actor would make a dispatched run belong to both and
        // would calculate idle time against a span that is not the conversation's.
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

    internal static IReadOnlyList<LedgerEventData> Complete(
        GovernedTaskState state,
        CompleteCoordinatorSessionCommand command,
        DateTimeOffset now)
    {
        var session = Get(state.CoordinatorSessions, command.SessionId, "coordinator session");
        if (session.EndedAt is not null)
        {
            throw new GovernanceException($"Coordinator session '{session.Id}' has already been closed.");
        }

        // An operator may close a bracket its actor left open. An unclosed bracket makes wall clock
        // and idle time unmeasurable, so this follows the same ownership shape as run completion.
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
}
