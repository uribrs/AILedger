namespace AILedger.Core.Contracts;

public sealed record RoleAssignment(
    ActorId ActorId,
    RoleKind Role,
    IReadOnlyList<Capability> Capabilities,
    Provenance AssignedBy);
