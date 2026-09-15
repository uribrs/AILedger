using AILedger.Core.Contracts;

namespace AILedger.Tests.Support;

// Shared Escalation command construction belongs to test infrastructure rather than to a behavior
// test class. Generic planning-lead setup lives separately in PlanningLeadTask.
internal static class EscalationCommands
{
    public static void Raise(
        TestTask task,
        ActorId actor,
        string id,
        EscalationKind kind,
        string question = "Which way?",
        WorkItemId? workItemId = null,
        IReadOnlyList<string>? options = null,
        string? recommendation = null,
        IReadOnlyList<EvidenceId>? evidence = null) =>
        task.Apply(new RaiseEscalationCommand(
            actor,
            null,
            task.NextCorrelation(),
            new EscalationId(id),
            kind,
            question,
            workItemId,
            options ?? [],
            recommendation,
            evidence ?? []));
}
