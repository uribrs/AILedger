using AILedger.Core.Contracts;

namespace AILedger.Tests.Support;

// Shared command setup belongs to test infrastructure rather than to a test class. Escalations,
// alternatives, constraints, and work-item lifecycle tests all use this same governed shape.
internal static class EscalationCommands
{
    public static TestTask PreparePlanningTask(out ActorId lead)
    {
        var task = new TestTask();
        lead = new ActorId("planning-lead");
        task.Assign(
            lead,
            RoleKind.PlanningLead,
            Capability.AddClaim,
            Capability.AddEvidence,
            Capability.RaiseEscalation,
            Capability.RecordAlternative,
            Capability.BuildContext);
        return task;
    }

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
