using AILedger.Core.Contracts;

namespace AILedger.Core.CoordinatorSessions;

// Describes where a waiver was issued. It does not decide whether the waiver is accepted.
internal static class CoordinatorSessionWaiverAttribution
{
    internal static WaiverProvenance Resolve(GovernedTaskState state, ActorId actorId)
    {
        var session = state.CoordinatorSessions.Values.FirstOrDefault(candidate =>
            candidate.ActorId == actorId && candidate.EndedAt is null);

        return session is null
            ? new WaiverProvenance(WaiverOrigin.Manual)
            : new WaiverProvenance(
                WaiverOrigin.CoordinatorSession,
                session.Id,
                session.Harness,
                session.HarnessSessionId);
    }
}
