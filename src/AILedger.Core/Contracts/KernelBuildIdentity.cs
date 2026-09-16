namespace AILedger.Core.Contracts;

/// <summary>
/// Identifies the running kernel executable that accepted or refused a command.
/// </summary>
/// <remarks>
/// This is executable provenance, not repository state. Persisted envelopes keep it optional so
/// histories written before kernel identity was recorded remain readable.
/// </remarks>
public sealed record KernelBuildIdentity(
    string Version,
    string SourceCommit,
    DateTimeOffset BuildTime);
