using AILedger.Core.Contracts;

namespace AILedger.Core.Application;

// Which live work items and which runs came through a door on the context gate, and what carried
// them through.
//
// A read over state, like TaskDebt beside it: written by nothing, read by `status`, and it refuses
// and judges nothing. It exists because a record nothing can read is the defect this task was opened
// to fix in another form. The waiver is its own event beside the item it let through, joined to that
// item on causationId by the reducer — but `status` reads state and not the log, so without this the
// question "which work bypassed the gate, who bypassed it, and why" is answerable only by hand
// (GC1, GC2).
//
// This has no replay counterpart and must never gain one. It reads a projection that no history
// written before the doors existed produces, and a rule keyed on its absence would refuse every one
// of them.
public sealed record BriefWaiver(
    // "work item" or "provider launch", carried straight from the projected waiver, so a row here and
    // the record it was read from read as the same fact.
    string Kind,
    string Id,
    // Who opened the door: the actor on the waiver event, which is the actor the gate was checked
    // against.
    string Actor,
    // Exactly one of these is set on any row, because an absent brief and a stale one are different
    // failures and the gate refuses a command naming both.
    string? OperatorReason,
    EvidenceId? StaleBriefEvidence)
{
    public static IReadOnlyList<BriefWaiver> Compute(GovernedTaskState state)
    {
        // Live items only. A completed or abandoned item is history, and the question `status` is
        // being asked is which of the work in front of the operator now was decomposed unbriefed.
        // The same predicate the stale-task sweep and TaskDebt use, so "live" means one thing.
        var items = state.ContextBriefWaivers
            .Where(waiver =>
                waiver.Kind == ContextBriefWaiver.WorkItemKind && IsLive(state, waiver.TargetId))
            .OrderBy(waiver => waiver.TargetId, StringComparer.Ordinal);

        // Every launch, not only the ones whose run is still active. A run is a thing that happened,
        // so filtering it by status would hide the dispatch the operator is asking about the moment
        // the agent exits.
        var runs = state.ContextBriefWaivers
            .Where(waiver => waiver.Kind == ContextBriefWaiver.ProviderLaunchKind)
            .OrderBy(waiver => waiver.TargetId, StringComparer.Ordinal);

        return
        [
            .. items.Concat(runs).Select(waiver => new BriefWaiver(
                waiver.Kind,
                waiver.TargetId,
                waiver.Actor.Value,
                waiver.OperatorReason,
                waiver.StaleBriefEvidenceId))
        ];
    }

    private static bool IsLive(GovernedTaskState state, string workItemId) =>
        state.WorkItems.TryGetValue(new WorkItemId(workItemId), out var item) &&
        item.Status is not (WorkItemStatus.Completed or WorkItemStatus.Abandoned or WorkItemStatus.Stale);
}
