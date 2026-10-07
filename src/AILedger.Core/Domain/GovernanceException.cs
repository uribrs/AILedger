namespace AILedger.Core.Domain;

public enum GovernanceRefusalKind { Opaque, RequiredRunOutput, StaleBasis }

public sealed class GovernanceException : InvalidOperationException
{
    public GovernanceRefusalKind Kind { get; }

    public GovernanceException(string message, GovernanceRefusalKind kind = GovernanceRefusalKind.Opaque)
        : base(message)
    {
        Kind = kind;
    }
}
