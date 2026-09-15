namespace AILedger.Core.Contracts;

public sealed record Constraint(
    ConstraintId Id,
    string Statement,
    string Source,
    IReadOnlyList<string> Scope,
    ConstraintStatus Status,
    Provenance Provenance);
