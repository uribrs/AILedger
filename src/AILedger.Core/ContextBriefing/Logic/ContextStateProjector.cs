using AILedger.Core.Contracts;

namespace AILedger.Core.ContextBriefing;

// Projects context events and the one-event waiver join into replayed task state.
internal static class ContextStateProjector
{
    internal static GovernedTaskState RecordBuild(
        GovernedTaskState state,
        LedgerEvent @event,
        ContextBuilt built) =>
        state with
        {
            // Keyed by actor: the latest brief is the gate's current answer; earlier briefs remain
            // in the event log. WorkItemId stays on the event because projecting one invocation's
            // item would make every later build appear to belong to it.
            ContextBuilds = Set(
                state.ContextBuilds,
                @event.ActorId,
                new ContextBuild(@event.ActorId, built.Role, built.Skills, @event.RecordedAt))
        };

    internal static GovernedTaskState RecordPendingWaiver(
        GovernedTaskState state,
        LedgerEvent @event,
        ContextBriefWaived waived) =>
        state with
        {
            // Kept for one event. The following work.added or run.started event owns the target and
            // joins this justification by causation ID.
            PendingContextBriefWaiver = new PendingBriefWaiver(
                @event.EventId,
                @event.ActorId,
                waived.OperatorReason,
                waived.StaleBriefEvidenceId,
                waived.Provenance)
        };

    internal static IReadOnlyList<ContextBriefWaiver> JoinWaiver(
        GovernedTaskState state,
        LedgerEvent @event,
        string kind,
        string targetId) =>
        state.PendingContextBriefWaiver is { } waiver && @event.CausationId == waiver.EventId
            ?
            [
                .. state.ContextBriefWaivers,
                new ContextBriefWaiver(
                    kind,
                    targetId,
                    waiver.ActorId,
                    waiver.OperatorReason,
                    waiver.StaleBriefEvidenceId,
                    waiver.Provenance)
            ]
            : state.ContextBriefWaivers;

    internal static PendingBriefWaiver? PendingWaiverAfter(
        LedgerEventData eventData,
        GovernedTaskState projected) =>
        eventData is ContextBriefWaived ? projected.PendingContextBriefWaiver : null;

    private static IReadOnlyDictionary<TKey, TValue> Set<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> source,
        TKey key,
        TValue value)
        where TKey : notnull
    {
        var copy = new Dictionary<TKey, TValue>(source)
        {
            [key] = value
        };
        return copy;
    }
}
