namespace AILedger.Core.Contracts;

public enum RoleKind
{
    Operator,
    PlanningLead,
    ImplementationLead,
    Researcher,
    Worker,
    Verifier,
    CodeReviewer
}

public enum Capability
{
    ManageRoles,
    ManageScope,
    AddClaim,
    ResolveClaim,
    AddEvidence,
    ProposeDecision,
    ResolveDecision,
    RaiseChallenge,
    DisposeChallenge,
    ManageWork,
    ManageRuns,
    RequestTransition,
    BuildContext,
    RaiseEscalation,
    ResolveEscalation,
    RecordAlternative,
    ManageConstraints,
    RecordArtifact
}


public sealed record Provenance(ActorId ActorId, DateTimeOffset RecordedAt, string? Source);

public sealed record RoleAssignment(
    ActorId ActorId,
    RoleKind Role,
    IReadOnlyList<Capability> Capabilities,
    Provenance AssignedBy);

// As on WorkItem above: the door a launch came through is recorded by the waiver event beside this
// run and projected into GovernedTaskState.ContextBriefWaivers, not copied onto the run.

// Who exercised a waiver. The actor remains on the event envelope; this records whether that actor
// was inside a coordinator bracket at the time, which is the distinction retrospective measurement
// needs. Session fields are absent for a manual waiver and for every waiver recorded before this
// field existed.
public enum WaiverOrigin
{
    Manual,
    CoordinatorSession
}

public sealed record WaiverProvenance(
    WaiverOrigin Origin,
    CoordinatorSessionId? CoordinatorSessionId = null,
    string? Harness = null,
    string? HarnessSessionId = null);
