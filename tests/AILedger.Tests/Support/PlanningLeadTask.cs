using AILedger.Core.Contracts;

namespace AILedger.Tests.Support;

// Shared setup for tests that need the ordinary planning-lead capabilities. Concept-specific
// command helpers stay with their concepts rather than owning this neutral fixture.
internal static class PlanningLeadTask
{
    public static TestTask Create(out ActorId lead)
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
}
